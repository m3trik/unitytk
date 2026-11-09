# !/usr/bin/python
# coding=utf-8
"""UnityProject -- a Unity project's folder, as this tooling reads and writes it.

The one home of what both the ``unity`` host (:class:`~unitytk.unity_host.UnityHost`)
and the owned project's builder (:class:`~unitytk.project_builder.ProjectBuilder`)
do to a project: its package manifest read and written (``Packages/manifest.json``),
the line that names this checkout's package (``file:``), the editor version it
names, whether an editor has it open (never written or opened then), whether its
path leaves the editor room for URP's deepest import, and why an editor on it
wrote nothing. Both build on it; neither reaches into the other.

Prior art: Unity's own lock (``Temp/UnityLockfile``, held while an editor has
the project open) and ``Library/EditorInstance.json``, read as the Hub reads
them; the manifest as the Package Manager reads it (UTF-8, a BOM allowed).
"""

import json
import os
import re
import time
from pathlib import Path
from typing import Any, Dict, Optional

import pythontk as ptk

from unitytk.unity_cli import UnityCli


class _UnityProjectInternal:
    """Internal helpers for :class:`UnityProject`."""

    _VERSION = re.compile(r"^m_EditorVersion:\s*(\S+)", re.M)
    _COMPILE_ERROR = re.compile(r"^.*error CS\d{4}:.*$", re.M)

    @staticmethod
    def _lockfile_held(path: Path) -> bool:
        """Whether an editor holds *path* (``Temp/UnityLockfile``): opened without
        creating it, then let go at once."""
        try:
            fd = os.open(str(path), os.O_RDWR)
        except FileNotFoundError:
            return False
        except PermissionError:  # Windows: the editor holds it, shared with nobody
            return True
        try:
            if os.name != "nt":
                import fcntl

                try:
                    fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except OSError:
                    return True
                fcntl.flock(fd, fcntl.LOCK_UN)
            return False
        finally:
            os.close(fd)


class UnityProject(_UnityProjectInternal):
    """A Unity project's folder, read and written safely (see the module docstring)."""

    #: The package's id, the manifest's key for it.
    PACKAGE = "com.m3trik.unitytk"
    #: What to do about a project an editor holds, which is never written to or opened.
    OPEN_FIX = "ask the person to close it, or name another project; never close it yourself"
    #: Seconds a write or a launch waits for an editor that is closing (this
    #: tooling's own, ending as the next starts) to let its project go.
    RELEASE_WAIT = 30.0
    #: What a batch editor's log says when it has no licence, and its exit code:
    #: Unity's own messages, never a word every log carries (``[Licensing::...]``).
    LICENCE_CODE = 198
    LICENCE_PHRASES = (
        "no valid unity editor license",
        "machine bindings don't match",
        "token not found in cache",
        "com.unity.editor.headless",
    )
    #: The longest path the editor opens: it is not long-path aware, and past
    #: Windows' MAX_PATH (260 with the terminating NUL) its imports fail.
    MAX_PATH = 259
    #: Where a project holds URP (6000.3's cache folder, ``@`` and a 12-digit hash).
    URP_PACKAGE_CACHE = r"\Library\PackageCache\com.unity.render-pipelines.universal@37e0d4fc2503"
    #: The deepest shader graph URP ships (samples aside), below its package root.
    #: URP's own material import lands the Autodesk materials (Interactive, 3ds
    #: Max Physical) an FBX carries on its graphs, so every one must import.
    URP_DEEPEST_GRAPH = (
        r"\Runtime\Materials\PhysicalMaterial3DsMax\PhysicalMaterial3DsMaxTransparent.shadergraph"
    )
    #: How far below a project root that graph sits. Past MAX_PATH its import
    #: fails (DirectoryNotFoundException), the graph resolves to nothing and
    #: URP's mapper silently leaves the material on plain Lit; measured on a
    #: shallower graph, a project 25 characters too deep passed (2026-10-19).
    URP_IMPORT_DEPTH = len(URP_PACKAGE_CACHE + URP_DEEPEST_GRAPH)
    #: The longest project root a URP project can have: MAX_PATH less that depth.
    URP_ROOT_MAX = MAX_PATH - URP_IMPORT_DEPTH

    @classmethod
    def package_dir(cls) -> Optional[Path]:
        """This checkout's Unity package folder, or ``None`` outside a
        checkout (a wheel carries no C#)."""
        import unitytk

        folder = Path(unitytk.__file__).resolve().parent.parent / cls.PACKAGE
        return folder if (folder / "package.json").is_file() else None

    @classmethod
    def package_line(cls, package: Optional[Path] = None) -> str:
        """The manifest value that links *package* (this checkout's when
        ``None``): ``file:`` and its absolute folder.

        Raises:
            FileNotFoundError: No *package*, and this unitytk is no checkout.
        """
        package = package or cls.package_dir()
        if package is None:
            raise FileNotFoundError(
                "this unitytk is no checkout, so there is no local package to link: install from git"
            )
        return "file:" + Path(package).resolve().as_posix()

    @classmethod
    def links_here(cls, project: str, spec: str) -> bool:
        """Whether the manifest dependency *spec* (``file:<folder>``, a relative
        folder read from the project's ``Packages/``, as Unity reads it) is
        this checkout's own package folder."""
        package = cls.package_dir()
        if package is None or not spec.startswith("file:"):
            return False
        target = spec[len("file:"):]
        folder = Path(target) if os.path.isabs(target) else Path(project, "Packages", target)
        try:
            return os.path.normcase(str(folder.resolve())) == os.path.normcase(str(package.resolve()))
        except OSError:
            return False

    @staticmethod
    def is_project(folder) -> bool:
        """Whether *folder* is a Unity project (``Assets`` and ``ProjectSettings``)."""
        return (Path(folder) / "Assets").is_dir() and (Path(folder) / "ProjectSettings").is_dir()

    @classmethod
    def version(cls, project: str) -> Optional[str]:
        """The editor version *project* was last saved with (``ProjectVersion.txt``)."""
        path = Path(project, "ProjectSettings", "ProjectVersion.txt")
        try:
            match = cls._VERSION.search(path.read_text(encoding="utf-8", errors="replace"))
        except OSError:
            return None
        return match.group(1) if match else None

    @classmethod
    def editor(cls, project: str) -> str:
        """The executable of the editor version *project* names, never another
        (a project opened in another version is upgraded).

        Raises:
            FileNotFoundError: It names none, or that version is not installed.
        """
        version = cls.version(project)
        if version is None:
            raise FileNotFoundError(f"{project} names no editor version (ProjectSettings/ProjectVersion.txt)")
        editors = UnityCli.editors()
        if version not in editors:
            raise FileNotFoundError(
                f"Unity {version}, the version {project} names, is not installed "
                f"(installed: {sorted(editors) or 'none'}); install it through Unity Hub"
            )
        return editors[version]

    @staticmethod
    def manifest_path(project: str) -> str:
        """*project*'s package manifest, ``Packages/manifest.json``."""
        return os.path.abspath(os.path.join(project, "Packages", "manifest.json"))

    @classmethod
    def manifest(cls, project) -> Dict[str, Any]:
        """*project*'s package manifest; empty when it has none.

        Raises:
            ValueError: It is not a JSON object (the fix: repair it by hand).
        """
        path = cls.manifest_path(str(project))
        try:
            text = Path(path).read_text(encoding="utf-8-sig")
        except FileNotFoundError:
            return {}
        try:
            data = json.loads(text)
        except ValueError as exc:
            raise ValueError(
                f"{path} is not valid JSON ({exc}): repair it by hand (Unity cannot read it either)"
            ) from exc
        if not isinstance(data, dict):
            raise ValueError(f"{path} holds no JSON object: repair it by hand")
        return data

    @classmethod
    def write_manifest(cls, project, manifest: Dict[str, Any], wait: float = 0.0) -> str:
        """Write *manifest* as *project*'s, atomically, once no editor has it
        open (:meth:`refuse_open`, waiting up to *wait* seconds for one
        closing); returns its path. The caller asks its ``ctx`` first.

        Raises:
            RuntimeError: An editor has the project open.
        """
        cls.refuse_open(str(project), wait)
        path = cls.manifest_path(str(project))
        data = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        ptk.FileUtils.atomic_write(path, lambda part: Path(part).write_bytes(data))
        return path

    @classmethod
    def open_by(cls, project: str) -> Optional[str]:
        """What says an editor has *project* open, else ``None``: Unity's own lock
        file is held, or ``Library/EditorInstance.json`` names a Unity that runs."""
        if cls._lockfile_held(Path(project, "Temp", "UnityLockfile")):
            return "its Temp/UnityLockfile is held"
        try:
            text = Path(project, "Library", "EditorInstance.json").read_text(encoding="utf-8")
            pid = int(json.loads(text)["process_id"])
        except (OSError, ValueError, KeyError, TypeError):
            return None
        if pid in ptk.AppLauncher.get_running_processes("Unity"):
            return f"Unity (pid {pid}) has it open"
        return None

    @classmethod
    def refuse_open(cls, project: str, wait: float = 0.0) -> None:
        """Raise unless no editor has *project* open, waiting up to *wait* seconds
        for one that is closing.

        Raises:
            RuntimeError: An editor has it open (a person's, perhaps).
        """
        deadline = time.monotonic() + wait
        while True:
            why = cls.open_by(project)
            if why is None:
                return
            if time.monotonic() >= deadline:
                raise RuntimeError(f"{project} is open in a Unity editor ({why}): {cls.OPEN_FIX}")
            time.sleep(0.25)

    @classmethod
    def refuse_deep(cls, project, fix: str) -> None:
        """Raise when *project*'s root is too long for URP's deepest import
        (:attr:`URP_ROOT_MAX` characters).

        Parameters:
            project: The project's folder, made or not.
            fix: How to give it a shorter one, as the message says it.

        Raises:
            ValueError: It is longer.
        """
        root = os.path.abspath(str(project))
        if len(root) > cls.URP_ROOT_MAX:
            raise ValueError(
                f"{root} is {len(root)} characters, too deep for the Unity editor: URP's deepest "
                f"import lands {cls.URP_IMPORT_DEPTH} below it, past MAX_PATH ({cls.MAX_PATH}), where "
                f"the import fails and its materials fall back to plain Lit; a URP project's root is "
                f"at most {cls.URP_ROOT_MAX} characters: {fix}"
            )

    @classmethod
    def diagnose(cls, code: Optional[int], tail: str) -> Optional[str]:
        """Why an editor on a project wrote nothing, from its exit code and the
        end of its log; ``None`` for the generic reading (its last error line).
        A compile error is read first, then the licence wall: exit code 198 or
        one of Unity's own licence messages, never a word every log carries."""
        low = tail.lower()
        compile_errors = cls._COMPILE_ERROR.findall(tail)
        if compile_errors:
            return f"the project's scripts do not compile ({len(compile_errors)} error(s)); the first: {compile_errors[0].strip()}"
        if code == cls.LICENCE_CODE or any(phrase in low for phrase in cls.LICENCE_PHRASES):
            return (
                "Unity could not get a licence for a batch editor: sign in with Unity Hub "
                "(or `unity auth login`) and activate one (`unity license`)"
            )
        if "another unity instance is running with this project" in low or "multiple unity instances cannot open the same project" in low:
            return f"the project is open in another Unity editor (a person's?): {cls.OPEN_FIX}"
        return None
