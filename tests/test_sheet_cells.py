# !/usr/bin/python
# coding=utf-8
"""Tests for a sheet in the cell format read into a plan (``unitytk/sheets/_cell_reading.py``).

A row with an Action is read by its cells as its word, never by a phrase: the plan's behavior is
the word, its fields and settings the row's cells and settings by their own names. The oracle is
the Tasks sample: its task in cells (the shipped sheet) reads to the plan its older sheet of
sentences (kept here) reads to, and that sheet of sentences converted to cells reads to the plan it
read to, so a sheet of either kind builds the same."""

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

#: The Tasks sample's task, written in cells (as its recipe writes the shipped sheet): each name a node's own.
TASK = (
    "INTRO",
    "IN_01,Welcome,Welcome to the cabinet service task.,,,note,,,,,,,,The cabinet stands in front of you with its tools on the table.",
    "IN_02,Begin,Select 'Begin' to start.,,,button,,,,,Begin,next,,",
    "SECTION A: Open the cabinet",
    "A01,Unlock the latches,Unlock both latches with the key.,,,travel,CABINET,,,,,,where=VIEW_FRONT,",
    ",,,,,highlight,CABINET_LATCHES_GRP,,,,,,,",
    ",,,,,indicate,KEY_GRP,,,,,,,",
    ",,,,,take,KEY_GRP,,,,,,,",
    ",,,,,use,LATCH_UPPER_GRP,KEY_GRP,A01_1,LATCH_UPPER_GRP,,,,",
    ",,,,,use,LATCH_LOWER_GRP,KEY_GRP,A01_2,LATCH_LOWER_GRP,,,,",
    ",,,,,return,KEY_GRP,,,,,,,",
    "A02,Open the door,Now open the door.,Select the door to open it,,highlight,DOOR_GRP,,,,,,,",
    ",,,,,use,DOOR_GRP,,A02,,,,,",
    "SECTION B: Replace the bolt",
    "B01,Remove the bolt,Remove the bolt with the wrench.,,,travel,CABINET,,,,,,where=VIEW_BACK,",
    ",,,,,highlight,BOLT_GRP,,,,,,,",
    ",,,,,indicate,WRENCH_GRP,,,,,,,",
    ",,,,,take,WRENCH_GRP,,,,,,,",
    ",,,,,use,BOLT_GRP,WRENCH_GRP,B01_1,BOLT_GRP,,,,",
    ",,,,,play,,,B01_2,LID_GRP,,,after=B01_1,",
    ",,,,,hide,BOLT_GRP,,,,,,,",
    ",,,,,return,WRENCH_GRP,,,,,,,",
    "B02,Test the lamp,Press the button.,,,select,BUTTON_GRP,,,,,,,",
    "B03,Did it light?,Did the lamp light?,Did the lamp light?,,button,,,,,YES,B04,,",
    ",,,,,button,,,,,NO,B03A,,",
    "B03A,Wrong answer,That is not right. Try again.,,,button,,,,,Try Again,B03,,",
    "B04,Done,Well done.,,,button,,,,,Exit,exit,,",
)

#: The same task in the older sheet of sentences (the sample before it was written in cells).
SENTENCES = """Step,Step Name,Step Contents,Asset Names,Action,Animation Clip Names,Text On Placard,Hint,Voice Support,Audio Files
INTRO,,,,,,,,,
IN_01,Welcome,The cabinet stands in front of you with its tools on the table.,,note,,,,Welcome to the cabinet service task.,
,,Auto Advance,,,,,,,
IN_02,Begin,Selecting 'Begin' triggers step advancement,,,,,,Select 'Begin' to start.,
SECTION A: Open the cabinet,,,,,,,,,
A01,Unlock the latches,Highlight the Cabinet Latches,CABINET_LATCHES,,,,-Bouncing arrow pointing at the key on the table,Unlock both latches with the key.,
,,The user is teleported to the front view,VIEW_FRONT,,,,,,
,,Selecting the key places it in hand,KEY,,,,,,
,,Selecting the top latch triggers Top Latch Open Animation and returns the key to hand,"LATCH_UPPER
KEY",,A01_1,,,,
,,Selecting the bottom latch triggers Bottom Latch Open Animation and returns the key to hand,"LATCH_LOWER
KEY",,A01_2,,,,
,,Return the key to the table,KEY,,,,,,
,,Auto Advance when both latches are open,,,,,,,
A02,Open the door,Highlight the door,DOOR,,,Select the door to open it,,Now open the door.,
,,Selecting the door triggers Door Open Animation,DOOR,,A02,,,,
,,Auto Advance,,,,,,,
SECTION B: Replace the bolt,,,,,,,,,
B01,Remove the bolt,Highlight the bolt,BOLT,,,,-Bouncing arrow pointing at the wrench on the table,Remove the bolt with the wrench.,
,,The user is teleported to the back view,VIEW_BACK,,,,,,
,,Selecting the wrench places it in hand,WRENCH,,,,,,
,,Selecting the bolt with wrench in hand triggers Bolt Removal Animation,"BOLT
WRENCH",,B01_1,,,,
,,Trigger Lid Open Animation,LID,,B01_2,,,,
,,The bolt fades away,BOLT,,,,,,
,,Return the wrench to the table,WRENCH,,,,,,
,,Auto Advance,,,,,,,
B02,Test the lamp,Selecting the button triggers step advancement,BUTTON,,,,,Press the button.,
B03,Did it light?,YES and NO buttons appear on the placard screen,,,,Did the lamp light?,,Did the lamp light?,
,,Selecting 'YES' triggers step advancement to step B04,,,,,,,
,,Selecting 'NO' triggers step advancement to step B03.5,,,,,,,
B03.5,Wrong answer,Selecting 'Try Again' triggers step advancement to step B03,,,,,,That is not right. Try again.,
B04,Done,Selecting 'Exit' ends the task,,,,,,Well done.,
"""

#: A sheet of sentences of every row the format needs no row for, each folded into the row or step it belongs to.
FOLDS = '''Step,Step Name,Step Contents,Asset Names,Action,Animation Clip Names,Text On Placard,Hint,Voice Support,Audio Files
A01,Look,PiP screen shows the bolt,BOLT,,,,"Placard text reads ""Look at the bolt""",Look.,
,,"A checklist appears on the placard: Door, Lid",,,,,,,
,,The button turns on,BUTTON,,,,,,
,,The destination marker fades in,,,,,,,
,,Selecting 'Next' triggers step advancement,,,,,,,
,,The user is teleported to the back view,VIEW_BACK,,,,,,
A02,Press,PiP screen fades away,,,,,,Press.,
,,Pressing and holding the button plays Button Press Animation,BUTTON,,A02,,,,
,,The button turns amber,BUTTON,,,,,,
,,The destination marker fades away,,,,,,,
,,Checklist item 'Door' gets a check mark,,,,,,,
,,The wrench returns to hand,WRENCH,,,,,,
A03,Fit,Selecting the wrench places it in hand,WRENCH,,,Did it fit?,,Fit.,
,,Moving the wrench close to the bolt triggers Bolt Fit Animation,"WRENCH
BOLT",,A03,,,,
,,Checklist item 'Lid' gets a check mark,,,,,,,
,,The checklist fades away,,,,,,,
,,YES and NO buttons appear on the placard screen,,,,,,,
,,Selecting 'YES' triggers step A04,,,,,,,
,,Selecting 'NO' triggers step A01,,,,,,,
A04,Code,A number pad appears on the placard,,,,Enter the code.,,,
,,Entering '11' triggers step A05,,,,,,,
,,Entering any other number triggers step A04,,,,,,,
A05,Quiz,Selecting 'Take Quiz' triggers advancement to the quiz,,,,Ready?,,,
QUIZ,,,,,,,,,
Q01,Which?,To free the key,,,,Why?,,,
,,* To reach the door,,,,,,,
OUTRO,,,,,,,,,
E01,Done,Selecting 'Exit' ends the task,,,,Done.,,,
'''
#: The fold fixture's delivery.
FOLD_NODES = [("CABINET", "Null"), ("CABINET|BOLT_GRP", "Null"), ("CABINET|BUTTON_GRP", "Null"), ("TABLE", "Null"),
              ("TABLE|WRENCH_GRP", "Null"), ("VIEW_BACK", "Null")]


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


def _shape(action, without=(), ids=None):
    """An action as what a builder is handed: no text, and none of the fields one kind of sheet alone gives (a
    sentence's phrase; a cell row's ``helpers``, its own helpers' names derived from its Target cell, which a sentence
    sheet has none of: converted to cells, it gets them), nor any field or setting of *without*; its jump through
    *ids* (``{a step's ID: as another sheet spells it}``)."""
    fields = {k: v for k, v in action.fields.items() if k not in ("phrase", "helpers") + tuple(without)}
    settings = {k: v for k, v in action.settings.items() if k not in without}
    to = (ids or {}).get(action.to, action.to)
    return (action.behavior, tuple(action.targets), action.clip, to,
            tuple(sorted((k, str(v)) for k, v in fields.items())), tuple(sorted(settings.items())))


def _rows(plan, step, ids=None, without=()):
    """*step*'s actions of *plan* as builders are handed them, a sheet of sentences' ``advance`` aside (what a step
    that offers no choice does anyway, which a cell sheet writes no row for)."""
    return Counter(_shape(a, without, ids) for a in step.actions if a.behavior != "advance")


class SampleInCellsTestCase(UnityTkTestCase):
    def _sentences(self):
        path = os.path.join(self.create_temp_dir(), "sentences.csv")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(SENTENCES)
        return SheetPlan.from_sheet(path, str(FOLDER / "cabinet.fbx"), mapping="interactive")

    def test_the_task_in_cells_reads_to_the_plan_its_sentences_read_to(self):
        cells, sentences = _plan(*TASK), self._sentences()
        for plan in (cells, sentences):
            self.assertEqual(plan.issues, [])
            self.assertEqual([a.issues + a.warnings for s in plan.steps for a in s.actions if a.issues or a.warnings], [])
        ids = {"B03_5": "B03A"}
        self.assertEqual([s.id for s in cells.steps], [ids.get(s.id, s.id) for s in sentences.steps])
        for mine, theirs in zip(cells.steps, sentences.steps):
            self.assertEqual(mine.fields.get("voice"), theirs.fields.get("voice"), mine.id)
            self.assertEqual(mine.fields.get("placard"), theirs.fields.get("placard"), mine.id)
            # What the cells say that the sentences do not: what a travel faces, the take a play follows; the page's
            # buttons the sentences said are the step's choices, a note.
            cell_rows = Counter(
                _shape(a, ("after",)) if a.behavior != "travel" else _shape(a)[:1] + ((),) + _shape(a)[2:]
                for a in mine.actions
            )
            said = _rows(sentences, theirs, ids, ("after",))
            said -= Counter(s for s in said if s[0] == "note" and theirs.id == "B03")
            self.assertEqual(cell_rows, said, mine.id)
        self.assertEqual(sorted(a.clip for s in cells.steps for a in s.actions if a.clip),
                         ["A01_1", "A01_2", "A02", "B01_1", "B01_2"])

    def test_a_travel_stands_at_its_steps_start_and_a_chained_take_after_the_one_it_follows(self):
        plan = _plan(
            "B01,Remove,Remove the bolt.,,,play,,,B01_2,LID_GRP,,,after=B01_1,",
            ",,,,,button,,,,,Next,next,,",
            ",,,,,use,BOLT_GRP,WRENCH_GRP,B01_1,,,,,",
            ",,,,,travel,CABINET,,,,,,where=VIEW_BACK,",
            ",,,,,travel,CABINET,,,,,,where=VIEW_FRONT; when=end,",
        )
        self.assertEqual([(a.behavior, a.clip or (a.fields["where"][0].split("|")[-1] if a.fields.get("where") else ""))
                          for a in plan.steps[0].actions],
                         [("travel", "VIEW_BACK"), ("button", ""), ("use", "B01_1"), ("play", "B01_2"),
                          ("travel", "VIEW_FRONT")])


class ConvertedToCellsTestCase(UnityTkTestCase):
    """``sheet.convert``: a sheet of sentences rewritten in cells reads to the plan it read to."""

    def _converted(self, sheet: str, delivery: str):
        from unitytk.sheets import SheetConverter

        out = os.path.join(self.create_temp_dir(), "cells.csv")
        result = SheetConverter.convert(sheet, delivery, out, mapping="interactive")
        with open(out, encoding="utf-8", newline="") as handle:
            return result, list(csv.reader(handle)), out

    def _sheet(self, text: str) -> str:
        path = os.path.join(self.create_temp_dir(), "sentences.csv")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write(text)
        return path

    def _same(self, sentences, cells, ids):
        """Each step of the *cells* plan holds what the *sentences* plan's does, its jumps respelled (*ids*)."""
        self.assertEqual(cells.issues, [])
        self.assertEqual([i for s in cells.steps for a in s.actions for i in a.issues + a.warnings], [])
        for mine, theirs in zip(cells.steps, sentences.steps):
            self.assertEqual(mine.id, ids.get(theirs.id, theirs.id))
            self.assertEqual((mine.fields.get("voice"), mine.fields.get("placard")),
                             (theirs.fields.get("voice"), theirs.fields.get("placard")), mine.id)
            self.assertEqual(_rows(cells, mine), _rows(sentences, theirs, ids), mine.id)

    def test_the_sample_task_converted_reads_to_the_plan_it_read_to(self):
        sheet = self._sheet(SENTENCES)
        result, rows, out = self._converted(sheet, str(FOLDER / "cabinet.fbx"))
        self.assertEqual(result["left"], [], "nothing for a person to decide")
        self.assertEqual(rows[0], HEADER.split(","))
        self.assertEqual([r for r in rows[1:] if r[0] and any(r[5:11])], [], "a step's row holds no action")
        # A step the old format numbered with a dot is spelled as every tool spells it, and its jumps with it.
        self.assertIn("B03A", [r[0] for r in rows])
        self.assertIn(["NO", "B03A"], [[r[10], r[11]] for r in rows])
        # The page's buttons the sentences named are the step's choices: a note, its words kept.
        self.assertIn(["note", "YES and NO buttons appear on the placard screen"], [[r[5], r[13]] for r in rows])
        sentences = SheetPlan.from_sheet(sheet, str(FOLDER / "cabinet.fbx"), mapping="interactive")
        cells = SheetPlan.from_sheet(out, str(FOLDER / "cabinet.fbx"), mapping="interactive")
        self._same(sentences, cells, {"B03_5": "B03A"})

    def test_every_fold_reads_to_the_plan_its_cells_read_to(self):
        """The rows a sheet of sentences needed that the format does not fold into the rows they belong to, each kept as
        a note: a picture or a list taken away is its row's ``until``, a ring shown and hidden its travel's ``ring``, a
        travel after a choice that ends its step happens as it ends, a page's text is its step's, its buttons the
        step's choices, a number pad every answer's, a tool back in hand its row's With, the quiz's button goes to the
        first question, a question's options a right and a wrong answer. Converted, the cells read to the same plan."""
        from test_sheet_plan import write_delivery

        sheet = self._sheet(FOLDS)
        fbx = write_delivery(os.path.join(self.create_temp_dir(), "folds.fbx"), nodes=FOLD_NODES, takes=["A02", "A03"])
        sentences = SheetPlan.from_sheet(sheet, fbx, mapping="interactive")
        self.assertEqual([i for s in sentences.steps for a in s.actions for i in a.issues + a.warnings], [])
        acts = {s.id: s.actions for s in sentences.steps}
        said = {s.id: [(a.behavior, a.fields.get("label") or a.to or "", a.settings) for a in s.actions]
                for s in sentences.steps}
        self.assertEqual(said["A01"], [
            ("picture", "The bolt", {"until": "A02"}), ("list", "Door, Lid", {"until": "A03"}),
            ("highlight", "", {"style": "steady"}), ("note", "", {}), ("button", "Next", {}),
            ("travel", "", {"ring": "A02", "when": "end"}), ("note", "", {}),
        ])
        self.assertEqual(sentences.steps[0].fields["placard"], "Look at the bolt", "the hint's page text")
        self.assertEqual([a.text for a in acts["A01"] if a.behavior == "note"],
                         ["The destination marker fades in", 'Placard text reads "Look at the bolt"'])
        self.assertEqual(said["A02"][1:3], [("use", "", {"trigger": "hold"}), ("set", "", {"state": "amber"})])
        self.assertEqual([b for b, _, _ in said["A02"]], ["note", "use", "set", "note", "tick", "note"])
        self.assertEqual(said["A03"][1], ("use", "", {"trigger": "near"}))
        self.assertEqual([(a.fields.get("label"), a.to) for a in acts["A03"] if a.behavior == "button"],
                         [("YES", "A04"), ("NO", "restart")], "a jump to the first step starts over")
        self.assertEqual([(a.fields.get("label"), a.to) for a in acts["A04"] if a.behavior == "answer"],
                         [("11", "A05"), ("*", "A04")])
        self.assertEqual([(a.behavior, a.to) for a in acts["A05"]], [("button", "Q01")], "the quiz: its first question")
        self.assertEqual([(a.fields.get("label"), a.to) for a in acts["Q01"]],
                         [("To free the key", "retry"), ("To reach the door", "next")])
        result, _, out = self._converted(sheet, fbx)
        self.assertEqual(result["left"], [])
        self._same(sentences, SheetPlan.from_sheet(out, fbx, mapping="interactive"), {})

    def test_a_folded_row_names_what_it_could_not_fold_and_what_is_undecided_is_left_by_row(self):
        sheet = self._sheet(
            "Step,Step Name,Step Contents,Asset Names,Action,Animation Clip Names,Text On Placard,Hint,Voice Support,Audio Files\n"
            "A01,Look,PiP screen shows the door,DOOR,,,,,Look at the door.,\n"
            ",,The button turns on,BUTTON,,,,,,\n"
            ",,Wave at the cabinet,,,,,,,\n"
            ",,YES and MAYBE buttons appear on the placard screen,,,,,,,\n"
            ",,Selecting 'YES' triggers step advancement,,,,,,,\n"
            "A02,Go on,PiP screen fades away,,,,,,Good.,\n"
            ",,The checklist fades away,,,,,,,\n"
        )
        result, rows, _ = self._converted(sheet, str(FOLDER / "cabinet.fbx"))
        by_action = {r[5]: r for r in rows[1:] if r[5]}
        self.assertEqual((by_action["picture"][10], by_action["picture"][12]), ("The door", "until=A02"),
                         "the picture's own row says its caption and until when")
        self.assertEqual([r[5] for r in rows[1:] if r[5]], ["picture", "highlight", "note", "button", "note", "note"])
        # A row no phrase reads keeps its words, with no word of its own, and is left for a person.
        self.assertIn("Wave at the cabinet", [r[13] for r in rows[1:] if not r[5] and not r[0]])
        self.assertEqual([(i["step"], i["why"].split(": ", 1)[-1]) for i in result["left"]], [
            ("A01", "no phrase reads it and no Action names it: write it as a phrase, or fill Action"),
            ("A01", "the page's 'MAYBE' button: no row of the step says what it does: write the row it is, or take it "
                    "from this row"),
        ])
        plan = SheetPlan.from_sheet(sheet, str(FOLDER / "cabinet.fbx"), mapping="interactive")
        (buttons,) = [a for a in plan.steps[0].actions if a.text.startswith("YES and MAYBE")]
        self.assertEqual(buttons.warnings, ["row 4 (Step Contents): 'YES and MAYBE buttons appear on the placard screen' "
                                            "is shown on the step's Text On Placard, which is empty: write its text in "
                                            "Text On Placard"])
        (gone,) = [a for a in plan.steps[1].actions if a.text.startswith("The checklist")]
        self.assertEqual(gone.warnings, ["row 2 (Step Contents): nothing an earlier row showed is still shown for it to "
                                         "take away: remove the row"])


class WordsTestCase(UnityTkTestCase):
    def _one(self, *rows):
        plan = _plan(*rows)
        return plan.steps[0].actions

    def test_a_button_goes_where_its_to_says(self):
        actions = self._one(
            "A01,Ask,Ready?,,,button,,,,,Go,next,,",
            ",,,,,button,,,,,Leave,exit,,",
            ",,,,,button,,,,,Again,restart,,",
            ",,,,,button,,,,,Wait,next,when=start,",
        )
        self.assertEqual([(a.behavior, a.fields.get("label"), a.to, a.settings) for a in actions],
                         [("button", "Go", "next", {}), ("button", "Leave", "exit", {}),
                          ("button", "Again", "restart", {}), ("button", "Wait", "next", {"when": "start"})])

    def test_a_question_is_a_step_with_a_wrong_button(self):
        actions = self._one(
            "Q01,Question,Which tool?,Which tool?,,button,,,,,Wrench,next,,",
            ",,,,,button,,,,,Key,retry,reply=Not that one.,",
        )
        self.assertEqual([(a.behavior, a.fields, a.to, a.settings) for a in actions],
                         [("button", {"label": "Wrench"}, "next", {}),
                          ("button", {"label": "Key"}, "retry", {"reply": "Not that one."})])

    def test_an_answer_is_its_value_and_any_other_is_its_star(self):
        actions = self._one(
            "C10,Dial,Enter the number.,,,answer,,,,,11,C11,,",
            ",,,,,answer,,,,,*,C10,,",
            "C11,Right,Correct.,,,note,,,,,,,,",
        )
        self.assertEqual([(a.behavior, a.fields.get("label"), a.to, a.settings) for a in actions],
                         [("answer", "11", "C11", {}), ("answer", "*", "C10", {})])

    def test_a_rows_settings_are_its_own_and_a_span_rides_on_the_row_that_shows(self):
        actions = self._one(
            "A01,Open,Open it.,,,use,BUTTON_GRP,,A02,,,,trigger=hold,",
            ",,,,,use,DOOR_GRP,KEY_GRP,A01_1,,,,trigger=near,",
            ",,,,,use,LID_GRP,,A01_2,,,,trigger=hold; hold=2,",
            ",,,,,travel,CABINET,,,,,,where=VIEW_FRONT; ring=step,",
            ",,,,,picture,BOLT_GRP,,,,,,until=A02,",
            ",,,,,set,BUTTON_GRP,,,,,,state=amber,",
            "A02,Next,Go on.,,,note,,,,,,,,",
        )
        got = [(a.behavior, a.settings, {k: v.split("|")[-1] for k, v in a.fields.items() if k not in ("helpers", "where")})
               for a in actions]
        # A travel stands at its step's start; its ring is its own span: here until its step ends.
        self.assertEqual(got[0], ("travel", {"ring": "step"}, {}))
        self.assertEqual(([t.split("|")[-1] for t in actions[0].targets], [w.split("|")[-1] for w in actions[0].fields["where"]]),
                         (["CABINET"], ["VIEW_FRONT"]), "it faces its Target, from where its row says")
        self.assertEqual(got[1], ("use", {"trigger": "hold"}, {}))
        self.assertEqual(got[2], ("use", {"trigger": "near"}, {"with": "KEY_GRP"}))
        self.assertEqual(got[3], ("use", {"trigger": "hold", "hold": "2"}, {}))
        # A picture is captioned by its Label alone (a row's words only name its object): with none, no caption.
        self.assertEqual(got[4:6], [("picture", {"until": "A02"}, {}), ("set", {"state": "amber"}, {})])
        self.assertEqual(actions[4].fields["helpers"], ["CAM_A01_BOLT_GRP"], "a picture naming no camera: its own, as every tool names it")
        self.assertEqual(len(actions), 6, "no row takes a shown thing away")
        self.assertEqual([i for a in actions for i in a.issues + a.warnings if "has no" in i or "takes no" in i], [])
        later = _plan(
            "A01,Open,Open it.,,,picture,BOLT_GRP,,,,,,until=A02,",
            "A02,Next,Go on.,,,highlight,DOOR_GRP,,,,,,,",
        ).steps[1].actions
        self.assertEqual([(a.behavior, a.settings) for a in later], [("highlight", {})])

    def test_a_list_goes_at_the_step_its_span_names(self):
        # A real task's checklist stayed on the placard to the end, over the quiz's buttons: every shown thing has a span.
        from unitytk.sheets.clause_book import ClauseBook

        plan = _plan(
            'A01,Open,Open it.,,,list,,,,,"Door\nLid",,until=A02.5,',
            "A02.5,Next,Go on.,,,highlight,DOOR_GRP,,,,,,,",
        )
        self.assertEqual([(a.behavior, a.fields, a.settings, a.warnings) for a in plan.steps[0].actions],
                         [("list", {"label": "Door, Lid"}, {"until": "A02_5"}, [])], "a span names a step as the reader spells it")
        self.assertEqual([a.behavior for a in plan.steps[1].actions], ["highlight"])
        book = ClauseBook()
        self.assertEqual(book.roles("list"), {"lists"})
        self.assertEqual(book.roles("answer", fields={"label": "*"}), {"advances", "entered", "otherwise"},
                         "a role given with a field belongs to the rows carrying it")
        self.assertEqual(book.roles("answer", fields={"label": "11"}), {"advances", "entered"})

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
        placed = [i for a in self._one("A01,Open,Open it.,,,travel,DOOR_GRP,,,,,,where=VIEW_GONE,") for i in a.issues]
        self.assertTrue(any(i.startswith("row 1 (Settings): the delivery has no VIEW_GONE") for i in placed), placed)
        # A setting is handed on by the sheet's own name for it.
        self.assertEqual((actions[2].settings, actions[2].warnings), ({"style": "steady"}, []))
        # A picture's own camera, named where it is taken from, is its `where`.
        (shown,) = self._one("A01,Open,Open it.,,,picture,DOOR_GRP,,,,,,where=VIEW_FRONT,")
        self.assertEqual(([w.split("|")[-1] for w in shown.fields.get("where", [])], shown.warnings, shown.issues),
                         (["VIEW_FRONT"], [], []))

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

    def test_a_setting_that_does_nothing_beside_its_rows_cells_is_said(self):
        """``trigger=near; hold=2`` lost its hold, and ``when=start`` on a button leaving the task was dropped, each with
        nothing said. A setting the reading says does something only beside certain cells (``applies``) is still handed
        on where it does nothing, and said. Added: 2026-11-26"""
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
        unread = "so it does nothing here: remove the setting, or leave it as a note in Notes"
        self.assertEqual([(a.settings, a.warnings) for a in actions], [
            ({"trigger": "hold", "hold": "2"}, []),
            ({"trigger": "near", "hold": "2"}, ["row 2 (Settings): a use row with trigger=near takes no hold, " + unread]),
            ({"trigger": "select", "hold": "2"}, ["row 3 (Settings): a use row with trigger=select takes no hold, " + unread]),
            ({"when": "start"}, ["row 4 (Settings): a button row with To exit takes no when, " + unread]),
            ({"when": "end"}, []),  # a button's own default
            ({"when": "start"}, []),
        ])
        answered = _plan("Q01,Ask,Which?,,,button,,,,,Two,next,when=start,", ",,,,,button,,,,,Three,retry,,",
                         delivery=index).steps[0].actions[0]
        self.assertEqual(answered.warnings, ["row 1 (Settings): a button row with To next beside one with To retry takes "
                                             "no when, " + unread], "a question's answer is no pause")

    def test_a_pointer_two_nodes_share_is_told_apart_by_the_end_of_its_path(self):
        """``where=POINT1`` with two POINT1 nodes bound the first with nothing said. Added: 2026-11-26"""
        from unitytk.sheets import DeliveryIndex

        index = DeliveryIndex(["ROOM", "ROOM|A", "ROOM|A|POINT1", "ROOM|B", "ROOM|B|POINT1", "ROOM|DOOR_GRP"], [], "d.fbx")
        shared, told = _plan("A01,Open,Open it.,,,indicate,DOOR_GRP,,,,,,where=POINT1,",
                             ",,,,,indicate,DOOR_GRP,,,,,,where=b|point1,", delivery=index).steps[0].actions
        self.assertEqual((shared.issues, shared.fields.get("where")), (
            ["row 1 (Settings): POINT1 names 2 nodes: write the one meant as the end of its path that tells it apart "
             "(A|POINT1, B|POINT1)"], None))
        self.assertEqual((told.issues, told.fields.get("where"), told.warnings), ([], ["ROOM|B|POINT1"], [
            "row 2 (Settings): the delivery spells b|point1 as B|POINT1: write it as the scene does, whose tools tell "
            "names apart by case"]))

    def test_with_names_one_node(self):
        """With ``KEY_GRP`` and ``LID_GRP`` made the key the tool and dropped the lid with nothing said. Added: 2026-11-26"""
        actions = self._one('A01,Open,Open it.,,,use,DOOR_GRP,"KEY_GRP\nLID_GRP",A02,,,,,')
        self.assertEqual([i for a in actions for i in a.issues],
                         ["row 1 (With): With takes one name: keep one, and write each other on a row of its own"])

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

    def test_a_placing_row_that_names_no_helper_takes_its_own_where_the_delivery_has_it(self):
        # One name per target, derived from what the row says (ptk.ManifestPlacement.helper_name), so the camera the
        # Shot Manifest made in the DCC for a picture that names none is the one Unity takes the picture from.
        from unitytk.sheets._cell_reading import _CellReading

        class Delivery:
            def named(self, name):
                return {"CAM_A01_DOOR": ["ROOT|CABINET|DOOR|CAM_A01_DOOR"],
                        "CAM_A01_LID": ["A|CAM_A01_LID", "B|CAM_A01_LID"]}.get(name, [])

        reader = _CellReading.__new__(_CellReading)
        reader.index = Delivery()

        def own(row):
            return reader._where(row, reader._helpers(row, "A01", [row]), {})[0]

        row = {"word": "picture", "places": "view", "assets": ["CABINET|DOOR"], "settings": {"until": "A02"}}
        self.assertEqual(own(row), ["ROOT|CABINET|DOOR|CAM_A01_DOOR"], "read as if written")
        self.assertEqual(own(dict(row, assets=["CABINET|HINGE"])), [], "none delivered: Unity makes its own")
        self.assertEqual(own(dict(row, assets=["LID"])), [], "two delivered: none is guessed")
        self.assertEqual(reader._helpers(dict(row, settings={"where": "CAM_WIDE"}), "A01"), [],
                         "a row's own `where` is never replaced")
        self.assertEqual(reader._helpers(dict(row, places=""), "A01"), [], "a word that places nothing")
        two = dict(row, assets=["CABINET|DOOR", "CABINET|HINGE"])
        self.assertEqual(own(two), ["ROOT|CABINET|DOOR|CAM_A01_DOOR", ""], "one per target, in order")

    def test_two_targets_of_a_step_sharing_a_name_get_a_helper_each(self):
        """``picture PANEL|LATCH`` and ``picture DOOR|LATCH`` in one step both named their camera ``CAM_A01_LATCH``, one
        object for two pictures, where the DCC's Shot Manifest names each by the end of its path that tells it apart
        (``ptk.ManifestPlacement.helper_names``). Added: 2026-11-26"""
        from unitytk.sheets import DeliveryIndex

        index = DeliveryIndex(["ROOT", "ROOT|PANEL", "ROOT|PANEL|LATCH", "ROOT|DOOR", "ROOT|DOOR|LATCH",
                               "ROOT|DOOR|LATCH|CAM_A01_DOOR_LATCH"], [], "d.fbx")
        panel, door = _plan("A01,Look,Look.,,,picture,PANEL|LATCH,,,,,,,", ",,,,,picture,DOOR|LATCH,,,,,,,",
                            delivery=index).steps[0].actions
        self.assertEqual((panel.fields["helpers"], panel.fields.get("where")), (["CAM_A01_PANEL_LATCH"], None),
                         "none delivered: Unity makes its own, under the DCC's name")
        self.assertEqual((door.fields["helpers"], door.fields.get("where")),
                         (["CAM_A01_DOOR_LATCH"], ["ROOT|DOOR|LATCH|CAM_A01_DOOR_LATCH"]), "the DCC's camera, taken")
        self.assertEqual([i for a in (panel, door) for i in a.issues + a.warnings], [])

    def test_a_member_row_builds_nothing_a_list_is_one_field_and_a_setting_is_carried_as_written(self):
        actions = self._one(
            "A01,Open,Open it.,,,hide,BOLT_GRP,,,,,,,",
            ",,,,,,LID_GRP,,,,,,,",
            ',,,,,list,,,,,"Lights\nPower",,,',
            ",,,,,travel,CABINET,,,,,,where=VIEW_FRONT; when=start,",
        )
        self.assertEqual([a.behavior for a in actions], ["travel", "hide", "list"])
        self.assertEqual(actions[2].fields, {"label": "Lights, Power"})
        self.assertEqual(actions[0].settings, {"when": "start"}, "its default written out, handed on as written")
        self.assertEqual([i for a in actions for i in a.issues + a.warnings], [])

    def test_a_travel_stands_on_the_helper_it_names_and_a_spot_is_placed_only_where_it_names_none(self):
        named, unnamed = self._one(
            "A01,Go,Step up.,,,travel,CABINET,,,,,,where=VIEW_FRONT,",
            ",,,,,travel,CABINET,,,,,,when=end,",
        )
        # A helper named: the user stands on it, facing what the row's Target says.
        self.assertEqual(([t.split("|")[-1] for t in named.targets], named.fields), (["CABINET"], {"where": ["VIEW_FRONT"]}))
        # None named: the build places one facing the part, once, named as every tool names the row's own helper.
        self.assertEqual(([t.split("|")[-1] for t in unnamed.targets], unnamed.fields, unnamed.settings),
                         (["CABINET"], {"helpers": ["VIEW_A01_CABINET"]}, {"when": "end"}))

    def test_the_pointer_name_rule_is_a_sentence_sheets_alone(self):
        from unitytk.sheets import DeliveryIndex, SheetCheck

        index = DeliveryIndex(["CABINET", "CABINET|DOOR_GRP", "POINTS", "POINTS|LID_POINT", "POINTS|DOOR_GRP_POINT"],
                              [], "d.fbx")
        fd, path = tempfile.mkstemp(suffix=".csv")
        with os.fdopen(fd, "w", encoding="utf-8", newline="") as handle:
            handle.write(HEADER + "\nA01,Open,Open it.,,,,,,,,,,,\n,,,,,indicate,DOOR_GRP,,,,,,,\n")
        try:
            check = SheetCheck.run(path, delivery=index)
            plan = SheetPlan.from_sheet(path, index)
        finally:
            os.remove(path)
        # LID_POINT names a part the delivery lacks: a sentence sheet's rule. In cells a row names its pointer.
        self.assertEqual([item.line() for item in check.items], [])
        self.assertIsNone(plan.steps[0].actions[0].fields.get("where"), "no pointer found by its name")

    def test_a_linear_sheets_member_row_is_no_action(self):
        plan = _plan("A01,Open,The bolt comes out.,,,hide,BOLT_GRP,,,,,,,", ",,,,,,LID_GRP,,,,,,,", mapping="linear")
        actions = plan.steps[0].actions
        self.assertEqual([(a.behavior, a.issues, a.warnings) for a in actions], [("hide", [], [])])


if __name__ == "__main__":
    unittest.main()
