# !/usr/bin/python
# coding=utf-8
"""An old sheet's plan written as rows of cells (the converter's writer).

A sheet of sentences is read into a plan by the phrase reader; this writes that plan back as
the cell format's rows: a step's own row, then one row per action with its word, its names
and its settings (pythontk's ``ManifestActions``). It is the one place that knows the names
the phrase reader's behaviors go by, so that an old sheet can be rewritten once; it goes when
they do. What it cannot decide (a row no phrase reads, a ring at a viewpoint its teleport does
not go to, a teleport that names nothing) is left for a person, by row.
"""

from __future__ import annotations

import re
from typing import Any, Dict, List, Optional, Sequence, Tuple

from unitytk.sheets.reading_spec import ReadingSpec

#: A step ID the old format wrote with a dot, as the reader spells it: its base and its number.
_DOTTED = re.compile(r"^([A-Z]+_?\d+)_(\d+)$")
#: The behaviors a cell sheet needs no row for: what a step does anyway, or what its other rows say.
_UNSAID = ("advance", "buttons", "number_pad", "placard", "hold")
#: The behaviors that are their targets and nothing else, and the word each is written as.
_PLAIN = {
    "highlight": "highlight", "turn_on": "highlight", "take": "take", "return": "return", "select": "select",
    "note": "note",
    "fade_in": "fade_in", "show": "fade_in", "fade_out": "fade_out",
}
#: The behaviors whose choice ends their step: a teleport written after one happens as the step ends.
_ENDS = ("pause", "goto", "exit", "quiz")
#: The template's field (pythontk's ``ColumnMap``) each of a row's cells is written in.
_FIELDS = {
    "step": "step_id", "name": "step_name", "voice": "audio", "placard": "placard_text", "audio": "audio_file",
    "action": "action", "target": "assets", "with": "with", "clip": "clip", "members": "members", "label": "label",
    "to": "to", "settings": "settings", "notes": "description",
}


class _CellWriting:
    """A plan's steps as the cell format's rows.

    Parameters:
        plan: The old sheet's plan (its steps and their actions).
        index: The delivery (a path's node name, what a take moves).
        columns: The cell format's headers, in order.
        template: The format's mapping template: which of *columns* holds each field, as its reader takes them.
        moved: ``(index, take, listed paths) -> more paths``: what a take moves beyond what its row lists.
        audio: ``{step: its audio file cell}``, where the old sheet named one.
    """

    def __init__(self, plan, index, columns: Sequence[str], template, moved, audio: Optional[Dict[str, str]] = None):
        self.plan = plan
        self.audio = audio or {}  # each step's audio file cell, as the old sheet wrote it
        self.index = index
        self.columns = list(columns)
        self.at = {key: ReadingSpec.column_of(template, field, self.columns) for key, field in _FIELDS.items()}
        self.moved = moved
        self.left: List[Dict[str, Any]] = []
        self.ids = self._respelled([s.id for s in plan.steps])
        self.first = plan.steps[0].id if plan.steps else ""
        self.question = next((s.id for s in plan.steps if any(a.behavior == "option" for a in s.actions)), "")

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

    def _names(self, paths: Sequence[str]) -> str:
        return "\n".join(dict.fromkeys(self._name(p) for p in paths if p))

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
        # Every step is read before any row is written: a row that takes a shown thing away says so on the
        # row that showed it, which may be an earlier step's.
        parts: List[Any] = []
        section: Optional[Tuple[str, str]] = None
        shown: Dict[str, Dict[str, Any]] = {}  # what is on show, by behavior: its row's cells and its step
        self.ring: Optional[Dict[str, str]] = None  # a ring put up for the teleport that follows it
        for at, step in enumerate(self.plan.steps):
            if (step.section, step.section_title) != section:
                section = (step.section, step.section_title)
                if step.section:
                    title = "SECTION %s: %s" % (step.section, step.section_title) if step.section_title else step.section
                    parts.append([title] + [""] * (len(self.columns) - 1))
            fields = step.fields or {}
            made: List[Dict[str, str]] = []
            ended = False
            for action in step.actions:
                cells = self._action(at, step, action, ended, shown)
                ended = ended or action.behavior in _ENDS
                if cells is not None:
                    made.append(cells)
            # A hint line that was the page's words is the step's Placard, where the step wrote none.
            page = fields.get("placard", "") or next(
                ((a.fields or {}).get("label", "") for a in step.actions if a.behavior == "placard"), ""
            )
            parts.append(dict(step=self.ids[step.id], name=step.name, voice=fields.get("voice", ""),
                              placard=page, audio=self.audio.get(step.id, "")))
            parts.extend(made)
        return [part if isinstance(part, list) else self._row(**part) for part in parts]

    def _action(self, at: int, step, action, ended: bool, shown) -> Optional[Dict[str, str]]:
        """One action's cells, or None where a cell sheet needs no row for it (or it ends a span)."""
        behavior, fields, settings = action.behavior, action.fields or {}, dict(action.settings or {})
        cells = {"notes": action.text or "", "target": self._names(action.targets or ())}
        said: List[str] = []
        if behavior in _UNSAID:
            return None
        if behavior == "unread" or action.issues:
            self._leave(step.id, action.text, "; ".join(action.issues) or "no phrase reads it")
        if behavior == "unread":
            return {"notes": action.text or ""}  # its words kept, for the person who says what it is
        if behavior in _PLAIN:
            cells["action"] = _PLAIN[behavior]
        elif behavior == "indicate":
            cells["action"] = "indicate"
            arrows = fields.get("arrows") or []
            if len(arrows) == 1:  # the arrow an artist made: where it happens
                said.append("where=%s" % self._names(arrows))
            elif arrows:
                self._leave(step.id, action.text, "name each arrow on a row of its own: indicate <its part>, "
                                                  "Settings where=<its arrow>")
        elif behavior == "set_state":
            cells["action"] = "set"
            said.append("state=%s" % fields.get("state", ""))
        elif behavior in ("use", "place", "play"):
            cells["action"] = "play" if behavior == "play" else "use"
            tool = fields.get("tool", "")
            cells["with"], cells["clip"] = self._names([tool]), action.clip or ""
            listed = list(action.targets or ()) + ([tool] if tool else [])
            more = self.moved(self.index, action.clip, listed) if action.clip else []
            if more or behavior == "play":
                cells["members"] = self._names(listed + list(more))
            if behavior == "place":
                said.append("trigger=near")
            trigger = settings.pop("useTrigger", "")
            if trigger == "HoldToPlay":
                said.append("trigger=hold")
            elif trigger == "TimedPress":
                seconds = settings.pop("pressSeconds", "")
                if seconds:
                    said.append("hold=%s" % seconds)
                else:  # never the kit's own default: the sheet says how long, or a person does
                    self._leave(step.id, action.text, "say how long the press is held: Settings hold=<seconds>")
        elif behavior in ("pause", "goto", "exit", "quiz", "option"):
            cells.update(action="button", label=fields.get("label", ""))
            if behavior == "pause":
                cells["to"] = "next"
                if settings.pop("pauseWhen", "") == "Start":
                    said.append("when=start")
            elif behavior == "goto":
                cells["to"] = "restart" if action.restart or action.to == self.first else self.ids.get(action.to, action.to or "")
            elif behavior == "exit":
                cells["to"] = "exit"
            elif behavior == "quiz":
                cells["to"] = self.ids.get(self.question, self.question)
            else:
                cells["to"] = "next" if fields.get("value") == "correct" else "retry"
        elif behavior == "answer":
            other = settings.pop("answerKind", "") == "Otherwise"
            cells.update(action="answer", label="*" if other else fields.get("value", ""),
                         to=self.ids.get(action.to, action.to or ""))
        elif behavior == "teleport":
            facing = fields.get("facing") or fields.get("side_of") or ""
            cells.update(action="teleport", target=self._names([facing]))
            viewpoints = list(action.targets or ())
            if len(viewpoints) == 1:  # the viewpoint the row names: where the user stands
                said.append("where=%s" % self._names(viewpoints))
            elif viewpoints:
                self._leave(step.id, action.text, "name one viewpoint: Settings where=<it>")
            if not cells["target"] and not viewpoints:
                self._leave(step.id, action.text, "name the viewpoint in Settings where=<it>, or what the user faces in Target")
            if ended:
                said.append("when=end")
            if self.ring is not None:  # the ring a row before it put up is this teleport's own
                said.append("ring=%s" % self.ring.get("until", "step"))
                self.ring = None
        elif behavior in ("pip", "marker"):
            hide = settings.pop("pipShow", "") == "Hide" or settings.pop("markerShow", "") == "Hide"
            if behavior == "marker" and not action.targets:
                # A ring that names no viewpoint is the next teleport's: that row says how long it shows.
                if not hide:
                    self.ring = {"step": step.id}
                elif self.ring is not None:
                    self.ring["until"] = "step" if self.ring["step"] == step.id else self.ids[step.id]
                else:
                    self._leave(step.id, action.text, "no ring is up for it to take away")
                return None
            if hide:  # a thing on show goes: its own row says until when
                on = shown.pop(behavior, None)
                if on is None:
                    self._leave(step.id, action.text, "nothing is on show for it to take away")
                else:
                    until = "until=%s" % ("step" if on["step"] == step.id else self.ids[step.id])
                    on["cells"]["settings"] = "; ".join(filter(None, [on["cells"].get("settings", ""), until]))
                return None
            if behavior == "pip":
                cells["action"] = "pip"
            else:  # a ring at a viewpoint the row names: left for a person, the teleport that goes there says it
                self._leave(step.id, action.text, "say the ring on the teleport that goes to %s: Settings ring=step"
                            % cells["target"])
                return None
            shown[behavior] = {"cells": cells, "step": step.id}
        elif behavior in ("checklist", "check"):
            cells.update(action=behavior, label=fields.get("label", ""))
        else:
            cells["action"] = behavior
            self._leave(step.id, action.text, "no word is written for the behavior '%s'" % behavior)
        if settings:
            self._leave(step.id, action.text, "its settings have no word here: %s" % ", ".join(sorted(settings)))
        if said:
            cells["settings"] = "; ".join(said)
        return cells
