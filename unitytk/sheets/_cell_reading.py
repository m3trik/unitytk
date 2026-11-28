# !/usr/bin/python
# coding=utf-8
"""A row read by its cells: the plan's action is its word, its cells and its settings, as written.

A sheet in the cell format writes each row as an action word and its cells (pythontk's
``ManifestActions``: Target, With, Clip, Label, To, Settings). Its word is the plan's behavior,
and nothing translates it: Target is its targets, With its ``with``, Label its ``label``, To its
``to`` (a step's ID spelled as the reader spells it), each setting the row gives its ``settings``
by the sheet's name and value, a setting that names an object (``where``) its ``where`` field: a
node per target, the row's own for its first, else the helper every tool names it by where the
delivery has it (``ptk.ManifestPlacement.helper_names``). A span (``until``, ``ring``) rides on the
row that shows the thing: no row takes it away.

What the reading says of a cell row is data: where a word's row stands among its step's
(:attr:`ReadingSpec.order_by`: a travel's ``when``) and when a setting does something
(:attr:`ReadingSpec.applies`): a setting a row gives that does nothing beside its other cells is a
warning, never dropped in silence, unless it is written at its word's default. A name is found
exactly (:meth:`_CellReading.exact`): a node's own, the end of its path, or a declared alias.
"""

from __future__ import annotations

from typing import Dict, List, Mapping, Optional, Sequence, Tuple

import pythontk as ptk

from unitytk.sheets.clause_book import Clause

#: One problem with a row: its cell's header, and what is wrong with its fix.
Issue = Tuple[str, str]


class _CellReading:
    """Cell rows read as their words.

    Parameters:
        reading: The reading: its ``order_by`` and ``applies``.
        index: The delivery's nodes (a cell's names are found in it).
        warning: What leads an issue that is a warning.
        unread: The behavior of a row nothing reads.
        actions: The template's words (pythontk's ``actions``): each word's cells and settings.
    """

    #: The cells a row may fill.
    CELLS = ("target", "with", "clip", "members", "label", "to")
    #: The setting types whose value names a step (or ``step``, its own): spelled as the reader spells a step ID.
    STEPPED = ("span", "step")

    def __init__(self, reading, index, warning: str, unread: str, actions: Optional[Mapping[str, Mapping]] = None):
        self.order_by: Mapping[str, str] = reading.order_by or {}
        self.applies: Mapping[str, Mapping] = reading.applies or {}
        self.actions: Mapping[str, Mapping] = actions or {}
        self.index = index
        self.warning = warning
        self.unread = unread

    @staticmethod
    def _cell(row: Mapping, key: str) -> List[str]:
        """The names or text of *row*'s cell *key*, as a list."""
        if key == "target":
            return list(row.get("assets") or ())
        value = row.get(key)
        if isinstance(value, (list, tuple)):
            return list(value)
        return [line.strip() for line in str(value or "").splitlines() if line.strip()]

    def _value(self, row: Mapping, key: str) -> str:
        """What *row* says for *key*: a cell's first value, else a setting's (its word's default where the row
        leaves it out, ``ptk.ManifestActions.setting``)."""
        if key in self.CELLS:
            found = self._cell(row, key)
            return found[0] if found else ""
        return ptk.ManifestActions.setting(row, self.actions.get(row.get("word", "")), key) or ""

    def _default(self, word: str, name: str) -> str:
        """The default *word*'s template declares for its setting *name* (empty: none)."""
        return ptk.ManifestActions.setting({}, self.actions.get(word), name) or ""

    def _declared(self, word: str) -> Mapping[str, Mapping]:
        """The settings *word* declares, each whole."""
        return (self.actions.get(word) or {}).get("settings") or {}

    @staticmethod
    def stepped(value: str) -> str:
        """A span's or a step setting's *value* as the reader spells it: ``step`` (its own step), else a step's
        ID, legal-named (``B03.5`` -> ``B03_5``)."""
        value = str(value).strip()
        return value.lower() if value.lower() == ptk.ManifestActions.SPAN_STEP else ptk.StrUtils.to_legal_name(value.upper())

    def _head(self, headers: Mapping[str, str], key: str) -> str:
        """The sheet's header for *key*: its cell's, else (a setting) the Settings cell's."""
        if key in self.CELLS:
            return headers.get(key) or key.title()
        return headers.get("settings", "Settings")

    def _holds(self, row: Mapping, conditions: Mapping, step_rows: Sequence[Mapping]) -> bool:
        """Whether *row* meets *conditions* (``{cell or setting: value}``; ``any`` / ``none``: another row of the
        step does / none does; a value of ``?`` asks only that it is filled)."""
        for key, wanted in (conditions or {}).items():
            if key in ("any", "none"):
                others = any(r is not row and r.get("cells") and self._holds(r, wanted, step_rows) for r in step_rows)
                if others != (key == "any"):
                    return False
            elif wanted == "?":
                if not self._value(row, key):
                    return False
            elif self._value(row, key).casefold() != str(wanted).casefold():
                return False
        return True

    def _said(self, row: Mapping, conditions: Mapping, headers: Mapping[str, str], step_rows: Sequence[Mapping]) -> str:
        """What *row* says where it misses *conditions*, as its words say it: `` with To exit``,
        `` with trigger=near``, `` with To next beside one with To retry``; empty for none."""
        parts = []
        for key in conditions or {}:
            if key in ("any", "none"):
                continue
            value = self._value(row, key)
            if key in self.CELLS:
                parts.append("%s %s" % (self._head(headers, key), value) if value else "no %s" % self._head(headers, key))
            else:
                parts.append("%s=%s" % (key, value) if value else "no %s" % key)
        said = " with " + " and ".join(parts) if parts else ""
        beside = (conditions or {}).get("none")
        if beside and not self._holds(row, {"none": beside}, step_rows):  # another row of the step holds what it may not
            said += " beside one with " + " and ".join(
                "%s %s" % (self._head(headers, k), v) if k in self.CELLS else "%s=%s" % (k, v) for k, v in beside.items()
            )
        return said

    def exact(self, name: str, column: str) -> Tuple[List[str], List[Issue]]:
        """``(paths, issues)`` for *name*, a cell's: the one node of that name (``DeliveryIndex.named``: its own
        name, the end of its path, or a declared alias), never a match by words; each issue at *column*."""
        found = self.index.named(name)
        if len(found) == 1:
            spelled = self.index.spelling(name)
            return found, [(column, "%sthe delivery spells %s as %s: write it as the scene does, whose tools tell "
                                    "names apart by case" % (self.warning, name, spelled))] if spelled else []
        if not found:
            return [], [(column, "the delivery has no %s: correct the name, or have the artist add it" % name)]
        return [], [(column, "%s names %s nodes: write the one meant as the end of its path that tells it apart (%s)"
                     % (name, len(found), ", ".join(self.index.shortest(p) for p in found)))]

    @staticmethod
    def _helpers(row: Mapping, step_id: str, step_rows: Sequence[Mapping] = ()) -> List[str]:
        """The names of *row*'s own helpers, one per Target, where it is a placing row that names none (its ``where``
        empty): ``ptk.ManifestPlacement.helper_names``, the one rule every tool names a step's helpers by
        (``VIEW_<step>_<target>``; a target another row of the step shares a name with, by the end of its path that
        tells it apart), so what Unity makes for the row is named as the Shot Manifest makes it. None for any
        other row.

        Parameters:
            row: One of *step_rows*.
            step_id: Its step's ID.
            step_rows: Every row of its step (none: *row* alone).
        """
        rows = list(step_rows) or [row]
        names = ptk.ManifestPlacement.helper_names(step_id, rows) if step_id else []
        at = next((i for i, each in enumerate(rows) if each is row), None)
        if at is None or names[at] is None:
            return []
        # Its other Targets told apart as its first is: from every Target of the step's rows of its purpose.
        among = [t for each, name in zip(rows, names) if name and each.get("places") == row.get("places")
                 for t in each.get("assets") or ()]
        return [names[at]] + [ptk.ManifestPlacement.helper_name(row["places"], step_id, t, among)
                              for t in list(row.get("assets") or ())[1:]]

    def _where(self, row: Mapping, helpers: Sequence[str], headers: Mapping[str, str]) -> Tuple[List[str], List[Issue]]:
        """``(where, issues)``: *row*'s object per Target (one where it has none): its own ``where`` for its first,
        else each Target's own helper where the delivery has it, alone (*helpers*); ``""`` where a Target has none.
        Empty where no Target has one."""
        named = str((row.get("settings") or {}).get(ptk.ManifestActions.WHERE, "")).strip()
        count = max(1, len(row.get("assets") or ()))
        if named:
            paths, issues = self.exact(named, self._head(headers, ptk.ManifestActions.WHERE))
            return ([paths[0]] + [""] * (count - 1) if paths else []), issues
        out = []
        for helper in helpers:
            found = self.index.named(helper)
            out.append(found[0] if len(found) == 1 else "")  # none delivered (or several): Unity makes its own
        return (out if any(out) else []), []

    def read(self, row: Mapping, step_rows: Sequence[Mapping], headers: Mapping[str, str], step_id: str = ""):
        """``(clauses, issues, order, after)`` for one cell row.

        Parameters:
            row: The row as pythontk's reader made it (``word``, ``assets``, ``with``, ``clip``,
                ``label``, ``to``, ``settings``, ``says``, ``issues``, ``warnings``).
            step_rows: Every row of its step.
            headers: ``{cell key: header}`` for the issues (``action``, ``with``, ``settings``).
            step_id: Its step's ID: a placing row that names no helper takes its own by it, where the
                delivery has it, and its helpers' names (``helpers``, :meth:`_helpers`) for what Unity makes
                where it does not.

        Returns:
            Its clause (none for a row that only lists a shot's member), its issues (pythontk's, each at its
            cell, a warning led by the warning mark), where its word places it among its step's rows
            (``start``, ``end`` or empty) and the take it plays after (its ``after`` setting, else empty).
        """
        word = row.get("word", "")
        if not word and not row.get("issues"):
            return [], [], "", ""  # a row that only lists a shot's member: nothing here is built from it
        text = row.get("says") or row.get("text") or word
        issues: List[Issue] = list(row.get("issues") or ())
        issues += [(head, self.warning + why) for head, why in row.get("warnings") or ()]
        declared = self.actions.get(word)
        if not word or declared is None:  # pythontk's check said why
            return [Clause(self.unread, text, template="cells")], issues, "", ""
        fields: Dict[str, object] = {"targets": self._cell(row, "target")}
        nodes: List[str] = []
        tools = self._cell(row, "with")
        if len(tools) > 1:  # one node: which of them is never guessed
            head = self._head(headers, "with")
            issues.append((head, "%s takes one name: keep one, and write each other on a row of its own" % head))
        elif tools:
            fields["with"] = tools[0]
            nodes.append("with")
        label = ", ".join(self._cell(row, "label"))
        if label:
            fields["label"] = label
        to = str(row.get("to") or "").strip()
        if to:
            fields["to"] = to
        helpers = self._helpers(row, step_id, step_rows)
        where, why = self._where(row, helpers, headers)
        issues += why
        if where:
            fields[ptk.ManifestActions.WHERE] = where
        if helpers:
            fields["helpers"] = helpers
        settings: Dict[str, str] = {}
        kinds = self._declared(word)
        for name, value in (row.get("settings") or {}).items():
            kind = (kinds.get(name) or {}).get("type")
            if name not in kinds or kind == "object":
                continue  # an undeclared one is pythontk's issue; an object is its field
            settings[name] = self.stepped(value) if kind in self.STEPPED else str(value).strip()
        # A setting does something only beside the cells its reading says; one written at its default changes nothing.
        for name, conditions in (self.applies.get(word) or {}).items():
            value = (row.get("settings") or {}).get(name)
            if value is None or str(value).casefold() == self._default(word, name).casefold():
                continue
            if any(self._holds(row, each, step_rows) for each in conditions):
                continue
            issues.append((headers.get("settings", "Settings"),
                           "%sa %s row%s takes no %s, so it does nothing here: remove the setting, or leave it as a "
                           "note in Notes" % (self.warning, word, self._said(row, conditions[0], headers, step_rows),
                                              name)))
        order = self._value(row, self.order_by[word]) if word in self.order_by else ""
        clause = Clause(word, text, fields, bool(declared.get("take")), "cells", settings, nodes)
        return [clause], issues, order if order in ("start", "end") else "", str((row.get("settings") or {}).get("after", ""))
