# !/usr/bin/python
# coding=utf-8
"""SheetCheck -- the strict check of a build sheet, alone or against its delivery.

A plain Python check: it reads the sheet itself and the FBX through pythontk's reader, so it needs
neither Unity nor a DCC. Run at each hand-off: on the sheet alone before it goes to the artist (the
sheet's own names stand for the delivery, and it writes the take list the artist builds from), and on
the sheet and the exported FBX before delivery. Every item names its cell (the step, the row counted in
it, the column by the template's own header), its severity, the reason and a ready fix: the reading
pass's issues (:meth:`SheetPlan.parse_issue`) and the sheet's own checks, each keyed by the roles the
clause templates declare (:attr:`ClauseSpec.roles`), never a behavior's name -- a column no field takes,
a jump back past a take (error) or forward past one (warning), a row that goes on by itself beside a
choice, an entered value with no row for any other, a question's one right answer, a list and its
ticks, a pointer whose part is gone.

A strict reading (Interactive, Linear) fails on an error; any other reports the same items and passes.
"""

import csv
import re
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Callable, Dict, Iterable, List, Mapping, Optional, Union

import pythontk as ptk

from unitytk.sheets._row_reading import QUESTION, _RowReading, headers
from unitytk.sheets.delivery_index import DeliveryIndex
from unitytk.sheets.reading_spec import ReadingSpec
from unitytk.sheets.sheet_plan import DEFAULT_TEMPLATE, SheetPlan


@dataclass
class CheckItem:
    """One thing the check found.

    Parameters:
        step: The step's ID; empty for the sheet's own (a column, the delivery).
        row: The row, counted in the step; None where the item names none.
        column: The column to fix (``Asset Names``); empty where none.
        severity: ``error``, ``warning``, or ``info`` (said, counted against nothing).
        reason: What is wrong.
        fix: What to do about it.
    """

    step: str
    row: Optional[int]
    column: str
    severity: str
    reason: str
    fix: str

    def line(self) -> str:
        """The item as one line of the report: its cell, severity, reason and fix."""
        cell = (
            " ".join(
                p
                for p in (
                    self.step,
                    "row %s" % self.row if self.row else "",
                    "(%s)" % self.column if self.column else "",
                )
                if p
            )
            or "sheet"
        )
        return "%s %s: %s%s" % (
            cell,
            self.severity,
            self.reason,
            " -- fix: %s" % self.fix if self.fix else "",
        )


class _SheetCheckInternal:
    """Internal helpers for :class:`SheetCheck`: the sheet's own checks."""

    @staticmethod
    def _sheet_index(
        steps, reading: ReadingSpec, takes: List[Dict[str, Any]], actions: Optional[Mapping[str, Any]] = None
    ) -> DeliveryIndex:
        """The delivery the sheet declares: every name its rows list or write inline, and its takes."""
        stated = (reading.names or {}).get("inline")
        inline = re.compile(stated) if stated else None
        names: Dict[str, None] = {}
        for step in steps:
            for row in step.rows:
                names.update(dict.fromkeys(row.get("assets") or []))
                if row.get("cells"):  # every object it names (its With, Members, `where`); its notes name nothing
                    names.update(dict.fromkeys(ptk.ManifestActions.objects(row, actions or {})))
                elif inline:
                    names.update(dict.fromkeys(inline.findall(row.get("text") or "")))
        return DeliveryIndex(
            list(names),
            [t["take"] for t in takes],
            "(the sheet)",
            suffixes=(reading.names or {}).get("suffixes"),
        )

    @staticmethod
    def _said(text: str, severity: str, step: str = "") -> CheckItem:
        """The item an issue's *text* is (:meth:`SheetPlan.parse_issue`): *step*'s, or the step the
        text names itself (a step row's own cell)."""
        parts = SheetPlan.parse_issue(text)
        return CheckItem(parts.pop("step") or step, severity=severity, **parts)

    @staticmethod
    def _row_of(step, text: str) -> Optional[int]:
        """The row of *step* whose text is *text* (counted in the step), or None (a hint line): a row in
        cells by the sentence its cells read as, any other by its own words."""
        for n, row in enumerate(step.rows, 1):
            said = (row.get("says") or row.get("word") or "") if row.get("cells") else (row.get("text") or "")
            if said.strip() == text.strip():
                return n
        return None

    @classmethod
    def _flow(cls, plan: SheetPlan, steps, context: "_Context") -> List[CheckItem]:
        """Jumps past takes, a row going on by itself beside a choice, an entered value and its otherwise row, a
        question's right answer, and the lists (as the clause templates' roles say)."""
        out: List[CheckItem] = []
        order = {s.id: i for i, s in enumerate(plan.steps)}
        plays = [any(a.clip for a in s.actions) for s in plan.steps]
        by_id = {s.step_id: s for s in steps}

        def add(step, action, severity, reason, fix, cell="action"):
            row = (
                cls._row_of(by_id[step.id], action.text)
                if action is not None and step.id in by_id
                else None
            )
            column = ""
            if row:
                # A row in cells: the cell the item is about, by the sheet's own header; else its sentence.
                own = by_id[step.id].rows[row - 1]
                column = (
                    (own.get("headers") or {}).get(cell) or cell.title()
                    if own.get("cells")
                    else context.columns["contents"]
                )
            out.append(CheckItem(step.id, row, column, severity, reason, fix))

        for i, step in enumerate(plan.steps):
            for a in step.actions:
                cls._jump(plan, i, a, order, plays, add)
            cls._step_rows(step, context, add)
        cls._lists(plan, context, add)
        cls._hand(plan, context, add)
        return out

    @staticmethod
    def _hand(plan: SheetPlan, context: "_Context", add: Callable) -> None:
        """A row done with a tool no row has put in the user's hand by its step, read in sheet order: a
        warning, since a jump may bring them there holding it. The hand's field is the reading's
        (``hand``); what takes, uses and gives back are the clause templates' roles."""
        field = context.reading.hand
        if not field:
            return
        held: set = set()
        for step in plan.steps:
            roles = [(a, context.roles(a)) for a in step.actions]
            taken = {t for a, r in roles if "takes" in r for t in a.targets}
            for a, r in roles:
                tool = a.field(field)
                if tool and "uses" in r and tool not in held | taken:
                    # Named as the sheet names it: a cell its node's own name, a sentence its object's.
                    name = (tool.rsplit("|", 1)[-1] if step.id in context.celled
                            else DeliveryIndex.object_name(tool, (context.reading.names or {}).get("suffixes")))
                    add(
                        step,
                        a,
                        "warning",
                        "no row puts %s in the user's hand by this step" % name,
                        "add a %s row for it, in this step or an earlier one" % context.names("takes"),
                        "with",
                    )
            held |= taken
            held -= {t for a, r in roles if "releases" in r for t in a.targets}

    @staticmethod
    def _jump(plan, i, a, order, plays, add) -> None:
        """A jump of step *i*'s row *a* back past a take (an error) or forward past one (a warning)."""
        if not a.to or a.to not in order:
            return  # a destination: restart loads the scene again, so nothing a take did stands
        to = order[a.to]
        step = plan.steps[i]
        if to <= i:
            past = [plan.steps[k].id for k in range(to, i) if plays[k]]
            if past:
                add(
                    step,
                    a,
                    "error",
                    "the jump back to step %s replays step %s, which plays a take"
                    % (a.to, past[0]),
                    "jump back to a step that plays no take, or restart the sequence",
                )
        else:
            past = [plan.steps[k].id for k in range(i + 1, to) if plays[k]]
            if past:
                add(
                    step,
                    a,
                    "warning",
                    "the jump to step %s skips step %s, which plays a take, so the scene stands "
                    "as it did before it" % (a.to, ", ".join(past)),
                    "restructure the steps, or accept it",
                )

    @staticmethod
    def _step_rows(step, context: "_Context", add) -> None:
        """A step's own rows checked against each other by their roles."""
        acts = step.actions
        roles = [context.roles(a) for a in acts]
        has = lambda role: [a for a, r in zip(acts, roles) if role in r]  # noqa: E731
        choices = has("advances")
        for goes_on in has("continues")[:1] if choices else []:
            add(
                step,
                goes_on,
                "error",
                "'%s' goes on by itself beside a choice that advances the step"
                % goes_on.text,
                "remove that row",
            )
        entered = has("entered")
        if entered and not has("otherwise"):
            cells = context.written(step, "otherwise")
            add(
                step,
                None if cells else entered[0],  # in cells it is a row the step lacks: the step's own
                "error",
                "no row of the step takes any other value",
                "add %s" % cells if cells
                else "add a row taken for any other value (%s)" % context.templates("otherwise"),
            )
        # A question: a step holding a wrong answer's choice, or written as one; exactly one answer of it right.
        right, wrong = QUESTION
        if any(a.to == wrong for a in choices) or step.id in context.questions:
            correct = sum(1 for a in choices if a.to == right)
            if correct != 1:
                cells = context.written(step, "option", {"to": right})
                add(
                    step,
                    None,
                    "error",
                    "the question has %s right answers" % correct if cells
                    else "the question marks %s options correct" % correct,
                    "write exactly one: %s" % cells if cells
                    else "mark exactly one option with a leading %s" % context.mark,
                )

    @staticmethod
    def _list_items(label) -> List[str]:
        """A list row's own items: its label split at commas and semicolons, unquoted. The Unity kit splits a list's
        label by the same rule as it builds the row (``Lists.Items``): it splits the label the row is built with,
        which a list behavior declaring ``label`` among its fields lets a person set in the manifest, past this
        reader, so the plan cannot carry the items for both."""
        return [
            i.strip().strip("'\"")
            for i in re.split(r"[,;]", label or "")
            if i.strip().strip("'\"")
        ]

    @classmethod
    def _lists(cls, plan: SheetPlan, context: "_Context", add: Callable) -> None:
        """A list naming no items whose tick rows (up to the next list) add none; a tick no list before it holds.
        Each is reported through *add*."""
        flat = [(s, a, context.roles(a)) for s in plan.steps for a in s.actions]
        lists = []  # the items of each list so far, latest last
        for n, (step, a, roles) in enumerate(flat):
            label = a.field("label")
            if "lists" in roles:
                items = cls._list_items(label)
                if not items:
                    for _, later, later_roles in flat[n + 1 :]:
                        if "lists" in later_roles:
                            break
                        if "ticks" in later_roles and later.field("label"):
                            items.append(later.field("label"))
                if not items:
                    add(
                        step,
                        a,
                        "error",
                        "the list holds no items, and no row ticking one follows it",
                        "list them after a colon, comma separated, or add its tick rows",
                    )
                lists.append({i.lower() for i in items})
            elif (
                "ticks" in roles
                and label
                and not any(label.lower() in items for items in lists)
            ):
                add(
                    step,
                    a,
                    "error",
                    "no list before it holds '%s'" % label,
                    "add the item to a list row before it, or correct the item's name",
                )

    @staticmethod
    def _retired(
        plan: SheetPlan, retired: Iterable[str], step_column: str
    ) -> List[CheckItem]:
        """A step carrying a retired ID (a jump to one is its row's issue, as the reading raised it)."""
        gone = {ptk.StrUtils.to_legal_name(r.upper()) for r in retired}
        return [
            CheckItem(
                s.id,
                None,
                step_column,
                "error",
                "the step ID %s is retired" % s.id,
                "give the step a new ID: a retired ID never returns",
            )
            for s in plan.steps
            if s.id in gone
        ]

    @staticmethod
    def _pointers(index: DeliveryIndex, names: Mapping[str, Any]) -> List[CheckItem]:
        """Each authored pointer of the delivery whose part it lacks (*names*' ``point`` rule; none stated, none
        known)."""
        if not names.get("point"):
            return []
        pattern = re.compile(names["point"])
        out = []
        for path in index.paths:
            leaf = path.rsplit("|", 1)[-1]
            match = pattern.search(leaf)
            if not match:
                continue
            part = match.groupdict().get("part")
            if part and not index.find(part):
                out.append(
                    CheckItem(
                        "",
                        None,
                        "",
                        "warning",
                        "the pointer %s points at %s, which the delivery lacks"
                        % (leaf, part),
                        "rename the pointer after its part, or remove it",
                    )
                )
        return out

    @staticmethod
    def _states(index: DeliveryIndex, steps, actions: Optional[Mapping[str, Any]]) -> List[CheckItem]:
        """Each row's setting its word declares a ``state`` (``set``'s) that a target's indicator light lacks, where
        the delivery's ``render_effects`` record gives the light its states (:meth:`DeliveryIndex.states_of`): one
        it gives none is Unity's build to say, which gives the track it makes the defaults."""
        out = []
        for step in steps:
            for n, row in enumerate(step.rows, 1):
                declared = ((actions or {}).get(row.get("word", "")) or {}).get("settings") or {}
                for name, value in (row.get("settings") or {}).items():
                    state = str(value).strip()
                    if (declared.get(name) or {}).get("type") != "state" or not state:
                        continue
                    for target in row.get("assets") or ():
                        found = index.named(target)
                        has = index.states_of(found[0]) if len(found) == 1 else []
                        if has and state not in has:
                            out.append(
                                CheckItem(
                                    step.step_id,
                                    n,
                                    (row.get("headers") or {}).get("settings") or "Settings",
                                    "warning",
                                    "%s has no state '%s': its indicator light has %s" % (target, state, ", ".join(has)),
                                    "write one of those, or give its emissive group the state in the DCC",
                                )
                            )
        return out

    @staticmethod
    def _empty_take_cells(
        steps, takes: List[Dict[str, Any]], clip: str
    ) -> List[CheckItem]:
        """Each animation row whose take cell (column *clip*) is empty (*takes*: the take list, each take's
        step and row; *steps*: the sheet as read, its rows' cells): a step's take cells are what give it a
        shot in the Shot Manifest (Shots: "Steps with an animation clip"), so an empty one leaves an animated
        step without one. The fix names the take the reading gives it."""
        by_id = {s.step_id: s for s in steps}
        out = []
        for take in takes:
            step, row = by_id.get(take["step"]), take.get("row")
            if step is None or not row or row > len(step.rows):
                continue
            if step.rows[row - 1].get("cells"):
                continue  # a row in cells: the format's own check says its empty Clip, at the sheet's header
            if (step.rows[row - 1].get("clip") or "").strip():
                continue
            out.append(
                CheckItem(
                    take["step"],
                    row,
                    clip,
                    "error",
                    "the animation row names no take, so the Shot Manifest gives the step no shot",
                    "write %s in %s" % (take["take"], clip),
                )
            )
        return out

    @staticmethod
    def _take_ends(
        index: DeliveryIndex, takes: List[Dict[str, Any]], clip: str
    ) -> List[CheckItem]:
        """Each take of the delivery that ends on the next take's setup (:attr:`DeliveryIndex.jumps`), one
        item a take, at the row that names it (*takes*, the sheet's take list; the delivery's own where no
        row does): played alone it shows the next step's first pose before its own is over."""
        rows = {t["take"]: t for t in takes}
        cuts: Dict[tuple, List[Mapping[str, Any]]] = {}
        for jump in index.jumps:
            cuts.setdefault((jump["take"], jump["next"]), []).append(jump)
        out = []
        for (take, after), found in cuts.items():
            first = min(found, key=lambda j: j["frame"])
            who = first["target"].split("#", 1)[0]
            if len(found) > 1:
                who += " and %d more" % (len(found) - 1)
            when = (
                "on its last frame"
                if first["shown"] == 0
                else "%d frames before it ends" % first["shown"]
            )
            row = rows.get(take)
            out.append(
                CheckItem(
                    row["step"] if row else "",
                    row["row"] if row else None,
                    clip if row else "",
                    "warning",
                    "the take %s ends on %s's first pose: %s jumps there at frame %d, %s"
                    % (take, after, who, first["frame"], when),
                    "cut %s before frame %d, or key that move in %s"
                    % (take, first["frame"], after),
                )
            )
        return out

    @classmethod
    def silent(cls, plan: SheetPlan, template, reading: ReadingSpec) -> List[str]:
        """The IDs of *plan*'s voiced steps that *template* (its options applied) narrates and that have
        no sound yet: the task is silent there until their lines are rendered or recorded. Empty
        where the template narrates nothing (its voice lines text alone).

        Parameters:
            plan: The plan the sheet read as.
            template: Its mapping template, its options applied (pythontk's ``Mapping.apply_options``).
            reading: Its reading (which plan fields hold a step's voice line and its sound).
        """
        voiced = cls._voiced(plan, template, reading)
        if voiced is None:
            return []
        sound = reading.field_of("sound")
        return [s.id for s in voiced[2] if not s.field(sound)]

    @staticmethod
    def _voiced(plan: SheetPlan, template, reading: ReadingSpec):
        """``(narrate, methods, voiced)``: the template's narrate method, all its audio methods and the
        plan's voiced steps; None where it narrates nothing."""
        methods = template.get("audio_resolve") or []
        methods = methods if isinstance(methods, list) else [methods]
        narrate = next((m for m in methods if m.get("method") == "narrate"), None)
        voice, sound = reading.field_of("audio"), reading.field_of("sound")
        if narrate is None or not voice or not sound:
            return None
        blanks = ReadingSpec.blanks(template)
        voiced = [
            s
            for s in plan.steps
            if (s.field(voice) or "").strip()
            and (s.field(voice) or "").strip().upper() not in blanks
        ]
        return narrate, methods, voiced

    @classmethod
    def _narration(
        cls, plan: SheetPlan, template, reading: ReadingSpec, columns: Mapping[str, str]
    ) -> List[CheckItem]:
        """Where the template narrates: each voiced step with no sound yet (a warning: the build would be
        silent there, :meth:`silent`), and, said once, how many steps speak narration standing in for
        recordings."""
        found = cls._voiced(plan, template, reading)
        if found is None:
            return []
        narrate, methods, voiced = found
        sound = reading.field_of("sound")
        folder = narrate.get("directory", "").replace("\\", "/").rstrip("/") + "/"
        recorded = next(
            (
                m.get("directory")
                for m in methods
                if m.get("method") in ("cell", "prefix") and m.get("directory")
            ),
            "",
        )
        silent = set(cls.silent(plan, template, reading))
        out = [
            CheckItem(
                s.id,
                None,
                columns["voice"],
                "warning",
                "the line is not narrated yet: the step would be silent",
                "run narration.render (python -m pythontk ops apply narration.render sheet=... options=... base=...)",
            )
            for s in voiced
            if s.id in silent
        ]
        synthetic = [
            s
            for s in voiced
            if (s.field(sound) or "").replace("\\", "/").startswith(folder)
        ]
        if synthetic:
            where = (
                "record them into %s/ (named by step ID, or in %s)"
                % (recorded, ReadingSpec.header(template, "audio_file"))
                if recorded
                else "record them"
            )
            out.append(
                CheckItem(
                    "",
                    None,
                    columns["voice"],
                    "info",
                    "%d of %d voiced steps speak synthetic narration"
                    % (len(synthetic), len(voiced)),
                    "%s: narration stays a stand-in until then" % where,
                )
            )
        return out


class _Context:
    """What the flow checks read a plan by: the reading, the clause book's roles and the column headers."""

    def __init__(self, reading: ReadingSpec, book, template: Mapping[str, Any]):
        self.reading = reading
        self.book = book
        self.columns = headers(template)
        self.mark = (reading.question or {}).get("mark", "")
        self.page = ReadingSpec.header(
            template, reading.source_of(reading.page) or reading.page
        )
        #: The steps whose rows are written in cells: their fixes say the word and its cells.
        self.celled: set = set()
        #: The steps of a sheet of sentences written as a question (a row of each led by the question mark).
        self.questions: set = set()

    def written(self, step, role: str, fields: Optional[Mapping[str, str]] = None) -> str:
        """How a row of *role* (carrying *fields*) is written in cells, from the clause template that reads one:
        ``an answer row whose Label is *``. Empty where *step* is not written in cells, or no template is such a
        row."""
        if step.id not in self.celled:
            return ""
        found = self.book.written(role, fields)
        if found is None:
            return ""
        word, cells = found
        said = ["%s is %s" % (k.title(), v) for k, v in cells.items()]
        return "a%s %s row%s" % ("n" if word[:1] in "aeiou" else "", word,
                                 " whose " + " and ".join(said) if said else "")

    def roles(self, action) -> set:
        """The roles of *action*: its word's, as its settings, fields and jump say (:meth:`ClauseBook.roles`)."""
        return set(self.book.roles(action.behavior, action.settings, action.fields, action.to))

    def names(self, role: str) -> str:
        """The words of *role*, ``/`` joined."""
        return "/".join(sorted(self.book.with_role(role))) or role

    def templates(self, role: str) -> str:
        """The templates declaring *role*, ``/`` joined."""
        return "/".join(self.book.templates_with(role)) or role


class SheetCheck(_SheetCheckInternal):
    """A sheet's strict check: what is wrong, cell by cell, and the take list.

    Parameters:
        items: What it found, in sheet order (the sheet's own first).
        takes: ``[{take, step, row, text}]``, :meth:`SheetPlan.take_list`'s.
        strict: Whether the reading is a strict one, so an error fails the check.
        sheet: The sheet's name.
        text_header: The header of the take list's text column (the template's own for a row's text).
    """

    def __init__(
        self,
        items: List[CheckItem],
        takes: List[Dict[str, Any]],
        strict: bool = True,
        sheet: str = "",
        text_header: str = "Text",
    ):
        self.items = list(items)
        self.takes = list(takes)
        self.strict = strict
        self.sheet = sheet
        self.text_header = text_header

    @classmethod
    def run(
        cls,
        sheet: str,
        delivery: Union[str, DeliveryIndex, None] = None,
        *,
        mapping: Union[str, Mapping[str, Any], None] = None,
        reading: Union[str, Mapping[str, Any], ReadingSpec, None] = None,
        options: Optional[Mapping[str, Any]] = None,
        aliases: Optional[Mapping[str, str]] = None,
        book=None,
        retired: Iterable[str] = (),
        base: Optional[str] = None,
    ) -> "SheetCheck":
        """Check *sheet*, against *delivery* where given.

        Parameters:
            sheet: The sheet (CSV path or URL), parsed once for every check.
            delivery: The FBX (path) or its :class:`DeliveryIndex`; None checks the sheet alone, its own
                names standing for the delivery.
            mapping: As :meth:`SheetPlan.from_sheet` (:data:`DEFAULT_TEMPLATE` where None).
            reading: As :meth:`SheetPlan.from_sheet`: its name rules are the check's too (``point``).
            options: As :meth:`SheetPlan.from_sheet`.
            aliases: As :meth:`SheetPlan.from_sheet`.
            book: As :meth:`SheetPlan.from_sheet`.
            retired: Step IDs the project has retired (as the sheet writes them): no step carries one, no
                jump goes to one.
            base: As :meth:`SheetPlan.from_sheet`: where the sheet's narration and recordings are found.
        """
        from pythontk.core_utils.engines.shots.manifest.mapping import (
            Mapping as SheetMapping,
        )

        steps, template = SheetPlan._steps(sheet, mapping, options, base)
        reading = SheetPlan._reading(mapping, reading)
        book = book or SheetPlan._book(reading)
        takes = SheetPlan._take_list(steps, template, reading, book)
        if delivery is None:
            index = cls._sheet_index(steps, reading, takes, (template.get("columns") or {}).get("actions"))
        else:
            index = (
                delivery
                if isinstance(delivery, DeliveryIndex)
                else DeliveryIndex.from_file(delivery)
            )
        plan = SheetPlan._read_steps(
            steps,
            template,
            reading,
            index,
            sheet=sheet,
            aliases=aliases,
            book=book,
            base=base,
            retired=retired,
        )
        context = _Context(reading, book, template)
        context.celled = {s.step_id for s in steps if s.rows and all(r.get("cells") for r in s.rows)}
        context.questions = {s.step_id for s in steps if _RowReading.marked(s, context.mark)}
        columns = context.columns

        items = [
            CheckItem(
                "",
                None,
                column,
                "warning",
                "no field of the template takes the column %s" % column,
                "rename it to a column the template reads, or add it to the template",
            )
            for column in SheetMapping.unmapped_columns(
                sheet, template, options=dict(options or {})
            )
        ]
        if not plan.steps:
            items.append(
                CheckItem(
                    "",
                    None,
                    ReadingSpec.header(template, "step_id"),
                    "error",
                    "the sheet holds no step",
                    "write a step's row: its ID in %s, then a row for each thing that happens in it"
                    % ReadingSpec.header(template, "step_id"),
                )
            )
        items += [cls._said(i, "error") for i in plan.issues if not SheetPlan.parse_issue(i)["step"]]
        ends = (
            cls._take_ends(index, takes, columns["clip"])
            if delivery is not None
            else []
        )
        if delivery is not None:
            # A sheet of sentences finds a pointer by its name; a sheet in cells names it in its Settings' where.
            if not all(row.get("cells") for step in steps for row in step.rows):
                items += cls._pointers(index, reading.names or {})
            items += [i for i in ends if not i.step]
        order = {s.id: i for i, s in enumerate(plan.steps)}
        found = cls._flow(plan, steps, context)
        found += cls._retired(plan, retired, ReadingSpec.header(template, "step_id"))
        found += [i for i in ends if i.step]
        found += cls._narration(
            plan,
            SheetMapping.apply_options(template, dict(options or {})),
            reading,
            columns,
        )
        found += cls._empty_take_cells(steps, takes, columns["clip"])
        if delivery is not None:
            found += cls._states(index, steps, (template.get("columns") or {}).get("actions"))
        # A step row's own cells (its ID, its To, its Settings): the step's, before its rows'.
        found += [cls._said(i, "error") for i in plan.issues if SheetPlan.parse_issue(i)["step"]]
        for step in plan.steps:
            for action in step.actions:
                found += [cls._said(i, "error", step.id) for i in action.issues]
                found += [cls._said(i, "warning", step.id) for i in action.warnings]
        found.sort(key=lambda i: (order.get(i.step, -1), i.row or 0))
        return cls(
            items + found, takes, reading.strict, plan.source.sheet, columns["contents"]
        )

    @property
    def errors(self) -> List[CheckItem]:
        """The items that are errors."""
        return [i for i in self.items if i.severity == "error"]

    @property
    def warnings(self) -> List[CheckItem]:
        """The items that are warnings."""
        return [i for i in self.items if i.severity == "warning"]

    @property
    def passed(self) -> bool:
        """True where nothing fails it: no error, or a tolerant reading."""
        return not (self.strict and self.errors)

    def text(self) -> str:
        """The report: a summary line, then one line per item."""
        head = "%s: %s (%s errors, %s warnings, %s takes)" % (
            self.sheet or "sheet",
            "passed" if self.passed else "failed",
            len(self.errors),
            len(self.warnings),
            len(self.takes),
        )
        return "\n".join([head] + ["  " + i.line() for i in self.items])

    def to_dict(self) -> Dict[str, Any]:
        """``{sheet, passed, errors, warnings, items, takes}``, JSON-safe."""
        return {
            "sheet": self.sheet,
            "passed": self.passed,
            "errors": len(self.errors),
            "warnings": len(self.warnings),
            "items": [asdict(i) for i in self.items],
            "takes": self.takes,
        }

    def write_takes(
        self, path: Union[str, Path], ctx: Optional["ptk.OpContext"] = None
    ) -> Path:
        """Write the take list as a CSV (Take, Step, Row, and the row's text) the artist builds from.

        Parameters:
            path: Where.
            ctx: Asked before the write (``should_write``): a dry run writes nothing.
        """
        path = Path(path)
        if ctx is not None and not ctx.should_write(str(path), "write the take list"):
            return path
        path.parent.mkdir(parents=True, exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="") as fh:
            out = csv.writer(fh)
            out.writerow(["Take", "Step", "Row", self.text_header])
            out.writerows(
                [t["take"], t["step"], t["row"] or "", t["text"]] for t in self.takes
            )
        return path

    @classmethod
    def check(
        cls,
        sheet: str = "",
        delivery: str = "",
        mapping: str = DEFAULT_TEMPLATE,
        takes: str = "",
        settings: str = "",
        ctx: Optional["ptk.OpContext"] = None,
    ) -> Dict[str, Any]:
        """Check a build sheet: alone, or against its delivery (the FBX or GLB), naming each problem's cell and
        fix; optionally write the take list the artist builds from.

        Parameters:
            sheet: The sheet (CSV path or URL); empty where *settings* names it.
            delivery: The delivery's FBX or GLB; empty checks the sheet alone.
            mapping: The template's name (``interactive``, ``linear``, ``default`` or a user's).
            takes: Where to write the take list (CSV); empty writes none.
            settings: A sheet settings file (:class:`SheetSettings`): its sheet, delivery, template, names,
                clause templates and retired IDs, in place of *sheet*, *delivery* and *mapping*.
            ctx: Asked before the take list is written: a dry run writes nothing.

        Returns:
            :meth:`to_dict`, plus ``report`` (:meth:`text`) and ``takes_file`` where one was written.

        Raises:
            ValueError: Neither *sheet* nor *settings* names a sheet.
        """
        if settings:
            from unitytk.sheets.sheet_settings import SheetSettings

            result = SheetSettings.load(settings).check()
        elif sheet:
            result = cls.run(sheet, delivery or None, mapping=mapping)
        else:
            raise ValueError(
                "name the sheet to check: sheet=<csv>, or settings=<sheet settings file>"
            )
        out = result.to_dict()
        out["report"] = result.text()
        if takes:
            out["takes_file"] = str(result.write_takes(takes, ctx))
        return out
