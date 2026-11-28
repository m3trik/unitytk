# !/usr/bin/python
# coding=utf-8
"""SheetConverter -- a sheet written before a format, rewritten in it.

The old sheet is read the tolerant way (by default the ``default`` template and its options)
against its delivery, and each row is written back in the target format (by default the
Interactive one) with what that reading bound: the names its words named in the names column (the
object's name, or the node's where two chains share it; every node a row's words name, and an
animation row's tool and the parts its take moves too, as the format asks), its take in the take
column, and each hint line whose phrase has the ``page`` role moved to the page column (its quoted
text). Every row's wording stays as written, so the strict reading of the result binds what the
tolerant one did; what it could not fill (a phrase naming no node, a row no phrase reads) is listed
by cell for the author (the strict check reports it too), as is each value the format has no column
for (a hint line where it has no hint column: a line moves to the page only where it has one).

The target is data: its columns are the target reading's :attr:`ReadingSpec.columns`, each headed as
the target template first names it, and a step ID is written as the target template's step pattern
reads it back.

By default the result is the format's cells (the reading's ``cell_columns``, written by
``_CellWriting``): a step's own row, then a row per action of the plan, already in the sheet's
words: its word, its names, its take, its Label and To and its settings, each row's old wording kept
in its Notes (which nothing reads), a sentence the format needs no row for a ``note``, a row no
phrase reads keeping its words and taking no word. Read again, the cells read to the plan the
sentences did. This is the one place a sentence becomes cells: a person reviews its guesses once,
and no later read guesses again. It never invents a value the old sheet does not say (a state, a
viewpoint): that row is listed for a person instead.
"""

import csv
import re
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional

import pythontk as ptk

from unitytk.sheets._cell_writing import _CellWriting
from unitytk.sheets._row_reading import UNREAD
from unitytk.sheets.delivery_index import DeliveryIndex
from unitytk.sheets.reading_spec import ReadingSpec
from unitytk.sheets.sheet_plan import DEFAULT_TEMPLATE, SheetPlan

#: The template an old sheet is read with where none is named: the tolerant one.
SOURCE_TEMPLATE = "default"


class _SheetConverterInternal:
    """Internal helpers for :class:`SheetConverter`."""

    @staticmethod
    def _name(index: DeliveryIndex, path: str) -> str:
        """The name a row lists for the node *path*: its object's, else its node's, else the end of its path that
        tells it apart (``DeliveryIndex.shortest``)."""
        leaf = path.rsplit("|", 1)[-1]
        if index.find(index.name_of(leaf)) == [path]:
            return index.name_of(leaf)
        return index.shortest(path)

    @staticmethod
    def _moved_parts(index: DeliveryIndex, take: str, listed: List[str]) -> List[str]:
        """The nodes *take* moves that no node of *listed* holds or sits in, top-most first."""
        out: List[str] = []
        for node in sorted(index.moved.get(take, ()), key=lambda p: p.count("|")):
            if not any(
                node == p or node.startswith(p + "|") or p.startswith(node + "|")
                for p in listed + out
            ):
                out.append(node)
        return out

    @staticmethod
    def _spelling(step_id: str, patterns: List["re.Pattern"]) -> str:
        """*step_id* (legal-named) as the target template's step patterns read it back: as it is, else with
        an underscore read as a full stop (``B03_5`` -> ``B03.5``), the last first."""
        candidates = [step_id] + [
            step_id[:i] + "." + step_id[i + 1 :]
            for i in sorted(
                (i for i, c in enumerate(step_id) if c == "_"), reverse=True
            )
        ]
        for candidate in candidates:
            for pattern in patterns:
                match = pattern.match(candidate)
                if (
                    match
                    and ptk.StrUtils.to_legal_name(
                        match.group(1) if pattern.groups else candidate
                    )
                    == step_id
                ):
                    return candidate
        return step_id

    @classmethod
    def _rows(
        cls,
        step,
        actions,
        book,
        index: DeliveryIndex,
        hand: str,
        left: List[Dict[str, Any]],
    ):
        """``(rows, hint actions)``: each of *step*'s rows as ``(text, names, take)`` paired with the
        actions the tolerant reading made of it, the issues it left noted in *left*."""
        out, at = [], 0
        for n, row in enumerate(step.rows, 1):
            text = (row.get("text") or "").strip()
            if not text:
                out.append((text, list(row.get("assets") or []), ""))
                continue
            clauses = book.read(text, "contents") or [None]
            names: Dict[str, None] = {}
            take = ""
            for clause, action in zip(clauses, actions[at : at + len(clauses)]):
                nodes = clause.nodes if clause is not None else []
                paths = list(action.targets)
                for g in nodes:  # a node group's path (`where`'s, one per target)
                    value = action.fields.get(g) if g != hand else None
                    paths += [v for v in (value if isinstance(value, list) else [value]) if v]
                tool = action.fields.get(hand)
                if tool and (
                    action.clip or (clause is not None and clause.fields.get(hand))
                ):
                    paths.append(
                        tool
                    )  # an animation row lists its tool, the hand's too
                if action.clip:
                    paths += cls._moved_parts(index, action.clip, paths)
                names.update(dict.fromkeys(cls._name(index, p) for p in paths))
                take = take or action.clip or ""
                if action.behavior == UNREAD:
                    left.append(
                        {
                            "step": step.step_id,
                            "row": n,
                            "text": text,
                            "why": "no phrase reads it",
                        }
                    )
                elif action.issues:
                    left.append(
                        {
                            "step": step.step_id,
                            "row": n,
                            "text": text,
                            "why": "; ".join(action.issues),
                        }
                    )
            at += len(clauses)
            out.append((text, list(names) or list(row.get("assets") or []), take))
        return out, actions[at:]

    @staticmethod
    def _hint(step, hint_actions, book, index, to_page: bool = True):
        """``(hint lines, page lines, names, moved)``: *step*'s hint lines kept, those whose phrase has the
        ``page`` role (or that no phrase reads) moved to the page where the format has one (*to_page*: their
        ``label``, else the line), the names the kept ones bind, and how many moved."""
        hint, page, hinted, moved = [], [], {}, 0
        for line in (
            each.strip() for each in step.pass_through.get("hint", "").splitlines()
        ):
            if not line:
                continue
            read = book.read(line, "hint")
            count = len(read or [None])
            if not to_page or (
                read and "page" not in book.roles_of(read[0])
            ):
                hint.append(line)
                for action in hint_actions[:count]:
                    hinted.update(
                        dict.fromkeys(
                            _SheetConverterInternal._name(index, p)
                            for p in action.targets
                        )
                    )
            else:
                page.append(read[0].fields.get("label") or line if read else line)
                moved += 1
            hint_actions = hint_actions[count:]
        return hint, page, hinted, moved


class SheetConverter(_SheetConverterInternal):
    """Rewrites an old sheet in a format (:meth:`convert`)."""

    @classmethod
    def convert(
        cls,
        sheet: str = "",
        delivery: str = "",
        out: str = "",
        mapping: str = SOURCE_TEMPLATE,
        settings: str = "",
        to: str = DEFAULT_TEMPLATE,
        options: Optional[Mapping[str, Any]] = None,
        ctx: Optional["ptk.OpContext"] = None,
        cells: bool = True,
    ) -> Dict[str, Any]:
        """Rewrite *sheet* in the format *to* at *out*, from what its tolerant reading against *delivery*
        bound.

        Parameters:
            sheet: The old sheet (CSV path or URL); empty where *settings* names it.
            delivery: Its delivery's FBX or GLB; empty where *settings* names it.
            out: The converted sheet's path (CSV).
            mapping: The old sheet's template (``default``: the tolerant reading).
            settings: The old sheet's settings file (:class:`SheetSettings`): its sheet, delivery
                (extra files too), template, options, renamed nodes and clause templates, in place of
                *sheet*, *delivery*, *mapping* and *options*.
            to: The format to write: a template whose reading lists its columns (``interactive``, ``linear``).
            options: The old sheet's template options (as :meth:`SheetPlan.from_sheet`).
            ctx: Asked before the file is written: a dry run writes nothing.
            cells: Write the format's cells (a step's own row, then a row per action with its word and
                its names: the reading's ``cell_columns``); False writes the older sheet of sentences.

        Returns:
            ``{file, steps, rows, moved, left}``: the file, the steps and rows written, the hint lines
            moved to the page, and ``[{step, row, text, why}]`` for each row it could not fill and each value
            the format has no column for.

        Raises:
            ValueError: No sheet, delivery or out is named, or *to* names a format with no columns.
        """
        if settings:
            from unitytk.sheets.sheet_settings import SheetSettings

            loaded = SheetSettings.load(settings)
            reading = loaded.reading()
            return cls._convert(
                loaded.sheet_path(),
                loaded.index(),
                out,
                loaded.template(),
                reading,
                loaded.aliases,
                to=to,
                options=loaded.options,
                book=loaded.book(reading),
                base=str(loaded.base),
                ctx=ctx,
                cells=cells,
            )
        if not (sheet and delivery and out):
            raise ValueError(
                "name the old sheet, its delivery and where to write: sheet=, delivery=, out= "
                "(or settings=<sheet settings file> and out=)"
            )
        return cls._convert(
            sheet,
            DeliveryIndex.from_file(delivery),
            out,
            mapping,
            None,
            to=to,
            options=options,
            ctx=ctx,
            cells=cells,
        )

    @classmethod
    def _convert(
        cls,
        sheet: str,
        index: DeliveryIndex,
        out: str,
        mapping,
        reading,
        aliases: Optional[Dict[str, str]] = None,
        *,
        to: str = DEFAULT_TEMPLATE,
        options: Optional[Mapping[str, Any]] = None,
        book=None,
        base: Optional[str] = None,
        ctx: Optional["ptk.OpContext"] = None,
        cells: bool = True,
    ) -> Dict[str, Any]:
        """:meth:`convert` over a read delivery, a template (a name or a loaded mapping) and its reading."""
        from pythontk.core_utils.engines.shots.manifest.mapping import (
            Mapping as SheetMapping,
        )

        if not out:
            raise ValueError("name where to write the converted sheet: out=<csv>")
        target, target_reading = SheetMapping.load_mapping(to), ReadingSpec.load(to)
        if not (target_reading.cell_columns if cells else target_reading.columns):
            raise ValueError(
                f"the format {to!r} lists no columns to write (its reading's columns): name another, "
                "or give its reading the columns"
            )
        steps, template = SheetPlan._steps(sheet, mapping, options, base)
        reading = SheetPlan._reading(mapping, reading)
        book = book or SheetPlan._book(reading)
        plan = SheetPlan._read_steps(
            steps,
            template,
            reading,
            index,
            sheet=sheet,
            aliases=aliases,
            book=book,
            base=base,
        )
        if cells:
            writer = _CellWriting(
                plan, index, target_reading.cell_columns, target, cls._moved_parts,
                audio={s.step_id: s.pass_through.get("audio_file", "") for s in steps},
            )
            table = [list(target_reading.cell_columns)] + writer.rows()
            path = Path(out)
            if ctx is None or ctx.should_write(str(path), "write the converted sheet"):
                path.parent.mkdir(parents=True, exist_ok=True)
                with open(path, "w", encoding="utf-8", newline="") as fh:
                    csv.writer(fh, lineterminator="\n").writerows(table)
            return {"file": str(path), "steps": len(steps), "rows": len(table) - 1, "moved": 0, "left": writer.left}
        planned = {s.id: s for s in plan.steps}
        columns = list(target_reading.columns)
        table: List[List[str]] = [[ReadingSpec.header(target, c) for c in columns]]
        left: List[Dict[str, Any]] = []
        moved, written, section = 0, 0, None
        patterns = [
            re.compile(p) for p in (target.get("columns") or {}).get("step_pattern", [])
        ]
        sections = [
            re.compile(p)
            for p in (target.get("columns") or {}).get("section_pattern", [])
        ]
        page = target_reading.source_of(target_reading.page) or target_reading.page
        own_page = reading.source_of(reading.page) or reading.page
        for step in steps:
            if (step.section, step.section_title) != section:
                section = (step.section, step.section_title)
                if step.section:
                    # A section with no title is written bare where the format reads it so (its own section patterns).
                    reads = any(
                        (m := p.match(step.section)) and m.group(1) == step.section
                        for p in sections
                    )
                    title = (
                        step.section
                        if reads and not step.section_title
                        else "SECTION %s: %s" % (step.section, step.section_title)
                    )
                    table.append([title] + [""] * (len(columns) - 1))
            actions = (
                list(planned[step.step_id].actions) if step.step_id in planned else []
            )
            rows, hint_actions = cls._rows(
                step, actions, book, index, reading.hand, left
            )
            hint, page_lines, hinted, count = cls._hint(
                step, hint_actions, book, index, page in columns
            )
            moved += count
            cells = step.pass_through
            if cells.get(own_page, "").strip():
                page_lines.insert(0, cells[own_page])
            listed = {name for _, names, _ in rows for name in names}
            extra = [
                name for name in hinted if name not in listed
            ]  # a hint's names, on a row of names alone
            if extra:
                rows.append(("", extra, ""))
            # The step's own cells, on its first row: the hint and the page as rewritten above.
            step_cells = {k: v for k, v in cells.items() if k not in ("hint", own_page)}
            step_cells.update(
                step_id=cls._spelling(step.step_id, patterns),
                step_name=cells.get("step_name", "") or step.name,
                audio=step.audio,
                hint="\n".join(hint),
            )
            step_cells[page] = "\n".join(page_lines)
            for i, (text, names, take) in enumerate(rows or [("", [], "")]):
                values = dict(
                    step_cells if i == 0 else {},
                    description=text,
                    assets="\n".join(names),
                    clip=take,
                    action="",
                )
                table.append([values.get(c, "") for c in columns])
                left += [
                    {
                        "step": step.step_id,
                        "row": i + 1,
                        "text": value,
                        "why": "the format has no %s column" % name,
                    }
                    for name, value in values.items()
                    if value and name not in columns
                ]
                written += 1

        path = Path(out)
        if ctx is None or ctx.should_write(str(path), "write the converted sheet"):
            path.parent.mkdir(parents=True, exist_ok=True)
            with open(path, "w", encoding="utf-8", newline="") as fh:
                csv.writer(fh).writerows(table)
        return {
            "file": str(path),
            "steps": len(steps),
            "rows": written,
            "moved": moved,
            "left": left,
        }
