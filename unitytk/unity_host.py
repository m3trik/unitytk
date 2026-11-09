# !/usr/bin/python
# coding=utf-8
"""UnityHost -- the ``unity`` operation host: calls in a Unity editor the tooling starts.

unitytk's runner for ``python -m pythontk ops`` and every other front end of
``ptk.OperationCatalog``: an operation declared with ``host="unity"`` (the C#
``[Operation]`` methods of unitytk's kernel, catalogued in
``editor_operations.json``) runs in a Unity editor this tooling launched, a
new one per call from the command line, or, for a front end that lives long
(the MCP server), the one ``ptk.SessionOperationHost`` keeps per session and
project. Never in an editor a person opened: Unity's own project lock refuses a
second editor on a project already open, and nothing here attaches to one.

The editor serves the calls itself (the kernel's ``BatchServer`` per call, its
``SessionServer`` for a session, in C#), so this class only says how to start
it: the editor version the project names (a project is never opened in another,
which would upgrade it), the arguments, and why a start failed. A call names
its project through ``scenes``: the project folder itself, or ``.unity`` scenes
inside it; with none, the session's last project, else the owned project
(:class:`~unitytk.project_builder.ProjectBuilder`). A project that does not
install this unitytk's package is refused, with the operation that installs it
(``unity_project.install``) as the fix. The first call on the owned project
makes it (about four minutes, once per editor): each step is logged, and a
cancel of the call stops Unity's CLI and the editor it started.

Editors come from Unity's own CLI (``UnityCli``); a licence is the Hub's
(signed in once there, or ``unity auth login``), never this tooling's.
"""

import json
import logging
import os
import subprocess
from pathlib import Path
from typing import List, Literal, Optional, Sequence

import pythontk as ptk

from unitytk.project_builder import ProjectBuilder
from unitytk.unity_cli import UnityCli
from unitytk.unity_project import UnityProject


class _UnityHostInternal(UnityProject):
    """Internal helpers for :class:`UnityHost`; a project's files are
    :class:`~unitytk.unity_project.UnityProject`'s."""

    #: The kernel's per-call entry point (C#).
    BATCH_METHOD = "M3trik.Unitytk.Kernel.BatchServer.Run"
    #: The git URL a release install names, ``{version}`` its tag's.
    GIT_URL = "https://github.com/m3trik/unitytk.git?path=/com.m3trik.unitytk#v{version}"
    #: Seconds asking the remote for a release's tag may take before it counts as unanswered.
    TAG_TIMEOUT = 20.0
    #: Seconds a call may take, and a cancelled one has before the editor is stopped.
    TIMEOUT = 1800.0
    GRACE = 30.0
    #: Where :meth:`UnityHost.install` has Unity get the package.
    SOURCES = ("local", "git")

    @classmethod
    def _sandbox_folder(cls) -> Optional[Path]:
        """Where the newest editor's owned project lives (made or not); ``None``
        when no usable editor is installed."""
        try:
            return ProjectBuilder.folder(ProjectBuilder.newest_editor())
        except FileNotFoundError:
            return None

    @classmethod
    def _is_sandbox(cls, project: Optional[str]) -> bool:
        """Whether *project* means the owned project (``None`` does)."""
        if project is None:
            return True
        return Path(project).resolve() == cls._sandbox_folder()

    @staticmethod
    def _scenes_of(run_dir: Path) -> List[str]:
        """The scenes a run folder's request names."""
        try:
            request = json.loads((run_dir / "request.json").read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return []
        return list(request.get("scenes") or [])

    @classmethod
    def _spec(cls, source: str) -> str:
        """The manifest value *source* names: this checkout's package
        (``file:``) or this version's git URL.

        Raises:
            FileNotFoundError: ``"local"`` outside a checkout.
        """
        import unitytk

        if source == "local":
            return cls.package_line()
        return cls.GIT_URL.format(version=unitytk.__version__)

    @classmethod
    def _remote_lists(cls, tag: str) -> Optional[bool]:
        """Whether the git URL's remote lists *tag* (``git ls-remote``):
        ``True`` or ``False`` when it answered, ``None`` when it could not be
        asked (no git, offline, no access, no answer in :attr:`TAG_TIMEOUT`).
        Never prompts for credentials."""
        remote = cls.GIT_URL.split("?", 1)[0]
        env = dict(os.environ, GIT_TERMINAL_PROMPT="0", GCM_INTERACTIVE="never")
        flags = {"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}
        try:
            answer = subprocess.run(
                ["git", "ls-remote", "--tags", remote, f"refs/tags/{tag}"],
                capture_output=True, text=True, timeout=cls.TAG_TIMEOUT, env=env,
                stdin=subprocess.DEVNULL, **flags,
            )
        except (OSError, subprocess.TimeoutExpired):
            return None
        if answer.returncode != 0:
            return None
        refs = {line.split("\t")[-1].strip() for line in (answer.stdout or "").splitlines()}
        return f"refs/tags/{tag}" in refs or f"refs/tags/{tag}^{{}}" in refs

    @classmethod
    def _verify_tag(cls, ctx: Optional["ptk.OpContext"]) -> None:
        """Refuse a git install of a version the remote does not list; where
        the remote cannot be asked, go on with one warning saying so.

        Raises:
            ValueError: The remote answered and lists no tag of this version.
        """
        import unitytk

        tag = f"v{unitytk.__version__}"
        remote = cls.GIT_URL.split("?", 1)[0]
        listed = cls._remote_lists(tag)
        if listed is False:
            raise ValueError(
                f"{remote} lists no tag {tag}: unitytk {unitytk.__version__} is not released there, so "
                "Unity could not resolve the package. Install from a released unitytk, or with "
                "source='local' for this checkout"
            )
        if listed is None:
            message = (
                f"the tag {tag} was not verified: {remote} could not be asked (no git, offline, or no "
                "access), so the manifest pins it unchecked; Unity resolves nothing if the tag does not exist"
            )
            if ctx is not None:
                ctx.issue("unity.install.unverified_tag", message, "warning", location=remote)
            else:
                logging.getLogger(__name__).warning("unity: %s", message)

    @staticmethod
    def _log_progress(fraction: float, message: str) -> None:
        """The owned project's build, said in the log (a call that makes it
        waits minutes, and nothing else reports it)."""
        logging.getLogger(__name__).warning("unity: %s (%d%%)", message, round(fraction * 100))


class UnityHost(_UnityHostInternal):
    """How the ``unity`` host starts an editor (see the module docstring)."""

    @classmethod
    def operation_host(cls) -> "ptk.BatchOperationHost":
        """The ``unity`` runner (``pythontk.operation_hosts``): a new editor
        per call, or one kept per session and project."""
        return ptk.BatchOperationHost(
            cls.find_editor,
            command=cls.command,
            serve=cls.serve,
            instance=cls.instance,
            diagnose=cls.diagnose,
            timeout=cls.TIMEOUT,
            grace=cls.GRACE,
        )

    @classmethod
    def instance(cls, scenes: Sequence[str]) -> Optional[str]:
        """The project whose editor a call needs, its key for a session: the
        one *scenes* name, else the owned project (one editor per project). A
        path in no project keys to the owned project too, whose editor refuses
        it: a typo never starts a second editor."""
        try:
            project = cls.project_of(scenes)
        except ValueError:
            project = None
        if project is not None:
            return project
        folder = cls._sandbox_folder()
        return None if folder is None else str(folder)

    @classmethod
    def project_of(cls, scenes: Sequence[str]) -> Optional[str]:
        """The project *scenes* name: the first path, a project folder or a file
        inside one; ``None`` when they name none.

        Raises:
            ValueError: The first path is in no Unity project.
        """
        if not scenes:
            return None
        path = Path(scenes[0]).resolve()
        for folder in (path, *path.parents):
            if cls.is_project(folder):
                return str(folder)
        raise ValueError(f"{scenes[0]} is in no Unity project (no Assets and ProjectSettings above it)")

    @classmethod
    def find_editor(cls, project: Optional[str] = None) -> Optional[str]:
        """The editor *project* needs: the version it names, never another (a
        project opened in another version is upgraded); with none, the newest
        installed at or above the package's floor.

        Raises:
            FileNotFoundError: That version is not installed, or none is.
        """
        if project and not cls._is_sandbox(project):
            return cls.editor(project)
        return UnityCli.editors()[ProjectBuilder.newest_editor()]

    @classmethod
    def sandbox(cls, wait: float = 0.0, ctx: Optional["ptk.OpContext"] = None) -> str:
        """The owned project for the newest editor (:meth:`ProjectBuilder.build`),
        linked to this checkout's package; never while an editor has it open
        (waiting up to *wait* seconds for one closing).

        Its first build takes minutes: *ctx* (else one that logs each step and
        stops at the ambient cancel) hears its progress, and a cancel stops
        Unity's CLI and the editor it started.

        Raises:
            FileNotFoundError: No usable editor is installed.
            RuntimeError: Unity could not make the project, or one has it open.
            ptk.OperationCancelled: The call was cancelled while it was made.
        """
        folder = cls._sandbox_folder()
        if folder is not None and cls.is_project(folder):
            cls.refuse_open(str(folder), wait)
        return ProjectBuilder.build(ctx=ctx or ptk.OpContext(on_progress=cls._log_progress))

    @classmethod
    def kernel_source(cls, project: str) -> Optional[str]:
        """How *project* gets this unitytk's package, as Unity resolves it.

        ``"local"``: a manifest ``file:`` dependency on this checkout's package
        folder. ``"git"``: this version's git URL. A folder of the package's
        name in the project's ``Packages/`` (an embedded copy, which Unity
        resolves over the manifest) is no kernel whatever the manifest names.

        Returns:
            str | None: One of those, or ``None``: install it
            (``unity_project.install``).
        """
        import unitytk

        if Path(project, "Packages", cls.PACKAGE).is_dir():
            return None
        try:
            manifest = cls.manifest(project)
        except ValueError:
            return None
        dependencies = manifest.get("dependencies")
        spec = dependencies.get(cls.PACKAGE) if isinstance(dependencies, dict) else None
        if not isinstance(spec, str):
            return None
        if spec.startswith("file:"):
            return "local" if cls.links_here(project, spec) else None
        if spec == cls.GIT_URL.format(version=unitytk.__version__):
            return "git"
        return None

    @classmethod
    def _ready(cls, project: Optional[str]) -> str:
        """*project* (the owned project when ``None``) to launch an editor on:
        refused when it lacks this unitytk's package (:meth:`kernel_source`), or
        while an editor has it open (one this tooling stopped has
        :attr:`RELEASE_WAIT` seconds to let it go).

        Raises:
            RuntimeError: The project lacks the package, or an editor has it open.
        """
        if cls._is_sandbox(project):
            return cls.sandbox(cls.RELEASE_WAIT)
        cls.refuse_open(project, cls.RELEASE_WAIT)
        if cls.kernel_source(project) is None:
            raise RuntimeError(
                f"{project} does not install this unitytk's package: install it first "
                f"(unity_project.install project={project})"
            )
        return project

    @classmethod
    def serve(cls, folder: Path, owner: Optional[int], project: Optional[str]) -> List[str]:
        """The arguments that start an editor serving the session *folder*
        (the kernel's ``SessionServer``), on *project* (a session's key), ending
        when *owner* (a pid) does.

        Raises:
            ValueError: *project* is ``None``: a session names its project.
        """
        if project is None:
            raise ValueError("a session's editor serves the project its key names, and this one names none")
        args = [
            "-batchmode",
            "-projectPath", cls._ready(project),
            "-logFile", "-",
            "-unitytkServe", str(folder),
        ]
        return args + (["-unitytkOwner", str(owner)] if owner else [])

    @classmethod
    def command(cls, run_dir: Path) -> List[str]:
        """The arguments that start an editor answering one run folder (the
        kernel's ``BatchServer``), on the project its request's scenes name."""
        project = cls.project_of(cls._scenes_of(Path(run_dir)))
        return [
            "-batchmode",
            "-projectPath", cls._ready(project),
            "-logFile", "-",
            "-executeMethod", cls.BATCH_METHOD,
            "-unitytkRun", str(run_dir),
        ]

    @classmethod
    def install(
        cls,
        project: str,
        source: Literal["local", "git"] = "local",
        ctx: Optional["ptk.OpContext"] = None,
    ) -> List[str]:
        """Install unitytk's package in *project*: one line in its manifest.

        *source* says where Unity gets the package. ``"local"``: this checkout's
        package folder (``file:``), for development; ``"git"``: this version's
        tag of the repo (``?path=/com.m3trik.unitytk#v<version>``), refused where
        the remote answers and lists no such tag, and written with one warning
        where it cannot be asked (``git ls-remote``). Every other
        entry is kept. Run again, it moves the project to this checkout or this
        version; with nothing to change, it plans nothing. Unity fetches the
        package when it next opens the project.

        Parameters:
            project: The Unity project folder.
            source: ``"local"`` or ``"git"``.
            ctx: The operation context, asked before the manifest is written
                (``should_write``): a dry run lists it and changes nothing.

        Returns:
            The files written (in a dry run, none).

        Raises:
            ValueError: *project* is no Unity project, its manifest is no JSON
                object, it embeds a copy of the package (remove it from
                ``Packages/`` first), *source* is unknown, or ``"git"`` names a
                tag the remote answers it does not list.
            FileNotFoundError: ``"local"`` outside a checkout.
            RuntimeError: An editor has it open (its scripts would recompile
                under the person working in it): install once it is closed.
        """
        if source not in cls.SOURCES:
            raise ValueError(f"source {source!r} is not one of {list(cls.SOURCES)}: pass one of them")
        if not cls.is_project(project):
            raise ValueError(f"{project} is no Unity project (no Assets and ProjectSettings)")
        if Path(project, "Packages", cls.PACKAGE).is_dir():
            raise ValueError(
                f"{project} embeds a copy of {cls.PACKAGE} in Packages/, which Unity resolves "
                "over the manifest: move it out, then install again"
            )
        manifest = cls.manifest(project)
        spec = cls._spec(source)
        if (manifest.get("dependencies") or {}).get(cls.PACKAGE) == spec:
            return []
        if source == "git":
            cls._verify_tag(ctx)
        path = cls.manifest_path(project)
        if ctx is not None and not ctx.should_write(path, f"install {cls.PACKAGE} from {source}"):
            return []
        updated = json.loads(json.dumps(manifest))
        updated.setdefault("dependencies", {})[cls.PACKAGE] = spec
        return [cls.write_manifest(project, updated)]
