# !/usr/bin/python
# coding=utf-8
"""unitytk's operations: the actions agents and front ends run on a Unity package or project.

``unity_package.compile`` is the fast loop: the package's C# compiled with an
installed editor's own compiler, no editor running, in any Python;
``unity_project.install`` puts unitytk's package in a project, one manifest
line, so the ``unity`` host can open it; ``unity_project.build`` builds or
refreshes the owned test and demo project, made with Unity's CLI; ``sheet.check``
checks a build sheet (alone, or against its FBX) and writes the
take list the artist builds from; ``sheet.convert`` rewrites a sheet written
before the format in the Interactive one; ``sheet.sync`` reads a sheet again and
stages its plan, delivery and sounds into a Unity project, where the Manifest
window builds from them. The editor's own operations (``unity.*``)
are C#, catalogued in ``editor_operations.json``.
``ptk.OperationProvider`` turns these declarations into the committed
``operations.json`` beside this file; after changing one (or its function's
signature or docstring), rewrite it: ``python -m pythontk ops apply
operations.catalog module=unitytk.operations``.
"""

from pythontk import Operation

OPERATIONS = (
    Operation(
        "unity_package.compile",
        "unitytk:UnityCompile.compile",
        title="Compile the package's C#",
    ),
    Operation(
        "unity_project.install",
        "unitytk:UnityHost.install",
        effect="files",
        title="Install unitytk in a Unity project",
    ),
    Operation(
        "unity_project.build",
        "unitytk:ProjectBuilder.build",
        effect="files",
        title="Build the owned test and demo project",
    ),
    Operation(
        "sheet.check",
        "unitytk.sheets:SheetCheck.check",
        effect="files",
        title="Check a build sheet, alone or against its FBX",
    ),
    Operation(
        "sheet.convert",
        "unitytk.sheets:SheetConverter.convert",
        effect="files",
        title="Rewrite an old build sheet in the Interactive format",
    ),
    Operation(
        "sheet.sync",
        "unitytk.sheets:SheetSync.sync",
        effect="files",
        title="Read a sheet again and stage its plan and delivery into a Unity project",
    ),
)
