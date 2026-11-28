# !/usr/bin/python
# coding=utf-8
"""ClauseSpec -- one clause template: how a sheet's row of sentences reads as one of the sheet's words.

A clause is one row of a step ("Selecting the door triggers Door open Animation") read as one of the
Interactive template's words (``use``), its regex's named groups filling the row's cells: the plan a
sheet of sentences reads to is in the same words, cells and settings as one in cells. Templates are
data: built in under ``sheets/clauses/``, and a project's own in a folder beside its settings file
(:attr:`SheetSettings.clauses`), winning a built-in of its name, so a project words its sheet its own
way without code.

A named group fills the row's field of its name, or of its name before a ``_<n>`` suffix (``with``
and ``with_2``: Python names a group once). ``targets`` is split into phrases on "and" and commas,
each bound to a node; ``to`` is where the row goes; a group :attr:`ClauseSpec.nodes` lists is bound
to a node too (its path is the field's value; ``where``'s, a list of one per target); a group a
setting's value names (``{"state": "{state}"}``) is that setting; any other is the row's text, as
written. A template's ``{step}`` stands for a step ID's shape (the reading's :attr:`ReadingSpec.step_id`)
and ``{mark}`` for the mark of a question's right answer (the reading's ``question.mark``).

What a row does beyond its word is its template's roles (:data:`ROLES`, :data:`OWN_ROLES`), never a
word's name: a row the format needs none for (a page's buttons, a picture taken away) is kept as a
``note`` whose role folds what it says into the row or the step it belongs to.
"""

import re
from dataclasses import dataclass
from typing import Any, Dict, List, Optional

import pythontk as ptk

SchemaSpec = ptk.SchemaSpec
_field = SchemaSpec.spec_field

#: The sheet columns a template reads a row of (``any``: either).
COLUMNS = ("contents", "hint", "any")

#: The words a template may read a row as besides the Interactive template's own: ``advance``, the
#: step going on by itself (the format's own, which writes no row for it).
ADVANCE = "advance"

#: The roles a row of a word has, whichever template (or Action cell) read it: each what the reading
#: or the sheet's check does with it. A role declared with ``settings`` or ``fields`` belongs to the rows
#: that carry them.
ROLES = {
    "plural": "its phrase may name several of the row's parts",
    "shows": "its targets are what the step shows, against which a vague phrase reads",
    "points": "its targets join what the step shows after the `shows` rows, and the delivery's authored "
    "pointer for each (the reading's `point` names) is its `where`",
    "takes": "its target is in hand after it",
    "uses": "it is done with what the hand holds",
    "places": "what the hand holds stays where it acts: the hand is empty after it",
    "releases": "its target leaves the hand",
    "destination": "its `where` is a place the user is brought to (the reading's `viewpoint` names)",
    "advances": "a choice that advances the step",
    "ends_step": "a choice that ends its step (no answer of a question)",
    "continues": "the step goes on by itself: beside an `advances` row a conflict",
    "entered": "a value entered: its step needs an `otherwise` row",
    "otherwise": "the row taken for any other value entered",
    "lists": "a list on the page, its items its `label`, comma separated",
    "ticks": "an item of a list before it is ticked",
}

#: The roles that belong to the rows their template reads alone, never to a row of its word read
#: another way: how a sentence the format needs no row for folds into the plan.
OWN_ROLES = {
    "option": "read only in a question (a step one of whose rows starts with the reading's question "
    "mark), for a row no other template reads",
    "questions": "its `to` is the first later step that asks a question: none is the row's issue",
    "after_choice": "written after a choice that ends its step (an `ends_step` row), it happens as the "
    "step ends: its `when` is `end`",
    "captioned": "its `label`, where it has none, is what its words say it shows (the reading's "
    "`caption`, capitalized)",
    "page": "its `label` is the step's page text (the reading's `page` field) where the step has none; "
    "the row is kept as a note",
    "buttons": "its `label` names the buttons on the step's page (split at the reading's `separators`): "
    "each one a choice a row of the step offers (an `advances` row of that label), and the page not "
    "empty; the row is kept as a note",
    "opens": "it shows a thing its `span` says how long lasts, until a `closes` row ends it: the row's own "
    "where it is of the span's word; else the next row of that word's, given the span `step` (until its "
    "step ends), the row kept as a note",
    "closes": "it ends the span (its `span`) of the latest row of the span's word still showing: as its "
    "own step starts (`step` where that is the row's own step); the row is kept as a note",
}

#: The own roles that work on a span (:attr:`ClauseSpec.span`).
SPAN_ROLES = ("opens", "closes")

#: A setting's or a field's value that names a group (``{state}``): filled with what the group read.
_GROUP = re.compile(r"^\{(\w+)\}$")


class _ClauseSpecInternal:
    """Internal helpers for :class:`ClauseSpec` (field validators)."""

    _WORDS: Optional[Dict[str, Any]] = None

    @classmethod
    def vocabulary(cls) -> Dict[str, Any]:
        """The words a template reads a row as: the Interactive template's (pythontk's ``actions``, each
        setting declared whole), read once."""
        if cls._WORDS is None:
            from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

            cls._WORDS = dict(Mapping.load_mapping("interactive")["columns"].get("actions") or {})
        return cls._WORDS

    @staticmethod
    def validate_detect(value: Any) -> List[str]:
        """``detect`` = a non-empty list of regexes (matched whole, ignoring case)."""
        if isinstance(value, str) or not isinstance(value, list) or not value:
            return ["expected a non-empty list of regular expressions"]
        errs: List[str] = []
        for pattern in value:
            try:
                re.compile(pattern.replace("{step}", "x").replace("{mark}", r"\*"))
            except (re.error, TypeError, AttributeError) as exc:
                errs.append(f"{pattern!r} is not a valid regular expression: {exc}")
        return errs

    @staticmethod
    def validate_roles(value: Any) -> List[str]:
        """``roles`` = a list of :data:`ROLES` and :data:`OWN_ROLES`."""
        if not isinstance(value, list):
            return ["expected a list of roles"]
        known = list(ROLES) + list(OWN_ROLES)
        return [f"{r!r} is not a role: one of {', '.join(known)}" for r in value if r not in known]

    @classmethod
    def validate_word(cls, value: Any) -> List[str]:
        """A word is one of the Interactive template's, or ``advance``."""
        words = sorted(w for w in cls.vocabulary() if w) + [ADVANCE]
        if value in words:
            return []
        return [f"{value!r} is no word of the sheet: one of {', '.join(words)}"]

    @staticmethod
    def validate_nodes(value: Any) -> List[str]:
        """``nodes`` = group names, ``targets`` and ``to`` aside (they are read their own way)."""
        if not isinstance(value, list) or not all(isinstance(n, str) for n in value):
            return ["expected a list of group names"]
        return [f"{n!r} is read its own way: leave it out" for n in value if n in ("targets", "to")]

    @staticmethod
    def validate_span(value: Any) -> List[str]:
        """``span`` = ``{word, setting}``: a word and its setting of type span."""
        if not value:
            return []
        if not isinstance(value, dict) or set(value) != {"word", "setting"}:
            return ["expected {word, setting}"]
        declared = (_ClauseSpecInternal.vocabulary().get(value["word"]) or {}).get("settings") or {}
        if (declared.get(value["setting"]) or {}).get("type") != "span":
            return [f"{value['word']!r} has no span {value['setting']!r}"]
        return []


@dataclass
class ClauseSpec(SchemaSpec, _ClauseSpecInternal):
    """Schema for one clause template."""

    word: str = _field(
        help=(
            "The sheet's word a matching row reads as: one of the Interactive template's words (pythontk's "
            "`actions`: `use`, `button`, `note` ...) or `advance` (the step goes on by itself). It is the "
            "plan's behavior, as a row in cells writes it."
        ),
        required=True,
        validate=_ClauseSpecInternal.validate_word,
        example="use",
    )
    description: str = _field(
        help="One line: what a row that reads so does.",
        example="Selecting a target plays the row's clip.",
        default="",
    )
    detect: List[str] = _field(
        help=(
            "Regexes, tried in order, each matched against the WHOLE row (its "
            "surrounding space and a closing full stop stripped), ignoring case; "
            "the first that matches reads the row, its named groups filling the "
            "row's fields. `{step}` stands for a step ID (the reading's shape), `{mark}` for a "
            "question's mark of its right answer (the reading's)."
        ),
        required=True,
        validate=_ClauseSpecInternal.validate_detect,
        example=[r"^selecting\s+(?P<targets>.+?)\s+triggers\s+(?P<phrase>.+?)\s+animation$"],
    )
    column: str = _field(
        help="The sheet column whose rows the template reads: `contents`, `hint` or `any` (either).",
        choices=COLUMNS,
        example="contents",
        default="contents",
    )
    clip: bool = _field(
        help=(
            "The row plays one of its step's clips: the step's rows that do are "
            "numbered in order to name the takes (one: the step's ID; several: "
            "`<step>_<n>`)."
        ),
        example=True,
        default=False,
    )
    also: List[str] = _field(
        help="Further words the same row is, each its own row with the same fields.",
        example=["highlight"],
        default_factory=list,
    )
    nodes: List[str] = _field(
        help=(
            "The groups whose text names a node, as `targets` do: each is bound to the delivery's "
            "node and the row's field of its name holds that node's path (`with`, `side_of`; `where`, a "
            "list of one per target)."
        ),
        validate=_ClauseSpecInternal.validate_nodes,
        example=["with"],
        default_factory=list,
    )
    settings: Dict[str, str] = _field(
        help=(
            "Settings a matching row gives its word, by the sheet's names and values (`{\"trigger\": \"hold\"}`: "
            "\"Pressing and holding ...\" reads as a use row held), each one the word declares; a value "
            "`{<group>}` is what that group read (`{\"state\": \"{state}\"}`), which is then no field."
        ),
        example={"trigger": "hold"},
        default_factory=dict,
    )
    fields: Dict[str, str] = _field(
        help=(
            "Cells a matching row gives as the sheet writes them (`{\"label\": \"*\"}`: any other value; "
            "`{\"to\": \"next\"}`: the choice goes on), over what its groups read."
        ),
        example={"to": "next"},
        default_factory=dict,
    )
    span: Dict[str, str] = _field(
        help=(
            "The span an `opens` or `closes` row works on: `{word, setting}`, a word and its setting of "
            "type span (`{\"word\": \"picture\", \"setting\": \"until\"}`)."
        ),
        validate=_ClauseSpecInternal.validate_span,
        example={"word": "picture", "setting": "until"},
        default_factory=dict,
    )
    roles: List[str] = _field(
        help=(
            "What the reading and the sheet's check do with a row of it, by name: "
            + "; ".join(f"`{name}` ({about})" for name, about in ROLES.items())
            + ". A role declared with `settings` or `fields` belongs to the rows that carry them. Its own, "
            "for the rows it reads alone: "
            + "; ".join(f"`{name}` ({about})" for name, about in OWN_ROLES.items())
            + "."
        ),
        example=["takes"],
        default_factory=list,
        validate=_ClauseSpecInternal.validate_roles,
    )
    order: int = _field(
        help=(
            "Where the template is tried, lowest first: a narrower reading goes "
            "before a wider one that would also match (`use_near` before `play`)."
        ),
        example=100,
        default=100,
    )

    def gives(self) -> Dict[str, str]:
        """What a matching row carries whatever it says: its fields and the settings no group fills."""
        given = {k: v for k, v in (self.settings or {}).items() if not _GROUP.match(str(v))}
        given.update(self.fields or {})
        return given

    def problems(self) -> List[str]:
        """What is wrong between the template's parts and its word: a setting its word does not take, a
        value no value of it, a span role with no span, a placeholder naming no group."""
        out: List[str] = []
        declared = (self.vocabulary().get(self.word) or {}).get("settings") or {}
        for name, value in (self.settings or {}).items():
            if name not in declared:
                out.append(f"a {self.word} row takes no setting {name!r}: one of {', '.join(declared) or 'none'}")
                continue
            if _GROUP.match(str(value)):
                group = _GROUP.match(str(value)).group(1)
                if not any(f"(?P<{group}>" in p for p in self.detect):
                    out.append(f"setting {name!r} names the group {group!r}, which no regex has")
                continue
            from pythontk.core_utils.engines.shots.manifest.manifest_actions import ManifestActions

            error = ManifestActions._value_error(name, str(value), declared[name])
            if error:
                out.append(error)
        for role in SPAN_ROLES:
            if role in (self.roles or []) and not self.span:
                out.append(f"the role {role!r} works on a span: give the template its span")
        return out
