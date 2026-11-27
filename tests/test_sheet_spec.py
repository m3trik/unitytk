# !/usr/bin/python
# coding=utf-8
"""Tests that a sheet's spec is declared once and every restatement follows it.

The spec is the template (pythontk's ``mapping/<template>.json``): its columns and its
actions, each word with its cells and settings. Everything else restates part of it and is
held to it here: the starter sheets an author copies, the reading that binds each word to a
builder (``sheets/readings/default.json`` ``words``), and the builders and their settings
in the Unity package (the Tasks kit's C#)."""

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

from unitytk.sheets import DeliveryIndex, ReadingSpec, SheetCheck
from unitytk.sheets._cell_reading import _CellReading

KIT = Path(__file__).resolve().parent.parent / "com.m3trik.unitytk" / "Kits" / "Tasks" / "Editor"

#: The settings a template declares that no builder is handed yet: each a gap, listed so it is
#: closed on purpose (a setting carried leaves this list, and the test says so).
NOT_CARRIED = {}
#: What the reader itself reads of a word's settings (where its row stands among its step's).
READ_BY_THE_READER = {"play": {"after"}}


def _actions(template: str) -> dict:
    return Mapping.load_mapping(template)["columns"]["actions"]


def _words() -> dict:
    return ReadingSpec.load("interactive").words


def _read_by(cases) -> set:
    """The sheet settings and cells any of a word's cases reads (each case's own: ``_CellReading.reads``)."""
    return set().union(*(_CellReading.reads(case) for case in cases))


#: A value of each setting type the templates declare, for a row that sets it.
VALUES = {"number": "2", "color": "#fff", "object": "NODE_GRP", "span": "step", "clip": "A01", "text": "x",
          "state": "amber", "step": "A01", "flag": "on"}


def _row(word: str, conditions: dict, settings=None) -> dict:
    """A cell row of *word* meeting *conditions* (a case's ``if``), with *settings* over them."""
    row = {"word": word, "cells": True, "settings": {}, "issues": [], "warnings": []}
    for key, wanted in (conditions or {}).items():
        if key == "any":
            continue
        value = "x" if wanted == "?" else str(wanted)
        if key == "target":
            row["assets"] = [value] if value else []
        elif key in _CellReading.CELLS:
            row[key] = value
        elif value:
            row["settings"][key] = value
    row["settings"].update(settings or {})
    return row


class SpecTestCase(UnityTkTestCase):
    def test_every_word_of_the_templates_has_its_binding_and_no_other_word_does(self):
        declared = {w for name in ("interactive", "linear") for w in _actions(name) if w}
        self.assertEqual(set(_words()), declared)

    def test_every_setting_a_word_declares_is_carried_or_is_a_listed_gap(self):
        words = _words()
        gaps = {}
        for word, declared in _actions("interactive").items():
            missing = set(declared.get("settings") or {}) - _read_by(words[word]) - READ_BY_THE_READER.get(word, set())
            if missing:
                gaps[word] = missing
        self.assertEqual(gaps, NOT_CARRIED)

    def test_a_setting_its_rows_case_does_not_read_is_said_never_dropped(self):
        """Per case, not per word: a setting one case of a word read counted as read for every row of the word, so
        ``trigger=near; hold=2`` lost its hold and a ``when`` on a button leaving the task was dropped, with nothing
        said. Each case, given each setting of its word it does not read (at a value that keeps the row its case's),
        warns. Added: 2026-11-26"""
        reading, actions = ReadingSpec.load("interactive"), _actions("interactive")
        reader = _CellReading(reading, DeliveryIndex(["NODE_GRP", "X"]), "warning: ", "unread", actions)
        heads = {"action": "Action", "settings": "Settings", "to": "To", "label": "Label", "target": "Target"}
        checked = 0
        for word, cases in reading.words.items():
            declared = actions[word].get("settings") or {}
            for case in cases:
                for name in set(declared) - _CellReading.reads(case) - READ_BY_THE_READER.get(word, set()):
                    spec = declared[name]
                    values = [c for c in spec.get("choices") or () if c != spec.get("default")] or [VALUES[spec.get("type", "text")]]
                    for value in values:
                        row = _row(word, case.get("if"), {name: value})
                        siblings = [row] + ([_row(word, case["if"]["any"])] if "any" in (case.get("if") or {}) else [])
                        if reader._case(row, siblings) is not case:
                            continue  # the setting makes it another case's row, which reads it
                        _, issues, _, _ = reader.read(row, siblings, heads, "A01")
                        with self.subTest(word=word, case=case.get("if"), setting=name):
                            self.assertTrue(any(i.startswith("warning: ") and " takes no %s," % name in i for _, i in issues), issues)
                        checked += 1
        self.assertGreater(checked, 10)

    def test_a_binding_reads_only_what_its_word_declares(self):
        cells = {"target", "with", "clip", "members", "label", "to"}
        for word, cases in _words().items():
            declared = _actions("interactive")[word]
            known = cells | set(declared.get("settings") or {}) | {"any"}
            self.assertEqual(_read_by(cases) - known, set(), word)

    def test_every_behavior_and_builder_setting_a_binding_names_is_the_kits(self):
        sources = "\n".join(p.read_text(encoding="utf-8") for p in (KIT / "Behaviors").glob("*.cs"))
        built = set(re.findall(r'\[TaskBehavior\("(\w+)"', sources))
        settings = (KIT / "TaskSettings.cs").read_text(encoding="utf-8")
        fields = {name: kind for kind, name in re.findall(r"public\s+([\w.\[\]]+)\s+(\w+)\s*(?:=|;)", settings)}
        for word, cases in _words().items():
            for case in cases:
                for name in filter(None, (case.get("behavior"), case.get("brings"))):
                    self.assertIn(name, built, f"{word}: no builder is registered as {name}")
                for name in list(case.get("pass") or {}) + list(case.get("settings") or {}):
                    self.assertIn(name, fields, f"{word}: the kit's settings hold no {name}")
                for name, value in (case.get("settings") or {}).items():
                    enum = KIT / f"{fields[name]}.cs"
                    self.assertTrue(enum.is_file(), f"{word}: {name} is a {fields[name]}, which is no enum of the kit")
                    self.assertRegex(enum.read_text(encoding="utf-8"), rf"\b{value}\b", f"{word}: {name} has no value {value}")


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
