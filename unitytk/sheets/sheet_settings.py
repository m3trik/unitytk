# !/usr/bin/python
# coding=utf-8
"""SheetSettings -- one project's settings file: its sheet, its template, its delivery and its own names.

Any project a build sheet drives (a task, an animated procedure, a configurator) keeps one beside its
sheet, in the project that owns it (never in a public repository): the sheet and the files its delivery is
(one FBX is the aim; an older delivery may split its arrows, viewpoints or panel anchors into files of their
own), the template, the project's own name rules over its reading's (``USER_POS_1`` viewpoints,
``A01_1_KEYS_ARROW`` arrows, other build suffixes), the old names of renamed nodes (each an alias of its new
name, so a rename is one entry and old rows still read), the retired step IDs, the folder of its own clause
templates and each kit's own data. Paths are relative to the file, so a project moves as one folder.

A typed :class:`~pythontk.SchemaSpec`, so this one declaration validates a settings file.
"""

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, ClassVar, Dict, List, Optional, Union

import pythontk as ptk

from unitytk.sheets.delivery_index import DeliveryIndex
from unitytk.sheets.reading_spec import ReadingSpec
from unitytk.sheets.sheet_plan import DEFAULT_TEMPLATE

SchemaSpec = ptk.SchemaSpec
_field = SchemaSpec.spec_field


def _alias_cycles(value: Any) -> List[str]:
    """Each chain of *value*'s renamed nodes (``{old name: new name}``, names ignoring case) that comes back to
    a name it passed: such an old name names nothing."""
    if not isinstance(value, dict):
        return []
    renamed = {str(old).casefold(): str(new) for old, new in value.items()}
    errors, said = [], set()
    for old in value:
        chain, at = [str(old)], str(old).casefold()
        while at in renamed and at not in said:
            new = renamed[at]
            chain.append(new)
            if new.casefold() in {c.casefold() for c in chain[:-1]}:
                errors.append(
                    "%s goes round in a cycle: map each old name to a name the delivery has" % " -> ".join(chain)
                )
                said |= {c.casefold() for c in chain}
                break
            at = new.casefold()
    return errors


@dataclass
class SheetSettings(SchemaSpec):
    """A project's sheet settings: what its plan, its check and its conversion read."""

    TYPED = True

    #: The settings file's schema version: the one this reads.
    VERSION: ClassVar[int] = 1

    version: int = _field(
        help="The file's schema version (SheetSettings.VERSION): 1.",
        required=True,
        example=1,
    )
    sheet: str = _field(
        help="The sequence sheet: a path beside this file, or a URL.",
        required=True,
        example="sheet.csv",
    )
    mapping: str = _field(
        help="The sheet's template: `interactive`, `linear`, `default` or a user's.",
        example="interactive",
        default=DEFAULT_TEMPLATE,
    )
    deliveries: List[str] = _field(
        help="The FBX (or GLB) files the project's delivery is, in order (their takes in that order): paths beside this file.",
        example=["assembly.fbx", "arrows.fbx"],
        default_factory=list,
    )
    extras: List[str] = _field(
        help=(
            "Files whose nodes join the delivery but whose takes no step plays (an older delivery's arrows "
            "with their own bounce takes, its viewpoints, its panel anchors): paths beside this file."
        ),
        example=["arrows.fbx", "viewpoints.glb"],
        default_factory=list,
    )
    names: Dict[str, Any] = _field(
        help=(
            "The project's own name rules, over its reading's (`ReadingSpec.names`): `inline`, `viewpoint` and "
            "`arrow` regexes, and `suffixes`, the node-name endings its delivery's builds use, where it names "
            "them another way."
        ),
        validate=ReadingSpec.validate_names,
        example={"viewpoint": "^(VIEW_|USER_POS_)", "suffixes": ["GRP", "GEO", "MSH"]},
        default_factory=dict,
    )
    aliases: Dict[str, str] = _field(
        help="Renamed nodes, `{old name: new name}`: a row listing the old name reads the new one.",
        validate=_alias_cycles,
        example={"DOOR_OLD": "RED_DOOR"},
        default_factory=dict,
    )
    options: Dict[str, Any] = _field(
        help=(
            'The template\'s option values (`{"audio": "narrate"}`: each step\'s recording, else its '
            "narration, in `recordings/` and `narration/` beside this file)."
        ),
        example={"audio": "narrate"},
        default_factory=dict,
    )
    retired: List[str] = _field(
        help="Retired step IDs, as the sheet writes them: no step carries one again, no jump goes to one.",
        example=["B04"],
        default_factory=list,
    )
    clauses: str = _field(
        help=(
            "The folder of the project's own clause templates, beside this file (each a clause template's "
            "JSON, winning a built-in one of its name); a folder that does not exist: none."
        ),
        example="clauses",
        default="clauses",
    )
    extensions: Dict[str, Dict[str, Any]] = _field(
        help=(
            "Each kit's own data, by the kit's name, carried into the plan as it stands (its `extensions`): "
            "read by that kit alone, which says what is wrong with it."
        ),
        example={"my_kit": {"parts": {"LID_GRP": {"offset": [0.0, 0.0, 0.12]}}}},
        default_factory=dict,
    )

    def __post_init__(self):
        self.base = (
            Path.cwd()
        )  # where relative paths are read from: the file's folder, once loaded
        self.path: Optional[Path] = None  # the file it was loaded from

    @classmethod
    def load(cls, path: Union[str, Path]) -> "SheetSettings":
        """The settings saved at *path*, validated; its paths read beside it.

        Raises:
            ValueError: The file is no settings file of :attr:`VERSION` (the message names the fix).
        """
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        if data.get("version") != cls.VERSION:
            raise ValueError(
                f"{path} is a version {data.get('version')} sheet settings file; this reads "
                f'version {cls.VERSION}: set its "version" to {cls.VERSION} and check its fields'
            )
        result = cls.validate(data)
        if not result.ok:
            raise ValueError(f"{path} is not a sheet settings file: {result}")
        # A key nothing reads is a setting that silently does nothing (a tool's fit kept beside the settings
        # worked a screwdriver sideways): refused, never skipped. A `_`-led key is a note.
        known = {field.name for field in cls.describe()}
        stray = sorted(k for k in data if k not in known and not str(k).startswith("_"))
        if stray:
            raise ValueError(
                f"{path} gives {', '.join(repr(k) for k in stray)}, which no sheet settings file reads: "
                'a kit\'s own data goes under "extensions" by the kit\'s name '
                '({"extensions": {"task": {...}}}); move it there, or lead a note\'s key with "_"'
            )
        settings = cls.from_dict(data)
        settings.path = Path(path).resolve()
        settings.base = settings.path.parent
        return settings

    def _path(self, value: str) -> str:
        return value if "://" in value else str(self.base / value)

    def sheet_path(self) -> str:
        """The sheet: its path (beside the file) or URL."""
        return self._path(self.sheet)

    def delivery_paths(self) -> List[str]:
        """The delivery's FBX files' paths, in order."""
        return [self._path(d) for d in self.deliveries]

    def extra_paths(self) -> List[str]:
        """The extra files' paths (their nodes join the delivery), in order."""
        return [self._path(e) for e in self.extras]

    def clauses_path(self) -> Optional[str]:
        """The project's clause templates' folder, beside the file; None where it names none."""
        return self._path(self.clauses) if self.clauses else None

    def index(self) -> DeliveryIndex:
        """The delivery the project's files make together (an extra file's nodes and what they wear, not its
        takes), its renamed nodes read."""
        extras = [DeliveryIndex.from_file(p) for p in self.extra_paths()]
        extras = [DeliveryIndex(i.paths, (), i.name, kinds=i.kinds, worn=i.worn, states=i.states) for i in extras]
        return DeliveryIndex.merged(
            [DeliveryIndex.from_file(p) for p in self.delivery_paths()] + extras,
            self.aliases,
        )

    def template(self) -> Dict:
        """The sheet's mapping template (pythontk's ``Mapping.load_mapping``)."""
        from pythontk.core_utils.engines.shots.manifest.mapping import Mapping

        return Mapping.load_mapping(self.mapping)

    def reading(self) -> ReadingSpec:
        """The template's reading, the project's own name rules over its ``names`` (its strictness its own)."""
        return ReadingSpec.load(self.mapping).with_names(self.names)

    def book(self, reading: Optional[ReadingSpec] = None):
        """The clause templates the project's sheet is read with: the built-in ones and its own (:attr:`clauses`),
        with *reading*'s step shape (the project's reading where None)."""
        from unitytk.sheets.clause_book import ClauseBook

        return ClauseBook(
            folder=self.clauses_path(), step=(reading or self.reading()).step_id
        )

    def plan(self, book=None, index: Optional[DeliveryIndex] = None):
        """The project's plan (:meth:`SheetPlan.from_sheet` over its sheet, delivery and names), each kit's
        data carried (:meth:`SheetPlan.with_extensions`).

        Parameters:
            book: As :meth:`SheetPlan.from_sheet`; the project's own (:meth:`book`) where None.
            index: The delivery, read already; :meth:`index` where None.
        """
        from unitytk.sheets.sheet_plan import SheetPlan

        reading = self.reading()
        plan = SheetPlan.from_sheet(
            self.sheet_path(),
            index or self.index(),
            mapping=self.template(),
            reading=reading,
            options=self.options,
            aliases=self.aliases,
            book=book or self.book(reading),
            base=str(self.base),
            retired=self.retired,
        )
        if self.path is not None:
            plan.source.settings_digest = SheetPlan.digest(str(self.path))
        if book is None:  # read with the project's own templates: their folder, as this file names it
            plan.source.clauses = self.clauses
        return plan.with_extensions(self.extensions)

    def check(self, alone: bool = False, book=None):
        """The project's strict check (:class:`SheetCheck`), its retired IDs among the rules.

        Parameters:
            alone: Check the sheet alone (before the delivery exists), not against the delivery.
            book: As :meth:`SheetPlan.from_sheet`; the project's own (:meth:`book`) where None.
        """
        from unitytk.sheets.sheet_check import SheetCheck

        reading = self.reading()
        delivery: Optional[DeliveryIndex] = (
            None if alone or not (self.deliveries or self.extras) else self.index()
        )
        return SheetCheck.run(
            self.sheet_path(),
            delivery,
            mapping=self.template(),
            reading=reading,
            options=self.options,
            aliases=self.aliases,
            book=book or self.book(reading),
            retired=self.retired,
            base=str(self.base),
        )
