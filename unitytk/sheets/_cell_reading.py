# !/usr/bin/python
# coding=utf-8
"""A row read by its cells, into the clauses its builders take.

A sheet in the cell format writes each row as an action word and its cells (pythontk's
``ManifestActions``: Target, With, Clip, Label, To, Settings). The reading says, as data
(:attr:`ReadingSpec.words`), which behavior each word is and where each cell goes: nothing
here names a word or a behavior. A word is a list of cases, the first whose ``if`` holds
read::

    "button": [
        {"if": {"to": "next"}, "behavior": "pause", "fields": {"label": "{label}"}},
        {"if": {"to": "exit"}, "behavior": "exit", "fields": {"label": "{label}"}},
        {"behavior": "goto", "fields": {"label": "{label}"}, "to": "{to}"}
    ]

A case's keys: ``if`` (``{cell or setting: value}``; ``any``: a row of the step holds the same),
``behavior``, ``targets`` (the cell its targets are: ``target``, ``with``, ``none``, or a setting
that names an object: ``where``),
``fields`` (``{field: text}``, ``{cell}`` filled), ``nodes`` (``{field: cell}``, the one name the
cell holds, a node: two are an issue), ``lists`` (``{field: cell}``, every node the cell names), ``settings``
(given as written), ``pass`` (``{builder's setting: the sheet's}``, where the row sets it),
``to`` (``{to}``, or ``first``: the sheet's first step), ``clip`` (it plays the row's take),
``brings`` (a behavior its step needs once, before it), ``order`` (``start`` or ``end``:
where it stands among its step's rows, whose order the sheet gives no meaning) and ``until``
(``{behavior, settings, targets, by, shows}``: how what it shows goes, as the row's setting ``by``
says when, ``until`` by default: as its own step ends, ``step``, or as another starts; ``shows``,
where the row itself is not what shows it, is the clause that does). An ``if`` value of ``?`` asks only
that the cell or setting is filled; a setting the row leaves out holds its word's declared default.

A setting is read only where the case its row reads as reads it (:meth:`_CellReading.reads`): one the
case does not read is a warning, never dropped in silence, unless it is written at its word's default,
which changes nothing. A name is found exactly (:meth:`_CellReading.exact`): a node's own, the end of
its path, or a declared alias.
"""

from __future__ import annotations

import re
from typing import Dict, List, Mapping, Optional, Sequence, Tuple

import pythontk as ptk

from unitytk.sheets.clause_book import Clause

#: One problem with a row: its cell's header, and what is wrong with its fix.
Issue = Tuple[str, str]


class _CellReading:
    """Cell rows read by a reading's words.

    Parameters:
        reading: The reading: its ``words``.
        index: The delivery's nodes (a ``lists`` cell's names are found in it).
        warning: What leads an issue that is a warning.
        unread: The behavior of a row nothing reads.
        actions: The template's words (pythontk's ``actions``): each setting's declared default.
    """

    #: The cells a case may read from.
    CELLS = ("target", "with", "clip", "members", "label", "to")
    _FIELD = re.compile(r"\{(\w+)\}")

    def __init__(self, reading, index, warning: str, unread: str, actions: Optional[Mapping[str, Mapping]] = None):
        self.words: Mapping[str, Sequence[Mapping]] = reading.words or {}
        self.actions: Mapping[str, Mapping] = actions or {}
        self.index = index
        self.warning = warning
        self.unread = unread
        self.first = ""
        self.openings: Dict[str, List[Clause]] = {}  # step -> what a span of its rows shows first
        self.closings: Dict[str, List[Tuple[Clause, bool]]] = {}  # step -> (what ends there, from another step)

    def prime(self, steps) -> None:
        """Note what the whole sheet says: its first step, and each shown thing's span. A row whose case
        says how what it shows goes (its ``until``) and whose Settings say when (the setting the case
        names, ``until`` by default: ``step``, as its own step ends, or a step's ID, as that step starts)
        gives the step it goes in a clause; where the case says what shows it too (``shows``), the row's
        own step gets that clause first."""
        self.first = steps[0].step_id if steps else ""
        self.openings, self.closings = {}, {}
        ids = {step.step_id for step in steps}
        for step in steps:
            for row in step.rows:
                case = self._case(row, step.rows) if row.get("cells") else None
                spec = (case or {}).get("until")
                span = (row.get("settings") or {}).get(spec.get("by", "until"), "") if spec else ""
                if not span:
                    continue
                at = step.step_id if span.lower() == "step" else ptk.StrUtils.to_legal_name(span.upper())
                if at not in ids:
                    continue  # said where the row is read (pythontk's check)
                source = spec.get("targets", "target")
                targets = [] if source == "none" else self._cell(row, source)
                text = row.get("says") or row.get("word", "")
                if spec.get("shows"):
                    shows = spec["shows"]
                    self.openings.setdefault(step.step_id, []).append(
                        Clause(shows["behavior"], text, {"targets": list(targets)}, False, "cells",
                               dict(shows.get("settings") or {}))
                    )
                clause = Clause(spec["behavior"], text, {"targets": list(targets)}, False, "cells",
                                dict(spec.get("settings") or {}))
                self.closings.setdefault(at, []).append((clause, at != step.step_id))

    def _case(self, row: Mapping, step_rows: Sequence[Mapping]):
        """The case of *row*'s word that holds for it (None: none does, or its word has none)."""
        return next((c for c in self.words.get(row.get("word", "")) or () if self._holds(row, c.get("if"), step_rows)), None)

    @staticmethod
    def _cell(row: Mapping, key: str) -> List[str]:
        """The names or text of *row*'s cell *key*, as a list; a key that is no cell is the setting of
        that name, the object it names (``where``)."""
        if key == "target":
            return list(row.get("assets") or ())
        if key not in _CellReading.CELLS:
            value = str((row.get("settings") or {}).get(key, "")).strip()
            return [value] if value else []
        value = row.get(key)
        if isinstance(value, (list, tuple)):
            return list(value)
        return [line.strip() for line in str(value or "").splitlines() if line.strip()]

    def _value(self, row: Mapping, key: str) -> str:
        """What *row* says for *key*: a cell's first value, else a setting's (its word's default where the row
        leaves it out, ``ptk.ManifestActions.setting``: a default written out reads as left out)."""
        if key in self.CELLS:
            found = self._cell(row, key)
            return found[0] if found else ""
        return ptk.ManifestActions.setting(row, self.actions.get(row.get("word", "")), key) or ""

    def _default(self, word: str, name: str) -> str:
        """The default *word*'s template declares for its setting *name* (empty: none)."""
        return ptk.ManifestActions.setting({}, self.actions.get(word), name) or ""

    @classmethod
    def reads(cls, case: Mapping) -> set:
        """The cells and settings *case* reads: what its ``if`` tests, what it passes on, fills its fields and its
        jump with, takes its objects from, and its span's setting and objects."""
        read = set(case.get("if") or {}) | set((case.get("pass") or {}).values())
        for template in list((case.get("fields") or {}).values()) + [case.get("to", "")]:
            read |= set(cls._FIELD.findall(template))
        read |= {case.get("targets", "target")} | set((case.get("nodes") or {}).values())
        read |= set((case.get("lists") or {}).values())
        if case.get("until"):
            read |= {case["until"].get("by", "until"), case["until"].get("targets", "target")}
        return read - {"any", "none"}

    def _head(self, headers: Mapping[str, str], key: str) -> str:
        """The sheet's header for *key*: its cell's, else (a setting) the Settings cell's."""
        if key in self.CELLS:
            return headers.get(key) or key.title()
        return headers.get("settings", "Settings")

    def _said(self, conditions: Mapping, headers: Mapping[str, str]) -> str:
        """A case's ``if`` as a row's words say it (" with To exit", " with trigger=near"); empty for none."""
        parts = []
        for key, wanted in (conditions or {}).items():
            if key == "any":
                continue
            name = self._head(headers, key) if key in self.CELLS else key
            if wanted == "?":
                parts.append("%s filled" % name)
            elif wanted == "":
                parts.append("no %s" % name)
            else:
                parts.append(("%s %s" if key in self.CELLS else "%s=%s") % (name, wanted))
        said = " with " + " and ".join(parts) if parts else ""
        if (conditions or {}).get("any"):
            said += " beside one%s" % self._said(conditions["any"], headers)
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

    def _holds(self, row: Mapping, conditions: Mapping, step_rows: Sequence[Mapping]) -> bool:
        """Whether *row* meets a case's ``if``."""
        for key, wanted in (conditions or {}).items():
            if key == "any":
                if not any(r is not row and r.get("cells") and self._holds(r, wanted, step_rows) for r in step_rows):
                    return False
            elif wanted == "?":  # filled, whatever it holds
                if not self._value(row, key):
                    return False
            elif self._value(row, key).casefold() != str(wanted).casefold():
                return False
        return True

    def _own_helper(self, row: Mapping, helpers: Sequence[str]) -> Mapping:
        """*row*, a placing row that names no helper (its ``where`` empty), as if it named its own where
        the delivery has it: the name every reader of the sheet gives it (*helpers*' first, :meth:`_helpers`),
        which the Shot Manifest makes. Where the delivery has none (or several), the row is read as written
        and Unity makes its own."""
        if not helpers or len(self.index.named(helpers[0])) != 1:
            return row
        named = dict(row)
        named["settings"] = dict(row.get("settings") or {}, where=helpers[0])
        return named

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

    def read(self, row: Mapping, step_rows: Sequence[Mapping], headers: Mapping[str, str], step_id: str = ""):
        """``(clauses, issues, order, after)`` for one cell row.

        Parameters:
            row: The row as pythontk's reader made it (``word``, ``assets``, ``with``, ``clip``,
                ``label``, ``to``, ``settings``, ``says``, ``issues``, ``warnings``).
            step_rows: Every row of its step.
            headers: ``{cell key: header}`` for the issues (``action``, ``with``, ``settings``).
            step_id: Its step's ID: a placing row that names no helper takes its own by it, where the
                delivery has it (:meth:`_own_helper`), and its helpers' names (``helpers``, :meth:`_helpers`)
                for what Unity makes where it does not.

        Returns:
            Its clauses (one a case ``brings`` first), its issues (pythontk's, each at its cell, a
            warning led by the warning mark), where the case orders it (``start``, ``end`` or
            empty) and the take it plays after (its ``after`` setting, else empty).
        """
        word = row.get("word", "")
        if not word and not row.get("issues"):
            return [], [], "", ""  # a row that only lists a shot's member: nothing here is built from it
        helpers = self._helpers(row, step_id, step_rows)
        row = self._own_helper(row, helpers)
        text = row.get("says") or row.get("text") or word
        issues: List[Issue] = list(row.get("issues") or ())
        issues += [(head, self.warning + why) for head, why in row.get("warnings") or ()]
        case = self._case(row, step_rows)
        if case is None:
            if not row.get("issues"):  # a word its template declares and this reading builds nothing for
                issues.append((headers.get("action", "Action"),
                               "%snothing here builds a '%s' row, so it is left as written: remove the row, or build "
                               "it by hand" % (self.warning, word)))
            return [Clause(self.unread, text, template="cells")], issues, "", ""
        values = {key: ", ".join(self._cell(row, key)) for key in self.CELLS}
        values.update({k: str(v) for k, v in (row.get("settings") or {}).items()})
        fields: Dict[str, object] = {}
        source = case.get("targets", "target")
        fields["targets"] = [] if source == "none" else self._cell(row, source)
        for name, template in (case.get("fields") or {}).items():
            value = self._FIELD.sub(lambda m: values.get(m.group(1), ""), template)
            if value:
                fields[name] = value
        nodes: List[str] = []
        for name, cell in (case.get("nodes") or {}).items():
            found = self._cell(row, cell)
            if len(found) > 1:  # a field that is one node: which of them is never guessed
                head = self._head(headers, cell)
                issues.append((head, "%s takes one name here (the row's %s): keep one, and write each other on a row "
                                     "of its own" % (head, name)))
            elif found:
                fields[name] = found[0]
                nodes.append(name)
        for name, cell in (case.get("lists") or {}).items():
            paths = []
            for each in self._cell(row, cell):
                at, why = self.exact(each, self._head(headers, cell))
                paths += at
                issues += why
            if paths:
                fields[name] = paths
        if helpers:
            fields["helpers"] = helpers
        to = case.get("to", "")
        if to:
            fields["to"] = self.first if to == "first" else self._FIELD.sub(lambda m: values.get(m.group(1), ""), to)
        settings = dict(case.get("settings") or {})
        for theirs, ours in (case.get("pass") or {}).items():
            if ours in (row.get("settings") or {}):
                settings[theirs] = row["settings"][ours]
        # A setting is read where the case the row reads as reads it (``after``, by the reader itself, for any row);
        # one written at its word's default changes nothing.
        used = self.reads(case) | {"after"}
        for name, value in (row.get("settings") or {}).items():
            if name in used or str(value).casefold() == self._default(word, name).casefold():
                continue
            issues.append((headers.get("settings", "Settings"),
                           "%sa %s row%s takes no %s, so it does nothing here: remove the setting, or leave it as a "
                           "note in Notes" % (self.warning, word, self._said(case.get("if"), headers), name)))
        clause = Clause(case["behavior"], text, fields, bool(case.get("clip")), "cells", settings, nodes)
        out = [clause]
        if case.get("brings"):
            out.insert(0, Clause(case["brings"], text, {"targets": []}, False, "cells"))
        return out, issues, case.get("order", ""), str((row.get("settings") or {}).get("after", ""))
