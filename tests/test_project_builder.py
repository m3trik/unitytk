"""Tests for unitytk.project_builder: the owned test and demo project, made by
Unity's CLI and linked to this checkout's package by one manifest line. No
editor runs here: the CLI is a stand-in that makes the folders it would."""

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

import pythontk as ptk

from unitytk.project_builder import ProjectBuilder
from unitytk.unity_cli import UnityCli

RESULTS = """<?xml version="1.0" encoding="utf-8"?>
<test-run result="Failed(Child)">
  <test-case fullname="A.Passes" result="Passed" />
  <test-case fullname="A.Fails" result="Failed"><failure><message>expected 1</message></failure></test-case>
  <test-case fullname="A.Skips" result="Skipped" />
  <test-case fullname="A.Assumes" result="Inconclusive"><reason><message>this project draws with Built-in</message></reason></test-case>
</test-run>
"""

#: Unity exits 2 for this run too: an inconclusive test and no failure.
INCONCLUSIVE = """<?xml version="1.0" encoding="utf-8"?>
<test-run result="Passed">
  <test-case fullname="A.Passes" result="Passed" />
  <test-case fullname="A.Assumes" result="Inconclusive"><reason><message>Built-in only</message></reason></test-case>
</test-run>
"""

NOTHING = """<?xml version="1.0" encoding="utf-8"?>
<test-run result="Passed" />
"""


def _made_by_cli(path, version=None, template=None, timeout=None, cancel=None):
    """What ``unity projects new`` leaves: an imported project with a manifest."""
    folder = Path(path)
    for name in ("Assets", "ProjectSettings", "Packages"):
        (folder / name).mkdir(parents=True, exist_ok=True)
    (folder / "Packages" / "manifest.json").write_text(
        json.dumps({"dependencies": {"com.unity.render-pipelines.universal": "17.3.0"}}), encoding="utf-8"
    )
    return str(folder)


class TestProjectBuilder(UnityTkTestCase):
    """Added: 2026-11-04"""

    def setUp(self):
        super().setUp()
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.cache = Path(self._tmp.name)
        patches = [
            patch.object(UnityCli, "editors", return_value={"6000.3.10f1": "C:/U/Unity.exe"}),
            patch.object(ptk.UserConfig, "cache_dir", side_effect=lambda *parts, **_: self.cache.joinpath(*parts)),
            patch.dict(os.environ, {ProjectBuilder.ENV_VAR: ""}),
            # The temp folder is deeper than a URP project may be; the guard's own test sets it back.
            patch.object(ProjectBuilder, "URP_ROOT_MAX", 10_000),
        ]
        for p in patches:
            p.start()
            self.addCleanup(p.stop)

    def _manifest(self, folder):
        return json.loads(Path(folder, "Packages", "manifest.json").read_text(encoding="utf-8"))

    def test_it_is_made_once_by_the_cli_from_the_editors_template_and_linked_here(self):
        with patch.object(UnityCli, "create_project", side_effect=_made_by_cli) as made:
            folder = ProjectBuilder.build()
            ProjectBuilder.build()
        made.assert_called_once()
        self.assertEqual(made.call_args.kwargs["template"], "com.unity.template.urp-blank")
        self.assertEqual(made.call_args.kwargs["version"], "6000.3.10f1")
        manifest = self._manifest(folder)
        self.assertEqual(
            manifest["dependencies"]["com.m3trik.unitytk"],
            "file:" + ProjectBuilder.package_dir().resolve().as_posix(),
        )
        self.assertEqual(manifest["testables"], ["com.m3trik.unitytk"])
        for name, version in ProjectBuilder.DOOR_PACKAGES.items():
            self.assertEqual(manifest["dependencies"][name], version)
        self.assertEqual(manifest["dependencies"]["com.unity.render-pipelines.universal"], "17.3.0", "the template's kept")

    def test_builtin_takes_the_builtin_template_in_a_folder_of_its_own(self):
        with patch.object(UnityCli, "create_project", side_effect=_made_by_cli) as made:
            urp = ProjectBuilder.build()
            builtin = ProjectBuilder.build(pipeline="builtin")
        self.assertNotEqual(urp, builtin)
        self.assertEqual(made.call_args.kwargs["template"], "com.unity.template.3d")
        with self.assertRaisesRegex(ValueError, "not one of"):
            ProjectBuilder.build(pipeline="hdrp")

    def test_a_dry_run_makes_and_writes_nothing(self):
        ctx = ptk.OpContext(dry_run=True)
        with patch.object(UnityCli, "create_project", side_effect=_made_by_cli) as made:
            folder = ProjectBuilder.build(ctx=ctx)
        made.assert_not_called()
        self.assertFalse(Path(folder).exists())
        self.assertEqual([c.kind for c in ctx.changes], ["create"])

    def test_an_open_project_is_never_written(self):
        with patch.object(UnityCli, "create_project", side_effect=_made_by_cli):
            folder = ProjectBuilder.build()
        before = self._manifest(folder)
        before["dependencies"].pop("com.m3trik.unitytk")
        Path(folder, "Packages", "manifest.json").write_text(json.dumps(before), encoding="utf-8")
        with patch.object(ProjectBuilder, "open_by", return_value="its Temp/UnityLockfile is held"), patch.object(
            ProjectBuilder, "RELEASE_WAIT", 0.0
        ):
            with self.assertRaisesRegex(RuntimeError, "never close it yourself"):
                ProjectBuilder.build()
        self.assertNotIn("com.m3trik.unitytk", self._manifest(folder)["dependencies"])

    def test_a_key_builds_a_project_of_its_own_beside_the_owned_one(self):
        """Added: 2026-11-08"""
        with patch.object(UnityCli, "create_project", side_effect=_made_by_cli) as made:
            owned = ProjectBuilder.build()
            keyed = ProjectBuilder.build(key="e2e")
            ProjectBuilder.build(key="e2e")
        self.assertEqual(made.call_count, 2, "each made once")
        self.assertEqual(Path(keyed), Path(owned).with_name("Unitytk-e2e"))
        self.assertEqual(self._manifest(keyed)["testables"], ["com.m3trik.unitytk"], "linked alike")
        for bad in ("", "e2e/../x", "a" * 17):
            with self.assertRaisesRegex(ValueError, "project key"):
                ProjectBuilder.folder("6000.3.10f1", key=bad)

    def test_no_editor_at_the_floor_says_what_to_install(self):
        with patch.object(UnityCli, "editors", return_value={"2022.3.40f1": "C:/U/2022/Unity.exe"}):
            with self.assertRaisesRegex(FileNotFoundError, "6000.3 or newer"):
                ProjectBuilder.build()

    def test_test_results_are_counted_with_their_failures(self):
        path = self.cache / "results.xml"
        path.write_text(RESULTS, encoding="utf-8")
        outcome = ProjectBuilder.parse(str(path))
        self.assertEqual(
            (outcome["total"], outcome["passed"], outcome["failed"], outcome["skipped"], outcome["inconclusive"]),
            (4, 1, 1, 1, 1),
        )
        self.assertEqual(outcome["failures"], [{"name": "A.Fails", "message": "expected 1"}])
        self.assertFalse(outcome["ok"])

    def test_the_verdict_is_the_results_files_not_the_exit_code(self):
        """Unity exits 2 for an inconclusive test as for a failed one, and a run
        of no test is no pass: an inconclusive case is reported, not failed.
        Added: 2026-11-04"""
        path = self.cache / "results.xml"
        path.write_text(INCONCLUSIVE, encoding="utf-8")
        outcome = ProjectBuilder.parse(str(path))
        self.assertTrue(outcome["ok"])
        self.assertEqual(outcome["inconclusives"], [{"name": "A.Assumes", "message": "Built-in only"}])
        path.write_text(NOTHING, encoding="utf-8")
        outcome = ProjectBuilder.parse(str(path))
        self.assertEqual(outcome["total"], 0)
        self.assertFalse(outcome["ok"], "0 of 0 is not a pass")

    def test_a_run_reads_its_verdict_from_its_results(self):
        """Added: 2026-11-04"""
        project = _made_by_cli(self.cache / "P")
        (Path(project) / "ProjectSettings" / "ProjectVersion.txt").write_text(
            "m_EditorVersion: 6000.3.10f1\n", encoding="utf-8"
        )

        def run(exe, args, **_):
            Path(args[args.index("-testResults") + 1]).write_text(INCONCLUSIVE, encoding="utf-8")
            return subprocess.CompletedProcess(args, 2)

        with patch.object(ptk.AppLauncher, "run", side_effect=run) as launched:
            outcome = ProjectBuilder.test(project=project)
        self.assertEqual(launched.call_args[0][0], "C:/U/Unity.exe", "the editor the project names")
        self.assertEqual((outcome["code"], outcome["ok"], outcome["inconclusive"]), (2, True, 1))

    def test_the_first_build_reports_its_progress_and_a_cancel_takes_the_half_made_project_away(self):
        """Its first build takes minutes inside an operation call: progress
        through the context, and a cancel stops the CLI (CODE_STANDARD
        section 15). Added: 2026-11-04"""
        heard = []
        ctx = ptk.OpContext(on_progress=lambda fraction, message: heard.append((fraction, message)))
        with patch.object(UnityCli, "create_project", side_effect=_made_by_cli) as made:
            ProjectBuilder.build(ctx=ctx)
        self.assertEqual([f for f, _ in heard], [0.0, 1.0])
        self.assertIn("about 4 minutes", heard[0][1])
        self.assertIsNotNone(made.call_args.kwargs["cancel"], "the CLI hears the cancel")

        scope = ptk.CancelScope()
        ctx = ptk.OpContext(scope=scope)

        def cancelled(path, cancel=None, **_):
            _made_by_cli(path)
            scope.cancel("stop")
            cancel()

        with patch.object(UnityCli, "create_project", side_effect=cancelled), patch.object(
            UnityCli, "unregister", return_value=True
        ) as forgot:
            with self.assertRaises(ptk.OperationCancelled):
                ProjectBuilder.build(pipeline="builtin", ctx=ctx)
        folder = ProjectBuilder.folder("6000.3.10f1", "builtin")
        self.assertFalse(folder.exists(), "no half-made project is left to refuse the next build")
        forgot.assert_called_once_with(str(folder))

    def test_a_folder_that_holds_no_project_is_refused_and_left_alone(self):
        """The make's cleanup deleted whatever was at the folder when the CLI
        failed there, a folder this call never made included. Added: 2026-11-08"""
        folder = ProjectBuilder.folder("6000.3.10f1")
        folder.mkdir(parents=True)
        (folder / "notes.txt").write_text("someone's", encoding="utf-8")
        with patch.object(UnityCli, "create_project", side_effect=RuntimeError("a folder that exists")) as made:
            with self.assertRaisesRegex(RuntimeError, "holds no Unity project"):
                ProjectBuilder.build()
        made.assert_not_called()
        self.assertTrue((folder / "notes.txt").is_file())

    def test_a_failed_make_takes_away_only_what_it_made(self):
        """Added: 2026-11-08"""
        def fails(path, **_):
            _made_by_cli(path)
            raise RuntimeError("Unknown template")

        with patch.object(UnityCli, "create_project", side_effect=fails), patch.object(
            UnityCli, "unregister", return_value=True
        ) as forgot:
            with self.assertRaisesRegex(RuntimeError, "Unknown template"):
                ProjectBuilder.build()
        folder = ProjectBuilder.folder("6000.3.10f1")
        self.assertFalse(folder.exists())
        self.assertEqual(sorted(p.name for p in folder.parent.iterdir() if p.name != folder.name + ".lock"), [])
        forgot.assert_called_once_with(str(folder))

    def test_a_half_made_project_a_killed_build_left_is_made_again(self):
        """A build killed mid-make (no cleanup ran) left a folder that passes
        ``is_project``: its marker says this tooling made it. Added: 2026-11-08"""
        folder = ProjectBuilder.folder("6000.3.10f1")
        _made_by_cli(folder)
        (folder / "Assets" / "half.txt").write_text("x", encoding="utf-8")
        ProjectBuilder._pending(folder).write_text("4242", encoding="utf-8")
        with patch.object(UnityCli, "create_project", side_effect=_made_by_cli) as made, patch.object(
            UnityCli, "unregister", return_value=True
        ):
            ProjectBuilder.build()
        made.assert_called_once()
        self.assertFalse((folder / "Assets" / "half.txt").exists())
        self.assertFalse(ProjectBuilder._pending(folder).exists())

    def test_a_leftover_that_could_not_be_removed_is_never_taken_for_a_project(self):
        """The cleanup's ``rmtree`` ignored its errors: a project a held file
        kept half of passed ``is_project`` on the next build. Added: 2026-11-08"""
        def fails(path, **_):
            _made_by_cli(path)
            raise RuntimeError("Unknown template")

        folder = ProjectBuilder.folder("6000.3.10f1")
        with patch("shutil.rmtree"), patch.object(ProjectBuilder, "REMOVE_TRIES", 1), patch.object(
            UnityCli, "unregister", return_value=True
        ):
            with patch.object(UnityCli, "create_project", side_effect=fails):
                with self.assertRaisesRegex(RuntimeError, "Unknown template"):
                    ProjectBuilder.build()
            self.assertTrue(ProjectBuilder.is_project(folder), "the stand-in removal left it")
            with patch.object(UnityCli, "create_project", side_effect=_made_by_cli) as made:
                with self.assertRaisesRegex(RuntimeError, "could not be removed"):
                    ProjectBuilder.build()
            made.assert_not_called()

    def test_two_builds_of_one_project_take_turns(self):
        """Two runs building the one project at once both made it. Added: 2026-11-08"""
        import threading

        started, release = threading.Event(), threading.Event()

        def slow(path, **_):
            started.set()
            release.wait(10)
            return _made_by_cli(path)

        results, errors = [], []

        def build():
            try:
                results.append(ProjectBuilder.build())
            except Exception as error:  # noqa: BLE001
                errors.append(error)

        with patch.object(UnityCli, "create_project", side_effect=slow) as made, patch.object(
            ProjectBuilder, "LOCK_POLL", 0.01
        ):
            first = threading.Thread(target=build)
            first.start()
            self.assertTrue(started.wait(10))
            second = threading.Thread(target=build)
            second.start()
            second.join(0.3)
            self.assertTrue(second.is_alive(), "the second build waits for the first")
            release.set()
            first.join(10)
            second.join(10)
        self.assertEqual(errors, [])
        self.assertEqual(made.call_count, 1)
        self.assertEqual(len(set(results)), 1)

    def _project(self):
        project = _made_by_cli(self.cache / "P")
        (Path(project) / "ProjectSettings" / "ProjectVersion.txt").write_text(
            "m_EditorVersion: 6000.3.10f1\n", encoding="utf-8"
        )
        return project

    def test_a_run_that_wrote_no_results_says_why_with_the_logs_end(self):
        """Added: 2026-11-08"""
        project = self._project()

        def run(exe, args, **_):
            log = Path(args[args.index("-logFile") + 1])
            log.write_text("starting\nAssets/A.cs(1,1): error CS0246: no type Foo\nAborting\n", encoding="utf-8")
            return subprocess.CompletedProcess(args, 1)

        with patch.object(ptk.AppLauncher, "run", side_effect=run):
            with self.assertRaises(RuntimeError) as raised:
                ProjectBuilder.test(project=project)
        message = str(raised.exception)
        self.assertIn("wrote no test results", message)
        self.assertIn("do not compile", message)
        self.assertIn("Aborting", message, "the log's last lines")
        self.assertIn("editor.log", message)

    def test_a_run_past_its_timeout_ends_the_editors_tree_and_says_so(self):
        """``AppLauncher.run``'s ``TimeoutExpired`` was neither documented nor
        caught: a traceback after an hour, the import workers left behind.
        Added: 2026-11-08"""
        project = self._project()

        def run(exe, args, **_):
            Path(args[args.index("-logFile") + 1]).write_text("importing\nstill importing\n", encoding="utf-8")
            raise subprocess.TimeoutExpired(exe, 5.0)

        with patch.object(ptk.AppLauncher, "run", side_effect=run) as launched:
            with self.assertRaisesRegex(RuntimeError, r"did not finish within 5 s(.|\n)*still importing"):
                ProjectBuilder.test(project=project, timeout=5.0)
        self.assertTrue(launched.call_args.kwargs["hidden"], "in a job that ends everything it started")
        self.assertEqual(launched.call_args.kwargs["timeout"], 5.0)

    def test_the_filter_is_unitys_test_filter(self):
        """Added: 2026-11-08"""
        project = self._project()

        def run(exe, args, **_):
            Path(args[args.index("-testResults") + 1]).write_text(INCONCLUSIVE, encoding="utf-8")
            return subprocess.CompletedProcess(args, 0)

        with patch.object(ptk.AppLauncher, "run", side_effect=run) as launched:
            ProjectBuilder.test(project=project, test_filter="Kernel.Json|Records")
            args = launched.call_args.kwargs.get("args") or launched.call_args[0][1]
            self.assertEqual(args[args.index("-testFilter") + 1], "Kernel.Json|Records")
            ProjectBuilder.test(project=project, platform="PlayMode")
            args = launched.call_args.kwargs.get("args") or launched.call_args[0][1]
            self.assertNotIn("-testFilter", args)
            self.assertEqual(args[args.index("-testPlatform") + 1], "PlayMode")

    def test_a_project_an_editor_has_open_is_never_tested(self):
        """Added: 2026-11-08"""
        project = self._project()
        with patch.object(ProjectBuilder, "open_by", return_value="Unity (pid 7) has it open"), patch.object(
            ptk.AppLauncher, "run"
        ) as launched:
            with self.assertRaisesRegex(RuntimeError, "never close it yourself"):
                ProjectBuilder.test(project=project)
        launched.assert_not_called()

    def test_a_folder_too_deep_for_urps_imports_is_refused_with_its_fix(self):
        """The margin is derived: MAX_PATH less URP's deepest import below a
        project root. Added: 2026-11-04"""
        from unitytk.unity_project import UnityProject

        self.assertEqual(UnityProject.URP_ROOT_MAX, UnityProject.MAX_PATH - UnityProject.URP_IMPORT_DEPTH)
        self.assertEqual(UnityProject.URP_ROOT_MAX, 101)
        deep = str(self.cache / ("d" * 120))
        with patch.dict(os.environ, {ProjectBuilder.ENV_VAR: deep}), patch.object(
            ProjectBuilder, "URP_ROOT_MAX", UnityProject.URP_ROOT_MAX
        ), patch.object(UnityCli, "create_project") as made:
            with self.assertRaisesRegex(ValueError, ProjectBuilder.ENV_VAR):
                ProjectBuilder.build()
            made.assert_not_called()
            # Built-in carries no URP: its own folder is no concern of URP's.
            with patch.object(UnityCli, "create_project", side_effect=_made_by_cli):
                ProjectBuilder.build(pipeline="builtin")

    def test_the_builder_and_the_host_share_one_project_layer_and_import_neither_way(self):
        """The manifest, the package line and the open guard have one home
        below both. Added: 2026-11-04"""
        import ast

        from unitytk.unity_host import UnityHost
        from unitytk.unity_project import UnityProject

        self.assertTrue(issubclass(ProjectBuilder, UnityProject) and issubclass(UnityHost, UnityProject))
        source = Path(__file__).resolve().parents[1] / "unitytk" / "project_builder.py"
        tree = ast.parse(source.read_text(encoding="utf-8"))
        imported = {node.module for node in ast.walk(tree) if isinstance(node, ast.ImportFrom) and node.module}
        self.assertNotIn("unitytk.unity_host", imported)


class TestSampleImport(UnityTkTestCase):
    """The package's samples, copied in where the Package Manager imports
    them. A stand-in package with one sample. Added: 2026-11-05"""

    FILES = {
        "Model.fbx": b"a delivery",
        "Model.fbx.meta": b"fileFormatVersion: 2\nguid: 0123\n",
        "Editor.meta": b"fileFormatVersion: 2\nguid: 4567\n",
        "Editor/Recipe.cs": b"// the demo recipe\n",
        "Editor/Recipe.cs.meta": b"fileFormatVersion: 2\nguid: 89ab\n",
    }

    def setUp(self):
        super().setUp()
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        root = Path(self._tmp.name)
        self.package = root / "com.example.pkg"
        self.source = self.package / "Samples~" / "Demo"
        for rel, data in self.FILES.items():
            (self.source / rel).parent.mkdir(parents=True, exist_ok=True)
            (self.source / rel).write_bytes(data)
        self._manifest("1.2.0")
        self.project = root / "Project"
        (self.project / "Assets").mkdir(parents=True)

    def _manifest(self, version):
        (self.package / "package.json").write_text(
            json.dumps({"name": "com.example.pkg", "version": version, "displayName": "Pkg",
                        "samples": [{"displayName": "Demo", "description": "d", "path": "Samples~/Demo"}]}),
            encoding="utf-8",
        )

    def _imported(self, version="1.2.0"):
        return self.project / "Assets" / "Samples" / "Pkg" / version / "Demo"

    def test_each_sample_lands_where_the_package_manager_imports_it_whole(self):
        copied = ProjectBuilder.import_samples(self.project, self.package)
        self.assertEqual(copied, [str(self._imported())])
        for rel, data in self.FILES.items():
            self.assertEqual((self._imported() / rel).read_bytes(), data, rel)

    def test_an_unchanged_sample_is_left_with_what_its_recipe_built_and_a_changed_one_is_replaced(self):
        ProjectBuilder.import_samples(self.project, self.package)
        built = self._imported() / "Demo.unity"
        built.write_text("the demo scene", encoding="utf-8")
        self.assertEqual(ProjectBuilder.import_samples(self.project, self.package), [], "unchanged: no copy")
        self.assertTrue(built.is_file())

        (self.source / "Editor" / "Recipe.cs").write_bytes(b"// changed\n")
        (self.source / "Model.fbx").unlink()
        self.assertEqual(ProjectBuilder.import_samples(self.project, self.package), [str(self._imported())])
        self.assertEqual((self._imported() / "Editor" / "Recipe.cs").read_bytes(), b"// changed\n")
        self.assertFalse((self._imported() / "Model.fbx").exists(), "a re-import replaces the folder whole")
        self.assertFalse(built.exists())

    def test_a_replacement_is_delivered_in_place_of_the_samples_file_until_a_build_without_it(self):
        """Added: 2026-11-08"""
        twin = Path(self._tmp.name) / "twin"
        twin.mkdir()
        (twin / "Model.fbx").write_bytes(b"another producer's delivery")
        copied = ProjectBuilder.import_samples(self.project, self.package, replacements={"Demo": twin})
        self.assertEqual(copied, [str(self._imported())])
        self.assertEqual((self._imported() / "Model.fbx").read_bytes(), b"another producer's delivery")
        self.assertEqual(
            (self._imported() / "Editor" / "Recipe.cs").read_bytes(), self.FILES["Editor/Recipe.cs"], "the rest is the sample's"
        )
        self.assertEqual(ProjectBuilder.import_samples(self.project, self.package, replacements={"Demo": twin}), [], "unchanged")
        self.assertEqual(ProjectBuilder.import_samples(self.project, self.package), [str(self._imported())], "without it")
        self.assertEqual((self._imported() / "Model.fbx").read_bytes(), b"a delivery", "the sample's own again")

    def test_a_replacement_names_a_sample_and_only_its_files_before_anything_is_copied(self):
        """Added: 2026-11-08"""
        twin = Path(self._tmp.name) / "twin"
        twin.mkdir()
        with self.assertRaisesRegex(ValueError, "holds no file"):
            ProjectBuilder.import_samples(self.project, self.package, replacements={"Demo": twin})
        (twin / "Other.fbx").write_bytes(b"x")
        with self.assertRaisesRegex(ValueError, "holds no Other.fbx"):
            ProjectBuilder.import_samples(self.project, self.package, replacements={"Demo": twin})
        with self.assertRaisesRegex(ValueError, "named Nope"):
            ProjectBuilder.import_samples(self.project, self.package, replacements={"Nope": twin})
        self.assertFalse((self.project / "Assets" / "Samples").exists())

    def test_another_versions_samples_leave(self):
        ProjectBuilder.import_samples(self.project, self.package)
        self._manifest("1.3.0")
        ProjectBuilder.import_samples(self.project, self.package)
        self.assertTrue(self._imported("1.3.0").is_dir())
        self.assertFalse(self._imported("1.2.0").parent.exists(), "one version's assemblies, never two")

    def test_a_dry_run_plans_the_copy_and_writes_nothing(self):
        ctx = ptk.OpContext(dry_run=True)
        self.assertEqual(ProjectBuilder.import_samples(self.project, self.package, ctx=ctx), [])
        self.assertFalse((self.project / "Assets" / "Samples").exists())
        self.assertEqual([c.kind for c in ctx.changes], ["write"])

    def test_the_build_imports_this_checkouts_samples(self):
        with patch.object(UnityCli, "editors", return_value={"6000.3.10f1": "C:/U/Unity.exe"}), patch.object(
            ptk.UserConfig, "cache_dir", side_effect=lambda *parts, **_: Path(self._tmp.name).joinpath(*parts)
        ), patch.dict(os.environ, {ProjectBuilder.ENV_VAR: ""}), patch.object(
            ProjectBuilder, "URP_ROOT_MAX", 10_000
        ), patch.object(UnityCli, "create_project", side_effect=_made_by_cli):
            folder = ProjectBuilder.build()
        package = ProjectBuilder.package_dir()
        samples = ProjectBuilder.samples(package)
        self.assertTrue(samples, "the package lists its samples")
        for sample in samples:
            imported = ProjectBuilder.sample_folder(folder, package, sample)
            for path in Path(sample["source"]).rglob("*"):
                if path.is_file():
                    rel = path.relative_to(sample["source"])
                    self.assertEqual((imported / rel).read_bytes(), path.read_bytes(), rel)


if __name__ == "__main__":
    unittest.main()
