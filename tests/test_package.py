# !/usr/bin/python
# coding=utf-8
"""The package root: a declaration, never an import (CODE_STANDARD section 4).

``import unitytk`` is paid by every consumer that only wants one name (a DCC's
tool reading ``UnityCli``): an eager ``__init__`` loaded every
module of the package -- and pythontk's under them -- on that import.
"""

import os
import sys
import unittest

try:
    from .base_test import UnityTkTestCase
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import UnityTkTestCase


class TestPackageRoot(UnityTkTestCase):
    def test_plain_import_loads_only_declarations(self):
        """``import unitytk`` loads no implementation module and no third-party
        package, of unitytk or of the packages below it."""
        from pythontk.core_utils.module_resolver import import_footprint

        self.assertEqual(
            import_footprint("unitytk"), {"modules": [], "third_party": []}
        )

    def test_the_unity_package_and_the_python_package_carry_one_version(self):
        """One release publishes both (PyPI and the Package Manager), and no
        check held them equal: ``package.json`` could ship a version behind
        the wheel's. Added: 2026-11-08"""
        import json

        import unitytk

        root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        with open(os.path.join(root, "com.m3trik.unitytk", "package.json"), encoding="utf-8-sig") as fh:
            self.assertEqual(json.load(fh)["version"], unitytk.__version__)

    def test_every_public_name_resolves_from_the_root(self):
        """Lazy, not missing: each name in ``__all__`` loads its module on first
        use, and is the class that module defines."""
        import unitytk

        for name in unitytk.__all__:
            value = getattr(unitytk, name)
            self.assertTrue(
                value.__module__.startswith("unitytk."), f"{name}: {value!r}"
            )


if __name__ == "__main__":
    unittest.main()
