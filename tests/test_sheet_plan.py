# !/usr/bin/python
# coding=utf-8
"""Tests for ``unitytk.sheets``: a sequence sheet and its delivery read into a plan.

Two fixtures: a cabinet procedure in a studio's layout (``A01`` / ``IN_01`` / ``B02.5`` step
IDs, ``SECTION X:`` rows, one row per thing a step does), and a desk lamp assembly named
another way (``_MSH`` build suffixes, ``S-1`` step IDs, its own words); each delivery is an
FBX written by pythontk's ``FbxWriter`` and read back by ``FbxFile``, as a DCC's export is.
"""

import json
import os
import sys
import unittest
import unittest.mock
from pathlib import Path

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

import pythontk as ptk

from unitytk.sheets import (
    ClauseBook,
    ClauseSpec,
    DeliveryIndex,
    NodeMatcher,
    ReadingSpec,
    SheetPlan,
)

FbxNode, FbxWriter = ptk.FbxNode, ptk.FbxWriter

#: The delivery: (path, kind), parents before children.
NODES = [
    ("CABINET", "Null"),
    ("CABINET|DOOR_GRP", "Null"),
    ("CABINET|DOOR_GRP|DOOR_GEO", "Mesh"),
    ("CABINET|DOOR_RELEASE_LEVER", "Mesh"),
    ("CABINET|CABINET_LATCHES_GRP", "Null"),
    ("CABINET|CABINET_LATCHES_GRP|LATCH_UPPER_GRP", "Null"),
    ("CABINET|CABINET_LATCHES_GRP|LATCH_UPPER_GRP|LATCH_UPPER_GEO", "Mesh"),
    ("CABINET|CABINET_LATCHES_GRP|LATCH_LOWER_GRP", "Null"),
    ("CABINET|CABINET_LATCHES_GRP|LATCH_LOWER_GRP|LATCH_LOWER_GEO", "Mesh"),
    ("CABINET|SCREWS", "Null"),
    ("CABINET|SCREWS|SCREW_UPPER", "Mesh"),
    ("CABINET|SCREWS|SCREW_LOWER", "Mesh"),
    ("TOOLS", "Null"),
    ("TOOLS|KEY_GRP", "Null"),
    ("TOOLS|KEY_GRP|KEY_GEO", "Mesh"),
    ("TOOLS|WRENCH_GRP", "Null"),
    ("TOOLS|WRENCH_GRP|WRENCH_GEO", "Mesh"),
    ("data_export", "Null"),
]
TAKES = ["A01_1", "A01_2", "A02", "B01", "Z99"]

SHEET = """INTRO,,,,,
Step,Step Name,Hint,Text On Placard,Voice Support,Step Contents,Asset Names
IN_01,Welcome,,,Welcome.,The cabinet stands in front of you.,
,,,,,Selecting 'Begin' triggers step advancement,
SECTION A: Open the cabinet,,,,,
Step,Step Name,Hint,Text On Placard,Voice Support,Step Contents,Asset Names
A01,Unlock,-Bouncing arrow pointing at the key on the table,,Unlock both latches.,Highlight the Cabinet Latches,
,,,,,Selecting the key places it in hand,
,,,,,Selecting the top latch triggers Top Latch Open Animation and returns the key to hand,
,,,,,Selecting the bottom latch triggers Bottom Latch Open Animation and returns the key to hand,
,,,,,Key fades away from hand when both latches are open,
,,,,,Return key to table,
,,,,,Auto Advance,
A02,Open,"Placard text reads \"\"Select the door\"\"",,Select the door.,Highlight the door,
,,,,,Selecting the door triggers Door Open Animation,
,,,,,Auto Advance,
SECTION B: Check,,,,,
Step,Step Name,Hint,Text On Placard,Voice Support,Step Contents,Asset Names
B01,Tighten,,,,Highlight the screws,
,,,,,Selecting the top screw with the wrench in hand triggers Screw Tighten Animation,
,,,,,Highlight the toolbox lid,
,,,,,Highlight the release,
B02,Did it open?,,,,YES and NO buttons appear on the placard screen,
,,,,,Selecting 'YES' triggers step advancement to step B02.5,
,,,,,Selecting 'NO' triggers step advancement to step B03,
B02.5,Wrong,,,,Selecting 'Try Again' triggers step advancement to step B02,
B03,Done,,,,Highlight the cabinet lotches,
,,,,,Dance the cabinet jig,
"""


def write_delivery(
    path,
    nodes=NODES,
    takes=TAKES,
    shots=None,
    version=3,
    keys=None,
    carrier="data_export",
):
    """An FBX holding *nodes* (``|`` paths), one take per name of *takes*, and a
    carrier (the node named *carrier*: ``data_export`` among *nodes* is renamed to it) whose
    ``animation`` record (of *version*) lists *shots* (``[{clip, description}]``); *keys*
    ``{take: {path: [value, ...]}}`` keys each path's X translation in that take, one value
    per frame."""
    ids = {p: 1000 + i for i, (p, _) in enumerate(nodes)}
    objects, links = FbxNode("Objects"), FbxNode("Connections")
    for node, kind in nodes:
        name = node.rsplit("|", 1)[-1]
        is_carrier = name == "data_export"
        name = carrier if is_carrier else name
        model = objects.add(
            "Model", FbxWriter.i64(ids[node]), f"{name}\x00\x01Model", kind
        )
        if is_carrier:
            record = json.dumps({"version": version, "shots": shots or []})
            model.add("Properties70").p("animation", "KString", "", "U", record)
        parent = ids.get(node.rsplit("|", 1)[0], 0) if "|" in node else 0
        links.add("C", "OO", FbxWriter.i64(ids[node]), FbxWriter.i64(parent))
    uid = iter(range(9000, 99999))
    for i, take in enumerate(takes):
        objects.add(
            "AnimationStack", FbxWriter.i64(5000 + i), f"{take}\x00\x01AnimStack", ""
        )
        if not (keys or {}).get(take):
            continue
        layer = next(uid)
        objects.add(
            "AnimationLayer", FbxWriter.i64(layer), "BaseLayer\x00\x01AnimLayer", ""
        )
        links.add("C", "OO", FbxWriter.i64(layer), FbxWriter.i64(5000 + i))
        for node, values in keys[take].items():
            curve_node, curve = next(uid), next(uid)
            objects.add(
                "AnimationCurveNode",
                FbxWriter.i64(curve_node),
                "T\x00\x01AnimCurveNode",
                "",
            )
            links.add("C", "OO", FbxWriter.i64(curve_node), FbxWriter.i64(layer))
            links.add(
                "C",
                "OP",
                FbxWriter.i64(curve_node),
                FbxWriter.i64(ids[node]),
                "Lcl Translation",
            )
            record = objects.add(
                "AnimationCurve", FbxWriter.i64(curve), "\x00\x01AnimCurve", ""
            )
            record.add("Default", 0.0)
            record.add("KeyVer", 4009)
            record.add(
                "KeyTime",
                FbxWriter.array("l", [f * 1924423250 for f in range(len(values))]),
            )
            record.add("KeyValueFloat", FbxWriter.array("f", list(values)))
            record.add("KeyAttrFlags", FbxWriter.array("i", [4]))
            record.add("KeyAttrDataFloat", FbxWriter.array("f", [0.0, 0.0, 0.0, 0.0]))
            record.add("KeyAttrRefCount", FbxWriter.array("i", [len(values)]))
            links.add("C", "OP", FbxWriter.i64(curve), FbxWriter.i64(curve_node), "d|X")
    return FbxWriter.write(
        path, FbxWriter.header_records("unitytk tests") + [objects, links]
    )


class SheetPlanTestCase(UnityTkTestCase):
    def setUp(self):
        super().setUp()
        tmp = self.create_temp_dir()
        self.sheet = os.path.join(tmp, "cabinet_task.csv")
        with open(self.sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(SHEET)
        self.fbx = write_delivery(
            os.path.join(tmp, "cabinet.fbx"),
            shots=[
                {
                    "clip": "A02",
                    "description": "Selecting the door triggers Door Open Animation",
                }
            ],
        )
        self.tmp = tmp
        self.plan = SheetPlan.from_sheet(self.sheet, self.fbx, mapping="default")
        self.steps = {s.id: s for s in self.plan.steps}

    def _behaviors(self, step_id):
        return [a.behavior for a in self.steps[step_id].actions]

    def _action(self, step_id, behavior, nth=0):
        return [a for a in self.steps[step_id].actions if a.behavior == behavior][nth]

    def test_steps_keep_the_sheet_order_ids_and_text(self):
        self.assertEqual(
            list(self.steps), ["IN_01", "A01", "A02", "B01", "B02", "B02_5", "B03"]
        )
        a01 = self.steps["A01"]
        self.assertEqual(
            (a01.name, a01.section, a01.section_title),
            ("Unlock", "A", "Open the cabinet"),
        )
        self.assertEqual(a01.field("voice"), "Unlock both latches.")
        self.assertEqual(self.plan.source.sheet, "cabinet_task.csv")
        self.assertEqual(self.plan.source.delivery, "cabinet.fbx")

    def test_each_row_reads_as_one_action_in_sheet_order_then_the_hints(self):
        self.assertEqual(
            self._behaviors("A01"),
            [
                "highlight",
                "take",
                "use",
                "use",
                "hide",
                "return",
                "advance",
                "indicate",
            ],
        )
        self.assertEqual(self._behaviors("IN_01"), ["unread", "button"])
        # A hint line that is the page's text is the step's page, its row a note.
        self.assertEqual(self.steps["A02"].field("placard"), "Select the door")
        self.assertEqual(
            (self._action("A02", "note").text, self._action("A02", "note").fields),
            ('Placard text reads "Select the door"', {}),
        )

    def test_clip_rows_are_named_as_the_shot_manifest_names_their_takes(self):
        self.assertEqual(
            [a.clip for a in self.plan.actions("use")], ["A01_1", "A01_2", "A02", "B01"]
        )
        self.assertEqual(
            [SheetPlan.parse_issue(i)["reason"] for i in self.plan.issues],
            ["no step plays the take Z99"],
        )

    def test_targets_are_node_paths_and_the_tool_comes_from_the_row_or_the_hand(self):
        top = self._action("A01", "use")
        self.assertEqual(top.targets, ["CABINET|CABINET_LATCHES_GRP|LATCH_UPPER_GRP"])
        self.assertEqual(
            top.field("with"),
            "TOOLS|KEY_GRP",
            "'returns the key to hand' names the tool",
        )
        self.assertEqual(self._action("A01", "take").targets, ["TOOLS|KEY_GRP"])
        screw = self._action("B01", "use")
        self.assertEqual(screw.targets, ["CABINET|SCREWS|SCREW_UPPER"])
        self.assertEqual(
            screw.field("with"), "TOOLS|WRENCH_GRP", "'with the wrench in hand'"
        )
        self.assertIsNone(
            self._action("A02", "use").field("with"), "the key went back to the table"
        )

    def test_a_step_reads_a_vague_phrase_against_what_it_shows(self):
        self.assertEqual(self._action("A02", "use").targets, ["CABINET|DOOR_GRP"])
        self.assertEqual(
            self._action("A01", "indicate").targets,
            ["TOOLS|KEY_GRP"],
            "'on the table' says where, not what",
        )

    def test_a_button_names_steps_as_the_steps_spell_their_ids(self):
        self.assertEqual(
            [
                (a.field("label"), a.to)
                for a in self.steps["B02"].actions
                if a.behavior == "button"
            ],
            [("YES", "B02_5"), ("NO", "B03")],
        )
        self.assertEqual(self._action("B02_5", "button").to, "B02")
        # The page's buttons the sentence names are the step's choices: a note, held to them.
        buttons = self._action("B02", "note")
        self.assertEqual((buttons.text, buttons.fields, buttons.issues),
                         ("YES and NO buttons appear on the placard screen", {}, []))
        self.assertEqual(self._action("IN_01", "button").to, "next")

    def test_what_a_row_cannot_be_read_into_is_stated_never_guessed(self):
        lid, release = [
            a for a in self.steps["B01"].actions if a.behavior == "highlight"
        ][1:]
        self.assertEqual(
            (lid.targets, lid.issues), ([], ['no node is "the toolbox lid"'])
        )
        self.assertEqual(
            release.targets, [], "a vaguer word for one object binds nothing"
        )
        self.assertIn("DOOR_RELEASE_LEVER", release.issues[0])
        typo = self._action("B03", "highlight")
        self.assertEqual(typo.targets, ["CABINET|CABINET_LATCHES_GRP"])
        self.assertEqual(
            (typo.issues, typo.warnings),
            ([], ['read "lotch" as "latch": correct the typo']),
        )
        self.assertEqual(self._action("B03", "unread").text, "Dance the cabinet jig")

    def test_a_plan_round_trips_through_its_file(self):
        path = self.plan.write(os.path.join(self.tmp, "plan.json"))
        data = json.loads(open(path, encoding="utf-8").read())
        self.assertTrue(SheetPlan.validate(data).ok)
        self.assertEqual(data["version"], SheetPlan.VERSION)
        self.assertEqual(SheetPlan.load(path).to_dict(), self.plan.to_dict())

    def test_a_dry_run_plans_the_write_and_writes_nothing(self):
        import pythontk as ptk

        path = os.path.join(self.tmp, "plan.json")
        ctx = ptk.OpContext(dry_run=True)
        self.plan.write(path, ctx=ctx)
        self.assertFalse(os.path.exists(path))
        self.assertEqual([c.target for c in ctx.changes], [path])

    def test_the_summary_counts_steps_actions_unread_and_issues(self):
        summary = self.plan.summary()
        self.assertEqual((summary["steps"], summary["unread"]), (7, 2))
        self.assertEqual(summary["behaviors"]["use"], 4)
        self.assertEqual(summary["actions"], sum(summary["behaviors"].values()))

    def test_load_refuses_another_version(self):
        path = self.plan.write(os.path.join(self.tmp, "plan.json"))
        data = json.loads(open(path, encoding="utf-8").read())
        data["version"] = 99
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(data, fh)
        with self.assertRaisesRegex(ValueError, "read its sheet again"):
            SheetPlan.load(path)


INTERACTIVE = """Step,Step Name,Text On Placard,Step Contents,Asset Names,Action,Animation Clip Names
SECTION A: Open the cabinet,,,,,,
A01,Unlock,Unlock both latches.,Highlight the cabinet latches,"LATCH_UPPER
LATCH_LOWER",,
,,,Selecting the key places it in hand,KEY,,
,,,Selecting the top latch triggers Top Latch Open Animation and returns the key to hand,"LATCH_UPPER
KEY",,A01_1
,,,Selecting the bottom latch triggers Bottom Latch Open Animation and returns the key to hand,"LATCH_UPPER
KEY",,A01_2
,,,Auto Advance,,,
A02,Door,Open the door.,Click the door so it swings open,DOOR,use,
,,,Selecting DOOR_RELEASE_LEVER triggers step advancement,,,
B01,Swap,,Selecting the door triggers Door Animation,DOOR,,
,,,Trigger Lid Animation,DOOR,,
,,,The wrench is somewhere,,,
B03,Shown,,Highlight the door,DOOR,,
,,,Selecting the highlighted area triggers Door Animation,"DOOR
DOOR_RELEASE_LEVER",,
,,,The room is quiet,,note,
,,,Selecting the latch with the key in hand triggers Latch Animation,"CABINET_LATCHES
KEY",,B03_2
B02,Mismatch,,Highlight the door,DOOR,use,
,,,Selecting the lever with the key in hand triggers Lever Animation,"DOOR_RELEASE_LEVER
KEY",,B02_2
,,,Selecting the screws triggers Screw Animation,"SCREWS
NO_SUCH_PART",,
"""


class InteractiveSheetTestCase(UnityTkTestCase):
    """A sheet in the Interactive format: each row's words bind to that row's own names, an Action
    cell reads a row no phrase does, and each animation row names its take."""

    def setUp(self):
        super().setUp()
        tmp = self.create_temp_dir()
        sheet = os.path.join(tmp, "task.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(INTERACTIVE)
        fbx = write_delivery(os.path.join(tmp, "cabinet.fbx"))
        self.plan = SheetPlan.from_sheet(sheet, fbx, mapping="interactive")
        self.steps = {s.id: s for s in self.plan.steps}

    def _acts(self, step_id):
        return self.steps[step_id].actions

    def _leafs(self, paths):
        return [p.rsplit("|", 1)[-1] for p in paths]

    def test_a_rows_words_bind_only_to_the_names_that_row_lists(self):
        use = [a for a in self._acts("A01") if a.behavior == "use"][0]
        self.assertEqual(self._leafs(use.targets), ["LATCH_UPPER_GRP"])
        self.assertEqual(self._leafs([use.field("with")]), ["KEY_GRP"])
        self.assertEqual(use.clip, "A01_1")
        self.assertEqual(use.issues, [])
        highlight = self._acts("A01")[0]
        self.assertEqual(
            sorted(self._leafs(highlight.targets)),
            ["LATCH_LOWER_GRP", "LATCH_UPPER_GRP"],
            "a plural phrase binds every listed name it fits",
        )

    def test_a_modifier_that_contradicts_the_name_is_reported_not_bound_silently(self):
        # The reviewer wrote "bottom" but the row still lists LATCH_UPPER.
        use = [a for a in self._acts("A01") if a.behavior == "use"][1]
        self.assertTrue(
            any("bottom" in i and "LATCH_UPPER" in i for i in use.issues), use.issues
        )

    def test_an_action_cell_reads_a_row_no_phrase_does(self):
        use = self._acts("A02")[0]
        self.assertEqual(
            (use.behavior, self._leafs(use.targets), use.clip),
            ("use", ["DOOR_GRP"], "A02"),
        )
        self.assertEqual(use.issues, [])

    def test_a_name_written_inline_binds_exactly(self):
        select = self._acts("A02")[1]
        self.assertEqual(
            (select.behavior, self._leafs(select.targets)),
            ("select", ["DOOR_RELEASE_LEVER"]),
        )

    def test_a_step_of_several_animation_rows_names_each_take(self):
        b01 = self._acts("B01")
        self.assertTrue(any("Animation Clip Names" in i for a in b01 for i in a.issues))

    def test_a_row_neither_a_phrase_nor_an_action_reads_is_an_error_with_its_fix(self):
        unread = [a for a in self._acts("B01") if a.behavior == "unread"][0]
        self.assertTrue(any("Action" in i for i in unread.issues), unread.issues)

    def test_a_phrase_and_an_action_that_disagree_and_an_unknown_name_are_reported(
        self,
    ):
        b02 = self._acts("B02")
        self.assertTrue(any("disagree" in i for i in b02[0].issues), b02[0].issues)
        self.assertTrue(any("NO_SUCH_PART" in i for a in b02 for i in a.issues))

    def test_a_vague_phrase_reads_against_what_the_step_shows(self):
        use = next(a for a in self._acts("B03") if a.behavior == "use")
        bound = [
            i for i in use.issues if "(Animation Clip Names)" not in i
        ]  # the fixture has no take B03
        self.assertEqual((self._leafs(use.targets), bound), (["DOOR_GRP"], []))

    def test_a_head_noun_matches_a_names_plural_word(self):
        use = next(a for a in self._acts("B03") if "latch" in a.text)
        self.assertEqual(self._leafs(use.targets), ["CABINET_LATCHES_GRP"])

    def test_a_row_its_author_marks_a_note_is_a_note(self):
        note = next(a for a in self._acts("B03") if a.behavior == "note")
        self.assertEqual((note.behavior, note.targets, note.issues), ("note", [], []))

    def test_a_phrase_binds_the_one_listed_name_carrying_its_head_noun(self):
        use = next(a for a in self._acts("B02") if "lever" in a.text)
        self.assertEqual(
            (self._leafs(use.targets), self._leafs([use.field("with")])),
            (["DOOR_RELEASE_LEVER"], ["KEY_GRP"]),
        )

    def test_issues_name_their_cell_and_fix(self):
        unread = [a for a in self._acts("B01") if a.behavior == "unread"][0]
        self.assertEqual(
            SheetPlan.parse_issue(unread.issues[0]),
            {
                "step": "",
                "row": 3,
                "column": "Step Contents",
                "reason": "no phrase reads it and no Action names it",
                "fix": "write it as a phrase, or fill Action",
            },
        )
        missing = [
            i for a in self._acts("B02") for i in a.issues if "NO_SUCH_PART" in i
        ][0]
        self.assertEqual(SheetPlan.parse_issue(missing)["column"], "Asset Names")

    def test_parse_issue_reads_a_cell_a_hint_line_and_a_bare_issue(self):
        self.assertEqual(
            SheetPlan.parse_issue(
                "row 2 (Animation Clip Names): the take moves nothing: re-export it"
            ),
            {
                "step": "",
                "row": 2,
                "column": "Animation Clip Names",
                "reason": "the take moves nothing",
                "fix": "re-export it",
            },
        )
        self.assertEqual(
            SheetPlan.parse_issue(
                '(Hint): no hint phrase reads "x": move it to Text On Placard'
            )["column"],
            "Hint",
        )
        self.assertEqual(
            SheetPlan.parse_issue('no node is "the lid"'),
            {"step": "", "row": None, "column": "", "reason": 'no node is "the lid"', "fix": ""},
        )
        # A step row's own cell names its step (the check found it in the text alone). Added: 2026-11-25
        self.assertEqual(
            SheetPlan.parse_issue("step B04A (To): the sheet has no step B99: write a step's ID"),
            {"step": "B04A", "row": None, "column": "To", "reason": "the sheet has no step B99",
             "fix": "write a step's ID"},
        )


TAKES_SHEET = """Step,Step Contents,Asset Names,Animation Clip Names
A01,Selecting the door triggers Door Animation,DOOR,
A02,Selecting the lever triggers Lever Animation,DOOR_RELEASE_LEVER,
B01,Selecting the door triggers Close Animation,DOOR,A02
"""


QUIZ_SHEET = """Step,Text On Placard,Step Contents,Asset Names
QUIZ,,,
Q01,Why are the latches opened first?,To free the key,
,,* To reach the door,
,,Auto Advance,
"""


VOICED_SHEET = """Step,Step Contents,Asset Names,Voice Support,Audio Files
A01,Auto Advance,,Open the door.,
A02,Auto Advance,,Close the door.,a02_take.wav
A03,Auto Advance,,Lock the door.,
"""


class StepSoundTestCase(UnityTkTestCase):
    """A step's sound file -- its recording, else its narration -- is in the plan, relative to the
    project's settings folder, found as the Shot Manifest finds it."""

    def test_a_steps_recording_else_its_narration_is_its_sound_file(self):
        from pythontk.audio_utils.narrator import Narrator

        project = self.create_temp_dir()
        sheet = os.path.join(project, "exports", "task.csv")
        os.makedirs(os.path.dirname(sheet))
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(VOICED_SHEET)
        for folder, name in (
            ("narration", Narrator().key("Open the door.") + ".wav"),
            ("recordings", "a02_take.wav"),
        ):
            os.makedirs(os.path.join(project, folder), exist_ok=True)
            open(os.path.join(project, folder, name), "wb").close()
        index = DeliveryIndex([p for p, _ in NODES], [], "d.fbx")
        plan = SheetPlan.from_sheet(
            sheet,
            index,
            mapping="interactive",
            options={"audio": "narrate"},
            base=project,
        )
        sounds = {s.id: s.field("sound") or "" for s in plan.steps}
        self.assertEqual(
            sounds,
            {
                "A01": "narration/" + Narrator().key("Open the door.") + ".wav",
                "A02": "recordings/a02_take.wav",
                "A03": "",
            },
            "a recording wins; a line not narrated yet has none",
        )
        self.assertIsNone(
            SheetPlan.from_sheet(sheet, index, mapping="interactive")
            .steps[0]
            .field("sound"),
            "a sheet read without the option has no sound files",
        )


class UpstreamTestCase(UnityTkTestCase):
    """What the delivery's own build keyed (the Shot Manifest's `keyed`, per step) is left to it: the
    plan marks those rows upstream, the rest it builds; with no record, it builds them all (2026-11-23:
    "behaviors work hand-in-hand when both manifests are used in the same pipeline, and work
    independently when not")."""

    def _plan(self, keyed):
        sheet = os.path.join(self.create_temp_dir(), "task.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(SHEET)
        index = DeliveryIndex([p for p, _ in NODES], [], "d.fbx", keyed=keyed)
        return {
            s.id: s for s in SheetPlan.from_sheet(sheet, index, mapping="default").steps
        }

    def test_the_objects_the_delivery_keyed_are_left_to_it_and_the_rest_built(self):
        first = self._plan({})["A01"].actions[0]  # "Highlight the Cabinet Latches"
        self.assertEqual(first.behavior, "highlight")
        one = DeliveryIndex([p for p, _ in NODES], [], "d.fbx").name_of(
            first.targets[0]
        )  # the name the DCC's entry spells
        steps = self._plan({"A01": [(one, "highlight"), (one, "fade_out")]})
        acts = steps["A01"].actions
        self.assertEqual(
            acts[0].upstream, [first.targets[0]], "the object the delivery keyed it on"
        )
        self.assertEqual(
            [a.upstream for a in acts if a.behavior == "take"],
            [[]],
            "a behavior it did not key",
        )
        self.assertFalse(
            any(a.upstream for a in steps["IN_01"].actions), "only the step it keyed"
        )
        alone = self._plan({})
        self.assertFalse(
            any(a.upstream for s in alone.values() for a in s.actions),
            "no record: everything is built here",
        )

    def test_a_cell_rows_keyed_node_is_found_by_the_name_the_sheet_wrote(self):
        """The DCC's entry names what it keyed as the sheet writes it, a cell a node's own name (``DOOR_GRP``), and
        the match by object name (``door``) missed it: both manifests built the highlight. Added: 2026-11-26"""
        sheet = os.path.join(self.create_temp_dir(), "cells.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write("Step,Step Name,Voice,Action,Target,Settings\nA01,Open,Open it.,,,\n,,,highlight,DOOR_GRP,\n"
                     ",,,take,KEY_GRP,\n")
        index = DeliveryIndex([p for p, _ in NODES], [], "d.fbx", keyed={"A01": [("DOOR_GRP", "highlight")]})
        highlight, take = SheetPlan.from_sheet(sheet, index, mapping="interactive").steps[0].actions
        self.assertEqual((highlight.upstream, take.upstream), (["CABINET|DOOR_GRP"], []))


class ContractTestCase(UnityTkTestCase):
    """The plan names no kit's data: a row's words are its fields, a step's values its fields (named by
    the reading), a kit's plan-wide data its entry of the extensions; each round trips the plan file."""

    def test_a_kits_data_rides_the_extensions_as_given_and_round_trips(self):
        from unitytk.sheets.sheet_plan import PlanAction, PlanStep

        use = PlanAction(
            behavior="use",
            text="Selecting the screw triggers Screw Out",
            targets=["PANEL|SCREW_GRP"],
            clip="A01",
            fields={"with": "TOOLS|SCREWDRIVER_GRP", "where": ["POINTS|SCREW_POINT"]},
        )
        plan = SheetPlan(
            version=SheetPlan.VERSION,
            steps=[PlanStep(id="A01", fields={"voice": "Go."}, actions=[use])],
        )
        tools = {"tools": {"SCREWDRIVER_GRP": {"tip": [0, 0, 0.12], "travel": False}}}
        plan.with_extensions({"task": tools, "other_kit": {"anything": [1, "two"]}})
        path = plan.write(os.path.join(self.create_temp_dir(), "plan.json"))
        loaded = SheetPlan.load(path)
        self.assertEqual(
            loaded.extensions["task"],
            tools,
            "a kit's data as given: that kit reads it",
        )
        self.assertEqual(loaded.extensions["other_kit"], {"anything": [1, "two"]})
        self.assertEqual(
            (
                loaded.steps[0].field("voice"),
                loaded.steps[0].actions[0].field("where"),
            ),
            ("Go.", ["POINTS|SCREW_POINT"]),
        )
        self.assertEqual(
            loaded.steps[0].actions[0].field("clip"),
            "A01",
            "a declared field by name too",
        )
        self.assertFalse(
            SheetPlan.validate(
                {"version": SheetPlan.VERSION, "steps": [], "extensions": {"kit": 3}}
            ).ok,
            "an entry is an object",
        )

    def test_a_projects_own_group_is_a_field_of_its_row(self):
        own = ClauseSpec(
            word="highlight",
            detect=[r"^weigh\s+(?P<targets>.+?)\s+at\s+(?P<grams>\d+)\s+grams$"],
            order=5,
        )
        sheet = os.path.join(self.create_temp_dir(), "weigh.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(
                "Step,Step Contents,Asset Names\nA01,Weigh the key at 12 grams,KEY\n"
            )
        plan = SheetPlan.from_sheet(
            sheet,
            DeliveryIndex([p for p, _ in NODES], [], "d.fbx"),
            book=ClauseBook([("weigh", own)]),
        )
        action = plan.steps[0].actions[0]
        self.assertEqual(
            (action.behavior, action.targets, action.fields),
            ("highlight", ["TOOLS|KEY_GRP"], {"grams": "12"}),
        )


class QuizTestCase(UnityTkTestCase):
    """A question is a step with an option written with a leading `*`: its rows no phrase reads are
    its options, buttons: the starred one goes on, any other is a wrong answer."""

    def test_a_questions_rows_are_its_options_the_starred_one_correct(self):
        sheet = os.path.join(self.create_temp_dir(), "quiz.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(QUIZ_SHEET)
        index = DeliveryIndex([p for p, _ in NODES], [], "d.fbx")
        plan = SheetPlan.from_sheet(sheet, index, mapping="interactive")
        acts = plan.steps[0].actions
        self.assertEqual(
            [(a.behavior, a.field("label"), a.to, a.issues) for a in acts],
            [
                ("button", "To free the key", "retry", []),
                ("button", "To reach the door", "next", []),
                ("advance", None, None, []),
            ],
        )
        self.assertEqual(
            plan.steps[0].field("placard"), "Why are the latches opened first?"
        )


class TakeChecksTestCase(UnityTkTestCase):
    """The takes follow the sheet: the file's order, a take named like another step, the export's record."""

    def _plan(self, takes, steps=None):
        sheet = os.path.join(self.create_temp_dir(), "takes.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(TAKES_SHEET)
        index = DeliveryIndex([p for p, _ in NODES], takes, "d.fbx", steps=steps or {})
        plan = SheetPlan.from_sheet(sheet, index, mapping="interactive")
        return {s.id: [i for a in s.actions for i in a.issues] for s in plan.steps}

    def test_a_take_out_of_the_sheets_order_in_the_file_is_reported_to_the_artist(self):
        issues = self._plan(["A02", "A01"])
        self.assertTrue(
            any("re-export" in i and "A02" in i for i in issues["A02"]), issues
        )
        self.assertEqual(issues["A01"], [])

    def test_a_take_named_like_another_steps_id_is_an_error(self):
        issues = self._plan(["A01", "A02"])
        self.assertTrue(any("A02" in i and "step" in i for i in issues["B01"]), issues)

    def test_a_take_the_export_records_for_another_step_is_an_error(self):
        issues = self._plan(["A01", "A02"], steps={"A01": "B01"})
        self.assertTrue(
            any("records" in i and "B01" in i for i in issues["A01"]), issues
        )
        self.assertEqual(self._plan(["A01", "A02"], steps={"A01": "A01"})["A01"], [])


MOVES_SHEET = """Step,Step Contents,Asset Names,Animation Clip Names
A01,Selecting the top latch triggers Top Latch Open Animation,"LATCH_UPPER
KEY",A01_1
,Selecting the bottom latch triggers Bottom Latch Open Animation,"LATCH_LOWER
KEY",A01_2
"""

LATCH_UPPER = "CABINET|CABINET_LATCHES_GRP|LATCH_UPPER_GRP"
LATCH_LOWER = "CABINET|CABINET_LATCHES_GRP|LATCH_LOWER_GRP"


class MovesCheckTestCase(UnityTkTestCase):
    """A take moves its own row's names: one moving only another row's is the wrong take, one moving
    none of its row's is suspect; a name two rows list belongs to neither."""

    def _issues(self, moved, warnings=False):
        sheet = os.path.join(self.create_temp_dir(), "moves.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(MOVES_SHEET)
        index = DeliveryIndex(
            [p for p, _ in NODES], ["A01_1", "A01_2"], "d.fbx", moved=moved
        )
        plan = SheetPlan.from_sheet(sheet, index, mapping="interactive")
        return {
            a.clip: (a.warnings if warnings else a.issues)
            for a in plan.steps[0].actions
            if a.clip
        }

    def test_a_take_moving_its_own_rows_names_passes(self):
        issues = self._issues(
            {
                "A01_1": [LATCH_UPPER + "|LATCH_UPPER_GEO", "TOOLS|KEY_GRP"],
                "A01_2": [LATCH_LOWER],
            }
        )
        self.assertEqual(issues, {"A01_1": [], "A01_2": []})

    def test_a_take_moving_another_rows_names_and_none_of_its_own_is_an_error(self):
        issues = self._issues({"A01_1": [LATCH_UPPER], "A01_2": [LATCH_UPPER]})
        self.assertEqual(issues["A01_1"], [])
        self.assertTrue(
            any(
                "LATCH_UPPER (row 1)" in SheetPlan.parse_issue(i)["reason"]
                for i in issues["A01_2"]
            ),
            issues,
        )

    def test_a_take_moving_none_of_its_rows_names_is_a_warning_a_shared_name_counting_for_neither(
        self,
    ):
        moved = {"A01_1": [LATCH_UPPER], "A01_2": ["TOOLS|KEY_GRP|KEY_GEO"]}
        warnings = self._issues(moved, warnings=True)
        self.assertTrue(any("LATCH_LOWER" in w for w in warnings["A01_2"]), warnings)
        self.assertEqual(
            self._issues(moved)["A01_2"], [], "KEY is listed by both rows: no error"
        )

    def test_a_take_whose_motion_is_unknown_is_not_checked(self):
        self.assertEqual(self._issues({}), {"A01_1": [], "A01_2": []})

    def test_an_ancestor_moving_moves_the_names_under_it(self):
        issues = self._issues(
            {"A01_1": ["CABINET|CABINET_LATCHES_GRP"], "A01_2": ["CABINET"]}
        )
        self.assertEqual(issues, {"A01_1": [], "A01_2": []})


TELEPORT_SHEET = """Step,Step Contents,Asset Names
A01,"User is teleported to VIEW_FRONT, facing the door","VIEW_FRONT
DOOR"
A02,User is teleported to the key,KEY
"""


class TravelTestCase(UnityTkTestCase):
    """A travel goes to a viewpoint (its `where`: a name the template's viewpoint pattern matches) and
    faces what the row says it faces (its targets), among the row's names."""

    def setUp(self):
        super().setUp()
        sheet = os.path.join(self.create_temp_dir(), "teleport.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(TELEPORT_SHEET)
        index = DeliveryIndex(
            [p for p, _ in NODES] + ["VIEWS", "VIEWS|VIEW_FRONT"], [], "d.fbx"
        )
        self.plan = SheetPlan.from_sheet(sheet, index, mapping="interactive")
        self.steps = {s.id: s.actions[0] for s in self.plan.steps}

    def test_a_travel_stands_at_its_viewpoint_facing_the_named_object(self):
        travel = self.steps["A01"]
        self.assertEqual(
            (travel.behavior, travel.targets, travel.field("where")),
            ("travel", ["CABINET|DOOR_GRP"], ["VIEWS|VIEW_FRONT"]),
        )
        self.assertEqual(travel.issues, [])

    def test_a_travel_may_say_only_what_the_user_faces_its_spot_found_by_the_build(
        self,
    ):
        """ "The user is teleported facing the door": no destination named, none needed; the
        build finds a spot where the door is seen. Added: 2026-11-21"""
        sheet = os.path.join(self.create_temp_dir(), "facing.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(
                "Step,Step Contents,Asset Names\nA01,User is teleported facing the door,DOOR\n"
            )
        index = DeliveryIndex([p for p, _ in NODES], [], "d.fbx")
        travel = (
            SheetPlan.from_sheet(sheet, index, mapping="interactive")
            .steps[0]
            .actions[0]
        )
        self.assertEqual(
            (
                travel.behavior,
                travel.targets,
                travel.field("where"),
                travel.issues,
            ),
            ("travel", ["CABINET|DOOR_GRP"], None, []),
        )

    def test_a_travel_may_say_which_side_of_what_the_user_stands_on(self):
        """ "... to the front of the cabinet. Facing the door." (as a sheet writes it): the side and
        what it is a side of, both from the row's names. Added: 2026-11-21"""
        sheet = os.path.join(self.create_temp_dir(), "side.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(
                'Step,Step Contents,Asset Names\nA01,User is teleported to the front of the cabinet.  Facing the door.,"CABINET\nDOOR"\n'
                'A02,"User is teleported to the other side of the door, facing the lid",DOOR\n'
            )
        index = DeliveryIndex([p for p, _ in NODES] + ["CABINET|LID"], [], "d.fbx")
        front, other = [
            s.actions[0]
            for s in SheetPlan.from_sheet(sheet, index, mapping="interactive").steps
        ]
        self.assertEqual(
            (
                front.targets,
                front.field("side"),
                front.field("side_of"),
                front.field("where"),
                front.issues,
            ),
            (["CABINET|DOOR_GRP"], "front", "CABINET", None, []),
        )
        self.assertEqual(
            (other.field("side"), other.field("side_of"), other.issues),
            ("other", "CABINET|DOOR_GRP", []),
            other.issues,
        )

    def test_a_listed_name_the_delivery_repeats_says_so_and_a_path_settles_it(self):
        """Two nodes named CABINET: the row is told which (not "matches none"), and listing one by
        its path binds it. Added: 2026-11-21"""
        sheet = os.path.join(self.create_temp_dir(), "twice.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(
                'Step,Step Contents,Asset Names\nA01,"User is teleported to the front of the cabinet, facing the door","CABINET\nDOOR"\n'
                'A02,"User is teleported to the front of the cabinet, facing the door","A|CABINET\nDOOR"\n'
            )
        index = DeliveryIndex(
            ["A", "A|CABINET", "A|CABINET|DOOR", "B", "B|CABINET"], [], "d.fbx"
        )
        first, second = [
            s.actions[0]
            for s in SheetPlan.from_sheet(sheet, index, mapping="interactive").steps
        ]
        self.assertTrue(
            any("could be any of A|CABINET, B|CABINET" in i for i in first.issues),
            first.issues,
        )
        self.assertEqual((second.field("side_of"), second.issues), ("A|CABINET", []))

    def test_a_pointer_is_a_step_row_naming_its_part(self):
        """ "Arrow points at the door" in Step Contents, the part in Asset Names: no Hint wording
        to match. Added: 2026-11-21"""
        sheet = os.path.join(self.create_temp_dir(), "pointer.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(
                "Step,Step Contents,Asset Names\nA01,Highlight the door,DOOR\n,Arrow points at the door,DOOR\n"
                ",Arrows point at the key,KEY\n"
            )
        index = DeliveryIndex([p for p, _ in NODES], [], "d.fbx")
        actions = (
            SheetPlan.from_sheet(sheet, index, mapping="interactive").steps[0].actions
        )
        pointers = [(a.targets, a.issues) for a in actions if a.behavior == "indicate"]
        self.assertEqual(pointers, [(["CABINET|DOOR_GRP"], []), (["TOOLS|KEY_GRP"], [])])

    def test_a_destination_that_is_no_viewpoint_is_an_error(self):
        self.assertTrue(
            any("viewpoint" in i and "KEY" in i for i in self.steps["A02"].issues),
            self.steps["A02"].issues,
        )

    def test_a_hint_arrow_names_the_deliverys_authored_pointer_for_its_target(self):
        sheet = os.path.join(self.create_temp_dir(), "pointers.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(
                'Step,Step Contents,Asset Names,Hint\nA01,Highlight the door,"DOOR\nKEY",'
                '"-Bouncing arrow pointing at the door\n-Bouncing arrow pointing at the key"\n'
            )
        index = DeliveryIndex(
            [p for p, _ in NODES] + ["POINTS", "POINTS|DOOR_POINT"], [], "d.fbx"
        )
        plan = SheetPlan.from_sheet(sheet, index, mapping="interactive")
        door, key = [a for a in plan.steps[0].actions if a.behavior == "indicate"]
        self.assertEqual(
            (door.field("where"), door.issues), (["POINTS|DOOR_POINT"], [])
        )
        self.assertIsNone(key.field("where"), "no pointer authored: the kit makes one")

    def test_a_reading_without_a_viewpoint_rule_imposes_none(self):
        sheet = os.path.join(self.create_temp_dir(), "t.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(TELEPORT_SHEET)
        reading = ReadingSpec.load("interactive")
        del reading.names["viewpoint"]
        index = DeliveryIndex(
            [p for p, _ in NODES] + ["VIEWS", "VIEWS|VIEW_FRONT"], [], "d.fbx"
        )
        plan = SheetPlan.from_sheet(sheet, index, reading=reading)
        self.assertEqual(
            plan.steps[1].actions[0].issues,
            [],
            "no viewpoint rule declared: the key is a place like any",
        )

    def test_the_viewpoint_round_trips_through_the_plan_file(self):
        path = os.path.join(self.create_temp_dir(), "plan.json")
        self.plan.write(path)
        travel = SheetPlan.load(path).steps[0].actions[0]
        self.assertEqual((travel.targets, travel.field("where")), (["CABINET|DOOR_GRP"], ["VIEWS|VIEW_FRONT"]))


#: The second fixture: a desk lamp assembly whose builds end ``_MSH`` and ``_NUL``, its steps ``S-1``.
LAMP_NODES = [
    "DESK",
    "DESK|LAMP_NUL",
    "DESK|LAMP_NUL|LAMP_MSH",
    "DESK|LAMP_NUL|SHADE_MSH",
    "DESK|SWITCH_RED_MSH",
    "DESK|SWITCH_GRAY_MSH",
    "DESK|BULB_NEW_MSH",
    "DESK|BULB_OLD_MSH",
]
#: How the lamp's sheet is read: its build suffixes, its step IDs' shape and a word of its own.
LAMP_READING = {
    "names": {"suffixes": ["MSH", "NUL"]},
    "step_id": r"S-\d+",
    "vocabulary": {"synonyms": {"cover": "shade"}},
}


def lamp_template():
    """The Interactive template, its step IDs written ``S-1``."""
    from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

    template = Mapping.load_mapping("interactive")
    template["columns"]["step_pattern"] = [r"^(S-\d+)$"]
    return template


class LampFixtureTestCase(UnityTkTestCase):
    """A delivery named another way reads as its reading says: its build suffixes on every path (the
    tolerant reading, a row's own names, what a step shows), its step IDs' shape, its own words."""

    def _plan(self, text, mapping="default", reading=LAMP_READING, **kwargs):
        sheet = os.path.join(self.create_temp_dir(), "lamp.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(text)
        return SheetPlan.from_sheet(
            sheet,
            DeliveryIndex(LAMP_NODES, [], "lamp.fbx"),
            mapping=mapping,
            reading=reading,
            **kwargs,
        )

    def test_the_tolerant_reading_groups_the_delivery_by_the_readings_suffixes(self):
        plan = self._plan(
            "Step,Step Contents,Asset Names\nA01,Highlight the lamp,\n,Highlight the cover,\n"
        )
        self.assertEqual(
            [(a.targets, a.issues) for a in plan.steps[0].actions],
            [(["DESK|LAMP_NUL"], []), (["DESK|LAMP_NUL|SHADE_MSH"], [])],
            "LAMP_NUL is the lamp; 'cover' is the reading's word for a shade",
        )

    def test_a_row_binds_its_own_names_by_the_readings_suffixes(self):
        plan = self._plan(
            "Step,Step Contents,Asset Names\nA01,Selecting the gray switch triggers step advancement,"
            '"SWITCH_GRAY\nSWITCH_RED"\n',
            mapping="interactive",
            reading=dict(LAMP_READING, strict=True),
        )
        select = plan.steps[0].actions[0]
        self.assertEqual(
            (select.targets, select.issues), (["DESK|SWITCH_GRAY_MSH"], [])
        )

    def test_what_a_step_shows_is_matched_by_the_deliverys_own_suffixes(self):
        matcher = NodeMatcher(DeliveryIndex(LAMP_NODES, suffixes=["MSH", "NUL"]))
        self.assertIsNone(matcher.match("the switch").path, "two switches: a tie")
        self.assertEqual(
            matcher.match("the switch", ["DESK|SWITCH_GRAY_MSH"]).path,
            "DESK|SWITCH_GRAY_MSH",
        )

    def test_a_step_ids_shape_is_the_readings(self):
        text = "Step,Step Contents,Asset Names\nS-1,Selecting 'Next' triggers step S-2,\nS-2,Highlight the lamp,LAMP\n"
        plan = self._plan(
            text, mapping=lamp_template(), reading=dict(LAMP_READING, strict=True)
        )
        button = plan.steps[0].actions[0]
        self.assertEqual(
            ([s.id for s in plan.steps], button.behavior, button.to, button.issues),
            (["S_1", "S_2"], "button", "S_2", []),
        )
        default = self._plan(
            text,
            mapping=lamp_template(),
            reading=dict(LAMP_READING, strict=True, step_id=r"[A-Z]+\d+"),
        )
        self.assertEqual(
            default.steps[0].actions[0].behavior,
            "unread",
            "the default shape reads no S-2",
        )

    def test_the_vocabulary_is_injected(self):
        index = DeliveryIndex(LAMP_NODES, suffixes=["MSH", "NUL"])
        self.assertIsNone(NodeMatcher(index).match("the cover").path)
        self.assertEqual(
            NodeMatcher(index, vocabulary={"synonyms": {"cover": "shade"}})
            .match("the cover")
            .path,
            "DESK|LAMP_NUL|SHADE_MSH",
        )
        old = NodeMatcher(index, vocabulary={"distinct": []}).match("the old bulb")
        self.assertEqual(old.path, "DESK|BULB_OLD_MSH", "its own words decide")
        ruled = NodeMatcher(index, vocabulary={"opposites": [[["red"], ["gray"]]]})
        self.assertIsNotNone(
            ruled.contradiction("the red switch", "SWITCH_GRAY"), "its own opposites"
        )
        self.assertIsNone(
            NodeMatcher(index).contradiction("the red switch", "SWITCH_GRAY")
        )

    def test_a_behavior_takes_part_only_by_the_roles_its_template_declares(self):
        """No behavior is known by its name: a take template declaring no role puts nothing in hand."""
        text = (
            "Step,Step Contents,Asset Names\nA01,Selecting the lamp places it in hand,\n"
            ",Selecting the gray switch triggers Click Animation,\n"
        )
        silent = [
            (n, ClauseSpec.from_dict(dict(spec.to_dict(), roles=[])))
            if n == "take"
            else (n, spec)
            for n, spec in ClauseBook.load_templates()
        ]
        reading = dict(LAMP_READING, vocabulary={"distinct": []})
        heard = self._plan(text, reading=reading).steps[0].actions[1]
        unheard = (
            self._plan(text, reading=reading, book=ClauseBook(silent))
            .steps[0]
            .actions[1]
        )
        self.assertEqual(
            (heard.field("with"), unheard.field("with")), ("DESK|LAMP_NUL", None)
        )


class DeliveryIndexTestCase(UnityTkTestCase):
    def test_from_fbx_reads_what_each_take_moves(self):
        fbx = write_delivery(
            os.path.join(self.create_temp_dir(), "d.fbx"),
            keys={
                "A02": {"CABINET|DOOR_GRP": [0.0, 5.0], "TOOLS|KEY_GRP": [2.0, 2.0]},
                "B01": {},
            },
        )
        index = DeliveryIndex.from_fbx(fbx)
        self.assertEqual(
            index.moved.get("A02"), ("CABINET|DOOR_GRP",), "a held key moves nothing"
        )
        self.assertEqual(
            index.moved.get("B01"), (), "a take keying nothing moves nothing"
        )
        unkeyed = DeliveryIndex.from_fbx(
            write_delivery(os.path.join(self.create_temp_dir(), "u.fbx"))
        )
        self.assertEqual(unkeyed.moved, {}, "a file keying nothing states no motion")
        self.assertEqual(unkeyed.issues, ())

    def test_a_delivery_whose_keys_cannot_be_read_says_so_in_its_plan(self):
        """Its takes' motion and ends are unchecked: said, never an empty answer read as "moves nothing"."""
        fbx = write_delivery(
            os.path.join(self.create_temp_dir(), "d.fbx"),
            keys={"A02": {"CABINET|DOOR_GRP": [0.0, 5.0]}},
        )
        broken = ValueError("a curve's key times and values differ in count")
        with unittest.mock.patch.object(ptk.FbxFile, "take_keys", side_effect=broken):
            index = DeliveryIndex.from_fbx(fbx)
        self.assertEqual((index.moved, index.jumps), ({}, ()))
        (issue,) = index.issues
        self.assertTrue(
            issue.startswith("d.fbx: the delivery's keys cannot be read (a curve's"), issue
        )
        self.assertEqual(DeliveryIndex.merged([index]).issues, index.issues, "kept by a merge")
        sheet = os.path.join(self.create_temp_dir(), "s.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write("Step,Step Contents,Asset Names\nA01,Go on,\n")
        plan = SheetPlan.from_sheet(sheet, index.with_suffixes(["GRP"]), mapping="default")
        self.assertIn(issue, plan.issues, "the plan carries it")

    def test_from_fbx_reads_paths_kinds_takes_and_descriptions(self):
        fbx = write_delivery(
            os.path.join(self.create_temp_dir(), "d.fbx"),
            shots=[{"clip": "A02", "description": "The door opens"}],
        )
        index = DeliveryIndex.from_fbx(fbx)
        self.assertEqual(index.name, "d.fbx")
        self.assertIn("CABINET|DOOR_GRP|DOOR_GEO", index.paths)
        self.assertNotIn("data_export", index.paths, "the carrier is no object")
        self.assertEqual(index.kinds["CABINET|DOOR_GRP|DOOR_GEO"], "Mesh")
        self.assertEqual(list(index.takes), TAKES)
        self.assertEqual(index.descriptions, {"A02": "The door opens"})

    def test_a_record_of_another_version_states_no_description(self):
        # Version 1 is the record before one take per shot: its shots are not
        # the takes, so a reader refuses it (SceneRecords.SHOTS).
        fbx = write_delivery(
            os.path.join(self.create_temp_dir(), "d.fbx"),
            shots=[{"clip": "A02", "description": "The door opens"}],
            version=1,
        )
        index = DeliveryIndex.from_fbx(fbx)
        self.assertEqual(list(index.takes), TAKES, "the takes are the file's own")
        self.assertEqual(index.descriptions, {})

    def test_a_carrier_named_as_an_import_or_a_module_names_it_is_no_node_and_its_record_is_read(
        self,
    ):
        """``data_export1`` (an import's copy) and ``NS:data_export`` (a module's) are carriers
        (``SceneStoreBase.is_carrier``): an exact name match missed both."""
        for carrier in ("data_export1", "data_export.001", "NS:data_export"):
            with self.subTest(carrier=carrier):
                fbx = write_delivery(
                    os.path.join(self.create_temp_dir(), "d.fbx"),
                    carrier=carrier,
                    shots=[{"clip": "A02", "description": "The door opens"}],
                )
                index = DeliveryIndex.from_fbx(fbx)
                self.assertFalse([p for p in index.paths if "data_export" in p])
                self.assertEqual(index.descriptions, {"A02": "The door opens"})

    def test_another_grouping_keeps_every_reading_of_the_delivery(self):
        index = DeliveryIndex(
            LAMP_NODES,
            ["T1"],
            "lamp.fbx",
            keyed={"S_1": [("LAMP", "highlight")]},
            aliases={"OLD": "LAMP"},
            moved={"T1": ["DESK|LAMP_NUL"]},
        )
        for other in (
            index.with_suffixes(["MSH", "NUL"]),
            index.subset(["DESK", "DESK|LAMP_NUL"]),
        ):
            self.assertEqual(
                (other.keyed, other.aliases, other.moved, other.takes),
                (index.keyed, index.aliases, index.moved, index.takes),
            )
        self.assertEqual(index.subset(["DESK"]).paths, ("DESK",))
        grouped = DeliveryIndex(LAMP_NODES, suffixes=["MSH"]).subset(LAMP_NODES)
        self.assertEqual(
            grouped.suffixes, ("MSH",), "a subset reads by the delivery's suffixes"
        )

    def test_merged_keeps_both_files_keyed_pairs_of_one_step(self):
        one = DeliveryIndex(["A"], keyed={"A01": [("DOOR", "highlight")]})
        two = DeliveryIndex(
            ["B"], keyed={"A01": [("LID", "fade_in")], "A02": [("LID", "highlight")]}
        )
        merged = DeliveryIndex.merged([one, two])
        self.assertEqual(
            merged.keyed,
            {
                "A01": (("door", "highlight"), ("lid", "fade_in")),
                "A02": (("lid", "highlight"),),
            },
        )

    def test_a_chain_is_one_object_its_top_node_standing_for_it(self):
        index = DeliveryIndex([p for p, _ in NODES])
        self.assertEqual(index.objects["DOOR"], ("CABINET|DOOR_GRP",))
        self.assertEqual(
            index.objects["LATCH_UPPER"],
            ("CABINET|CABINET_LATCHES_GRP|LATCH_UPPER_GRP",),
        )
        self.assertEqual(DeliveryIndex.object_name("PLUG_GEO_RIG_jnt_3"), "PLUG")
        self.assertEqual(index.find("key_geo"), ["TOOLS|KEY_GRP|KEY_GEO"])
        self.assertEqual(index.find("KEY"), ["TOOLS|KEY_GRP"])


class NodeMatcherTestCase(unittest.TestCase):
    PATHS = [
        "SET|LAMP_GRP",
        "SET|NEW_LAMP_GRP",
        "SET|SWITCH_UPPER",
        "SET|SWITCH_LOWER",
        "SET|PANEL",
        "SET|DOOR_RED",
        "SET|DOOR_GRAY",
    ]

    def setUp(self):
        self.matcher = NodeMatcher(DeliveryIndex(self.PATHS))

    def match(self, phrase, context=(), aliases=None):
        matcher = (
            NodeMatcher(DeliveryIndex(self.PATHS), aliases) if aliases else self.matcher
        )
        return matcher.match(phrase, context)

    def test_words_read_in_any_order_case_and_number_with_synonyms(self):
        self.assertEqual(self.match("the top switch").path, "SET|SWITCH_UPPER")
        self.assertEqual(self.match("Bottom Switches").path, "SET|SWITCH_LOWER")

    def test_what_the_step_shows_decides_a_vague_phrase_before_an_exact_name(self):
        self.assertEqual(self.match("the lamp").path, "SET|LAMP_GRP")
        self.assertEqual(
            self.match("the lamp", ["SET|NEW_LAMP_GRP"]).path, "SET|NEW_LAMP_GRP"
        )
        self.assertEqual(
            self.match("the door", ["SET|DOOR_GRAY"]).path, "SET|DOOR_GRAY"
        )

    def test_a_vague_phrase_without_context_is_a_tie_never_a_guess(self):
        found = self.match("the door")
        self.assertIsNone(found.path)
        self.assertEqual(sorted(found.candidates), ["DOOR_GRAY", "DOOR_RED"])

    def test_a_word_naming_another_thing_rules_out_an_object_lacking_it(self):
        self.assertEqual(
            self.match("the invisible lamp on the panel").issues(),
            ['no node is "the invisible lamp on the panel"'],
        )
        self.assertEqual(
            self.match("lamp on the panel").path,
            "SET|LAMP_GRP",
            "where it is is no part of its name",
        )

    def test_an_alias_names_what_no_rule_finds(self):
        self.assertEqual(
            self.match("the glowing thing", aliases={"glowing thing": "PANEL"}).path,
            "SET|PANEL",
        )

    def test_the_highlighted_area_is_the_one_object_the_step_shows(self):
        self.assertEqual(
            self.match("the highlighted area", ["SET|PANEL"]).path, "SET|PANEL"
        )
        self.assertIsNone(self.match("the highlighted area").path)

    def test_a_readings_referent_words_replace_the_matchers_own(self):
        """The words by which a phrase means what its step shows are vocabulary, as every other word is."""
        own = NodeMatcher(
            DeliveryIndex(self.PATHS), vocabulary={"referents": ["marked"]}
        )
        self.assertEqual(own.match("the marked area", ["SET|PANEL"]).path, "SET|PANEL")
        self.assertIsNone(
            own.match("the highlighted area", ["SET|PANEL"]).path,
            "the reading's words, not the matcher's own",
        )
        self.assertEqual(
            own.words("the marked panel"), frozenset({"panel"}), "a referent word names no part"
        )
        self.assertFalse(
            ReadingSpec.validate({"vocabulary": {"referents": "marked"}}).ok,
            "a list of words",
        )


class ClauseBookTestCase(unittest.TestCase):
    #: One row per built-in template, as a sheet writes it, and what it reads as: the template, its word, the
    #: settings it gives and its fields. A question's option is read as one (``options``).
    ROWS = [
        ("Selecting 'Next' triggers step advancement", "button_next", "button", {}, {"label": "Next", "to": "next"}),
        ("Selecting 'YES' triggers step advancement to step B02.5", "button_step", "button", {},
         {"label": "YES", "to": "B02.5"}),
        ("Selecting 'Exit' ends the task", "button_exit", "button", {}, {"label": "Exit", "to": "exit"}),
        ("Selecting 'Take Quiz' triggers advancement to the quiz", "button_quiz", "button", {}, {"label": "Take Quiz"}),
        ("Selecting the nut with wrench in hand triggers nut removal animation", "use", "use", {},
         {"targets": ["the nut"], "with": "wrench", "phrase": "nut removal"}),
        ("Pressing and holding the button plays Button Press Animation", "use_hold", "use", {"trigger": "hold"},
         {"targets": ["the button"], "phrase": "Button Press"}),
        ("Trigger Part install animation when moved close to empty slot", "use_near", "use", {"trigger": "near"},
         {"targets": ["empty slot"], "phrase": "Part install"}),
        ("Selecting the button triggers step advancement", "select", "select", {}, {"targets": ["the button"]}),
        ("Selecting the keys places them in hand", "take", "take", {}, {"targets": ["the keys"]}),
        ("Trigger Lid open animation", "play", "play", {}, {"phrase": "Lid open"}),
        ("Return Keys to table", "return", "return", {}, {"targets": ["Keys"]}),
        ("Screwdriver returns to hand", "note_in_hand", "note", {}, {}),
        ("Pip screen fades away", "picture_hide", "note", {}, {}),
        ("PiP screen appears showing the far side", "picture", "picture", {}, {"targets": ["the far side"]}),
        ("Highlighted bolt appears in place", "show_highlighted", "show", {}, {"targets": ["bolt"]}),
        ("Highlight Both Plugs", "highlight", "highlight", {}, {"targets": ["Both Plugs"]}),
        ("Status light turns on", "highlight_steady", "highlight", {"style": "steady"}, {"targets": ["Status light"]}),
        ("Lamp fades away", "hide", "hide", {}, {"targets": ["Lamp"]}),
        ("Marker fades in at the door", "show", "show", {}, {"targets": ["Marker"]}),
        ("Auto advance to step C05 when the number 42 is entered", "answer", "answer", {}, {"to": "C05", "label": "42"}),
        ("Auto Advance when both screws are removed", "advance", "advance", {}, {}),
        ("When any number other than 42 is entered advance to step C04.5", "answer_else", "answer", {},
         {"to": "C04.5", "label": "*"}),
        ("YES and NO buttons appear on the placard screen", "note_buttons", "note", {}, {"label": "YES and NO"}),
        ("Number pad with enter button appears on placard", "note_number_pad", "note", {}, {}),
        ("Checklist appears on placard with blank check boxes", "list", "list", {}, {}),
        ("Checklist item 'Lights' gets a check mark", "tick", "tick", {}, {"label": "Lights"}),
        ("User is teleported to the front of the cabinet", "travel", "travel", {}, {"where": "the front of the cabinet"}),
        ("The solenoid LED turns amber", "set", "set", {"state": "amber"}, {"targets": ["The solenoid LED"]}),
        ("The destination marker fades in", "ring_show", "note", {}, {}),
        ("The destination marker fades away", "ring_hide", "note", {}, {}),
        ("The checklist fades away", "list_hide", "note", {}, {}),
        ("-Bouncing arrows pointing at both screws", "indicate", "indicate", {}, {"targets": ["both screws"]}),
        ('Placard text reads "Select the door"', "note_page", "note", {}, {"label": "Select the door"}),
        ("Note: the room is quiet", "note", "note", {}, {}),
        ("* To reach the door", "option_right", "button", {}, {"label": "To reach the door", "to": "next"}),
        ("To free the key", "option", "button", {}, {"label": "To free the key", "to": "retry"}),
    ]
    #: The column each row is read from where it is not the contents, and the rows read as a question's.
    COLUMN = {"indicate": "hint", "note_page": "hint"}
    OPTIONS = ("option_right", "option")

    @classmethod
    def setUpClass(cls):
        cls.book = ClauseBook()

    def test_every_built_in_template_validates_and_reads_its_row(self):
        built_in = {name for name, _ in ClauseBook.load_templates()}
        self.assertEqual(built_in, {row[1] for row in self.ROWS}, "one sample row per template")
        for text, template, word, settings, fields in self.ROWS:
            with self.subTest(text=text):
                read = self.book.read(text, self.COLUMN.get(template, "contents"), options=template in self.OPTIONS)
                self.assertEqual(
                    (read[0].template, read[0].behavior, read[0].settings),
                    (template, word, settings),
                )
                self.assertEqual(read[0].fields, fields)

    def test_a_questions_options_are_read_in_a_question_alone(self):
        self.assertEqual(self.book.read("To free the key"), [], "outside a question no template reads it")
        self.assertEqual([c.template for c in self.book.read("Highlight the door", options=True)], ["option"],
                         "in a question, a row no other template reads")

    def test_a_travel_goes_where_it_says_not_where_it_faces(self):
        # "Facing the door" names what the user looks at; read as the destination,
        # it bound the door itself and put the user inside it.
        read = self.book.read(
            "User is teleported to the front of the cabinet.  Facing the open door."
        )
        self.assertEqual(read[0].behavior, "travel")
        self.assertEqual(
            read[0].fields,
            {"side": "front", "side_of": "the cabinet", "targets": ["the open door"]},
            "a side of the cabinet, not a viewpoint named 'the front of the cabinet'",
        )
        read = self.book.read("User is teleported to VIEW_FRONT, facing the red door")
        self.assertEqual(
            read[0].fields, {"where": "VIEW_FRONT", "targets": ["the red door"]}
        )

    def test_the_format_s_phrasebook_reads_every_phrase(self):
        rows = [
            (
                "Selecting the top lock triggers Top Lock Open Animation with the keys in hand",
                "use",
                {
                    "targets": ["the top lock"],
                    "phrase": "Top Lock Open",
                    "with": "keys",
                },
            ),
            (
                "Moving the solenoid close to the bracket triggers Solenoid Install Animation",
                "use",
                {
                    "with": "solenoid",
                    "targets": ["bracket"],
                    "phrase": "Solenoid Install",
                },
            ),
            ("A number pad appears on the placard", "note", {}),
            ("Entering '11' triggers step C11", "answer", {"label": "11", "to": "C11"}),
            (
                "Entering any other number triggers step C10.5",
                "answer",
                {"to": "C10.5", "label": "*"},
            ),
            (
                "A checklist appears on the placard: 'Lights', 'Power'",
                "list",
                {"label": "Lights, Power"},
            ),
            (
                "'YES' and 'NO' buttons appear on the placard",
                "note",
                {"label": "YES and NO"},
            ),
            ("The destination marker fades in at the door", "note", {}),
            ("Destination circle fades in", "note", {}),
            ("Destination circle fades in at teleport destination", "note", {}),
            ("Destination circle fades away", "note", {}),
        ]
        for text, behavior, fields in rows:
            with self.subTest(text=text):
                read = self.book.read(text)
                self.assertEqual((read[0].behavior, read[0].fields), (behavior, fields))

    def test_an_exit_ends_the_task_or_the_module_never_a_training(self):
        """The exit phrase read "ends the training", a word no sheet of the format uses. Added: 2026-11-26"""
        book = ClauseBook()
        for ends in ("task", "module"):
            with self.subTest(ends=ends):
                self.assertEqual([(c.behavior, c.fields.get("to")) for c in book.read("Selecting 'Exit' ends the %s" % ends)],
                                 [("button", "exit")])
        self.assertEqual(book.read("Selecting 'Exit' ends the training"), [])

    def test_show_is_also_a_highlight(self):
        self.assertEqual(
            [c.behavior for c in self.book.read("Highlighted lid appears in place")],
            ["show", "highlight"],
        )

    def test_a_template_reads_only_its_column_or_either_when_any(self):
        hint = ClauseSpec(
            word="note", detect=[r"^tip:\s+(?P<text>.+)$"], column="hint", order=5
        )
        either = ClauseSpec(
            word="indicate",
            detect=[r"^arrow at\s+(?P<targets>.+)$"],
            column="any",
            order=6,
        )
        book = ClauseBook([("tip", hint), ("pointer", either)])
        self.assertEqual(
            book.read("tip: look up"), [], "a hint template does not read the contents"
        )
        self.assertEqual(len(book.read("tip: look up", "hint")), 1)
        self.assertEqual(
            [len(book.read("arrow at the lid", c)) for c in ("contents", "hint")],
            [1, 1],
        )

    def test_a_project_template_reads_its_own_wording(self):
        own = ClauseSpec(
            word="use", detect=[r"^press\s+(?P<targets>.+)$"], clip=True, order=5
        )
        book = ClauseBook([("press", own)])
        self.assertEqual(
            book.read("Press the red button")[0].fields, {"targets": ["the red button"]}
        )
        self.assertTrue(book.read("Press the red button")[0].clip)

    def test_a_template_names_a_word_of_the_sheet_and_the_settings_it_takes(self):
        """No other name reaches a plan: a template's word is the sheet's (or ``advance``), each setting it gives one its
        word takes, at a value of it."""
        self.assertFalse(ClauseSpec.validate({"word": "teleport", "detect": ["^x$"]}).ok, "no word of the sheet")
        self.assertFalse(ClauseSpec.validate({"behavior": "use", "detect": ["^x$"]}).ok)
        self.assertTrue(ClauseSpec.validate({"word": "advance", "detect": ["^x$"]}).ok)
        for settings in ({"useTrigger": "HoldToPlay"}, {"trigger": "press"}):
            with self.subTest(settings=settings), self.assertRaisesRegex(ValueError, "template 'own'"):
                ClauseBook([("own", ClauseSpec(word="use", detect=["^x$"], settings=settings))])

    def test_the_roles_are_declared_by_the_templates(self):
        declared = {name: set(spec.roles) for name, spec in ClauseBook.load_templates()}
        self.assertEqual(declared["take"], {"takes"})
        self.assertEqual(self.book.roles("answer"), {"advances", "entered"})
        self.assertEqual(
            self.book.roles("answer", fields={"label": "*"}),
            {"advances", "entered", "otherwise"},
            "a role declared with fields belongs to the rows carrying them",
        )
        self.assertEqual(self.book.roles("use", {"trigger": "near"}), {"uses", "places"})
        self.assertEqual(self.book.roles("note"), set(), "a template's own roles belong to the rows it reads alone")
        (buttons,) = self.book.read("YES and NO buttons appear on the placard")
        self.assertEqual(self.book.roles_of(buttons), {"buttons"})

    def test_a_projects_own_template_is_an_action_word_only_as_its_word(self):
        from unitytk.sheets._row_reading import _RowReading

        own = ClauseSpec(
            word="highlight", detect=[r"^inspect\s+(?P<targets>.+)$"], order=5
        )
        reading = _RowReading(
            DeliveryIndex(["UNIT", "UNIT|LID"]),
            ClauseBook([("inspect", own)]),
            ReadingSpec.load(),
            None,
        )
        self.assertEqual(reading.actions.get("highlight"), ("highlight", {}))
        self.assertEqual(
            (reading.actions.get("inspect"), reading.actions.get("note"), reading.actions.get("press and hold")),
            (None, None, None),
            "a template's name is no word; a book without note or use_hold has no word for them",
        )
        built_in = _RowReading(
            DeliveryIndex(["UNIT"]), ClauseBook(), ReadingSpec.load(), None
        )
        self.assertEqual(
            built_in.actions.get("press and hold"),
            ("use", {"trigger": "hold"}),
            "the word reads as its reading: a use held",
        )
        self.assertEqual(
            built_in.actions.get("note"),
            ("note", {}),
            "every word the book reads is a word",
        )
        self.assertIsNone(built_in.actions.get("teleport"), "no older name is read")


class ProjectClausesTestCase(UnityTkTestCase):
    """A project's own clause templates, in a folder beside its settings file: never a per-user one."""

    def test_project_templates_win_a_built_in_of_their_name_and_change_the_digest(self):
        folder = os.path.join(self.create_temp_dir(), "clauses")
        os.makedirs(folder)
        with open(os.path.join(folder, "button_next.json"), "w", encoding="utf-8") as fh:
            json.dump(
                {
                    "word": "button",
                    "detect": ["^press\\s+'(?P<label>[^']+)'\\s+to\\s+go\\s+on$"],
                    "fields": {"to": "next"},
                    "roles": ["advances"],
                },
                fh,
            )
        own = ClauseBook(folder=folder)
        self.assertEqual(own.read("Press 'Next' to go on")[0].fields, {"label": "Next", "to": "next"})
        self.assertEqual(ClauseBook().read("Press 'Next' to go on"), [])
        self.assertNotEqual(own.digest(), ClauseBook().digest())
        self.assertEqual(
            ClauseBook(folder=os.path.join(folder, "absent")).digest(),
            ClauseBook().digest(),
            "no folder: the built-in ones alone, wherever the reader runs",
        )

    def test_a_per_user_folder_never_shadows_a_built_in(self):
        root = self.create_temp_dir()
        user = os.path.join(root, "unitytk", "sheet_clauses")
        os.makedirs(user)
        with open(os.path.join(user, "button_next.json"), "w", encoding="utf-8") as fh:
            json.dump({"word": "button", "detect": ["^go on$"]}, fh)
        built_in = ClauseBook().digest()
        with unittest.mock.patch.dict(os.environ, {ptk.UserConfig.CONFIG_ROOT_ENV_VAR: root}):
            self.assertEqual(ClauseBook().read("Go on"), [], "no project: the built-ins alone")
            self.assertEqual(ClauseBook().digest(), built_in)

    def test_no_project_has_no_second_tier_and_nothing_is_written(self):
        """With no project the built-in folder is read alone, as a folder of the package: never a tier written to."""
        root = self.create_temp_dir()
        shipped = os.path.join(root, "clauses")  # the package's folder, under a config root that holds it
        os.makedirs(shipped)
        with open(os.path.join(shipped, "go.json"), "w", encoding="utf-8") as fh:
            json.dump({"word": "button", "detect": ["^go on$"]}, fh)
        env = {ptk.UserConfig.CONFIG_ROOT_ENV_VAR: root}
        with unittest.mock.patch.dict(os.environ, env), unittest.mock.patch.object(
            ClauseBook, "BUILTIN_DIR", Path(shipped)
        ):
            book = ClauseBook()
            self.assertEqual(book.names, ["go"])
            self.assertIsNone(book.folder, "no project folder")
            self.assertEqual(book.digest(), "", "no project's templates")
        self.assertEqual(os.listdir(shipped), ["go.json"], "nothing written beside the built-ins")
        self.assertEqual(sorted(os.listdir(root)), ["clauses"], "nor anywhere under the config root")

    #: A fixed folder of a project's templates (one file with CRLF line endings) and its digest: the C# staleness
    #: test (ManifestsActionsTests.An_edited_clause_template_of_the_project_makes_its_plan_stale) holds the same bytes,
    #: which a digest reads as bytes alone, never as templates.
    FIXED_CLAUSES = {
        "go.json": '{"word": "button",\r\n "detect": ["^go on$"]}\r\n',
        "stop.json": '{"word": "button", "detect": ["^stop$"]}\n',
    }
    FIXED_DIGEST = "4973bc23b2555913d22f2c98ab3b3fb764f5bc3334f0b12a2a421df68049af90"

    def test_the_digest_of_a_fixed_folder_is_the_golden_one(self):
        folder = os.path.join(self.create_temp_dir(), "clauses")
        os.makedirs(folder)
        for name, text in self.FIXED_CLAUSES.items():
            with open(os.path.join(folder, name), "w", encoding="utf-8", newline="") as fh:
                fh.write(text)
        self.assertEqual(ClauseBook.folder_digest(folder), self.FIXED_DIGEST)


class RolesTestCase(UnityTkTestCase):
    """A project's own template takes part in the reading by the roles it declares."""

    def test_a_projects_own_template_takes_part_by_the_roles_it_declares(self):
        sheet = os.path.join(self.create_temp_dir(), "roles.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write(
                "Step,Step Contents,Asset Names\nA01,Grab the key,\nA02,Selecting the top latch triggers Latch Animation,\n"
            )
        grab = ClauseSpec(
            word="take",
            detect=[r"^grab\s+(?P<targets>.+)$"],
            order=5,
            roles=["takes"],
        )
        book = ClauseBook([("grab", grab)] + ClauseBook.load_templates())
        plan = SheetPlan.from_sheet(
            sheet,
            DeliveryIndex([p for p, _ in NODES], ["A02"], "d.fbx"),
            mapping="default",
            book=book,
        )
        use = plan.steps[1].actions[0]
        self.assertEqual(
            (use.behavior, use.field("with")),
            ("use", "TOOLS|KEY_GRP"),
            "the grabbed key is in hand",
        )
        self.assertFalse(
            ClauseSpec.validate(
                {"word": "take", "detect": ["^x$"], "roles": ["juggles"]}
            ).ok
        )


if __name__ == "__main__":
    unittest.main()
