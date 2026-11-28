# !/usr/bin/python
# coding=utf-8
"""ReadingSpec -- how a sheet of one template is read: the reader's own settings beside the template.

pythontk's mapping template (``Mapping.load_mapping``) says which columns a sheet has and how its
steps and sections are told apart; a reading says how this reader takes their words: strictly or
not, how the sheet and the scene write names, a step ID's shape inside a row, which of a step's
values the plan carries and under which names, the words of the Action cell, how a question's
options are marked, the phrase matcher's vocabulary, and the column order a sheet in the format
is written in. Readings are data, built in under ``sheets/readings/``: ``default.json`` holds every
key, and ``<template>.json`` overlays it for the template of that name; a caller's own overlay
(a dict) goes over both (:meth:`load`).

A typed :class:`~pythontk.SchemaSpec`, so this one declaration validates a reading.
"""

import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Union

import pythontk as ptk

SchemaSpec = ptk.SchemaSpec
_field = SchemaSpec.spec_field

#: The name rules a reading (or a project's settings file) may state.
NAME_RULES = ("inline", "suffixes", "viewpoint", "point")
#: The vocabulary a reading may give the phrase matcher (:class:`~unitytk.sheets.NodeMatcher`).
VOCABULARY = ("synonyms", "stop", "distinct", "qualifiers", "referents", "opposites")


class _ReadingSpecInternal:
    """Internal helpers for :class:`ReadingSpec` (field validators, the merge)."""

    @staticmethod
    def _regex_errors(key: str, value: Any) -> List[str]:
        """Why *value* is no regex, each message led by *key* where one is given."""
        lead = f"{key}: " if key else ""
        if not isinstance(value, str):
            return [f"{lead}expected a regular expression (a string)"]
        try:
            re.compile(value)
        except re.error as exc:
            return [f"{lead}{value!r} is not a valid regular expression: {exc}"]
        return []

    @classmethod
    def validate_names(cls, value: Any) -> List[str]:
        """``names`` = ``{inline, viewpoint, point}`` regexes and ``suffixes``, a list of node-name endings."""
        if not isinstance(value, dict):
            return ["expected an object of name rules"]
        errors = [
            f"{k!r} is no name rule: one of {', '.join(NAME_RULES)}"
            for k in value
            if k not in NAME_RULES
        ]
        for key, rule in value.items():
            if key == "suffixes":
                if not isinstance(rule, list) or not all(
                    isinstance(s, str) and s.strip("_") for s in rule
                ):
                    errors.append(
                        "suffixes: expected a list of node-name endings (GRP, GEO ...)"
                    )
            elif key in NAME_RULES:
                errors += cls._regex_errors(key, rule)
        return errors

    @classmethod
    def validate_step_id(cls, value: Any) -> List[str]:
        """``step_id`` = a regex with no groups of its own (it is placed inside a template's)."""
        errors = cls._regex_errors("", value)
        if not errors and re.compile(value).groups:
            errors.append(
                "use non-capturing groups (?:...) only: it is placed inside a template's"
            )
        return errors

    @classmethod
    def validate_caption(cls, value: Any) -> List[str]:
        """``caption`` = empty, or a regex with a ``what`` group."""
        if not value:
            return []
        errors = cls._regex_errors("", value)
        if not errors and "what" not in re.compile(value).groupindex:
            errors.append("name what it shows in a group: (?P<what>...)")
        return errors

    @staticmethod
    def validate_applies(value: Any) -> List[str]:
        """``applies`` = ``{word: {setting: [condition, ...]}}``, each condition an object."""
        if not isinstance(value, dict) or not all(
            isinstance(by, dict)
            and all(isinstance(c, list) and all(isinstance(each, dict) for each in c) for c in by.values())
            for by in value.values()
        ):
            return ["expected {word: {setting: [condition, ...]}}, each condition an object"]
        return []

    @staticmethod
    def validate_vocabulary(value: Any) -> List[str]:
        """``vocabulary`` = ``{synonyms: {word: word}, stop/distinct/qualifiers/referents: [words], opposites: [[words], [words]]}``."""
        if not isinstance(value, dict):
            return ["expected an object"]
        errors = [
            f"{k!r} is no vocabulary key: one of {', '.join(VOCABULARY)}"
            for k in value
            if k not in VOCABULARY
        ]
        if "synonyms" in value and not (
            isinstance(value["synonyms"], dict)
            and all(isinstance(v, str) for v in value["synonyms"].values())
        ):
            errors.append("synonyms: expected {word: word}")
        for key in ("stop", "distinct", "qualifiers", "referents"):
            if key in value and not (
                isinstance(value[key], list)
                and all(isinstance(w, str) for w in value[key])
            ):
                errors.append(f"{key}: expected a list of words")
        if "opposites" in value:
            pairs = value["opposites"]
            if not isinstance(pairs, list) or not all(
                isinstance(p, list)
                and len(p) == 2
                and all(isinstance(side, list) for side in p)
                for p in pairs
            ):
                errors.append(
                    "opposites: expected a list of pairs, each two lists of words"
                )
        return errors

    @staticmethod
    def merge(
        base: Mapping[str, Any], overlay: Optional[Mapping[str, Any]]
    ) -> Dict[str, Any]:
        """*base* with *overlay* over it: an object value merged key by key (one level), any other replaced."""
        out = dict(base)
        for key, value in (overlay or {}).items():
            if isinstance(value, dict) and isinstance(out.get(key), dict):
                out[key] = {**out[key], **value}
            else:
                out[key] = value
        return out


@dataclass
class ReadingSpec(SchemaSpec, _ReadingSpecInternal):
    """How a sheet is read: the reader's settings for one template."""

    TYPED = True

    #: Where the built-in readings are.
    BUILTIN_DIR = Path(__file__).resolve().parent / "readings"

    strict: bool = _field(
        help=(
            "Read the sheet row by row: each row's words bind only to the names that row lists (or "
            "writes inline), and a row nothing reads is an error, never a guess. Off: names are "
            "inferred from the prose."
        ),
        example=True,
        default=False,
    )
    step_id: str = _field(
        help=(
            "A step ID's shape as a row writes it (a regex with no groups of its own, matched ignoring "
            'case): a clause template\'s `{step}` stands for it ("triggers step {step}").'
        ),
        validate=_ReadingSpecInternal.validate_step_id,
        example=r"[A-Z]+_?\d+(?:[._]\d+)?",
        default=r"[A-Z]+_?\d+(?:[._]\d+)?",
    )
    restart: bool = _field(
        help=(
            "A sheet of sentences' jump to its first step starts the sequence over, its scene loaded again so "
            "nothing a take did stands: the row goes to `restart`, and the check lets it go back past takes. "
            "Off: a jump like any other. A row in cells says `restart` itself."
        ),
        example=True,
        default=True,
    )
    names: Dict[str, Any] = _field(
        help=(
            "How the sheet and the scene write names, each rule applying only where stated: `inline`, "
            "a regex a word of a row's text matches to be a scene name written inline; `suffixes`, "
            "the node-name endings that say how a node is built, ignored when matching (the delivery "
            "index's own list where absent); `viewpoint`, a regex a destination's object name must "
            "match; `point`, a regex an authored pointer's node name matches (a sentence sheet's: a row in "
            "cells names its pointer in `where`), its `part` group the part it points at and its `step` group, "
            "where it has one, the step it is for."
        ),
        validate=_ReadingSpecInternal.validate_names,
        example={
            "inline": r"\b[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+\b",
            "suffixes": ["GRP", "GEO", "MSH"],
            "viewpoint": "^VIEW_",
            "point": "^(?P<part>.+)_POINT$",
        },
        default_factory=dict,
    )
    step_fields: Dict[str, str] = _field(
        help=(
            "The values each plan step carries (its `fields`), `{plan name: source}`: a source is one of "
            "the step's columns by the template's field name (`audio`, the narration column; a "
            "pass-through column's: `hint`, `placard_text` ...) or `sound`, the step's sound file as the "
            "template's audio methods find it (relative to the settings folder). An empty value is left out."
        ),
        example={"voice": "audio", "sound": "sound"},
        default_factory=dict,
    )
    staged: List[str] = _field(
        help=(
            "The step fields that name a file beside the settings file, which staging copies beside the "
            "plan at the same path (`sound`)."
        ),
        example=["sound"],
        default_factory=list,
    )
    page: str = _field(
        help=(
            "The step field a row of the `on_page` role is shown on (an empty one is a warning), and the "
            "one a converted sheet's `page` hint lines move to."
        ),
        example="placard",
        default="",
    )
    hand: str = _field(
        help=(
            "The action field that names what the hand holds (the With cell's): a `uses` row of a sheet of "
            "sentences names it there where its words do not, as the hand is followed down the sheet."
        ),
        example="with",
        default="with",
    )
    action_words: Dict[str, str] = _field(
        help=(
            "Words a sheet of sentences' Action cell may hold beside each word the clause book reads, "
            "`{word: template}`: a row with the word reads as that clause template's word, with its settings "
            "(a word for a template the book lacks is no word)."
        ),
        example={"press and hold": "use_hold"},
        default_factory=dict,
    )
    cell_columns: List[str] = _field(
        help=(
            "The headers a sheet in this format's cells is written under, in order (`sheet.convert` "
            "writes them): a step's own cells, then an action's."
        ),
        example=["Step", "Step Name", "Voice", "Action", "Target", "Notes"],
        default_factory=list,
    )
    question: Dict[str, str] = _field(
        help=(
            "How a sheet of sentences writes a question: a step with a row starting `mark` is one, its rows no "
            "other template reads its options (the clause templates of the `option` role, whose `{mark}` "
            "stands for it): the marked one the right answer."
        ),
        example={"mark": "*"},
        default_factory=dict,
    )
    order_by: Dict[str, str] = _field(
        help=(
            "The words whose row stands at its step's start or end, `{word: setting}`: the setting (its word's "
            "default where the row gives none) saying `start` or `end` (`travel`: `when`). Any other row "
            "stands where it is written, a row that plays after a take (its `after`) after that take's row."
        ),
        example={"travel": "when"},
        default_factory=dict,
    )
    applies: Dict[str, Dict[str, List[Dict[str, Any]]]] = _field(
        help=(
            "The settings that do something only beside other cells or settings, `{word: {setting: [condition, "
            "...]}}`: a row giving one meets one of its conditions (`{cell or setting: value}`, `any` / `none`: "
            "another row of the step does / none does), else it is a warning, never dropped in silence (one "
            "written at its word's default changes nothing)."
        ),
        validate=_ReadingSpecInternal.validate_applies,
        example={"use": {"hold": [{"trigger": "hold"}]}},
        default_factory=dict,
    )
    caption: str = _field(
        help=(
            "A regex whose `what` group is what a sheet of sentences' row says its picture shows (\"PiP screen "
            "shows the bolt\"): a `captioned` row's `label`, capitalized, where it has none (the row's words "
            "where it does not match)."
        ),
        validate=_ReadingSpecInternal.validate_caption,
        example=r"(?:showing|shows|with)\s+(?P<what>.+?)(?:\s+appears)?\.?$",
        default="",
    )
    separators: List[str] = _field(
        help=(
            "What splits a `buttons` row's `label` into its buttons (\"YES and NO\"): punctuation as written, a "
            "word where it stands alone."
        ),
        example=[",", "&", "and", "or"],
        default_factory=list,
    )
    vocabulary: Dict[str, Any] = _field(
        help=(
            "The phrase matcher's words over its own (each key replaces its list): `synonyms` "
            "`{word: word}` read as one, `stop` words naming no part, `distinct` words that make a "
            "phrase another object, `qualifiers` words after which a phrase says where, not what, "
            "`referents` words by which a phrase means the one object its step shows (\"the highlighted "
            "area\"), and `opposites` pairs of word lists a phrase's modifier must not contradict."
        ),
        validate=_ReadingSpecInternal.validate_vocabulary,
        example={"synonyms": {"top": "upper"}, "stop": ["the", "a"]},
        default_factory=dict,
    )
    columns: List[str] = _field(
        help=(
            "The order a sheet in this format is written in, by the template's field names "
            "(`step_id`, `description`, `assets`, a pass-through or row column's): what a sheet "
            "converted to it holds."
        ),
        example=["step_id", "step_name", "description", "assets", "clip"],
        default_factory=list,
    )

    @classmethod
    def load(
        cls,
        name: Optional[str] = None,
        overlay: Union["ReadingSpec", Mapping[str, Any], None] = None,
    ) -> "ReadingSpec":
        """The reading of template *name*: the built-in ``default.json``, *name*'s own over it where one
        is built in, and *overlay* over both, validated.

        Parameters:
            name: A template's name (``interactive``, ``linear`` ...); None or one with no reading of
                its own: the default.
            overlay: A reading, or a dict of keys to set over it (an object value merged key by key).

        Raises:
            ValueError: The reading fails its schema (the message names each field).
        """
        if isinstance(overlay, ReadingSpec):
            return overlay
        data = cls._builtin("default")
        if name and name != "default":
            data = cls.merge(data, cls._builtin(name))
        data = cls.merge(data, overlay)
        result = cls.validate(data)
        if not result.ok:
            raise ValueError(
                f"the reading of {name or 'default'} fails its schema: {result}"
            )
        return cls.from_dict(data)

    @classmethod
    def _builtin(cls, name: str) -> Dict[str, Any]:
        path = cls.BUILTIN_DIR / f"{name}.json"
        return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else {}

    def with_names(self, names: Optional[Mapping[str, Any]]) -> "ReadingSpec":
        """This reading with *names*' rules over its own (a project's: :attr:`SheetSettings.names`).

        Raises:
            ValueError: A rule fails (the message names it).
        """
        if not names:
            return self
        data = self.merge(self.to_dict(), {"names": dict(names)})
        result = type(self).validate(data)
        if not result.ok:
            raise ValueError(f"the name rules fail: {result}")
        return type(self).from_dict(data)

    def source_of(self, field: str) -> Optional[str]:
        """The source of step field *field* (:attr:`step_fields`), or None."""
        return self.step_fields.get(field)

    def field_of(self, source: str) -> Optional[str]:
        """The step field carrying *source* (:attr:`step_fields`), or None."""
        return next(
            (name for name, src in self.step_fields.items() if src == source), None
        )

    @staticmethod
    def header(template: Mapping[str, Any], field: str) -> str:
        """The header the template knows column *field* by first (``description``: ``Step Contents``);
        the field's own name where it knows none.

        Parameters:
            template: A loaded mapping template.
            field: A field name: a column's, a pass-through column's or a row column's.
        """
        columns = template.get("columns") or {}
        for group in (
            columns,
            columns.get("metadata_pass") or {},
            columns.get("row_pass") or {},
        ):
            aliases = group.get(field)
            if isinstance(aliases, list) and aliases and isinstance(aliases[0], str):
                return aliases[0]
        return field

    @staticmethod
    def column_of(template: Mapping[str, Any], field: str, headers: List[str]) -> Optional[int]:
        """Where in *headers* (a sheet's header row) the template's reader takes column *field* from: the first of
        the field's names the row holds, ignoring case, as pythontk's reader finds a column; None where it holds
        none.

        Parameters:
            template: A loaded mapping template.
            field: A field name: a column's, a pass-through column's or a row column's.
            headers: A header row.
        """
        columns = template.get("columns") or {}
        lowered = [str(h).strip().lower() for h in headers]
        for group in (columns, columns.get("metadata_pass") or {}, columns.get("row_pass") or {}):
            aliases = group.get(field)
            if isinstance(aliases, list) and aliases and isinstance(aliases[0], str):
                return next((lowered.index(a.strip().lower()) for a in aliases if a.strip().lower() in lowered), None)
        return None

    #: A step's header keys (pythontk's ``BuilderStep.headers``) that name another field of the template.
    HEADER_FIELDS = {"target": "assets", "step": "step_id"}

    @classmethod
    def as_written(cls, template: Mapping[str, Any], headers: Mapping[str, str]) -> Dict[str, Any]:
        """*template* with each column known first by the header the sheet itself writes
        (*headers*: ``{column key: the sheet's own header}``, as the sheet's reader found
        them), so :meth:`header` names a cell as the sheet does. It reads the sheet as
        before: only the order of a column's names changes, and the one the sheet uses
        was the one found.

        Parameters:
            template: A loaded mapping template.
            headers: The sheet's own header for each column it carries.
        """
        out = dict(template)
        columns = out["columns"] = dict(template.get("columns") or {})
        for group in ("metadata_pass", "row_pass"):
            if isinstance(columns.get(group), dict):
                columns[group] = dict(columns[group])
        for key, own in headers.items():
            field = cls.HEADER_FIELDS.get(key, key)
            for group in (columns, columns.get("metadata_pass"), columns.get("row_pass")):
                aliases = group.get(field) if isinstance(group, dict) else None
                if own and isinstance(aliases, list) and any(str(a).lower() == own.lower() for a in aliases):
                    group[field] = [own] + [a for a in aliases if str(a).lower() != own.lower()]
                    break
        return out

    @staticmethod
    def blanks(template: Mapping[str, Any]) -> set:
        """The cell values the template reads as empty (its ``exclude_values``, ``N/A`` and the like),
        upper-cased."""
        excluded = (template.get("columns") or {}).get("exclude_values") or {}
        return {
            v.strip().upper()
            for values in excluded.values()
            for v in values or []
            if v and v.strip()
        }
