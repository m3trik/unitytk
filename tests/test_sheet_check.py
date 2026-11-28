# !/usr/bin/python
# coding=utf-8
"""Tests for ``unitytk.sheets.SheetCheck``: a build sheet's strict check, alone or against
its delivery, each item naming its cell, its severity and its fix, and the take list it writes."""

import csv
import os
import sys

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

from unitytk.sheets import DeliveryIndex, SheetCheck

SHEET = """Step,Text On Placard,Step Contents,Asset Names,Animation Clip Names,Hint,Remarks
IN_01,Welcome.,Selecting 'Begin' triggers step advancement,,,,
A01,,Selecting the door triggers Door Open Animation,DOOR,,-Bouncing arrow pointing at the door,
A02,,YES and NO buttons appear on the placard,,,Select the door,
,,Selecting 'YES' triggers step A01,,,,
,,Selecting 'NO' triggers step A04,,,,
,,Auto Advance,,,,
A03,,Selecting the lever triggers Lever Animation,DOOR_RELEASE_LEVER,,-Bouncing arrow pointing at the lid,
A04,Enter the number.,A number pad appears on the placard,,,,
,,Entering '11' triggers step A05,,,,
A05,Done.,Entering '3' triggers step A01,,,,
,,Selecting 'Restart' triggers step IN_01,,,,
QUIZ,,,,,,
Q01,Why?,* To free the key,,,,
,,* To reach the door,,,,
"""


class SheetCheckTestCase(UnityTkTestCase):
    """The sheet alone: every item the format's check reports, each with its cell and fix."""

    def setUp(self):
        super().setUp()
        self.tmp = self.create_temp_dir()
        self.sheet = os.path.join(self.tmp, "task.csv")
        with open(self.sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(SHEET)
        self.check = SheetCheck.run(self.sheet)

    def _items(self, step, severity=None):
        return [
            i
            for i in self.check.items
            if i.step == step and (severity is None or i.severity == severity)
        ]

    def _has(self, step, severity, words, row=None, column=None):
        hits = [
            i
            for i in self._items(step, severity)
            if all(w in i.reason for w in words)
            and (row is None or i.row == row)
            and (column is None or i.column == column)
        ]
        self.assertTrue(
            hits,
            "%s %s %s not in %s"
            % (step, severity, words, [vars(i) for i in self._items(step)]),
        )
        self.assertTrue(all(i.fix for i in hits), "each names its fix")

    def test_a_column_no_field_takes_is_reported(self):
        self._has("", "warning", ["Remarks"], column="Remarks")

    def test_a_hint_line_no_hint_phrase_reads_belongs_on_the_placard(self):
        self._has("A02", "error", ["Select the door"], column="Hint")

    def test_an_animation_row_with_no_take_is_an_error_with_the_take_to_write(self):
        """A step's take cells are what give it a shot (the template's Shots option, "Steps with an
        animation clip"): an empty one left the step without a shot, so the format's old shortcut (one
        animation row may leave its cell empty) is gone, and the cell is the sheet's one mark of an
        animated step. Added: 2026-11-22"""
        self._has(
            "A01",
            "error",
            ["names no take", "no shot"],
            row=1,
            column="Animation Clip Names",
        )
        (item,) = [
            i for i in self._items("A01", "error") if "names no take" in i.reason
        ]
        self.assertIn("A01", item.fix, "the take the reading gives it, ready to write")
        self.assertFalse(
            [i for i in self._items("A02") if "names no take" in i.reason],
            "A02 plays no take",
        )

    def test_buttons_on_an_empty_placard_are_reported(self):
        self._has("A02", "warning", ["placard"], row=1)

    def test_a_jump_back_past_a_take_is_an_error_and_forward_past_one_a_warning(self):
        self._has("A02", "error", ["A01"], row=2)
        self._has("A02", "warning", ["A03"], row=3)

    def test_a_hint_lines_issue_names_the_hint_as_its_cell(self):
        self._has("A03", "error", ["the lid"], column="Hint")

    def test_a_jump_to_the_first_step_is_a_restart_never_a_jump_back(self):
        self.assertFalse(
            [i for i in self._items("A05") if i.row == 2], "the scene reloads"
        )
        jump = [
            i
            for i in SheetCheck.run(self.sheet, reading={"restart": False}).items
            if i.step == "A05" and i.row == 2
        ]
        self.assertEqual(
            [(i.severity, "replays step A01" in i.reason) for i in jump],
            [("error", True)],
            "a reading where the first step is no restart: a jump back like any other",
        )

    def test_choices_and_auto_advance_in_one_step_are_an_error(self):
        self._has("A02", "error", ["Auto Advance"], row=4)

    def test_an_entered_value_without_a_row_for_any_other_is_an_error_and_needs_no_pad_row(self):
        # The key pad is every answer row's: a step's "number pad appears" is a note, and none is no error.
        self._has("A04", "error", ["any other"], row=2)
        self.assertFalse([i for i in self._items("A05", "error") if "pad" in i.reason or "number" in i.reason])

    def test_a_question_marks_exactly_one_option_correct(self):
        self._has("Q01", "error", ["2"])

    def test_a_quiz_without_questions_and_checklist_ticks_no_list_holds_are_errors(
        self,
    ):
        sheet = os.path.join(self.tmp, "lists.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(
                "Step,Text On Placard,Step Contents,Asset Names\n"
                'IN_01,Start.,"A checklist appears on the placard: Door, Lid",\n'
                "A01,,Checklist item 'Lid' gets a check mark,\n"
                "A02,,Checklist item 'Bolt' gets a check mark,\n"
                "A03,,Checklist appears on placard with blank check boxes,\n"
                "A04,,Checklist item 'Fuse' gets a check mark,\n"
                "A05,,Checklist appears on placard with blank check boxes,\n"
                "A06,Done.,Selecting 'Take Quiz' triggers advancement to the quiz,\n"
            )
        found = SheetCheck.run(sheet)
        errors = {
            (i.step, i.reason.split(":")[0])
            for i in found.items
            if i.severity == "error"
        }
        self.assertIn(("A02", "no list before it holds 'Bolt'"), errors)
        self.assertIn(
            ("A05", "the list holds no items, and no row ticking one follows it"),
            errors,
        )
        self.assertIn(("A06", "no step after it asks a question"), errors)
        self.assertFalse(
            [e for e in errors if e[0] in ("A01", "A03", "A04")],
            "a listed tick, and a blank list its ticks fill, pass",
        )
        self.assertTrue(all(i.fix for i in found.items))

    def test_the_sheet_alone_is_its_own_delivery_and_writes_the_take_list(self):
        self.assertFalse(
            [i for i in self.check.items if "delivery has no" in i.reason],
            "the sheet's names are its delivery",
        )
        self.assertEqual(
            [(t["take"], t["step"], t["row"]) for t in self.check.takes],
            [("A01", "A01", 1), ("A03", "A03", 1)],
        )
        path = self.check.write_takes(os.path.join(self.tmp, "takes.csv"))
        with open(path, encoding="utf-8", newline="") as fh:
            rows = list(csv.reader(fh))
        self.assertEqual(rows[0], ["Take", "Step", "Row", "Step Contents"])
        self.assertEqual(
            rows[1],
            ["A01", "A01", "1", "Selecting the door triggers Door Open Animation"],
        )

    def test_a_strict_sheet_with_errors_fails_and_the_report_names_each_cell(self):
        self.assertFalse(self.check.passed)
        text = self.check.text()
        self.assertIn("A02 row 2 (Step Contents)", text)
        self.assertIn("fix:", text)

    def test_the_operation_returns_the_report_and_writes_the_take_list_when_asked(self):
        out = SheetCheck.check(self.sheet, takes=os.path.join(self.tmp, "t.csv"))
        self.assertEqual(out["passed"], False)
        self.assertEqual(out["errors"], len(self.check.errors))
        self.assertTrue(os.path.isfile(os.path.join(self.tmp, "t.csv")))


class SheetCheckReadsOnceTestCase(UnityTkTestCase):
    """The check parses its sheet once for the plan, the take list and its own checks, and names each column
    by the sheet's own header (the template's first name for it where the sheet has no such column)."""

    def test_the_sheet_is_parsed_once_and_columns_carry_the_sheets_own_headers(self):
        from unittest import mock

        from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

        sheet = os.path.join(self.create_temp_dir(), "s.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(SHEET)
        with mock.patch.object(Mapping, "resolve", wraps=Mapping.resolve) as resolve:
            SheetCheck.run(sheet)
        self.assertEqual(resolve.call_count, 1)
        tolerant = SheetCheck.run(sheet, mapping="default")
        jumps = [i for i in tolerant.items if i.step == "A02" and i.row]
        self.assertTrue(
            jumps and all(i.column == "Step Contents" for i in jumps),
            "the sheet writes Step Contents: the tolerant template's first name for it (Description) named a "
            "column the sheet does not have: %s" % [vars(i) for i in jumps],
        )


NARRATED_SHEET = """Step,Step Contents,Asset Names,Voice Support
A01,Auto Advance,,Open the door.
A02,Auto Advance,,Close the door.
A03,Auto Advance,,
"""


class NarrationCheckTestCase(UnityTkTestCase):
    """A sheet whose template narrates: a voiced step with no file yet is reported, and the narration
    standing in for recordings is said once, counting against nothing."""

    def test_a_line_not_narrated_yet_is_a_warning_and_narration_is_said_once(self):
        from pythontk.audio_utils.narrator import Narrator

        project = self.create_temp_dir()
        sheet = os.path.join(project, "task.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(NARRATED_SHEET)
        os.makedirs(os.path.join(project, "narration"))
        open(
            os.path.join(
                project, "narration", Narrator().key("Open the door.") + ".wav"
            ),
            "wb",
        ).close()
        check = SheetCheck.run(sheet, options={"audio": "narrate"}, base=project)
        missing = [i for i in check.items if "not narrated" in i.reason]
        self.assertEqual(
            [(i.step, i.severity) for i in missing],
            [("A02", "warning")],
            "a step with nothing to say is not",
        )
        self.assertIn("narration.render", missing[0].fix)
        said = [i for i in check.items if i.severity == "info"]
        self.assertEqual(len(said), 1)
        self.assertIn("1 of 2", said[0].reason)
        self.assertNotIn(
            said[0],
            check.warnings + check.errors,
            "information, counted against nothing",
        )
        plain = SheetCheck.run(sheet)
        self.assertFalse(
            [i for i in plain.items if "narrat" in i.reason],
            "a sheet that does not narrate is not checked for it",
        )


class StarterSheetTestCase(UnityTkTestCase):
    def test_the_interactive_starter_sheet_passes_the_strict_check(self):
        from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

        check = SheetCheck.run(str(Mapping.starter_sheet("interactive")))
        self.assertEqual(
            [i.line() for i in check.items], [], "a starter shows the format, clean"
        )
        self.assertTrue(check.takes)


class CellSheetCheckTestCase(UnityTkTestCase):
    """A sheet in cells is checked in its own words: each problem once, at the sheet's own header,
    its fix the word and cells to write."""

    HEADER = "Step,Step Name,Voice,Placard,Audio,Action,Target,With,Clip,Members,Label,To,Settings,Notes"

    def _check(self, *rows: str, header: str = "") -> SheetCheck:
        sheet = os.path.join(self.create_temp_dir(), "cells.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write("\n".join(((header or self.HEADER),) + rows) + "\n")
        return SheetCheck.run(sheet, mapping="interactive")

    def _lines(self, check: SheetCheck) -> list:
        return [i.line() for i in check.items]

    def test_an_empty_take_is_said_once_at_the_sheets_own_column(self):
        """It was said twice, once at "Animation Clip Names", a column the sheet does not have. Added: 2026-11-25"""
        check = self._check("A01,Open,Open it.,,,,,,,,,,,", ",,,,,use,DOOR,,,,,,,", ",,,,,use,LID,,,,,,,")
        self.assertEqual(self._lines(check), [
            "A01 row %d (Clip) error: use needs its Clip -- fix: write the take it plays (its step's ID; <ID>_<n> "
            "where the step plays several)" % n for n in (1, 2)])

    def test_a_question_and_a_typed_answer_say_their_fix_as_cells(self):
        """The fixes spoke the older sheets ("a leading *", "answer_else"). Added: 2026-11-25"""
        check = self._check(
            "Q01,Ask,Which?,Which?,,,,,,,,,,", ",,,,,button,,,,,Two,retry,,", ",,,,,button,,,,,Three,retry,,",
            "Q02,Enter,How many?,How many?,,,,,,,,,,", ",,,,,answer,,,,,4,Q03,,",
            "Q03,Done,Done.,,,,,,,,,,,")
        self.assertEqual(self._lines(check), [
            "Q01 error: the question has 0 right answers -- fix: write exactly one: a button row whose To is next",
            "Q02 error: no row of the step takes any other value -- fix: add an answer row whose Label is *",
        ])

    def test_a_step_rows_own_issue_names_its_step_and_a_sheet_with_no_step_is_an_error(self):
        check = self._check("A01,Open,Open it.,,,,,,,,,Z99,,")
        self.assertEqual([(i.step, i.column, i.reason) for i in check.items], [("A01", "To", "the sheet has no step Z99")])
        empty = self._check()
        self.assertEqual([(i.severity, i.reason) for i in empty.items], [("error", "the sheet holds no step")])
        self.assertFalse(empty.passed)

    def test_a_sheet_with_no_notes_column_reads_and_a_missing_column_is_said_once(self):
        """A sheet in cells without its Notes column (which nothing reads) could not be read at all. Added: 2026-11-25"""
        check = self._check(
            "A01,Open,Open it.,,,,,,", ",,,use,DOOR,,,,", ",,,use,LID,,,,",
            header="Step,Step Name,Voice,Action,Target,With,Label,To,Settings")
        self.assertEqual(self._lines(check),
                         ["A01 (Action) error: the sheet has no Clip column, which use needs -- fix: add the column"])


class CellSheetDeliveryCheckTestCase(UnityTkTestCase):
    """A sheet in cells against a delivery: names as the scene spells them, a take judged by its own
    row's cells, a tool the user can have in hand."""

    HEADER = CellSheetCheckTestCase.HEADER
    #: A cabinet: a bolt and a lid inside it, a wrench and a key on a table, a viewpoint.
    NODES = ["CABINET", "CABINET|BOLT_GRP", "CABINET|BOLT_GRP|BOLT_GEO", "CABINET|LID_GRP", "TABLE",
             "TABLE|WRENCH_GRP", "TABLE|KEY_GRP", "VIEW_BACK"]

    def _check(self, *rows: str, moved=None, worn=None, states=None) -> SheetCheck:
        sheet = os.path.join(self.create_temp_dir(), "cells.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write("\n".join((self.HEADER,) + rows) + "\n")
        index = DeliveryIndex(list(self.NODES), ["B01_1", "B01_2"], "cabinet.fbx",
                              moved=moved or {"B01_1": ("CABINET|BOLT_GRP",), "B01_2": ("CABINET|LID_GRP",)},
                              worn=worn, states=states)
        return SheetCheck.run(sheet, index, mapping="interactive")

    def test_a_state_its_targets_light_lacks_is_said_where_the_delivery_names_the_lights_states(self):
        """``set`` with a state the part's emissive group lacks checked clean: only Unity's build refused it.
        Added: 2026-11-26"""
        worn, states = {"CABINET|LID_GRP": ["AC:lamp_mat"]}, {"lamp_mat": ["off", "green", "amber"]}
        rows = self.STEP[:2] + (",,,,,set,LID_GRP,,,,,,state=purple,", ",,,,,set,LID_GRP,,,,,,state=amber,",
                                ",,,,,set,BOLT_GRP,,,,,,state=purple,")
        check = self._check(*rows, worn=worn, states=states)
        self.assertEqual([(i.row, i.column, i.severity, i.reason, i.fix) for i in check.items if "state" in i.reason], [
            (2, "Settings", "warning", "LID_GRP has no state 'purple': its indicator light has off, green, amber",
             "write one of those, or give its emissive group the state in the DCC"),
        ])  # the bolt's material is none the record names: Unity's build gives it the defaults, and says
        self.assertEqual([i.line() for i in self._check(*rows).items if "state" in i.reason], [], "no record: Unity's to say")

    STEP = ("B01,Remove the bolt,Remove the bolt.,,,,,,,,,,,", ",,,,,travel,CABINET,,,,,,where=VIEW_BACK,",
            ",,,,,highlight,BOLT_GRP,,,,,,,", ",,,,,take,WRENCH_GRP,,,,,,,")

    def test_a_take_that_moves_its_rows_target_is_its_rows_whatever_else_the_step_names(self):
        """The row's Target counted for nothing where another row named it too (its highlight), so the take
        was judged by its tool alone, and a travel's CABINET, above the bolt, read as what it moved:
        "the take B01_1 moves CABINET (row 1) and none of this row's WRENCH". Added: 2026-11-25"""
        check = self._check(*self.STEP, ",,,,,use,BOLT_GRP,WRENCH_GRP,B01_1,,,,,", ",,,,,play,,,B01_2,LID_GRP,,,after=B01_1,")
        self.assertEqual([i.line() for i in check.items], [])
        swapped = self._check(*self.STEP, ",,,,,use,BOLT_GRP,WRENCH_GRP,B01_2,,,,,", ",,,,,play,,,B01_1,LID_GRP,,,after=B01_2,")
        self.assertEqual([(i.row, i.severity, i.reason) for i in swapped.items if "moves" in i.reason], [
            (4, "error", "the take B01_2 moves LID_GRP (row 5) and none of this row's BOLT_GRP, WRENCH_GRP"),
            (5, "error", "the take B01_1 moves BOLT_GRP (row 4) and none of this row's LID_GRP"),
        ])
        still = self._check(*self.STEP, ",,,,,use,BOLT_GRP,WRENCH_GRP,B01_1,,,,,", ",,,,,play,,,B01_2,WRENCH_GRP,,,after=B01_1,")
        self.assertEqual([(i.row, i.severity, i.reason) for i in still.items if "moves" in i.reason],
                         [(5, "warning", "the take B01_2 moves none of this row's WRENCH_GRP")])

    def test_a_name_found_by_ignoring_case_alone_is_a_warning_with_the_scenes_spelling(self):
        """``bolt_grp`` read as BOLT_GRP with nothing said, while the DCC's tools, which tell case apart, find no
        such node. Added: 2026-11-25"""
        check = self._check(*self.STEP[:2], ",,,,,highlight,bolt_grp,,,,,,,", ",,,,,highlight,Cabinet|Bolt_Grp,,,,,,,",
                            ",,,,,highlight,LID_GRP,,,,,,,", ",,,,,highlight,CABINET|LID_GRP,,,,,,,")
        self.assertEqual([(i.row, i.column, i.severity, i.reason, i.fix) for i in check.items if i.step == "B01"], [
            (2, "Target", "warning", "the delivery spells bolt_grp as BOLT_GRP", "write it as the scene does, whose tools tell "
                                                                          "names apart by case"),
            (3, "Target", "warning", "the delivery spells Cabinet|Bolt_Grp as CABINET|BOLT_GRP",
             "write it as the scene does, whose tools tell names apart by case"),
        ])  # a path's names are found as a name is (2026-11-26: a path's tail names a node)

    def test_a_tool_no_row_puts_in_hand_by_its_step_is_a_warning(self):
        """``use BOLT_GRP`` with a wrench no ``take`` row gives the user read clean: the step could not be done.
        Added: 2026-11-25"""
        rows = ("A01,Get it,Take the key.,,,,,,,,,,,", ",,,,,take,KEY_GRP,,,,,,,",
                "B01,Remove the bolt,Remove the bolt.,,,,,,,,,,,", ",,,,,use,BOLT_GRP,WRENCH_GRP,B01_1,,,,,",
                ",,,,,play,,,B01_2,LID_GRP,,,after=B01_1,")
        check = self._check(*rows)
        self.assertEqual([(i.step, i.row, i.column, i.severity, i.reason, i.fix) for i in check.items], [
            ("B01", 1, "With", "warning", "no row puts WRENCH_GRP in the user's hand by this step",
             "add a take row for it, in this step or an earlier one"),
        ])
        # Taken in an earlier step and kept, or taken in its own step (rows of a step are open together): in hand.
        kept = self._check("A01,Get it,Take it.,,,,,,,,,,,", ",,,,,take,WRENCH_GRP,,,,,,,", *rows[2:])
        self.assertEqual([i.line() for i in kept.items], [])
        same = self._check(rows[2], rows[3], ",,,,,take,WRENCH_GRP,,,,,,,", rows[4])
        self.assertEqual([i.line() for i in same.items], [])
        # Given back before the step that needs it: said.
        back = self._check("A01,Get it,Take it.,,,,,,,,,,,", ",,,,,take,WRENCH_GRP,,,,,,,", ",,,,,return,WRENCH_GRP,,,,,,,", *rows[2:])
        self.assertEqual([i.reason for i in back.items], ["no row puts WRENCH_GRP in the user's hand by this step"])


class SheetCheckDeliveryTestCase(UnityTkTestCase):
    """Against a delivery: an authored pointer whose part is gone."""

    def test_a_pointer_whose_part_the_delivery_lacks_is_reported(self):
        sheet = os.path.join(self.create_temp_dir(), "s.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write("Step,Step Contents,Asset Names\nA01,Highlight the door,DOOR\n")
        index = DeliveryIndex(
            [
                "CABINET",
                "CABINET|DOOR_GRP",
                "POINTS",
                "POINTS|DOOR_POINT",
                "POINTS|LID_POINT",
            ],
            [],
            "d.fbx",
        )
        check = SheetCheck.run(sheet, index)
        pointers = [i for i in check.items if "POINT" in i.reason]
        self.assertEqual(
            [(i.severity, "LID_POINT" in i.reason) for i in pointers], [("warning", True)]
        )
        self.assertTrue(check.passed, "a warning does not fail the check")

    LOCKS = (
        "Step,Step Contents,Asset Names,Animation Clip Names\n"
        "A01,Selecting the upper lock triggers Unlock Upper Animation,LOCK_UPPER,A01_1\n"
        ",Selecting the lower lock triggers Unlock Lower Animation,LOCK_LOWER,A01_2\n"
        "A02,Selecting the door triggers Door Open Animation,DOOR,A02\n"
    )

    def _locks(self, jumps):
        sheet = os.path.join(self.create_temp_dir(), "s.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(self.LOCKS)
        index = DeliveryIndex(
            [
                "RIG",
                "RIG|LOCK_UPPER",
                "RIG|LOCK_LOWER",
                "RIG|DOOR",
                "RIG|KEYS",
                "RIG|RING",
            ],
            ["A01_1", "A01_2", "A02"],
            "d.fbx",
            jumps=jumps,
        )
        return SheetCheck.run(sheet, index)

    def test_a_take_that_ends_on_the_next_takes_setup_is_reported_at_its_row(self):
        """Played alone, such a take shows the next step's first pose before its own is over (a real task:
        the keys went to the lower lock while the upper one was still the step). The delivery's reader finds
        them (``FbxFile.take_setup_jumps``); the check names the row, the frame and the fix. Added: 2026-11-22"""
        jump = {
            "take": "A01_1",
            "next": "A01_2",
            "frame": 100,
            "shown": 11,
            "properties": ["Lcl Translation"],
        }
        check = self._locks(
            [
                dict(jump, target="KEYS"),
                dict(jump, target="RING#2", frame=101, shown=10),
            ]
        )
        (item,) = [i for i in check.items if "first pose" in i.reason]
        self.assertEqual(
            (item.step, item.row, item.column, item.severity),
            ("A01", 1, "Animation Clip Names", "warning"),
        )
        self.assertIn("A01_1 ends on A01_2's first pose", item.reason)
        self.assertIn("KEYS and 1 more", item.reason)
        self.assertIn("11 frames before it ends", item.reason)
        self.assertIn("before frame 100", item.fix)
        self.assertTrue(check.passed, "a warning: the artist's call")

    def test_a_jump_on_the_last_frame_says_so_and_a_take_no_row_names_is_the_deliverys(
        self,
    ):
        jump = {
            "take": "EXTRA",
            "next": "A02",
            "target": "KEYS",
            "frame": 240,
            "shown": 0,
            "properties": ["Lcl Rotation"],
        }
        (item,) = [i for i in self._locks([jump]).items if "first pose" in i.reason]
        self.assertEqual((item.step, item.row, item.column), ("", None, ""))
        self.assertIn("on its last frame", item.reason)

    def test_a_delivery_with_no_such_take_adds_nothing(self):
        self.assertFalse([i for i in self._locks([]).items if "first pose" in i.reason])

    def test_several_files_keep_each_files_jumps(self):
        one = DeliveryIndex(
            ["A"],
            ["T1", "T2"],
            "a.fbx",
            jumps=[
                {
                    "take": "T1",
                    "next": "T2",
                    "target": "A",
                    "frame": 9,
                    "shown": 0,
                    "properties": ["Lcl Translation"],
                }
            ],
        )
        two = DeliveryIndex(["B"], ["T3"], "b.fbx")
        merged = DeliveryIndex.merged([one, two])
        self.assertEqual([j["take"] for j in merged.jumps], ["T1"])
        self.assertEqual(
            [j["take"] for j in merged.with_suffixes(["GEO"]).jumps], ["T1"]
        )
