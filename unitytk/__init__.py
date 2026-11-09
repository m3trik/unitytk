__version__ = "0.7.0"

#: Where the Unity package's C# is, relative to this folder: the checkout keeps
#: it beside this one (a wheel carries none). Read as data by ``ptk.ApiSurface``
#: (``api.search``, the release's API check), never imported.
CSHARP_SOURCES = ("../com.m3trik.unitytk",)

# A declaration, never an import (CODE_STANDARD section 4): each name loads its
# module on first use, so ``import unitytk`` loads none of them. An explicit
# ``__all__``, no bootstrap.
from pythontk.core_utils.module_resolver import lazy_exports as _lazy_exports

__all__ = [
    "ProjectBuilder",
    "UnityCli",
    "UnityCompile",
    "UnityHost",
]

_lazy_exports(
    globals(),
    {
        "project_builder": "ProjectBuilder",
        "unity_cli": "UnityCli",
        "unity_compile": "UnityCompile",
        "unity_host": "UnityHost",
    },
)
