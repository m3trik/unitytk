# !/usr/bin/python
# coding=utf-8
"""UnityCli -- Unity's own command line (``unity``), read through its JSON.

Standard library only, so it imports anywhere.

Unity Hub installs and keeps current a CLI that lists the editors installed and
makes projects without the GUI, answering every command with one JSON document
(``--json``: ``{"success", "data", "errors", "warnings"}``). This class runs it
and reads that answer, so a tool finds an editor or makes a project the way
Unity does, instead of scanning install folders or launching an editor with
``-createProject`` itself. It runs with no console window and no stdin (a GUI
host calls it), and a caller's cancel stops it and what it started.

Measured 2026-11-04 (CLI 1.0.0-beta.13, Unity 6000.3.10f1): ``editors -i``
answers in about 0.4 s (read once a process, :meth:`UnityCli.editors`);
``projects new`` with the URP template (``com.unity.template.urp-blank``) took
214 s and leaves the project imported, and registered in the Hub's project list
(``projects remove`` takes it out again, never touching its files). unitytk's
``test_unity_cli`` reads the real CLI's answer where one is installed.

Prior art: the Hub's own discovery (``secondaryInstallPath.json`` and the
default install folders), which tools scanned by hand; the CLI is its public
face, and a Hub update keeps it right.
"""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import time
from typing import Any, Callable, Dict, List, Optional, Sequence, Tuple


class _UnityCliInternal:
    """Internal helpers for :class:`UnityCli`: the child process and its answer."""

    #: Seconds between two looks at a running command (its cancel, its timeout).
    POLL = 0.5
    _VERSION = re.compile(r"^(\d+)\.(\d+)\.(\d+)([a-z]*)(\d*)")
    #: :meth:`UnityCli.editors`' answer, read once a process.
    _editors: Optional[Dict[str, str]] = None

    @staticmethod
    def _spawn_flags() -> Dict[str, Any]:
        """How the child starts: no console window on Windows (a GUI host calls
        this), its own session elsewhere, so a stop reaches what it started."""
        if os.name == "nt":
            return {"creationflags": getattr(subprocess, "CREATE_NO_WINDOW", 0)}
        return {"start_new_session": True}

    @staticmethod
    def _stop(proc: "subprocess.Popen") -> None:
        """End *proc* and every process it started (``projects new`` runs an editor)."""
        if proc.poll() is None:
            if os.name == "nt":
                subprocess.run(
                    ["taskkill", "/T", "/F", "/PID", str(proc.pid)],
                    stdin=subprocess.DEVNULL,
                    capture_output=True,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                )
            else:
                import signal

                try:
                    os.killpg(proc.pid, signal.SIGKILL)
                except OSError:
                    pass
            if proc.poll() is None:
                proc.kill()
        proc.communicate()

    @classmethod
    def _answer(
        cls,
        args: Sequence[str],
        timeout: float,
        cancel: Optional[Callable[[], None]],
    ) -> Tuple[Any, str, int]:
        """Run ``unity <args>``: its parsed answer (``None`` when it printed no
        JSON), the text it printed and its exit code.

        Raises:
            FileNotFoundError: The CLI is not installed.
            RuntimeError: It outran *timeout*.
            BaseException: Whatever *cancel* raised, or an interrupt while it
                waited; the command is stopped first (as ``subprocess.run``
                stops its child).
        """
        exe = cls.exe()
        if exe is None:
            raise FileNotFoundError(
                "Unity's command line (unity) is not installed: install Unity Hub, "
                f"which installs it, or name it in ${cls.ENV_VAR}"
            )
        proc = subprocess.Popen(
            [exe, *args, *cls.FLAGS],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            **cls._spawn_flags(),
        )
        deadline = time.monotonic() + timeout
        try:
            while True:
                try:
                    out, err = proc.communicate(timeout=cls.POLL)
                    break
                except subprocess.TimeoutExpired:
                    if cancel is not None:
                        cancel()
                    if time.monotonic() >= deadline:
                        raise RuntimeError(
                            f"unity {' '.join(args)} did not answer within {timeout:g} s"
                        ) from None
        except BaseException:
            # A cancel, the timeout, and as much a Ctrl-C in the wait itself:
            # the CLI and the editor it started must not run on.
            cls._stop(proc)
            raise
        text = (out or b"").decode("utf-8", errors="replace").strip()
        try:
            answer = json.loads(text)
        except ValueError:
            answer = None
            text = text or (err or b"").decode("utf-8", errors="replace")
        return answer, text, proc.returncode


class UnityCli(_UnityCliInternal):
    """Unity's command line: the editors installed, and a project made."""

    #: The variable that names the CLI's executable, before every other place.
    ENV_VAR = "UNITY_CLI"
    #: Flags every call passes: one JSON answer, no prompt, no banner.
    FLAGS: Tuple[str, ...] = ("--json", "--no-banner", "--non-interactive")
    #: Seconds the editors listing may take. It answers in about 0.4 s
    #: (measured 2026-11-04), and a GUI panel waits on it while it builds, so
    #: a hung CLI must fail fast: 15 s is over thirty times the measured time,
    #: room for a cold start or a slow disk, and short enough to wait out.
    EDITORS_TIMEOUT = 15.0

    @staticmethod
    def locations() -> List[str]:
        """Where the Hub installs the CLI when ``unity`` is not on ``PATH``
        (the Windows one only when ``LOCALAPPDATA`` names a folder)."""
        local = os.environ.get("LOCALAPPDATA")
        found = [os.path.join(local, "Unity", "bin", "unity.exe")] if local else []
        return found + [os.path.expanduser("~/.local/bin/unity"), "/usr/local/bin/unity"]

    @classmethod
    def exe(cls) -> Optional[str]:
        """The CLI's executable: :attr:`ENV_VAR`, then ``PATH``, then
        :meth:`locations`; ``None`` when it is not installed."""
        named = os.environ.get(cls.ENV_VAR)
        if named and os.path.isfile(named):
            return named
        found = shutil.which("unity")
        if found:
            return found
        return next((path for path in cls.locations() if os.path.isfile(path)), None)

    @classmethod
    def run(
        cls,
        *args: str,
        timeout: float = 120.0,
        cancel: Optional[Callable[[], None]] = None,
    ) -> Any:
        """Run ``unity <args>`` and return its answer's ``data``.

        Parameters:
            *args: The command and its arguments (``"editors", "-i"``).
            timeout: Seconds the command may take.
            cancel: Called about twice a second while it runs; what it raises
                (a cancelled operation's) stops the command and what it
                started, and propagates.

        Returns:
            The answer's ``data``.

        Raises:
            FileNotFoundError: The CLI is not installed.
            RuntimeError: The command failed (its own messages), answered
                something that is not its JSON document, or outran *timeout*.
        """
        answer, text, code = cls._answer(args, timeout, cancel)
        if answer is None:
            raise RuntimeError(
                f"unity {' '.join(args)} answered no JSON (exit {code}): {text[-400:]}"
            )
        if not isinstance(answer, dict) or not answer.get("success"):
            errors = answer.get("errors") if isinstance(answer, dict) else None
            messages = "; ".join(
                str(e.get("message", e)) if isinstance(e, dict) else str(e)
                for e in (errors or [])
            )
            raise RuntimeError(f"unity {' '.join(args)} failed: {messages or text[-400:]}")
        return answer.get("data")

    @classmethod
    def editors(cls, refresh: bool = False) -> Dict[str, str]:
        """The installed editors, ``{version: executable}``: read once a
        process, again with *refresh* (after an install).

        Raises:
            FileNotFoundError: The CLI is not installed.
            RuntimeError: It could not list them, or took longer than
                :attr:`EDITORS_TIMEOUT`.
        """
        if refresh or cls._editors is None:
            found: Dict[str, str] = {}
            for entry in cls.run("editors", "-i", timeout=cls.EDITORS_TIMEOUT) or []:
                if isinstance(entry, dict) and entry.get("version") and entry.get("location"):
                    found[str(entry["version"])] = str(entry["location"])
            cls._editors = found
        return dict(cls._editors)

    @classmethod
    def version_key(cls, version: str) -> Tuple[Any, ...]:
        """A sort key that orders editor versions by number
        (``2022.3.9f1`` before ``2022.3.10f1``); an unreadable one sorts first."""
        match = cls._VERSION.match(str(version))
        if not match:
            return (-1,)
        major, minor, patch, stream, build = match.groups()
        return (int(major), int(minor), int(patch), stream, int(build or 0))

    @classmethod
    def newest(cls, versions: Sequence[str], floor: Tuple[int, int] = (0, 0)) -> Optional[str]:
        """The newest of *versions* at or above *floor* (``(major, minor)``)."""
        usable = [v for v in versions if cls.version_key(v)[:2] >= tuple(floor)]
        return max(usable, key=cls.version_key) if usable else None

    @classmethod
    def create_project(
        cls,
        path: str,
        version: Optional[str] = None,
        template: Optional[str] = None,
        timeout: float = 1800.0,
        cancel: Optional[Callable[[], None]] = None,
    ) -> str:
        """Make a Unity project at *path* (``unity projects new``), imported
        and closed, never linked to Unity Cloud. The Hub lists it from then on
        (:meth:`unregister` takes it out).

        Parameters:
            path: The project folder to make (it must not exist).
            version: The editor version; the CLI's default when ``None``.
            template: A template's name (``com.unity.template.urp-blank``,
                ``com.unity.template.3d`` for Built-in); the CLI's default
                when ``None``.
            timeout: Seconds the creation may take.
            cancel: As :meth:`run`'s: what it raises stops the creation.

        Returns:
            The project's folder, as the CLI reports it.

        Raises:
            FileNotFoundError: The CLI is not installed.
            RuntimeError: It could not make the project (an unknown
                template, a version not installed, a folder that exists).
        """
        folder = os.path.abspath(path)
        args: List[str] = [
            "projects",
            "new",
            os.path.basename(folder),
            "--path",
            os.path.dirname(folder),
        ]
        if version:
            args += ["--editor-version", version]
        if template:
            args += ["--template", template]
        data = cls.run(*args, timeout=timeout, cancel=cancel)
        return str((data or {}).get("path") or folder)

    @classmethod
    def projects(cls) -> List[str]:
        """The project folders the Hub lists (``unity projects list -a``).

        Raises:
            FileNotFoundError: The CLI is not installed.
            RuntimeError: It could not list them.
        """
        return [
            str(entry["path"])
            for entry in cls.run("projects", "list", "-a") or []
            if isinstance(entry, dict) and entry.get("path")
        ]

    @classmethod
    def unregister(cls, path: str) -> bool:
        """Take *path* out of the Hub's project list (``unity projects
        remove``); its files are never touched. ``False`` when the Hub did not
        list it.

        Raises:
            FileNotFoundError: The CLI is not installed.
            RuntimeError: The Hub could not take it out.
        """
        args = ("projects", "remove", os.path.abspath(path), "--force")
        answer, text, code = cls._answer(args, 120.0, None)
        entries = answer.get("data") if isinstance(answer, dict) else None
        for entry in entries if isinstance(entries, list) else []:
            if isinstance(entry, dict) and entry.get("ok"):
                return True
            if isinstance(entry, dict) and "not found" in str(entry.get("error", "")).lower():
                return False
        raise RuntimeError(f"unity {' '.join(args)} failed (exit {code}): {text[-400:]}")
