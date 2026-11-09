# !/usr/bin/python
# coding=utf-8
"""UnityCompile -- the fast loop: a Unity package's C# compiled in seconds, with no editor running.

The compiler is the editor's own (its bundled Roslyn, run on its bundled .NET
runtime) against its own engine assemblies, so what compiles here compiles in
that editor. Each assembly definition (``.asmdef``) under the source folder is
one assembly: the ``.cs`` files below it and not below a nested one, built in
dependency order against those before it, its ``versionDefines`` and
``defineConstraints`` honoured.

It builds a matrix: the editor build (``UNITY_EDITOR``, the editor's modules)
and the player build (neither), each without the optional packages (Built-in,
the legacy input manager) and with them (URP, the Input System, the XR
Interaction Toolkit). Those packages' assemblies exist only in a project an
editor imported (``Library/ScriptAssemblies``), so the package half needs a
``reference_project``: by default the owned project
(:class:`~unitytk.project_builder.ProjectBuilder`) once an editor has imported
it (it lists all three); without one the half is left out, and the outcome
says so. Where that project holds the Test Framework, the editor build with
the packages compiles the package's test assemblies too (``UNITY_INCLUDE_TESTS``,
NUnit from the project's own ``com.unity.ext.nunit``), so a test file that
does not compile fails the compile as the editor's would.

Prior art: Unity's own compile (Bee) runs only inside an editor with a project
open, tens of seconds before the first error; a ``.csproj`` built with the .NET
SDK needs Unity's reference assemblies wired by hand per version and compiles
with another compiler than the editor's. The editor's bundled compiler against
its own assemblies needs neither and answers in about a second (measured on
6000.3: 1.1 s for 24 files).
"""

from __future__ import annotations

import json
import os
import platform
import re
import subprocess
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

from pythontk import TempArtifacts



class _UnityCompileInternal:
    """Internal helpers for :class:`UnityCompile`: the editor's tools, the
    assemblies and one variant's build."""

    #: Below the editor's Data folder: its compiler, and the runtime that runs it.
    COMPILER = ("DotNetSdkRoslyn", "csc.dll")
    RUNTIME = {"windows": ("NetCoreRuntime", "dotnet.exe")}
    RUNTIME_DEFAULT = ("NetCoreRuntime", "dotnet")
    #: Below the Data folder: the engine and editor modules, and the .NET Standard facade.
    ENGINE = ("Managed", "UnityEngine")
    NETSTANDARD = ("NetStandard", "ref", "2.1.0", "netstandard.dll")
    #: Below the Data folder: the .NET Framework facades a precompiled .NET
    #: Framework assembly (NUnit's) needs beside .NET Standard, as Unity adds them.
    NETFX_SHIMS = ("NetStandard", "compat", "2.1.0", "shims", "netfx")
    #: The optional packages the package half adds, by the id an asmdef's
    #: ``versionDefines`` names: present when the reference project holds them.
    PACKAGES = (
        "com.unity.render-pipelines.universal",
        "com.unity.inputsystem",
        "com.unity.xr.interaction.toolkit",
    )
    #: The package whose presence turns on the test assemblies (their
    #: ``versionDefines`` and ``UNITY_INCLUDE_TESTS``), and NUnit's, whose
    #: ``nunit.framework.dll`` they name among their precompiled references.
    TEST_FRAMEWORK = "com.unity.test-framework"
    NUNIT = "com.unity.ext.nunit"
    #: Warnings Unity's own compile silences: an obsolete API, assembly
    #: unification, a serialized field the inspector assigns.
    NOWARN = "CS0618,CS1701,CS0649"
    LANGVERSION = "9.0"
    #: Seconds one assembly's compile may take before it counts as hung.
    COMPILE_TIMEOUT = 300
    #: The standalone player a build stands for, by OS (an asmdef's platform name).
    STANDALONE = {
        "windows": "WindowsStandalone64",
        "darwin": "macOSStandalone",
        "linux": "LinuxStandalone64",
    }
    _DIAGNOSTIC = re.compile(
        r"^(?P<file>.+?)\((?P<line>\d+),(?P<column>\d+)\): "
        r"(?P<severity>error|warning) (?P<code>\w+): (?P<message>.*)$"
    )

    @staticmethod
    def _data(editor: str) -> Path:
        """The editor's Data folder (``Contents`` in a macOS bundle)."""
        exe = Path(editor)
        if platform.system().lower() == "darwin":
            return exe.parents[1]  # Unity.app/Contents/MacOS/Unity
        return exe.parent / "Data"

    @classmethod
    def _editor(cls, editor: Optional[str]) -> Tuple[str, str]:
        """The editor executable and its version: *editor* (a path or an
        installed version), else the newest installed one.

        Unity's CLI is read only for a version or the newest: an editor's
        own executable needs none (where the CLI lists it, its version is the
        CLI's; else its install folder's name).

        Raises:
            FileNotFoundError: No such editor is installed.
        """
        from unitytk.unity_cli import UnityCli

        if editor and os.path.isfile(editor):
            try:
                editors = UnityCli.editors()
            except (FileNotFoundError, RuntimeError):
                editors = {}
            version = next(
                (v for v, p in editors.items() if os.path.isfile(p) and os.path.samefile(p, editor)), ""
            )
            return editor, version or Path(editor).parents[1].name
        editors = UnityCli.editors()
        if editor:
            if editor not in editors:
                raise FileNotFoundError(
                    f"no Unity {editor} is installed (installed: {sorted(editors) or 'none'})"
                )
            return editors[editor], editor
        if not editors:
            raise FileNotFoundError("no Unity editor is installed: install one through Unity Hub")
        version = max(editors, key=UnityCli.version_key)
        return editors[version], version

    @staticmethod
    def _version_defines(version: str) -> List[str]:
        """Unity's version symbols for *version*: ``UNITY_<major>_<minor>_OR_NEWER``
        for every release up to it, as the editor defines them."""
        match = re.match(r"(\d+)\.(\d+)", version)
        if not match:
            return []
        major, minor = int(match.group(1)), int(match.group(2))
        defines = [f"UNITY_5_{m}_OR_NEWER" for m in range(3, 7)]
        for year in range(2017, 2024):
            for m in range(1, 5):
                if major >= 6000 or (year, m) <= (major, minor):
                    defines.append(f"UNITY_{year}_{m}_OR_NEWER")
        if major >= 6000:
            defines += [f"UNITY_6000_{m}_OR_NEWER" for m in range(minor + 1)]
        return defines + [f"UNITY_{major}", f"UNITY_{major}_{minor}"]

    @staticmethod
    def _assemblies(source: Path) -> List[Dict[str, Any]]:
        """Each assembly definition under *source*: its name, settings and
        ``.cs`` files, in an order where each follows the ones it references.
        A reference by GUID (``GUID:<hex>``, the editor's default) is named by
        the assembly whose ``.asmdef.meta`` holds that GUID."""
        found: Dict[str, Dict[str, Any]] = {}
        by_guid: Dict[str, str] = {}
        definitions = sorted(source.rglob("*.asmdef"))
        roots = [d.parent for d in definitions]
        for definition in definitions:
            spec = json.loads(definition.read_text(encoding="utf-8-sig"))
            folder = definition.parent
            nested = [r for r in roots if r != folder and folder in r.parents]
            files = sorted(
                f
                for f in folder.rglob("*.cs")
                if not any(n == f.parent or n in f.parents for n in nested)
            )
            found[spec["name"]] = dict(spec, folder=folder, files=files)
            meta = definition.with_name(definition.name + ".meta")
            guid = re.search(r"^guid:\s*([0-9a-fA-F]+)", meta.read_text(encoding="utf-8-sig"), re.M) if meta.is_file() else None
            if guid:
                by_guid[guid.group(1).lower()] = spec["name"]
        for spec in found.values():
            spec["references"] = [
                by_guid.get(r[5:].lower(), r) if r.startswith("GUID:") else r
                for r in spec.get("references", [])
            ]
        ordered: List[Dict[str, Any]] = []

        def visit(name: str, trail: Tuple[str, ...]) -> None:
            if name not in found or any(a["name"] == name for a in ordered):
                return
            if name in trail:
                raise ValueError(f"assembly references loop: {' -> '.join(trail + (name,))}")
            for reference in found[name].get("references", []):
                visit(reference, trail + (name,))
            ordered.append(found[name])

        for name in sorted(found):
            visit(name, ())
        return ordered

    @staticmethod
    def _dev_project(version: str) -> Optional[Path]:
        """The owned project of editor *version* once an editor imported it,
        else ``None`` (:meth:`ProjectBuilder.imported`): its ScriptAssemblies
        are that editor's, never another version's."""
        from unitytk.project_builder import ProjectBuilder

        return ProjectBuilder.imported(version)

    @staticmethod
    def _packages(project: Optional[Path]) -> Dict[str, str]:
        """The packages *project* holds, by id: its ``packages-lock.json``."""
        if project is None:
            return {}
        lock = project / "Packages" / "packages-lock.json"
        try:
            deps = json.loads(lock.read_text(encoding="utf-8-sig")).get("dependencies", {})
        except (OSError, ValueError):
            return {}
        return {name: str(info.get("version", "")) for name, info in deps.items()}

    @staticmethod
    def _precompiled(project: Optional[Path]) -> Dict[str, str]:
        """The precompiled assemblies *project*'s packages ship that a test
        assembly names by file (NUnit's ``nunit.framework.dll``): ``{file: path}``."""
        if project is None:
            return {}
        found: Dict[str, str] = {}
        cache = project / "Library" / "PackageCache"
        for folder in sorted(cache.glob(f"{_UnityCompileInternal.NUNIT}@*")) if cache.is_dir() else []:
            for dll in folder.rglob("nunit.framework.dll"):
                found.setdefault(dll.name, str(dll))
        return found

    @staticmethod
    def _satisfied(constraints: Sequence[str], defines: Sequence[str]) -> bool:
        """Whether *defines* meet each ``defineConstraints`` entry (``!X`` = not X;
        ``A || B`` = either)."""

        def holds(term: str) -> bool:
            term = term.strip()
            return term[1:] not in defines if term.startswith("!") else term in defines

        return all(any(holds(t) for t in c.split("||")) for c in constraints)

    @classmethod
    def _targets(cls, assembly: Dict[str, Any], in_editor: bool) -> bool:
        """Whether *assembly* builds for this build's platform: the editor, or
        this OS's standalone player (its ``includePlatforms``, else its
        ``excludePlatforms``)."""
        platform_name = "Editor" if in_editor else cls.STANDALONE.get(platform.system().lower(), "")
        include = assembly.get("includePlatforms") or []
        if include:
            return platform_name in include
        return platform_name not in (assembly.get("excludePlatforms") or [])


    @classmethod
    def _build(
        cls,
        data: Path,
        version: str,
        assemblies: List[Dict[str, Any]],
        variant: Tuple[str, bool, bool],
        packages: Dict[str, str],
        project: Optional[Path],
        out: Path,
        check=lambda: None,
    ) -> Dict[str, Any]:
        """One variant: each assembly in order, against the ones built before it,
        *check* (a cancel checkpoint) before each."""
        name, in_editor, _ = variant
        started = time.monotonic()
        system = platform.system().lower()
        shell = {"windows": "WIN", "darwin": "OSX", "linux": "LINUX"}.get(system, "WIN")
        base = cls._version_defines(version) + [
            "NET_STANDARD_2_1", "NET_STANDARD", "NETSTANDARD2_1", "NETSTANDARD", "TRACE",
        ]
        base += (
            ["UNITY_EDITOR", f"UNITY_EDITOR_{shell}", "DEBUG"]
            if in_editor
            else ["UNITY_STANDALONE", f"UNITY_STANDALONE_{shell}"]
        )
        base.append(
            "ENABLE_INPUT_SYSTEM" if "com.unity.inputsystem" in packages else "ENABLE_LEGACY_INPUT_MANAGER"
        )
        precompiled = cls._precompiled(project) if in_editor and cls.TEST_FRAMEWORK in packages else {}
        if precompiled:
            base.append("UNITY_INCLUDE_TESTS")
        engine = sorted((data / Path(*cls.ENGINE)).glob("*.dll"))
        if not in_editor:
            engine = [d for d in engine if not d.name.startswith("UnityEditor")]
        references = [str(d) for d in engine] + [str(data / Path(*cls.NETSTANDARD))]
        if packages and project is not None:
            for dll in sorted((project / "Library" / "ScriptAssemblies").glob("*.dll")):
                ours = any(dll.stem == a["name"] for a in assemblies)
                if not ours and (in_editor or ".Editor" not in dll.stem):
                    references.append(str(dll))
        runtime = data / Path(*cls.RUNTIME.get(system, cls.RUNTIME_DEFAULT))
        built: Dict[str, str] = {}
        report = []
        folder = out / name.replace("+", "_")
        folder.mkdir(parents=True, exist_ok=True)
        for assembly in assemblies:
            defines = list(base)
            for entry in assembly.get("versionDefines", []):
                if entry.get("name") in packages:
                    defines.append(entry["define"])
            if not cls._targets(assembly, in_editor) or not cls._satisfied(
                assembly.get("defineConstraints", []), defines
            ):
                continue
            check()
            target = folder / f"{assembly['name']}.dll"
            own = [built[r] for r in assembly.get("references", []) if r in built]
            framework = [precompiled[p] for p in assembly.get("precompiledReferences", []) if p in precompiled]
            if framework:
                own += framework + [str(d) for d in sorted((data / Path(*cls.NETFX_SHIMS)).glob("*.dll"))]
            rsp = folder / f"{assembly['name']}.rsp"
            lines = [
                "-nologo -noconfig -nostdlib+ -target:library -deterministic",
                f"-langversion:{cls.LANGVERSION} -nowarn:{cls.NOWARN}",
                f"-unsafe{'+' if assembly.get('allowUnsafeCode') else '-'}",
                "-define:" + ";".join(dict.fromkeys(defines)),
                f'-out:"{target}"',
            ]
            lines += [f'-r:"{r}"' for r in references + own]
            lines += [f'"{f}"' for f in assembly["files"]]
            rsp.write_text("\n".join(lines) + "\n", encoding="utf-8")
            try:
                proc = subprocess.run(
                    [str(runtime), str(data / Path(*cls.COMPILER)), f"@{rsp}"],
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    timeout=cls.COMPILE_TIMEOUT,
                )
            except subprocess.TimeoutExpired:
                report.append(
                    {
                        "assembly": assembly["name"],
                        "files": len(assembly["files"]),
                        "errors": [f"the compiler ran past {cls.COMPILE_TIMEOUT} s and was stopped"],
                        "warnings": 0,
                    }
                )
                continue
            diagnostics = [m for m in map(cls._DIAGNOSTIC.match, proc.stdout.splitlines()) if m]
            errors = [m.group(0) for m in diagnostics if m.group("severity") == "error"]
            if proc.returncode and not errors:  # the compiler failed without saying where
                errors = [line for line in proc.stdout.splitlines() if line.strip()][-3:] or [
                    f"the compiler exited {proc.returncode}"
                ]
            report.append(
                {
                    "assembly": assembly["name"],
                    "files": len(assembly["files"]),
                    "errors": errors,
                    "warnings": sum(m.group("severity") == "warning" for m in diagnostics),
                }
            )
            if not errors:
                built[assembly["name"]] = str(target)
        return {
            "variant": name,
            "assemblies": report,
            "seconds": round(time.monotonic() - started, 2),
        }


class UnityCompile(_UnityCompileInternal):
    """A Unity package's C# compiled with an installed editor's own compiler (see the module docstring)."""

    @classmethod
    def compile(
        cls,
        source: Optional[str] = None,
        editor: Optional[str] = None,
        reference_project: Optional[str] = None,
        tests: bool = True,
        ctx=None,
    ) -> Dict[str, Any]:
        """Compile every assembly under *source*, editor and player builds,
        without the optional packages and (given a reference project) with them.

        Parameters:
            source: The folder holding the package's ``.asmdef`` files; default:
                this checkout's own package (``com.m3trik.unitytk/``).
            editor: The editor whose compiler and assemblies to use: a Unity
                executable or an installed version (``"6000.3.10f1"``); default:
                the newest installed.
            reference_project: A project an editor imported with the URP, Input
                System and XR Interaction Toolkit packages, whose
                ``Library/ScriptAssemblies`` the package half compiles against;
                default, for the package itself (no *source*): the owned
                project of *editor* once imported; without one, only the half without them
                is built.
            tests: Compile the package's test assemblies too, in the editor
                build with the packages, where the reference project holds the
                Test Framework (they are Editor-only and need its assemblies and
                NUnit); False leaves them out, a faster loop that cannot see a
                broken test file.
            ctx: The operation context: each compiler error is an issue on it,
                and a cancel stops it between assemblies.

        Returns:
            ``{"editor", "variants": [{"variant", "assemblies": [{"assembly",
            "files", "errors", "warnings"}], "seconds"}], "errors", "seconds"}``.

        Raises:
            FileNotFoundError: No such editor, or *source* holds no assembly definition.
            OperationCancelled: The run was cancelled.
        """
        from pythontk import CancelScope

        started = time.monotonic()
        # Caught here: the builds run in worker threads, which see no ambient scope.
        scope = (getattr(ctx, "scope", None) if ctx is not None else None) or CancelScope.current()
        check = scope.checkpoint if scope is not None else (lambda: None)
        check()
        exe, version = cls._editor(editor)
        data = cls._data(exe)
        if source:
            folder = Path(source)
        else:
            from unitytk.project_builder import ProjectBuilder

            folder = ProjectBuilder.package_dir()
            if folder is None:
                raise FileNotFoundError("this unitytk is no checkout: name the package folder (source)")
        assemblies = cls._assemblies(folder)
        if not assemblies:
            raise FileNotFoundError(f"no assembly definition (.asmdef) under {folder}")
        # The owned project is the default only for the checkout's own package.
        project = Path(reference_project) if reference_project else (None if source else cls._dev_project(version))
        held = cls._packages(project)
        packages = {p: v for p, v in held.items() if p in cls.PACKAGES}
        if tests and packages and cls.TEST_FRAMEWORK in held:
            packages[cls.TEST_FRAMEWORK] = held[cls.TEST_FRAMEWORK]
        elif tests and packages and ctx is not None:
            # (With no packages at all, unity.compile.no_packages already says the half is left out.)
            ctx.issue(
                "unity.compile.no_tests",
                "the test assemblies were left out: the reference_project holds no Test Framework, which "
                "they compile against (the owned project does: unity_project.build)",
                "info",
            )
        variants = [("editor", True, False), ("player", False, False)]
        if packages:
            variants += [("editor+packages", True, True), ("player+packages", False, True)]
        elif ctx is not None:
            ctx.issue(
                "unity.compile.no_packages",
                "the URP, Input System and XR Interaction Toolkit builds were left out: "
                "name a reference_project an editor imported with those packages "
                "(the owned project is one once built: unity_project.build)",
                "info",
            )
        out = Path(TempArtifacts("unity_compile", max_age_days=1).dir_path())
        with ThreadPoolExecutor(max_workers=len(variants)) as pool:
            results = list(
                pool.map(
                    lambda v: cls._build(
                        data, version, assemblies, v, packages if v[2] else {}, project, out, check
                    ),
                    variants,
                )
            )
        errors = 0
        for result in results:
            for assembly in result["assemblies"]:
                errors += len(assembly["errors"])
                if ctx is not None:
                    for diagnostic in assembly["errors"]:
                        ctx.issue(
                            "unity.compile.error",
                            f"{result['variant']} {assembly['assembly']}: {diagnostic}",
                            "error",
                            diagnostic.split("(", 1)[0],
                        )
        return {
            "editor": version,
            "variants": results,
            "errors": errors,
            "seconds": round(time.monotonic() - started, 2),
        }
