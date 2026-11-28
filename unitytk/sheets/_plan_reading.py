# !/usr/bin/python
# coding=utf-8
"""How a sheet becomes a plan: :meth:`SheetPlan.from_sheet`'s reading pass, the
plan's internal base (:class:`_SheetPlanInternal`).

Per step, in sheet order: each row of its contents and of its hint is read by
the clause book, or, in cells, as its word (an unread row stays, as ``unread``);
the rows that play a clip are numbered to name their takes; the phrases of the
rows whose roles say they show something (``shows``, then ``points``) are matched
first, as the step's context; then every action's phrases are matched against the
delivery, and the hand is followed as the sheet runs (what a ``takes`` row puts in
it, a ``releases`` or ``places`` row takes out), so a ``uses`` row done with what
the hand holds names it in its ``with``. Then the sheet's sentences the format needs
no row for are folded, by their templates' own roles, into the rows and steps they
belong to (:meth:`_ReadingPass.fold`): a picture taken away is its row's ``until``, a
ring shown before a travel that travel's ``ring``, a page's text its step's. What a
row does with its roles is the clause templates' data (:attr:`ClauseSpec.roles`),
never a word's name.
"""

import os
import re
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple, Union
from urllib.parse import urlparse

import pythontk as ptk

from unitytk.sheets._row_reading import QUESTION, UNREAD, WARNING, _RowReading, headers
from unitytk.sheets.clause_book import Clause, ClauseBook
from unitytk.sheets.clause_spec import OWN_ROLES
from unitytk.sheets.delivery_index import DeliveryIndex
from unitytk.sheets.node_matcher import NodeMatcher
from unitytk.sheets.reading_spec import ReadingSpec


class _ReadingPass:
    """One reading of a sheet's steps against a delivery: what the steps share as the sheet runs
    (the takes claimed, the hand, the file's order) and how each step is read.

    Parameters:
        index: The delivery (its build suffixes the reading's).
        book: The clause templates.
        reading: The reading.
        template: The sheet's mapping template (the headers its issues name).
        aliases: ``{phrase: node}`` where words name a node no rule finds.
        step_ids: Every step's ID in sheet order, for the jumps.
        base: The settings folder each step's sound is relative to.
        retired: The step IDs the project retired (legal-named), which a jump never goes to.
    """

    def __init__(
        self, index, book, reading, template, aliases, step_ids, base, retired=()
    ):
        self.index = index
        self.book = book
        self.reading = reading
        self.template = template
        self.columns = headers(template)
        self.matcher = NodeMatcher(index, aliases, reading.vocabulary)
        # A strict reading's sheet is read row by row, each row's words bound to that row's own names.
        self.rows = (
            _RowReading(index, book, reading, aliases, template)
            if reading.strict
            else None
        )
        self.step_ids = step_ids
        self.retired = set(retired)
        self.base = base
        self.claimed: Dict[str, str] = {}  # take -> the step that plays it
        self.last_take: Optional[Tuple[int, str]] = (
            None  # (place in the file, name) of the take played last
        )
        self.played: set = set()
        self.held: Optional[str] = (
            None  # the path of what the hand holds as the sheet runs
        )
        self.records: List[Tuple[Any, Any, Clause, Optional[int]]] = []  # (plan step, action, clause, row) in order

    # ---- a step's rows ------------------------------------------------------------------------------------------

    def _items(self, step):
        """``(clauses, row_of, takes, clip_of)``: *step*'s rows read, where each came from, the strict
        reading's takes by row and each clip row's take."""
        if not self.rows:
            clauses = _SheetPlanInternal._clauses(step, self.book)
            clip_clauses = [c for c in clauses if c.clip]
            names = _SheetPlanInternal._numbered_takes(step.step_id, len(clip_clauses))
            return clauses, {}, {}, {id(c): n for c, n in zip(clip_clauses, names)}
        items = self.rows.clauses(step)
        # A hint line's words bind among the step's names: by match or head noun, never as the only one.
        listed = {
            "assets": [n for r in step.rows for n in r.get("assets") or []],
            "step": True,
        }
        page = ReadingSpec.header(
            self.template,
            self.reading.source_of(self.reading.page) or self.reading.page,
        )
        for line in (
            each.strip() for each in step.pass_through.get("hint", "").splitlines()
        ):
            if not line:
                continue
            read = self.book.read(line, "hint")
            pre = (
                []
                if read
                else [
                    (
                        self.columns["hint"],
                        'no hint phrase reads "%s": move it to %s' % (line, page),
                    )
                ]
            )
            items += [(c, None, listed, pre) for c in (read or [Clause(UNREAD, line)])]
        takes = self.rows.takes(
            step.step_id, [(n, r) for c, n, r, _ in items if c.clip and n]
        )
        clip_of = {id(c): takes.get(n, (None, []))[0] for c, n, _, _ in items if c.clip}
        return (
            [c for c, _, _, _ in items],
            {id(c): (n, r, pre) for c, n, r, pre in items},
            takes,
            clip_of,
        )

    def _bind(
        self, phrase: str, clause: Clause, row_of, context
    ) -> Tuple[List[str], List]:
        """``(paths, issues)`` for *phrase* of *clause*: among its row's names, or the delivery's."""
        if id(clause) in row_of and row_of[id(clause)][1] is not None:
            return self.rows.bind(
                phrase, clause.behavior, row_of[id(clause)][1], context
            )
        found = self.matcher.match(phrase, context)
        return ([found.path] if found.path else []), [
            (self.columns["contents"], i) for i in found.issues()
        ]

    def _context(self, clauses: Sequence[Clause], row_of) -> List[str]:
        """What the step shows: the targets of its ``shows`` rows first, then of its ``points`` rows read
        against them ("the lamp on the table" is the new one it highlights)."""
        context: List[str] = []
        for role in ("shows", "points"):
            for clause in clauses:
                if role not in self.book.roles_of(clause):
                    continue
                for phrase in clause.fields.get("targets", []):
                    for path in self._bind(phrase, clause, row_of, context)[0]:
                        if path not in context:
                            context.append(path)
        return context

    def step(self, step):
        """*step* read: its :class:`PlanStep`."""
        from unitytk.sheets.sheet_plan import PlanStep

        clauses, row_of, takes, clip_of = self._items(step)
        context = self._context(clauses, row_of)
        actions = [
            self._action(clause, clauses, row_of, takes, clip_of, context, step)
            for clause in clauses
        ]
        columns = step.pass_through
        planned = PlanStep(
            id=step.step_id,
            name=columns.get("step_name", "") or step.name,
            section=step.section,
            section_title=step.section_title,
            fields=self._step_fields(step),
            actions=actions,
        )
        self.records += [(planned, a, c, row_of.get(id(c), (None,))[0]) for c, a in zip(clauses, actions)]
        return planned

    def _step_fields(self, step) -> Dict[str, str]:
        """The step's values the reading carries (:attr:`ReadingSpec.step_fields`), the empty left out."""
        sources = {**step.pass_through, "audio": step.audio}
        to = getattr(step, "to", "") or ""
        if to:  # where the step goes by itself, as the sheet's step row writes it
            sources["to"] = to.lower() if to.lower() in ("next", "restart", "exit") else ptk.StrUtils.to_legal_name(to.upper())
        if "sound" in self.reading.step_fields.values():
            sources["sound"] = _SheetPlanInternal._sound(step, self.base)
        out = {}
        for name, source in self.reading.step_fields.items():
            value = sources.get(source) or ""
            if value.strip():
                out[name] = value
        return out

    # ---- one row ------------------------------------------------------------------------------------------------

    def _action(self, clause, clauses, row_of, takes, clip_of, context, step):
        """*clause* read into its :class:`PlanAction`: its nodes, fields, take and jump, the hand followed."""
        from unitytk.sheets.sheet_plan import PlanAction

        action = PlanAction(
            behavior=clause.behavior,
            text=clause.text,
            settings=dict(clause.settings or {}),
        )
        roles = self.book.roles_of(clause)
        row_no, row, pre = row_of.get(id(clause), (None, None, []))
        issues: List = list(pre)
        first = next(
            (c for c in clauses if row_of.get(id(c), (None,))[0] == row_no), None
        )
        if row is not None and row_no and clause is first:
            issues.extend(self.rows.missing(row))
        for phrase in clause.fields.get("targets", []):
            paths, why = self._bind(phrase, clause, row_of, context)
            issues.extend(why)
            action.targets.extend(p for p in paths if p not in action.targets)
        where = ptk.ManifestActions.WHERE
        for key, value in clause.fields.items():
            if key in ("targets", "to"):
                continue
            if key in clause.nodes:
                paths, why = self._bind(value, clause, row_of, context)
                issues.extend(why)
                if paths:  # `where` is one per target, the row's own its first's
                    action.fields[key] = (
                        [paths[0]] + [""] * (len(action.targets) - 1) if key == where else paths[0]
                    )
            else:
                action.fields[key] = value
        cells = clause.template == "cells"
        if not cells and "points" in roles and action.targets and where not in action.fields:
            # A sentence names no pointer: the delivery's authored one for each target, by its name (a row in cells
            # names its own in Settings).
            pattern = (self.reading.names or {}).get("point")
            pointers = [
                (self.index.pointers_at(t, pattern, step.step_id) or [""])[0]
                for t in action.targets
            ]
            if any(pointers):
                action.fields[where] = pointers
        if self.rows and "destination" in roles and not cells:  # a cell row's where is its viewpoint, whatever its name
            for path in action.fields.get(where) or ():
                if path:
                    issues.extend(self.rows.not_viewpoint(path))
        if clause.clip:
            issues.extend(
                self._clip(
                    action,
                    clip_of.get(id(clause)),
                    takes.get(row_no, (None, []))[1],
                    row,
                    step,
                )
            )
        if clause.fields.get("to"):
            action.to = self._to(str(clause.fields["to"]), cells)
            if action.to in self.retired:
                issues.append(
                    (
                        self.columns["contents"],
                        f"the jump goes to step {action.to}, which is retired: jump to the step that replaced it",
                    )
                )
            elif action.to not in ptk.ManifestActions.DESTINATIONS and action.to not in self.step_ids and not cells:
                # a cell row's To is held to the sheet where it is read
                issues.append(
                    (
                        self.columns["contents"],
                        f"the sheet has no step {action.to}: jump to a step the sheet has",
                    )
                )
        if not cells:  # a cell row names its tool in With
            self._follow_hand(action, roles)
        self._state(action, issues, row_no)
        # The targets the delivery's own build keyed this behavior on: left to it (built once between the two
        # manifests). Its entry names each as the sheet wrote it, so it is found as the sheet's names are.
        keyed = self.index.keyed.get(step.step_id, ())
        if keyed:
            look = self.index.named if cells else self.index.find
            done = {p for name, behavior in keyed if behavior == clause.behavior for p in look(name)}
            action.upstream = [t for t in action.targets if t in done]
        return action

    def _to(self, to: str, cells: bool) -> str:
        """Where a row goes, as the plan says it: a destination (``next``, ``restart``, ``exit``, ``retry``) in lower
        case, else a step's ID as the reader spells it (``B03.5`` -> ``B03_5``); a sheet of sentences' jump to its
        first step ``restart``, where its reading says so (:attr:`ReadingSpec.restart`)."""
        if to.strip().lower() in ptk.ManifestActions.DESTINATIONS:
            return to.strip().lower()
        target = ptk.StrUtils.to_legal_name(to.strip().upper())
        if not cells and self.reading.restart and target == next(iter(self.step_ids), None):
            return "restart"
        return target

    def _clip(self, action, name, cell_issues, row, step) -> List:
        """The issues of *action*'s take *name*, its ``clip`` set where the delivery plays it here."""
        clip = self.columns["clip"]
        issues = list(cell_issues) if self.rows else []
        if name and name in self.claimed and self.claimed[name] != step.step_id:
            issues.append(
                (
                    clip,
                    f"the take {name} is claimed by step {self.claimed[name]} too: "
                    f"name each row's take in {clip}",
                )
            )
        elif name and name in self.index.takes:
            action.clip = name
            self.played.add(name)
            self.claimed[name] = step.step_id
            if self.rows:
                issues.extend(
                    _SheetPlanInternal._take_checks(
                        name,
                        step.step_id,
                        self.step_ids,
                        self.index,
                        self.last_take,
                        clip,
                    )
                )
                issues.extend(self.rows.moves(name, row, step.rows))
                self.last_take = max(
                    self.last_take or (-1, ""), (self.index.takes.index(name), name)
                )
        elif name:
            issues.append(
                (
                    clip,
                    f"the delivery has no take {name}: name the take as the delivery does, or "
                    "have the artist export it",
                )
            )
        return issues

    def _follow_hand(self, action, roles) -> None:
        """What the hand holds after *action*, and what a ``uses`` row is done with."""
        hand = self.reading.hand
        if "takes" in roles and action.targets:
            self.held = action.targets[0]
        elif "uses" in roles:
            if (
                not action.fields.get(hand)
                and self.held
                and self.held not in action.targets
            ):
                action.fields[hand] = self.held
            if "places" in roles:
                self.held = None  # placed: it is the scene's now
            elif action.fields.get(hand):
                self.held = action.fields[hand]
        elif "releases" in roles and self.held in action.targets:
            self.held = None

    def _state(self, action, issues, row_no) -> None:
        """*issues* stated on *action*: each its cell first where the sheet is read row by row, a warning apart."""
        for column, text in issues:
            warned = text.startswith(WARNING)
            text = text[len(WARNING) :] if warned else text
            cell = _SheetPlanInternal._cell(
                row_no, column if row_no else self.columns["hint"], text
            )
            (action.warnings if warned else action.issues).append(
                cell if self.rows else text
            )

    # ---- the sentences the format needs no row for ------------------------------------------------------------

    def _own(self, clause: Clause) -> set:
        """*clause*'s template's own roles (:data:`OWN_ROLES`): none for a row in cells or of the Action cell."""
        spec = self.book.spec(clause.template)
        return set(spec.roles or []) & set(OWN_ROLES) if spec is not None else set()

    def _labels(self, label: str) -> List[str]:
        """The buttons a ``buttons`` row's *label* names, split at the reading's separators (punctuation as
        written, a word where it stands alone; ``YES and NO``, ``A, B or C``), unquoted."""
        between = [
            (r"\b%s\b" if each[:1].isalnum() else "%s") % re.escape(each)
            for each in (s.strip() for s in self.reading.separators or ())
            if each
        ]
        parts = re.split(r"\s*(?:%s)\s*" % "|".join(between), label or "", flags=re.I) if between else [label or ""]
        return [part.strip().strip("'\"") for part in parts if part.strip().strip("'\"")]

    def _caption(self, text: str) -> str:
        """What *text* says its picture shows (the reading's ``caption``'s ``what``), capitalized; *text* where it
        does not say."""
        text = (text or "").strip()
        found = re.search(self.reading.caption, text, re.I) if self.reading.caption else None
        what = found.group("what").strip() if found and found.group("what") else text
        return what[:1].upper() + what[1:]

    def fold(self, asks: set) -> None:
        """The rows of sentences the format needs no row for, folded by their templates' own roles into the rows and
        steps they belong to (:data:`OWN_ROLES`), each kept as a note of its words alone, and what they say held to
        the rows they name: a page's text, its buttons, a picture's caption, a travel after a choice, the quiz's
        step, a span shown and ended.

        Parameters:
            asks: The IDs of the steps that ask a question, for a ``questions`` row's ``to``.
        """
        page_field = self.reading.page
        page = ReadingSpec.header(self.template, self.reading.source_of(page_field) or page_field)
        contents = self.columns["contents"]
        order = [s.id for s, _, _, _ in self.records]
        # Within a step, first: its page's text, then the buttons on the page.
        for role in ("page", "buttons"):
            for step, action, clause, row_no in self.records:
                if role not in self._own(clause):
                    continue
                label = str(clause.fields.get("label") or "")
                if role == "page":
                    if page_field and label and not (step.fields.get(page_field) or "").strip():
                        step.fields[page_field] = label
                    continue
                offered = {
                    str(a.fields.get("label") or "").casefold()
                    for s, a, c, _ in self.records
                    if s is step and a is not action and "advances" in self.book.roles_of(c)
                }
                said = []
                names = self._labels(label)
                if not names:
                    said.append((contents, "it names no button: name them, or remove the row"))
                for name in names:
                    if name.casefold() not in offered:
                        said.append((contents, "the page's '%s' button: no row of the step says what it does: write "
                                               "the row it is, or take it from this row" % name))
                if page_field and not (step.fields.get(page_field) or "").strip():
                    said.append((contents, "%s'%s' is shown on the step's %s, which is empty: write its text in %s"
                                 % (WARNING, action.text, page, page)))
                self._state(action, said, row_no)
        # Then down the sheet: what each row's own roles say of the rows before and after it.
        ended: set = set()  # the steps a choice that ends them has come in
        pending: Dict[str, Dict[str, Any]] = {}  # word -> a span shown for its next row: {setting, step, until, record}
        showing: List[Dict[str, Any]] = []  # each span still shown: {word, setting, action, step}
        for at, (step, action, clause, row_no) in enumerate(self.records):
            own = self._own(clause)
            sentence = clause.template != "cells"
            if sentence and action.behavior in pending:  # the next row of the word a span was shown for: its own
                span = pending.pop(action.behavior)
                action.settings[span["setting"]] = span["until"] or ptk.ManifestActions.SPAN_STEP
                showing.append({"word": action.behavior, "setting": span["setting"], "action": action, "step": step.id})
            if "captioned" in own and not action.fields.get("label"):
                action.fields["label"] = self._caption(action.text)
            if "after_choice" in own and step.id in ended:
                setting = (self.reading.order_by or {}).get(action.behavior)
                if setting:
                    action.settings[setting] = "end"
            if "questions" in own:
                later = next((s for s in order[at:] if s in asks and s != step.id), None)
                if later:
                    action.to = later
                else:
                    self._state(action, [(contents, "no step after it asks a question: write the questions, a step "
                                                    "per question, its options its rows, the right one marked")], row_no)
            spec = self.book.spec(clause.template)
            span = dict(spec.span or {}) if spec is not None else {}
            if "opens" in own:
                if action.behavior == span.get("word"):  # the row shows it itself
                    showing.append({"word": span["word"], "setting": span["setting"], "action": action, "step": step.id})
                else:
                    pending[span["word"]] = {"setting": span["setting"], "step": step.id, "until": "", "record": (action, row_no)}
            if "closes" in own:
                waiting = pending.get(span["word"])
                shown = next((s for s in reversed(showing) if (s["word"], s["setting"]) == (span["word"], span["setting"])), None)
                if waiting is not None and waiting["setting"] == span["setting"]:
                    waiting["until"] = ptk.ManifestActions.SPAN_STEP if waiting["step"] == step.id else step.id
                elif shown is not None:
                    showing.remove(shown)
                    shown["action"].settings[span["setting"]] = (
                        ptk.ManifestActions.SPAN_STEP if shown["step"] == step.id else step.id
                    )
                else:
                    self._state(action, [(contents, "%snothing an earlier row showed is still shown for it to take "
                                                    "away: remove the row" % WARNING)], row_no)
            if "ends_step" in self.book.roles_of(clause):
                ended.add(step.id)
            if action.behavior == "note" and own:  # folded: its words alone
                action.fields, action.targets, action.settings = {}, [], {}
        for word, span in pending.items():
            action, row_no = span["record"]
            self._state(action, [(contents, "%sno %s row after it shows what it shows: remove the row, or add the %s "
                                            "row" % (WARNING, word, word))], row_no)


class _SheetPlanInternal:
    """Internal helpers for :class:`~unitytk.sheets.SheetPlan`: the reading pass."""

    @staticmethod
    def _source_name(sheet: str) -> str:
        if "://" in sheet:
            return Path(urlparse(sheet).path).name or sheet
        return Path(sheet).name

    @staticmethod
    def _template(mapping) -> Dict[str, Any]:
        """The mapping template *mapping* names (the default where None), or a copy of the one given."""
        from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

        from unitytk.sheets.sheet_plan import DEFAULT_TEMPLATE

        if mapping is None or isinstance(mapping, str):
            return Mapping.load_mapping(mapping or DEFAULT_TEMPLATE)
        return dict(mapping)

    @classmethod
    def _steps(cls, sheet: str, mapping, options, base=None):
        """``(steps, template)``: *sheet* read by its template (pythontk's ``Mapping.resolve``), the
        template naming each column as the sheet itself does (:meth:`ReadingSpec.as_written`), so every
        issue names its cell by the sheet's own header."""
        from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

        template = cls._template(mapping)
        steps = Mapping.resolve(sheet, template, options=dict(options or {}), base=base)
        own = next((s.headers for s in steps if getattr(s, "headers", None)), {})
        return steps, ReadingSpec.as_written(template, own)

    @staticmethod
    def _reading(mapping, reading) -> ReadingSpec:
        """The reading *reading* says (:meth:`SheetPlan.from_sheet`), over the one *mapping* names."""
        from unitytk.sheets.sheet_plan import DEFAULT_TEMPLATE

        if isinstance(reading, ReadingSpec):
            return reading
        if isinstance(reading, str):
            return ReadingSpec.load(reading)
        name = (
            (mapping or DEFAULT_TEMPLATE)
            if mapping is None or isinstance(mapping, str)
            else None
        )
        return ReadingSpec.load(name, reading)

    @staticmethod
    def _book(reading: ReadingSpec, folder=None) -> ClauseBook:
        """The built-in clause templates (and *folder*'s), with *reading*'s step shape and question mark."""
        return ClauseBook.for_reading(reading, folder)

    @staticmethod
    def _sound(step, base) -> str:
        """The step's sound file as its audio methods found it, relative to *base* (``/`` joined);
        empty where none was found."""
        found = next(
            (
                o.source_path
                for o in step.objects
                if o.kind == "audio" and o.source_path
            ),
            "",
        )
        if not found:
            return ""
        if base:
            try:
                found = os.path.relpath(found, base)
            except ValueError:  # another drive: keep it whole
                pass
        return found.replace(os.sep, "/")

    @staticmethod
    def _clauses(step, book: ClauseBook) -> List[Clause]:
        hint = step.pass_through.get("hint", "")
        rows = [(line, "contents") for line in step.lines]
        rows += [(line, "hint") for line in hint.splitlines()]
        out: List[Clause] = []
        for line, column in rows:
            if not line.strip():
                continue
            out.extend(book.read(line, column) or [Clause(UNREAD, line.strip())])
        return out

    @staticmethod
    def _numbered_takes(step_id: str, count: int) -> List[str]:
        """A sheet without take cells' names for a step's *count* takes, as the Shot Manifest names its
        shots: the step's ID for one, ``<step>_<n>`` for several."""
        return [step_id] if count == 1 else [f"{step_id}_{i + 1}" for i in range(count)]

    @classmethod
    def _take_list(
        cls, steps, template, reading: ReadingSpec, book: ClauseBook
    ) -> List[dict]:
        """``[{take, step, row, text}]``: every take *steps* name, in sheet order, as the reading pass names
        them (a strict sheet's per row, ``row`` counted in its step; any other's by its clip rows, ``row``
        None)."""
        out = []
        rows = (
            _RowReading(DeliveryIndex([]), book, reading, None, template)
            if reading.strict
            else None
        )
        for step in steps:
            if rows:
                items = rows.clauses(step)
                takes = rows.takes(
                    step.step_id, [(n, r) for c, n, r, _ in items if c.clip and n]
                )
                out += [
                    {
                        "take": take,
                        "step": step.step_id,
                        "row": n,
                        "text": (step.rows[n - 1].get("text") or "").strip(),
                    }
                    for n, (take, _) in sorted(takes.items())
                    if take
                ]
            else:
                clips = [c for c in cls._clauses(step, book) if c.clip]
                out += [
                    {"take": take, "step": step.step_id, "row": None, "text": c.text}
                    for c, take in zip(
                        clips, cls._numbered_takes(step.step_id, len(clips))
                    )
                ]
        return out

    @staticmethod
    def _cell(row_no: Optional[int], column: str, text: str) -> str:
        """An issue as the plan states it: its cell first (``row 3 (Asset Names): ...``; a hint line's,
        ``(Hint): ...``), as :meth:`SheetPlan.parse_issue` reads it."""
        return (
            "row %s (%s): %s" % (row_no, column, text)
            if row_no
            else "(%s): %s" % (column, text)
        )

    @staticmethod
    def _take_checks(name, step_id, step_ids, index, last_take, clip: str):
        """What is wrong with take *name* as step *step_id*'s: a name another step owns, a step the export
        records for it that is another, a place in the file before a take the sheet plays earlier (each
        issue's column *clip*)."""
        out = []
        if name in step_ids and name != step_id:
            out.append(
                (
                    clip,
                    f"the take {name} is named like step {name}: name it after its own step",
                )
            )
        # A take built as its own shot is recorded under its own name, which says its step.
        recorded = index.steps.get(name)
        if recorded and recorded not in (step_id, name):
            out.append(
                (
                    clip,
                    f"the export records the take {name} as step {recorded}'s: re-export it from its own step",
                )
            )
        if last_take and index.takes.index(name) < last_take[0]:
            out.append(
                (
                    clip,
                    f"the take {name} comes before {last_take[1]} in the file but after it in the sheet: "
                    "re-export the takes in the sheet's order",
                )
            )
        return out

    @classmethod
    def _read(
        cls,
        sheet: str,
        delivery: Union[str, DeliveryIndex],
        *,
        mapping=None,
        reading=None,
        options: Optional[Mapping[str, Any]] = None,
        aliases: Optional[Mapping[str, str]] = None,
        book: Optional[ClauseBook] = None,
        base: Optional[str] = None,
        retired: Iterable[str] = (),
    ):
        """:meth:`SheetPlan.from_sheet`: the sheet read by its template, then :meth:`_read_steps`."""
        index = (
            delivery
            if isinstance(delivery, DeliveryIndex)
            else DeliveryIndex.from_file(delivery)
        )
        steps, template = cls._steps(sheet, mapping, options, base)
        reading = cls._reading(mapping, reading)
        return cls._read_steps(
            steps,
            template,
            reading,
            index,
            sheet=sheet,
            aliases=aliases,
            book=book or cls._book(reading),
            base=base,
            retired=retired,
        )

    @classmethod
    def _read_steps(
        cls,
        steps,
        template,
        reading: ReadingSpec,
        index: DeliveryIndex,
        *,
        sheet: str,
        aliases: Optional[Mapping[str, str]],
        book: ClauseBook,
        base: Optional[str],
        retired: Iterable[str] = (),
    ):
        """The plan of *steps* (a sheet as its template read it) over *index*, read by *reading*; a jump to a
        *retired* step ID (as the sheet writes it) is the row's issue."""
        from unitytk.sheets.sheet_plan import PlanSource

        suffixes = (reading.names or {}).get("suffixes")
        if (
            suffixes is not None
        ):  # the reading's build suffixes, over the delivery's own
            index = index.with_suffixes(suffixes)
        reader = _ReadingPass(
            index,
            book,
            reading,
            template,
            aliases,
            dict.fromkeys(s.step_id for s in steps),
            base,
            {ptk.StrUtils.to_legal_name(r.upper()) for r in retired},
        )
        plan_steps = [reader.step(step) for step in steps]
        # The steps that ask a question: one written as one (its rows marked), or holding a wrong answer's choice.
        asks = {s.step_id for s in steps if reader.rows and reader.rows.is_question(s)}
        asks |= {s.id for s in plan_steps if any(a.to == QUESTION[1] for a in s.actions)}
        reader.fold(asks)
        plan_issues = list(index.issues) + [
            f"no step plays the take {t}: play it from its row, or have the artist remove it"
            for t in index.takes
            if t not in reader.played
        ]
        # A step row's own cells (its To, its Settings), as the sheet's reader found them.
        plan_issues += [
            "step %s (%s): %s" % (step.step_id, header, text)
            for step in steps
            for header, text in getattr(step, "issues", ())
        ]
        return cls(
            version=cls.VERSION,
            source=PlanSource(
                sheet=cls._source_name(sheet),
                delivery=index.name,
                sheet_digest=cls.digest(sheet),
                clauses_digest=book.digest(),
            ),
            steps=plan_steps,
            issues=plan_issues,
        )
