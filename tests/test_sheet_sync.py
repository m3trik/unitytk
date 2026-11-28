# !/usr/bin/python
# coding=utf-8
"""Tests for ``unitytk.sheets.SheetSync``: a sheet's settings file staged into a Unity project (its
plan, its delivery's files and its sounds), only what differs written, and the plan stamped with
what it was read from."""

import hashlib
import json
import os
import sys
from pathlib import Path
from unittest import mock

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

import pythontk as ptk

from test_sheet_plan import write_delivery

from unitytk.sheets import SheetPlan, SheetSync

SHEET = """Step,Step Contents,Asset Names,Animation Clip Names
A01,Selecting the door triggers Door open Animation,DOOR_GRP,A01
A02,Selecting 'Next' triggers step advancement,,
"""


class SheetSyncTestCase(UnityTkTestCase):
    def setUp(self):
        super().setUp()
        work = Path(self.create_temp_dir())
        self.source = work / "sheets"
        self.source.mkdir()
        (self.source / "door.csv").write_text(SHEET, encoding="utf-8", newline="")
        write_delivery(
            str(self.source / "cabinet.fbx"),
            nodes=[("CABINET", "Null"), ("CABINET|DOOR_GRP", "Null")],
            takes=["A01"],
        )
        self.settings = self.source / "door.json"
        self.settings.write_text(
            json.dumps(
                {
                    "version": 1,
                    "sheet": "door.csv",
                    "mapping": "interactive",
                    "deliveries": ["cabinet.fbx"],
                }
            ),
            encoding="utf-8",
        )
        self.project = work / "Project"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()

    def test_a_sync_stages_the_plan_and_the_delivery_and_a_second_writes_nothing(self):
        result = SheetSync.sync(str(self.settings), str(self.project))
        folder = self.project / "Assets" / "door"
        self.assertEqual(Path(result["folder"]), folder)
        self.assertEqual(
            sorted(p.name for p in folder.iterdir()), ["cabinet.fbx", "door.plan.json"]
        )
        self.assertEqual(
            (
                result["steps"],
                result["issues"],
                result["unchanged"],
                result["missing_files"],
            ),
            (2, 0, 0, []),
        )
        self.assertEqual(
            (folder / "cabinet.fbx").read_bytes(),
            (self.source / "cabinet.fbx").read_bytes(),
        )

        plan = SheetPlan.load(result["plan"])
        self.assertEqual([a.clip for a in plan.actions() if a.clip], ["A01"])
        self.assertEqual(
            plan.source.sheet_digest, hashlib.sha256(SHEET.encode("utf-8")).hexdigest()
        )
        self.assertEqual(
            plan.source.settings_digest,
            hashlib.sha256(self.settings.read_bytes()).hexdigest(),
        )
        (self.source / "door.csv").write_bytes(
            SHEET.replace("\n", "\r\n").encode("utf-8")
        )
        self.assertEqual(
            SheetPlan.digest(str(self.source / "door.csv")),
            plan.source.sheet_digest,
            "a checkout's line endings are no change",
        )
        (self.source / "door.csv").write_text(SHEET, encoding="utf-8", newline="")
        self.assertEqual(
            plan.source.settings,
            "../sheets/door.json",
            "how the project reaches the settings file",
        )

        again = SheetSync.sync(str(self.settings), str(self.project))
        self.assertEqual(
            (again["written"], again["unchanged"]),
            ([], 2),
            "what is there already is left alone",
        )

    def test_a_changed_sheet_rewrites_the_plan_alone_and_a_dry_run_only_plans_it(self):
        SheetSync.sync(str(self.settings), str(self.project))
        plan_file = self.project / "Assets" / "door" / "door.plan.json"
        before = plan_file.read_bytes()
        (self.source / "door.csv").write_text(
            SHEET.replace("Door open", "Door close"), encoding="utf-8", newline=""
        )

        dry = ptk.OpContext(dry_run=True)
        planned = SheetSync.sync(str(self.settings), str(self.project), ctx=dry)
        self.assertEqual(
            planned["written"], [str(plan_file)], "the delivery did not change"
        )
        self.assertEqual(plan_file.read_bytes(), before, "a dry run writes nothing")
        self.assertEqual([c.kind for c in dry.changes], ["write"])

        SheetSync.sync(str(self.settings), str(self.project))
        self.assertIn("Door close", plan_file.read_text(encoding="utf-8"))

    def test_a_folder_is_named_under_assets_and_a_folder_that_is_no_project_is_refused(
        self,
    ):
        result = SheetSync.sync(
            str(self.settings), str(self.project), folder="Assets/Tasks/Door"
        )
        self.assertTrue(
            (self.project / "Assets" / "Tasks" / "Door" / "door.plan.json").is_file()
        )
        self.assertEqual(Path(result["plan"]).name, "door.plan.json")
        with self.assertRaisesRegex(ValueError, "not under the project's Assets"):
            SheetSync.sync(str(self.settings), str(self.project), folder="Library/Door")
        with self.assertRaisesRegex(ValueError, "is no Unity project"):
            SheetSync.sync(str(self.settings), str(self.source))

    def test_a_steps_sound_is_staged_at_its_path_and_one_outside_the_folder_is_refused(
        self,
    ):
        settings = json.loads(self.settings.read_text(encoding="utf-8"))
        settings["options"] = {"audio": "narrate"}
        self.settings.write_text(json.dumps(settings), encoding="utf-8")
        (self.source / "door.csv").write_text(
            "Step,Step Contents,Asset Names,Animation Clip Names,Voice Support,Audio Files\n"
            "A01,Selecting the door triggers Door open Animation,DOOR_GRP,A01,Open the door.,a01_take.wav\n"
            "A02,Selecting 'Next' triggers step advancement,,,Go on.,\n",
            encoding="utf-8",
            newline="",
        )
        (self.source / "recordings").mkdir()
        (self.source / "recordings" / "a01_take.wav").write_bytes(b"RIFF")

        result = SheetSync.sync(str(self.settings), str(self.project))
        staged = self.project / "Assets" / "door" / "recordings" / "a01_take.wav"
        self.assertEqual(
            staged.read_bytes(),
            b"RIFF",
            "the sound at its own path under the staged folder",
        )
        self.assertEqual(
            SheetPlan.load(result["plan"]).steps[0].field("sound"),
            "recordings/a01_take.wav",
        )

        read = SheetPlan.from_sheet

        def outside(*args, **kwargs):
            plan = read(*args, **kwargs)
            plan.steps[0].fields["sound"] = (
                "../elsewhere/a01_take.wav"  # as a sound beside another folder would read
            )
            return plan

        (self.source / "door.csv").write_text(
            SHEET.replace("Door open", "Door ajar"), encoding="utf-8", newline=""
        )
        before = {
            p: p.read_bytes()
            for p in (self.project / "Assets").rglob("*")
            if p.is_file()
        }
        with mock.patch.object(SheetPlan, "from_sheet", outside):
            with self.assertRaisesRegex(
                ValueError, "lies outside the settings file's folder"
            ):
                SheetSync.sync(str(self.settings), str(self.project))
        self.assertEqual(
            {
                p: p.read_bytes()
                for p in (self.project / "Assets").rglob("*")
                if p.is_file()
            },
            before,
            "a refusal writes nothing, though the plan had changed",
        )

    def test_a_voiced_step_with_no_sound_yet_is_said_with_the_render_that_makes_it(self):
        # Measured 2026-11-25 on a private task: every narration file was keyed by an engine pin since bumped, so
        # every voice line read as not narrated, and staging made a silent task without a word.
        settings = json.loads(self.settings.read_text(encoding="utf-8"))
        settings["options"] = {"audio": "narrate"}
        self.settings.write_text(json.dumps(settings), encoding="utf-8")
        (self.source / "door.csv").write_text(
            "Step,Step Contents,Asset Names,Animation Clip Names,Voice Support\n"
            "A01,Selecting the door triggers Door open Animation,DOOR_GRP,A01,Open the door.\n"
            "A02,Selecting 'Next' triggers step advancement,,,\n",
            encoding="utf-8",
            newline="",
        )
        ctx = ptk.OpContext()
        result = SheetSync.sync(str(self.settings), str(self.project), ctx=ctx)
        self.assertEqual(result["silent"], ["A01"], "the voiced step alone")
        said = [i for i in ctx.issues if i.code == SheetSync.SILENT_CODE]
        self.assertEqual(len(said), 1, "said once, however many steps")
        self.assertEqual(said[0].severity, "warning", "staged all the same: a recording may come later")
        self.assertIn("A01", said[0].message)
        self.assertEqual(said[0].fix.op, "narration.render", "its fix is the render that makes the line")

        settings["options"] = {}
        self.settings.write_text(json.dumps(settings), encoding="utf-8")
        quiet = ptk.OpContext()
        self.assertEqual(SheetSync.sync(str(self.settings), str(self.project), ctx=quiet)["silent"], [])
        self.assertEqual([i for i in quiet.issues if i.code == SheetSync.SILENT_CODE], [], "a sheet that does not narrate expects no sound")

    def test_two_delivery_files_of_one_name_are_refused_before_anything_is_written(
        self,
    ):
        (self.source / "older").mkdir()
        write_delivery(
            str(self.source / "older" / "cabinet.fbx"),
            nodes=[("POINTS", "Null")],
            takes=[],
        )
        settings = json.loads(self.settings.read_text(encoding="utf-8"))
        settings["extras"] = ["older/cabinet.fbx"]
        self.settings.write_text(json.dumps(settings), encoding="utf-8")
        with self.assertRaisesRegex(
            ValueError, "two of the delivery's files are named cabinet.fbx"
        ):
            SheetSync.sync(str(self.settings), str(self.project))
        self.assertEqual(
            list((self.project / "Assets").iterdir()), [], "nothing staged"
        )

    def test_the_operation_is_declared(self):
        from unitytk.operations import OPERATIONS

        self.assertIn(
            ("sheet.sync", "unitytk.sheets:SheetSync.sync"),
            [(o.id, o.target) for o in OPERATIONS],
        )

    def test_the_unity_manifests_command_names_the_operation_and_its_arguments(self):
        """The Manifest window's "read it again" command (ManifestsActions.SyncCommand) restates the operation's
        id and arguments in C#: held here to the operation's catalog, so a renamed argument fails here first."""
        import re

        source = (
            Path(__file__).resolve().parents[1]
            / "com.m3trik.unitytk/Features/Manifests/Editor/ManifestsActions.cs"
        ).read_text(encoding="utf-8")
        operation = re.search(r'SyncOperation = "([^"]+)"', source).group(1)
        arguments = re.findall(
            r'"(\w+)"', re.search(r"SyncArguments = \{([^}]*)\}", source).group(1)
        )
        catalog = json.loads(
            (Path(__file__).resolve().parents[1] / "unitytk/operations.json").read_text(
                encoding="utf-8"
            )
        )
        declared = next(o for o in catalog["operations"] if o["id"] == operation)
        self.assertEqual(operation, "sheet.sync")
        self.assertEqual(arguments, list(declared["args"]["properties"]))
