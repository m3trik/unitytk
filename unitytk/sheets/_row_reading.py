# !/usr/bin/python
# coding=utf-8
"""How a sheet read strictly (its reading's ``strict``: the Interactive and Linear ones) is read row by
row: the reading pass's internal half for such sheets.

Each row is read with its own cells: its text by the clause book, or, where no phrase reads it, by
its Action cell; a phrase's words bind only to the names the same row lists in its names column (or
to a scene name written inline), so a match cannot wander to another part; a modifier the phrase
carries that contradicts the name it matched ("the bottom latch" against ``LATCH_UPPER``) is an
error, as is a row nothing reads, but in a question (a step with a row written with the reading's
question mark, ``* ...``), whose rows no phrase reads are its options, the marked one correct. Each
animation row's take is checked by what it moves, read from the delivery's keys: a take moving
another row's names and none of its own is an error, one moving none of its own a warning (a name
two rows list counts for neither). Every issue is stated with its column (``(column, text)``, the
column the template's own header for it, the text a reason and its fix, ``<reason>: <fix>``), and
the plan names its row in the step: the sheet's cell to fix (:meth:`SheetPlan.parse_issue`).
"""

import re
from collections import Counter
from typing import Dict, List, Mapping, Optional, Sequence, Tuple

import pythontk as ptk

from unitytk.sheets._cell_reading import _CellReading
from unitytk.sheets.clause_book import Clause, ClauseBook
from unitytk.sheets.delivery_index import DeliveryIndex
from unitytk.sheets.node_matcher import NodeMatcher
from unitytk.sheets.reading_spec import ReadingSpec

#: The behavior of a row no template reads: the plan keeps it, with its issue.
UNREAD = "unread"
#: A collaborator's mark of an issue that is a warning (``warning: <reason>: <fix>``): the plan lists it apart.
WARNING = "warning: "
#: The columns an issue names, by the template's field name, and the header each falls back to.
COLUMNS = {
    "contents": "description",
    "assets": "assets",
    "action": "action",
    "clip": "clip",
    "hint": "hint",
    "voice": "audio",
}
#: One issue: the column to fix and the text (``<reason>: <fix>``).
Issue = Tuple[str, str]


def headers(template: Optional[Mapping]) -> Dict[str, str]:
    """``{column: header}`` the issues name (:data:`COLUMNS`): the template's own header for each
    (``contents``: ``Step Contents``), its field name where the template knows none."""
    return {
        key: ReadingSpec.header(template or {}, field) for key, field in COLUMNS.items()
    }


class _RowReading:
    """One strict reading: the delivery, the clause book, the reading and the columns its issues name.

    Parameters:
        index: The delivery's nodes and takes.
        book: The clause templates.
        reading: The reading (its name rules, Action words, question mark, vocabulary).
        aliases: ``{phrase: node}`` where words name a node no rule finds.
        template: The sheet's mapping template: the headers its issues name.
    """

    def __init__(
        self,
        index: DeliveryIndex,
        book: ClauseBook,
        reading: ReadingSpec,
        aliases: Optional[Mapping[str, str]],
        template: Optional[Mapping] = None,
    ):
        self.index = index
        self.book = book
        self.reading = reading
        names = reading.names or {}
        # Each rule applies only where the reading states it.
        self.inline = re.compile(names["inline"]) if names.get("inline") else None
        self.viewpoint = (
            re.compile(names["viewpoint"]) if names.get("viewpoint") else None
        )
        self.aliases = aliases
        self.columns = headers(template)
        self.matcher = NodeMatcher(DeliveryIndex([]), vocabulary=reading.vocabulary)
        # A row in the cell format is read by the reading's words, never by a phrase.
        actions = ((template or {}).get("columns") or {}).get("actions")
        self.cells = _CellReading(reading, index, WARNING, UNREAD, actions)
        self.cell_headers = {
            "action": self.columns["action"], "target": self.columns["assets"], "clip": self.columns["clip"],
            "with": "With", "members": "Members", "label": "Label", "to": "To", "settings": "Settings",
        }
        # The Action cell's words: every behavior the book reads (a project's own too), each reading that gives its
        # behavior settings (by its template's name), and the reading's own words; each with the settings it gives.
        self.actions = {b.replace("_", " "): (b, {}) for b in book.behaviors()}
        readings = book.readings()
        for name, (behavior, settings) in readings.items():
            if settings:
                self.actions.setdefault(name.replace("_", " "), (behavior, settings))
        for word, template in (reading.action_words or {}).items():
            if template in readings:  # a word for a template this book lacks is no word
                self.actions[word] = readings[template]

    # ---- rows -> clauses ----------------------------------------------------------------------------------------

    def _option(self, text: str, row: int, cells: Dict, issues: List[Issue]):
        """A question's row *text* read as its option (the reading's question behavior), the marked one correct."""
        question = self.reading.question or {}
        mark = question.get("mark", "")
        correct = bool(mark) and text.startswith(mark)
        label = text[len(mark) :].strip() if correct else text
        fields = {question.get("label", "label"): label}
        if correct:
            fields[question.get("value", "value")] = question.get("correct", "correct")
        return (
            Clause(question.get("behavior", "option"), text, fields),
            row,
            cells,
            issues,
        )

    def clauses(self, step) -> List[Tuple[Clause, int, Dict, List[Issue]]]:
        """``[(clause, row number, row, issues)]`` for every row of *step*, in order; a row nothing
        reads is an ``unread`` clause with its issue.

        Parameters:
            step: A ``BuilderStep`` whose ``rows`` carry each row's cells (``text``, ``assets``,
                ``action``, ``clip``).
        """
        out = []
        placed: List[Tuple[str, str]] = []  # each item's (order, the take it plays after), where a cell row's
        # What a span of this step's rows shows, first: it is there before the row that set it happens.
        for clause in self.cells.openings.get(step.step_id, ()):
            out.append((clause, None, {"cells": True, "assets": []}, []))
            placed.append(("start", ""))
        brought: set = set()
        mark = (self.reading.question or {}).get("mark", "")
        # A step with a row written with the question mark is a question: its rows no phrase reads are its options.
        question = bool(mark) and any(
            (r.get("text") or "").lstrip().startswith(mark) for r in step.rows
        )
        for n, row in enumerate(step.rows, 1):
            if row.get("cells"):
                read, issues, order, after = self.cells.read(row, step.rows, self._heads(row), step.step_id)
                for i, clause in enumerate(read):
                    last = i == len(read) - 1
                    if not last and clause.behavior in brought:
                        continue  # what a case brings, its step has once
                    if not last:
                        brought.add(clause.behavior)
                    out.append((clause, n, row, issues if last else []))
                    placed.append((order, after) if last else ("", ""))
                continue
            text = (row.get("text") or "").strip()
            action = (row.get("action") or "").strip().lower()
            read = self.book.read(text) if text else []
            issues: List[Issue] = []
            if question and text and not read and not action:
                out.append(self._option(text, n, row, issues))
                placed.append(("", ""))
                continue
            if action:
                read = self._action_cell(action, text, row, read, issues)
            if not read:
                if not text and not action:
                    continue  # a row of names alone lists more objects for the step
                read = [Clause(UNREAD, text)]
                issues.append(
                    (
                        self.columns["contents"],
                        "no phrase reads it and no %s names it: write it as a phrase, "
                        "or fill %s" % (self.columns["action"], self.columns["action"]),
                    )
                )
            for i, clause in enumerate(read):
                out.append((clause, n, row, issues if i == 0 else []))
                placed.append(("", ""))
        # What ends in this step: a thing an earlier step showed goes as this one starts, its own as it ends.
        for clause, from_another in self.cells.closings.get(step.step_id, ()):
            out.append((clause, None, {"cells": True, "assets": []}, []))
            placed.append(("start" if from_another else "end", ""))
        return self._ordered(out, placed)

    @staticmethod
    def _ordered(items: List, placed: List[Tuple[str, str]]) -> List:
        """*items* as their step runs them: a cell row's order carries no meaning, so one its word places
        at its step's start or end stands there, and one that plays after a take follows that take's row."""
        if not any(order or after for order, after in placed):
            return items
        paired = list(zip(items, placed))
        out = [p for p in paired if p[1][0] == "start"]
        out += [p for p in paired if p[1][0] not in ("start", "end")]
        out += [p for p in paired if p[1][0] == "end"]
        for pair in [p for p in out if p[1][1]]:
            lead = next((p for p in out if p is not pair and p[0][0].clip and (p[0][2].get("clip") or "") == pair[1][1]), None)
            if lead is not None:
                out.remove(pair)
                out.insert(out.index(lead) + 1, pair)
        return [item for item, _ in out]

    def _action_cell(
        self, action: str, text: str, row: Dict, read: List[Clause], issues: List[Issue]
    ) -> List[Clause]:
        """What a row with Action cell *action* reads as: its phrase's clauses, the settings the word gives
        added; the word's own behavior where no phrase reads the row; an issue where they disagree."""
        behavior, settings = self.actions.get(action, (None, {}))
        if behavior is None:
            issues.append(
                (
                    self.columns["action"],
                    "the %s '%s' is no %s word: write one of %s"
                    % (
                        self.columns["action"],
                        action,
                        self.columns["action"],
                        ", ".join(sorted(self.actions)),
                    ),
                )
            )
        elif read and read[0].behavior != behavior:
            issues.append(
                (
                    self.columns["action"],
                    "its phrase reads as '%s' and its %s says '%s', which disagree: "
                    "clear the %s, or reword the phrase"
                    % (
                        read[0].behavior,
                        self.columns["action"],
                        action,
                        self.columns["action"],
                    ),
                )
            )
        elif not read:
            return [
                Clause(
                    behavior,
                    text or action,
                    {"targets": list(row.get("assets") or [])},
                    self.book.plays(behavior),
                    "action",
                    dict(settings),
                )
            ]
        else:
            read[0].settings.update(
                settings
            )  # the Action word says how ("press and hold" a Use)
        return read

    # ---- binding ------------------------------------------------------------------------------------------------

    @staticmethod
    def _words(text: str) -> set:
        return {w.lower() for w in re.findall(r"[A-Za-z]+", text)}

    def contradiction(self, phrase: str, path: str) -> Optional[str]:
        """Why *phrase*'s modifier contradicts the node *path* (its name), or None.

        Parameters:
            phrase: The words a clause names a part with ("the bottom latch").
            path: The node path it matched.
        """
        found = self.matcher.contradiction(phrase, path.rsplit("|", 1)[-1])
        if found is None:
            return None
        return '"%s" says %s but matched %s' % (
            phrase,
            "/".join(sorted(found[0])),
            self.index.name_of(path),
        )

    def _refused(
        self, phrase: str, path: str, paths: List[str]
    ) -> Tuple[List[str], List[Issue]]:
        """*paths*, or the contradiction of *phrase* with *path* as the row's issue."""
        why = self.contradiction(phrase, path)
        return (
            ([], [(self.columns["assets"], why + ": list the right name")])
            if why
            else (paths, [])
        )

    def _inline(self, names: List[str]) -> Tuple[List[str], List[Issue]]:
        """The nodes the scene names written inline name, each one the delivery lacks an issue."""
        paths, issues = [], []
        for name in names:
            found = self.index.find(name)
            if found:
                paths.append(found[0])
            else:
                issues.append(
                    (
                        self.columns["contents"],
                        "no node of the delivery is named %s: correct the name, "
                        "or have the artist add it" % name,
                    )
                )
        return paths, issues

    def _exact(self, name: str, row: Dict) -> Tuple[List[str], List[Issue]]:
        """``(paths, issues)`` for *name*, a cell row's: the one node of that name, never a match by words
        (:meth:`_CellReading.exact`). Its issue is at the cell that names it: With, Target, else Settings (a
        setting naming an object)."""
        heads = self._heads(row)
        if name in (row.get("with") or ()):
            column = heads["with"]
        elif name in (row.get("assets") or ()):
            column = heads["target"]
        else:
            column = heads.get("settings", "Settings")
        return self.cells.exact(name, column)

    def _heads(self, row: Dict) -> Dict[str, str]:
        """``{cell key: header}`` for a cell row's issues: the sheet's own headers, as its reader kept them."""
        return {**self.cell_headers, **{k: v for k, v in (row.get("headers") or {}).items() if v}}

    @staticmethod
    def _listed(row: Dict) -> List[str]:
        """The names *row* lists as its own: a cell row that plays a take, what the take animates."""
        if row.get("cells") and row.get("take"):
            return ptk.ManifestActions.members(row)
        return list(row.get("assets") or [])

    def bind(
        self, phrase: str, behavior: str, row: Dict, context: Sequence[str] = ()
    ) -> Tuple[List[str], List[Issue]]:
        """``(paths, issues)``: what *phrase* names among *row*'s names (or written inline).

        Parameters:
            phrase: The words a clause names a part with.
            behavior: The clause's behavior (a plural one may bind several names).
            row: The row's cells; its ``assets`` are the names the phrase may bind (``step`` true for a
                hint line's: the step's names, of which a lone one binds nothing unnamed).
            context: What the step shows (its highlights' paths): a vague phrase ("the highlighted
                area") reads as the one of them the row lists.
        """
        if row.get("cells"):
            return self._exact(phrase, row)
        inline = self.inline.findall(phrase) if self.inline else []
        if inline:
            return self._inline(inline)
        listed = []
        for name in row.get("assets") or []:
            listed += self.index.find(name)
        assets = self.columns["assets"]
        if not listed:
            return [], [
                (
                    assets,
                    '"%s" names nothing this row lists in %s: list it'
                    % (phrase, assets),
                )
            ]
        if len(listed) == 1 and behavior == UNREAD:
            return listed, []
        plural = "plural" in self.book.roles(behavior)
        sub = self.index.subset(
            [
                p
                for p in self.index.paths
                if any(p == top or p.startswith(top + "|") for top in listed)
            ]
        )
        matcher = NodeMatcher(sub, self.aliases, self.reading.vocabulary)
        match = matcher.match(phrase, [p for p in context if p in sub.paths])
        paths = [p for p in match.paths if p in listed] or match.paths
        if match.path:
            return self._refused(phrase, match.path, [match.path])
        if len(paths) > 1 and plural:
            return paths, []
        # By head noun: "the wrench" is the one listed name carrying it (SOCKET_WRENCH); "the cabinet
        # latches", every one.
        head = matcher._singular(matcher.last_word(phrase).lower())
        hits = [
            p
            for p in listed
            if head
            in {
                matcher._singular(w)
                for w in self._words(self.index.name_of(p).replace("_", " "))
            }
        ]
        if hits and (plural or len(hits) == 1):
            return (
                self._refused(phrase, hits[0], hits) if len(hits) == 1 else (hits, [])
            )
        if len(listed) == 1 and not paths and not row.get("step"):
            return self._refused(phrase, listed[0], listed)
        names = ", ".join(self.index.name_of(p) for p in listed)
        if len(hits) > 1:  # one listed name the delivery gives several nodes
            return [], [
                (
                    assets,
                    '"%s" could be any of %s (the delivery repeats the name): list one by its path, %s'
                    % (phrase, ", ".join(hits), hits[0]),
                )
            ]
        if len(paths) > 1:
            return [], [
                (
                    assets,
                    '"%s" could be any of %s: list one, or write the name inline'
                    % (phrase, names),
                )
            ]
        return [], [
            (
                assets,
                '"%s" matches none of this row\'s names (%s): write the name inline, or list it'
                % (phrase, names),
            )
        ]

    def missing(self, row: Dict) -> List[Issue]:
        """Each name *row* lists that the delivery lacks: a cell row's Members (each other cell's names are found
        where they are read, :meth:`_exact`), any other row's names.

        Parameters:
            row: The row's cells.
        """
        if row.get("cells"):
            read = set(row.get("assets") or ()) | set(row.get("with") or ())
            column = self._heads(row)["members"]
            return [issue for name in dict.fromkeys(row.get("members") or ()) if name not in read
                    for issue in self.cells.exact(name, column)[1]]
        return [
            (
                self.columns["assets"],
                "the delivery has no %s: correct the name, or have the artist add it"
                % name,
            )
            for name in row.get("assets") or []
            if not self.index.find(name)
        ]

    def not_viewpoint(self, path: str) -> List[Issue]:
        """Why the node *path* is no place the user may be brought to (its object name misses the
        reading's viewpoint rule), or nothing (also where the reading states none).

        Parameters:
            path: A destination.
        """
        name = self.index.name_of(path)
        if self.viewpoint is None or self.viewpoint.search(name):
            return []
        return [
            (
                self.columns["assets"],
                "%s is no viewpoint (its name must match %s): go to a viewpoint, and say "
                "what the user faces with ', facing %s'"
                % (name, self.viewpoint.pattern, name),
            )
        ]

    @staticmethod
    def _moves(paths: Sequence[str], moved: Sequence[str]) -> bool:
        """True where a node of *moved* is one of *paths*, inside one or above one."""
        return any(
            m == p or m.startswith(p + "|") or p.startswith(m + "|")
            for p in paths
            for m in moved
        )

    def moves(self, take: str, row: Dict, step_rows: Sequence[Dict]) -> List[Issue]:
        """What is wrong with *take* as *row*'s by what it moves: another row's names and none of its
        own (an error), or none of its own (a warning). In a sheet of sentences a name two rows of
        the step list counts for neither; a row in cells says its own (its Members, else its Target
        and its With), and only another row that plays a take can be the one it belongs to. A take
        whose motion the delivery does not state is not checked.

        Parameters:
            take: The take *row* plays.
            row: The animation row's cells.
            step_rows: Every row of the step, *row* among them.
        """
        moved = self.index.moved.get(take)
        if moved is None:
            return []
        if row.get("cells"):
            # A cell names a node exactly; a name the delivery lacks is its cell's issue, never what a take moves.
            look = self.index.named
            own = [n for n in dict.fromkeys(self._listed(row)) if look(n)]
            if not own or any(self._moves(look(n), moved) for n in own):
                return []
            theirs = [
                (i, n)
                for i, r in enumerate(step_rows, 1)
                if r is not row and r.get("cells") and r.get("take")
                for n in dict.fromkeys(self._listed(r))
                if n not in own and self._moves(look(n), moved)
            ]
        else:
            look = self.index.find
            counts = Counter(
                n for r in step_rows for n in dict.fromkeys(self._listed(r))
            )
            own = [n for n in dict.fromkeys(self._listed(row)) if counts[n] == 1]
            if not own or any(self._moves(look(n), moved) for n in own):
                return []
            theirs = [
                (i, n)
                for i, r in enumerate(step_rows, 1)
                if r is not row
                for n in dict.fromkeys(self._listed(r))
                if counts[n] == 1 and self._moves(look(n), moved)
            ]
        mine = ", ".join(own)
        if theirs:
            listed = ", ".join("%s (row %s)" % (n, i) for i, n in theirs)
            return [
                (
                    self.columns["clip"],
                    "the take %s moves %s and none of this row's %s: swap the takes, or list "
                    "the names it moves" % (take, listed, mine),
                )
            ]
        return [
            (
                self.columns["clip"],
                "%sthe take %s moves none of this row's %s: re-export it, or list what it moves"
                % (WARNING, take, mine),
            )
        ]

    # ---- takes --------------------------------------------------------------------------------------------------

    def takes(
        self, step_id: str, animation_rows: Sequence[Tuple[int, Dict]]
    ) -> Dict[int, Tuple[Optional[str], List[Issue]]]:
        """``{row number: (take, issues)}`` for a step's animation rows: each row's take cell, or, where
        the step has one animation row, the step's own ID.

        Parameters:
            step_id: The step's ID (legal-named).
            animation_rows: ``[(row number, row)]`` of the step's rows that play a take.
        """
        out = {}
        for n, row in animation_rows:
            cell = (row.get("clip") or "").strip()
            if cell:
                out[n] = (ptk.StrUtils.to_legal_name(cell), [])
            elif len(animation_rows) == 1:
                out[n] = (step_id, [])
            elif row.get("cells"):
                out[n] = (None, [])  # a row in cells: the format's own check says its empty Clip
            else:
                out[n] = (
                    None,
                    [
                        (
                            self.columns["clip"],
                            "the step has %s animation rows: name each row's take in %s"
                            % (len(animation_rows), self.columns["clip"]),
                        )
                    ],
                )
        return out
