# !/usr/bin/python
# coding=utf-8
"""An old sheet's plan written as rows of cells (the converter's writer).

A sheet of sentences is read into a plan in the sheet's own words, cells and settings (the clause
templates fold what the format needs no row for into the rows it belongs to); this writes that plan
back as the cell format's rows: a step's own row, then one row per action with its word, its names,
its take, its Label and To and its settings (pythontk's ``ManifestActions``), its old wording in
Notes. No word is translated here. What a person must still decide is listed, by row: a row no
phrase reads, a row the reading could not bind, a value the row needs that the old sheet never said
(each row written is held to the format's own check, ``ManifestActions.check_row``), and a field the
format has no cell for.
"""

from __future__ import annotations

import re
from typing import Any, Dict, List, Optional, Sequence, Tuple

import pythontk as ptk

from unitytk.sheets.reading_spec import ReadingSpec

#: A step ID the old format wrote with a dot, as the reader spells it: its base and its number.
_DOTTED = re.compile(r"^([A-Z]+_?\d+)_(\d+)$")
#: The template's field (pythontk's ``ColumnMap``) each of a row's cells is written in.
_FIELDS = {
    "step": "step_id", "name": "step_name", "voice": "audio", "placard": "placard_text", "audio": "audio_file",
    "action": "action", "target": "assets", "with": "with", "clip": "clip", "members": "members", "label": "label",
    "to": "to", "settings": "settings", "notes": "description",
}
#: The plan's fields a row in cells writes in its own cells, and the ones a sheet of sentences alone says that a cell
#: needs no word for (a take's words, kept in Notes; the helpers' names, which a cell row is given again).
_WRITTEN = ("with", "label", ptk.ManifestActions.WHERE)
_UNSAID = ("phrase", "helpers")


class _CellWriting:
    """A plan's steps as the cell format's rows.

    Parameters:
        plan: The old sheet's plan (its steps and their actions).
        index: The delivery (a path's node name, what a take moves).
        columns: The cell format's headers, in order.
        template: The format's mapping template: which of *columns* holds each field, as its reader takes them,
            and its words (what a row of each needs).
        moved: ``(index, take, listed paths) -> more paths``: what a take moves beyond what its row lists.
        audio: ``{step: its audio file cell}``, where the old sheet named one.
    """

    def __init__(self, plan, index, columns: Sequence[str], template, moved, audio: Optional[Dict[str, str]] = None):
        self.plan = plan
        self.audio = audio or {}  # each step's audio file cell, as the old sheet wrote it
        self.index = index
        self.columns = list(columns)
        self.at = {key: ReadingSpec.column_of(template, field, self.columns) for key, field in _FIELDS.items()}
        self.heads = {key: self.columns[at] for key, at in self.at.items() if at is not None}
        self.actions = (template.get("columns") or {}).get("actions") or {}
        self.moved = moved
        self.left: List[Dict[str, Any]] = []
        self.ids = self._respelled([s.id for s in plan.steps])

    @staticmethod
    def _respelled(ids: Sequence[str]) -> Dict[str, str]:
        """``{a step's ID: how the format writes it}``: an ID the old format wrote with a dot (read as
        ``B03_5``) takes its base and a letter, in order (``B03A``, then ``B03B``); any other stays."""
        out = {each: each for each in ids}
        groups: Dict[str, List[Tuple[int, str]]] = {}
        for each in ids:
            match = _DOTTED.match(each)
            if match and match.group(1) in out:
                groups.setdefault(match.group(1), []).append((int(match.group(2)), each))
        for base, members in groups.items():
            for n, (_, each) in enumerate(sorted(members)):
                out[each] = base + chr(ord("A") + n)
        return out

    def _name(self, path: str) -> str:
        """The node at *path* as a cell names it: its own name, or the end of its path that tells it apart where
        another node shares the name (``DeliveryIndex.shortest``)."""
        return self.index.shortest(path)

    def _names(self, paths: Sequence[str]) -> List[str]:
        return list(dict.fromkeys(self._name(p) for p in paths if p))

    def _step(self, value: str) -> str:
        """A step's ID (or ``step``, a destination) as the format writes it."""
        return self.ids.get(value, value)

    def _row(self, **cells: str) -> List[str]:
        """A row of *cells* (``{cell: value}``), each in the column its template field is read from."""
        row = [""] * len(self.columns)
        for key, value in cells.items():
            if value and self.at.get(key) is not None:
                row[self.at[key]] = value
        return row

    def _leave(self, step: str, text: str, why: str) -> None:
        self.left.append({"step": step, "row": None, "text": text, "why": why})

    def rows(self) -> List[List[str]]:
        """Every step's rows, a section's title row before its first step."""
        parts: List[Any] = []
        section: Optional[Tuple[str, str]] = None
        for step in self.plan.steps:
            if (step.section, step.section_title) != section:
                section = (step.section, step.section_title)
                if step.section:
                    title = "SECTION %s: %s" % (step.section, step.section_title) if step.section_title else step.section
                    parts.append([title] + [""] * (len(self.columns) - 1))
            fields = step.fields or {}
            parts.append(dict(step=self.ids[step.id], name=step.name, voice=fields.get("voice", ""),
                              placard=fields.get("placard", ""), audio=self.audio.get(step.id, "")))
            parts.extend(cells for cells in (self._action(step, a) for a in step.actions) if cells is not None)
        return [part if isinstance(part, list) else self._row(**part) for part in parts]

    def _action(self, step, action) -> Optional[Dict[str, str]]:
        """One action's cells; None for a row the format has no word for (the step goes on by itself anyway)."""
        from unitytk.sheets._row_reading import UNREAD

        word, fields = action.behavior, dict(action.fields or {})
        if action.issues or word == UNREAD:
            self._leave(step.id, action.text, "; ".join(action.issues) or "no phrase reads it")
        if word == UNREAD:
            return {"notes": action.text or ""}  # its words kept, for the person who says what it is
        declared = self.actions.get(word)
        if declared is None:
            return None  # `advance`: what a step that offers no choice does anyway
        targets = self._names(action.targets or ())
        tool = fields.get("with") or ""
        settings = {k: (self._step(v) if k in self._stepped(word) else v) for k, v in (action.settings or {}).items()}
        where = list(fields.get(ptk.ManifestActions.WHERE) or ())
        if where and not any(where[1:]):  # the row's one object, at its first target (or with none)
            if where[0]:
                settings[ptk.ManifestActions.WHERE] = self._name(where[0])
        elif where:
            self._leave(step.id, action.text, "name each one's object on a row of its own: %s <its target>, Settings "
                                              "where=<its object>" % word)
        members: List[str] = []
        if action.clip:
            listed = list(action.targets or ()) + ([tool] if tool else [])
            more = self.moved(self.index, action.clip, listed)
            if more or "members" in (declared.get("needs") or ()):
                members = self._names(listed + list(more))
        cells = {
            "action": word, "notes": action.text or "", "target": "\n".join(targets),
            "with": self._name(tool) if tool else "", "clip": action.clip or "", "members": "\n".join(members),
            "label": str(fields.get("label") or ""), "to": self._step(action.to or ""),
            "settings": "; ".join("%s=%s" % pair for pair in settings.items()),
        }
        for name, value in fields.items():
            if name not in _WRITTEN + _UNSAID:
                self._leave(step.id, action.text, "the format has no cell for its %s (%s): say it in a cell the "
                                                  "row has, or leave it in Notes" % (name, value))
        # What the row still needs that the old sheet never said, as the format's own check says it.
        record = {"word": word, "assets": targets, "with": [cells["with"]] if tool else [], "members": members,
                  "clip": cells["clip"], "label": cells["label"], "to": cells["to"], "settings": settings,
                  "setting_errors": []}
        for head, why in ptk.ManifestActions.check_row(record, self.actions, self.heads):
            self._leave(step.id, action.text, "%s: %s" % (head, why))
        return cells

    def _stepped(self, word: str) -> List[str]:
        """The settings of *word* whose value names a step (a span, a step): respelled as the format writes it."""
        return [name for name, spec in ((self.actions.get(word) or {}).get("settings") or {}).items()
                if (spec or {}).get("type") in ("span", "step")]
