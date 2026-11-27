# !/usr/bin/python
# coding=utf-8
"""Tests for ``unitytk.sheets.SheetSettings``: a task's settings file (its sheet, template,
deliveries, name patterns, renamed nodes and retired step IDs), the delivery its FBX files make
together, and the plan and check it reads."""

import hashlib
import json
import os
import struct
import sys

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

from test_sheet_plan import write_delivery

from unitytk.sheets import SheetSettings, DeliveryIndex, SheetCheck, SheetPlan

SHEET = """Step,Step Contents,Asset Names,Animation Clip Names
A01,"User is teleported to USER_POS_1, facing the door","USER_POS_1
DOOR",
A02,Selecting the old door triggers Door Animation,OLD_DOOR,
A03,Selecting 'Back' triggers step A05,,
"""


def write_glb(path, nodes, animations=()):
    """A GLB of *nodes* (``[(name, [child indices])]``) and named *animations*, no binary chunk."""
    gltf = {
        "asset": {"version": "2.0"},
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": n, **({"children": c} if c else {})} for n, c in nodes],
        "animations": [{"name": a, "channels": [], "samplers": []} for a in animations],
    }
    body = json.dumps(gltf).encode("utf-8")
    body += b" " * (-len(body) % 4)
    with open(path, "wb") as fh:
        fh.write(
            struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(body))
            + struct.pack("<I4s", len(body), b"JSON")
            + body
        )
    return path


class DeliveryIndexMergedTestCase(UnityTkTestCase):
    def test_a_glb_delivery_names_its_nodes_by_path_and_its_animations_as_takes(self):
        glb = write_glb(
            os.path.join(self.create_temp_dir(), "views.glb"),
            [("VIEWS", [1, 2]), ("USER_POS_1", []), ("USER_POS_2", [])],
            ["Bounce"],
        )
        index = DeliveryIndex.from_file(glb)
        self.assertEqual(
            (index.paths, index.takes, index.name),
            (
                ("VIEWS", "VIEWS|USER_POS_1", "VIEWS|USER_POS_2"),
                ("Bounce",),
                "views.glb",
            ),
        )

    def test_merged_holds_every_files_nodes_and_takes_in_order(self):
        a = DeliveryIndex(
            ["CABINET", "CABINET|DOOR_GRP"],
            ["A01", "A02"],
            "a.fbx",
            steps={"A01": "A01"},
            moved={"A01": ["CABINET|DOOR_GRP"]},
        )
        b = DeliveryIndex(["VIEWS", "VIEWS|USER_POS_1"], ["B01"], "b.fbx")
        merged = DeliveryIndex.merged([a, b])
        self.assertEqual(merged.paths, a.paths + b.paths)
        self.assertEqual(merged.takes, ("A01", "A02", "B01"))
        self.assertEqual(
            (merged.name, merged.steps, merged.moved),
            ("a.fbx, b.fbx", {"A01": "A01"}, {"A01": ("CABINET|DOOR_GRP",)}),
        )
        self.assertEqual(merged.find("USER_POS_1"), ["VIEWS|USER_POS_1"])

    def test_a_projects_delivery_keeps_what_each_node_wears_and_its_lights_states(self):
        """``DeliveryIndex.merged`` left out ``worn`` and ``states``, and a project's delivery is always merged
        (``SheetSettings.index``): ``sheet.check settings=``, ``sheet.sync`` and the Manifest window's read-again
        never saw a light's states. Added: 2026-11-26"""
        from unittest import mock

        folder = self.create_temp_dir()
        path = os.path.join(folder, "task.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump({"version": 1, "sheet": "task.csv", "deliveries": ["a.fbx"], "extras": ["b.fbx"]}, fh)
        files = {
            "a.fbx": DeliveryIndex(["CABINET", "CABINET|LAMP_GRP"], [], "a.fbx", worn={"CABINET|LAMP_GRP": ["AC:lamp_mat"]},
                                   states={"lamp_mat": ["off", "amber"]}),
            "b.fbx": DeliveryIndex(["PANEL", "PANEL|LED_GRP"], [], "b.fbx", worn={"PANEL|LED_GRP": ["led_mat"]},
                                   states={"led_mat": ["off", "green"]}),
        }
        with mock.patch.object(DeliveryIndex, "from_file", side_effect=lambda p: files[os.path.basename(p)]):
            index = SheetSettings.load(path).index()
        self.assertEqual((index.states_of("CABINET|LAMP_GRP"), index.states_of("PANEL|LED_GRP")),
                         (["off", "amber"], ["off", "green"]))

    def test_a_glb_delivery_is_read_by_the_plan_and_the_check(self):
        """``SheetPlan.from_sheet`` and ``sheet.check delivery=`` read every delivery as an FBX, so a GLB failed in
        the FBX reader (a settings file's read either). Added: 2026-11-26"""
        folder = self.create_temp_dir()
        glb = write_glb(os.path.join(folder, "d.glb"), [("CABINET", [1]), ("DOOR_GRP", [])])
        sheet = os.path.join(folder, "s.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write("Step,Step Name,Voice,Action,Target,Settings\nA01,Look,Look.,,,\n,,,highlight,DOOR_GRP,\n")
        plan = SheetPlan.from_sheet(sheet, glb, mapping="interactive")
        self.assertEqual((plan.steps[0].actions[0].targets, plan.issues), (["CABINET|DOOR_GRP"], []))
        self.assertEqual(SheetCheck.check(sheet=sheet, delivery=glb)["items"], [])

    def test_an_authored_arrow_is_found_by_the_part_it_points_at_its_steps_first(self):
        index = DeliveryIndex(
            [
                "CABINET",
                "CABINET|DOOR_GRP",
                "CABINET|KEY_GRP",
                "ARROWS",
                "ARROWS|DOOR_ARROW_GRP",
                "ARROWS|DOOR_ARROW_GRP|DOOR_ARROW",
                "ARROWS|A01_KEY_ARROW",
                "ARROWS|B01_KEY_ARROW",
            ]
        )
        arrow = "^(?P<part>.+)_ARROW$"
        self.assertEqual(
            index.arrows_at("CABINET|DOOR_GRP", arrow),
            ["ARROWS|DOOR_ARROW_GRP"],
            "the chain's top",
        )
        self.assertEqual(
            index.arrows_at("CABINET|DOOR_GRP"),
            [],
            "no pattern stated: no arrow is known",
        )
        stepped = r"^(?P<step>[A-Z]+\d+(?:_\d+)?)_(?P<part>.+?)_ARROW$"
        self.assertEqual(
            index.arrows_at("CABINET|KEY_GRP", stepped, "B01"), ["ARROWS|B01_KEY_ARROW"]
        )
        self.assertEqual(index.arrows_at("CABINET|LID", arrow), [])

    def test_a_projects_build_suffixes_group_its_nodes_into_objects(self):
        index = DeliveryIndex(
            ["UNIT", "UNIT|DOOR_MSH", "UNIT|DOOR_MSH|DOOR_NULL"],
            suffixes=["MSH", "NULL"],
        )
        self.assertEqual(index.find("DOOR"), ["UNIT|DOOR_MSH"])
        self.assertEqual(index.name_of("UNIT|DOOR_MSH"), "DOOR")
        self.assertEqual(
            DeliveryIndex(["UNIT|DOOR_MSH"]).find("DOOR"), [], "not a default suffix"
        )

    def test_a_renamed_nodes_old_name_finds_its_new_one(self):
        index = DeliveryIndex(
            ["CABINET", "CABINET|DOOR_GRP"], aliases={"OLD_DOOR": "DOOR"}
        )
        self.assertEqual(index.find("OLD_DOOR"), ["CABINET|DOOR_GRP"])
        self.assertEqual(
            index.find("old_door"), ["CABINET|DOOR_GRP"], "as any name, ignoring case"
        )


class SheetSettingsTestCase(UnityTkTestCase):
    def setUp(self):
        super().setUp()
        self.dir = self.create_temp_dir()
        with open(
            os.path.join(self.dir, "task.csv"), "w", encoding="utf-8", newline=""
        ) as fh:
            fh.write(SHEET)
        write_delivery(os.path.join(self.dir, "assembly.fbx"), takes=["A02"])
        write_delivery(
            os.path.join(self.dir, "views.fbx"),
            nodes=[("VIEWS", "Null"), ("VIEWS|USER_POS_1", "Null")],
            takes=["USER_POS_BOB"],
        )
        self.path = os.path.join(self.dir, "task.json")
        with open(self.path, "w", encoding="utf-8") as fh:
            json.dump(
                {
                    "version": 1,
                    "sheet": "task.csv",
                    "mapping": "interactive",
                    "deliveries": ["assembly.fbx"],
                    "extras": ["views.fbx"],
                    "names": {"viewpoint": "^(VIEW_|USER_POS_)"},
                    "aliases": {"OLD_DOOR": "DOOR"},
                    "retired": ["A05"],
                },
                fh,
            )
        self.task = SheetSettings.load(self.path)

    def test_the_settings_read_their_files_beside_them(self):
        self.assertEqual(self.task.sheet_path(), os.path.join(self.dir, "task.csv"))
        index = self.task.index()
        self.assertEqual(index.name, "assembly.fbx, views.fbx")
        self.assertIn("VIEWS|USER_POS_1", index.paths)
        self.assertEqual(
            index.takes,
            ("A02",),
            "an extra file's nodes join the delivery, its takes no step plays",
        )

    def test_the_plan_reads_the_tasks_patterns_and_renamed_nodes(self):
        steps = {s.id: s.actions[0] for s in self.task.plan().steps}
        teleport = steps["A01"]
        self.assertEqual(
            (teleport.targets, teleport.field("facing"), teleport.issues),
            (["VIEWS|USER_POS_1"], "CABINET|DOOR_GRP", []),
            "USER_POS_ is this task's viewpoint",
        )
        self.assertEqual(
            (steps["A02"].targets, steps["A02"].clip, steps["A02"].issues),
            (["CABINET|DOOR_GRP"], "A02", []),
            "OLD_DOOR is DOOR renamed",
        )

    def test_the_check_reports_a_jump_to_a_retired_step(self):
        check = self.task.check()
        retired = [i for i in check.items if "retired" in i.reason]
        self.assertEqual(
            [(i.step, i.row, i.severity) for i in retired], [("A03", 1, "error")]
        )
        self.assertTrue(retired[0].fix)
        self.assertFalse(
            [i for i in check.items if "has no step" in i.reason],
            "the reading knows the step is retired: said once, as that",
        )
        jump = next(a for s in self.task.plan().steps for a in s.actions if a.to == "A05")
        self.assertEqual(
            [SheetPlan.parse_issue(i)["reason"] for i in jump.issues],
            ["the jump goes to step A05, which is retired"],
            "the plan the project builds by says so too",
        )

    def test_a_step_still_carrying_a_retired_id_is_an_error(self):
        self.task.retired = ["A02"]
        retired = [i for i in self.task.check().items if "retired" in i.reason]
        self.assertEqual([(i.step, i.severity) for i in retired], [("A02", "error")])

    def test_a_tolerant_template_stays_tolerant_under_the_projects_patterns(self):
        self.task.mapping = "default"
        reading = self.task.reading()
        self.assertEqual(
            (reading.strict, reading.names["viewpoint"]), (False, "^(VIEW_|USER_POS_)")
        )
        self.assertFalse(self.task.check().strict, "its check reports and passes")

    def test_a_readings_suffixes_reach_the_delivery(self):
        sheet = os.path.join(self.dir, "suffixed.csv")
        with open(sheet, "w", encoding="utf-8", newline="") as fh:
            fh.write("Step,Step Contents,Asset Names\nA01,Highlight the door,DOOR\n")
        from unitytk.sheets import SheetPlan

        plan = SheetPlan.from_sheet(
            sheet,
            DeliveryIndex(["UNIT", "UNIT|DOOR_MSH"]),
            reading={"names": {"suffixes": ["MSH"]}},
        )
        self.assertEqual(
            (plan.steps[0].actions[0].targets, plan.steps[0].actions[0].issues),
            (["UNIT|DOOR_MSH"], []),
        )

    def test_a_projects_clause_templates_beside_its_file_read_its_sheet_and_the_plan_says_so(
        self,
    ):
        """A project's own wording lives beside its settings file (``clauses/``), so its sheet reads the same
        on every machine, and the plan's digest of its templates changes with them."""
        before = self.task.plan().source.clauses_digest
        os.makedirs(os.path.join(self.dir, "clauses"))
        with open(
            os.path.join(self.dir, "clauses", "back.json"), "w", encoding="utf-8"
        ) as fh:
            json.dump(
                {
                    "behavior": "go_back",
                    "detect": [
                        "^selecting\\s+'(?P<label>[^']+)'\\s+triggers\\s+step\\s+(?P<to>{step})$"
                    ],
                    "order": 1,
                    "roles": ["advances"],
                },
                fh,
            )
        plan = self.task.plan()
        back = plan.steps[2].actions[0]
        self.assertEqual(
            (back.behavior, back.field("label"), back.to), ("go_back", "Back", "A05")
        )
        self.assertTrue(
            plan.source.clauses_digest and plan.source.clauses_digest != before
        )
        self.assertEqual(plan.source.clauses_digest, self.task.book().digest())
        # What a reader with the folder in hand makes again (the manifest's Stale): a line per file, its
        # name and its own digest, line endings aside.
        path = os.path.join(self.dir, "clauses", "back.json")
        with open(path, "rb") as fh:
            body = fh.read()
        line = "back.json " + hashlib.sha256(body.replace(b"\r\n", b"\n")).hexdigest()
        self.assertEqual(
            (plan.source.clauses, plan.source.clauses_digest),
            ("clauses", hashlib.sha256(line.encode("utf-8")).hexdigest()),
        )
        with open(path, "wb") as fh:
            fh.write(body.replace(b"\n", b"\r\n"))
        self.assertEqual(self.task.plan().source.clauses_digest, plan.source.clauses_digest)

    def test_the_name_rules_are_validated_and_a_suffix_list_reaches_the_reading(self):
        self.task.names = {"suffixes": ["MSH"], "viewpoint": "^(VIEW_|USER_POS_)"}
        self.assertEqual(self.task.reading().names["suffixes"], ["MSH"])
        for names in ({"suffixes": "MSH"}, {"viewpoint": "("}, {"arrows": "^A_"}):
            with self.subTest(names=names):
                with open(self.path, "w", encoding="utf-8") as fh:
                    json.dump({"version": 1, "sheet": "task.csv", "names": names}, fh)
                with self.assertRaisesRegex(ValueError, "names"):
                    SheetSettings.load(self.path)

    def test_each_kits_data_is_carried_into_the_plan(self):
        self.task.extensions = {"task": {"tools": {"KEY": {"travel": False}}}}
        self.assertEqual(
            self.task.plan().extensions,
            {"task": {"tools": {"KEY": {"travel": False}}}},
        )

    def test_the_operation_checks_a_task_by_its_settings_file(self):
        out = SheetCheck.check(settings=self.path)
        self.assertTrue(
            any("retired" in i["reason"] for i in out["items"]), out["report"]
        )
        with self.assertRaisesRegex(ValueError, "settings="):
            SheetCheck.check()

    def test_an_alias_cycle_is_refused_naming_it(self):
        """``{"A": "B", "B": "A"}`` recursed until Python gave up, wherever a name was looked up. Added: 2026-11-26"""
        with open(self.path, "w", encoding="utf-8") as fh:
            json.dump({"version": 1, "sheet": "task.csv", "aliases": {"A": "B", "b": "A", "OLD": "NEW"}}, fh)
        with self.assertRaisesRegex(ValueError, r"aliases: A -> B -> A goes round in a cycle: map each old name to a name the delivery has"):
            SheetSettings.load(self.path)

    def test_a_file_of_another_version_is_refused_naming_the_fix(self):
        with open(self.path, "w", encoding="utf-8") as fh:
            json.dump({"version": 9, "sheet": "task.csv"}, fh)
        with self.assertRaisesRegex(ValueError, "version"):
            SheetSettings.load(self.path)

    def test_a_key_nothing_reads_is_refused_naming_where_a_kits_data_goes(self):
        # A real task's file kept its tool fits under a top-level "tools", which nothing read: the screwdriver
        # worked sideways and no check said why.
        with open(self.path, "w", encoding="utf-8") as fh:
            json.dump({"version": 1, "sheet": "task.csv", "tools": {"WRENCH": {"tip": [0, 0, 0.1]}}, "_note": "kept"}, fh)
        with self.assertRaisesRegex(ValueError, r"'tools'.*extensions"):
            SheetSettings.load(self.path)
        with open(self.path, "w", encoding="utf-8") as fh:
            json.dump({"version": 1, "sheet": "task.csv", "_note": "kept",
                       "extensions": {"task": {"tools": {"WRENCH": {"tip": [0, 0, 0.1]}}}}}, fh)
        self.assertEqual(SheetSettings.load(self.path).extensions["task"]["tools"]["WRENCH"]["tip"], [0, 0, 0.1])
