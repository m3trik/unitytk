"""Tests for unitytk.unity_host: how the ``unity`` operation host starts an editor
-- the version a project names, the owned project, the arguments, why a start
failed, and the one manifest line an install writes. No editor is launched here."""

import contextlib
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock
from unittest.mock import patch

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

import pythontk as ptk

import unitytk
from unitytk.project_builder import ProjectBuilder
from unitytk.unity_host import UnityHost

_EDITORS = "unitytk.unity_cli.UnityCli.editors"


def _plan(project: Path, **kwargs) -> list:
    """What a dry run of ``install`` plans: ``(kind, path)`` pairs."""
    ctx = ptk.OpContext(dry_run=True)
    assert UnityHost.install(str(project), ctx=ctx, **kwargs) == []
    return [(c.kind, os.path.abspath(c.target)) for c in ctx.changes]


def _remote(stdout=None, returncode=0, stderr="", raises=None):
    """``git ls-remote`` stood in: its answer (*stdout*, *returncode*), or
    *raises* for a git that cannot run. ``stdout=None`` lists this version's tag."""
    if stdout is None:
        stdout = f"0123abcd\trefs/tags/v{unitytk.__version__}\n"
    answer = mock.Mock(returncode=returncode, stdout=stdout, stderr=stderr)
    return mock.patch("unitytk.unity_host.subprocess.run", side_effect=raises, return_value=answer)


def _write_manifest(project: Path, manifest: dict) -> Path:
    path = project / "Packages" / "manifest.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return path


def _manifest(project: Path) -> dict:
    return json.loads((project / "Packages" / "manifest.json").read_text(encoding="utf-8"))


def _project(root: Path, version: str = "6000.3.10f1") -> Path:
    (root / "Assets" / "Scenes").mkdir(parents=True)
    (root / "ProjectSettings").mkdir()
    (root / "ProjectSettings" / "ProjectVersion.txt").write_text(
        f"m_EditorVersion: {version}\nm_EditorVersionWithRevision: {version} (x)\n",
        encoding="utf-8",
    )
    return root


@contextlib.contextmanager
def _held(path: Path):
    """Hold *path* as an editor holds its ``Temp/UnityLockfile``: shared with no
    one on Windows, an exclusive ``flock`` elsewhere."""
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateFileW.restype = wintypes.HANDLE
        handle = kernel32.CreateFileW(str(path), 0x80000000 | 0x40000000, 0, None, 3, 0x80, None)
        try:
            yield
        finally:
            kernel32.CloseHandle(handle)
    else:
        import fcntl

        with open(path, "a") as fh:
            fcntl.flock(fh, fcntl.LOCK_EX)
            yield


class TestUnityHost(UnityTkTestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.project = _project(self.root / "Shots")
        self.local = "file:" + ProjectBuilder.package_dir().resolve().as_posix()

    def tearDown(self):
        self._tmp.cleanup()

    def test_a_call_names_its_project_by_a_folder_or_a_scene_inside_it(self):
        scene = self.project / "Assets" / "Scenes" / "Main.unity"
        self.assertEqual(UnityHost.project_of([str(self.project)]), str(self.project.resolve()))
        self.assertEqual(UnityHost.project_of([str(scene)]), str(self.project.resolve()))
        self.assertIsNone(UnityHost.project_of([]))
        with self.assertRaisesRegex(ValueError, "in no Unity project"):
            UnityHost.project_of([str(self.root / "loose.unity")])

    def test_a_project_opens_in_the_version_it_names_and_never_another(self):
        editors = {"6000.3.10f1": "C:/U/6000.3.10f1/Unity.exe", "2022.3.40f1": "C:/U/2022/Unity.exe"}
        with patch(_EDITORS, return_value=editors):
            self.assertEqual(UnityHost.find_editor(str(self.project)), editors["6000.3.10f1"])
            old = _project(self.root / "Old", version="6000.0.1f1")
            with self.assertRaisesRegex(FileNotFoundError, "6000.0.1f1, the version .* names, is not installed"):
                UnityHost.find_editor(str(old))
            # No project (the owned one): the newest at or above the package's floor.
            self.assertEqual(UnityHost.find_editor(), editors["6000.3.10f1"])
        with patch(_EDITORS, return_value={"2022.3.40f1": "C:/U/2022/Unity.exe"}):
            with self.assertRaisesRegex(FileNotFoundError, "no Unity 6000.3 or newer"):
                UnityHost.find_editor()

    def test_a_project_without_the_package_is_refused_with_the_install_as_the_fix(self):
        """Added: 2026-11-04"""
        with self.assertRaisesRegex(RuntimeError, "unity_project.install"):
            UnityHost.serve(self.root / "session", 123, str(self.project))
        _write_manifest(self.project, {"dependencies": {"com.m3trik.unitytk": self.local}})
        args = UnityHost.serve(self.root / "session", 123, str(self.project))
        self.assertEqual(
            args,
            [
                "-batchmode", "-projectPath", str(self.project), "-logFile", "-",
                "-unitytkServe", str(self.root / "session"), "-unitytkOwner", "123",
            ],
        )

    def test_a_call_from_the_command_line_runs_on_the_project_its_scenes_name(self):
        run = self.root / "run"
        run.mkdir()
        scene = self.project / "Assets" / "Scenes" / "Main.unity"
        (run / "request.json").write_text(json.dumps({"scenes": [str(scene)]}), encoding="utf-8")
        _write_manifest(self.project, {"dependencies": {"com.m3trik.unitytk": self.local}})
        args = UnityHost.command(run)
        self.assertEqual(args[:3], ["-batchmode", "-projectPath", str(self.project.resolve())])
        self.assertEqual(args[-4:], ["-executeMethod", UnityHost.BATCH_METHOD, "-unitytkRun", str(run)])

    def test_a_failed_start_says_why(self):
        """A licence is the Hub's: the fix names it, never a credential store.
        Added: 2026-11-04"""
        for why in (UnityHost.diagnose(198, ""), UnityHost.diagnose(1, "No valid Unity Editor license found")):
            self.assertIn("licence", why)
            self.assertIn("Unity Hub", why)
            self.assertNotIn("Credentials", why)
        locked = "It looks like another Unity instance is running with this project open."
        self.assertIn("open in another Unity", UnityHost.diagnose(1, locked))
        broken = "Assets/A.cs(3,1): error CS0103: The name 'x' does not exist\nAssets/B.cs(1,1): error CS1002: ; expected"
        self.assertIn("2 error(s)", UnityHost.diagnose(1, broken))
        self.assertIsNone(UnityHost.diagnose(1, "Exiting without the bug reporter."))

    def test_a_licensed_editors_log_is_no_licence_failure_and_a_compile_error_is_read_first(self):
        """Every editor's log carries ``[Licensing::...]`` lines, so the bare
        word "licensing" called any failed run a licence failure, and the test
        base turned a crash or a compile error into an environment skip. Only
        exit code 198 or one of Unity's own licence messages is the licence
        wall, and a compile error is read before either.
        Added: 2026-11-04"""
        licensed = (
            # The head of a licensed 6000.3.10f1 batch editor's log, verbatim.
            "[Licensing::Module] Trying to connect to existing licensing client channel...\n"
            '[Licensing::IpcConnector] Successfully connected to: "LicenseClient-x" at "2026-11-04T15:58:51.9512428Z"\n'
            '[Licensing::Client] Code 10 while verifying Licensing Client signature (process Id: 2516, path: '
            '"C:/Program Files/Unity Hub/UnityLicensingClient_V1/Unity.Licensing.Client.exe")\n'
            "[Licensing::Module] LicensingClient has failed validation; ignoring\n"
            "[Licensing::Client] Handshaking with LicensingClient:\n"
            "[Licensing::Client] Successfully updated the access token\n"
            "[Licensing::Module] License group:\n"
            "[Licensing::Client] Successfully updated license, isAsync: True, time: 0.00\n"
            "[Licensing::Module] Licensing Background thread has ended after 0.04s\n"
        )
        broken = licensed + "Assets/Sandbox/Idea.cs(3,13): error CS1002: ; expected\n"
        why = UnityHost.diagnose(1, broken)
        self.assertIn("do not compile", why)
        self.assertIn("CS1002", why)
        self.assertIn("do not compile", UnityHost.diagnose(198, broken), "a compile error is read first")
        self.assertIsNone(UnityHost.diagnose(1, licensed + "Crash!!!\n"), "a crash is no licence failure")
        for marker in (
            "No valid Unity Editor license found",
            "Machine bindings don't match",
            "Token not found in cache",
            "com.unity.editor.headless",
        ):
            self.assertIn("licence", UnityHost.diagnose(1, licensed + marker + "\n"), marker)

    def test_the_editors_it_starts_carry_no_licence_arguments(self):
        """Added: 2026-11-04"""
        run = self.root / "run"
        run.mkdir()
        (run / "request.json").write_text(json.dumps({"scenes": [str(self.project)]}), encoding="utf-8")
        _write_manifest(self.project, {"dependencies": {"com.m3trik.unitytk": self.local}})
        for args in (UnityHost.command(run), UnityHost.serve(self.root / "session", 123, str(self.project))):
            for flag in ("-username", "-password", "-serial"):
                self.assertNotIn(flag, args)

    def test_install_writes_one_manifest_line_and_keeps_every_other(self):
        """Local links this checkout's package; git names this version's tag; a
        dry run plans the one write; run again, it changes nothing.
        Added: 2026-11-04"""
        _write_manifest(self.project, {"dependencies": {"com.unity.timeline": "1.8.10"}, "testables": ["x"]})
        manifest = str(self.project / "Packages" / "manifest.json")
        self.assertEqual(_plan(self.project), [("write", os.path.abspath(manifest))])
        self.assertEqual(UnityHost.install(str(self.project)), [os.path.abspath(manifest)])
        self.assertEqual(
            _manifest(self.project),
            {"dependencies": {"com.unity.timeline": "1.8.10", "com.m3trik.unitytk": self.local}, "testables": ["x"]},
        )
        self.assertEqual(UnityHost.kernel_source(str(self.project)), "local")
        self.assertEqual(UnityHost.install(str(self.project)), [], "nothing to change")
        with _remote():  # the remote lists this version's tag
            UnityHost.install(str(self.project), source="git")
        url = f"https://github.com/m3trik/unitytk.git?path=/com.m3trik.unitytk#v{unitytk.__version__}"
        self.assertEqual(_manifest(self.project)["dependencies"]["com.m3trik.unitytk"], url)
        self.assertEqual(UnityHost.kernel_source(str(self.project)), "git")

    def test_a_git_install_of_a_tag_the_remote_does_not_list_is_refused(self):
        """A version with no tag on the remote (a dev build, one before its
        release) wrote a line Unity cannot resolve. Added: 2026-11-08"""
        _write_manifest(self.project, {"dependencies": {"com.unity.timeline": "1.8.10"}})
        before = _manifest(self.project)
        other = "0123abcd\trefs/tags/v0.0.1\n"
        with _remote(stdout=""), self.assertRaisesRegex(ValueError, rf"lists no tag v{unitytk.__version__}.*source='local'"):
            UnityHost.install(str(self.project), source="git")
        with _remote(stdout=other), self.assertRaisesRegex(ValueError, "lists no tag"):
            UnityHost.install(str(self.project), source="git", ctx=ptk.OpContext(dry_run=True))
        self.assertEqual(_manifest(self.project), before, "nothing written")

    def test_a_git_install_the_remote_cannot_confirm_proceeds_with_one_warning(self):
        """No git, no network or no access: the line is written, and one
        warning says what was not verified. Added: 2026-11-08"""
        tag = f"v{unitytk.__version__}"
        for failure in (dict(returncode=128, stderr="fatal: could not read Username"), dict(raises=OSError("no git"))):
            _write_manifest(self.project, {"dependencies": {}})
            ctx = ptk.OpContext()
            with _remote(**failure):
                UnityHost.install(str(self.project), source="git", ctx=ctx)
            self.assertIn(f"#{tag}", _manifest(self.project)["dependencies"]["com.m3trik.unitytk"])
            warnings = [i for i in ctx.issues if i.severity == "warning"]
            self.assertEqual(len(warnings), 1, ctx.issues)
            self.assertIn(tag, warnings[0].message)
            self.assertIn("not verified", warnings[0].message)
        _write_manifest(self.project, {"dependencies": {}})
        with _remote(returncode=128), self.assertLogs("unitytk.unity_host", "WARNING") as said:
            UnityHost.install(str(self.project), source="git")
        self.assertEqual(len(said.records), 1)

    def test_install_refuses_what_it_cannot_write_safely(self):
        """Added: 2026-11-04"""
        with self.assertRaisesRegex(ValueError, "not one of"):
            UnityHost.install(str(self.project), source="openupm")
        with self.assertRaisesRegex(ValueError, "no Unity project"):
            UnityHost.install(str(self.root / "nowhere"))
        (self.project / "Packages" / "com.m3trik.unitytk").mkdir(parents=True)
        with self.assertRaisesRegex(ValueError, "embeds a copy"):
            UnityHost.install(str(self.project))
        self.assertIsNone(UnityHost.kernel_source(str(self.project)), "Unity resolves the copy over the manifest")

    def test_a_link_elsewhere_or_another_version_is_no_kernel(self):
        """Added: 2026-11-04"""
        for spec in ("file:../../Elsewhere", "https://github.com/m3trik/unitytk.git?path=/com.m3trik.unitytk#v0.0.1", "0.7.0"):
            _write_manifest(self.project, {"dependencies": {"com.m3trik.unitytk": spec}})
            self.assertIsNone(UnityHost.kernel_source(str(self.project)), spec)
        # A relative link reads from the project's Packages/: a project on the
        # checkout's drive (a relative path never crosses one).
        near = Path(tempfile.mkdtemp(dir=Path(__file__).resolve().parent / "temp_tests"))
        self.addCleanup(lambda: __import__("shutil").rmtree(near, ignore_errors=True))
        project = _project(near / "Rel")
        relative = os.path.relpath(ProjectBuilder.package_dir(), project / "Packages")
        _write_manifest(project, {"dependencies": {"com.m3trik.unitytk": "file:" + relative.replace(os.sep, "/")}})
        self.assertEqual(UnityHost.kernel_source(str(project)), "local", "a relative link reads from Packages/")

    def test_a_call_without_scenes_and_one_naming_an_owned_scene_share_one_editor(self):
        # A project opens in one editor: the owned project's key is its own folder,
        # so naming a scene inside it reaches the editor a call without scenes started.
        owned = _project(self.root / "Owned").resolve()
        editors = {"6000.3.10f1": "C:/U/6000.3.10f1/Unity.exe"}
        with patch.object(UnityHost, "_sandbox_folder", return_value=owned), patch(_EDITORS, return_value=editors):
            key = UnityHost.instance([])
            self.assertEqual(key, str(owned))
            self.assertEqual(UnityHost.instance([str(owned / "Assets" / "Scenes" / "Main.unity")]), key)
            self.assertEqual(UnityHost.instance([str(self.project)]), str(self.project.resolve()))
            self.assertEqual(UnityHost.find_editor(key), editors["6000.3.10f1"])  # the newest
            with patch.object(UnityHost, "sandbox", return_value=key) as made:
                self.assertEqual(UnityHost.serve(self.root / "session", None, key)[2], key)
            made.assert_called_once()  # built, or its manifest refreshed

    def test_the_owned_projects_first_build_is_logged_and_cancellable(self):
        """The first call builds the owned project (minutes) inside the host:
        it hands the build a context that logs each step and checks the
        ambient cancel. Added: 2026-11-04"""
        owned = self.root / "Owned"
        with patch.object(UnityHost, "_sandbox_folder", return_value=owned), patch.object(
            ProjectBuilder, "build", return_value=str(owned)
        ) as build:
            self.assertEqual(UnityHost.sandbox(), str(owned))
        ctx = build.call_args.kwargs["ctx"]
        self.assertIsInstance(ctx, ptk.OpContext)
        with self.assertLogs("unitytk.unity_host", level="WARNING") as said:
            ctx.progress(0.0, "make the urp project")
        self.assertIn("make the urp project", said.output[0])
        scope = ptk.CancelScope()
        scope.cancel("stop")
        with scope:
            with self.assertRaises(ptk.OperationCancelled):
                ctx.check()

    def test_a_path_in_no_project_keys_to_the_owned_project_and_a_session_names_its_project(self):
        owned = _project(self.root / "Owned").resolve()
        with patch.object(UnityHost, "_sandbox_folder", return_value=owned):
            self.assertEqual(UnityHost.instance([str(self.root / "typo" / "Main.unity")]), str(owned))
        with self.assertRaisesRegex(ValueError, "names none"):
            UnityHost.serve(self.root / "session", 123, None)
        run = self.root / "run"
        run.mkdir()
        (run / "request.json").write_text(json.dumps({"scenes": [str(self.root / "typo.unity")]}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "in no Unity project"):
            UnityHost.command(run)

    def test_a_project_an_editor_holds_is_never_written_or_opened(self):
        """Unity's own lock file held, or ``EditorInstance.json`` naming a running
        Unity: install and a launch refuse, saying to ask the person -- never to
        close it."""
        lockfile = self.project / "Temp" / "UnityLockfile"
        lockfile.parent.mkdir()
        lockfile.write_text("", encoding="utf-8")
        self.assertIsNone(UnityHost.open_by(str(self.project)))
        with _held(lockfile):
            self.assertIn("UnityLockfile", UnityHost.open_by(str(self.project)))
            with self.assertRaisesRegex(RuntimeError, "never close it yourself"):
                UnityHost.install(str(self.project))
            self.assertFalse((self.project / "Packages").exists(), "nothing was written")
            _write_manifest(self.project, {"dependencies": {"com.m3trik.unitytk": self.local}})
            with patch.object(UnityHost, "RELEASE_WAIT", 0.0):
                with self.assertRaisesRegex(RuntimeError, "is open in a Unity editor"):
                    UnityHost.serve(self.root / "session", 123, str(self.project))
        library = self.project / "Library"
        library.mkdir()
        (library / "EditorInstance.json").write_text(json.dumps({"process_id": 4242}), encoding="utf-8")
        with patch.object(ptk.AppLauncher, "get_running_processes", return_value=[4242]):
            self.assertEqual(UnityHost.open_by(str(self.project)), "Unity (pid 4242) has it open")
        with patch.object(ptk.AppLauncher, "get_running_processes", return_value=[]):
            self.assertIsNone(UnityHost.open_by(str(self.project)), "a pid no Unity runs under is stale")

    def test_a_call_on_a_project_an_editor_holds_fails_with_the_fix_and_launches_nothing(self):
        lockfile = self.project / "Temp" / "UnityLockfile"
        lockfile.parent.mkdir()
        lockfile.write_text("", encoding="utf-8")
        _write_manifest(self.project, {"dependencies": {"com.m3trik.unitytk": self.local}})
        entry = {"id": "unity.project.doctor", "host": "unity", "effect": "read", "dry_run": True}
        request = ptk.OpRequest(op="unity.project.doctor", scenes=[str(self.project)])
        with _held(lockfile), patch(_EDITORS, return_value={"6000.3.10f1": "Unity.exe"}), patch.object(
            UnityHost, "RELEASE_WAIT", 0.0
        ), patch.object(ptk.AppLauncher, "run") as launched:
            outcome = UnityHost.operation_host().run(entry, request)
        self.assertEqual(outcome.status, "failed")
        self.assertIn("never close it yourself", outcome.error)
        launched.assert_not_called()

    def test_its_runner_is_kept_per_session_and_project(self):
        runner = UnityHost.operation_host()
        self.assertIsNotNone(runner.command)
        self.assertIsNotNone(runner.serve_args)
        self.assertEqual(runner.instance, UnityHost.instance)
        kept = ptk.SessionOperationHost(runner, "unitytk-test", "unity")
        self.assertIs(kept.host, runner)


if __name__ == "__main__":
    unittest.main()
