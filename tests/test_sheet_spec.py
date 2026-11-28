# !/usr/bin/python
# coding=utf-8
"""Tests that a sheet's spec is declared once and every restatement follows it.

The spec is the template (pythontk's ``mapping/<template>.json``): its columns and its
actions, each word with its cells and settings. Everything else restates part of it and is
held to it here: the starter sheets an author copies, the reading's data about cell rows
(``sheets/readings/default.json``: ``order_by``, ``applies``), the clause templates a sheet
of sentences is read with (each names a word and settings the template declares), the plan
(every setting a row gives reaches it), and the kit that builds the plan in the Unity package
(a builder per word, a field per setting in the word's group of the Tasks kit's settings)."""

import os
import re
import sys
import unittest
from pathlib import Path

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase

from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

from unitytk.sheets import ClauseBook, DeliveryIndex, ReadingSpec, SheetCheck
from unitytk.sheets._cell_reading import _CellReading
from unitytk.sheets.clause_spec import ADVANCE

KIT = Path(__file__).resolve().parent.parent / "com.m3trik.unitytk" / "Kits" / "Tasks" / "Editor"

#: A value of each setting type the templates declare, for a row that sets it.
VALUES = {"number": "2", "color": "#fff", "object": "NODE_GRP", "span": "step", "clip": "A01", "text": "x",
          "state": "amber", "step": "A01", "flag": "on"}


def _actions(template: str) -> dict:
    return Mapping.load_mapping(template)["columns"]["actions"]


class _Kit:
    """The Tasks kit's C#, read as text: each builder's word, each settings group's fields and each enum's members."""

    def __init__(self, folder: Path):
        self.sources = {p: p.read_text(encoding="utf-8") for p in folder.rglob("*.cs")}
        text = "\n".join(self.sources.values())
        self.builders = set(re.findall(r'\[TaskBehavior\("(\w+)"', text))
        self.enums = {name: {m.lower() for m in re.findall(r"^\s*(\w+)\s*(?:=[^,]*)?,?\s*$", body, re.M)}
                      for name, body in re.findall(r"\benum\s+(\w+)\s*\{([^}]*)\}", text)}
        self.classes = {}
        for name, base, body in self._bodies(text):
            fields = dict((n, t) for t, n in re.findall(
                r"^\s*public\s+(?!const\b|static\b|override\b)([\w.<>\[\]]+)\s+@?(\w+)\s*(?:=|;)", body, re.M))
            self.classes[name] = (base, fields)

    @staticmethod
    def _bodies(text: str):
        """``(name, base, body)`` of every class: its body to its matching brace."""
        for match in re.finditer(r"\bclass\s+(\w+)\s*(?::\s*([\w.]+))?[^{;]*\{", text):
            depth, at = 1, match.end()
            while depth and at < len(text):
                depth += {"{": 1, "}": -1}.get(text[at], 0)
                at += 1
            yield match.group(1), match.group(2), text[match.end():at - 1]

    def fields(self, name: str) -> dict:
        """The public fields of class *name*, its base classes' with them."""
        base, own = self.classes.get(name, (None, {}))
        return {**(self.fields(base) if base in self.classes else {}), **own}

    def group(self, word: str):
        """The type of the kit's settings' member named as *word*, or None."""
        return self.fields("TaskSettings").get(word)


class SpecTestCase(UnityTkTestCase):
    def test_every_clause_template_reads_a_word_and_settings_its_template_declares(self):
        actions = _actions("interactive")
        words = set(actions) | {ADVANCE}
        for name, spec in ClauseBook.load_templates():
            with self.subTest(template=name):
                self.assertIn(spec.word, words)
                self.assertEqual(spec.problems(), [])
                for also in spec.also:
                    self.assertIn(also, words)
        self.assertEqual(set(ClauseBook().behaviors()) - {ADVANCE}, set(actions) - {""},
                         "a sheet of sentences reads into every word, and into no other")

    def test_the_readings_data_names_only_what_its_words_declare(self):
        reading, actions = ReadingSpec.load("interactive"), _actions("interactive")
        cells = set(_CellReading.CELLS)
        for word, setting in reading.order_by.items():
            self.assertIn(setting, actions[word].get("settings") or {}, word)
        for word, by in reading.applies.items():
            declared = set(actions[word].get("settings") or {})
            for setting, conditions in by.items():
                self.assertIn(setting, declared, word)
                for condition in conditions:
                    keys = set(condition) - {"any", "none"} | {k for c in ("any", "none") for k in condition.get(c) or {}}
                    self.assertEqual(keys - cells - declared, set(), (word, setting))

    def test_every_setting_a_row_gives_reaches_the_plan(self):
        """Nothing a template declares is dropped: each setting a row gives is in the plan's settings by its own name,
        and a setting naming an object (``where``) is the row's ``where`` field."""
        actions = _actions("interactive")
        index = DeliveryIndex(["NODE_GRP", "X", "A01"], ["A01"], "d.fbx")
        reader = _CellReading(ReadingSpec.load("interactive"), index, "warning: ", "unread", actions)
        heads = {"action": "Action", "settings": "Settings"}
        checked = 0
        for word, declared in actions.items():
            for name, spec in (declared.get("settings") or {}).items():
                value = next((c for c in spec.get("choices") or () if c != spec.get("default")), None) or VALUES[spec["type"]]
                row = {"word": word, "cells": True, "assets": ["X"], "settings": {name: value}, "issues": [], "warnings": []}
                (clause,), _, _, _ = reader.read(row, [row], heads, "A01")
                with self.subTest(word=word, setting=name):
                    if spec["type"] == "object":
                        self.assertEqual(clause.fields.get(name), ["NODE_GRP"])
                    else:
                        self.assertEqual(clause.settings.get(name), value)
                checked += 1
        self.assertGreater(checked, 15)

    def test_a_setting_its_row_makes_do_nothing_is_said_never_dropped(self):
        """``trigger=near; hold=2`` lost its hold and a ``when`` on a button leaving the task was dropped, with nothing
        said. Each setting the reading says does something only beside other cells (``applies``), given beside cells
        that meet none of its conditions, warns. Added: 2026-11-26"""
        reading, actions = ReadingSpec.load("interactive"), _actions("interactive")
        reader = _CellReading(reading, DeliveryIndex(["X"]), "warning: ", "unread", actions)
        heads = {"action": "Action", "settings": "Settings", "to": "To", "label": "Label", "target": "Target"}
        cases = [("use", {"trigger": "near"}, "hold"), ("use", {}, "hold"), ("button", {"to": "exit"}, "when"),
                 ("button", {"to": "B01"}, "reply"), ("button", {"to": "next"}, "reply")]
        for word, cells, setting in cases:
            spec = actions[word]["settings"][setting]
            value = next((c for c in spec.get("choices") or () if c != spec.get("default")), None) or VALUES[spec["type"]]
            row = {"word": word, "cells": True, "assets": ["X"], "issues": [], "warnings": [],
                   "settings": {k: v for k, v in cells.items() if k != "to"}, "to": cells.get("to", "")}
            row["settings"][setting] = value
            _, issues, _, _ = reader.read(row, [row], heads, "A01")
            with self.subTest(word=word, cells=cells, setting=setting):
                self.assertTrue(any(i.startswith("warning: ") and " takes no %s," % setting in i for _, i in issues), issues)
        # Met, or written at its default: nothing said.
        for word, cells, setting, value in (("use", {"trigger": "hold"}, "hold", "2"), ("button", {"to": "exit"}, "when", "end"),
                                            ("button", {"to": "retry"}, "reply", "No.")):
            row = {"word": word, "cells": True, "assets": ["X"], "issues": [], "warnings": [],
                   "settings": dict({k: v for k, v in cells.items() if k != "to"}, **{setting: value}),
                   "to": cells.get("to", "")}
            self.assertEqual(reader.read(row, [row], heads, "A01")[1], [], (word, cells))


class KitTestCase(UnityTkTestCase):
    """The kit builds every word as the template declares it: a builder registered by the word, and each setting a
    field of the word's group of the kit's settings (``TaskSettings.<word>.<setting>``), a choice an enum whose members
    are the sheet's choices. The kit is read as text, so its C# need not compile here."""

    @classmethod
    def setUpClass(cls):
        cls.kit = _Kit(KIT)

    def test_every_word_has_a_builder_registered_by_it(self):
        words = set(_actions("interactive")) - {""} | {ADVANCE}
        self.assertEqual(words - self.kit.builders, set(), "a word no builder builds")
        self.assertEqual(self.kit.builders - words, set(), "a builder of no word")

    def test_every_setting_is_a_field_of_its_words_group(self):
        for word, declared in _actions("interactive").items():
            settings = {n: s for n, s in (declared.get("settings") or {}).items() if s.get("type") != "object"}
            if not settings:
                continue
            group = self.kit.group(word)
            with self.subTest(word=word):
                self.assertIsNotNone(group, "TaskSettings has no member %s for the %s row's settings" % (word, word))
                fields = self.kit.fields(group)
                for name, spec in settings.items():
                    self.assertIn(name, fields, "%s.%s" % (word, name))
                    if spec.get("choices"):
                        members = self.kit.enums.get(fields[name].rsplit(".", 1)[-1])
                        self.assertIsNotNone(members, "%s.%s is a %s, no enum of the kit" % (word, name, fields[name]))
                        self.assertEqual({str(c).lower() for c in spec["choices"]} - members, set(),
                                         "%s.%s: the sheet's choices are its members" % (word, name))


class StarterTestCase(UnityTkTestCase):
    def test_the_starter_sheets_follow_their_templates(self):
        for template in ("interactive", "linear"):
            steps = Mapping.resolve(str(Mapping.starter_sheet(template)), name=template)
            rows = [row for step in steps for row in step.rows]
            self.assertTrue(rows and all(row.get("cells") for row in rows), f"{template}: written in cells")
            self.assertEqual([i for row in rows for i in row["issues"] + row["warnings"]], [], template)
            self.assertEqual(Mapping.unmapped_columns(str(Mapping.starter_sheet(template)), Mapping.load_mapping(template)), [])
            self.assertEqual({row["word"] for row in rows}, set(_actions(template)),
                             f"{template}: its starter shows every word")

    def test_the_interactive_starter_reads_clean_into_a_plan(self):
        check = SheetCheck.run(str(Mapping.starter_sheet("interactive")))
        self.assertEqual([item.line() for item in check.items], [])
        self.assertEqual(len(check.takes), 7)


if __name__ == "__main__":
    unittest.main()
