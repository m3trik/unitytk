# !/usr/bin/python
# coding=utf-8
"""Tests for a sheet in the cell format read into a plan (``unitytk/sheets/_cell_reading.py``).

A row with an Action is read by its cells through the reading's ``words``, never by a
phrase. The oracle is the Tasks sample: its task written in cells reads to the plan
its committed sheet of sentences reads to, so everything built from that plan stands."""

import csv
import io
import os
import sys
import tempfile
import unittest
from collections import Counter
from pathlib import Path

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

from unitytk.sheets import SheetPlan

FOLDER = Path(__file__).resolve().parent.parent / "com.m3trik.unitytk" / "Samples~" / "Tasks"
HEADER = "Step,Step Name,Voice,Placard,Audio,Action,Target,With,Clip,Members,Label,To,Settings,Notes"

#: The Tasks sample's task, written in cells: each name a node's own, as the delivery spells it.
TASK = (
    "INTRO",
    "IN_01,Welcome,Welcome to the cabinet service task.,,,note,,,,,,,,The cabinet stands in front of you.",
    "IN_02,Begin,Select 'Begin' to start.,,,button,,,,,Begin,next,,",
    "SECTION A: Open the cabinet",
    "A01,Unlock the latches,Unlock both latches with the key.,,,highlight,CABINET_LATCHES_GRP,,,,,,,",
    ",,,,,indicate,KEY_GRP,,,,,,,",
    ",,,,,teleport,CABINET,,,,,,where=VIEW_FRONT,",
    ",,,,,take,KEY_GRP,,,,,,,",
    ",,,,,use,LATCH_UPPER_GRP,KEY_GRP,A01_1,,,,,",
    ",,,,,use,LATCH_LOWER_GRP,KEY_GRP,A01_2,,,,,",
    ",,,,,return,KEY_GRP,,,,,,,",
    "A02,Open the door,Now open the door.,Select the door to open it,,highlight,DOOR_GRP,,,,,,,",
    ",,,,,use,DOOR_GRP,,A02,,,,,",
    "SECTION B: Replace the bolt",
    "B01,Remove the bolt,Remove the bolt with the wrench.,,,highlight,BOLT_GRP,,,,,,,",
    ",,,,,indicate,WRENCH_GRP,,,,,,,",
    ",,,,,teleport,CABINET,,,,,,where=VIEW_BACK,",
    ",,,,,take,WRENCH_GRP,,,,,,,",
    ",,,,,use,BOLT_GRP,WRENCH_GRP,B01_1,,,,,",
    ",,,,,play,,,B01_2,LID_GRP,,,after=B01_1,",
    ",,,,,fade_out,BOLT_GRP,,,,,,,",
    ",,,,,return,WRENCH_GRP,,,,,,,",
    "B02,Test the lamp,Press the button.,,,select,BUTTON_GRP,,,,,,,",
    "B03,Did it light?,Did the lamp light?,Did the lamp light?,,button,,,,,YES,B04,,",
    ",,,,,button,,,,,NO,B03.5,,",
    "B03.5,Wrong answer,That is not right. Try again.,,,button,,,,,Try Again,B03,,",
    "B04,Done,Well done.,,,button,,,,,Exit,exit,,",
)


def _as_written(row: str) -> list:
    """*row* as the sheet writes it. A test writes a step and its first action on one line for
    brevity; a sheet writes the step's own row, then the action on the row below it."""
    cells = next(csv.reader(io.StringIO(row)))
    if len(cells) != len(HEADER.split(",")) or not cells[0] or not any(cells[5:11]):
        return [cells]
    return [cells[:5] + [""] * 9, [""] * 5 + cells[5:]]


def _plan(*rows: str, delivery=None, mapping: str = "interactive") -> SheetPlan:
    fd, path = tempfile.mkstemp(suffix=".csv")
    with os.fdopen(fd, "w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(HEADER.split(","))
        for row in rows:
            writer.writerows(_as_written(row))
    try:
        given = delivery if delivery is not None and not isinstance(delivery, (str, os.PathLike)) else None
        return SheetPlan.from_sheet(path, given or str(delivery or FOLDER / "cabinet.fbx"), mapping=mapping)
    finally:
        os.remove(path)


def _shape(action, without=()):
    """An action as what a builder is handed: no text, and none of the fields one format alone gives (a sentence's
    phrase; a cell row's ``helpers``, its own helpers' names derived from its Target cell, which a sentence sheet has
    none of: converted to cells, it gets them)."""
    fields = {k: v for k, v in action.fields.items() if k not in ("phrase", "helpers") + tuple(without)}
    return (action.behavior, tuple(action.targets), action.clip, action.to, bool(action.restart),
            tuple(sorted((k, str(v)) for k, v in fields.items())), tuple(sorted(action.settings.items())))


class SampleInCellsTestCase(UnityTkTestCase):
    def test_the_task_in_cells_reads_to_the_plan_its_sentences_read_to(self):
        cells = _plan(*TASK)
        sentences = SheetPlan.load(FOLDER / "cabinet.plan.json")
        self.assertEqual(cells.issues, [])
        self.assertEqual([a.issues + a.warnings for s in cells.steps for a in s.actions if a.issues or a.warnings], [])
        self.assertEqual([s.id for s in cells.steps], [s.id for s in sentences.steps])
        # A sentence sheet says what a step does anyway (it goes on; its buttons are there); a cell sheet
        # says where a teleport faces, which the sentences left to the viewpoint.
        unsaid = ("advance", "buttons")
        for mine, theirs in zip(cells.steps, sentences.steps):
            self.assertEqual(mine.fields.get("voice"), theirs.fields.get("voice"), mine.id)
            self.assertEqual(mine.fields.get("placard"), theirs.fields.get("placard"), mine.id)
            self.assertEqual(
                Counter(_shape(a, ("facing", "faces")) for a in mine.actions),
                Counter(_shape(a, ("facing", "faces")) for a in theirs.actions if a.behavior not in unsaid),
                mine.id,
            )
        self.assertEqual(sorted(a.clip for s in cells.steps for a in s.actions if a.clip),
                         ["A01_1", "A01_2", "A02", "B01_1", "B01_2"])

    def test_a_teleport_stands_at_its_steps_start_and_a_chained_take_after_the_one_it_follows(self):
        plan = _plan(
            "B01,Remove,Remove the bolt.,,,play,,,B01_2,LID_GRP,,,after=B01_1,",
            ",,,,,button,,,,,Next,next,,",
            ",,,,,use,BOLT_GRP,WRENCH_GRP,B01_1,,,,,",
            ",,,,,teleport,CABINET,,,,,,where=VIEW_BACK,",
            ",,,,,teleport,CABINET,,,,,,where=VIEW_FRONT; when=end,",
        )
        self.assertEqual([(a.behavior, a.clip or (a.targets[0].split("|")[-1] if a.targets else ""))
                          for a in plan.steps[0].actions],
                         [("teleport", "VIEW_BACK"), ("pause", ""), ("use", "B01_1"), ("play", "B01_2"),
                          ("teleport", "VIEW_FRONT")])


class ConvertedToCellsTestCase(UnityTkTestCase):
    """``sheet.convert``: a sheet of sentences rewritten in cells reads to the plan it read to."""

    def _converted(self, sheet: str, delivery: str):
        from unitytk.sheets import SheetConverter

        out = os.path.join(self.create_temp_dir(), "cells.csv")
        result = SheetConverter.convert(sheet, delivery, out, mapping="interactive")
        with open(out, encoding="utf-8", newline="") as handle:
            return result, list(csv.reader(handle)), out

    def test_the_sample_task_converted_reads_to_the_plan_it_read_to(self):
        result, rows, out = self._converted(str(FOLDER / "cabinet.csv"), str(FOLDER / "cabinet.fbx"))
        self.assertEqual(result["left"], [], "nothing for a person to decide")
        self.assertEqual(rows[0], HEADER.split(","))
        self.assertEqual([r for r in rows[1:] if r[0] and any(r[5:11])], [], "a step's row holds no action")
        # A step the old format numbered with a dot is spelled as every tool spells it, and its jumps with it.
        self.assertIn("B03A", [r[0] for r in rows])
        self.assertIn(["NO", "B03A"], [[r[10], r[11]] for r in rows])
        cells = SheetPlan.from_sheet(out, str(FOLDER / "cabinet.fbx"), mapping="interactive")
        sentences = SheetPlan.load(FOLDER / "cabinet.plan.json")
        self.assertEqual(cells.issues, [])
        self.assertEqual([i for s in cells.steps for a in s.actions for i in a.issues + a.warnings], [])
        ids = {theirs.id: mine.id for mine, theirs in zip(cells.steps, sentences.steps)}

        def held(action):
            behavior, targets, clip, to, restart, fields, settings = _shape(action, ("facing",))
            return behavior, targets, clip, ids.get(to, to), restart, fields, settings

        for mine, theirs in zip(cells.steps, sentences.steps):
            self.assertEqual(Counter(_shape(a, ("facing", "faces")) for a in mine.actions),
                             Counter(held(a) for a in theirs.actions if a.behavior not in ("advance", "buttons")), mine.id)

    def test_a_hide_row_becomes_its_show_rows_span_and_what_is_undecided_is_left_by_row(self):
        sheet = os.path.join(self.create_temp_dir(), "old.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as handle:
            handle.write(
                "Step,Step Name,Step Contents,Asset Names,Action,Animation Clip Names,Text On Placard,Hint,Voice Support,Audio Files\n"
                "A01,Look,PiP screen shows the door,DOOR,,,,,Look at the door.,\n"
                ",,The button turns on,BUTTON,,,,,,\n"
                ",,Wave at the cabinet,,,,,,,\n"
                "A02,Go on,PiP screen fades away,,,,,,Good.,\n"
            )
        result, rows, _ = self._converted(sheet, str(FOLDER / "cabinet.fbx"))
        by_action = {r[5]: r for r in rows[1:] if r[5]}
        self.assertEqual(by_action["pip"][12], "until=A02", "the picture's own row says until when")
        self.assertEqual([r[5] for r in rows[1:] if r[5]], ["pip", "highlight"], "the hide row is no row")
        # A row no phrase reads keeps its words, with no word of its own, and is left for a person.
        self.assertIn("Wave at the cabinet", [r[13] for r in rows[1:] if not r[5] and not r[0]])
        self.assertEqual([item["step"] for item in result["left"]], ["A01"])


class WordsTestCase(UnityTkTestCase):
    def _one(self, *rows):
        plan = _plan(*rows)
        return plan.steps[0].actions

    def test_a_button_is_the_choice_its_to_says(self):
        actions = self._one(
            "A01,Ask,Ready?,,,button,,,,,Go,next,,",
            ",,,,,button,,,,,Leave,exit,,",
            ",,,,,button,,,,,Again,restart,,",
            ",,,,,button,,,,,Wait,next,when=start,",
        )
        self.assertEqual([(a.behavior, a.fields.get("label"), a.to, bool(a.restart), a.settings) for a in actions],
                         [("pause", "Go", None, False, {}), ("exit", "Leave", None, False, {}),
                          ("goto", "Again", "A01", True, {}), ("pause", "Wait", None, False, {"pauseWhen": "Start"})])

    def test_a_question_is_a_step_with_a_wrong_button(self):
        actions = self._one(
            "Q01,Question,Which tool?,Which tool?,,button,,,,,Wrench,next,,",
            ",,,,,button,,,,,Key,retry,reply=Not that one.,",
        )
        self.assertEqual([(a.behavior, a.fields, a.settings) for a in actions],
                         [("option", {"label": "Wrench", "value": "correct"}, {}),
                          ("option", {"label": "Key"}, {"wrongChoiceReply": "Not that one."})])

    def test_an_answer_brings_its_key_pad_once_and_any_other_is_its_own_kind(self):
        actions = self._one(
            "C10,Dial,Enter the number.,,,answer,,,,,11,C11,,",
            ",,,,,answer,,,,,*,C10,,",
            "C11,Right,Correct.,,,note,,,,,,,,",
        )
        self.assertEqual([(a.behavior, a.fields.get("value"), a.to, a.settings) for a in actions],
                         [("number_pad", None, None, {}), ("answer", "11", "C11", {}),
                          ("answer", None, "C10", {"answerKind": "Otherwise"})])

    def test_a_use_is_set_by_its_trigger_and_a_teleports_ring_is_its_own_span(self):
        actions = self._one(
            "A01,Open,Open it.,,,use,BUTTON_GRP,,A02,,,,trigger=hold,",
            ",,,,,use,DOOR_GRP,KEY_GRP,A01_1,,,,trigger=near,",
            ",,,,,use,LID_GRP,,A01_2,,,,hold=2,",
            ",,,,,teleport,CABINET,,,,,,where=VIEW_FRONT; ring=step,",
            ",,,,,pip,BOLT_GRP,,,,,,until=A02,",
            ",,,,,set,BUTTON_GRP,,,,,,state=amber,",
            "A02,Next,Go on.,,,note,,,,,,,,",
        )
        got = [(a.behavior, a.settings, {k: v.split("|")[-1] for k, v in a.fields.items() if k != "helpers"}) for a in actions]
        # A teleport's ring is up before it and for as long as its `ring` says: here until its step ends.
        self.assertEqual(got[0:2], [("marker", {}, {}), ("teleport", {}, {"faces": "CABINET"})])
        self.assertEqual(got[2], ("use", {"useTrigger": "HoldToPlay"}, {}))
        self.assertEqual(got[3], ("place", {}, {"tool": "KEY_GRP"}))
        self.assertEqual(got[4], ("use", {"useTrigger": "TimedPress", "pressSeconds": "2"}, {}))
        # A picture is captioned by its Label alone (a row's words only name its object): with none, no caption.
        self.assertEqual(got[5:7], [("pip", {"captionFrom": "Label"}, {}), ("set_state", {}, {"state": "amber"})])
        self.assertEqual(got[7], ("marker", {"markerShow": "Hide"}, {}))
        self.assertEqual([t.split("|")[-1] for t in actions[0].targets], ["VIEW_FRONT"], "the ring is that viewpoint's")
        self.assertEqual(actions[5].fields["helpers"], ["CAM_A01_BOLT_GRP"], "a picture naming no camera: its own, as every tool names it")
        self.assertEqual([i for a in actions for i in a.issues + a.warnings if "has no" in i or "takes no" in i], [])
        later = _plan(
            "A01,Open,Open it.,,,pip,BOLT_GRP,,,,,,until=A02,",
            "A02,Next,Go on.,,,highlight,DOOR_GRP,,,,,,,",
        ).steps[1].actions
        self.assertEqual([(a.behavior, a.settings) for a in later], [("pip", {"pipShow": "Hide"}), ("highlight", {})])

    def test_a_checklist_goes_at_the_step_its_span_names(self):
        # A real task's checklist stayed on the placard to the end, over the quiz's buttons: every shown thing has a span.
        from unitytk.sheets.clause_book import ClauseBook

        plan = _plan(
            'A01,Open,Open it.,,,checklist,,,,,"Door, Lid",,until=A02,',
            "A02,Next,Go on.,,,highlight,DOOR_GRP,,,,,,,",
        )
        self.assertEqual([(a.behavior, a.settings, a.warnings) for a in plan.steps[0].actions], [("checklist", {}, [])])
        self.assertEqual([(a.behavior, a.settings) for a in plan.steps[1].actions],
                         [("checklist", {"checklistShow": "Hide"}), ("highlight", {})])
        # A row that takes the list away lists nothing: its settings' own template speaks for it.
        book = ClauseBook()
        self.assertEqual(book.roles("checklist"), {"lists"})
        self.assertEqual(book.roles("checklist", {"checklistShow": "Hide"}), set())
        self.assertEqual(book.roles("answer", {"answerKind": "Otherwise"}), {"advances", "entered", "otherwise"})

    def test_a_name_is_found_exactly_and_each_issue_names_its_cell(self):
        actions = self._one(
            "A01,Open,Open it.,,,use,HATCH,SPANNER,A02,,,,,",
            ",,,,,hilight,DOOR_GRP,,,,,,,",
            ",,,,,highlight,DOOR_GRP,,,,,,style=steady,",
        )
        issues = [i for a in actions for i in a.issues]
        self.assertTrue(any(i.startswith("row 1 (Target): the delivery has no HATCH") for i in issues), issues)
        self.assertTrue(any(i.startswith("row 1 (With): the delivery has no SPANNER") for i in issues), issues)
        self.assertTrue(any(i.startswith("row 2 (Action): 'hilight' is no action") for i in issues), issues)
        self.assertEqual(actions[1].behavior, "unread")
        # An object a setting names is said at Settings, where it is written (a real task: at Target).
        placed = [i for a in self._one("A01,Open,Open it.,,,teleport,DOOR_GRP,,,,,,where=VIEW_GONE,") for i in a.issues]
        self.assertTrue(any(i.startswith("row 1 (Settings): the delivery has no VIEW_GONE") for i in placed), placed)
        # A setting its builder takes is handed on under the builder's name for it (a highlight's style, 2026-11-25).
        self.assertEqual((actions[2].settings, actions[2].warnings), ({"highlightStyle": "steady"}, []))
        # A picture's own camera, named as where it is taken from, is handed on as a node (2026-11-26: it was a named gap).
        (shown,) = self._one("A01,Open,Open it.,,,pip,DOOR_GRP,,,,,,where=VIEW_FRONT,")
        self.assertEqual((str(shown.fields.get("from", "")).split("|")[-1], shown.warnings, shown.issues), ("VIEW_FRONT", [], []))

    def test_a_name_two_nodes_share_is_told_apart_by_the_end_of_its_path(self):
        # The real task (2026-11-26): the installed solenoid's lights group shares its name with the spare's, and only
        # its whole path, eight names long, told them apart.
        from unitytk.sheets import DeliveryIndex

        index = DeliveryIndex(["ROOM", "ROOM|RACK", "ROOM|RACK|SOLENOID_LOC", "ROOM|RACK|SOLENOID_LOC|PCB_LIGHTS_GRP",
                               "ROOM|SPARES", "ROOM|SPARES|SOLENOID_SPARE", "ROOM|SPARES|SOLENOID_SPARE|PCB_LIGHTS_GRP"],
                              [], "d.fbx")
        installed, spare = "ROOM|RACK|SOLENOID_LOC|PCB_LIGHTS_GRP", "ROOM|SPARES|SOLENOID_SPARE|PCB_LIGHTS_GRP"
        self.assertEqual(index.named("SOLENOID_LOC|PCB_LIGHTS_GRP"), [installed])
        self.assertEqual(index.named("solenoid_loc|pcb_lights_grp"), [installed], "names ignore case, as a name does")
        self.assertEqual(index.spelling("solenoid_loc|pcb_lights_grp"), "SOLENOID_LOC|PCB_LIGHTS_GRP")
        self.assertEqual(index.named("RACK|PCB_LIGHTS_GRP"), [], "a tail is the path's last names, none skipped")
        self.assertEqual(sorted(index.named("PCB_LIGHTS_GRP")), [installed, spare])
        self.assertEqual((index.shortest(installed), index.shortest("ROOM|RACK")), ("SOLENOID_LOC|PCB_LIGHTS_GRP", "RACK"))
        plan = _plan("A01,Open,Open it.,,,highlight,SOLENOID_LOC|PCB_LIGHTS_GRP,,,,,,,", ",,,,,highlight,PCB_LIGHTS_GRP,,,,,,,",
                     delivery=index)
        told, shared = plan.steps[0].actions
        self.assertEqual((told.targets, told.issues), ([installed], []))
        self.assertEqual(shared.issues, ["row 2 (Target): PCB_LIGHTS_GRP names 2 nodes: write the one meant as the end of its "
                                         "path that tells it apart (SOLENOID_LOC|PCB_LIGHTS_GRP, SOLENOID_SPARE|PCB_LIGHTS_GRP)"])

    def test_a_cell_names_a_node_as_the_delivery_spells_it_never_by_its_object_name(self):
        """``DOOR`` found ``CABINET|DOOR_GRP`` with nothing said, so a sheet read clean here named nothing in the DCC's
        Shot Manifest, which finds a node as written. Added: 2026-11-26"""
        from unitytk.sheets import DeliveryIndex

        actions = self._one("A01,Open,Open it.,,,highlight,DOOR,,,,,,,", ",,,,,take,KEY_GRP,,,,,,,")
        self.assertEqual([i for a in actions for i in a.issues],
                         ["row 1 (Target): the delivery has no DOOR: correct the name, or have the artist add it"])
        index = DeliveryIndex(["A|bolt", "B|BOLT", "C|DOOR_GRP", "C|DOOR_GRP|DOOR_GEO"], aliases={"OLD_DOOR": "DOOR_GRP"})
        self.assertEqual((index.named("bolt"), index.named("BOLT")), (["A|bolt"], ["B|BOLT"]), "the name as written wins")
        self.assertEqual(sorted(index.named("Bolt")), ["A|bolt", "B|BOLT"], "neither as written: both, said as such")
        self.assertEqual((index.named("DOOR"), index.find("DOOR")), ([], ["C|DOOR_GRP"]), "an object name is a sentence's")
        self.assertEqual(index.named("old_door"), ["C|DOOR_GRP"], "a declared alias, ignoring case")
        self.assertEqual(index.spelling("door_grp"), "DOOR_GRP")

    def test_an_alias_cycle_names_nothing(self):
        """``{"A": "B", "B": "A"}`` (a settings file's renamed nodes) recursed until Python gave up. Added: 2026-11-26"""
        from unitytk.sheets import DeliveryIndex

        index = DeliveryIndex(["ROOT|C_GRP"], aliases={"A": "B", "B": "A", "D": "d"})
        self.assertEqual((index.find("A"), index.named("B"), index.named("D")), ([], [], []))

    def test_a_name_is_looked_up_among_the_nodes_sharing_its_last_name(self):
        """20,000 nodes: 300 tails took 23 s and a 200-row sheet 4 s, each name tested against every path.
        Added: 2026-11-26"""
        import time

        from unitytk.sheets import DeliveryIndex

        paths = ["ROOT"] + ["ROOT|G%d" % g for g in range(200)]
        paths += ["ROOT|G%d|PART_%d_GRP" % (g, n) for g in range(200) for n in range(100)]
        index = DeliveryIndex(paths)
        started = time.perf_counter()
        for n in range(100):
            self.assertEqual(index.named("G%d|PART_%d_GRP" % (n, n)), ["ROOT|G%d|PART_%d_GRP" % (n, n)])
            self.assertEqual(index.spelling("part_%d_grp" % n), "PART_%d_GRP" % n)
            self.assertEqual(index.shortest("ROOT|G%d|PART_%d_GRP" % (n, n)), "G%d|PART_%d_GRP" % (n, n))
        self.assertLess(time.perf_counter() - started, 1.0)

    def test_a_setting_is_read_only_by_the_case_its_row_reads_as(self):
        """``trigger=hold; hold=2`` lost its seconds, ``trigger=near; hold=2`` its hold, and ``when=start`` on a button
        leaving the task was dropped, each with nothing said: a setting any case of the word reads counted as read
        for every row of it. Added: 2026-11-26"""
        from unitytk.sheets import DeliveryIndex

        index = DeliveryIndex(["CABINET", "CABINET|DOOR_GRP", "CABINET|LID_GRP", "CABINET|BOLT_GRP", "TABLE|KEY_GRP"],
                              ["A01_1", "A01_2", "A01_3"], "d.fbx")
        actions = _plan(
            "A01,Open,Open it.,,,use,DOOR_GRP,,A01_1,,,,trigger=hold; hold=2,",
            ",,,,,use,LID_GRP,KEY_GRP,A01_2,,,,trigger=near; hold=2,",
            ",,,,,use,BOLT_GRP,,A01_3,,,,trigger=select; hold=2,",
            ",,,,,button,,,,,Leave,exit,when=start,",
            ",,,,,button,,,,,Again,restart,when=end,",
            ",,,,,button,,,,,Wait,next,when=start,",
            delivery=index,
        ).steps[0].actions
        self.assertEqual([(a.behavior, a.settings, a.warnings) for a in actions], [
            ("use", {"useTrigger": "TimedPress", "pressSeconds": "2"}, []),
            ("place", {}, ["row 2 (Settings): a use row with trigger=near takes no hold, so it does nothing here: "
                           "remove the setting, or leave it as a note in Notes"]),
            ("use", {"useTrigger": "TimedPress", "pressSeconds": "2"}, []),  # select written out reads as left out
            ("exit", {}, ["row 4 (Settings): a button row with To exit takes no when, so it does nothing here: "
                          "remove the setting, or leave it as a note in Notes"]),
            ("goto", {}, []),  # when=end is a button's own default
            ("pause", {"pauseWhen": "Start"}, []),
        ])

    def test_an_arrow_two_nodes_share_is_told_apart_by_the_end_of_its_path(self):
        """``where=ARROW1`` with two ARROW1 nodes bound the first with nothing said. Added: 2026-11-26"""
        from unitytk.sheets import DeliveryIndex

        index = DeliveryIndex(["ROOM", "ROOM|A", "ROOM|A|ARROW1", "ROOM|B", "ROOM|B|ARROW1", "ROOM|DOOR_GRP"], [], "d.fbx")
        shared, told = _plan("A01,Open,Open it.,,,indicate,DOOR_GRP,,,,,,where=ARROW1,",
                             ",,,,,indicate,DOOR_GRP,,,,,,where=b|arrow1,", delivery=index).steps[0].actions
        self.assertEqual((shared.issues, shared.fields.get("arrows")), (
            ["row 1 (Settings): ARROW1 names 2 nodes: write the one meant as the end of its path that tells it apart "
             "(A|ARROW1, B|ARROW1)"], None))
        self.assertEqual((told.issues, told.fields.get("arrows"), told.warnings), ([], ["ROOM|B|ARROW1"], [
            "row 2 (Settings): the delivery spells b|arrow1 as B|ARROW1: write it as the scene does, whose tools tell "
            "names apart by case"]))

    def test_a_cell_that_names_one_node_takes_one(self):
        """With ``KEY_GRP`` and ``LID_GRP`` made the key the tool and dropped the lid with nothing said. Added: 2026-11-26"""
        actions = self._one('A01,Open,Open it.,,,use,DOOR_GRP,"KEY_GRP\nLID_GRP",A02,,,,,',
                            ',,,,,teleport,"CABINET\nDOOR_GRP",,,,,,,')
        self.assertEqual([i for a in actions for i in a.issues], [
            "row 2 (Target): Target takes one name here (the row's facing): keep one, and write each other on a row of its "
            "own",
            "row 1 (With): With takes one name here (the row's tool): keep one, and write each other on a row of its own",
        ])  # the teleport stands at its step's start

    def test_a_members_name_the_delivery_lacks_is_its_cells_issue(self):
        """``play`` with Members ``NOPE_GRP`` read clean where the delivery states no motion, and said "the take moves
        none of this row's NOPE_GRP: re-export it" where it does. Added: 2026-11-26"""
        from unitytk.sheets import DeliveryIndex

        for moved in ({}, {"A01": ("CABINET|LID_GRP",)}):
            with self.subTest(moved=moved):
                index = DeliveryIndex(["CABINET", "CABINET|LID_GRP"], ["A01"], "d.fbx", moved=moved)
                (action,) = _plan("A01,Open,Open it.,,,play,,,A01,NOPE_GRP,,,,", delivery=index).steps[0].actions
                self.assertEqual((action.issues, action.warnings), (
                    ["row 1 (Members): the delivery has no NOPE_GRP: correct the name, or have the artist add it"], []))

    def test_a_word_nothing_builds_and_a_stray_setting_say_their_fix(self):
        """Their messages ended in what happens, not what to do. Added: 2026-11-26"""
        fd, path = tempfile.mkstemp(suffix=".csv")
        with os.fdopen(fd, "w", encoding="utf-8", newline="") as handle:
            handle.write(HEADER + "\nA01,Open,Open it.,,,,,,,,,,,\n,,,,,note,,,,,,,,Say hello.\n")
        try:
            plan = SheetPlan.from_sheet(path, str(FOLDER / "cabinet.fbx"), mapping="interactive",
                                        reading={"words": {"note": []}})
        finally:
            os.remove(path)
        (action,) = plan.steps[0].actions
        self.assertEqual(action.warnings, ["row 1 (Action): nothing here builds a 'note' row, so it is left as written: "
                                           "remove the row, or build it by hand"])

    def test_a_placing_row_that_names_no_helper_takes_its_own_where_the_delivery_has_it(self):
        # One name per row, derived from what it says (ptk.ManifestPlacement.helper_name), so the camera the Shot
        # Manifest made in the DCC for a picture that names none is the one Unity takes the picture from.
        from unitytk.sheets._cell_reading import _CellReading

        class Delivery:
            def named(self, name):
                return {"CAM_A01_DOOR": ["ROOT|CABINET|DOOR|CAM_A01_DOOR"], "CAM_A01_LID": ["A|CAM_A01_LID", "B|CAM_A01_LID"]}.get(name, [])

        reader = _CellReading.__new__(_CellReading)
        reader.index = Delivery()

        def own(row):
            return reader._own_helper(row, reader._helpers(row, "A01", [row]))

        row = {"word": "pip", "places": "view", "assets": ["CABINET|DOOR"], "settings": {"until": "A02"}}
        self.assertEqual(own(row)["settings"], {"until": "A02", "where": "CAM_A01_DOOR"}, "read as if written")
        self.assertNotIn("where", own(dict(row, assets=["CABINET|HINGE"]))["settings"], "none delivered: Unity makes its own")
        self.assertNotIn("where", own(dict(row, assets=["LID"]))["settings"], "two delivered: none is guessed")
        named = dict(row, settings={"where": "CAM_WIDE"})
        self.assertEqual(own(named)["settings"]["where"], "CAM_WIDE", "a row's own `where` is never replaced")
        self.assertNotIn("where", own(dict(row, places=""))["settings"], "a word that places nothing")

    def test_two_targets_of_a_step_sharing_a_name_get_a_helper_each(self):
        """``pip PANEL|LATCH`` and ``pip DOOR|LATCH`` in one step both named their camera ``CAM_A01_LATCH``, one
        object for two pictures, where the DCC's Shot Manifest names each by the end of its path that tells it apart
        (``ptk.ManifestPlacement.helper_names``). Added: 2026-11-26"""
        from unitytk.sheets import DeliveryIndex

        index = DeliveryIndex(["ROOT", "ROOT|PANEL", "ROOT|PANEL|LATCH", "ROOT|DOOR", "ROOT|DOOR|LATCH",
                               "ROOT|DOOR|LATCH|CAM_A01_DOOR_LATCH"], [], "d.fbx")
        panel, door = _plan("A01,Look,Look.,,,pip,PANEL|LATCH,,,,,,,", ",,,,,pip,DOOR|LATCH,,,,,,,",
                            delivery=index).steps[0].actions
        self.assertEqual((panel.fields["helpers"], panel.fields.get("from")), (["CAM_A01_PANEL_LATCH"], None),
                         "none delivered: Unity makes its own, under the DCC's name")
        self.assertEqual((door.fields["helpers"], door.fields.get("from")),
                         (["CAM_A01_DOOR_LATCH"], "ROOT|DOOR|LATCH|CAM_A01_DOOR_LATCH"), "the DCC's camera, taken")
        self.assertEqual([i for a in (panel, door) for i in a.issues + a.warnings], [])

    def test_a_member_row_builds_nothing_a_list_is_one_field_and_a_default_written_out_is_no_stray_setting(self):
        actions = self._one(
            "A01,Open,Open it.,,,fade_out,BOLT_GRP,,,,,,,",
            ",,,,,,LID_GRP,,,,,,,",
            ',,,,,checklist,,,,,"Lights\nPower",,,',
            ",,,,,teleport,CABINET,,,,,,where=VIEW_FRONT; when=start,",
        )
        self.assertEqual([a.behavior for a in actions], ["teleport", "fade_out", "checklist"])
        self.assertEqual(actions[2].fields, {"label": "Lights, Power"})
        self.assertEqual([i for a in actions for i in a.issues + a.warnings], [])

    def test_a_teleport_stands_on_the_helper_it_names_and_a_spot_is_placed_only_where_it_names_none(self):
        named, unnamed = self._one(
            "A01,Go,Step up.,,,teleport,CABINET,,,,,,where=VIEW_FRONT,",
            ",,,,,teleport,CABINET,,,,,,when=end,",
        )
        # A helper named: the user stands on it, facing its forward; what it is for is not handed on.
        self.assertEqual(([t.split("|")[-1] for t in named.targets], named.fields), (["VIEW_FRONT"], {"faces": "CABINET"}))  # stood on as it stands; what the row says they face is checked against it
        # None named: the build places one facing the part, once, named as every tool names the row's own helper.
        self.assertEqual((unnamed.targets, unnamed.fields["facing"].split("|")[-1], unnamed.fields["helpers"]),
                         ([], "CABINET", ["VIEW_A01_CABINET"]))
        self.assertNotIn("helpers", named.fields, "a row that names its helper has no other")

    def test_the_arrow_name_rule_is_a_sentence_sheets_alone(self):
        from unitytk.sheets import DeliveryIndex, SheetCheck

        index = DeliveryIndex(["CABINET", "CABINET|DOOR_GRP", "ARROWS", "ARROWS|LID_ARROW"], [], "d.fbx")
        fd, path = tempfile.mkstemp(suffix=".csv")
        with os.fdopen(fd, "w", encoding="utf-8", newline="") as handle:
            handle.write(HEADER + "\nA01,Open,Open it.,,,,,,,,,,,\n,,,,,highlight,DOOR_GRP,,,,,,,\n")
        try:
            check = SheetCheck.run(path, delivery=index)
        finally:
            os.remove(path)
        # LID_ARROW names a part the delivery lacks: a sentence sheet's rule. In cells a row names its arrow.
        self.assertEqual([item.line() for item in check.items], [])

    def test_a_linear_sheets_member_row_is_no_action(self):
        plan = _plan("A01,Open,The bolt comes out.,,,fade_out,BOLT_GRP,,,,,,,", ",,,,,,LID_GRP,,,,,,,", mapping="linear")
        actions = plan.steps[0].actions
        self.assertEqual([(a.behavior, a.issues, a.warnings) for a in actions], [("fade_out", [], [])])

    def test_a_sheet_of_sentences_reads_as_it_did(self):
        again = SheetPlan.from_sheet(str(FOLDER / "cabinet.csv"), str(FOLDER / "cabinet.fbx"), mapping="interactive")
        committed = SheetPlan.load(FOLDER / "cabinet.plan.json")
        self.assertEqual([[_shape(a) for a in s.actions] for s in again.steps],
                         [[_shape(a) for a in s.actions] for s in committed.steps])


if __name__ == "__main__":
    unittest.main()
