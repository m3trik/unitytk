# !/usr/bin/python
# coding=utf-8
"""The PreToolUse hook that refuses a hand edit to a generated C# file.

``.agents/generated_files_hook.py`` (registered in ``.claude/settings.json``)
reads Claude Code's event on stdin: an Edit or Write to a ``*.g.cs`` (or its
``.meta``) is denied with a reason naming the generator its header names, and
everything else passes untouched. m3trik's ``check_csharp.py`` is the check
behind it; this is the edit-time half.
"""

import json
import os
import subprocess
import sys
import unittest

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HOOK = os.path.join(REPO, ".agents", "generated_files_hook.py")
SETTINGS = os.path.join(REPO, ".claude", "settings.json")
#: Folders that hold no package source: build copies, tests, tool state.
SKIP = {"build", "tests", "docs", "__pycache__", ".git", ".agents", ".claude"}


def _run(event):
    proc = subprocess.run(
        [sys.executable, "-I", HOOK],
        input=json.dumps(event).encode("utf-8"),
        capture_output=True,
        timeout=30,
    )
    out = proc.stdout.decode("utf-8").strip()
    return proc.returncode, json.loads(out) if out else None


def _sources(suffix):
    """Every ``*<suffix>`` of the package's C#, wherever the tree keeps it."""
    for folder, dirs, files in os.walk(REPO):
        dirs[:] = sorted(d for d in dirs if d not in SKIP and not d.endswith(".egg-info"))
        for name in sorted(files):
            if name.endswith(suffix):
                yield os.path.join(folder, name)


def _generated(generator_stem):
    """A committed ``*.g.cs`` whose header names *generator_stem*."""
    for path in _sources(".g.cs"):
        with open(path, encoding="utf-8") as handle:
            if generator_stem in handle.read(400):
                return path
    raise AssertionError(f"no committed *.g.cs names {generator_stem}")


class TestGeneratedFilesHook(unittest.TestCase):
    def test_an_edit_to_a_generated_file_is_denied_naming_its_generator(self):
        for stem in ("sync_operation_contract", "sync_scene_records"):
            path = _generated(stem)
            for tool in ("Edit", "Write", "MultiEdit"):
                with self.subTest(stem=stem, tool=tool):
                    code, out = _run({"tool_name": tool, "tool_input": {"file_path": path}})
                    self.assertEqual(code, 0)
                    decision = out["hookSpecificOutput"]
                    self.assertEqual(decision["hookEventName"], "PreToolUse")
                    self.assertEqual(decision["permissionDecision"], "deny")
                    self.assertIn(
                        f"m3trik/scripts/{stem}.py", decision["permissionDecisionReason"]
                    )

    def test_its_meta_and_a_new_one_are_denied_too(self):
        meta = _generated("sync_scene_records") + ".meta"
        code, out = _run({"tool_name": "Edit", "tool_input": {"file_path": meta}})
        self.assertIn("sync_scene_records.py", out["hookSpecificOutput"]["permissionDecisionReason"])
        new = os.path.join(REPO, "Nowhere", "Fresh.g.cs")
        code, out = _run({"tool_name": "Write", "tool_input": {"file_path": new}})
        reason = out["hookSpecificOutput"]["permissionDecisionReason"]
        self.assertIn("sync_operation_contract.py", reason)
        self.assertIn("sync_scene_records.py", reason)

    def test_anything_else_passes_untouched(self):
        hand = next(p for p in _sources(".cs") if not p.endswith(".g.cs"))
        for event in (
            {"tool_name": "Edit", "tool_input": {"file_path": hand}},
            {"tool_name": "Write", "tool_input": {"file_path": "Pkg/Notes.g.csv"}},
            {"tool_name": "Bash", "tool_input": {"command": "python sync_scene_records.py"}},
            {},
        ):
            with self.subTest(event=event):
                self.assertEqual(_run(event), (0, None))
        proc = subprocess.run([sys.executable, "-I", HOOK], input=b"not json", capture_output=True)
        self.assertEqual((proc.returncode, proc.stdout), (0, b""))

    def test_it_is_registered_for_the_editing_tools(self):
        with open(SETTINGS, encoding="utf-8") as handle:
            groups = json.load(handle)["hooks"]["PreToolUse"]
        (group,) = [g for g in groups if any("generated_files_hook.py" in a for h in g["hooks"] for a in h.get("args", []))]
        for tool in ("Edit", "Write", "MultiEdit"):
            self.assertIn(tool, group["matcher"].split("|"))


if __name__ == "__main__":
    unittest.main()
