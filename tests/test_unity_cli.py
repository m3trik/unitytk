# !/usr/bin/python
# coding=utf-8
"""Tests for ``unitytk.UnityCli``: Unity's own command line, read through its JSON.

The stand-in answers as the real CLI does (``unity editors -i --json``,
``unity projects new ... --json``, ``unity projects remove ... --json``,
measured 2026-11-04 on CLI 1.0.0-beta.13 with Unity 6000.3.10f1);
:class:`RealCliTest` reads the real one where it is installed (read-only).
"""

import io
import json
import os
import subprocess
import sys
import unittest
from unittest import mock

try:
    from .base_test import EnvSkip
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import EnvSkip

from unitytk.unity_cli import UnityCli


def _answer(data=None, success=True, errors=()):
    """What the CLI prints for one command, as bytes."""
    return json.dumps(
        {"success": success, "command": "x", "data": data, "errors": list(errors), "warnings": []}
    ).encode("utf-8")


class _Child:
    """A ``subprocess.Popen`` stand-in: answers *stdout* after *polls* looks."""

    def __init__(self, stdout=b"", returncode=0, polls=0):
        self.stdout, self.returncode, self.polls, self.pid = stdout, returncode, polls, 4242
        self.killed = False

    def communicate(self, timeout=None):
        if self.polls > 0 and not self.killed:
            self.polls -= 1
            raise subprocess.TimeoutExpired("unity", timeout)
        return self.stdout, b""

    def poll(self):
        return -9 if self.killed else None

    def kill(self):
        self.killed = True


def _spawn(child):
    """Patch ``subprocess.Popen`` to hand back *child*; the mock records the call."""
    return mock.patch("subprocess.Popen", return_value=child)


def _no_stop():
    """Stand in for the stop's ``taskkill`` (Windows) and ``killpg`` (elsewhere):
    the stand-in child's pid is no real process's to end."""
    stack = __import__("contextlib").ExitStack()
    taskkill = stack.enter_context(mock.patch("subprocess.run"))
    stack.enter_context(mock.patch("os.killpg", create=True))
    return stack, taskkill


class UnityCliTest(unittest.TestCase):
    """Added: 2026-11-04"""

    def setUp(self):
        patcher = mock.patch.object(UnityCli, "exe", return_value="unity")
        patcher.start()
        self.addCleanup(patcher.stop)
        # The stand-in's listings fill the process-wide cache: put back the real one.
        self.addCleanup(setattr, UnityCli, "_editors", UnityCli._editors)

    def test_editors_are_the_installed_versions_and_their_executables(self):
        data = [
            {"version": "6000.3.10f1", "location": r"C:\U\6000.3.10f1\Editor\Unity.exe"},
            {"version": "2022.3.4f1", "location": r"C:\U\2022.3.4f1\Editor\Unity.exe"},
        ]
        with _spawn(_Child(_answer(data))) as spawned:
            editors = UnityCli.editors(refresh=True)
        self.assertEqual(editors["6000.3.10f1"], r"C:\U\6000.3.10f1\Editor\Unity.exe")
        args = spawned.call_args[0][0]
        self.assertEqual(args[:3], ["unity", "editors", "-i"])
        self.assertIn("--json", args)
        self.assertIn("--non-interactive", args)

    def test_editors_are_read_once_a_process_until_refreshed(self):
        """Each call spawned the CLI (0.4 s), up to three times per operation call."""
        first = [{"version": "6000.3.10f1", "location": "C:/U/a/Unity.exe"}]
        second = first + [{"version": "6000.4.1f1", "location": "C:/U/b/Unity.exe"}]
        with _spawn(_Child(_answer(first))) as spawned:
            UnityCli.editors(refresh=True)
            cached = UnityCli.editors()
            cached["edited"] = "by the caller"
            self.assertNotIn("edited", UnityCli.editors(), "a copy is handed out")
        self.assertEqual(spawned.call_count, 1)
        with _spawn(_Child(_answer(second))):
            self.assertIn("6000.4.1f1", UnityCli.editors(refresh=True))

    def test_the_child_has_no_stdin_and_no_console_window(self):
        """A GUI host calls it: no console flashes up and nothing waits on input."""
        with _spawn(_Child(_answer([]))) as spawned:
            UnityCli.run("editors", "-i")
        kwargs = spawned.call_args.kwargs
        self.assertIs(kwargs["stdin"], subprocess.DEVNULL)
        if os.name == "nt":
            self.assertEqual(kwargs["creationflags"], subprocess.CREATE_NO_WINDOW)
        else:
            self.assertTrue(kwargs["start_new_session"])

    def test_a_command_that_outruns_its_timeout_is_stopped_and_raises_runtime_error(self):
        child = _Child(_answer([]), polls=100)
        stop, _ = _no_stop()
        with _spawn(child), mock.patch.object(UnityCli, "POLL", 0.0), stop:
            with self.assertRaisesRegex(RuntimeError, "did not answer within"):
                UnityCli.run("editors", "-i", timeout=0.0)
        self.assertTrue(child.killed)

    def test_the_editors_listing_has_a_short_timeout_of_its_own(self):
        """A GUI panel waits on it while it builds: a hung CLI held it two
        minutes on the default timeout. Added: 2026-11-04"""
        self.assertLessEqual(UnityCli.EDITORS_TIMEOUT, 15.0)
        with mock.patch.object(UnityCli, "run", return_value=[]) as ran:
            UnityCli.editors(refresh=True)
        self.assertEqual(ran.call_args.kwargs["timeout"], UnityCli.EDITORS_TIMEOUT)
        child = _Child(_answer([]), polls=100)
        stop, _ = _no_stop()
        with _spawn(child), mock.patch.object(UnityCli, "POLL", 0.0), mock.patch.object(
            UnityCli, "EDITORS_TIMEOUT", 0.0
        ), stop:
            with self.assertRaisesRegex(RuntimeError, "did not answer within"):
                UnityCli.editors(refresh=True)
        self.assertTrue(child.killed)

    def test_a_cancel_stops_the_command_and_propagates(self):
        class Cancelled(BaseException):
            pass

        def cancel():
            raise Cancelled()

        child = _Child(_answer({"path": "C:/P"}), polls=3)
        stop, taskkill = _no_stop()
        with _spawn(child), stop:
            with self.assertRaises(Cancelled):
                UnityCli.create_project("C:/work/P", cancel=cancel)
        self.assertTrue(child.killed)
        if os.name == "nt":
            self.assertEqual(taskkill.call_args[0][0][:3], ["taskkill", "/T", "/F"], "the editor it started too")

    def test_an_interrupt_while_it_waits_stops_the_command_and_propagates(self):
        """A Ctrl-C lands in ``communicate`` (where the wait is spent), not in
        *cancel*: it skipped the stop, leaving the CLI and the editor it
        started running. Added: 2026-11-08"""

        class Interrupted(_Child):
            def communicate(self, timeout=None):
                if not self.killed:
                    raise KeyboardInterrupt()
                return self.stdout, b""

        child = Interrupted(_answer({"path": "C:/P"}))
        stop, taskkill = _no_stop()
        with _spawn(child), stop:
            with self.assertRaises(KeyboardInterrupt):
                UnityCli.create_project("C:/work/P")
        self.assertTrue(child.killed)
        if os.name == "nt":
            self.assertEqual(taskkill.call_args[0][0][:3], ["taskkill", "/T", "/F"], "the editor it started too")

    def test_a_failed_command_raises_with_the_clis_own_message(self):
        failed = _answer(success=False, errors=[{"code": "E", "message": "Unknown template"}])
        with _spawn(_Child(failed, 1)):
            with self.assertRaisesRegex(RuntimeError, "Unknown template"):
                UnityCli.run("projects", "new", "P")

    def test_output_that_is_not_json_raises_naming_the_command(self):
        with _spawn(_Child(b"Usage: unity", 1)):
            with self.assertRaisesRegex(RuntimeError, "editors"):
                UnityCli.editors(refresh=True)

    def test_without_the_cli_it_says_how_to_get_it(self):
        with mock.patch.object(UnityCli, "exe", return_value=None):
            with self.assertRaisesRegex(FileNotFoundError, "Unity Hub"):
                UnityCli.editors(refresh=True)

    def test_without_localappdata_no_relative_location_is_searched(self):
        with mock.patch.dict(os.environ, {}, clear=False):
            os.environ.pop("LOCALAPPDATA", None)
            self.assertTrue(all(os.path.isabs(p) for p in UnityCli.locations()), UnityCli.locations())
        with mock.patch.dict(os.environ, {"LOCALAPPDATA": "C:/Local"}):
            self.assertEqual(UnityCli.locations()[0], os.path.join("C:/Local", "Unity", "bin", "unity.exe"))

    def test_versions_sort_by_number_not_text(self):
        versions = ["2022.3.9f1", "2022.3.10f1", "6000.3.10f1", "6000.0.26f1"]
        self.assertEqual(
            sorted(versions, key=UnityCli.version_key),
            ["2022.3.9f1", "2022.3.10f1", "6000.0.26f1", "6000.3.10f1"],
        )
        self.assertEqual(UnityCli.version_key("6000.3.10f1")[:3], (6000, 3, 10))

    def test_a_project_is_made_in_its_parent_with_its_version_and_template(self):
        # This platform's own absolute path: a Windows one does not split on Linux.
        demo = os.path.join(os.path.abspath(os.sep), "work", "Demo")
        made = {"path": demo, "version": "6000.3.10f1"}
        with _spawn(_Child(_answer(made))) as spawned:
            path = UnityCli.create_project(demo, version="6000.3.10f1", template="com.unity.template.urp-blank")
        self.assertEqual(path, demo)
        args = spawned.call_args[0][0]
        self.assertEqual(args[1:4], ["projects", "new", "Demo"])
        self.assertEqual(args[args.index("--path") + 1], os.path.dirname(demo))
        self.assertEqual(args[args.index("--editor-version") + 1], "6000.3.10f1")
        self.assertEqual(args[args.index("--template") + 1], "com.unity.template.urp-blank")

    def test_the_hub_list_is_read_and_a_project_taken_out_of_it_keeps_its_files(self):
        listed = [{"title": "P", "path": r"C:\work\P", "version": "6000.3.10f1"}, {"title": "no path"}]
        with _spawn(_Child(_answer(listed))) as spawned:
            self.assertEqual(UnityCli.projects(), [r"C:\work\P"])
        self.assertEqual(spawned.call_args[0][0][1:4], ["projects", "list", "-a"])
        removed = _answer([{"path": r"C:\work\P", "ok": True}])
        with _spawn(_Child(removed)) as spawned:
            self.assertTrue(UnityCli.unregister(r"C:\work\P"))
        args = spawned.call_args[0][0]
        self.assertEqual(args[1:3], ["projects", "remove"])
        self.assertIn("--force", args, "no confirmation prompt")
        # The real CLI's answer for a path the Hub does not list: exit 6, no errors.
        absent = _answer([{"path": r"C:\P", "ok": False, "error": r"Project not found in Hub: C:\P"}], success=False)
        with _spawn(_Child(absent, 6)):
            self.assertFalse(UnityCli.unregister(r"C:\P"))
        broken = _answer([{"path": r"C:\P", "ok": False, "error": "registry locked"}], success=False)
        with _spawn(_Child(broken, 1)):
            with self.assertRaisesRegex(RuntimeError, "registry locked"):
                UnityCli.unregister(r"C:\P")


class RealCliTest(unittest.TestCase):
    """The real CLI's answers, read-only, held to the shape the stand-in plays.
    Added: 2026-11-04"""

    @EnvSkip.unless(UnityCli.exe() is not None, "Unity's CLI (unity) is not installed")
    def test_the_real_cli_lists_its_editors_in_the_shape_editors_reads(self):
        data = UnityCli.run("editors", "-i")
        self.assertIsInstance(data, list)
        for entry in data:
            self.assertIsInstance(entry.get("version"), str, entry)
            self.assertIsInstance(entry.get("location"), str, entry)
            self.assertGreaterEqual(UnityCli.version_key(entry["version"])[0], 0, entry)
        editors = UnityCli.editors(refresh=True)
        self.assertEqual(sorted(editors), sorted(e["version"] for e in data))
        for exe in editors.values():
            self.assertTrue(os.path.isfile(exe), exe)

    @EnvSkip.unless(UnityCli.exe() is not None, "Unity's CLI (unity) is not installed")
    def test_the_real_cli_lists_the_hubs_projects_by_path(self):
        for path in UnityCli.projects():
            self.assertTrue(os.path.isabs(path), path)


class UnityCliTestIsolationTest(unittest.TestCase):
    """The stand-in's listings never outlive the tests that make them: the
    process-wide cache (``UnityCli._editors``) held the last fake listing
    (none) after :class:`UnityCliTest`, so every later module of a full run
    read "no Unity editor is installed". Added: 2026-11-08"""

    def test_its_tests_leave_the_editor_listing_as_they_found_it(self):
        held = UnityCli._editors
        sentinel = {"6000.3.10f1": "C:/sentinel/Unity.exe"}
        UnityCli._editors = sentinel
        try:
            suite = unittest.TestLoader().loadTestsFromTestCase(UnityCliTest)
            result = unittest.TextTestRunner(stream=io.StringIO(), verbosity=0).run(suite)
            self.assertTrue(result.wasSuccessful(), result.failures + result.errors)
            self.assertIs(UnityCli._editors, sentinel, "the listing a later module reads")
        finally:
            UnityCli._editors = held


if __name__ == "__main__":
    unittest.main()
