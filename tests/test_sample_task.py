# !/usr/bin/python
# coding=utf-8
"""Tests for the Tasks sample's task (``com.m3trik.unitytk/Samples~/Tasks``):
unitytk's reader reads every row of its sheet against its delivery into what the
kit builds. The task is written by the workspace's sample sync (its recipe,
``m3trik/scripts/unity_samples/tasks.py``, which holds the files to it)."""

import os
import sys
import unittest
from pathlib import Path

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

import pythontk as ptk

from unitytk.sheets import SheetCheck, SheetPlan

#: The committed task: the Tasks sample's sheet, delivery and plan.
FOLDER = (
    Path(__file__).resolve().parent.parent
    / "com.m3trik.unitytk"
    / "Samples~"
    / "Tasks"
)


class SampleTaskTestCase(UnityTkTestCase):
    def test_every_row_reads_and_every_take_is_played(self):
        plan = SheetPlan.load(FOLDER / "cabinet.plan.json")
        self.assertEqual(list(plan.actions("unread")), [])
        self.assertEqual(
            [a.text for a in plan.actions("note")],
            ["The cabinet stands in front of you with its tools on the table."],
            "the intro's prose, a note",
        )
        self.assertEqual(plan.issues, [])
        self.assertEqual(sum(len(a.issues) for a in plan.actions()), 0)
        takes = list(ptk.FbxFile.load(str(FOLDER / "cabinet.fbx")).take_names())
        self.assertEqual(
            [a.clip for a in plan.actions() if a.clip],
            takes,
            "each take played once, in order",
        )

    def test_the_plan_is_what_the_reader_makes_of_the_sheet_and_the_delivery(self):
        fresh = Path(self.create_temp_dir()) / "cabinet.plan.json"
        SheetPlan.from_sheet(
            str(FOLDER / "cabinet.csv"),
            str(FOLDER / "cabinet.fbx"),
            mapping="interactive",
        ).write(fresh)
        self.assertEqual(
            fresh.read_bytes(),
            (FOLDER / "cabinet.plan.json").read_bytes(),
            "stale: run python m3trik/scripts/sync_unity_samples.py",
        )

    def test_the_sheet_is_in_the_interactive_format_and_passes_its_strict_check(self):
        check = SheetCheck.run(str(FOLDER / "cabinet.csv"), str(FOLDER / "cabinet.fbx"))
        self.assertEqual([i.line() for i in check.errors], [])
        self.assertTrue(check.strict and check.passed)

    def test_the_fbx_meta_names_its_importer_so_unity_reads_a_new_model(self):
        meta = (FOLDER / "cabinet.fbx.meta").read_text(encoding="utf-8")
        self.assertIn("ModelImporter:\n  serializedVersion: 24200", meta)

    def test_the_kits_node_fields_are_every_node_the_reading_binds(self):
        """The Tasks kit's rows' node fields (``TaskBehaviorAttribute``'s default ``NodeFields``, which a
        manifest's assessment finds as it finds targets) restate the built-in clause templates' node groups and the
        reading's hand and arrows: a group left out there is a node name never checked (a teleport's ``side_of``)."""
        import re

        from unitytk.sheets import ClauseBook, ReadingSpec

        kit = FOLDER.parent.parent / "Kits" / "Tasks" / "Editor"
        fields = dict(
            re.findall(
                r'(\w+) = "(\w+)"',
                re.search(
                    r"class Fields\s*\{\s*internal const string ([^;]+);",
                    (kit / "TaskKit.cs").read_text(encoding="utf-8"),
                ).group(1),
            )
        )
        declared = re.search(
            r"NodeFields = new\[\] \{([^}]*)\}",
            (kit / "TaskBehaviorAttribute.cs").read_text(encoding="utf-8"),
        ).group(1)
        node_fields = {fields[name] for name in re.findall(r"Fields\.(\w+)", declared)}
        reading = ReadingSpec.load("interactive")
        bound = {g for _, spec in ClauseBook.load_templates() for g in spec.nodes or []}
        # And each node a cell row's word binds (a teleport's `faces`: what its row says the user is brought to face).
        celled = {name for cases in (reading.words or {}).values() for case in cases for name in case.get("nodes") or {}}
        self.assertEqual(node_fields, bound | celled | {reading.hand, reading.arrows})

    def test_the_kits_traits_that_restate_a_clause_role_say_the_same(self):
        """Two of the Tasks kit's traits mean what a clause role means: ``GoesOn`` the role ``continues`` (the step goes
        on by itself), ``Teleports`` the role ``destination`` (the user is brought to the row's place). The kit keeps them
        for a row the manifest adds, which no clause template reads; held here to the built-in templates' roles."""
        import re

        from unitytk.sheets import ClauseBook

        behaviors = FOLDER.parent.parent / "Kits" / "Tasks" / "Editor" / "Behaviors"
        source = "\n".join(p.read_text(encoding="utf-8") for p in sorted(behaviors.glob("*.cs")))
        marks = re.findall(r'\[TaskBehavior\("(\w+)"(.*?)\)\]', source, re.S)
        book = ClauseBook()
        for trait, role in (("GoesOn", "continues"), ("Teleports", "destination")):
            with self.subTest(trait=trait):
                self.assertEqual(
                    {name for name, rest in marks if re.search(rf"\b{trait} = true\b", rest)},
                    book.with_role(role),
                )


if __name__ == "__main__":
    unittest.main()
