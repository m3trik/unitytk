# !/usr/bin/python
# coding=utf-8
"""unitytk's operations: the actions agents and front ends run on a Unity package or project.

``unity_package.compile`` is the fast loop: the package's C# compiled with an
installed editor's own compiler, no editor running, in any Python;
``unity_project.install`` puts unitytk's package in a project, one manifest
line, so the ``unity`` host can open it; ``unity_project.build`` builds or
refreshes the owned test and demo project, made with Unity's CLI. The editor's own operations (``unity.*``)
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
)
