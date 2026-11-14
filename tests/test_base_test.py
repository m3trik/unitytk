# !/usr/bin/python
# coding=utf-8
"""Tests for the shared harness in :mod:`base_test` (no Unity needed).

The contracts live here, all of them things whose absence let a run look
green without having verified anything:

* :class:`base_test.EnvSkip` -- a skip taken because the *machine* is missing
  something is marked, so the runner can count it separately from a deliberate
  "not applicable" skip and refuse to call the run a plain pass.
* :class:`base_test.UnityEnv` -- the two Unity failures that surface *mid*-test
  (no batch project, no batch license) say their reason once, marked. Fifteen
  hand-copied reasons across eight suites were unmarked, so a Unity machine
  without a batch license still stamped the badge and passed ``--strict-env``.
* :class:`base_test.PackageSources` -- the package's files resolve by bundle
  path in one place, so a moved file fails loudly.

:class:`PublicHygieneTest` is the permanent guard for the 2026-08-17 rename:
client program identifiers must not come back into tracked Python/C# source.
"""
import ast
import os
import re
import sys
import unittest
from unittest import mock

import base_test
from base_test import (
    EnvSkip,
    SuiteOptions,
    PackageSources,
    TempProjects,
    TestAssets,
    UnityEnv,
)
from unitytk.unity_project import UnityProject

#: Repo root (``unitytk/``), for the source scans below.
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

#: Directories whose contents are generated or historical, not tracked source.
_SKIP_DIRS = {"temp_tests", "build", "dist", ".git", "__pycache__", ".eggs"}

#: This module states every banned pattern in order to search for it, so it can
#: never be a subject of its own scans.
_SELF = os.path.basename(__file__)


def _source_files(*suffixes, exclude=()):
    """Yield tracked source paths under the repo, skipping generated trees.

    Args:
        suffixes (str): File extensions to yield.
        exclude (tuple): Basenames to skip on top of :data:`_SELF` -- for the
            file that *defines* a rule and therefore quotes the banned form in
            its own docstring.
    """
    skip = {_SELF, *exclude}
    for dirpath, dirnames, filenames in os.walk(REPO_ROOT):
        dirnames[:] = [d for d in dirnames if d not in _SKIP_DIRS]
        for name in filenames:
            if name.endswith(suffixes) and name not in skip:
                yield os.path.join(dirpath, name)


def _run_case(case_cls):
    """Run *case_cls* silently and return the :class:`unittest.TestResult`."""
    suite = unittest.TestLoader().loadTestsFromTestCase(case_cls)
    return unittest.TextTestRunner(
        stream=open(os.devnull, "w"), verbosity=0
    ).run(suite)


def _static_text(node):
    """The literal text of *node*, or None when it is built at runtime."""
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return node.value
    if isinstance(node, ast.JoinedStr):  # f-string: the literal halves only
        return "".join(
            v.value
            for v in node.values
            if isinstance(v, ast.Constant) and isinstance(v.value, str)
        )
    return None


def _literal_skip_reasons(path):
    """Yield ``(lineno, text)`` for every ``skipTest`` given a LITERAL reason.

    Parsed rather than grepped so a reason spanning four lines is one string. A
    reason built by a *call* -- ``EnvSkip.reason(...)``,
    ``UnityEnv.license_reason()`` -- is marked by construction and never yielded;
    a bare literal (f-string and implicit concatenation included) is what the
    runner's tally cannot see.
    """
    with open(path, encoding="utf-8", errors="replace") as fh:
        tree = ast.parse(fh.read(), filename=path)
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        if not (isinstance(node.func, ast.Attribute) and node.func.attr == "skipTest"):
            continue
        if not node.args:
            continue
        text = _static_text(node.args[0])
        if text is not None:
            yield node.lineno, text


class EnvSkipTest(unittest.TestCase):
    """Environment-scoped skips are distinguishable from every other skip."""

    def test_reason_carries_the_marker(self):
        self.assertTrue(EnvSkip.reason("no editor").startswith(EnvSkip.MARKER))

    def test_reason_is_idempotent(self):
        once = EnvSkip.reason("no editor")
        self.assertEqual(EnvSkip.reason(once), once)

    def test_plain_reason_is_not_an_env_reason(self):
        self.assertFalse(EnvSkip.is_env_reason("Interactive-only"))

    def test_unless_marks_the_skip_it_takes(self):
        class Case(unittest.TestCase):
            @EnvSkip.unless(False, "no editor")
            def test_gated(self):  # pragma: no cover - always skipped
                self.fail("must not run")

        result = _run_case(Case)
        self.assertEqual(len(result.skipped), 1)
        self.assertTrue(EnvSkip.is_env_reason(result.skipped[0][1]))

    def test_unless_lets_a_satisfied_condition_run(self):
        class Case(unittest.TestCase):
            @EnvSkip.unless(True, "no editor")
            def test_gated(self):
                pass

        result = _run_case(Case)
        self.assertEqual(result.testsRun, 1)
        self.assertEqual(result.skipped, [])

    def test_count_ignores_a_plain_skip(self):
        class Case(unittest.TestCase):
            @unittest.skip("deliberate, not environmental")
            def test_plain(self):  # pragma: no cover - always skipped
                self.fail("must not run")

            @EnvSkip.unless(False, "no editor")
            def test_gated(self):  # pragma: no cover - always skipped
                self.fail("must not run")

        result = _run_case(Case)
        self.assertEqual(len(result.skipped), 2)
        self.assertEqual(EnvSkip.count(result), 1)

    def test_tally_groups_by_reason(self):
        class Case(unittest.TestCase):
            @EnvSkip.unless(False, "no editor")
            def test_a(self):  # pragma: no cover - always skipped
                self.fail("must not run")

            @EnvSkip.unless(False, "no editor")
            def test_b(self):  # pragma: no cover - always skipped
                self.fail("must not run")

            @EnvSkip.unless(False, "no unity")
            def test_c(self):  # pragma: no cover - always skipped
                self.fail("must not run")

        tally = EnvSkip.tally(_run_case(Case))
        self.assertEqual(
            sorted(tally.items()),
            sorted(
                [
                    (EnvSkip.reason("no editor"), 2),
                    (EnvSkip.reason("no unity"), 1),
                ]
            ),
        )



class UnityEnvTest(unittest.TestCase):
    """Unity's mid-test environmental failures are marked, and said once."""

    #: What a Unity-side environmental skip reads like. Anything matching this
    #: as a bare ``skipTest`` literal is invisible to ``SKIPPED (env)``.
    REASON_RE = re.compile(r"batch license|batch project creation", re.IGNORECASE)

    def test_both_reasons_are_environment_marked(self):
        for reason in (
            UnityEnv.project_creation_reason(),
            UnityEnv.license_reason(),
        ):
            self.assertTrue(EnvSkip.is_env_reason(reason), reason)

    def test_the_reasons_are_constant_so_the_tally_groups_them(self):
        """Two call sites, one summary line.

        ``tally`` keys on the reason TEXT, so a return code or a per-run log
        path interpolated into the reason would split one environmental cause
        across as many lines as there are suites taking it -- the opposite of
        the summary the marker exists to produce. Asserting the two reasons
        equal themselves would pass for any implementation; this drives two
        distinct call sites through ``tally`` and pins that they merge.
        """
        for reason in (UnityEnv.license_reason(), UnityEnv.project_creation_reason()):
            self.assertNotRegex(reason, r"\d", reason)

        class Case(unittest.TestCase):
            def test_a(self):
                self.skipTest(UnityEnv.license_reason())

            def test_b(self):
                self.skipTest(UnityEnv.license_reason())

            def test_c(self):
                self.skipTest(UnityEnv.project_creation_reason())

        self.assertEqual(
            EnvSkip.tally(_run_case(Case)),
            {
                UnityEnv.license_reason(): 2,
                UnityEnv.project_creation_reason(): 1,
            },
        )

    def test_no_suite_takes_an_unmarked_unity_env_skip(self):
        """The regression: 15 hand-copied reasons never reached the tally.

        An unmarked reason contributes nothing to ``EnvSkip.tally``, so
        ``run_tests.py`` sees ``env_skipped == 0``, stamps the README badge and
        returns success -- on a machine that never ran a single Unity import.
        """
        offenders = []
        for path in _source_files(".py"):
            for lineno, text in _literal_skip_reasons(path):
                if self.REASON_RE.search(text):
                    offenders.append(f"{os.path.relpath(path, REPO_ROOT)}:{lineno}")
        self.assertEqual(
            offenders,
            [],
            "unmarked Unity environment skip (use base_test.UnityEnv): "
            f"{offenders}",
        )

    def _editors(self, editors):
        """Stand in *editors* for the CLI's answer, read fresh, for this test."""
        patcher = mock.patch.object(UnityEnv, "_editors", dict(editors))
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_the_editor_is_the_named_one_else_the_newest_above_the_floor(self):
        """``UNITYTK_TEST_EDITOR`` names a version or an executable; unset, the
        newest editor at or above the package's floor runs, never an older one.
        Added: 2026-11-04"""
        import pythontk as ptk

        store = ptk.TempArtifacts("unitytk_editor_choice", policy="scoped")
        self.addCleanup(store.cleanup)
        loose = os.path.join(store.dir_path(), "Unity.exe")
        with open(loose, "w", encoding="utf-8") as fh:
            fh.write("")
        self._editors({"2022.3.40f1": "C:/U/2022/Unity.exe", "6000.3.10f1": "C:/U/6000/Unity.exe"})
        with mock.patch.dict(os.environ, {UnityEnv.EDITOR_VAR: ""}):
            self.assertEqual(UnityEnv.selected(), ("6000.3.10f1", "C:/U/6000/Unity.exe"))
            self.assertTrue(UnityEnv.available())
        with mock.patch.dict(os.environ, {UnityEnv.EDITOR_VAR: "2022.3.40f1"}):
            self.assertEqual(UnityEnv.editor(), "C:/U/2022/Unity.exe")
        with mock.patch.dict(os.environ, {UnityEnv.EDITOR_VAR: loose}):
            self.assertEqual(UnityEnv.selected(), (None, loose))
        with mock.patch.dict(os.environ, {UnityEnv.EDITOR_VAR: "1.2.3f1"}):
            self.assertEqual(UnityEnv.selected(), (None, None))
        self._editors({"2022.3.40f1": "C:/U/2022/Unity.exe"})
        with mock.patch.dict(os.environ, {UnityEnv.EDITOR_VAR: ""}):
            self.assertIsNone(UnityEnv.editor())
            self.assertFalse(UnityEnv.available())

    def test_the_editors_are_read_once_and_none_without_the_cli(self):
        """Every gated test asks at import; the CLI answers once a run, and a
        machine without it has no editors rather than an import error.
        Added: 2026-11-04"""
        from unitytk.unity_cli import UnityCli

        self._editors({})
        UnityEnv._editors = None
        with mock.patch.object(UnityCli, "editors", side_effect=FileNotFoundError("no cli")) as asked:
            self.assertEqual(UnityEnv.editors(), {})
            self.assertEqual(UnityEnv.editors(), {})
        self.assertEqual(asked.call_count, 1)

    def test_a_project_the_machine_cannot_create_skips_saying_why(self):
        """``UnityEnv.create_project`` makes a blank Built-in project with the
        CLI on the selected editor's version: the licence wall is the
        batch-licence skip, any other failed creation (or a folder the CLI left
        empty) the project-creation skip, no editor the no-editor skip, and a
        created project runs on.
        Added: 2026-11-04"""
        from unitytk.project_builder import ProjectBuilder
        from unitytk.unity_cli import UnityCli

        def make(path, version=None, template=None, timeout=None):
            for name in ("Assets", "ProjectSettings"):
                os.makedirs(os.path.join(path, name), exist_ok=True)
            return path

        def case(outcome):
            import pythontk as ptk

            store = ptk.TempArtifacts("unitytk_create_project", policy="scoped")
            self.addCleanup(store.cleanup)
            project = os.path.join(store.dir_path(), "p")

            class Case(unittest.TestCase):
                def test_create(self):
                    UnityEnv.create_project(self, project)

            with mock.patch.object(UnityCli, "create_project", side_effect=outcome) as made:
                return _run_case(Case), made

        self._editors({"6000.3.10f1": "C:/U/6000/Unity.exe"})
        with mock.patch.dict(os.environ, {UnityEnv.EDITOR_VAR: ""}):
            unlicensed, _ = case(RuntimeError("unity projects new failed: No valid Unity Editor license found"))
            self.assertEqual(EnvSkip.tally(unlicensed), {UnityEnv.license_reason(): 1})
            failed, _ = case(RuntimeError("unity projects new failed: template not found"))
            self.assertEqual(EnvSkip.tally(failed), {UnityEnv.project_creation_reason(): 1})
            empty, _ = case(lambda path, **_: path)
            self.assertEqual(EnvSkip.tally(empty), {UnityEnv.project_creation_reason(): 1})
            made, cli = case(make)
            self.assertEqual((made.testsRun, made.skipped, made.errors, made.failures), (1, [], [], []))
            self.assertEqual(
                cli.call_args.kwargs,
                {"version": "6000.3.10f1", "template": ProjectBuilder.TEMPLATES["builtin"]},
            )
        self._editors({})
        none, _ = case(make)
        self.assertEqual(EnvSkip.tally(none), {EnvSkip.reason(UnityEnv.NO_EDITOR): 1})

    def test_a_named_editor_the_cli_does_not_list_makes_no_project(self):
        """An executable the CLI does not list has no version to make with, so
        the CLI made the project with its default editor while :meth:`run`
        then opened it in the named one (an upgrade, or a downgrade, mid-suite).
        Added: 2026-11-08"""
        from unitytk.unity_cli import UnityCli

        self._editors({"6000.3.10f1": "C:/U/6000/Unity.exe"})
        project = os.path.join(REPO_ROOT, "tests", "temp_tests", "never_made")

        class Case(unittest.TestCase):
            def test_create(self):
                UnityEnv.create_project(self, project)

        with mock.patch.object(UnityEnv, "selected", return_value=(None, "D:/Other/Unity.exe")), mock.patch.object(
            UnityCli, "create_project"
        ) as made:
            result = _run_case(Case)
        made.assert_not_called()
        self.assertEqual(EnvSkip.tally(result), {EnvSkip.reason(UnityEnv.UNLISTED_EDITOR): 1})

    def test_run_starts_a_batch_editor_on_the_project_and_returns_its_code(self):
        """``UnityEnv.run``: Unity's own arguments, no ``-nographics`` (a
        verifier that renders needs the device), the exit code back, and a run
        that outlives its timeout fails the test.
        Added: 2026-11-04"""
        import subprocess

        import pythontk as ptk

        self._editors({"6000.3.10f1": "C:/U/6000/Unity.exe"})
        seen = {}

        class Case(unittest.TestCase):
            def test_run(self):
                seen["code"] = UnityEnv.run(self, "C:/P", "V.Verify", "C:/P.log", ["-x"], timeout=5)

            def test_bare(self):
                seen["bare"] = UnityEnv.run(self, "C:/P")

        done = subprocess.CompletedProcess([], 7)
        with mock.patch.dict(os.environ, {UnityEnv.EDITOR_VAR: ""}), mock.patch.object(
            ptk.AppLauncher, "run", return_value=done
        ) as launched:
            result = _run_case(Case)
        self.assertEqual((result.errors, result.failures), ([], []))
        self.assertEqual((seen["code"], seen["bare"]), (7, 7))
        bare, full = launched.call_args_list  # the loader runs test_bare first
        self.assertEqual(
            full.args,
            (
                "C:/U/6000/Unity.exe",
                ["-batchmode", "-projectPath", "C:/P", "-executeMethod", "V.Verify", "-logFile", "C:/P.log", "-quit", "-x"],
            ),
        )
        self.assertEqual(full.kwargs, {"timeout": 5})
        self.assertEqual(bare.args[1], ["-batchmode", "-projectPath", "C:/P", "-quit"])

        class Slow(unittest.TestCase):
            def test_slow(self):
                UnityEnv.run(self, "C:/P", "V.Verify", "C:/P.log", timeout=5)

        with mock.patch.dict(os.environ, {UnityEnv.EDITOR_VAR: ""}), mock.patch.object(
            ptk.AppLauncher, "run", side_effect=subprocess.TimeoutExpired("Unity.exe", 5)
        ):
            slow = _run_case(Slow)
        self.assertEqual(slow.errors, [])
        self.assertEqual(len(slow.failures), 1)
        self.assertIn("timed out", slow.failures[0][1])

    def test_a_licence_failure_is_what_unity_host_diagnoses(self):
        """One reading of the licence wall: ``UnityHost.diagnose``'s.
        Added: 2026-11-04"""
        self.assertTrue(UnityEnv.is_licence_failure(198, ""))
        self.assertTrue(UnityEnv.is_licence_failure(1, "No valid Unity Editor license found"))
        self.assertFalse(UnityEnv.is_licence_failure(0, "Batchmode quit successfully invoked\n"))
        self.assertFalse(UnityEnv.is_licence_failure(None, ""))
        # Every editor's log names its licensing client: a crash in one is a failure.
        crash = "[Licensing::Module] Trying to connect to existing licensing client channel...\nCrash!!!\n"
        self.assertFalse(UnityEnv.is_licence_failure(1, crash))

    def test_require_results_skips_unlicensed_and_fails_a_clean_run(self):
        """The check every batch verifier makes: results there, nothing to do;
        none from an unlicensed editor, an environmental skip; none from a
        clean run, a failure. Asserting the file alone failed every such suite
        on a machine signed out of the Hub."""
        import pythontk as ptk

        store = ptk.TempArtifacts("unitytk_require_results", policy="scoped")
        self.addCleanup(store.cleanup)
        root = store.dir_path()
        present = os.path.join(root, "results.json")
        with open(present, "w", encoding="utf-8") as fh:
            fh.write("{}")
        missing = os.path.join(root, "missing.json")
        log = os.path.join(root, "unity_log.txt")
        with open(log, "w", encoding="utf-8") as fh:
            fh.write("Batchmode quit successfully invoked\n")

        class Case(unittest.TestCase):
            def test_present(self):
                UnityEnv.require_results(self, present, 0, log)

            def test_unlicensed(self):
                UnityEnv.require_results(self, missing, 198, log)

            def test_clean_run_wrote_nothing(self):
                UnityEnv.require_results(self, missing, 0, log)

        result = _run_case(Case)
        self.assertEqual(
            [reason for _t, reason in result.skipped], [UnityEnv.license_reason()]
        )
        self.assertEqual(
            [t.id().rsplit(".", 1)[-1] for t, _tb in result.failures],
            ["test_clean_run_wrote_nothing"],
        )
        self.assertEqual(result.errors, [])
        self.assertEqual(result.testsRun, 3)

    def test_the_depth_gate_covers_every_shader_graph_urp_ships(self):
        """The gate measured the Autodesk Interactive graph; URP's deepest
        transparent graph sits 25 characters deeper, so a 107-character project
        passed it, that graph failed to import, URP's mapper left the material
        on plain Lit and the material suite failed on every run from the
        in-repo base (2026-10-19). Measured against each installed editor's
        bundled URP, not a list of the graphs the suites use today."""
        packages = [
            p
            for p in (
                os.path.join(
                    os.path.dirname(exe), "Data", "Resources", "PackageManager",
                    "BuiltInPackages", "com.unity.render-pipelines.universal",
                )
                for exe in UnityEnv.editors().values()
            )
            if os.path.isdir(p)
        ]
        if not packages:
            self.skipTest(EnvSkip.reason("no installed editor ships a bundled URP"))
        deepest = UnityProject.URP_IMPORT_DEPTH - len(UnityProject.URP_PACKAGE_CACHE)
        for package in packages:
            for dirpath, dirnames, filenames in os.walk(package):
                # A folder ending "~" (samples, docs) or hidden is never imported.
                dirnames[:] = [d for d in dirnames if not d.endswith("~") and not d.startswith(".")]
                for name in filenames:
                    if name.endswith((".shadergraph", ".shadersubgraph")):
                        below = os.path.join(dirpath, name)[len(package):]
                        self.assertLessEqual(len(below), deepest, below)

    @unittest.skipUnless(
        os.name == "nt", "MAX_PATH is the Windows editor's limit; no Unity runs here"
    )
    def test_every_suite_fits_a_urp_project_under_the_default_base(self):
        """A plain ``run_tests.py --all`` builds its projects under the in-repo
        base, where ``<run>\\urp\\UnityProject`` put the material suite's URP
        project at 107 characters: past the gate, so every URP suite failed at
        once (2026-10-19). Every suite's prefix, not only today's URP suites:
        any of them may add a ``"urp"`` project."""
        prefixes, written = set(), set()
        # The allocator's own tests allocate into a sandboxed base, retired prefixes included.
        for path in _source_files(".py", exclude=("test_temp_projects.py", "test_run_tests.py")):
            with open(path, encoding="utf-8", errors="replace") as fh:
                text = fh.read()
            written.update(re.findall(r'TempProjects\.allocate\(\s*"(\w+)"', text))
            tree = ast.parse(text, filename=path)
            for node in ast.walk(tree):
                if (
                    isinstance(node, ast.Call)
                    and isinstance(node.func, ast.Attribute)
                    and node.func.attr == "allocate"
                    and getattr(node.func.value, "id", None) == "TempProjects"
                    and node.args
                    and isinstance(node.args[0], ast.Constant)
                    and isinstance(node.args[0].value, str)
                ):
                    prefixes.add(node.args[0].value)
        # The scan finds every allocation the suites spell, held to a plain text search of
        # the same files (not to a count, which each retired suite would break).
        self.assertTrue(prefixes, "the suites' allocations were not found")
        self.assertEqual(prefixes, written, "the scan missed an allocation the suites spell")
        # TempArtifacts' tag: the clock (16 hex digits), the pid in hex (6: Linux's
        # pid_max ceiling; Windows ids run below it) and 4 random digits.
        tag = "f" * (16 + 6 + 4)
        budget = UnityEnv.URP_ROOT_MAX
        for prefix in sorted(prefixes):
            run = os.path.join(TempProjects.DEFAULT_BASE, f"{prefix}_{tag}")
            for project in (TempProjects.project(run), TempProjects.project(run, "urp")):
                with self.subTest(project=project):
                    self.assertLessEqual(len(project), budget, project)


class SuiteOptionsTest(unittest.TestCase):
    """The opt-in switches are named once, neutrally."""

    def test_preserve_reads_its_env_var(self):
        with mock.patch.dict(os.environ, {SuiteOptions.PRESERVE: "1"}):
            self.assertTrue(SuiteOptions.preserve())
        with mock.patch.dict(os.environ, {}, clear=True):
            self.assertFalse(SuiteOptions.preserve())

    def test_switch_names_are_package_scoped(self):
        self.assertTrue(SuiteOptions.PRESERVE.startswith("UNITYTK_"))


class TestAssetsTest(unittest.TestCase):
    """Fixture resolution: a plain join under ``UNITYTK_TEST_ASSETS``."""

    def setUp(self):
        import tempfile

        tmp = tempfile.TemporaryDirectory(prefix="unitytk_assets_test_")
        self.addCleanup(tmp.cleanup)
        self.root = tmp.name
        patcher = mock.patch.object(base_test, "TEST_ASSETS", self.root)
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_path_joins_under_the_root(self):
        self.assertEqual(
            TestAssets.path("a", "b.fbx"), os.path.join(self.root, "a", "b.fbx")
        )

    def test_the_one_release_alias_map_is_gone(self):
        """Deleted 2026-09-23, as its own note asked: nothing maps a neutral
        fixture name to a legacy one, so ``path`` is the plain join above."""
        self.assertFalse(hasattr(TestAssets, "LEGACY_ALIASES"))

    def test_unset_root_cannot_exist_under_either_guard(self):
        from pathlib import Path

        with mock.patch.dict(os.environ, {}, clear=True):
            root = os.environ.get("UNITYTK_TEST_ASSETS", "") or os.path.join(
                os.path.dirname(base_test.__file__), "__missing_test_asset__"
            )
        probe = os.path.join(root, "anything.fbx")
        self.assertFalse(os.path.exists(probe))
        self.assertFalse(Path(probe).exists())

    def test_asset_path_delegates_to_the_class(self):
        self.assertEqual(
            base_test.asset_path("a", "b.fbx"), TestAssets.path("a", "b.fbx")
        )


class PackageSourcesTest(unittest.TestCase):
    """The package's files resolve by bundle path, in one place."""

    def test_every_bundle_path_resolves_in_the_checkout(self):
        names = PackageSources.files()
        self.assertTrue(names)
        for name in names:
            self.assertTrue(os.path.isfile(PackageSources.path(name)), name)
            self.assertFalse(name.endswith(".meta"), name)

    def test_the_package_is_the_folder_unity_host_installs(self):
        """The files a suite compares with what Unity imported are the
        installed package's: ``UnityHost.install`` links
        ``ProjectBuilder.package_dir()``.
        Added: 2026-11-04"""
        from unitytk.project_builder import ProjectBuilder

        self.assertEqual(
            os.path.normcase(os.path.abspath(PackageSources.DIR)),
            os.path.normcase(str(ProjectBuilder.package_dir().resolve())),
        )

    def test_a_folder_unity_hides_is_no_part_of_the_package_it_imports(self):
        """``Samples~`` (and any ``~`` or ``.`` folder) is never imported:
        its files carry no meta of their parent's, and a suite comparing
        what Unity imported must not look for them. Added: 2026-11-05"""
        names = PackageSources.files()
        self.assertTrue(os.path.isdir(os.path.join(PackageSources.DIR, "Samples~")))
        self.assertFalse([n for n in names if n.startswith("Samples~/")])
        self.assertNotIn("Samples~", PackageSources.folders(names))

    def test_folders_names_every_parent_once_a_parent_first(self):
        """Added: 2026-11-04"""
        self.assertEqual(
            PackageSources.folders(["a/b/c.cs", "a/b/d.cs", "a/e.cs", "f.cs"]),
            ["a", "a/b"],
        )


def _client_denylist():
    """The client identifiers no tracked file may carry: the denylist m3trik's
    ``check_public_hygiene.py`` reads, which lives off every repo (spelling them
    here would publish them); ``None`` where there is none (a lone clone, CI)."""
    scripts = os.path.join(os.path.dirname(REPO_ROOT), "m3trik", "scripts")
    if not os.path.isfile(os.path.join(scripts, "check_public_hygiene.py")):
        return None
    sys.path.insert(0, scripts)
    try:
        import check_public_hygiene

        return check_public_hygiene.load_denylist(check_public_hygiene.DEFAULT_DENYLIST)
    except (ImportError, OSError, ValueError):
        return None
    finally:
        sys.path.remove(scripts)


class PublicHygieneTest(unittest.TestCase):
    """Client program identifiers stay out of tracked Python/C# source.

    The absolute studio paths, vendor name and person name went 2026-08-15; the
    program identifiers went 2026-08-17. This is the guard that keeps them out,
    reading the identifiers from the off-repo denylist, so it never spells them.
    Since the one-release alias map went (2026-09-23), no Python or C# file may
    name the legacy assets -- ``base_test.py`` included.
    """

    #: Program identifiers that must not appear as code identifiers.
    PROGRAM_RE = _client_denylist()

    @EnvSkip.unless(PROGRAM_RE is not None, "no client denylist (m3trik's check_public_hygiene.py)")
    def test_no_module_filename_carries_a_program_identifier(self):
        offenders = [
            os.path.relpath(p, REPO_ROOT)
            for p in _source_files(".py", ".cs")
            if self.PROGRAM_RE.search(os.path.basename(p))
        ]
        self.assertEqual(offenders, [], f"program identifier in filename: {offenders}")

    @EnvSkip.unless(PROGRAM_RE is not None, "no client denylist (m3trik's check_public_hygiene.py)")
    def test_no_program_identifier_in_python_or_cs_source(self):
        offenders = []
        for path in _source_files(".py", ".cs"):
            with open(path, encoding="utf-8", errors="replace") as fh:
                for lineno, line in enumerate(fh, 1):
                    if self.PROGRAM_RE.search(line):
                        offenders.append(
                            f"{os.path.relpath(path, REPO_ROOT)}:{lineno}"
                        )
        self.assertEqual(offenders, [], f"program identifier in source: {offenders}")


if __name__ == "__main__":
    unittest.main()
