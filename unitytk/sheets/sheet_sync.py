# !/usr/bin/python
# coding=utf-8
"""SheetSync -- one action from a sheet's settings file to a Unity project.

A sheet reaches Unity as three kinds of file under the project's ``Assets``: the plan
(the sheet as read against its delivery), the delivery's own files, and the files its
steps name (each step field the reading stages, :attr:`ReadingSpec.staged`: a step's
sound). :meth:`SheetSync.sync` reads the sheet again and stages all three, writing only
what differs, so the same call is the first setup and every later update: the Unity
package's Manifest window then shows what a build would change. A delivery file is read
once for the plan (its media skipped) and compared and copied in chunks, never held whole.

Prior art: a folder a DCC exports into is how a delivery already reaches a project
(``ptk.FolderDeliverer``); this is that hand-off for what the sheet's reader makes,
and it writes no Unity asset of its own (a ``.meta``, the manifest): the editor makes
those as it imports the files.
"""

import filecmp
import os
import shutil
from pathlib import Path
from typing import Any, Dict, List, Optional

import pythontk as ptk

from unitytk.sheets.sheet_settings import SheetSettings


class SheetSync:
    """Stages what a sheet builds from into a Unity project."""

    #: A staged plan's file name: the settings file's stem, then this.
    PLAN_SUFFIX = ".plan.json"

    #: The issue a staging raises where voiced steps the sheet narrates have no sound yet.
    SILENT_CODE = "unity.sheet.silent"

    @classmethod
    def sync(
        cls,
        settings: str,
        project: str,
        folder: str = "",
        ctx: Optional["ptk.OpContext"] = None,
    ) -> Dict[str, Any]:
        """Read a sheet again and stage its plan, its delivery and its steps' files into a Unity project.

        The plan is read as :meth:`SheetSettings.plan` reads it and written as
        ``<settings stem>.plan.json``; the delivery's files (and the extras) are copied beside
        it by name, each file a step names (its staged fields: its sound) at its own path under
        the folder. A file whose bytes are already there is left alone, so Unity reimports only
        what changed; nothing is deleted. In the project, Tools > unitytk > Manifest opens the plan's manifest (New...
        makes it once) and shows what a build would change.

        Parameters:
            settings: The sheet's settings file (:class:`SheetSettings`).
            project: The Unity project's folder (the one holding ``Assets``).
            folder: Where to stage, under the project (``Assets/...``); empty: ``Assets/<settings stem>``.
            ctx: An optional ``ptk.OpContext``, asked before each file is written: a dry run lists them.

        Returns:
            ``{plan, folder, written, unchanged, steps, issues, missing_files, silent}``: the
            plan's file, the folder staged into, the files written (or planned), how many were
            there already, the plan's step count, how many issues its rows carry, the files the
            plan's steps name that are not beside the settings file, and the voiced steps the
            sheet narrates that have no sound yet (said on *ctx* too, with the render as its fix).

        Raises:
            ValueError: *project* is no Unity project, *folder* is not under its ``Assets``, two of
                the delivery's files share a name (they stage into one folder), or a file a step
                names lies outside the settings file's folder (its path beside the plan would leave
                the staged folder). Nothing is written then.
        """
        loaded = SheetSettings.load(settings)
        root = Path(project).resolve()
        if not (root / "Assets").is_dir() or not (root / "ProjectSettings").is_dir():
            raise ValueError(
                f"{project} is no Unity project (no Assets and ProjectSettings folders): "
                "name the project's own folder"
            )
        stem = Path(settings).stem
        target = (root / (folder or f"Assets/{stem}")).resolve()
        if (root / "Assets") not in target.parents and target != root / "Assets":
            raise ValueError(
                f"{folder} is not under the project's Assets folder: stage where Unity imports (Assets/...)"
            )

        plan = loaded.plan()
        plan.source.settings = cls._reach(loaded.path, root)
        written: List[str] = []
        unchanged = 0

        def put(path: Path, source, action: str) -> None:
            """Stage *source* (bytes, or a file copied in chunks) at *path*, unless the same bytes are there."""
            nonlocal unchanged
            same = path.is_file() and (
                path.read_bytes() == source
                if isinstance(source, bytes)
                else filecmp.cmp(source, path, shallow=False)
            )
            if same:
                unchanged += 1
                return
            written.append(str(path))
            if ctx is not None and not ctx.should_write(str(path), action):
                return
            path.parent.mkdir(parents=True, exist_ok=True)
            if isinstance(source, bytes):
                path.write_bytes(source)
            else:
                shutil.copyfile(source, path)

        # Every destination is settled before anything is written: a refusal leaves the project as it was.
        files = [
            Path(source) for source in loaded.delivery_paths() + loaded.extra_paths()
        ]
        names = [f.name.lower() for f in files]
        twice = sorted({f.name for f in files if names.count(f.name.lower()) > 1})
        if twice:
            raise ValueError(
                f"two of the delivery's files are named {', '.join(twice)}: they stage into one folder, "
                "so rename one (and its entry in the settings file)"
            )
        named, missing = [], []
        staged_fields = loaded.reading().staged
        for step in plan.steps:
            for field in staged_fields:
                value = step.field(field)
                if not value:
                    continue
                staged = (target / value).resolve()
                if target not in staged.parents:
                    raise ValueError(
                        f"{step.id}'s {field} {value} lies outside the settings file's folder: keep each "
                        "file a step names under it, so its path holds beside the plan"
                    )
                source = loaded.base / value
                if source.is_file():
                    named.append((staged, source, f"{step.id}'s {field}"))
                else:
                    missing.append(value)

        plan_file = target / (stem + cls.PLAN_SUFFIX)
        put(plan_file, plan.text().encode("utf-8"), "write the plan the sheet reads as")
        for source in files:
            put(target / source.name, source, "stage the delivery's file")
        for staged, source, what in named:
            put(staged, source, f"stage {what}")
        silent = cls._silent(loaded, plan, ctx)
        return {
            "plan": str(plan_file),
            "folder": str(target),
            "written": written,
            "unchanged": unchanged,
            "steps": len(plan.steps),
            "issues": plan.summary()["issues"],
            "missing_files": missing,
            "silent": silent,
        }

    @classmethod
    def _silent(cls, loaded: SheetSettings, plan, ctx) -> List[str]:
        """The voiced steps the sheet narrates that have no sound yet (``SheetCheck.silent``), said once on
        *ctx* as a warning whose fix is the render that makes their lines: staged all the same, since a
        recording may come later, but never silently."""
        from pythontk.core_utils.engines.shots.manifest.mapping import Mapping
        from unitytk.sheets.sheet_check import SheetCheck

        template = Mapping.apply_options(loaded.template(), dict(loaded.options or {}))
        silent = SheetCheck.silent(plan, template, loaded.reading())
        if silent and ctx is not None:
            named = ", ".join(silent[:5]) + (f" and {len(silent) - 5} more" if len(silent) > 5 else "")
            ctx.issue(
                cls.SILENT_CODE,
                f"{len(silent)} voiced step(s) have no sound yet ({named}): the task is silent there. "
                "Render their narration (or add their recordings), then stage again",
                "warning",
                location=str(loaded.path),
                fix={
                    "op": "narration.render",
                    "args": {
                        "sheet": str(loaded.sheet_path()),
                        "mapping": loaded.mapping,
                        "options": dict(loaded.options or {}),
                        "base": str(loaded.base),
                    },
                },
            )
        return silent

    @staticmethod
    def _reach(settings: Path, project: Path) -> str:
        """How *project* reaches the settings file: a path from its folder (``/`` separated),
        else the absolute one (another drive)."""
        try:
            return Path(os.path.relpath(settings, project)).as_posix()
        except ValueError:
            return settings.as_posix()
