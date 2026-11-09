# !/usr/bin/python
# coding=utf-8
"""
UnityTk Test Runner

Unified test runner for executing unitytk tests.
Can run tests locally or potentially interface with Unity for integration tests.

Usage:
    python run_tests.py                          # Run default core tests
    python run_tests.py unity_host               # Run specific modules
    python run_tests.py --all                    # Run ALL test modules
    python run_tests.py --list                   # List available test modules
    python run_tests.py --all --strict-env       # Fail if anything env-skipped
    python run_tests.py --edit-mode [-k NAME]    # The package's Edit Mode tests (Unity)
    python run_tests.py --all --strict-env --post-status
                                                 # The release receipt: the full suite, then
                                                 # Edit Mode on URP and Built-in, then the
                                                 # 'unity' commit status (UnityReceipt)

Environment-scoped skips:
    Most modules here gate on Unity / UNITYTK_TEST_ASSETS being present.
    Those skips are marked (``base_test.EnvSkip``) and reported on their own
    ``SKIPPED (env): N`` line: a nonzero count means the run did NOT execute the
    gated consumer contract, so the summary says ``[PARTIAL]`` instead of a
    plain pass and the README badge is refused. ``--strict-env`` turns it into
    a failing exit code for CI.

Directory Structure:
    - Main Test Suite: unitytk/test/ (Standardized test_*.py files only)
    - Temporary Tests: unitytk/test/temp_tests/ (Reproduction scripts, scratchpad tests)
"""
import importlib
import os
import sys
import unittest
from pathlib import Path

# Ensure the ecosystem repos are in path. Derived from this file's location
# so any checkout works; insert the REPO roots, never the bare workspace root
# (that resolves siblings as empty namespace packages). The host repo is
# inserted unconditionally so a checkout under any directory name still
# resolves `unitytk`; pythontk, the one package it depends on, by its name.
_REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPTS_ROOT = _REPO_ROOT.parent
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))
for _pkg in ("pythontk",):
    _pkg_root = SCRIPTS_ROOT / _pkg
    if _pkg_root.is_dir() and str(_pkg_root) not in sys.path:
        sys.path.insert(0, str(_pkg_root))


class UnityTestRunner:
    """Test runner for unitytk test suite."""

    def __init__(self):
        self.test_dir = Path(__file__).parent

        # Add project root to path so 'unitytk' package can be imported
        project_root = self.test_dir.parent
        if str(project_root) not in sys.path:
            sys.path.insert(0, str(project_root))

    def discover_tests(self, patterns=None):
        """Discover available test modules."""
        test_modules = []
        if not patterns:
            patterns = ["test_*.py"]

        for pattern in patterns:
            for file in self.test_dir.glob(pattern):
                test_modules.append(file.stem)
        return sorted(list(set(test_modules)))

    def run_tests(self, modules=None, run_all=False, update_badge=True,
                  strict_env=False, patterns=None):
        """
        Run valid unit tests.

        Args:
            modules (list): List of specific module names to run (e.g. ['unity_host']).
            run_all (bool): If True, run all discovered tests.
            patterns (list): ``-k`` test-id filters (unittest's own semantics: a
                pattern without ``*`` matches as a substring). A filtered run
                never stamps the badge.
            update_badge (bool): Stamp the README test badge. Only a run that
                covered every discovered module stamps it — a scoped run would
                otherwise publish a count that looks like a coverage regression.
            strict_env (bool): Treat environment-scoped skips as failures. Off
                by default so a dev box without Unity can still run the venv
                slice; on for CI, where a silently-skipped integration contract
                is the whole failure mode this reporting exists to expose.
        """
        # Before anything else, and specifically before reclaim_scratch():
        # base_test sits beside this file and is importable only once the test
        # dir is on the path, which is NOT automatic when the runner is loaded
        # by file path instead of run as a script.
        sys.path.insert(0, str(self.test_dir))

        self.isolate()
        self.reclaim_scratch()

        loader = unittest.TestLoader()
        if patterns:
            loader.testNamePatterns = [p if "*" in p else f"*{p}*" for p in patterns]
        suite = unittest.TestSuite()

        available_modules = self.discover_tests()
        modules_to_run = []
        # A name that resolves to nothing means the run did NOT cover what was
        # asked for; exiting 0 there lets a typo'd CI invocation pass silently.
        unknown_modules = []

        if run_all:
            modules_to_run = available_modules
        elif modules:
            # Map input names to test file names
            for mod in modules:
                # Handle cases like "unity_host" -> "test_unity_host" or "test_unity_host" -> "test_unity_host"
                candidate = f"test_{mod}" if not mod.startswith("test_") else mod
                if candidate in available_modules:
                    modules_to_run.append(candidate)
                else:
                    unknown_modules.append(mod)
                    print(f"[WARNING] Test module '{mod}' not found.")
        else:
            # Default behavior (can be just core tests, or all if no default list defined)
            modules_to_run = available_modules

        # Load tests
        print(f"Running tests: {', '.join(modules_to_run)}\n")

        # A module that fails to import contributes ZERO test cases, so counting
        # only the failures of what loaded reports a broken suite as green — and
        # every downstream gate (CI, the badge, a release check) reads that.
        load_errors = []
        for mod_name in modules_to_run:
            try:
                module = importlib.import_module(mod_name)
                suite.addTests(loader.loadTestsFromModule(module))
            except Exception as e:
                load_errors.append(mod_name)
                print(f"[ERROR] Failed to load module {mod_name}: {e}")

        # Either way the run did not cover what was requested, so it is not a pass.
        uncovered = load_errors + unknown_modules
        if load_errors:
            print(
                f"\n[FAIL] {len(load_errors)} module(s) did not load and ran no "
                f"tests: {', '.join(load_errors)}"
            )
        if unknown_modules:
            print(
                f"\n[FAIL] {len(unknown_modules)} requested module(s) do not "
                f"exist: {', '.join(unknown_modules)}"
            )

        # Run tests
        if suite.countTestCases() > 0:
            runner = unittest.TextTestRunner(verbosity=2)
            result = runner.run(suite)
            env_skipped = self.report_env_skips(result)
            # Never stamp on an uncovered run: the reduced count is missing whole
            # modules, and publishing it reads as a coverage regression. An
            # environment-scoped skip is the same defect wearing a skip — the
            # gated contract never executed, so the green count is not coverage.
            if (
                update_badge
                and not patterns
                and not uncovered
                and not env_skipped
                and sorted(modules_to_run) == available_modules
            ):
                self.update_readme_badge(result)
            failed = len(result.failures) + len(result.errors)
            if strict_env and env_skipped:
                return False
            return not uncovered and failed == 0

        print("No tests found to run.")
        return not uncovered

    @staticmethod
    def isolate() -> "str | None":
        """Keep the run off the machine: ``TestSandbox``, before any module loads.

        No real browser launch, and one throwaway temp root for the run --
        ``tempfile.gettempdir()`` here, ``TEMP``/``TMP`` for every Unity child,
        which inherit them. Without it a full run wrote into the user's real
        temp dir, and so did every child it launched. Every sibling runner
        does this; ``conftest.py`` calls this for a bare ``pytest``, which never
        runs this runner. The integration suites' Unity projects are
        unaffected: they live under ``test/temp_tests/``
        (:class:`base_test.TempProjects`), not in the temp dir, and so does
        the run's owned project (:meth:`base_test.TempProjects.owned`), whose
        path the sandbox's root would make too deep for URP.

        Best-effort: an unimportable pythontk is already a failing run, and it
        must fail on its own modules rather than here.

        Returns:
            str | None: The temp root, or None when isolation was unavailable.
        """
        try:
            from pythontk.core_utils.test_sandbox import TestSandbox

            TestSandbox.activate()
            root = TestSandbox.temp()
        except Exception as error:  # noqa: BLE001
            print(f"[WARN] test sandbox unavailable ({error}); runs use real TEMP.")
            return None
        from base_test import TempProjects

        TempProjects.owned()
        print(f"Temp: {root}")
        return root

    @staticmethod
    def reclaim_scratch() -> list:
        """Reclaim stale scratch runs under ``test/temp_tests/``, all prefixes,
        and the Hub entries of the projects they held.

        The integration suites keep their generated Unity project on purpose
        (a failed import is only diagnosable from the project Unity actually
        built), so the only lifetime they have is the age-gated sweep
        ``base_test.TempProjects.allocate`` runs -- and that sweep is
        prefix-scoped. A suite that env-skips allocates nothing, and a retired
        suite never allocates again, so their runs are reclaimed by no one:
        750 MB across 50 preserved runs when this was added.

        One all-prefix pass at start-up closes that, on the SAME bound through
        the SAME primitive -- nothing inside ``MAX_AGE_DAYS`` is touched, so
        this run's own directories (and a project still being inspected) are
        never at risk.

        Returns:
            list[str]: Absolute paths of the runs removed (empty if none).
        """
        from base_test import TempProjects

        removed = TempProjects.sweep_all()
        if removed:
            print(
                f"Reclaimed {len(removed)} scratch run(s) older than "
                f"{TempProjects.MAX_AGE_DAYS} days from {TempProjects.BASE}\n"
            )
        return removed

    @staticmethod
    def report_env_skips(result: unittest.TestResult) -> int:
        """Print the environment-skip summary; return how many there were.

        ``unittest`` files "Unity is not installed" in the same ``skipped`` bucket
        as "not applicable on this platform", so its trailing ``OK`` is identical
        whether the gated contract ran or was skipped wholesale -- the exact way
        12 integration suites reported green without ever running.
        Suites mark those reasons via :class:`base_test.EnvSkip`; this counts
        them on their own line and, when nonzero, refuses to let the run read as
        a plain pass.
        """
        from base_test import EnvSkip

        tally = EnvSkip.tally(result)
        total = sum(tally.values())

        print("\n" + "=" * 70)
        print(f"SKIPPED (env): {total}")
        if total:
            for reason, count in sorted(tally.items(), key=lambda kv: -kv[1]):
                print(f"  {count:>4} x {reason}")
            print(
                f"\n[PARTIAL] {total} test(s) never ran - the environment they "
                f"need is missing.\n"
                f"          This run did NOT cover the gated contract; do not "
                f"read it as green.\n"
                f"          README badge withheld. Use --strict-env to fail "
                f"the run instead."
            )
        print("=" * 70)
        return total

    def update_readme_badge(self, result: unittest.TestResult) -> bool:
        """Stamp the README test badge via the ecosystem SSoT.

        The count is individual test cases with skips excluded, matching every
        sibling package. See m3trik/docs/TEST_BADGE_STANDARD.md.
        """
        from pythontk.core_utils.status_badge import StatusBadge

        failed = len(result.failures) + len(result.errors)
        passed = result.testsRun - failed - len(result.skipped)
        readme_path = self.test_dir.parent / "docs" / "README.md"

        if not StatusBadge.update_test_badge(
            readme_path, passed, failed, test_dir=self.test_dir
        ):
            print(
                "[WARNING] README badge not updated (missing or "
                f"unwritable): {readme_path}"
            )
            return False

        print(f"\nREADME badge updated: {StatusBadge.test_status(passed, failed)[0]}")
        return True

    def list_tests(self):
        """List all available test modules."""
        modules = self.discover_tests()
        print("\n" + "=" * 70)
        print("AVAILABLE TEST MODULES")
        print("=" * 70)
        for i, module in enumerate(modules, 1):
            display_name = module.replace("test_", "")
            print(f"{i}. {display_name}")
        print("=" * 70 + "\n")


class EditMode:
    """The package's Edit Mode tests (``--edit-mode``): ``ProjectBuilder.test``,
    a batch editor this run starts on the owned project (refused while an
    editor has it open), its tally printed the way the workspace's
    ``affected_tests.py`` reads a runner's -- what a change to the package's
    C#, assets or samples selects there.
    """

    @staticmethod
    def run(test_filter: "str | None" = None, pipeline: str = "urp") -> bool:
        """Run them; print the tally and each failed or inconclusive case;
        return whether none failed (and some ran)."""
        from unitytk.project_builder import ProjectBuilder

        try:
            outcome = ProjectBuilder.test(
                platform="EditMode", test_filter=test_filter, pipeline=pipeline
            )
        except (RuntimeError, FileNotFoundError, ValueError) as error:
            # ValueError: the owned project's folder is too deep for URP (its fix said).
            print(f"Edit Mode did not run: {error}")
            return False
        print(
            f"Total: {outcome['total']} tests, {outcome['passed']} passed, "
            f"{outcome['failed']} failed, {outcome['skipped']} skipped, "
            f"{outcome['inconclusive']} inconclusive"
        )
        for kind in ("failures", "inconclusives"):
            for case in outcome.get(kind, []):
                label = "failed" if kind == "failures" else "inconclusive"
                print(f"  {label}: {case['name']}: {case['message']}")
        print(f"The editor log: {outcome.get('log')}")
        return bool(outcome["ok"])


class UnityReceipt:
    """The release gate's receipt: a ``unity`` commit status on this commit's SHA,
    posted only after a full local run passed -- every Python module with no
    environment skip, then the package's Edit Mode tests on each of
    :attr:`PIPELINES` -- on a clean tree. Branch protection requires it before
    main moves, so the suite that needs Unity and a licence -- which CI never
    has -- has run on exactly what merges. Posting is outward-facing, so only
    ``--post-status`` posts, and only it runs the Edit Mode half (``--all``
    alone starts no editor: CI runs it).
    """

    CONTEXT = "unity"
    #: The owned projects the receipt's Edit Mode runs take place in: each
    #: pipeline's tests are inconclusive in the other's project.
    PIPELINES = ("urp", "builtin")

    @staticmethod
    def _git(*args) -> str:
        import subprocess

        repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        return subprocess.run(
            ["git", "-C", repo, *args], capture_output=True, text=True, check=True
        ).stdout.strip()

    @classmethod
    def refusal(cls, run_all: bool, strict_env: bool, patterns, edit_mode: bool = False) -> "str | None":
        """Why this run cannot post the receipt, or ``None``."""
        if not run_all or not strict_env or patterns or edit_mode:
            return (
                "the receipt needs the full suite with --all --strict-env, no -k and no "
                "--edit-mode (it runs Edit Mode on each pipeline itself)"
            )
        if cls._git("status", "--porcelain"):
            return "the tree has changes: the receipt names a commit, so commit (or stash) first"
        return None

    @classmethod
    def edit_mode(cls) -> bool:
        """Run the package's Edit Mode tests in each of :attr:`PIPELINES`'
        owned projects (:class:`EditMode`); whether every one passed. Each
        runs, so a release run names every failing pipeline."""
        passed = []
        for pipeline in cls.PIPELINES:
            print(f"\n[receipt] Edit Mode, {pipeline} project")
            ok = EditMode.run(pipeline=pipeline)
            print(f"[receipt] Edit Mode, {pipeline}: {'passed' if ok else 'FAILED'}")
            passed.append(ok)
        return all(passed)

    @classmethod
    def post(cls, description: str) -> str:
        """Post the ``unity`` status ``success`` on HEAD; return its SHA.

        Raises:
            RuntimeError: ``gh`` is missing or the post failed (the message says why).
        """
        import re
        import subprocess

        sha = cls._git("rev-parse", "HEAD")
        remote = cls._git("remote", "get-url", "origin")
        match = re.search(r"github\.com[:/]([^/]+)/([^/.]+)", remote)
        if not match:
            raise RuntimeError(f"origin ({remote}) is no GitHub repository")
        owner, repo = match.groups()
        try:
            subprocess.run(
                ["gh", "api", "-X", "POST", f"repos/{owner}/{repo}/statuses/{sha}",
                 "-f", "state=success", "-f", f"context={cls.CONTEXT}", "-f", f"description={description}"],
                capture_output=True, text=True, check=True,
            )
        except FileNotFoundError as error:
            raise RuntimeError("the GitHub CLI (gh) is not installed: install it and sign in (gh auth login)") from error
        except subprocess.CalledProcessError as error:
            raise RuntimeError(f"gh could not post the status: {error.stderr.strip()}") from error
        return sha


def main(argv=None) -> int:
    """The command line (the module docstring's usage); its exit code."""
    import argparse

    parser = argparse.ArgumentParser(description="Run UnityTk tests")
    parser.add_argument("modules", nargs="*", help="Specific test modules to run")
    parser.add_argument("--all", action="store_true", help="Run all available tests")
    parser.add_argument(
        "--list", action="store_true", help="List available test modules"
    )
    parser.add_argument(
        "--no-badge", action="store_true", help="Skip the README badge update"
    )
    parser.add_argument(
        "--strict-env",
        action="store_true",
        help="Fail the run if any test skipped for a missing environment",
    )
    parser.add_argument(
        "-k",
        dest="patterns",
        action="append",
        default=[],
        help="Only tests whose id matches (substring, or a * pattern); repeatable",
    )

    parser.add_argument(
        "--edit-mode",
        action="store_true",
        help="Run the package's Edit Mode tests in the owned project (ProjectBuilder.test; -k is Unity's -testFilter)",
    )
    parser.add_argument(
        "--pipeline",
        choices=("urp", "builtin"),
        default="urp",
        help="With --edit-mode: the owned project's pipeline (builtin for the Built-in pipeline's tests)",
    )
    parser.add_argument(
        "--post-status",
        action="store_true",
        help=(
            "The release receipt: after a full --all --strict-env pass on a clean tree, run the "
            "package's Edit Mode tests on URP and Built-in, and post the 'unity' commit status "
            "when every one passed"
        ),
    )

    args = parser.parse_args(argv)

    if args.post_status:
        refused = UnityReceipt.refusal(args.all, args.strict_env, args.patterns, args.edit_mode)
        if refused:
            print(f"[receipt] refused before the run: {refused}")
            return 1

    runner = UnityTestRunner()

    if args.list:
        runner.list_tests()
        return 0
    if args.edit_mode:
        return 0 if EditMode.run("|".join(args.patterns) or None, args.pipeline) else 1
    success = runner.run_tests(
        modules=args.modules,
        run_all=args.all,
        update_badge=not args.no_badge,
        strict_env=args.strict_env,
        patterns=args.patterns,
    )
    if not (success and args.post_status):
        return 0 if success else 1
    if not UnityReceipt.edit_mode():
        print("[receipt] not posted: the Edit Mode tests did not pass on every pipeline")
        return 1
    try:
        sha = UnityReceipt.post(
            "full local run passed (--all --strict-env; Edit Mode on " + ", ".join(UnityReceipt.PIPELINES) + ")"
        )
    except RuntimeError as error:
        print(f"[receipt] not posted: {error}")
        return 1
    print(f"[receipt] posted the '{UnityReceipt.CONTEXT}' status on {sha}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
