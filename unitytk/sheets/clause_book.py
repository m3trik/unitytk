# !/usr/bin/python
# coding=utf-8
"""ClauseBook -- the clause templates, compiled: one row in, what it does out.

See :mod:`unitytk.sheets.clause_spec` for a template's shape. The book tries its templates in
``order`` (then name) and the first whose regex matches the whole row reads it; a row none
matches is one the plan reports as ``unread``. A template with the ``option`` role reads only a
question's rows (:meth:`ClauseBook.read`'s ``options``). The templates are the built-in ones and a
project's own (a folder beside its settings file), never a per-user folder, so a sheet reads the same
on every machine; :meth:`ClauseBook.digest` says which of a project's own a plan was read with.
"""

import hashlib
import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Mapping, Optional, Pattern, Set, Tuple, Union

from unitytk.sheets.clause_spec import OWN_ROLES, ClauseSpec

_SUFFIX = re.compile(r"_\d+$")
_SPLIT = re.compile(r"\s*(?:,|\band\b|&)\s*", re.I)
#: The quotes between a quoted list's items ('YES' and 'NO', 'Lights', 'Power').
_QUOTED_JOIN = re.compile(r"['\"]\s*(,|\band\b|&)\s*['\"]", re.I)
#: A value naming a group (``{state}``).
_GROUP = re.compile(r"^\{(\w+)\}$")
#: What a template writes for a step ID's shape, and for a question's mark of its right answer.
STEP, MARK = "{step}", "{mark}"
#: The role of the templates that read a question's rows alone.
OPTION = "option"


@dataclass
class Clause:
    """One row read: the word and the fields its template's groups filled.

    Attributes:
        behavior: The word it reads as (the plan's behavior).
        text: The row as the sheet writes it.
        fields: ``{group: value}``: ``targets`` a list of phrases, any other the group's text.
        clip: Whether the row plays one of its step's takes.
        template: The template that read it (empty: none did; ``action``: the Action cell; ``cells``:
            a row in cells).
        settings: The settings it gives its word, by the sheet's names.
        nodes: The groups of :attr:`fields`, ``targets`` aside, whose text names a node.
    """

    behavior: str
    text: str
    fields: Dict[str, object] = field(default_factory=dict)
    clip: bool = False
    template: str = ""
    settings: Dict[str, str] = field(default_factory=dict)
    nodes: List[str] = field(default_factory=list)


class _ClauseBookInternal:
    """Internal helpers for :class:`ClauseBook`."""

    BUILTIN_DIR = Path(__file__).resolve().parent / "clauses"

    @staticmethod
    def _clean(line: str) -> str:
        """*line* as a template reads it: one line, outer space and a closing
        full stop gone, typographic quotes made plain."""
        text = " ".join(line.replace("’", "'").replace("‘", "'").split())
        text = text.replace("“", '"').replace("”", '"')
        return text.rstrip(". ").strip()

    @staticmethod
    def _phrases(text: str) -> List[str]:
        """A ``targets`` group's text as its phrases ("the top and bottom
        latches" stays one phrase only where no "and" splits it)."""
        return [part.strip() for part in _SPLIT.split(text or "") if part.strip()]

    @classmethod
    def _fields(cls, match: "re.Match") -> Dict[str, object]:
        out: Dict[str, object] = {}
        for name, value in match.groupdict().items():
            if not value:
                continue
            key = _SUFFIX.sub("", name)
            if key in out:
                continue
            if key == "targets":
                out[key] = cls._phrases(value)
            else:
                value = _QUOTED_JOIN.sub(
                    lambda m: ", " if m.group(1) == "," else " %s " % m.group(1), value
                )
                out[key] = value.strip().strip("'\"").strip()
        return out

    @staticmethod
    def _given(spec: ClauseSpec, fields: Dict[str, object]) -> Tuple[Dict[str, object], Dict[str, str]]:
        """``(fields, settings)`` of a row *spec* read: its groups' fields with the template's own over them, and
        its settings, a value naming a group filled with what it read (that group then no field)."""
        out = dict(fields)
        settings: Dict[str, str] = {}
        for name, value in (spec.settings or {}).items():
            group = _GROUP.match(str(value))
            if group:
                read = out.pop(group.group(1), "")
                if read:
                    settings[name] = str(read)
            else:
                settings[name] = str(value)
        out.update(spec.fields or {})
        return out, settings


class ClauseBook(_ClauseBookInternal):
    """The clause templates a plan reads rows with, compiled once.

    Parameters:
        templates: ``(name, spec)`` pairs; default: :meth:`load_templates` (the built-in ones,
            and *folder*'s over them).
        folder: A project's own templates (a folder of ``ClauseSpec`` JSON files), when
            *templates* is None.
        step: A step ID's shape, which a template's ``{step}`` stands for; default: the default
            reading's (:attr:`ReadingSpec.step_id`).
        mark: The mark of a question's right answer, which a template's ``{mark}`` stands for;
            default: the default reading's (``question.mark``).

    Raises:
        ValueError: A template gives a setting its word does not take, or no value of it.

    Attributes:
        folder: The project's own templates it read (*folder*, where *templates* is None), else None.
    """

    def __init__(
        self,
        templates: Optional[Iterable[Tuple[str, ClauseSpec]]] = None,
        *,
        folder: Union[str, Path, None] = None,
        step: Optional[str] = None,
        mark: Optional[str] = None,
    ):
        self.folder = Path(folder) if templates is None and folder else None
        if templates is None:
            templates = self.load_templates(folder)
        if step is None or mark is None:
            from unitytk.sheets.reading_spec import ReadingSpec

            default = ReadingSpec.load()
            step = default.step_id if step is None else step
            mark = (default.question or {}).get("mark", "") if mark is None else mark
        self.step, self.mark = step, mark
        compiled = []
        for name, spec in templates:
            problems = spec.problems()
            if problems:
                raise ValueError(f"template {name!r}: " + "; ".join(problems))
            patterns = [
                re.compile(p.replace(STEP, step).replace(MARK, re.escape(mark or "\x00")), re.I)
                for p in spec.detect
            ]
            compiled.append((spec.order, name, spec, patterns))
        compiled.sort(key=lambda item: (item[0], item[1]))
        self._compiled: List[Tuple[int, str, ClauseSpec, List[Pattern]]] = compiled
        self._specs: Dict[str, ClauseSpec] = {name: spec for _, name, spec, _ in compiled}

    @classmethod
    def for_reading(cls, reading, folder: Union[str, Path, None] = None) -> "ClauseBook":
        """The built-in templates (and *folder*'s), with *reading*'s step shape and question mark."""
        return cls(folder=folder, step=reading.step_id, mark=(reading.question or {}).get("mark", ""))

    @classmethod
    def load_templates(
        cls, folder: Union[str, Path, None] = None
    ) -> List[Tuple[str, ClauseSpec]]:
        """Every template, validated, in name order: ``[(name, spec)]``: the built-in ones (:attr:`BUILTIN_DIR`), and
        *folder*'s over them (a template there wins a built-in of its name). There is no other tier: a sheet
        reads the same on every machine, and nothing is written.

        Parameters:
            folder: A project's own templates (its settings file's :attr:`SheetSettings.clauses`); None or a folder
                that does not exist: none.

        Raises:
            ValueError: A template fails its schema (the message names it).
        """
        files = {p.stem: p for p in cls.BUILTIN_DIR.glob("*.json")}
        if folder and Path(folder).is_dir():
            files.update({p.stem: p for p in Path(folder).glob("*.json")})
        out = []
        for name in sorted(files):
            data = json.loads(files[name].read_text(encoding="utf-8"))
            ClauseSpec.validate(data).raise_or_warn(
                prefix=f"template {name!r}: ", strict=True
            )
            out.append((name, ClauseSpec.from_dict(data)))
        return out

    @property
    def names(self) -> List[str]:
        """The templates' names in the order they are tried."""
        return [name for _, name, _, _ in self._compiled]

    def spec(self, name: str) -> Optional[ClauseSpec]:
        """The template named *name*, or None."""
        return self._specs.get(name)

    def digest(self) -> str:
        """The digest of the project's own template files it read (:attr:`folder`, :meth:`folder_digest`). Empty where
        it read none: the built-in ones are the reader's own, read again with it."""
        return self.folder_digest(self.folder)

    @staticmethod
    def folder_digest(folder: Union[str, Path, None]) -> str:
        """The SHA-256 (hex) of *folder*'s template files, as a reader with the folder in hand makes it again: a line
        per ``*.json`` file in name order, its name and its own SHA-256 (its bytes, CRLF read as LF) apart by a space,
        joined by newlines, UTF-8; their bytes alone, never what they say. Empty for none."""
        from unitytk.sheets.sheet_plan import SheetPlan

        files = sorted(Path(folder).glob("*.json"), key=lambda p: p.name) if folder and Path(folder).is_dir() else []
        lines = "\n".join(f"{p.name} {SheetPlan.digest(str(p))}" for p in files)
        return hashlib.sha256(lines.encode("utf-8")).hexdigest() if lines else ""

    def roles(
        self,
        behavior: str,
        settings: Optional[Mapping[str, str]] = None,
        fields: Optional[Mapping[str, object]] = None,
        to: Optional[str] = None,
    ) -> Set[str]:
        """The roles a row of the word *behavior* carrying *settings*, *fields* and *to* has, however it was read:
        those declared by the word's templates whose own settings and fields (:meth:`ClauseSpec.gives`) the row
        carries, a template's own roles (:data:`OWN_ROLES`) aside. A template that gives something says what its
        rows are in full (a use brought near places what the hand holds), so where the row carries one's, the word's
        templates that give nothing do not speak for it; a row carrying no such thing has theirs.

        Parameters:
            behavior: The row's word.
            settings: The row's settings (:attr:`Clause.settings`, ``PlanAction.settings``).
            fields: The row's fields (``label``...).
            to: Where the row goes (``PlanAction.to``).
        """
        carried = {**dict(fields or {}), **dict(settings or {})}
        if to:
            carried["to"] = to
        general: Set[str] = set()
        own: Set[str] = set()
        set_apart = False
        for _, _, spec, _ in self._compiled:
            if spec.word != behavior:
                continue
            word_roles = set(spec.roles or []) - set(OWN_ROLES)
            gives = spec.gives()
            if not gives:
                general |= word_roles
            elif all(str(carried.get(k, "")).casefold() == str(v).casefold() for k, v in gives.items()):
                set_apart = True
                own |= word_roles
        return own if set_apart else general

    def roles_of(self, clause: Clause) -> Set[str]:
        """The roles of *clause*: its template's, all of them, where a template read it; else its word's
        (:meth:`roles`: a row of the Action cell or in cells)."""
        spec = self._specs.get(clause.template)
        if spec is not None:
            return set(spec.roles or [])
        return self.roles(clause.behavior, clause.settings, clause.fields, str(clause.fields.get("to") or ""))

    def templates_with(self, role: str) -> List[str]:
        """The names of the templates declaring *role*, in the order they are tried.

        Parameters:
            role: A role's name.
        """
        return [
            name for _, name, spec, _ in self._compiled if role in (spec.roles or [])
        ]

    def with_role(self, role: str) -> Set[str]:
        """The words one of whose templates declares *role*, whatever its settings.

        Parameters:
            role: A role's name (``takes``, ``plural`` ...).
        """
        return {
            spec.word
            for _, _, spec, _ in self._compiled
            if role in (spec.roles or [])
        }

    def written(self, role: str, fields: Optional[Mapping[str, str]] = None) -> Optional[Tuple[str, Dict[str, str]]]:
        """``(word, cells)`` of the first template declaring *role* whose own fields hold *fields*: how a row of the
        role is written in cells (``answer``, ``{"label": "*"}``); None where none does."""
        for _, _, spec, _ in self._compiled:
            if role in (spec.roles or []) and all((spec.fields or {}).get(k) == v for k, v in (fields or {}).items()):
                return spec.word, dict(spec.fields or {})
        return None

    def plays(self, behavior: str) -> bool:
        """Whether a row of *behavior* plays a take (its template's ``clip``).

        Parameters:
            behavior: A word.
        """
        return any(
            spec.clip for _, _, spec, _ in self._compiled if spec.word == behavior
        )

    def readings(self) -> Dict[str, Tuple[str, Dict[str, str]]]:
        """Each template's name with what a row it reads becomes: its word and the settings it gives it
        (``use_hold``: ``use`` held)."""
        return {
            name: (spec.word, {k: v for k, v in (spec.settings or {}).items() if not _GROUP.match(str(v))})
            for _, name, spec, _ in self._compiled
        }

    def behaviors(self) -> List[str]:
        """Every word a row can read as, sorted."""
        out = set()
        for _, _, spec, _ in self._compiled:
            out.add(spec.word)
            out.update(spec.also)
        return sorted(out)

    def nodes(self, behavior: str) -> List[str]:
        """The node groups (:attr:`ClauseSpec.nodes`) of *behavior*'s templates, in order, each once."""
        found: Dict[str, None] = {}
        for _, _, spec, _ in self._compiled:
            if spec.word == behavior:
                found.update(dict.fromkeys(spec.nodes or []))
        return list(found)

    def read(self, line: str, column: str = "contents", options: bool = False) -> List[Clause]:
        """What *line* of *column* does: the first template to match reads it.

        Parameters:
            line: One row's text as the sheet writes it.
            column: ``contents`` or ``hint``.
            options: Read it as a question's row: by the templates of the ``option`` role alone (any
                other read: by the rest).

        Returns:
            One clause per word the row is (its template's, then its
            ``also``); none for an empty row or one no template reads.
        """
        text = self._clean(line)
        if not text:
            return []
        for _, name, spec, patterns in self._compiled:
            if spec.column not in (column, "any") or (OPTION in (spec.roles or [])) != options:
                continue
            for pattern in patterns:
                match = pattern.fullmatch(text)
                if match is None:
                    continue
                fields, settings = self._given(spec, self._fields(match))
                nodes = list(spec.nodes or [])
                clauses = [Clause(spec.word, line.strip(), dict(fields), spec.clip, name, settings, nodes)]
                clauses += [
                    Clause(extra, line.strip(), dict(fields), False, name, {}, nodes)
                    for extra in spec.also
                ]
                return clauses
        return []
