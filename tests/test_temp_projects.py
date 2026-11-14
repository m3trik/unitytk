# !/usr/bin/python
# coding=utf-8
"""Tests for :class:`base_test.TempProjects` (no Unity needed: Unity's CLI is a stand-in).

A suite's run is the suite's: allocated with its class, it goes when the class
is done, pass or fail, unless ``UNITYTK_PRESERVE=1`` keeps it
(``SuiteLifetimeTest``). Before 2026-10-20 the suites kept every run on purpose
and only the age sweep reclaimed any -- 75 runs and 10 GB, a full drive. The
sweep still reclaims what a killed run, a held file or a preserved run leaves,
and these tests pin the three properties that keep it honest:

* a stale run is reclaimed, so ``temp_tests/`` cannot grow without bound (it had
  reached 3.9 GB across 81 preserved runs when this was written);
* a fresh run is never touched, so a project still being inspected survives a
  later suite running in the same directory;
* the sweep is prefix-scoped, so one suite cannot delete another's current run --
  every suite shares the single ``temp_tests/`` base.

That prefix scoping leaves one hole, which :meth:`TempProjects.sweep_all` and
the runner's start-up call close: reclamation is a side effect of ALLOCATION,
so a prefix nobody allocates again is never swept. Suites that env-skip (no
Unity/assets) allocate nothing, and a renamed or retired suite never
allocates again -- their runs sit there forever (750 MB across 50 runs when
this was written). The bound stays the same; only who runs the sweep changes.
"""
import os
import tempfile
import time
import unittest
from unittest import mock

import base_test
from base_test import TempProjects
from unitytk.unity_cli import UnityCli


class _Hub:
    """Unity's CLI as far as the Hub's project list goes: *listed* folders,
    and the ones taken out of it."""

    def __init__(self, *listed):
        self.listed, self.removed = list(listed), []

    def projects(self):
        return list(self.listed)

    def unregister(self, path):
        self.removed.append(path)
        return True

    def patch(self, test):
        for name in ("projects", "unregister"):
            patcher = mock.patch.object(UnityCli, name, side_effect=getattr(self, name))
            patcher.start()
            test.addCleanup(patcher.stop)
        return self

#: The default base, whatever this run's environment names (UNITYTK_TEMP_PROJECTS).
DEFAULT_BASE = TempProjects.DEFAULT_BASE


class TempProjectsTest(unittest.TestCase):
    """Allocation + age-gated reclamation under a sandboxed base directory."""

    def setUp(self):
        # Sandbox the base so this suite never touches the real temp_tests/.
        tmp = tempfile.TemporaryDirectory(prefix="tempprojects_test_")
        self.addCleanup(tmp.cleanup)
        self.base = tmp.name
        patcher = mock.patch.object(TempProjects, "BASE", self.base)
        patcher.start()
        self.addCleanup(patcher.stop)
        self.hub = _Hub().patch(self)

    def _age(self, path, days):
        """Backdate *path* AND everything under it by *days*, so the sweep
        sees it as stale.

        The whole tree, not just the top entry: ``TempArtifacts`` reads a
        directory as being as fresh as its newest child (a file rewritten in
        place never touches its parent's mtime), so backdating the parent
        alone leaves a populated run reading as current and nothing is swept.
        """
        old = time.time() - days * 86400
        for root, dirs, files in os.walk(path, topdown=False):
            for name in dirs + files:
                os.utime(os.path.join(root, name), (old, old))
        os.utime(path, (old, old))

    def test_allocate_creates_a_prefixed_dir_under_base(self):
        run = TempProjects.allocate("shot_meta_integ")
        self.assertTrue(os.path.isdir(run))
        self.assertEqual(os.path.dirname(run), self.base)
        self.assertTrue(os.path.basename(run).startswith("shot_meta_integ_"))

    def test_allocations_are_distinct(self):
        first = TempProjects.allocate("shot_meta_integ")
        second = TempProjects.allocate("shot_meta_integ")
        self.assertNotEqual(first, second)
        self.assertTrue(os.path.isdir(first))
        self.assertTrue(os.path.isdir(second))

    def test_stale_run_is_swept(self):
        """A run older than MAX_AGE_DAYS is reclaimed by the next allocation."""
        stale = TempProjects.allocate("shot_meta_integ")
        # Populate it: the real leftovers are whole Unity projects, so this
        # covers the non-empty tree a plain os.rmdir would silently fail on.
        os.makedirs(os.path.join(stale, "Assets", "Editor"), exist_ok=True)
        with open(os.path.join(stale, "Assets", "Editor", "V.cs"), "w") as fh:
            fh.write("// verifier")
        self._age(stale, TempProjects.MAX_AGE_DAYS + 1)

        TempProjects.allocate("shot_meta_integ")
        self.assertFalse(os.path.exists(stale))

    def test_fresh_run_is_preserved(self):
        """A recent run survives -- it may still be under inspection."""
        fresh = TempProjects.allocate("shot_meta_integ")
        TempProjects.allocate("shot_meta_integ")
        self.assertTrue(os.path.isdir(fresh))

    def test_sweep_is_prefix_scoped(self):
        """One suite's allocation must not reclaim another suite's run.

        Every integration suite shares one temp_tests/ base, so an unscoped
        age sweep would delete a sibling suite's project mid-run.
        """
        other = TempProjects.allocate("animation_audio_integ")
        self._age(other, TempProjects.MAX_AGE_DAYS + 1)

        TempProjects.allocate("shot_meta_integ")
        self.assertTrue(os.path.isdir(other))

    def test_allocate_creates_a_missing_base(self):
        """A fresh clone has no temp_tests/ -- allocation must still work.

        The suites each used to call ``os.makedirs(base, exist_ok=True)``
        before allocating; centralizing removed those lines, so the guarantee
        has to hold here or the first run on a clean checkout dies in
        setUpClass.
        """
        missing = os.path.join(self.base, "not_yet", "temp_tests")
        self.assertFalse(os.path.exists(missing))
        with mock.patch.object(TempProjects, "BASE", missing):
            run = TempProjects.allocate("shot_meta_integ")
        self.assertTrue(os.path.isdir(run))
        self.assertEqual(os.path.dirname(run), missing)

    # ------------------------------------------------------------- sweep_all
    def test_sweep_all_reclaims_a_prefix_that_is_never_allocated_again(self):
        """The gap: allocation sweeps only its OWN prefix, so a suite that
        env-skips (or was renamed away) leaves its runs there forever."""
        retired = TempProjects.allocate("retired_suite")
        os.makedirs(os.path.join(retired, "Assets", "Editor"), exist_ok=True)
        with open(os.path.join(retired, "Assets", "Editor", "V.cs"), "w") as fh:
            fh.write("// verifier")
        self._age(retired, TempProjects.MAX_AGE_DAYS + 1)

        TempProjects.allocate("shot_meta_integ")  # a DIFFERENT prefix
        self.assertTrue(os.path.isdir(retired), "precondition: allocation missed it")

        removed = TempProjects.sweep_all()
        self.assertFalse(os.path.exists(retired))
        self.assertIn(os.path.normcase(retired), [os.path.normcase(p) for p in removed])

    def test_sweep_all_honors_the_same_age_bound(self):
        """A run inside the bound survives -- including the one the suite
        running right now just allocated, whose mtime is seconds old."""
        current = TempProjects.allocate("shot_meta_integ")
        old = TempProjects.allocate("retired_suite")
        self._age(old, TempProjects.MAX_AGE_DAYS + 1)

        removed = TempProjects.sweep_all()
        self.assertTrue(os.path.isdir(current))
        self.assertEqual(
            [os.path.normcase(p) for p in removed], [os.path.normcase(old)]
        )

    def test_sweep_all_reclaims_runs_of_either_project_layout(self):
        """A run left before the short layout (``<run>/urp/UnityProject``, under
        a since-shortened prefix) and one after it (``<run>/urp``) are the same
        allocation to the sweep: it judges the run, never what is inside."""
        old = TempProjects.allocate("multi_section_audio_integ")
        new = TempProjects.allocate("multi_section_audio")
        for project in (os.path.join(old, "urp", "UnityProject"), TempProjects.project(new, "urp")):
            os.makedirs(os.path.join(project, "Assets"))
            with open(os.path.join(project, "Assets", "V.cs"), "w") as fh:
                fh.write("// verifier")
        for run in (old, new):
            self._age(run, TempProjects.MAX_AGE_DAYS + 1)

        TempProjects.sweep_all()
        self.assertFalse(os.path.exists(old))
        self.assertFalse(os.path.exists(new))

    def test_sweep_all_leaves_a_hand_made_scratch_dir_alone(self):
        """Only ``<prefix>_<hex-tag>`` entries are allocations. A hand-made
        reproduction dir under temp_tests/ is somebody's working file, not a
        leftover -- age says nothing about whether it is owned."""
        probe = os.path.join(self.base, "vis_probe")
        os.makedirs(probe)
        with open(os.path.join(probe, "repro.py"), "w") as fh:
            fh.write("# hand-written\n")
        self._age(probe, 365)

        self.assertEqual(TempProjects.sweep_all(), [])
        self.assertTrue(os.path.isdir(probe))

    def test_sweep_all_no_ops_on_a_missing_base(self):
        """A fresh clone has no temp_tests/; the runner still starts."""
        missing = os.path.join(self.base, "not_yet", "temp_tests")
        with mock.patch.object(TempProjects, "BASE", missing):
            self.assertEqual(TempProjects.sweep_all(), [])

    def test_base_defaults_to_repo_temp_tests(self):
        """The default base is the in-repo scratch dir, not the system temp.

        These artifacts are deliberately durable and large; the repo lives on a
        roomy drive while C: is routinely full, and .gitignore covers this path.
        """
        expected = os.path.join(os.path.dirname(base_test.__file__), "temp_tests")
        self.assertEqual(
            os.path.normcase(os.path.normpath(DEFAULT_BASE)),
            os.path.normcase(os.path.normpath(expected)),
        )
        self.assertEqual(TempProjects.base_for({}), DEFAULT_BASE)

    def test_the_environment_names_another_base(self):
        """``UNITYTK_TEMP_PROJECTS`` moves the base to a roomier drive (a URP
        project is ~2 GB); empty, it is the default."""
        self.assertEqual(TempProjects.base_for({"UNITYTK_TEMP_PROJECTS": r"D:\scratch"}), r"D:\scratch")
        self.assertEqual(TempProjects.base_for({"UNITYTK_TEMP_PROJECTS": ""}), DEFAULT_BASE)


class SuiteLifetimeTest(unittest.TestCase):
    """A suite's run folder is the suite's to remove.

    Every Unity integration suite allocates in ``setUpClass`` and used to keep
    the run on purpose, so only the 3-day age sweep reclaimed anything: 75 runs
    and 10 GB on 2026-10-20, a full O: and a production module truncated to 0
    bytes. Handed the suite's class (*owner*), :meth:`TempProjects.allocate`
    removes the folder when the class is done -- a passing suite, a failing one,
    a ``setUpClass`` that raised after allocating (a batch Unity that timed out)
    -- and keeps it only when ``UNITYTK_PRESERVE=1`` asks, saying so.
    """

    def setUp(self):
        tmp = tempfile.TemporaryDirectory(prefix="tempprojects_life_")
        self.addCleanup(tmp.cleanup)
        self.base = tmp.name
        patcher = mock.patch.object(TempProjects, "BASE", self.base)
        patcher.start()
        self.addCleanup(patcher.stop)
        env = mock.patch.dict(os.environ, {base_test.SuiteOptions.PRESERVE: ""})
        env.start()
        self.addCleanup(env.stop)
        self.hub = _Hub().patch(self)

    def _run(self, outcome):
        """Run a suite shaped like every Unity integration suite; return its
        result and the run folders it left."""
        prefix = "life_" + outcome
        seen = []
        hub = self.hub

        class Suite(unittest.TestCase):
            @classmethod
            def setUpClass(cls):
                cls.temp_dir = TempProjects.allocate(prefix, cls)
                seen.append(cls.temp_dir)
                with open(os.path.join(cls.temp_dir, "unity_log.txt"), "w") as fh:
                    fh.write("a project's worth of files")
                os.makedirs(os.path.join(cls.temp_dir, "p", "Library"))
                os.makedirs(os.path.join(cls.temp_dir, "p", "ProjectSettings"))
                hub.listed.append(os.path.join(cls.temp_dir, "p"))
                if outcome == "setup_raises":
                    raise RuntimeError("Unity timed out")

            def test_it(self):
                self.assertTrue(os.path.isdir(type(self).temp_dir))
                if outcome == "fails":
                    self.fail("a batch Unity wrote no results")

        result = unittest.TestResult()
        unittest.defaultTestLoader.loadTestsFromTestCase(Suite).run(result)
        self.assertEqual(len(seen), 1, f"the suite allocated nothing: {result.errors}")
        left = sorted(n for n in os.listdir(self.base) if n.startswith(prefix + "_"))
        return result, left

    def test_a_passing_suite_leaves_no_run_folder(self):
        result, left = self._run("passes")
        self.assertEqual(
            (result.testsRun, len(result.failures), len(result.errors)),
            (1, 0, 0),
            result.errors,
        )
        self.assertEqual(left, [])

    def test_a_failing_suite_leaves_no_run_folder_either(self):
        result, left = self._run("fails")
        self.assertEqual((result.testsRun, len(result.failures)), (1, 1))
        self.assertEqual(left, [])

    def test_a_setUpClass_that_raises_after_allocating_leaves_none(self):
        result, left = self._run("setup_raises")
        self.assertEqual(len(result.errors), 1, result.errors)
        self.assertIn("Unity timed out", result.errors[0][1])
        self.assertEqual(left, [])

    def test_preserve_keeps_the_run_folder_and_says_so(self):
        with mock.patch.dict(os.environ, {base_test.SuiteOptions.PRESERVE: "1"}):
            with self.assertLogs("base_test", level="WARNING") as said:
                result, left = self._run("passes")
        self.assertTrue(result.wasSuccessful(), result.errors)
        self.assertEqual(len(left), 1)
        self.assertIn(base_test.SuiteOptions.PRESERVE, "\n".join(said.output))
        self.assertIn(left[0], "\n".join(said.output))

    def test_a_released_run_leaves_the_hubs_project_list_and_nothing_else_does(self):
        """``unity projects new`` lists each project in the Hub, and nothing
        took a run's out again: the list filled with folders long gone.
        Added: 2026-11-04"""
        elsewhere = os.path.join(self.base + "_not_ours", "p")
        self.hub.listed.append(elsewhere)
        result, left = self._run("passes")
        self.assertEqual(left, [])
        self.assertEqual(self.hub.removed, [self.hub.listed[-1]], "the run's project, once its folder went")
        self.assertNotIn(elsewhere, self.hub.removed, "a folder outside the runs is never touched")

    def test_a_preserved_run_stays_listed_and_the_sweep_takes_it_out_when_it_goes(self):
        """Added: 2026-11-04"""
        with mock.patch.dict(os.environ, {base_test.SuiteOptions.PRESERVE: "1"}):
            with self.assertLogs("base_test", level="WARNING"):
                _result, left = self._run("passes")
        self.assertEqual(self.hub.removed, [], "a kept project is still on disk")
        kept = os.path.join(self.base, left[0])
        still = os.path.join(self.base, "still_here", "p")
        os.makedirs(still)
        self.hub.listed.append(still)
        import shutil

        shutil.rmtree(kept)
        TempProjects.sweep_all()
        self.assertEqual(self.hub.removed, [os.path.join(kept, "p")])

    def test_without_the_cli_the_list_is_left_alone(self):
        """Added: 2026-11-04"""
        with mock.patch.object(UnityCli, "projects", side_effect=FileNotFoundError("no cli")):
            self.assertEqual(TempProjects.forget(self.base), [])

    def test_the_runs_owned_projects_are_a_run_of_its_own(self):
        """Below the sandbox's root a URP project is too deep for the editor:
        the run's owned project lives in a run under the base, released at exit.
        Added: 2026-11-04"""
        from unitytk.project_builder import ProjectBuilder

        with mock.patch.dict(os.environ, {ProjectBuilder.ENV_VAR: ""}), mock.patch("atexit.register") as at_exit:
            folder = TempProjects.owned()
            self.assertEqual(os.environ[ProjectBuilder.ENV_VAR], folder)
            self.assertTrue(os.path.basename(folder).startswith("own_"))
            self.assertEqual(os.path.dirname(folder), self.base)
            self.assertEqual(at_exit.call_args[0][0], TempProjects.release)
            self.assertEqual(TempProjects.owned(), folder, "a folder the environment names is kept")

    def _held(self, seconds):
        """A run folder holding a project, one of whose files a stand-in for an
        editor holds open for *seconds* (an exiting batch Unity's
        ``Library/ArtifactDB``): Windows refuses to remove it until then."""
        import threading

        store = TempProjects._store("held")
        path = store.dir_path()
        os.makedirs(os.path.join(path, "p", "ProjectSettings"))
        os.makedirs(os.path.join(path, "p", "Library"))
        held = open(os.path.join(path, "p", "Library", "ArtifactDB"), "wb")
        held.write(b"lmdb")
        held.flush()
        timer = threading.Timer(seconds, held.close)
        timer.start()
        self.addCleanup(timer.cancel)
        self.addCleanup(held.close)
        return store, path

    @unittest.skipUnless(os.name == "nt", "Windows refuses to remove a file a process holds open")
    def test_a_release_waits_for_the_editor_holding_the_run_to_let_go(self):
        """At the end of a run the owned project's ``Library/ArtifactDB`` was
        still held by a Unity that was exiting (WinError 32), and three tries
        two seconds apart gave up, leaving the project for the age sweep. The
        release waits for the holder to let go, then removes the folder.
        Added: 2026-11-05"""
        store, path = self._held(1.0)
        with mock.patch.object(TempProjects, "RELEASE_PAUSE_SECONDS", 0.1):
            self.assertTrue(TempProjects.release(store, path), "removed once the holder let go")
        self.assertFalse(os.path.exists(path))

    @unittest.skipUnless(os.name == "nt", "Windows refuses to remove a file a process holds open")
    def test_a_release_at_exit_never_logs_to_a_closed_stream(self):
        """At interpreter exit the release runs after the run's log streams
        have closed: its warning raised ``ValueError: I/O operation on closed
        file`` from inside logging. At exit it writes to the process's own
        stderr, guarded. Added: 2026-11-05"""
        import io
        import logging

        store, path = self._held(30.0)
        closed = io.StringIO()
        handler = logging.StreamHandler(closed)
        closed.close()
        log = logging.getLogger("base_test")
        log.addHandler(handler)
        self.addCleanup(log.removeHandler, handler)
        from unitytk.project_builder import ProjectBuilder

        with mock.patch.dict(os.environ, {ProjectBuilder.ENV_VAR: ""}), mock.patch("atexit.register") as at_exit:
            TempProjects.owned()
        release, *args = at_exit.call_args[0]
        stderr, exit_stderr = io.StringIO(), io.StringIO()
        with mock.patch.object(TempProjects, "RELEASE_PAUSE_SECONDS", 0.05), mock.patch.object(
            TempProjects, "RELEASE_WAIT_SECONDS", 0.2
        ), mock.patch("sys.stderr", stderr), mock.patch("sys.__stderr__", exit_stderr):
            # The owned run's own registration, handed a held run instead.
            release(store, path, *args[2:])
        self.assertNotIn("closed file", stderr.getvalue())
        self.assertIn(path, exit_stderr.getvalue(), "said where the process can still read it")

    def test_every_suite_allocation_names_its_owner(self):
        """Every suite hands ``TempProjects.allocate`` its class, so its run
        folder goes with it. Read off the suites' source: a new suite that
        forgets is caught here, not by a full drive."""
        import ast

        here = os.path.dirname(os.path.abspath(__file__))
        # The allocator's own tests allocate without an owner on purpose.
        exempt = {"test_temp_projects.py", "test_run_tests.py"}
        unowned = []
        for name in sorted(os.listdir(here)):
            is_test = name.startswith("test_") and name.endswith(".py")
            if not is_test or name in exempt:
                continue
            with open(os.path.join(here, name), encoding="utf-8") as fh:
                tree = ast.parse(fh.read())
            for node in ast.walk(tree):
                if (
                    isinstance(node, ast.Call)
                    and isinstance(node.func, ast.Attribute)
                    and node.func.attr == "allocate"
                    and isinstance(node.func.value, ast.Name)
                    and node.func.value.id == "TempProjects"
                    and len(node.args) < 2
                    and not any(k.arg == "owner" for k in node.keywords)
                ):
                    unowned.append(f"{name}:{node.lineno}")
        self.assertEqual(unowned, [], "suites that allocate without their class")


if __name__ == "__main__":
    unittest.main()
