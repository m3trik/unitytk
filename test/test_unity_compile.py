"""Tests for unitytk.unity_compile: the fast loop, a package's C# compiled with an
installed editor's own compiler and no editor running."""

import json
import os
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

try:
    from .base_test import EnvSkip, UnityEnv, UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import EnvSkip, UnityEnv, UnityTkTestCase

import pythontk as ptk

from unitytk.unity_compile import UnityCompile

_HAS_UNITY = bool(UnityEnv.editors())


def _asmdef(folder: Path, name: str, **spec) -> None:
    folder.mkdir(parents=True, exist_ok=True)
    (folder / f"{name}.asmdef").write_text(json.dumps(dict(name=name, **spec)), encoding="utf-8")


def _cs(folder: Path, name: str, body: str) -> None:
    (folder / name).write_text(textwrap.dedent(body), encoding="utf-8")


class TestUnityCompileModel(UnityTkTestCase):
    """What needs no editor: the version symbols and the assembly graph."""

    def test_version_symbols_name_every_release_up_to_the_editor(self):
        six = UnityCompile._version_defines("6000.3.10f1")
        for symbol in ("UNITY_2023_1_OR_NEWER", "UNITY_6000_0_OR_NEWER", "UNITY_6000_3_OR_NEWER"):
            self.assertIn(symbol, six)
        self.assertNotIn("UNITY_6000_4_OR_NEWER", six)
        old = UnityCompile._version_defines("2022.3.40f1")
        self.assertIn("UNITY_2022_3_OR_NEWER", old)
        self.assertNotIn("UNITY_2023_1_OR_NEWER", old)

    def test_an_editors_own_executable_needs_no_cli(self):
        """An explicit editor path failed where Unity's CLI was not installed:
        the CLI was read before the argument was looked at.
        Added: 2026-11-04"""
        from unittest import mock

        from unitytk.unity_cli import UnityCli

        exe = Path(self.create_temp_dir()) / "6000.3.10f1" / "Editor" / "Unity.exe"
        exe.parent.mkdir(parents=True)
        exe.write_bytes(b"")
        with mock.patch.object(UnityCli, "editors", side_effect=FileNotFoundError("no cli")):
            self.assertEqual(UnityCompile._editor(str(exe)), (str(exe), "6000.3.10f1"))
            with self.assertRaisesRegex(FileNotFoundError, "no cli"):
                UnityCompile._editor("6000.3.10f1")
        with mock.patch.object(UnityCli, "editors", return_value={"6000.3.11f1": str(exe)}):
            self.assertEqual(UnityCompile._editor(str(exe)), (str(exe), "6000.3.11f1"), "the CLI's version")

    def test_assemblies_follow_what_they_reference_and_own_no_nested_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            _asmdef(root, "Pkg.Feature", references=["Pkg.Core"])
            _cs(root, "Feature.cs", "class Feature {}")
            _asmdef(root / "Core", "Pkg.Core")
            _cs(root / "Core", "Core.cs", "class Core {}")
            order = UnityCompile._assemblies(root)
        self.assertEqual([a["name"] for a in order], ["Pkg.Core", "Pkg.Feature"])
        self.assertEqual([f.name for f in order[1]["files"]], ["Feature.cs"])

    def test_define_constraints_hold_negation_and_either(self):
        self.assertTrue(UnityCompile._satisfied(["A", "!B", "C || D"], ["A", "D"]))
        self.assertFalse(UnityCompile._satisfied(["!B"], ["B"]))

    def test_the_default_reference_project_is_the_compiles_own_editors(self):
        """The package half compiled against the newest editor's owned project
        whatever editor the compile ran, its ScriptAssemblies another
        version's. Added: 2026-11-08"""
        from unittest import mock

        from unitytk.project_builder import ProjectBuilder

        built = lambda data, version, assemblies, variant, *_: {"variant": variant[0], "assemblies": [], "seconds": 0}  # noqa: E731
        with mock.patch.object(UnityCompile, "_editor", return_value=("C:/U/Unity.exe", "6000.3.9f1")), mock.patch.object(
            UnityCompile, "_data", return_value=Path("C:/U/Data")
        ), mock.patch.object(UnityCompile, "_assemblies", return_value=[{"name": "A", "files": []}]), mock.patch.object(
            UnityCompile, "_build", side_effect=built
        ), mock.patch.object(ProjectBuilder, "imported", return_value=None) as imported:
            UnityCompile.compile(editor="6000.3.9f1")
        imported.assert_called_once_with("6000.3.9f1")


@EnvSkip.unless(_HAS_UNITY, "no Unity editor installed")
class TestUnityCompile(UnityTkTestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()

    def test_a_clean_package_builds_for_the_editor_and_the_player(self):
        _asmdef(self.root, "Pkg.Core", noEngineReferences=True)
        _cs(self.root, "Core.cs", "namespace Pkg { public static class Core { public static int One() => 1; } }")
        _asmdef(self.root / "Feature", "Pkg.Feature", references=["Pkg.Core"])
        _cs(
            self.root / "Feature",
            "Feature.cs",
            """\
            using UnityEngine;
            public class Feature : MonoBehaviour { int n = Pkg.Core.One(); }
            """,
        )
        ctx = ptk.OpContext()
        result = UnityCompile.compile(str(self.root), ctx=ctx)
        self.assertEqual(result["errors"], 0, result)
        self.assertEqual([v["variant"] for v in result["variants"]], ["editor", "player"])
        for variant in result["variants"]:
            self.assertEqual([a["assembly"] for a in variant["assemblies"]], ["Pkg.Core", "Pkg.Feature"])
        self.assertEqual([i.code for i in ctx.issues], ["unity.compile.no_packages"])

    def test_an_error_is_an_issue_naming_the_file_line_and_code(self):
        _asmdef(self.root, "Pkg.Broken")
        _cs(self.root, "Broken.cs", "public class Broken { int x = missing; }\n")
        ctx = ptk.OpContext()
        result = UnityCompile.compile(str(self.root), ctx=ctx)
        self.assertEqual(result["errors"], 2)  # once per build
        errors = [i for i in ctx.issues if i.code == "unity.compile.error"]
        self.assertEqual(len(errors), 2)
        self.assertIn("Broken.cs(1,", errors[0].message)
        self.assertIn("CS0103", errors[0].message)
        self.assertTrue(errors[0].location.endswith("Broken.cs"), errors[0].location)

    def test_editor_code_unguarded_fails_the_player_build_alone(self):
        _asmdef(self.root, "Pkg.Mixed")
        _cs(
            self.root,
            "Mixed.cs",
            """\
            public static class Mixed
            {
            #if UNITY_EDITOR
                public static bool Guarded => UnityEditor.EditorApplication.isPlaying;
            #endif
                public static bool Unguarded => UnityEditor.EditorApplication.isPlaying;
            }
            """,
        )
        result = UnityCompile.compile(str(self.root))
        failed = {v["variant"]: sum(len(a["errors"]) for a in v["assemblies"]) for v in result["variants"]}
        self.assertEqual(failed["editor"], 0)
        self.assertGreater(failed["player"], 0)

    def test_an_assembly_whose_constraint_is_unmet_is_left_out(self):
        _asmdef(self.root, "Pkg.Always")
        _cs(self.root, "Always.cs", "public class Always {}")
        _asmdef(self.root / "Xr", "Pkg.Xr", defineConstraints=["PKG_XRI"])
        _cs(self.root / "Xr", "Xr.cs", "public class Xr : NotDefinedAnywhere {}")
        result = UnityCompile.compile(str(self.root))
        self.assertEqual(result["errors"], 0)
        self.assertEqual(
            [a["assembly"] for a in result["variants"][0]["assemblies"]], ["Pkg.Always"]
        )

    def test_a_reference_by_guid_resolves_as_one_by_name(self):
        # The editor writes references as GUID:<the asmdef's .meta guid> by default.
        _asmdef(self.root / "Core", "Pkg.Core")
        (self.root / "Core" / "Pkg.Core.asmdef.meta").write_text(
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n", encoding="utf-8"
        )
        _cs(self.root / "Core", "Core.cs", "namespace Pkg { public static class Core { public static int One() => 1; } }")
        _asmdef(self.root / "Feature", "Pkg.Feature", references=["GUID:0123456789abcdef0123456789abcdef"])
        _cs(self.root / "Feature", "Feature.cs", "public static class Feature { public static int N = Pkg.Core.One(); }")
        result = UnityCompile.compile(str(self.root))
        self.assertEqual(result["errors"], 0, json.dumps(result["variants"], indent=1))

    def test_an_assembly_excluded_from_the_editor_builds_for_the_player_alone(self):
        _asmdef(self.root, "Pkg.Player", excludePlatforms=["Editor"])
        _cs(self.root, "Player.cs", "public class PlayerOnly {}")
        result = UnityCompile.compile(str(self.root))
        built = {v["variant"]: [a["assembly"] for a in v["assemblies"]] for v in result["variants"]}
        self.assertEqual(built, {"editor": [], "player": ["Pkg.Player"]})

    def test_a_cancel_stops_it_between_assemblies(self):
        _asmdef(self.root, "Pkg.One")
        _cs(self.root, "One.cs", "public class One {}")
        scope = ptk.CancelScope("test")
        scope.cancel("enough")
        with scope, self.assertRaises(ptk.OperationCancelled):
            UnityCompile.compile(str(self.root), ctx=ptk.OpContext())

    def test_a_broken_test_file_fails_the_compile(self):
        """The compile left every test assembly out (their constraints name the
        Test Framework), so it passed with a test file that does not compile.
        Added: 2026-11-08"""
        import shutil

        from unitytk.project_builder import ProjectBuilder

        version = UnityCompile._editor(None)[1]
        project = ProjectBuilder.imported(version)
        if project is None:
            # The run's own owned project, made and imported once (minutes): the Test
            # Framework's assemblies exist only in a project an editor imported.
            ProjectBuilder.test(project=ProjectBuilder.build(editor=version), test_filter="^NoSuchTestAnywhere$")
            project = ProjectBuilder.imported(version)
        self.assertIsNotNone(project, "the owned project, imported")
        copy = self.root / "com.m3trik.unitytk"
        shutil.copytree(
            ProjectBuilder.package_dir(), copy,
            ignore=shutil.ignore_patterns("*.fbx", "*.png", "*.exr", "*.hdr", "*.wav", "Samples~"),
        )
        broken = copy / "Kernel" / "Tests" / "ImportsTests.cs"
        broken.write_text(broken.read_text(encoding="utf-8") + "\nnot C# at all\n", encoding="utf-8")
        result = UnityCompile.compile(str(copy), reference_project=str(project))
        errors = [e for v in result["variants"] for a in v["assemblies"] for e in a["errors"]]
        self.assertTrue(any("ImportsTests.cs" in e for e in errors), json.dumps(result["variants"], indent=1))
        tested = {a["assembly"] for v in result["variants"] for a in v["assemblies"]}
        self.assertIn("M3trik.Unitytk.Kernel.Tests", tested)

    def test_the_shipped_package_compiles(self):
        # The fast loop on unitytk's own C#: what CI cannot run, in seconds. With a
        # reference project the editor build also compiles the test assemblies
        # (measured: 37 s without them, 58 s with, on a machine busy with an editor).
        result = UnityCompile.compile()
        self.assertEqual(result["errors"], 0, json.dumps(result["variants"], indent=1))
        self.assertLess(result["seconds"], 120)


if __name__ == "__main__":
    unittest.main()
