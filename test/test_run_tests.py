# !/usr/bin/python
# coding=utf-8
"""Tests for :class:`run_tests.UnityTestRunner`'s pass/fail contract.

A test runner that reports success when it never ran the tests is worse than no
runner: every downstream gate (CI, the README badge, a release check) reads that
exit code. The case pinned here is a module that fails to *import* -- a syntax
error or a bad import contributes zero test cases, so a runner that only counts
failures in what loaded will call the run green.

Observed live: adding ``test_temp_projects.py`` against a missing symbol printed
``[ERROR] Failed to load module`` and still exited 0.

The second family pinned here is the same defect wearing a skip: twelve
integration suites gate on Unity / a fixture root, and when those are absent
they contribute skips, not failures -- so a run that executed *none* of the
gated contract printed the same trailing ``OK`` as one that executed all of it. Environment-scoped skips are marked (``base_test.EnvSkip``),
counted on their own ``SKIPPED (env)`` line, kept off the README badge, and
refused a plain-green summary.
"""

import io
import os
import pathlib
import subprocess
import sys
import tempfile
import time
import unittest
from contextlib import redirect_stdout
from unittest import mock

from base_test import EnvSkip, TempProjects
from run_tests import EditMode, UnityReceipt, UnityTestRunner


class RunnerLoadFailureTest(unittest.TestCase):
    """A module that cannot be imported must fail the run, not skip it."""

    def setUp(self):
        tmp = tempfile.TemporaryDirectory(prefix="unitytk_runner_test_")
        self.addCleanup(tmp.cleanup)
        self.dir = tmp.name
        sys.path.insert(0, self.dir)
        self.addCleanup(sys.path.remove, self.dir)

        # run_tests() reclaims stale scratch at start-up; point that at the
        # sandbox so these nested runs never touch the real temp_tests/.
        base = mock.patch.object(
            TempProjects, "BASE", os.path.join(self.dir, "temp_tests")
        )
        base.start()
        self.addCleanup(base.stop)

        self.runner = UnityTestRunner()
        self.runner.test_dir = pathlib.Path(self.dir)

    def _write(self, name, body):
        path = os.path.join(self.dir, name)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(body)
        self.addCleanup(sys.modules.pop, name[:-3], None)
        return path

    def test_unimportable_module_fails_the_run(self):
        self._write(
            "test_broken_import.py",
            "import a_module_that_does_not_exist_anywhere\n",
        )
        self.assertFalse(self.runner.run_tests(run_all=True, update_badge=False))

    def test_unimportable_module_fails_even_beside_passing_tests(self):
        """The green module must not mask the broken one."""
        self._write(
            "test_broken_import.py",
            "import a_module_that_does_not_exist_anywhere\n",
        )
        self._write(
            "test_fine.py",
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    def test_ok(self):\n"
            "        self.assertTrue(True)\n",
        )
        self.assertFalse(self.runner.run_tests(run_all=True, update_badge=False))

    def test_load_failure_refuses_the_badge_stamp(self):
        """A reduced count from a module that never loaded is not coverage."""
        self._write(
            "test_broken_import.py",
            "import a_module_that_does_not_exist_anywhere\n",
        )
        self._write(
            "test_fine.py",
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    def test_ok(self):\n"
            "        self.assertTrue(True)\n",
        )
        with mock.patch.object(UnityTestRunner, "update_readme_badge") as stamp:
            self.runner.run_tests(run_all=True, update_badge=True)
        stamp.assert_not_called()

    def test_unknown_module_name_fails_the_run(self):
        """A requested module that does not exist must not exit 0.

        Same family as the load-error case: the runner reports success without
        having run what it was asked to run, so a typo in a CI invocation
        (``run_tests.py launchr``) passes silently.
        """
        self._write(
            "test_fine.py",
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    def test_ok(self):\n"
            "        self.assertTrue(True)\n",
        )
        self.assertFalse(
            self.runner.run_tests(modules=["no_such_module"], update_badge=False)
        )

    def test_known_module_name_still_runs(self):
        """The normal scoped-run path is unaffected by the unknown-name check."""
        self._write(
            "test_fine.py",
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    def test_ok(self):\n"
            "        self.assertTrue(True)\n",
        )
        self.assertTrue(self.runner.run_tests(modules=["fine"], update_badge=False))

    def test_all_importable_and_passing_is_green(self):
        self._write(
            "test_fine.py",
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    def test_ok(self):\n"
            "        self.assertTrue(True)\n",
        )
        self.assertTrue(self.runner.run_tests(run_all=True, update_badge=False))

    def test_k_narrows_and_never_stamps_even_covering_every_module(self):
        """A -k run touches every module and is still partial."""
        self._write(
            "test_fine.py",
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    def test_kept(self):\n"
            "        pass\n"
            "    def test_dropped(self):\n"
            "        self.fail('filtered out by -k')\n",
        )
        with mock.patch.object(UnityTestRunner, "update_readme_badge") as stamp, \
                redirect_stdout(io.StringIO()):
            ok = self.runner.run_tests(run_all=True, update_badge=True, patterns=["kept"])
        self.assertTrue(ok, "only test_kept ran, so the deliberate failure never did")
        stamp.assert_not_called()

    def test_genuine_test_failure_is_still_red(self):
        """The pre-existing contract must survive the load-error change."""
        self._write(
            "test_fails.py",
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    def test_no(self):\n"
            "        self.fail('deliberate')\n",
        )
        self.assertFalse(self.runner.run_tests(run_all=True, update_badge=False))


class RunnerEnvSkipTest(unittest.TestCase):
    """A run whose gated suites never executed must say so, loudly.

    ``unittest`` files "Unity is not installed" in the same bucket as "not
    applicable on this platform", so the trailing ``OK`` is identical whether
    the consumer contract ran or was skipped wholesale. The runner therefore
    reads the marker ``base_test.EnvSkip`` stamps and reports the count on its
    own line.
    """

    #: Reason bodies the generated modules skip with.
    ASSETS = "UNITYTK_TEST_ASSETS is not set"
    UNITY = "no Unity editor found"

    def setUp(self):
        tmp = tempfile.TemporaryDirectory(prefix="unitytk_envskip_test_")
        self.addCleanup(tmp.cleanup)
        self.dir = tmp.name
        sys.path.insert(0, self.dir)
        self.addCleanup(sys.path.remove, self.dir)

        # run_tests() reclaims stale scratch at start-up; point that at the
        # sandbox so these nested runs never touch the real temp_tests/.
        base = mock.patch.object(
            TempProjects, "BASE", os.path.join(self.dir, "temp_tests")
        )
        base.start()
        self.addCleanup(base.stop)

        self.runner = UnityTestRunner()
        self.runner.test_dir = pathlib.Path(self.dir)

    def _write(self, name, body):
        path = os.path.join(self.dir, name)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(body)
        self.addCleanup(sys.modules.pop, name[:-3], None)
        return path

    def _write_env_skipped(self, name="test_gated.py", reasons=(ASSETS,)):
        """A module whose tests all skip with env-marked reasons."""
        body = ["import unittest", "class T(unittest.TestCase):"]
        for i, reason in enumerate(reasons):
            body += [
                f"    @unittest.skip({EnvSkip.reason(reason)!r})",
                f"    def test_gated_{i}(self):",
                "        pass",
            ]
        return self._write(name, "\n".join(body) + "\n")

    def _write_passing(self, name="test_fine.py"):
        return self._write(
            name,
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    def test_ok(self):\n"
            "        self.assertTrue(True)\n",
        )

    def _run(self, **kwargs):
        """Run and capture stdout, so the summary itself can be asserted on."""
        buf = io.StringIO()
        with redirect_stdout(buf):
            ok = self.runner.run_tests(run_all=True, update_badge=False, **kwargs)
        return ok, buf.getvalue()

    def test_summary_prints_a_separate_env_skip_count(self):
        self._write_env_skipped(reasons=(self.ASSETS, self.ASSETS, self.UNITY))
        _ok, out = self._run()
        self.assertIn("SKIPPED (env): 3", out)

    def test_summary_names_what_was_missing(self):
        self._write_env_skipped(reasons=(self.ASSETS, self.UNITY))
        _ok, out = self._run()
        self.assertIn(self.ASSETS, out)
        self.assertIn(self.UNITY, out)

    def test_env_skips_are_not_reported_as_a_plain_pass(self):
        """The whole point: green here would mean 'contract verified'."""
        self._write_env_skipped()
        self._write_passing()
        _ok, out = self._run()
        self.assertIn("[PARTIAL]", out)

    def test_a_clean_run_still_reports_a_plain_pass(self):
        self._write_passing()
        ok, out = self._run()
        self.assertTrue(ok)
        self.assertNotIn("[PARTIAL]", out)
        self.assertIn("SKIPPED (env): 0", out)

    def test_a_plain_skip_is_not_counted_as_environmental(self):
        self._write(
            "test_plain_skip.py",
            "import unittest\n"
            "class T(unittest.TestCase):\n"
            "    @unittest.skip('deliberate, not environmental')\n"
            "    def test_skipped(self):\n"
            "        pass\n",
        )
        _ok, out = self._run()
        self.assertIn("SKIPPED (env): 0", out)
        self.assertNotIn("[PARTIAL]", out)

    def test_env_skips_refuse_the_badge_stamp(self):
        """A count that never covered the gated slice is not coverage."""
        self._write_env_skipped()
        self._write_passing()
        with mock.patch.object(UnityTestRunner, "update_readme_badge") as stamp:
            with redirect_stdout(io.StringIO()):
                self.runner.run_tests(run_all=True, update_badge=True)
        stamp.assert_not_called()

    def test_env_skips_alone_do_not_fail_the_run_by_default(self):
        """A dev box without Unity must still be able to run the venv slice."""
        self._write_env_skipped()
        self._write_passing()
        ok, _out = self._run()
        self.assertTrue(ok)

    def test_strict_env_turns_an_env_skip_into_a_failure(self):
        """CI opt-in: the gated contract must actually have run."""
        self._write_env_skipped()
        self._write_passing()
        ok, _out = self._run(strict_env=True)
        self.assertFalse(ok)

    def test_strict_env_is_green_when_nothing_was_env_skipped(self):
        self._write_passing()
        ok, _out = self._run(strict_env=True)
        self.assertTrue(ok)


#: Child program for ``test_reclaim_scratch_does_not_need_a_pre_seeded_sys_path``:
#: load run_tests.py by PATH, so its directory is not auto-added to ``sys.path``
#: the way running it as a script would. ``sweep_stale`` is neutered first --
#: this pins the import ordering, and a test must never reclaim the real
#: ``temp_tests/`` as a side effect.
_PATH_PROBE = """
import importlib.util, sys

spec = importlib.util.spec_from_file_location("rt_probe", r"%s")
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

import pythontk as ptk

ptk.TempArtifacts.sweep_stale = lambda self: []
assert r"%s" not in sys.path, "precondition: test dir already importable"
mod.UnityTestRunner().run_tests(modules=["definitely_not_a_module"])
print("RECLAIM_OK")
"""


class RunnerScratchReclamationTest(unittest.TestCase):
    """The runner reclaims stale scratch runs of EVERY prefix at start-up.

    The integration suites preserve their generated Unity project on purpose,
    so the only lifetime they have is the age-gated sweep that
    ``TempProjects.allocate`` runs -- and that sweep is prefix-scoped. A suite
    that env-skips allocates nothing and a retired suite never allocates
    again, so nobody ever reclaims their runs. The bound is unchanged; only
    who runs the sweep is.
    """

    def setUp(self):
        tmp = tempfile.TemporaryDirectory(prefix="unitytk_reclaim_test_")
        self.addCleanup(tmp.cleanup)
        self.base = tmp.name
        patcher = mock.patch.object(TempProjects, "BASE", self.base)
        patcher.start()
        self.addCleanup(patcher.stop)

    def _stale_run(self, prefix):
        run = TempProjects.allocate(prefix)
        old = time.time() - (TempProjects.MAX_AGE_DAYS + 1) * 86400
        os.utime(run, (old, old))
        return run

    def test_reclaim_scratch_sweeps_a_prefix_no_suite_allocates(self):
        retired = self._stale_run("retired_suite")
        with redirect_stdout(io.StringIO()):
            removed = UnityTestRunner.reclaim_scratch()
        self.assertFalse(os.path.exists(retired))
        self.assertEqual(
            [os.path.normcase(p) for p in removed], [os.path.normcase(retired)]
        )

    def test_reclaim_scratch_keeps_runs_inside_the_age_bound(self):
        """Including the directories the run about to start will allocate."""
        fresh = TempProjects.allocate("shot_meta_integ")
        with redirect_stdout(io.StringIO()):
            self.assertEqual(UnityTestRunner.reclaim_scratch(), [])
        self.assertTrue(os.path.isdir(fresh))

    def test_run_tests_reclaims_before_running_anything(self):
        """Wired into the run, not just available on the class."""
        runner = UnityTestRunner()
        with mock.patch.object(TempProjects, "sweep_all", return_value=[]) as sweep:
            with redirect_stdout(io.StringIO()):
                runner.run_tests(modules=["definitely_not_a_module"])
        sweep.assert_called_once_with()

    def test_reclaim_scratch_does_not_need_a_pre_seeded_sys_path(self):
        """``reclaim_scratch`` imports ``base_test``, which sits beside
        ``run_tests.py`` and is importable only once the runner has put its own
        test dir on ``sys.path``. Loading the runner by file path (no automatic
        script-dir entry) is where that guarantee has to hold, because the
        reclamation is the FIRST thing ``run_tests`` does.
        """
        runner_py = pathlib.Path(__file__).resolve().parent / "run_tests.py"
        proc = subprocess.run(
            [sys.executable, "-c", _PATH_PROBE % (runner_py, runner_py.parent)],
            cwd=str(runner_py.parents[1]),
            capture_output=True,
            text=True,
        )
        self.assertIn("RECLAIM_OK", proc.stdout, proc.stderr)


#: A test module for a fresh run: it records the temp dir at IMPORT time, before
#: any test allocates, and asks whether the process is sandboxed. Asking, not
#: launching: an unguarded ``webbrowser.open`` would open a real tab.
_SANDBOX_PROBE = """\
import os, tempfile, unittest
from pythontk.core_utils.test_sandbox import TestSandbox

SEEN = tempfile.gettempdir()


class Probe(unittest.TestCase):
    def test_the_run_is_sandboxed(self):
        self.assertTrue(TestSandbox.is_active(), "no sandbox")
        parent = os.environ["UNITYTK_PROBE_PARENT_TEMP"]
        self.assertNotEqual(os.path.normcase(SEEN), os.path.normcase(parent))
        self.assertEqual(SEEN, TestSandbox._state["temp_dir"])
"""

#: Drives one run of the probe through the runner in a FRESH interpreter, so a
#: sandbox this test process already has cannot stand in for the runner's own.
_RUNNER_DRIVER = r"""
import importlib.util, os, pathlib, sys
test_dir, scratch = sys.argv[1], sys.argv[2]
sys.path.insert(0, test_dir)
spec = importlib.util.spec_from_file_location(
    "run_tests", os.path.join(test_dir, "run_tests.py")
)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
import base_test

base_test.TempProjects.BASE = os.path.join(scratch, "temp_tests")
runner = mod.UnityTestRunner()
runner.test_dir = pathlib.Path(scratch)
sys.path.insert(0, scratch)
print("RESULT", runner.run_tests(modules=["sandbox_probe"], update_badge=False))
"""


class RunnerSandboxTest(unittest.TestCase):
    """A run keeps its temp files and browser launches off the machine.

    The runner never activated ``TestSandbox``, which every sibling runner
    does before its first temp allocation. A full ``--all`` run therefore
    wrote into the user's real temp dir, and so did every Unity child it
    launched, since they inherit ``TEMP``. Covers both entry points:
    this runner, and a bare ``pytest`` (configured in ``pyproject.toml``),
    which loads ``conftest.py`` and never this runner.
    """

    def setUp(self):
        tmp = tempfile.TemporaryDirectory(prefix="unitytk_sandbox_test_")
        self.addCleanup(tmp.cleanup)
        self.scratch = tmp.name
        with open(
            os.path.join(self.scratch, "test_sandbox_probe.py"), "w", encoding="utf-8"
        ) as fh:
            fh.write(_SANDBOX_PROBE)
        self.test_dir = str(pathlib.Path(__file__).resolve().parent)
        self.env = dict(os.environ, UNITYTK_PROBE_PARENT_TEMP=tempfile.gettempdir())

    def test_the_runner_sandboxes_the_run_before_loading_a_module(self):
        proc = subprocess.run(
            [sys.executable, "-c", _RUNNER_DRIVER, self.test_dir, self.scratch],
            capture_output=True,
            text=True,
            env=self.env,
        )
        self.assertIn("RESULT True", proc.stdout, proc.stdout + proc.stderr)

    def test_a_bare_pytest_run_is_sandboxed_by_the_conftest(self):
        try:
            import pytest  # noqa: F401
        except ImportError:
            self.skipTest("pytest is not installed")
        import pythontk as ptk

        # Beside conftest.py, which is what is under test. Handed the file by
        # path: pytest still collects it, while neither collector's own
        # discovery ever sees an underscore-named module; a killed run's copy
        # goes to the store's age sweep.
        store = ptk.TempArtifacts("_sandbox_probe", policy="scoped", dir=self.test_dir)
        self.addCleanup(store.cleanup)
        probe = store.path(extension=".py")
        with open(probe, "w", encoding="utf-8") as fh:
            fh.write(_SANDBOX_PROBE)
        proc = subprocess.run(
            [sys.executable, "-m", "pytest", probe, "-q", "-p", "no:cacheprovider"],
            capture_output=True,
            text=True,
            cwd=str(pathlib.Path(self.test_dir).parent),
            env=self.env,
        )
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        self.assertIn("1 passed", proc.stdout)


class UnityReceiptTest(unittest.TestCase):
    """The release receipt: refused for anything less than a full strict run on a
    clean tree, posted as the 'unity' status on HEAD through gh; nothing reaches
    GitHub here (git and gh are mocked)."""

    def _git(self, status="", sha="abc123", remote="https://github.com/m3trik/unitytk.git"):
        answers = {"status": status, "rev-parse": sha, "remote": remote}
        return mock.patch.object(UnityReceipt, "_git", side_effect=lambda *a: answers[a[0]])

    def test_only_a_full_strict_run_on_a_clean_tree_may_post(self):
        with self._git():
            self.assertIsNone(UnityReceipt.refusal(True, True, []))
            self.assertIn("--all --strict-env", UnityReceipt.refusal(True, False, []))
            self.assertIn("--all --strict-env", UnityReceipt.refusal(False, True, []))
            self.assertIn("-k", UnityReceipt.refusal(True, True, ["shadow"]))
        with self._git(status=" M unitytk/project_builder.py"):
            self.assertIn("commit", UnityReceipt.refusal(True, True, []))

    def test_the_status_is_posted_on_head_through_gh(self):
        with self._git(), mock.patch("subprocess.run") as run:
            self.assertEqual(UnityReceipt.post("full run"), "abc123")
        argv = run.call_args[0][0]
        self.assertEqual(argv[:5], ["gh", "api", "-X", "POST", "repos/m3trik/unitytk/statuses/abc123"])
        self.assertIn("context=unity", argv)
        self.assertIn("state=success", argv)

    def test_a_missing_gh_says_how_to_get_it(self):
        with self._git(), mock.patch("subprocess.run", side_effect=FileNotFoundError):
            with self.assertRaisesRegex(RuntimeError, "gh auth login"):
                UnityReceipt.post("full run")

    def _release(self, argv, suite=True, edit_mode=(True, True)):
        """``main(argv)`` with the suite, Edit Mode and the post stood in for:
        its exit code, the pipelines Edit Mode ran on, the post mock."""
        import run_tests

        verdicts = dict(zip(UnityReceipt.PIPELINES, edit_mode))
        ran = []

        def edit(test_filter=None, pipeline="urp"):
            ran.append(pipeline)
            return verdicts[pipeline]

        out = io.StringIO()
        with mock.patch.object(UnityTestRunner, "run_tests", return_value=suite), mock.patch.object(
            EditMode, "run", side_effect=edit
        ), mock.patch.object(UnityReceipt, "refusal", return_value=None), mock.patch.object(
            UnityReceipt, "post", return_value="abc123"
        ) as post, redirect_stdout(out):
            code = run_tests.main(argv)
        return code, ran, post, out.getvalue()

    def test_the_receipt_runs_edit_mode_on_urp_and_builtin_before_it_posts(self):
        """``--all --strict-env --post-status`` ran the Python modules alone,
        so the status branch protection reads was posted with no Edit Mode
        test run. Added: 2026-11-08"""
        code, ran, post, text = self._release(["--all", "--strict-env", "--post-status"])
        self.assertEqual(code, 0, text)
        self.assertEqual(ran, ["urp", "builtin"])
        post.assert_called_once()
        self.assertIn("Edit Mode", post.call_args[0][0])

    def test_a_failed_edit_mode_run_posts_nothing(self):
        """Added: 2026-11-08"""
        code, ran, post, text = self._release(
            ["--all", "--strict-env", "--post-status"], edit_mode=(True, False)
        )
        self.assertEqual(code, 1)
        self.assertEqual(ran, ["urp", "builtin"], "each pipeline reports")
        post.assert_not_called()
        self.assertIn("builtin", text)

    def test_a_failed_suite_posts_nothing_and_runs_no_editor(self):
        """Added: 2026-11-08"""
        code, ran, post, _ = self._release(["--all", "--strict-env", "--post-status"], suite=False)
        self.assertEqual((code, ran), (1, []))
        post.assert_not_called()

    def test_without_the_receipt_the_full_run_starts_no_editor(self):
        """CI runs ``--all`` with no Unity: Edit Mode belongs to the receipt.
        Added: 2026-11-08"""
        code, ran, post, _ = self._release(["--all", "--strict-env"])
        self.assertEqual((code, ran), (0, []))
        post.assert_not_called()

    def test_edit_mode_alone_cannot_post_the_receipt(self):
        """``--edit-mode --post-status`` ignored the flag. Added: 2026-11-08"""
        import run_tests

        with mock.patch.object(EditMode, "run") as edit, mock.patch.object(UnityReceipt, "post") as post, redirect_stdout(
            io.StringIO()
        ) as out:
            code = run_tests.main(["--edit-mode", "--post-status"])
        self.assertEqual(code, 1)
        edit.assert_not_called()
        post.assert_not_called()
        self.assertIn("--all --strict-env", out.getvalue())

    def test_git_runs_in_this_repo(self):
        """``_git`` used ``os``, which run_tests never imported (ruff F821):
        the receipt's first call raised NameError. Read-only.
        Added: 2026-11-04"""
        try:
            top = UnityReceipt._git("rev-parse", "--show-toplevel")
        except (FileNotFoundError, subprocess.CalledProcessError) as error:
            self.skipTest(EnvSkip.reason(f"no git checkout here ({error})"))
        repo = pathlib.Path(__file__).resolve().parents[1]
        self.assertEqual(os.path.normcase(str(pathlib.Path(top).resolve())), os.path.normcase(str(repo)))


class EditModeTest(unittest.TestCase):
    """``--edit-mode``: the package's Edit Mode tests, through
    ``ProjectBuilder.test`` (a batch editor on the owned project), tallied as
    the workspace's ``affected_tests.py`` reads a runner, the exit code the
    verdict. Nothing reaches Unity here (``ProjectBuilder.test`` is mocked)."""

    OUTCOME = {
        "total": 4, "passed": 2, "failed": 1, "skipped": 0, "inconclusive": 1,
        "failures": [{"name": "Kernel.Json.Writes", "message": "expected 1"}],
        "inconclusives": [{"name": "Lightmaps.BuiltIn", "message": "URP project"}],
        "ok": False, "log": "editor.log",
    }

    def _run(self, outcome=None, error=None, **kwargs):
        from unitytk.project_builder import ProjectBuilder

        out = io.StringIO()
        with mock.patch.object(
            ProjectBuilder, "test", return_value=outcome, side_effect=error
        ) as test, redirect_stdout(out):
            ok = EditMode.run(**kwargs)
        return ok, out.getvalue(), test

    def test_a_failure_fails_the_run_and_names_the_case(self):
        ok, text, test = self._run(self.OUTCOME)
        self.assertFalse(ok)
        test.assert_called_once_with(platform="EditMode", test_filter=None, pipeline="urp")
        self.assertIn("Total: 4 tests, 2 passed, 1 failed, 0 skipped, 1 inconclusive", text)
        self.assertIn("failed: Kernel.Json.Writes: expected 1", text)
        self.assertIn("inconclusive: Lightmaps.BuiltIn: URP project", text)

    def test_a_clean_run_passes_and_hands_on_the_filter_and_pipeline(self):
        clean = dict(self.OUTCOME, passed=4, failed=0, inconclusive=0, failures=[], inconclusives=[], ok=True)
        ok, text, test = self._run(clean, test_filter="Json", pipeline="builtin")
        self.assertTrue(ok)
        test.assert_called_once_with(platform="EditMode", test_filter="Json", pipeline="builtin")
        self.assertIn("Total: 4 tests, 4 passed, 0 failed", text)

    def test_a_run_unity_could_not_make_is_a_failure_that_says_why(self):
        ok, text, _ = self._run(error=RuntimeError("an editor has the project open"))
        self.assertFalse(ok)
        self.assertIn("Edit Mode did not run: an editor has the project open", text)
        # The owned project's folder too deep for URP: the fix said, no traceback (added 2026-11-08).
        ok, text, _ = self._run(error=ValueError("too deep: name a shorter folder"))
        self.assertFalse(ok)
        self.assertIn("Edit Mode did not run: too deep", text)


if __name__ == "__main__":
    unittest.main()
