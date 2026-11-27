# !/usr/bin/python
# coding=utf-8
"""Tests for ``unitytk.sheets.SheetConverter``: a sheet written before the format, rewritten in the
Interactive format from what the tolerant reading bound, so the strict reading binds the same."""

import csv
import os
import sys

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

from test_sheet_plan import SHEET, write_delivery

import functools

from unitytk.sheets import ReadingSpec, SheetConverter, SheetPlan


#: The older writer, a sheet of sentences: what these tests hold until that format goes.
_sentences = functools.partial(SheetConverter.convert, cells=False)

class SheetConverterTestCase(UnityTkTestCase):
    def setUp(self):
        super().setUp()
        self.dir = self.create_temp_dir()
        self.sheet = os.path.join(self.dir, "task.csv")
        with open(self.sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(SHEET)
        self.fbx = write_delivery(
            os.path.join(self.dir, "cabinet.fbx"),
            keys={
                "A01_1": {
                    "CABINET|CABINET_LATCHES_GRP|LATCH_UPPER_GRP": [0.0, 1.0],
                    "CABINET|SCREWS|SCREW_UPPER": [0.0, 1.0],
                }
            },
        )
        self.out = os.path.join(self.dir, "task_interactive.csv")
        self.result = _sentences(self.sheet, self.fbx, self.out)
        with open(self.out, encoding="utf-8", newline="") as fh:
            self.rows = list(csv.reader(fh))
        self.head = self.rows[0]

    def _cells(self, text):
        row = next(
            r
            for r in self.rows
            if len(r) > 2 and r[self.head.index("Step Contents")] == text
        )
        return dict(zip(self.head, row))

    def _step(self, step_id):
        return dict(zip(self.head, next(r for r in self.rows if r and r[0] == step_id)))

    def test_it_writes_the_formats_columns_and_keeps_the_sections(self):
        self.assertEqual(
            self.head,
            [
                "Step",
                "Step Name",
                "Step Contents",
                "Asset Names",
                "Action",
                "Animation Clip Names",
                "Text On Placard",
                "Hint",
                "Voice Support",
                "Audio Files",
            ],
            "the Interactive reading's columns, headed as its template names them",
        )
        self.assertEqual(len(self.head), len(ReadingSpec.load("interactive").columns))
        firsts = [r[0] for r in self.rows]
        self.assertIn("INTRO", firsts)
        self.assertIn("SECTION A: Open the cabinet", firsts)
        self.assertIn("B02.5", firsts, "a branch is written as the format spells it")

    def test_each_row_lists_what_its_words_bound_and_names_its_take(self):
        cells = self._cells(
            "Selecting the top latch triggers Top Latch Open Animation and returns the key to hand"
        )
        self.assertEqual(
            (cells["Asset Names"].splitlines()[:2], cells["Animation Clip Names"]),
            (["LATCH_UPPER", "KEY"], "A01_1"),
        )

    def test_an_animation_row_lists_the_parts_its_take_moves(self):
        cells = self._cells(
            "Selecting the top latch triggers Top Latch Open Animation and returns the key to hand"
        )
        self.assertEqual(
            cells["Asset Names"].splitlines(), ["LATCH_UPPER", "KEY", "SCREW_UPPER"]
        )

    def test_placard_text_in_the_hint_moves_to_the_placard(self):
        a02 = self._step("A02")
        self.assertEqual((a02["Text On Placard"], a02["Hint"]), ("Select the door", ""))
        self.assertEqual(
            self._step("A01")["Hint"],
            "-Bouncing arrow pointing at the key on the table",
        )

    def test_the_strict_reading_of_the_result_binds_what_the_tolerant_reading_did(self):
        before = SheetPlan.from_sheet(self.sheet, self.fbx, mapping="default")
        after = SheetPlan.from_sheet(self.out, self.fbx, mapping="interactive")
        for old, new in zip(before.steps, after.steps):
            # A placard line in the hint is the step's placard text now, no action.
            kept = [
                (a.behavior, a.targets, a.clip)
                for a in old.actions
                if not a.issues and a.behavior != "placard"
            ]
            read = [(a.behavior, a.targets, a.clip) for a in new.actions]
            if any(a.behavior == "placard" for a in old.actions):
                self.assertTrue(new.field("placard"), old.id)
            for action in kept:
                with self.subTest(step=old.id, action=action):
                    self.assertIn(action, read)

    def test_what_it_could_not_fill_is_reported_by_cell(self):
        left = {(i["step"], i["row"]) for i in self.result["left"]}
        self.assertIn(("B01", 3), left, "the toolbox lid names no node")
        self.assertIn(("B03", 2), left, "no phrase reads the jig")
        self.assertEqual(self.result["file"], self.out)

    def test_a_sheet_settings_file_names_the_sheet_and_its_delivery(self):
        import json

        task = os.path.join(self.dir, "task.json")
        with open(task, "w", encoding="utf-8") as fh:
            json.dump(
                {
                    "version": 1,
                    "sheet": "task.csv",
                    "mapping": "default",
                    "deliveries": ["cabinet.fbx"],
                },
                fh,
            )
        out = os.path.join(self.dir, "by_task.csv")
        _sentences(out=out, settings=task)
        with open(out, encoding="utf-8", newline="") as fh:
            self.assertEqual(list(csv.reader(fh)), self.rows)
        with self.assertRaisesRegex(ValueError, "sheet="):
            _sentences(out=out)

    def test_the_target_format_and_the_old_sheets_options_are_data(self):
        from unittest import mock

        from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

        out = os.path.join(self.dir, "linear.csv")
        resolve = Mapping.resolve
        seen = []

        def spy(sheet, mapping, options=None, base=None):
            seen.append(dict(options or {}))
            return resolve(sheet, mapping, options=options, base=base)

        with mock.patch.object(Mapping, "resolve", side_effect=spy):
            _sentences(
                self.sheet, self.fbx, out, to="linear", options={"audio": "text"}
            )
        with open(out, encoding="utf-8", newline="") as fh:
            head = next(csv.reader(fh))
        self.assertEqual(
            head,
            [
                "Step",
                "Step Name",
                "Step Contents",
                "Asset Names",
                "Animation Clip Names",
                "Voice Support",
                "Audio Files",
            ],
            "the Linear reading's columns",
        )
        self.assertEqual(
            seen, [{"audio": "text"}], "the old sheet read once, with its options"
        )
        with self.assertRaisesRegex(ValueError, "lists no columns"):
            _sentences(self.sheet, self.fbx, out, to="default")

    def test_what_the_format_has_no_column_for_is_left_with_its_reason(self):
        result = _sentences(
            self.sheet, self.fbx, os.path.join(self.dir, "linear.csv"), to="linear"
        )
        dropped = {
            (i["step"], i["row"], i["text"]): i["why"]
            for i in result["left"]
            if i["why"].startswith("the format has no")
        }
        self.assertEqual(
            dropped,
            {
                ("A01", 1, "-Bouncing arrow pointing at the key on the table"): (
                    "the format has no hint column"
                ),
                ("A02", 1, 'Placard text reads "Select the door"'): (
                    "the format has no hint column"
                ),
            },
            "a format with no page keeps a page line in the hint; with no hint either, it is left",
        )
        self.assertEqual(result["moved"], 0, "nothing moves to a page the format lacks")

    def test_a_dry_run_plans_the_write_and_writes_nothing(self):
        import pythontk as ptk

        out = os.path.join(self.dir, "planned.csv")
        ctx = ptk.OpContext(dry_run=True)
        _sentences(self.sheet, self.fbx, out, ctx=ctx)
        self.assertFalse(os.path.exists(out))
        self.assertEqual([c.kind for c in ctx.changes], ["write"])


class CellColumnsTestCase(UnityTkTestCase):
    def test_the_cells_are_written_under_the_starters_header(self):
        """A reading's ``cell_columns`` restate its template's header, which nothing held them to. Added: 2026-11-26"""
        from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

        for template in ("interactive", "linear"):
            with self.subTest(template=template):
                with open(Mapping.starter_sheet(template), encoding="utf-8", newline="") as fh:
                    header = next(csv.reader(fh))
                self.assertEqual(ReadingSpec.load(template).cell_columns, header)

    def test_a_timed_press_its_sheet_gives_no_seconds_is_left_for_a_person(self):
        """A timed press whose old sheet said no seconds was written ``hold=1.5``, a value the sheet never said (the
        kit's own default). Added: 2026-11-26"""
        import json

        folder = self.create_temp_dir()
        os.makedirs(os.path.join(folder, "clauses"))
        with open(os.path.join(folder, "clauses", "press_long.json"), "w", encoding="utf-8") as fh:
            json.dump({"behavior": "use", "order": 1, "clip": True, "roles": ["uses"],
                       "settings": {"useTrigger": "TimedPress"},
                       "detect": [r"^pressing\s+(?P<targets>.+?)\s+a\s+while\s+triggers\s+(?P<phrase>.+?)\s+animation$"]},
                      fh)
        with open(os.path.join(folder, "task.csv"), "w", encoding="utf-8", newline="") as fh:
            fh.write("Step,Step Contents,Asset Names,Animation Clip Names\n"
                     "A01,Pressing the door a while triggers Door Open Animation,DOOR,A01\n")
        write_delivery(os.path.join(folder, "task.fbx"), takes=["A01"])
        with open(os.path.join(folder, "task.json"), "w", encoding="utf-8") as fh:
            json.dump({"version": 1, "sheet": "task.csv", "mapping": "default", "deliveries": ["task.fbx"]}, fh)
        out = os.path.join(folder, "cells.csv")
        result = SheetConverter.convert(settings=os.path.join(folder, "task.json"), out=out)
        with open(out, encoding="utf-8", newline="") as fh:
            rows = list(csv.reader(fh))
        use = dict(zip(rows[0], next(r for r in rows if "use" in r)))
        self.assertEqual((use["Target"], use["Clip"], use["Settings"]), ("DOOR_GRP", "A01", ""))
        self.assertEqual([(i["step"], i["why"]) for i in result["left"]],
                         [("A01", "say how long the press is held: Settings hold=<seconds>")])

