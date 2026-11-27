# !/usr/bin/python
# coding=utf-8
"""ClauseBook -- the clause templates, compiled: one row in, what it does out.

See :mod:`unitytk.sheets.clause_spec` for a template's shape. The book tries its templates in
``order`` (then name) and the first whose regex matches the whole row reads it; a row none
matches is one the plan reports as ``unread``. The templates are the built-in ones and a project's
own (a folder beside its settings file), never a per-user folder, so a sheet reads the same on every
machine; :meth:`ClauseBook.digest` says which of a project's own a plan was read with.
"""

import hashlib
import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Mapping, Optional, Pattern, Set, Tuple, Union

from unitytk.sheets.clause_spec import ClauseSpec

_SUFFIX = re.compile(r"_\d+$")
_SPLIT = re.compile(r"\s*(?:,|\band\b|&)\s*", re.I)
#: The quotes between a quoted list's items ('YES' and 'NO', 'Lights', 'Power').
_QUOTED_JOIN = re.compile(r"['\"]\s*(,|\band\b|&)\s*['\"]", re.I)
#: What a template writes for a step ID's shape.
STEP = "{step}"


@dataclass
class Clause:
    """One row read: the behavior and the fields its template's groups filled.

    Attributes:
        behavior: The behavior it reads as.
        text: The row as the sheet writes it.
        fields: ``{group: value}``: ``targets`` a list of phrases, any other the group's text.
        clip: Whether the row plays one of its step's takes.
        template: The template that read it (empty: none did; ``action``: the Action cell).
        settings: The settings it gives its behavior.
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


class ClauseBook(_ClauseBookInternal):
    """The clause templates a plan reads rows with, compiled once.

    Parameters:
        templates: ``(name, spec)`` pairs; default: :meth:`load_templates` (the built-in ones,
            and *folder*'s over them).
        folder: A project's own templates (a folder of ``ClauseSpec`` JSON files), when
            *templates* is None.
        step: A step ID's shape, which a template's ``{step}`` stands for; default: the default
            reading's (:attr:`ReadingSpec.step_id`).

    Attributes:
        folder: The project's own templates it read (*folder*, where *templates* is None), else None.
    """

    def __init__(
        self,
        templates: Optional[Iterable[Tuple[str, ClauseSpec]]] = None,
        *,
        folder: Union[str, Path, None] = None,
        step: Optional[str] = None,
    ):
        self.folder = Path(folder) if templates is None and folder else None
        if templates is None:
            templates = self.load_templates(folder)
        if step is None:
            from unitytk.sheets.reading_spec import ReadingSpec

            step = ReadingSpec.load().step_id
        self.step = step
        compiled = []
        for name, spec in templates:
            patterns = [re.compile(p.replace(STEP, step), re.I) for p in spec.detect]
            compiled.append((spec.order, name, spec, patterns))
        compiled.sort(key=lambda item: (item[0], item[1]))
        self._compiled: List[Tuple[int, str, ClauseSpec, List[Pattern]]] = compiled

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

    def digest(self) -> str:
        """The SHA-256 (hex) of the project's own template files it read (:attr:`folder`), as a reader with
        the folder in hand makes it again: a line per ``*.json`` file in name order, its name and its own
        SHA-256 (its bytes, CRLF read as LF) apart by a space, joined by newlines, UTF-8. Empty where it read
        none: the built-in ones are the reader's own, read again with it."""
        from unitytk.sheets.sheet_plan import SheetPlan

        files = (
            sorted(self.folder.glob("*.json"), key=lambda p: p.name) if self.folder else []
        )
        lines = "\n".join(f"{p.name} {SheetPlan.digest(str(p))}" for p in files)
        return hashlib.sha256(lines.encode("utf-8")).hexdigest() if lines else ""

    def roles(
        self, behavior: str, settings: Optional[Mapping[str, str]] = None
    ) -> Set[str]:
        """The roles a row of *behavior* carrying *settings* has: those declared by the templates of the
        behavior whose own settings the row carries. A template with settings says what its rows are in full
        (a row that takes a checklist away lists no items), so where the row carries one's settings, the
        behavior's templates with none do not speak for it; a row carrying no such settings has theirs.

        Parameters:
            behavior: The row's behavior.
            settings: The row's settings (:attr:`Clause.settings`, ``PlanAction.settings``).
        """
        settings = settings or {}
        general: Set[str] = set()
        own: Set[str] = set()
        set_apart = False
        for _, _, spec, _ in self._compiled:
            if spec.behavior != behavior:
                continue
            if not spec.settings:
                general.update(spec.roles or [])
            elif all(settings.get(k) == v for k, v in spec.settings.items()):
                set_apart = True
                own.update(spec.roles or [])
        return own if set_apart else general

    def templates_with(self, role: str) -> List[str]:
        """The names of the templates declaring *role*, in the order they are tried.

        Parameters:
            role: A role's name.
        """
        return [
            name for _, name, spec, _ in self._compiled if role in (spec.roles or [])
        ]

    def with_role(self, role: str) -> Set[str]:
        """The behaviors one of whose templates declares *role*, whatever its settings.

        Parameters:
            role: A role's name (``takes``, ``plural`` ...).
        """
        return {
            spec.behavior
            for _, _, spec, _ in self._compiled
            if role in (spec.roles or [])
        }

    def plays(self, behavior: str) -> bool:
        """Whether a row of *behavior* plays a take (its template's ``clip``).

        Parameters:
            behavior: A behavior's name.
        """
        return any(
            spec.clip for _, _, spec, _ in self._compiled if spec.behavior == behavior
        )

    def readings(self) -> Dict[str, Tuple[str, Dict[str, str]]]:
        """Each template's name with what a row it reads becomes: its behavior and the settings it gives it
        (``press_hold``: ``use`` held to play)."""
        return {
            name: (spec.behavior, dict(spec.settings or {}))
            for _, name, spec, _ in self._compiled
        }

    def behaviors(self) -> List[str]:
        """Every behavior a row can read as, sorted."""
        out = set()
        for _, _, spec, _ in self._compiled:
            out.add(spec.behavior)
            out.update(spec.also)
        return sorted(out)

    def nodes(self, behavior: str) -> List[str]:
        """The node groups (:attr:`ClauseSpec.nodes`) of *behavior*'s templates, in order, each once."""
        found: Dict[str, None] = {}
        for _, _, spec, _ in self._compiled:
            if spec.behavior == behavior:
                found.update(dict.fromkeys(spec.nodes or []))
        return list(found)

    def read(self, line: str, column: str = "contents") -> List[Clause]:
        """What *line* of *column* does: the first template to match reads it.

        Parameters:
            line: One row's text as the sheet writes it.
            column: ``contents`` or ``hint``.

        Returns:
            One clause per behavior the row is (its template's, then its
            ``also``); none for an empty row or one no template reads.
        """
        text = self._clean(line)
        if not text:
            return []
        for _, name, spec, patterns in self._compiled:
            if spec.column not in (column, "any"):
                continue
            for pattern in patterns:
                match = pattern.fullmatch(text)
                if match is None:
                    continue
                fields = self._fields(match)
                nodes = list(spec.nodes or [])
                clauses = [
                    Clause(
                        spec.behavior,
                        line.strip(),
                        dict(fields),
                        spec.clip,
                        name,
                        dict(spec.settings or {}),
                        nodes,
                    )
                ]
                clauses += [
                    Clause(extra, line.strip(), dict(fields), False, name, {}, nodes)
                    for extra in spec.also
                ]
                return clauses
        return []
