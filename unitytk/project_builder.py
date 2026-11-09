# !/usr/bin/python
# coding=utf-8
"""ProjectBuilder -- the test and demo project, built on demand the way a user's is.

No Unity project lives in the repo. This builds one with Unity's own tools:
``unity projects new`` makes it from the editor's template (URP's
``com.unity.template.urp-blank``, or Built-in's ``com.unity.template.3d``),
then one manifest line installs this checkout's package (``file:``), listed
under ``testables`` so Unity's Test Framework runs its tests, beside the
packages the package's doors compile against. Each of the package's samples
(``package.json``'s ``samples``, in ``Samples~/``) is then copied in where the
Package Manager imports one (``Assets/Samples/<package>/<version>/<sample>``),
through pythontk's folder deliverer as a DCC bridge delivers a file, so the
package's Edit Mode tests find each sample imported and check it, its demo
recipe included. So every run proves the pipeline with the tools a user has,
and the repo stays small.

The project lives in the per-user cache (or the folder ``UNITYTK_PROJECTS``
names), one per editor version and pipeline, made once (about three and a half
minutes, its progress reported and cancellable) and refreshed in seconds after
that: its manifest is rewritten to name this checkout on every build. A
``key`` names another such project beside it, so two runs never share one (the
workspace's end-to-end suite builds its own, a producer's delivery in place of
a sample's file, while the package's suites run in this one). It stays
in the Hub's project list, as a project a person made would. The ``unity``
operation host runs its owned editor on it (:meth:`UnityHost.sandbox`), and
:meth:`test` runs the package's tests in it, in a batch editor this call
starts. Nothing here opens, writes or tests a project an editor has open.

Prior art: GameCI's package mode, which generates a temporary project around a
package to test it; Unity's own ``-createProject``, which the CLI wraps; and
for the samples, the Package Manager's own import
(``UnityEditor.PackageManager.UI.Sample.Import``), whose folder layout
:meth:`sample_folder` reproduces. The builder copies the samples itself
rather than calling it: it runs before any editor exists (the project is made
by the CLI, headless, and Unity's import needs an editor with the package
resolved, so it could only run inside a second editor launch), and it must be
idempotent across rebuilds -- a sample copied again only when its source
changed (:attr:`SAMPLE_STAMP`), another version's samples removed -- where
Unity's import copies the whole sample again on every call.
"""

from __future__ import annotations

import contextlib
import hashlib
import json
import logging
import os
import re
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Dict, Iterator, List, Optional, Tuple

import pythontk as ptk

from unitytk.unity_cli import UnityCli
from unitytk.unity_project import UnityProject


class _ProjectBuilderInternal(UnityProject):
    """Internal helpers for :class:`ProjectBuilder`: one build at a time, the
    make and what a failed one leaves, a sample's folder name and digest."""

    #: Seconds between two looks at a build lock another run holds.
    LOCK_POLL = 0.5
    #: Seconds a build waits for another run's build of the same project: a
    #: make (about four minutes; the CLI's own limit is 30) and a refresh.
    LOCK_WAIT = 2400.0
    #: Tries at removing a half-made project, a second apart: the editor a
    #: cancel stopped lets its files go a moment after it ends.
    REMOVE_TRIES = 5

    @staticmethod
    def _pending(folder: Path) -> Path:
        """The marker beside *folder* that a make of it began and has not
        finished: what proves a leftover there is this tooling's to remove."""
        return folder.with_name(folder.name + ".making")

    @classmethod
    @contextlib.contextmanager
    def _locked(cls, folder: Path, ctx=None) -> Iterator[None]:
        """Hold the build lock of *folder* (``<folder>.lock`` beside it), so two
        runs never build one project at once: an OS lock, let go with its
        handle, so a killed run never leaves it held. Waits for another run's
        build up to :attr:`LOCK_WAIT`, *ctx*'s cancel checked while it waits.

        Raises:
            RuntimeError: Another run held it past :attr:`LOCK_WAIT`.
        """
        folder.parent.mkdir(parents=True, exist_ok=True)
        handle = open(folder.with_name(folder.name + ".lock"), "a+b")
        try:
            deadline, said = time.monotonic() + cls.LOCK_WAIT, False
            while not cls._try_lock(handle):
                if time.monotonic() >= deadline:
                    raise RuntimeError(
                        f"another run has built {folder} for over {cls.LOCK_WAIT:g} s; let it finish and build again"
                    )
                if not said:
                    logging.getLogger(__name__).warning("Unity: another run is building %s; waiting for it", folder)
                    said = True
                if ctx is not None:
                    ctx.check()
                time.sleep(cls.LOCK_POLL)
            try:
                yield
            finally:
                cls._unlock(handle)
        finally:
            handle.close()

    @staticmethod
    def _try_lock(handle) -> bool:
        """Take the lock on *handle*'s file without waiting; whether it was free."""
        try:
            if os.name == "nt":
                import msvcrt

                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl

                fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            return False
        return True

    @staticmethod
    def _unlock(handle) -> None:
        """Let *handle*'s lock go at once (Windows may let a closed handle's go late)."""
        try:
            if os.name == "nt":
                import msvcrt

                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl

                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)
        except OSError:
            pass

    @classmethod
    def _discard(cls, folder: Path) -> bool:
        """Remove the project a make of *folder* began (its :meth:`_pending`
        marker says so) and take it out of the Hub's list; whether it is gone.
        A folder it could not remove keeps its marker, so no build takes it
        for a project."""
        for attempt in range(max(1, cls.REMOVE_TRIES)):
            if attempt:
                time.sleep(1.0)
            shutil.rmtree(folder, ignore_errors=True)
            if not folder.exists():
                break
        try:
            UnityCli.unregister(str(folder))
        except (FileNotFoundError, RuntimeError):
            pass
        if folder.exists():
            logging.getLogger(__name__).warning(
                "Unity: the half-made project at %s could not be removed (a process holds its files)", folder
            )
            return False
        cls._pending(folder).unlink(missing_ok=True)
        return True

    @classmethod
    def _make(cls, folder: Path, version: str, pipeline: str, action: str, ctx) -> None:
        """Have Unity's CLI make *folder*, which does not exist, saying so
        first; what a cancel or a failure left half-made is taken away (and
        out of the Hub's list), and only that: the :meth:`_pending` marker,
        written before the CLI starts, names it this call's."""
        logging.getLogger(__name__).warning("Unity: %s at %s", action, folder)
        if ctx is not None:
            ctx.progress(0.0, action)
        folder.parent.mkdir(parents=True, exist_ok=True)
        cls._pending(folder).write_text(f"{os.getpid()}\n", encoding="utf-8")
        try:
            UnityCli.create_project(
                str(folder), version=version, template=cls.TEMPLATES[pipeline],
                cancel=None if ctx is None else ctx.check,
            )
            if not cls.is_project(folder):
                raise RuntimeError(f"Unity {version} reported the project made, but {folder} holds none")
        except BaseException:
            cls._discard(folder)
            raise
        cls._pending(folder).unlink(missing_ok=True)
        if ctx is not None:
            ctx.progress(1.0, f"made the {pipeline} project with Unity {version}")

    @staticmethod
    def _safe(name: str) -> str:
        """*name* as the Package Manager names a sample's folder: the
        characters a file name cannot hold removed."""
        return re.sub(r'[<>:"/\\|?*\x00-\x1f]', "", str(name)).strip()

    @staticmethod
    def _digest(files: Dict[Path, Path]) -> str:
        """One hash of *files* (``{relative path: file}``): each path and its bytes."""
        digest = hashlib.sha256()
        for rel in sorted(files):
            digest.update(rel.as_posix().encode("utf-8") + b"\n")
            digest.update(files[rel].read_bytes())
        return digest.hexdigest()

    @staticmethod
    def _log_tail(log: Path, lines: int = 12) -> Tuple[str, str]:
        """The end of the editor log *log* (``""`` when it wrote none): the
        text :meth:`diagnose` reads, and its last *lines* lines, indented, to
        quote in an error."""
        tail = log.read_text(encoding="utf-8", errors="replace")[-20000:] if log.is_file() else ""
        last = [line.rstrip() for line in tail.splitlines() if line.strip()][-lines:]
        return tail, "".join(f"\n    {line}" for line in last)


class ProjectBuilder(_ProjectBuilderInternal):
    """Build, refresh and test the owned project (see the module docstring);
    a project's files are :class:`~unitytk.unity_project.UnityProject`'s."""

    #: The oldest editor the package compiles on.
    FLOOR = (6000, 3)
    #: The variable that names where the owned projects live, before the user cache.
    ENV_VAR = "UNITYTK_PROJECTS"
    #: The owned project's folder name (a keyed project's is ``<NAME>-<key>``).
    NAME = "Unitytk"
    #: What a project's key may hold: a short plain name (the folder's depth counts for URP).
    KEY = re.compile(r"[A-Za-z0-9_]{1,16}")
    #: The editor's own templates, by pipeline.
    TEMPLATES: Dict[str, str] = {
        "urp": "com.unity.template.urp-blank",
        "builtin": "com.unity.template.3d",
    }
    #: Packages beyond the template's that the package's doors compile against.
    DOOR_PACKAGES: Dict[str, str] = {
        "com.unity.inputsystem": "1.18.0",
        "com.unity.test-framework": "1.6.0",
        "com.unity.xr.interaction.toolkit": "3.3.1",
    }
    #: Where the Package Manager imports a sample, below ``Assets/``:
    #: ``Samples/<package display name>/<version>/<sample display name>``.
    SAMPLES = "Samples"
    #: The file a copied sample keeps its files' digest in (Unity imports no
    #: dot file), so a rebuild copies a sample again only when they changed.
    SAMPLE_STAMP = ".unitytk_sample"
    #: Seconds a test run may take.
    TIMEOUT = 3600.0
    #: Exit codes of Unity's command-line test runner, read only when it wrote
    #: no results (2 also stands for an inconclusive test, so the verdict is the
    #: results file's).
    EXIT_CODES = {0: "every test passed", 2: "a test failed or was inconclusive", 3: "the run could not complete"}

    @classmethod
    def newest_editor(cls) -> str:
        """The newest installed editor at or above :attr:`FLOOR`.

        Raises:
            FileNotFoundError: None is installed.
        """
        version = UnityCli.newest(list(UnityCli.editors()), cls.FLOOR)
        if version is None:
            floor = ".".join(map(str, cls.FLOOR))
            raise FileNotFoundError(f"no Unity {floor} or newer is installed; install one through Unity Hub")
        return version

    @classmethod
    def folder(cls, version: str, pipeline: str = "urp", key: Optional[str] = None) -> Path:
        """Where the project for *version* and *pipeline* lives, made or not:
        below :attr:`ENV_VAR`'s folder, else the user cache's; *key* names
        another project beside it (``<NAME>-<key>``), ``None`` the owned one.

        Raises:
            ValueError: *key* is not a short plain name (:attr:`KEY`).
        """
        if key is not None and not cls.KEY.fullmatch(key):
            raise ValueError(f"a project key is up to 16 letters, digits or underscores, not {key!r}")
        root = os.environ.get(cls.ENV_VAR)
        base = Path(root) if root else ptk.UserConfig.cache_dir("unitytk", "projects")
        return (base / version / pipeline / (f"{cls.NAME}-{key}" if key else cls.NAME)).resolve()

    @classmethod
    def linked(cls, manifest: Dict[str, Any], package: Path) -> Dict[str, Any]:
        """*manifest*, copied, naming *package* (``file:``) under
        :attr:`PACKAGE` and in ``testables``, with :attr:`DOOR_PACKAGES`."""
        out = json.loads(json.dumps(manifest))
        deps = out.setdefault("dependencies", {})
        deps[cls.PACKAGE] = cls.package_line(package)
        for name, version in cls.DOOR_PACKAGES.items():
            deps.setdefault(name, version)
        testables = out.setdefault("testables", [])
        if cls.PACKAGE not in testables:
            testables.append(cls.PACKAGE)
        return out

    @classmethod
    def build(
        cls,
        editor: Optional[str] = None,
        pipeline: str = "urp",
        ctx: Optional["ptk.OpContext"] = None,
        key: Optional[str] = None,
        replacements: Optional[Dict[str, Any]] = None,
    ) -> str:
        """Build the owned project for *editor* (the newest when ``None``) and
        *pipeline*, or refresh it: made once by Unity's CLI from the editor's
        template, its manifest naming this checkout's package every time.

        Parameters:
            editor: An installed editor version.
            pipeline: ``"urp"`` or ``"builtin"``.
            ctx: The operation context, asked before the project is made
                (``should_create``) and its manifest written (``should_write``),
                told the progress of the making (minutes), and checked for a
                cancel while it runs: a cancel stops Unity's CLI and the editor
                it started, and takes away the half-made project.
            key: Another project of its own beside the owned one
                (:meth:`folder`), built and refreshed alike: a run that must
                not share the owned project builds its own.
            replacements: Per sample, a folder of files delivered in place of
                its own (:meth:`import_samples`).

        Returns:
            The project's folder.

        Raises:
            FileNotFoundError: No usable editor is installed, or this unitytk
                is no checkout (it has no package to link).
            ValueError: *pipeline* or a replacement's sample or file is
                unknown, *key* is no plain name, or the project's folder is
                too deep for URP's imports (the fix: a shorter :attr:`ENV_VAR`).
            RuntimeError: Unity could not make the project, an editor has
                it open, its folder holds something that is no project this
                tooling made (never removed), a half-made one could not be
                removed, or another run's build of it outran :attr:`LOCK_WAIT`.
            ptk.OperationCancelled: *ctx*'s run was cancelled while it was made.
        """
        if pipeline not in cls.TEMPLATES:
            raise ValueError(f"pipeline {pipeline!r} is not one of {sorted(cls.TEMPLATES)}")
        package = cls.package_dir()
        if package is None:
            raise FileNotFoundError("this unitytk is no checkout: it has no Unity package to link")
        version = editor or cls.newest_editor()
        folder = cls.folder(version, pipeline, key)
        if pipeline == "urp":
            cls.refuse_deep(folder, f"name a shorter folder for the owned projects in ${cls.ENV_VAR}")
        # A plan changes nothing, so it takes no lock (and makes no folder for one).
        planning = ctx is not None and getattr(ctx, "dry_run", False)
        with contextlib.nullcontext() if planning else cls._locked(folder, ctx):
            if cls._pending(folder).exists():  # a make that never finished (a killed run)
                if ctx is not None and not ctx.should_delete(str(folder), "remove a half-made project"):
                    return str(folder)
                if not cls._discard(folder):
                    raise RuntimeError(
                        f"the half-made project at {folder} could not be removed (a process holds its "
                        "files): end that process, or delete the folder, and build again"
                    )
            if not cls.is_project(folder):
                if folder.exists():
                    raise RuntimeError(
                        f"{folder} exists and holds no Unity project this tooling made, so it is left as "
                        f"it is: move or delete it, or name another folder in ${cls.ENV_VAR}"
                    )
                action = f"make the {pipeline} project with Unity {version} (about 4 minutes, once)"
                if ctx is not None and not ctx.should_create(str(folder), action):
                    return str(folder)
                cls._make(folder, version, pipeline, action, ctx)
            cls.refuse_open(str(folder), cls.RELEASE_WAIT)
            current = cls.manifest(folder)
            wanted = cls.linked(current, package)
            if wanted != current and (
                ctx is None or ctx.should_write(cls.manifest_path(str(folder)), "link this checkout's package")
            ):
                cls.write_manifest(folder, wanted)
            cls.import_samples(folder, package, ctx, replacements)
        return str(folder)

    @classmethod
    def samples(cls, package: Path) -> List[Dict[str, Any]]:
        """*package*'s samples as its ``package.json`` lists them, each with
        its ``source`` folder; ``[]`` when it lists none."""
        data = json.loads(Path(package, "package.json").read_text(encoding="utf-8-sig"))
        return [dict(s, source=Path(package, s["path"])) for s in data.get("samples", [])]

    @classmethod
    def sample_folder(cls, project, package: Path, sample: Dict[str, Any]) -> Path:
        """Where the Package Manager imports *sample* into *project*:
        ``Assets/Samples/<display name>/<version>/<sample display name>``."""
        data = json.loads(Path(package, "package.json").read_text(encoding="utf-8-sig"))
        return Path(project, "Assets", cls.SAMPLES, cls._safe(data.get("displayName") or data["name"]),
                    data["version"], cls._safe(sample["displayName"]))

    @classmethod
    def import_samples(
        cls,
        project,
        package: Optional[Path] = None,
        ctx: Optional["ptk.OpContext"] = None,
        replacements: Optional[Dict[str, Any]] = None,
    ) -> List[str]:
        """Copy each of *package*'s samples into *project* as the Package
        Manager imports one (:meth:`sample_folder`), file by file through
        pythontk's :class:`~pythontk.FolderDeliverer` as a DCC bridge delivers
        a file (an FBX carrying the files its records name). A sample whose
        files are unchanged since its last copy is left as it is, with what
        its recipe built beside it; a changed one replaces its folder whole,
        as a re-import does. Another version's samples of the package leave
        (the owned project holds this checkout's), so no sample's assembly is
        defined twice.

        Parameters:
            project: The owned project.
            package: The Unity package (this checkout's when ``None``).
            ctx: Asked before a sample is copied (``should_write``) and an old
                one removed (``should_delete``).
            replacements: ``{sample display name: folder}``: the folder's
                files, by their path in it, are delivered in place of the
                sample's own of the same path -- another producer's delivery
                of the sample, which the sample's tests then check. The
                sample's other files come from its source; a build without
                the replacement imports the sample's own again.

        Returns:
            The folders copied in this call.

        Raises:
            ValueError: A replacement names no sample of *package*, or a file
                the sample does not hold (checked before anything is copied).
            RuntimeError: A file could not be delivered (the deliverer logged why).
        """
        package = Path(package or cls.package_dir())
        copied: List[str] = []
        samples = cls.samples(package)
        replacements = {name: Path(folder) for name, folder in (replacements or {}).items()}
        unknown = sorted(set(replacements) - {s["displayName"] for s in samples})
        if unknown:
            raise ValueError(f"no sample of {package.name} is named {', '.join(unknown)}")
        files_of: Dict[str, Dict[Path, Path]] = {}
        for sample in samples:
            source = Path(sample["source"])
            files = {p.relative_to(source): p for p in source.rglob("*") if p.is_file()}
            folder = replacements.get(sample["displayName"])
            if folder is not None:
                delivered = {p.relative_to(folder): p for p in folder.rglob("*") if p.is_file()}
                stray = sorted(rel.as_posix() for rel in delivered if rel not in files)
                if not delivered:
                    raise ValueError(f"the {sample['displayName']} replacement {folder} holds no file")
                if stray:
                    raise ValueError(
                        f"the {sample['displayName']} sample holds no {', '.join(stray)}: a replacement "
                        f"({folder}) delivers only in place of the sample's own files"
                    )
                files.update(delivered)
            files_of[sample["displayName"]] = files
        if not samples:
            return copied
        version_dir = cls.sample_folder(project, package, samples[0]).parent
        for old in sorted(p for p in version_dir.parent.glob("*") if p.is_dir() and p != version_dir):
            if ctx is None or ctx.should_delete(str(old), "remove another version's samples"):
                shutil.rmtree(old)
                Path(str(old) + ".meta").unlink(missing_ok=True)
        deliverer = ptk.FolderDeliverer(embed=ptk.FbxMedia.embed_dependencies)
        assets = Path(project, "Assets")
        for sample in samples:
            dest, files = cls.sample_folder(project, package, sample), files_of[sample["displayName"]]
            digest = cls._digest(files)
            stamp = dest / cls.SAMPLE_STAMP
            if stamp.is_file() and stamp.read_text(encoding="utf-8").strip() == digest:
                continue
            if ctx is not None and not ctx.should_write(str(dest), f"import the {sample['displayName']} sample"):
                continue
            if dest.exists():
                shutil.rmtree(dest)
            bridge = ptk.FileDeliveryBridge(target_folder=str(assets), deliverer=deliverer)
            for rel in sorted(files):
                landed = bridge.send(
                    str(files[rel]),
                    params={
                        "SUBDIR": (dest.relative_to(assets) / rel.parent).as_posix(),
                        "NAME": os.path.splitext(rel.name)[0],
                    },
                )
                if not landed or Path(landed["file"]) != dest / rel:
                    raise RuntimeError(f"the {sample['displayName']} sample's {rel.as_posix()} was not delivered to {dest}")
            stamp.write_text(digest + "\n", encoding="utf-8")
            copied.append(str(dest))
        return copied

    @classmethod
    def parse(cls, results: str) -> Dict[str, Any]:
        """The counts, failures and verdict of the NUnit 3 results file *results*.

        Returns:
            dict: ``total``, ``passed``, ``failed``, ``skipped``,
            ``inconclusive``, ``result`` (NUnit's overall result), ``failures``
            (each failed case's ``name`` and ``message``), ``inconclusives``
            (each inconclusive case's), and ``ok``: no case failed and at least
            one ran (an inconclusive case is reported, not failed; 0 of 0 is
            no pass).
        """
        root = ET.parse(results).getroot()
        cases: List[Dict[str, Any]] = []
        for case in root.iter("test-case"):
            said = case.find("failure")
            if said is None:
                said = case.find("reason")
            message = said.findtext("message") if said is not None else None
            cases.append(
                {"name": case.get("fullname") or case.get("name"), "result": case.get("result"),
                 "message": (message or "").strip() or None}
            )
        count = lambda r: sum(1 for c in cases if c["result"] == r)  # noqa: E731
        named = lambda r: [{"name": c["name"], "message": c["message"]} for c in cases if c["result"] == r]  # noqa: E731
        failed = count("Failed")
        return {
            "total": len(cases),
            "passed": count("Passed"),
            "failed": failed,
            "skipped": count("Skipped"),
            "inconclusive": count("Inconclusive"),
            "result": root.get("result"),
            "failures": named("Failed"),
            "inconclusives": named("Inconclusive"),
            "ok": failed == 0 and len(cases) > 0,
        }

    @classmethod
    def test(
        cls,
        platform: str = "EditMode",
        project: Optional[str] = None,
        test_filter: Optional[str] = None,
        timeout: Optional[float] = None,
        pipeline: str = "urp",
    ) -> Dict[str, Any]:
        """Run the package's tests in *project* (the owned project for
        *pipeline*, built or refreshed, when ``None``), in a batch editor this
        call starts and that exits by itself. It runs where no one sees it
        and inside a job of its own (``AppLauncher.run(hidden=True)``, on
        Windows): its exit, the timeout or an interrupt ends it and every
        process it started (the asset import workers among them), so none is
        left holding the project or the run's output.

        Parameters:
            platform: ``EditMode`` or ``PlayMode``.
            project: A Unity project that lists the package under ``testables``.
            test_filter: Unity's ``-testFilter`` (a name, or a regex), or ``None`` for all.
            timeout: Seconds the run may take (default :attr:`TIMEOUT`).
            pipeline: The owned project's pipeline when *project* is ``None``:
                ``"urp"``, or ``"builtin"`` for the tests of the Built-in
                pipeline (inconclusive in the URP project).

        Returns:
            dict: :meth:`parse`'s counts and verdict (``ok``), plus ``code``
            (the editor's exit code), ``project`` and ``log`` (the editor
            log's path).

        Raises:
            RuntimeError: An editor has the project open, the run outran
                *timeout* (the editor and what it started are ended), or it
                wrote no results; the message gives the reason read from the
                editor log, the log's last lines and its path.
            FileNotFoundError: No editor of the project's version is installed.
        """
        project = project or cls.build(pipeline=pipeline)
        cls.refuse_open(project)
        exe = cls.editor(project)
        out = Path(ptk.TempArtifacts("unity_tests", max_age_days=2).dir_path())
        results, log = out / "results.xml", out / "editor.log"
        args = [
            "-batchmode", "-projectPath", project,
            "-runTests", "-testPlatform", platform,
            "-testResults", str(results), "-logFile", str(log),
        ]
        if test_filter:
            args += ["-testFilter", test_filter]
        limit = timeout or cls.TIMEOUT
        try:
            proc = ptk.AppLauncher.run(exe, args=args, timeout=limit, hide_window=True, hidden=True)
        except subprocess.TimeoutExpired:
            tail, last = cls._log_tail(log)
            why = cls.diagnose(None, tail) or "it was still running"
            raise RuntimeError(
                f"Unity's tests did not finish within {limit:g} s, so the editor and every process it "
                f"started were ended ({why}); the editor log: {log}{last}"
            ) from None
        code = getattr(proc, "returncode", None)
        if not results.is_file():
            tail, last = cls._log_tail(log)
            why = cls.diagnose(code, tail) or cls.EXIT_CODES.get(code, f"exit code {code}")
            raise RuntimeError(f"Unity wrote no test results ({why}); the editor log: {log}{last}")
        outcome = cls.parse(str(results))
        outcome.update(code=code, project=project, log=str(log))
        return outcome

    @classmethod
    def imported(cls, version: Optional[str] = None, pipeline: str = "urp") -> Optional[Path]:
        """The owned project for *version* (the newest) once an editor imported
        it (``Library/ScriptAssemblies`` exists), else ``None``."""
        try:
            folder = cls.folder(version or cls.newest_editor(), pipeline)
        except (FileNotFoundError, RuntimeError):
            return None
        return folder if (folder / "Library" / "ScriptAssemblies").is_dir() else None
