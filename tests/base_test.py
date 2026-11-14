# !/usr/bin/python
# coding=utf-8
"""
Base Test Class for UnityTk Tests

Provides common functionality for all unitytk test cases.
"""

import logging
import unittest
import sys
import os
import re
import shutil
import time
from pathlib import Path
from typing import Any, List, Optional, Set, Tuple

# Make the sibling ecosystem repos importable when a test file runs outside
# the runner. Derived from this file's location so any checkout works. Insert
# the REPO roots, never the bare workspace root: the workspace root resolves
# each sibling as an empty namespace package whenever the real one isn't
# importable elsewhere.
_REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS_ROOT = os.path.dirname(_REPO_ROOT)
# The host repo is inserted unconditionally (works whatever the checkout is
# named); pythontk, the one package unitytk depends on, by its canonical name.
if _REPO_ROOT not in sys.path:
    sys.path.insert(0, _REPO_ROOT)
for _pkg in ("pythontk",):
    _pkg_root = os.path.join(SCRIPTS_ROOT, _pkg)
    if os.path.isdir(_pkg_root) and _pkg_root not in sys.path:
        sys.path.insert(0, _pkg_root)

from unitytk.unity_project import UnityProject  # noqa: E402  (after the path)


#: Root for the machine-local production assets the integration suites replay
#: (large client FBX/audio that cannot be checked in). Unset -> a can't-exist
#: sentinel, so each suite's existing availability guard skips rather than the
#: repo hardcoding one machine's studio drive layout: client paths and program
#: identifiers in tracked files leak business relationships, and this repo
#: ships a wheel and is headed public.
#:
#: The sentinel matters: ``""`` would betray any guard written with pathlib,
#: because ``Path("").exists()`` is ``Path(".").exists()`` -- True -- while
#: ``os.path.exists("")`` is False.
TEST_ASSETS = os.environ.get("UNITYTK_TEST_ASSETS", "") or os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "__missing_test_asset__"
)


class TestAssets:
    """Where the machine-local integration fixtures live, and what they are called.

    The fixtures are named for what they *exercise* -- a multi-section audio
    FBX, a keyed-visibility FBX -- not for the client program they came from.
    Program identifiers in tracked identifiers leak business relationships the
    same way a studio drive path does, and this repo ships a wheel and is headed
    public, so the 2026-08-17 pass renamed them along with the modules and
    classes that replayed them.
    """

    @classmethod
    def root(cls) -> str:
        """The fixture root; read live so a test can redirect it."""
        return TEST_ASSETS

    @classmethod
    def path(cls, *parts: str) -> str:
        """A path under :meth:`root`; an unset root still yields a can't-exist
        path under either guard style (``os.path.exists`` and
        ``Path(...).exists()``). The pre-2026-08-17 fixture names resolved here
        too for one release; that alias map was deleted 2026-09-23.
        """
        return os.path.join(cls.root(), *parts)


def asset_path(*parts: str) -> str:
    """A path under :data:`TEST_ASSETS`; can't-exist when that root is unset.

    Thin delegator kept for the call sites (and for parity with the other
    ``base_test.asset_path``); the resolution rules live on :class:`TestAssets`.
    """
    return TestAssets.path(*parts)


class PackageSources:
    """The Unity package in this checkout, by bundle path: what a suite
    installs (``UnityHost.install`` links this folder) and reads back.

    Names are bundle paths, "/"-joined below the package folder
    (``"Features/RenderEffects/Runtime/Fade.cs"``), resolved
    here so a moved file is a loud, one-place fix.
    """

    #: The Unity package in this checkout (``com.m3trik.unitytk/``).
    DIR = os.path.join(_REPO_ROOT, "com.m3trik.unitytk")

    @classmethod
    def path(cls, name: str) -> str:
        """Where bundle path *name* lives in the checkout."""
        return os.path.join(cls.DIR, *name.split("/"))

    @classmethod
    def files(cls) -> List[str]:
        """Every package file but the ``.meta`` ones, as bundle paths, sorted:
        what Unity imports from the installed package, each with its committed
        ``.meta`` (``cls.path(name + ".meta")``). Unity imports nothing below
        a folder it hides (a ``~`` suffix, ``Samples~``; a ``.`` prefix), so
        neither is listed."""
        root = Path(cls.DIR)
        return sorted(
            p.relative_to(root).as_posix()
            for p in root.rglob("*")
            if p.is_file()
            and not p.name.endswith(".meta")
            and not any(
                part.endswith("~") or part.startswith(".")
                for part in p.relative_to(root).parts[:-1]
            )
        )

    @staticmethod
    def folders(names) -> List[str]:
        """Every folder holding one of *names* (bundle paths), sorted, a parent first."""
        found = set()
        for name in names:
            parts = name.split("/")[:-1]
            found.update("/".join(parts[: d + 1]) for d in range(len(parts)))
        return sorted(found)


class EnvSkip:
    """A skip taken because the MACHINE is missing something, not the code.

    A suite that skips for want of Unity or a fixture root has verified
    nothing -- but ``unittest`` files it in the same undifferentiated ``skipped``
    bucket as a deliberate "not applicable here" skip, so a run that executed
    none of the DCC->Unity consumer contract prints the same trailing ``OK`` as
    one that executed all of it. Marking the reason lets ``run_tests.py`` count
    the two separately, name what was missing, keep the run off the README
    badge, and refuse to call it a plain pass.

    Use :meth:`unless` in place of :func:`unittest.skipUnless` wherever the
    condition is an environment probe.
    """

    #: Prefix stamped on every environment-scoped skip reason.
    MARKER = "[env]"

    @classmethod
    def reason(cls, text) -> str:
        """*text* stamped with :attr:`MARKER`. Idempotent."""
        text = str(text)
        return text if cls.is_env_reason(text) else f"{cls.MARKER} {text}"

    @classmethod
    def is_env_reason(cls, text) -> bool:
        """True when *text* was produced by :meth:`reason`."""
        return str(text).startswith(cls.MARKER)

    @classmethod
    def unless(cls, condition, text):
        """:func:`unittest.skipUnless` with an environment-marked reason."""
        return unittest.skipUnless(condition, cls.reason(text))

    @classmethod
    def collect(cls, result) -> list:
        """``[(test, reason), ...]`` -- the environment-scoped skips in *result*."""
        return [
            (test, why)
            for test, why in getattr(result, "skipped", [])
            if cls.is_env_reason(why)
        ]

    @classmethod
    def count(cls, result) -> int:
        """How many tests never ran because the environment was missing."""
        return len(cls.collect(result))

    @classmethod
    def tally(cls, result) -> dict:
        """``{reason: count}`` so a summary can name WHAT was missing."""
        counts: dict = {}
        for _test, why in cls.collect(result):
            counts[str(why)] = counts.get(str(why), 0) + 1
        return counts


class UnityEnv:
    """The editor the suites run, through unitytk's own API, and why a
    Unity-side step could not run on THIS machine.

    :meth:`editor` picks it (Unity's CLI, ``UnityCli``), :meth:`create_project`
    makes a suite's blank project with the CLI, ``UnityHost.install`` installs
    the package in it, and :meth:`run` starts a batch editor on it. Session-safe:
    each is a new process on a project this run made, and Unity refuses a
    project another editor holds.

    Unity's two environmental failures only surface mid-test -- a project the
    CLI could not make, and an editor that runs and exits without a batch
    license -- so the suites take them as in-body
    :meth:`~unittest.TestCase.skipTest` calls. Eight
    suites hand-copied the two reason strings and fifteen of the copies forgot
    :meth:`EnvSkip.reason`, so those skips never reached the ``SKIPPED (env)``
    tally: a Unity machine without a BATCH license stamped the README badge
    and exited 0 under ``--strict-env``, which is exactly the hole ``EnvSkip``
    was added to close. The reasons live here so there is one string to mark,
    and ``test_base_test.UnityEnvTest`` fails the run on a fresh copy.

    The reasons are CONSTANT on purpose -- no return code, no log path.
    :meth:`EnvSkip.tally` groups by reason text, so an interpolated per-run temp
    path would split one environmental cause across eight summary lines. Log the
    return code and the log path at the call site instead.
    """

    #: Names the editor a run uses: an installed version (``6000.3.10f1``) or an
    #: editor's executable. Unset: the newest installed at or above the
    #: package's floor (``ProjectBuilder.FLOOR``).
    EDITOR_VAR = "UNITYTK_TEST_EDITOR"

    #: No editor to run (the suites' ``EnvSkip.unless`` gates say the same).
    NO_EDITOR = "no Unity editor found"

    #: :attr:`EDITOR_VAR` names an executable Unity's CLI does not list, so it
    #: has no version the CLI could make a project with (:meth:`create_project`).
    UNLISTED_EDITOR = (
        "UNITYTK_TEST_EDITOR names an editor Unity's CLI does not list -- "
        "install it through Unity Hub, or name its version"
    )

    #: The template of the suites' projects: blank Built-in, as they always were.
    PIPELINE = "builtin"

    #: :meth:`editors`' answer, read once a run (every gated test asks at import).
    _editors: Optional[dict] = None

    #: The CLI could not make the project (:meth:`create_project`).
    PROJECT_CREATION = (
        "Unity batch project creation failed -- typically no batch-mode "
        "license on this machine (the test log says what Unity's CLI answered)."
    )

    #: The editor ran but exited unlicensed (:meth:`is_licence_failure`).
    BATCH_LICENSE = (
        "Unity exited without a valid batch license -- see the run's unity_log.txt"
    )

    #: How far below a project root URP's deepest import lands, and the longest
    #: root that leaves it room: ``UnityProject``'s, the one home of the margin
    #: (an aiStandardSurface with an opacity read as an importer bug once a
    #: project root was 25 characters too deep, 2026-10-19).
    URP_IMPORT_DEPTH = UnityProject.URP_IMPORT_DEPTH
    URP_ROOT_MAX = UnityProject.URP_ROOT_MAX

    @classmethod
    def project_creation_reason(cls) -> str:
        """Env-marked reason for a failed batch project creation."""
        return EnvSkip.reason(cls.PROJECT_CREATION)

    @classmethod
    def editors(cls) -> dict:
        """``{version: executable}`` of the installed editors (``UnityCli.editors``),
        read once a run; empty without Unity's CLI or an editor."""
        if cls._editors is None:
            from unitytk.unity_cli import UnityCli

            try:
                cls._editors = UnityCli.editors()
            except (FileNotFoundError, RuntimeError):
                cls._editors = {}
        return dict(cls._editors)

    @classmethod
    def selected(cls) -> Tuple[Optional[str], Optional[str]]:
        """``(version, executable)`` of the editor a run uses: the one
        :attr:`EDITOR_VAR` names (its version ``None`` for an executable the
        CLI does not list), else the newest at or above ``ProjectBuilder.FLOOR``;
        ``(None, None)`` when there is none."""
        from unitytk.project_builder import ProjectBuilder
        from unitytk.unity_cli import UnityCli

        editors = cls.editors()
        named = os.environ.get(cls.EDITOR_VAR, "").strip()
        if not named:
            version = UnityCli.newest(list(editors), ProjectBuilder.FLOOR)
            return version, editors.get(version)
        if named in editors:
            return named, editors[named]
        if not os.path.isfile(named):
            return None, None
        key = os.path.normcase(os.path.abspath(named))
        version = next(
            (v for v, exe in editors.items() if os.path.normcase(os.path.abspath(exe)) == key),
            None,
        )
        return version, named

    @classmethod
    def editor(cls) -> Optional[str]:
        """The executable of the editor a run uses (:meth:`selected`), or ``None``."""
        return cls.selected()[1]

    @classmethod
    def available(cls) -> bool:
        """Whether there is an editor to run: the suites' ``EnvSkip.unless`` gate."""
        return cls.editor() is not None

    @classmethod
    def create_project(cls, test: unittest.TestCase, project: str) -> None:
        """Make *project* a blank Built-in project with Unity's CLI
        (``UnityCli.create_project``, ``ProjectBuilder.TEMPLATES``) on the
        :meth:`selected` editor, or end *test* with the env skip saying why the
        machine could not: no editor, a named editor the CLI does not list
        (:attr:`UNLISTED_EDITOR`), the licence wall
        (:meth:`is_licence_failure`, :meth:`license_reason`), else any other
        failed creation (:meth:`project_creation_reason`).

        Parameters:
            test: The running test case.
            project: The project folder to make; it must not exist.
        """
        from unitytk.project_builder import ProjectBuilder
        from unitytk.unity_cli import UnityCli

        version, executable = cls.selected()
        if executable is None:
            test.skipTest(EnvSkip.reason(cls.NO_EDITOR))
        if version is None:
            # The CLI would make it with its own default editor, and :meth:`run`
            # then open it in this one: another version's project.
            test.skipTest(EnvSkip.reason(cls.UNLISTED_EDITOR))
        log = logging.getLogger(type(test).__module__)
        try:
            UnityCli.create_project(
                project, version=version, template=ProjectBuilder.TEMPLATES[cls.PIPELINE]
            )
        except (FileNotFoundError, RuntimeError) as exc:
            log.info("Unity could not make %s: %s", project, exc)
            if cls.is_licence_failure(None, str(exc)):
                test.skipTest(cls.license_reason())
            test.skipTest(cls.project_creation_reason())
        if not (os.path.isdir(os.path.join(project, "Assets")) and os.path.isdir(os.path.join(project, "ProjectSettings"))):
            log.info("Unity reported %s made, but it holds no Assets and ProjectSettings", project)
            test.skipTest(cls.project_creation_reason())

    @classmethod
    def run(
        cls,
        test: unittest.TestCase,
        project: str,
        method: Optional[str] = None,
        log: Optional[str] = None,
        extra_args: Tuple[str, ...] = (),
        timeout: float = 1800.0,
    ) -> int:
        """Run a batch editor (:meth:`editor`) on *project* until it quits, and
        return its exit code: ``-batchmode -projectPath <project>
        [-executeMethod <method>] [-logFile <log>] -quit <extra_args>``,
        through ``ptk.AppLauncher.run``. No ``-nographics``: a verifier that
        renders needs the graphics device.

        Skips (env) without an editor; fails *test* when the editor outruns
        *timeout* (``AppLauncher.run`` ends it).

        Parameters:
            test: The running test case.
            project: A project this run made (never one a person has open).
            method: The static C# method to execute (``Class.Method``).
            log: The editor's ``-logFile``.
            extra_args: More editor arguments.
            timeout: Seconds the run may take.
        """
        import subprocess

        import pythontk as ptk

        executable = cls.editor()
        if executable is None:
            test.skipTest(EnvSkip.reason(cls.NO_EDITOR))
        args = ["-batchmode", "-projectPath", project]
        if method:
            args += ["-executeMethod", method]
        if log:
            args += ["-logFile", log]
        args += ["-quit", *extra_args]
        try:
            done = ptk.AppLauncher.run(executable, args, timeout=timeout)
        except subprocess.TimeoutExpired:
            test.fail(f"Unity timed out after {timeout:g} s -- see {log}")
        return done.returncode

    @classmethod
    def is_licence_failure(cls, code: Optional[int], log_text: str) -> bool:
        """Whether an editor (or the CLI) failed for want of a licence, as
        ``UnityProject.diagnose`` reads its exit code and log (a compile error
        or a crash is a failure, whatever licensing lines the log carries)."""
        why = UnityProject.diagnose(code, log_text or "") or ""
        return why.startswith("Unity could not get a licence")

    @classmethod
    def license_reason(cls) -> str:
        """Env-marked reason for an editor that exited without a batch license."""
        return EnvSkip.reason(cls.BATCH_LICENSE)

    @classmethod
    def require_results(
        cls,
        test: unittest.TestCase,
        results_path: str,
        returncode: Optional[int],
        log_path: Optional[str],
    ) -> None:
        """End a batch verifier's test when it wrote no *results_path*.

        The check every Unity verifier makes after its run: SKIP when the editor
        ran unlicensed (:meth:`is_licence_failure` -- the environment,
        not the code), FAIL when it ran and wrote nothing. A no-op when the
        results exist. Asserting the file alone failed every such suite on a
        machine signed out of the Hub.

        Parameters:
            test: The running test case.
            results_path: The file the verifier writes.
            returncode: The editor's exit code; ``None`` when it was not waited on.
            log_path: Its ``-logFile``, read for the licence markers.
        """
        if os.path.exists(results_path):
            return
        log_text = ""
        if log_path and os.path.exists(log_path):
            with open(log_path, encoding="utf-8", errors="replace") as fh:
                log_text = fh.read()
        if cls.is_licence_failure(returncode, log_text):
            # The reason stays constant (it is tallied); the run's facts go here.
            logging.getLogger(type(test).__module__).info(
                "Unity batch license failure (rc=%s) -- see %s", returncode, log_path
            )
            test.skipTest(cls.license_reason())
        test.fail(
            f"{os.path.basename(results_path)} not created (Unity rc={returncode})"
            f" -- check {log_path}"
        )


class SuiteOptions:
    """Opt-in switches the integration suites read from the environment.

    Package-scoped names, so nothing in a public repo is keyed to a client
    program -- the variables these replaced on 2026-08-17 were named for
    one (see CHANGELOG.md). Renamed outright rather than aliased: an alias would
    have put the old names back into tracked source, which is the thing the
    rename removed. The asset FILENAMES were renamed too; a one-release alias
    map bridged the ones already staged on a user's disk (deleted 2026-09-23).
    """

    #: ``1`` keeps a run's generated Unity project for post-mortem inspection.
    PRESERVE = "UNITYTK_PRESERVE"

    @classmethod
    def preserve(cls) -> bool:
        """Keep this run's Unity project instead of deleting it? (*PRESERVE*
        set to exactly ``1``, whitespace tolerated.)"""
        return os.environ.get(cls.PRESERVE, "").strip() == "1"


class TempProjects:
    """Per-run scratch dirs under ``tests/temp_tests/``, removed with their suite.

    The integration suites generate a whole Unity project per run -- 1.65 GB for
    a URP one. They used to keep it on purpose (a failed import is diagnosable
    from the project Unity built), so only the age sweep below reclaimed any:
    75 runs and 10 GB on 2026-10-20, a full O: and a production module
    truncated to 0 bytes mid-export. Now the suite owns its run:
    :meth:`allocate` with the suite's class removes it when the class is done,
    pass or fail, and ``UNITYTK_PRESERVE=1`` (:class:`SuiteOptions`) keeps it
    for a post-mortem -- the opt-in, said in the log.

    :class:`pythontk.TempArtifacts` in ``detached`` policy owns the folders,
    and the reason the root AGENTS.md rule names it: allocation sweeps
    same-prefix entries older than :attr:`MAX_AGE_DAYS` while leaving the
    current run untouched -- what a killed run, a held file or a preserved run
    left goes that way. The sweep is prefix-scoped, so a suite reclaims only
    its own prior runs -- every suite shares this one base directory.

    Prefix scoping leaves one hole: a prefix nobody allocates again is nobody's
    to reclaim (an env-skipped suite allocates nothing; a retired one never
    will). :meth:`sweep_all` covers every prefix present, on the same bound and
    through the same primitive; ``run_tests.UnityTestRunner`` calls it once at
    start-up and nothing else needs to.

    Unity's CLI registers every project it makes in the Hub's project list,
    and the Hub keeps listing a folder after it is gone, so a run's projects
    leave the list as their folders go (:meth:`forget`): when the suite
    releases its run, and at the start-up sweep for what a killed run left.
    The owned project a person's own tooling keeps stays listed on purpose;
    the run's own (:meth:`owned`) is a run like any other.

    Not the system temp dir: these artifacts are deliberately durable and large,
    the repo lives on a roomy drive where C: is routinely full, and
    ``.gitignore`` already covers ``tests/temp_tests/``.
    """

    #: In-repo scratch base shared by every integration suite: the default.
    DEFAULT_BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "temp_tests")

    @classmethod
    def base_for(cls, environ) -> str:
        """The base *environ* names: ``UNITYTK_TEMP_PROJECTS``, else :attr:`DEFAULT_BASE`
        (a roomier drive: a URP project is ~2 GB, and the repo's drive can be the full one)."""
        return environ.get("UNITYTK_TEMP_PROJECTS") or cls.DEFAULT_BASE

    #: Where this run allocates (:meth:`base_for` of the environment).
    BASE = os.environ.get("UNITYTK_TEMP_PROJECTS") or DEFAULT_BASE

    #: Days a run its suite did not remove (preserved, killed, or held by a
    #: process) stays before a later allocation reclaims it. Deliberately short:
    #: an integration failure is normally read in the same session. Raise it on
    #: a class that needs longer -- the bound is what matters, not the number.
    MAX_AGE_DAYS = 3

    #: How long a release waits for whatever still holds part of a run to let
    #: go -- an editor the run started that is still exiting, its import
    #: workers (measured: an owned project's ``Library/ArtifactDB`` held at the
    #: end of a pytest run, WinError 32) -- and the pause between tries. It
    #: waits and never stops a process: what it did not launch is not its to end.
    RELEASE_WAIT_SECONDS = 60.0
    RELEASE_PAUSE_SECONDS = 1.0

    @classmethod
    def project(cls, run_dir: str, name: str = "p") -> str:
        """The Unity project root *name* in *run_dir*: ``<run>/p``, or one named
        for its pipeline (``"urp"``) when a run builds several.

        Directly under the run and short, because the editor is not long-path
        aware: URP's deepest shader graph lands ``UnityEnv.URP_IMPORT_DEPTH``
        characters below a project root, so a URP root must stay within ~101.
        ``<run>/urp/UnityProject`` put the material suite at 107 from the
        in-repo base and failed every URP suite (2026-10-19). A run's logs and
        results sit beside its projects, never in one.

        Not created: Unity's CLI makes the project (``UnityEnv.create_project``)
        and refuses a folder that exists.

        Parameters:
            run_dir: An :meth:`allocate` directory (or a folder in one).
            name: The project's folder name; keep it as short.

        Returns:
            The project root.
        """
        return os.path.join(run_dir, name)

    @classmethod
    def allocate(cls, prefix: str, owner: "Optional[type]" = None) -> str:
        """Return a fresh ``temp_tests/<prefix>_<tag>/`` for this run.

        With *owner* -- the suite's ``TestCase`` class, ``cls`` in its
        ``setUpClass`` -- the folder is the suite's: :meth:`release` removes it
        when the class is done (``addClassCleanup``: after ``tearDownClass``,
        and after a ``setUpClass`` that raised -- whatever the tests did, a
        batch Unity that crashed or timed out included -- under the runner and
        pytest alike). Without one it is left to the age sweep (this module's
        own tests of the sweep).

        Sweeps this prefix's stale runs as a side effect of allocating. Pass the
        prefix WITHOUT a trailing underscore -- the separator is added.

        Args:
            prefix (str): Suite-specific namespace, e.g. ``"shot_meta_integ"``.
            owner (type): The suite's TestCase class, whose run this is.

        Returns:
            str: The created directory.
        """
        store = cls._store(prefix)
        path = store.dir_path()
        if owner is not None:
            owner.addClassCleanup(cls.release, store, path)
        return path

    @classmethod
    def release(cls, store: "Any", path: str, at_exit: bool = False) -> bool:
        """End the run *path* (*store*'s allocation, :meth:`allocate`): remove
        it, or keep it when ``UNITYTK_PRESERVE=1`` asks -- said, with where.

        What a process still holds is waited for, up to
        :attr:`RELEASE_WAIT_SECONDS` (an editor this run started lets go of
        its project as it exits); nothing is stopped. What is still held then
        stays, named, for the age sweep.

        Parameters:
            store: The run's ``TempArtifacts`` store.
            path: The run's folder.
            at_exit: Called at interpreter exit (:meth:`owned`), after the
                run's log streams may have closed: what it says goes to the
                process's own stderr, guarded, instead of the logging tree.

        Returns:
            bool: True when the folder is gone.
        """
        held = cls._holds_a_project(path)
        if SuiteOptions.preserve():
            cls._say(
                at_exit,
                "Kept %s for inspection (%s=1); the age sweep reclaims it after %s days.",
                path,
                SuiteOptions.PRESERVE,
                cls.MAX_AGE_DAYS,
            )
            return False
        # Each try that finds the folder held would warn on the store's logger:
        # the outcome is said once, below, where it can be said.
        store.logger = cls._quiet()
        deadline = time.monotonic() + cls.RELEASE_WAIT_SECONDS
        while True:
            store.cleanup(force=True)
            if not os.path.exists(path):
                if held:
                    cls.forget(path, at_exit=at_exit)
                return True
            if time.monotonic() >= deadline:
                break
            time.sleep(cls.RELEASE_PAUSE_SECONDS)
        cls._say(
            at_exit,
            "Could not remove all of %s: a process still holds part of it after "
            "%ss; the age sweep reclaims the rest after %s days.",
            path,
            cls.RELEASE_WAIT_SECONDS,
            cls.MAX_AGE_DAYS,
        )
        return False

    @staticmethod
    def _quiet() -> logging.Logger:
        """A logger that says nothing (a store's tries, :meth:`release`)."""
        quiet = logging.getLogger(f"{__name__}.TempProjects.quiet")
        quiet.propagate = False
        quiet.disabled = True
        return quiet

    @staticmethod
    def _say(at_exit: bool, message: str, *args: Any, level: int = logging.WARNING) -> None:
        """Log *message* -- or, at interpreter exit (*at_exit*), write it to the
        process's own stderr, where a closed stream or none says nothing
        rather than raising inside logging."""
        if not at_exit:
            logging.getLogger(__name__).log(level, message, *args)
            return
        stream = sys.__stderr__
        if stream is None:
            return
        try:
            stream.write(f"[{logging.getLevelName(level)}] {message % args}\n")
            stream.flush()
        except (ValueError, OSError):
            pass

    @classmethod
    def sweep_all(cls) -> "List[str]":
        """Reclaim stale runs of EVERY prefix present under :attr:`BASE`.

        Reclamation is otherwise a side effect of :meth:`allocate`, which is
        prefix-scoped -- so a prefix that is never allocated again is never
        swept. That is the normal case here, not an edge one: a suite whose
        environment is missing env-skips and allocates nothing, and a renamed
        or retired suite never allocates at all. The runner calls this once at
        start-up; nothing else needs to.

        Same bound and same primitive as the allocation path -- this only
        widens *which* prefixes get swept, never what counts as stale. So a
        run inside :attr:`MAX_AGE_DAYS` is untouchable, including the one the
        calling process is about to (or did just) allocate.

        Prefixes are read back off the directory, since nothing records the
        set: an entry is an allocation when it matches TempArtifacts' own
        ``<prefix>_<tag>`` grammar with a lowercase-hex tag. A hand-made
        reproduction directory (``temp_tests/vis_probe/``) does not, and is
        left alone regardless of age -- it is somebody's working file, not an
        abandoned allocation.

        Returns:
            list[str]: Absolute paths removed (sorted); empty when nothing was
            stale or :attr:`BASE` does not exist.
        """
        removed = []
        for prefix in sorted(cls._allocated_prefixes()):
            removed.extend(cls._store(prefix).sweep_stale())
        cls.forget()
        return sorted(removed)

    @classmethod
    def forget(cls, root: Optional[str] = None, at_exit: bool = False) -> "List[str]":
        """Take out of the Hub's project list every project under *root*
        (:attr:`BASE` when ``None``) whose folder is gone; returns them.

        ``unity projects remove`` (``UnityCli.unregister``) never touches a
        file, and nothing outside *root* or still on disk is touched.
        Best-effort: without Unity's CLI there is no list to tidy, and a
        refusal is logged (*at_exit*: said as :meth:`release` says it).
        """
        from unitytk.unity_cli import UnityCli

        base = os.path.normcase(os.path.abspath(root or cls.BASE))
        try:
            listed = UnityCli.projects()
        except (FileNotFoundError, RuntimeError) as exc:
            cls._say(at_exit, "The Hub's project list was not read (%s)", exc, level=logging.INFO)
            return []
        gone = []
        for path in listed:
            key = os.path.normcase(os.path.abspath(path))
            if not (key == base or key.startswith(base + os.sep)) or os.path.exists(path):
                continue
            try:
                if UnityCli.unregister(path):
                    gone.append(path)
            except (FileNotFoundError, RuntimeError) as exc:
                cls._say(at_exit, "The Hub still lists %s, which is gone: %s", path, exc)
        return gone

    @classmethod
    def owned(cls) -> str:
        """Where this run's owned projects live (``ProjectBuilder.ENV_VAR``),
        unless the environment already names a folder: a run of its own under
        :attr:`BASE` (``own_<tag>``), released when the process exits.

        Not the user cache the sandbox points at: below the sandbox's root a
        URP project is too deep for the editor (``UnityProject.URP_ROOT_MAX``),
        and the person's own owned project is never a test's.

        Returns:
            str: The folder ``ProjectBuilder.folder`` builds below.
        """
        import atexit

        from unitytk.project_builder import ProjectBuilder

        named = os.environ.get(ProjectBuilder.ENV_VAR)
        if named:
            return named
        store = cls._store("own")
        path = store.dir_path()
        os.environ[ProjectBuilder.ENV_VAR] = path
        atexit.register(cls.release, store, path, True)
        return path

    # ------------------------------------------------------------------ internals
    #: TempArtifacts mints ``<prefix>_<time_ns:x>``; the lowercase-hex tail is
    #: what separates an allocated run from a hand-made scratch directory.
    _ALLOCATED = re.compile(r"^(?P<prefix>.+)_[0-9a-f]{8,}$")

    @staticmethod
    def _holds_a_project(path: str) -> bool:
        """Whether a Unity project (``ProjectSettings``) lies within *path*."""
        for _dirpath, dirnames, _files in os.walk(path):
            if "ProjectSettings" in dirnames:
                return True
            dirnames[:] = [d for d in dirnames if d not in ("Library", "Assets", "Packages", "Temp", "Logs")]
        return False

    @classmethod
    def _store(cls, prefix: str) -> "Any":
        """The :class:`pythontk.TempArtifacts` store for *prefix* under BASE."""
        import pythontk as ptk

        return ptk.TempArtifacts(
            prefix,
            policy="detached",
            dir=cls.BASE,
            max_age_days=cls.MAX_AGE_DAYS,
        )

    @classmethod
    def _allocated_prefixes(cls) -> "Set[str]":
        """Prefixes of the allocation-shaped entries currently under BASE."""
        try:
            names = os.listdir(cls.BASE)
        except OSError:
            return set()
        matches = (cls._ALLOCATED.match(n) for n in names)
        return {m.group("prefix") for m in matches if m}


class UnityTkTestCase(unittest.TestCase):
    """Base class for all unitytk test cases."""

    @classmethod
    def setUpClass(cls):
        """Set up once for all tests in the class."""
        pass

    def setUp(self):
        """Set up for each test."""
        self.temp_dirs = []

    def tearDown(self):
        """Clean up after each test."""
        for d in self.temp_dirs:
            if os.path.exists(d):
                try:
                    shutil.rmtree(d)
                except Exception as e:
                    print(f"Warning: Failed to remove temp dir {d}: {e}")

    def create_temp_dir(self):
        """Create a temporary directory that cleans up automatically.

        A scoped :class:`pythontk.TempArtifacts` dir: the run's temp root under
        the runner, and reclaimed by the age sweep if the process is killed
        before ``tearDown``.
        """
        import pythontk as ptk

        store = ptk.TempArtifacts("unitytk_test", policy="scoped")
        self.addCleanup(store.cleanup)
        tmp = store.dir_path()
        self.temp_dirs.append(tmp)
        return tmp
