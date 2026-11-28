# !/usr/bin/python
# coding=utf-8
"""SheetPlan -- what a sheet builds in Unity, declared once.

The seam between unitytk's two halves. This one reads a build sheet (the rows its author writes,
one step after another) and the delivery its steps play (an FBX: its nodes and its takes), and
writes a plan: each step's values, and each row read as a behavior on named nodes. The Unity
package's manifest reads the plan (its generated ``SheetPlan.g.cs``) and builds the scene from it,
one row at a time, each through the builder a kit registers for its word.

The plan speaks the sheet's own words: a row's behavior is its word (pythontk's template), its
``fields`` its cells by their names (``with``, ``label``, ``where``), its ``settings`` its settings
as written, a sheet of sentences read into the same. It names no kit's data: a step's values are its
``fields`` (named by the reading, :attr:`ReadingSpec.step_fields`), and a kit's own plan-wide data is
its entry of ``extensions``, as the project's settings file gives it.

A typed :class:`~pythontk.SchemaSpec` declaration, so this one definition validates a plan,
publishes its JSON Schema, and is what ``m3trik/scripts/sync_unity_contracts.py`` generates the C#
reader from: a field renamed here fails that check until it is regenerated. Change a field
together with the kit, and bump :attr:`SheetPlan.VERSION`.

Prior art: the Shot Manifest reads the same sheets for the DCC side (:class:`pythontk.ShotManifest`;
its parser, ``Mapping.resolve``, is the one this reads them through), so a sheet that builds the
shots builds the Unity scene too. Authoring tools that keep a procedure in their own editor's graph
(VR Builder and kin) save it as typed .NET objects; a sheet is what the studio already writes.
"""

import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import (
    TYPE_CHECKING,
    Any,
    ClassVar,
    Dict,
    Iterable,
    List,
    Mapping,
    Optional,
    Union,
)

import pythontk as ptk

from unitytk.sheets._plan_reading import _SheetPlanInternal

#: The template a sheet is read with where none is named: the Interactive format.
DEFAULT_TEMPLATE = "interactive"

#: The cell an issue names first: ``row 3 (Asset Names): ``, a hint line's ``(Hint): ``, or a step
#: row's own ``step A01 (To): ``.
_ISSUE_CELL = re.compile(r"^(?:step (?P<step>\S+) )?(?:row (?P<row>\d+) )?\((?P<column>[^)]+)\): ")

if TYPE_CHECKING:  # the reading pass's collaborators, named in from_sheet's signature
    from unitytk.sheets.clause_book import ClauseBook
    from unitytk.sheets.delivery_index import DeliveryIndex
    from unitytk.sheets.reading_spec import ReadingSpec

SchemaSpec = ptk.SchemaSpec
_field = SchemaSpec.spec_field


@dataclass
class PlanSource(SchemaSpec):
    """What a plan was read from, by file name (never a path: a plan travels), with each
    file's digest, so a reader with the file in hand can tell that it changed since."""

    TYPED = True

    sheet: str = _field(
        help="The sequence sheet's file name (or its URL's last segment).",
        example="door_service.csv",
        default="",
    )
    delivery: str = _field(
        help="The delivery's file name: the FBX whose nodes and takes the plan names.",
        example="cabinet.fbx",
        default="",
    )
    sheet_digest: str = _field(
        help="The SHA-256 of the sheet file as read (hex; its bytes, CRLF read as LF); empty for a sheet read from a URL.",
        example="",
        default="",
    )
    settings: str = _field(
        help=(
            "The sheet's settings file, as the project the plan was staged into reaches it (a path "
            "from that project's folder, else an absolute one): set by the staging alone, the one "
            "path a plan carries, so the project can say that the sheet changed. Empty otherwise."
        ),
        example="../sheets/door_service.json",
        default="",
    )
    settings_digest: str = _field(
        help="The SHA-256 of the settings file as read (hex; its bytes, CRLF read as LF); empty where no settings file was.",
        example="",
        default="",
    )
    clauses: str = _field(
        help=(
            "The folder of the project's own clause templates the rows were read with, as its settings "
            "file names it (beside that file): set where the plan is the settings file's reading. Empty otherwise."
        ),
        example="clauses",
        default="",
    )
    clauses_digest: str = _field(
        help=(
            "The SHA-256 of the project's own clause templates the rows were read with (hex): a line per "
            "JSON file of the folder in name order, its name and its own SHA-256 (its bytes, CRLF read as "
            "LF) apart by a space, joined by newlines, UTF-8; empty where it holds none."
        ),
        example="",
        default="",
    )


@dataclass
class PlanAction(SchemaSpec):
    """One row of a step, read as one of the sheet's words on named nodes."""

    TYPED = True

    behavior: str = _field(
        help=(
            "What the row does: the sheet's word, as a row in cells writes it (`highlight`, `indicate`, `show`, "
            "`hide`, `set`, `take`, `return`, `use`, `play`, `button`, `select`, `answer`, `travel`, `picture`, "
            "`list`, `tick`, `note`: the Interactive template's words), a sheet of sentences' rows read into "
            "the same words; `advance` (the step goes on by itself: a sheet of sentences' \"Auto Advance\"); or "
            "`unread` for a row nothing reads. The kit builds a word it has a builder for and reports the rest."
        ),
        required=True,
        example="use",
    )
    text: str = _field(
        help="The row as the sheet writes it: its sentence (a row in cells, the sentence its cells read as).",
        required=True,
        example="Selecting the door triggers Door open Animation",
    )
    targets: List[str] = _field(
        help=(
            "The row's Target: the nodes it is about, each its path in the delivery from the file's top level "
            "down, '|' joined (the path tells same-named nodes apart); a travel's, what the user is brought to "
            "face. A name that finds no node is left out, with an issue."
        ),
        example=["CABINET|DOOR_GRP"],
        default_factory=list,
    )
    clip: str = _field(
        help=(
            "The delivery's take the row plays: the step's ID where the step "
            "plays one clip, `<step>_<n>` for its n-th where it plays several "
            "(the Shot Manifest's names for them); absent when the row plays none."
        ),
        example="A02",
    )
    to: str = _field(
        help=(
            "Where the row goes, as its To cell writes it: `next` (on), `restart` (the sequence over, its scene "
            "loaded again: nothing a take did stands), `exit` (out), `retry` (a wrong answer: the step stays; a step "
            "holding one is a question, its `next` choice the right answer), or a step's ID as the step's own "
            "(`B03.5` spelled `B03_5`)."
        ),
        example="B04",
    )
    fields: Dict[str, Union[str, List[str]]] = _field(
        help=(
            "The row's cells beyond its word, Target, take and jump, by the sheet's names: `with`, the With node's "
            "path (what the hand holds); `label`, the Label (a button's words, an answer's value or `*` for any "
            "other, a list's items comma separated, a picture's caption); `where`, the node each Target happens "
            "at, one per Target in order (`\"\"` where a Target has none; one where the row has no Target): an "
            "indicate's pointer, a travel's viewpoint, a picture's camera; `helpers`, the names Unity gives the "
            "helpers it makes for a placing row that names none, one per Target. A sheet of sentences adds what "
            "its words say: `side` and `side_of` (a travel to a side of a node), `phrase` (a take's words), and a "
            "project's own clause groups. The kit that builds the word reads the names it knows."
        ),
        example={"with": "TOOLS|WRENCH_GRP", "where": ["VIEW_FRONT"], "phrase": "Door open"},
        default_factory=dict,
    )
    issues: List[str] = _field(
        help=(
            "What the row could not be read into: a phrase that names no node, a take the delivery lacks. "
            "Each keeps the row from being built. A strict sheet's name their cell and fix: "
            "`row <n> (<column>): <reason>: <fix>` (a hint line's `(<column>): ...`), the row counted in "
            "the step (SheetPlan.parse_issue)."
        ),
        example=['no node is "the toolbox lid"'],
        default_factory=list,
    )
    settings: Dict[str, str] = _field(
        help=(
            "The settings the row gives its word, by the sheet's own names and values as written (a sheet of "
            "sentences' as its clause gives them: \"Pressing and holding ...\" a use with `trigger: hold`): every "
            "one but an object's (`where`, a field). A span (`until`, `ring`) is `step` (as its own step ends) or a "
            "step's ID as the step's own (as that step starts). The kit resolves them under its settings of the "
            "row's word; a person may change them per object in the manifest. Absent: the kit's."
        ),
        example={"trigger": "hold", "hold": "2"},
        default_factory=dict,
    )
    upstream: List[str] = _field(
        help=(
            "The targets the delivery's own build already made this behavior on: its producer keyed it into "
            "the step's takes for them (each animation record behaviors entry's `mode: key`), so a build here "
            "leaves them to the delivery and builds the rest. Two manifests of one sheet, the producer's and "
            "this one, build each behavior once between them; either alone builds all it can."
        ),
        example=["CABINET|DOOR_GRP"],
        default_factory=list,
    )
    warnings: List[str] = _field(
        help=(
            "What may be wrong with the row, which is built anyway: a take that moves none of its "
            "row's names, a typo read past. Stated as `issues` are."
        ),
        example=[
            "row 2 (Animation Clip Names): the take A01_2 moves none of this row's LATCH_LOWER: re-export it"
        ],
        default_factory=list,
    )

    def field(self, name: str) -> Optional[Union[str, List[str]]]:
        """The row's value named *name*: a declared text field (``clip``, ``to``), else its
        :attr:`fields` entry; None where it has none.

        Parameters:
            name: A field's name.
        """
        if name in ("clip", "to"):
            return getattr(self, name)
        return self.fields.get(name)


@dataclass
class PlanStep(SchemaSpec):
    """One step: its values, and its rows as actions in sheet order."""

    TYPED = True

    id: str = _field(
        help="The step's ID as its shot is named (`A01`; `B03.5` spelled `B03_5`).",
        required=True,
        example="A01",
    )
    name: str = _field(help="The step's name.", example="Open the door", default="")
    section: str = _field(help="The section the step sits in.", example="A", default="")
    section_title: str = _field(
        help="The section's title.", example="Access", default=""
    )
    fields: Dict[str, str] = _field(
        help=(
            "The step's own values, by the names the reading gives them: each of the step's columns it "
            "carries (its narration, its page text, its hint: as the sheet writes them), and its sound file "
            "as the template's audio methods find it, a path relative to the project's settings folder. A "
            "value the sheet leaves empty is left out. The kit that builds the step reads the names it knows."
        ),
        example={
            "voice": "Select the door to open it.",
            "sound": "narration/select_the_door_4539e2926f60.wav",
        },
        default_factory=dict,
    )
    actions: List[PlanAction] = _field(
        help="The step's rows (its contents, then its hint's), each read as an action.",
        nested=[PlanAction],
        default_factory=list,
    )

    def field(self, name: str) -> Optional[str]:
        """The step's value named *name* (:attr:`fields`); None where it has none.

        Parameters:
            name: A value's name.
        """
        return self.fields.get(name)


@dataclass
class SheetPlan(SchemaSpec, _SheetPlanInternal):
    """A sheet plan: the steps a sheet writes, each row a behavior on the
    delivery's nodes."""

    # The docstrings and help texts above are the contract's own words: the
    # generated C# reader carries them, so they name no Python. A plan is
    # built by from_sheet, below.

    TYPED = True

    #: The plan's schema version: the one this writes and the kit reads.
    VERSION: ClassVar[int] = 2

    version: int = _field(
        help="The plan's schema version (SheetPlan.VERSION): 2.",
        required=True,
        example=2,
    )
    source: PlanSource = _field(
        help="What the plan was read from.",
        nested=PlanSource,
    )
    steps: List[PlanStep] = _field(
        help="The steps in sheet order.",
        required=True,
        nested=[PlanStep],
        default_factory=list,
    )
    extensions: Dict[str, Dict[str, Any]] = _field(
        help=(
            "Each kit's own data for the whole plan, by the kit's name, as the project's settings file "
            "gives it (its `extensions`): read by that kit alone, which says what is wrong with it."
        ),
        example={"my_kit": {"parts": {"LID_GRP": {"offset": [0.0, 0.0, 0.12]}}}},
        default_factory=dict,
    )
    issues: List[str] = _field(
        help=(
            "What the plan as a whole could not account for: a take no step "
            "plays, a step two rows send to that the sheet lacks."
        ),
        example=["no step plays the take Intro"],
        default_factory=list,
    )

    @staticmethod
    def digest(path: str) -> str:
        """The SHA-256 of the text file at *path* (hex) as a plan's source records a file it was read from
        (:attr:`PlanSource.sheet_digest`, ``settings_digest``, each file of ``clauses_digest``): its bytes, CRLF
        read as LF (a checkout's line endings are no change); ``""`` for a URL or no file.

        Parameters:
            path: The file.
        """
        if not path or "://" in str(path) or not Path(path).is_file():
            return ""
        return hashlib.sha256(
            Path(path).read_bytes().replace(b"\r\n", b"\n")
        ).hexdigest()

    @classmethod
    def from_sheet(
        cls,
        sheet: str,
        delivery: Union[str, "DeliveryIndex"],
        *,
        mapping: Union[str, Mapping[str, Any], None] = None,
        reading: Union[str, Mapping[str, Any], "ReadingSpec", None] = None,
        options: Optional[Mapping[str, Any]] = None,
        aliases: Optional[Mapping[str, str]] = None,
        book: Optional["ClauseBook"] = None,
        base: Optional[str] = None,
        retired: Iterable[str] = (),
    ) -> "SheetPlan":
        """Read *sheet* into a plan over *delivery*'s nodes and takes.

        Parameters:
            sheet: The sequence sheet: a CSV path or an ``http(s)`` URL, read as
                the Shot Manifest reads it (``Mapping.resolve``).
            delivery: The FBX or GLB the steps play (a path), or its
                :class:`~unitytk.sheets.DeliveryIndex`.
            mapping: The sheet's mapping template: a name, a loaded dict, or None
                for :data:`DEFAULT_TEMPLATE`.
            reading: How it is read (:class:`ReadingSpec`): a reading, a template's name, or a
                dict over the reading of the template named by *mapping*; None: that reading
                (the default reading for a loaded mapping).
            options: The template's option values (an option left out takes
                its default; Step IDs default to Auto, which reads either layout).
            aliases: ``{phrase: node}`` where the sheet's words name a node no
                rule finds (``"the highlighted area": "LATCH"``); a node is its
                name or path, a phrase matched ignoring case.
            book: The clause templates to read rows with (default: the built-in ones,
                with the reading's step shape).
            base: The project's settings folder: the sheet's relative audio folders are read
                there, and each step's sound is relative to it.
            retired: Step IDs the project retired (as the sheet writes them): a row's jump to one is
                its issue, saying so.

        Returns:
            The plan; :meth:`write` saves it. Every row is an action, one no
            template reads as ``unread``; what a row could not be read into is
            in its ``issues``, and the plan's own in :attr:`issues`.
        """
        return cls._read(
            sheet,
            delivery,
            mapping=mapping,
            reading=reading,
            options=options,
            aliases=aliases,
            book=book,
            base=base,
            retired=retired,
        )

    @classmethod
    def load(cls, path: Union[str, Path]) -> "SheetPlan":
        """The plan saved at *path*, validated.

        Raises:
            ValueError: The file is no plan of :attr:`VERSION` (the message names
                each field that fails).
        """
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        if data.get("version") != cls.VERSION:
            raise ValueError(
                f"{path} is a version {data.get('version')} plan; this reads "
                f"{cls.VERSION}: read its sheet again (SheetPlan.from_sheet)"
            )
        result = cls.validate(data)
        if not result.ok:
            raise ValueError(f"{path} is not a sheet plan: {result}")
        return cls.from_dict(data)

    def with_extensions(
        self, extensions: Optional[Mapping[str, Mapping[str, Any]]]
    ) -> "SheetPlan":
        """Set :attr:`extensions` from a settings file's ``{kit: {key: value}}``, each kit's data as given.

        Returns:
            This plan.
        """
        self.extensions = {
            str(kit): dict(data) for kit, data in (extensions or {}).items()
        }
        return self

    def write(
        self, path: Union[str, Path], ctx: Optional["ptk.OpContext"] = None
    ) -> Path:
        """Save the plan at *path* (UTF-8 JSON, LF), validated first.

        Parameters:
            path: The plan file (a project's, under its ``Assets/``).
            ctx: Asked before the file is written (``should_write``): a dry
                run writes nothing.

        Raises:
            ValueError: The plan fails its own declaration.
        """
        data = self.text().encode("utf-8")
        path = Path(path)
        if ctx is not None and not ctx.should_write(str(path), "write the plan"):
            return path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def text(self) -> str:
        """The plan as its file holds it (UTF-8 JSON, LF), validated first.

        Raises:
            ValueError: The plan fails its own declaration.
        """
        data = self.to_dict()
        result = type(self).validate(data)
        if not result.ok:
            raise ValueError(f"the plan fails its declaration: {result}")
        return json.dumps(data, indent=2, ensure_ascii=False) + "\n"

    def actions(self, behavior: Optional[str] = None) -> Iterable[PlanAction]:
        """Every action of every step in order; only *behavior*'s when given."""
        for step in self.steps:
            for action in step.actions:
                if behavior is None or action.behavior == behavior:
                    yield action

    def summary(self) -> Dict[str, Any]:
        """``{steps, actions, unread, issues, warnings, behaviors}``: the plan in numbers."""
        actions = list(self.actions())
        return {
            "steps": len(self.steps),
            "actions": len(actions),
            "unread": sum(1 for a in actions if a.behavior == "unread"),
            "issues": sum(len(a.issues) for a in actions) + len(self.issues),
            "warnings": sum(len(a.warnings) for a in actions),
            "behaviors": self.counts(),
        }

    @classmethod
    def take_list(
        cls,
        sheet: str,
        *,
        mapping: Union[str, Mapping[str, Any], None] = None,
        reading: Union[str, Mapping[str, Any], "ReadingSpec", None] = None,
        options: Optional[Mapping[str, Any]] = None,
        book: Optional["ClauseBook"] = None,
    ) -> List[Dict[str, Any]]:
        """The takes *sheet* names, in sheet order, as :meth:`from_sheet` names
        them: what the artist builds and names in the DCC.

        Parameters:
            sheet: The sheet (CSV path or URL).
            mapping: As :meth:`from_sheet`.
            reading: As :meth:`from_sheet`.
            options: As :meth:`from_sheet`.
            book: As :meth:`from_sheet`.

        Returns:
            ``[{take, step, row, text}]``: the take's name, its step's ID, its
            row counted in the step (None for a sheet without take cells, whose
            takes are numbered by their clip rows) and that row's text.
        """
        steps, template = cls._steps(sheet, mapping, options)
        reading = cls._reading(mapping, reading)
        return cls._take_list(steps, template, reading, book or cls._book(reading))

    @staticmethod
    def parse_issue(text: str) -> Dict[str, Any]:
        """One issue's parts, as :attr:`PlanAction.issues` states them.

        Parameters:
            text: An issue or a warning: ``row <n> (<column>): <reason>[: <fix>]``,
                ``(<column>): ...`` for a hint line, ``step <id> (<column>): ...`` for a
                step row's own cell (a plan's issue), or a bare ``<reason>[: <fix>]``.

        Returns:
            ``{step, row, column, reason, fix}``: the step the text itself names
            (``""`` where it names none: an action's issue is its step's), the row
            counted in its step (None where it names none), the column (``""``
            where none), the reason, and the fix (``""`` where none).
        """
        step, row, column, rest = "", None, "", text
        cell = _ISSUE_CELL.match(text)
        if cell:
            step = cell.group("step") or ""
            row = int(cell.group("row")) if cell.group("row") else None
            column = cell.group("column")
            rest = text[cell.end() :]
        reason, _, fix = rest.rpartition(": ")
        if not reason:
            reason, fix = fix, ""
        return {"step": step, "row": row, "column": column, "reason": reason, "fix": fix}

    def counts(self) -> Dict[str, int]:
        """``{behavior: actions}`` over the plan, ``unread`` included."""
        out: Dict[str, int] = {}
        for action in self.actions():
            out[action.behavior] = out.get(action.behavior, 0) + 1
        return dict(sorted(out.items()))
