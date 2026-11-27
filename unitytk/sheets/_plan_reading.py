# !/usr/bin/python
# coding=utf-8
"""How a sheet becomes a plan: :meth:`SheetPlan.from_sheet`'s reading pass, the
plan's internal base (:class:`_SheetPlanInternal`).

Per step, in sheet order: each row of its contents and of its hint is read by
the clause book (an unread row stays, as ``unread``); the rows that play a clip
are numbered to name their takes; the phrases of the rows whose roles say they
show something (``shows``, then ``points``) are matched first, as the step's
context; then every action's phrases are matched against the delivery, and the
hand is followed as the sheet runs (what a ``takes`` row puts in it, a
``releases`` or ``places`` row takes out), so a ``uses`` row done with what the
hand holds names it in its hand field. What a row does with its roles is the
clause templates' data (:attr:`ClauseSpec.roles`), never a behavior's name.
"""

import os
from pathlib import Path
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple, Union
from urllib.parse import urlparse

import pythontk as ptk

from unitytk.sheets._row_reading import UNREAD, WARNING, _RowReading, headers
from unitytk.sheets.clause_book import Clause, ClauseBook
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
                if role not in self.book.roles(clause.behavior, clause.settings):
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
        return PlanStep(
            id=step.step_id,
            name=columns.get("step_name", "") or step.name,
            section=step.section,
            section_title=step.section_title,
            fields=self._step_fields(step),
            actions=actions,
        )

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
        roles = self.book.roles(clause.behavior, clause.settings)
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
        for key, value in clause.fields.items():
            if key in ("targets", "to"):
                continue
            if key in clause.nodes:
                paths, why = self._bind(value, clause, row_of, context)
                issues.extend(why)
                if paths:
                    action.fields[key] = paths[0]
            else:
                action.fields[key] = value
        cells = clause.template == "cells"
        if "points" in roles and action.targets and self.reading.arrows not in action.fields:
            pattern = (self.reading.names or {}).get("arrow")
            arrows = [
                (self.index.arrows_at(t, pattern, step.step_id) or [""])[0]
                for t in action.targets
            ]
            if any(arrows):
                action.fields[self.reading.arrows] = arrows
        if self.rows and "destination" in roles and not cells:  # a cell row's With is its viewpoint, whatever its name
            for path in action.targets:
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
            target = ptk.StrUtils.to_legal_name(clause.fields["to"].upper())
            action.to = target
            action.restart = self.reading.restart and target == next(
                iter(self.step_ids), None
            )
            if target in self.retired:
                issues.append(
                    (
                        self.columns["contents"],
                        f"the jump goes to step {target}, which is retired: jump to the step that replaced it",
                    )
                )
            elif target not in self.step_ids and not cells:  # a cell row's To is held to the sheet where it is read
                issues.append(
                    (
                        self.columns["contents"],
                        f"the sheet has no step {target}: jump to a step the sheet has",
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
        """The built-in clause templates (and *folder*'s), with *reading*'s step shape."""
        return ClauseBook(folder=folder, step=reading.step_id)

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
        if reader.rows:
            reader.rows.cells.prime(steps)
        plan_steps = [reader.step(step) for step in steps]
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
