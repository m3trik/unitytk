"""The kernel live: ``UnityHost`` + ``SessionOperationHost`` + the C# kernel in
a Unity editor this test starts (the owned sandbox project), never one a person
opened. Skips without a Unity 6000.3+ editor.

One session: code runs and hands back its value and output, keeps ``Vars``
between calls, fails a compile error as never run, and a scene the code saved
opens for ``unity.scene.view`` (no held-changes refusal) and renders.
"""

import json
import os
import sys
import unittest
from pathlib import Path

try:
    from .base_test import EnvSkip, UnityEnv, UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import EnvSkip, UnityEnv, UnityTkTestCase

import pythontk as ptk

from unitytk.unity_host import UnityHost

CATALOG = Path(__file__).resolve().parent.parent / "unitytk" / "editor_operations.json"
SCENE = "Assets/UnitytkLiveTest.unity"


def _entries():
    with open(CATALOG, encoding="utf-8") as fh:
        return {e["id"]: e for e in json.load(fh)["operations"]}


class TestUnityKernelLive(UnityTkTestCase):
    @EnvSkip.unless(UnityEnv.available(), "no Unity 6000.3 or newer installed")
    def test_a_session_runs_code_and_views_a_scene_the_code_saved(self):
        entries = _entries()
        runner = ptk.SessionOperationHost(UnityHost.operation_host(), f"unitytk-live-{os.getpid()}", "unity")
        self.addCleanup(runner.close)

        def run(op, scenes=(), **args):
            return runner.run(entries[op], ptk.OpRequest(op=op, args=args, scenes=list(scenes)))

        out = run("unity.csharp.exec", code='Debug.Log("from the log"); Vars["k"] = 41; return 6 * 7;')
        self.assertEqual(out.status, "done", out.error)
        self.assertEqual(out.result, {"value": 42, "output": "from the log\n"})
        out = run("unity.csharp.exec", code='return (int)Vars["k"] + 1;')
        self.assertEqual(out.result["value"], 42, "Vars last between calls")

        out = run("unity.csharp.exec", code="int x = ;")
        self.assertEqual(out.status, "failed", "a compile error: none of it ran")
        self.assertIn("CS1525", out.error)

        out = run(
            "unity.csharp.exec",
            code=(
                "var s = UnityEditor.SceneManagement.EditorSceneManager.NewScene("
                "UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects, "
                "UnityEditor.SceneManagement.NewSceneMode.Single); GameObject.CreatePrimitive(PrimitiveType.Cube);"
                f' UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s, "{SCENE}");'
                " return System.IO.Path.GetDirectoryName(Application.dataPath);"
            ),
        )
        self.assertEqual(out.status, "done", out.error)
        scene = os.path.join(out.result["value"], *SCENE.split("/"))
        self.addCleanup(lambda: run("unity.csharp.exec", code=f'return AssetDatabase.DeleteAsset("{SCENE}");'))
        self.assertTrue(os.path.isfile(scene))

        out = run("unity.scene.view", scenes=[scene], width=64, height=48)
        self.assertEqual(out.status, "done", f"a scene the code saved opens: {out.error}")
        self.assertTrue(out.result.endswith(".png") and os.path.isfile(out.result), out.result)

    @EnvSkip.unless(UnityEnv.available(), "no Unity 6000.3 or newer installed")
    def test_a_menu_run_is_one_undo_step_and_play_mode_is_an_issue(self):
        entries = _entries()
        runner = ptk.SessionOperationHost(UnityHost.operation_host(), f"unitytk-live-menu-{os.getpid()}", "unity")
        self.addCleanup(runner.close)

        def run(op, scenes=(), **args):
            return runner.run(entries[op], ptk.OpRequest(op=op, args=args, scenes=list(scenes)))

        # A menu's run: each live ask records undo, the run is one undo step, and a
        # cancel undoes its scene edits.
        out = run(
            "unity.csharp.exec",
            code=(
                "var go = new GameObject(\"before\");"
                " int n = M3trik.Unitytk.Kernel.OpContext.RunMenu(\"Rename\", ctx =>"
                " { if (ctx.ShouldChange(go, \"rename\")) go.name = \"after\"; ctx.ShouldChange(go.transform, \"move\");"
                " go.transform.position = Vector3.one; return 7; });"
                " string renamed = go.name; Undo.PerformUndo();"
                " M3trik.Unitytk.Kernel.OpContext.RunMenu(\"Cancelled\", ctx =>"
                " { if (ctx.ShouldChange(go, \"rename\")) go.name = \"lost\"; throw new"
                " M3trik.Unitytk.Core.OperationCancelledException(\"cancelled\"); });"
                " var problems = M3trik.Unitytk.Kernel.Provided.Problems();"
                " return new object[] { n, renamed, go.name, go.transform.position == Vector3.zero, problems.Count };"
            ),
        )
        self.assertEqual(out.status, "done", out.error)
        self.assertEqual(out.result["value"], [7, "after", "before", True, 0], "one undo step undid both edits")

        # Play mode: code still runs, with the issue; a call that opens scenes is
        # refused, its fix the code that exits play mode.
        out = run("unity.csharp.exec", code="EditorApplication.EnterPlaymode(); return null;")
        self.assertEqual(out.status, "done", out.error)
        out = run("unity.csharp.exec", code="return EditorApplication.isPlayingOrWillChangePlaymode;")
        self.assertEqual(out.status, "done", out.error)
        self.assertTrue(out.result["value"])
        self.assertEqual([i.code for i in out.issues], ["unity.play_mode"])
        project = run("unity.csharp.exec", code="return System.IO.Path.GetDirectoryName(Application.dataPath);")
        out = run("unity.scene.view", scenes=[project.result["value"]], width=32, height=24)
        self.assertEqual(out.status, "failed")
        self.assertIn("play mode", out.error)
        fix = out.issues[0].fix
        out = run(fix.op, **fix.args)
        self.assertEqual(out.status, "done", out.error)
        out = run("unity.csharp.exec", code="return EditorApplication.isPlaying;")
        self.assertFalse(out.result["value"], "the fix left play mode")


if __name__ == "__main__":
    unittest.main()
