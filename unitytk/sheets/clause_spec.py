# !/usr/bin/python
# coding=utf-8
"""ClauseSpec -- one clause template: how a sheet's row reads as an action.

A clause is one row of a step ("Selecting the door triggers Door open Animation") read as one of
the plan's behaviors (``use``), its regex's named groups filling the action's fields. Templates are
data: built in under ``sheets/clauses/``, and a project's own in a folder beside its settings file
(:attr:`SheetSettings.clauses`), winning a built-in of its name, so a project words its sheet its own
way without code.

A named group fills the action field of its name, or of its name before a ``_<n>`` suffix (``tool``
and ``tool_2``: Python names a group once). ``targets`` is split into phrases on "and" and commas,
each bound to a node; ``to`` is the step the row goes to; a group :attr:`ClauseSpec.nodes` lists is
bound to a node too (its path is the field's value); any other is the row's text, as written. A
template's ``{step}`` stands for a step ID's shape (the reading's :attr:`ReadingSpec.step_id`).
"""

import re
from dataclasses import dataclass
from typing import Any, Dict, List

import pythontk as ptk

SchemaSpec = ptk.SchemaSpec
_field = SchemaSpec.spec_field

#: The sheet columns a template reads a row of (``any``: either).
COLUMNS = ("contents", "hint", "any")

#: The roles a template may give its behavior (:attr:`ClauseSpec.roles`), each what the reading or the
#: sheet's check does with a row of it.
ROLES = {
    "plural": "its phrase may name several of the row's parts",
    "shows": "its targets are what the step shows, against which a vague phrase reads",
    "points": "its targets join what the step shows after the `shows` rows, and its authored arrows are found",
    "takes": "its target is in hand after it",
    "uses": "it is done with what the hand holds",
    "places": "what the hand holds stays where it acts: the hand is empty after it",
    "releases": "its target leaves the hand",
    "destination": "its targets are places the user is brought to (the reading's `viewpoint` names)",
    "advances": "a choice that advances the step",
    "continues": "the step goes on by itself: beside an `advances` row a conflict",
    "on_page": "it is shown on the step's page (the reading's `page` field): an empty page a warning",
    "page": "its `label` is the step's page text: a converted sheet moves it there",
    "entry": "an entry pad in its step",
    "entered": "a value entered: its step needs an `entry` row and an `otherwise` row",
    "otherwise": "the row taken for any other value entered",
    "questions": "a choice going to the questions: a later step must ask one",
    "lists": "a list on the page, its items its `label`, comma separated",
    "ticks": "an item of a list before it is ticked",
}


class _ClauseSpecInternal:
    """Internal helpers for :class:`ClauseSpec` (field validators)."""

    @staticmethod
    def validate_detect(value: Any) -> List[str]:
        """``detect`` = a non-empty list of regexes (matched whole, ignoring case)."""
        if isinstance(value, str) or not isinstance(value, list) or not value:
            return ["expected a non-empty list of regular expressions"]
        errs: List[str] = []
        for pattern in value:
            try:
                re.compile(pattern)
            except (re.error, TypeError) as exc:
                errs.append(f"{pattern!r} is not a valid regular expression: {exc}")
        return errs

    @staticmethod
    def validate_roles(value: Any) -> List[str]:
        """``roles`` = a list of :data:`ROLES`."""
        if not isinstance(value, list):
            return ["expected a list of roles"]
        return [
            f"{r!r} is not a role: one of {', '.join(ROLES)}"
            for r in value
            if r not in ROLES
        ]

    @staticmethod
    def validate_behavior(value: Any) -> List[str]:
        """A behavior is a snake_case name (what the kit's applier is registered by)."""
        if isinstance(value, str) and re.fullmatch(r"[a-z][a-z0-9_]*", value):
            return []
        return [f"{value!r} is not a snake_case behavior name"]

    @staticmethod
    def validate_nodes(value: Any) -> List[str]:
        """``nodes`` = group names, ``targets`` and ``to`` aside (they are read their own way)."""
        if not isinstance(value, list) or not all(isinstance(n, str) for n in value):
            return ["expected a list of group names"]
        return [
            f"{n!r} is read its own way: leave it out"
            for n in value
            if n in ("targets", "to")
        ]


@dataclass
class ClauseSpec(SchemaSpec, _ClauseSpecInternal):
    """Schema for one clause template."""

    behavior: str = _field(
        help=(
            "The plan behavior a matching row reads as (snake_case): the name "
            "the Unity kit's applier for it is registered by."
        ),
        required=True,
        validate=_ClauseSpecInternal.validate_behavior,
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
            "action's fields. `{step}` stands for a step ID (the reading's shape)."
        ),
        required=True,
        validate=_ClauseSpecInternal.validate_detect,
        example=[
            r"^selecting\s+(?P<targets>.+?)\s+triggers\s+(?P<phrase>.+?)\s+animation$"
        ],
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
        help="Further behaviors the same row is, each its own action with the same fields.",
        example=["highlight"],
        default_factory=list,
    )
    nodes: List[str] = _field(
        help=(
            "The groups whose text names a node, as `targets` do: each is bound to the delivery's "
            "node and the action's field of its name holds that node's path (`tool`, `facing`)."
        ),
        validate=_ClauseSpecInternal.validate_nodes,
        example=["tool"],
        default_factory=list,
    )
    settings: Dict[str, str] = _field(
        help=(
            'Settings a matching row gives its behavior, by its kit\'s names (`{"useTrigger": "HoldToPlay"}`: '
            "\"Pressing and holding ...\" reads as a Use row held to play): carried on the plan's row over the kit's "
            "settings, so one behavior reads several ways, its chip saying which, and a person may change them per object."
        ),
        example={"useTrigger": "HoldToPlay"},
        default_factory=dict,
    )
    roles: List[str] = _field(
        help=(
            "What the reading and the sheet's check do with a row of it, by name: "
            + "; ".join(f"`{name}` ({about})" for name, about in ROLES.items())
            + ". A role declared with `settings` belongs to the rows that carry them."
        ),
        example=["takes"],
        default_factory=list,
        validate=_ClauseSpecInternal.validate_roles,
    )
    order: int = _field(
        help=(
            "Where the template is tried, lowest first: a narrower reading goes "
            "before a wider one that would also match (`place` before `play`)."
        ),
        example=100,
        default=100,
    )
