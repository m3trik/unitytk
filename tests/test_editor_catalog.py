# !/usr/bin/python
# coding=utf-8
"""The Unity editor kernel's operation catalog (``editor_operations.json``).

The kernel (the package's ``Kernel/``, C#) declares its operations with
``[Operation]`` and an editor writes their catalog
(``M3trik.Unitytk.Kernel.Catalog.WriteFromCommandLine``). These tests hold
what needs no editor: the committed file is written exactly as Python writes
JSON, every declared id is in it, it keeps pythontk's catalog rules, and the
kernel's engine-free half passes its own .NET tests where the SDK is
installed. That its operations agree with the other hosts' twins is the
workspace's to hold (``m3trik/tests/test_sync_workspace.py``): unitytk names
no other host.
"""
import json
import os
import re
import shutil
import subprocess
import sys
import unittest

import pythontk as ptk

try:
    from .base_test import EnvSkip
except ImportError:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from base_test import EnvSkip

PACKAGE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "unitytk")
CATALOG = os.path.join(PACKAGE, "editor_operations.json")
KERNEL = os.path.join(os.path.dirname(PACKAGE), "com.m3trik.unitytk", "Kernel")
CORE_TESTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "core_dotnet")


def declared_ids():
    """Every ``[Operation("unity.…")]`` id in the kernel's C#."""
    ids = set()
    for root, _dirs, files in os.walk(KERNEL):
        for name in files:
            if name.endswith(".cs"):
                with open(os.path.join(root, name), encoding="utf-8") as fh:
                    ids.update(re.findall(r'\[Operation\(\s*"(unity\.[a-z0-9_.]+)"', fh.read()))
    return ids


def needs_dotnet(which=shutil.which):
    """Skip unless the .NET SDK is installed: an environment skip
    (:class:`base_test.EnvSkip`), which ``--strict-env`` fails on."""
    return EnvSkip.unless(which("dotnet"), "the .NET SDK is not installed")


class EditorCatalogTest(unittest.TestCase):
    def setUp(self):
        with open(CATALOG, encoding="utf-8", newline="") as fh:
            self.text = fh.read()
        self.document = json.loads(self.text)

    def test_written_as_python_writes_json(self):
        """The C# writer's indent and escapes are Python's, so the file diffs like any catalog."""
        self.assertEqual(self.text, json.dumps(self.document, indent=2) + "\n")

    def test_every_declared_operation_is_listed(self):
        """A declaration added or renamed without writing the catalog shows here."""
        listed = {entry["id"] for entry in self.document["operations"]}
        self.assertEqual(listed, declared_ids())
        self.assertEqual(self.document["provider"], "com.m3trik.unitytk")

    def test_the_slice_zero_effects(self):
        by_id = {entry["id"]: entry for entry in self.document["operations"]}
        self.assertEqual(by_id["unity.csharp.exec"]["effect"], "code")
        self.assertFalse(by_id["unity.csharp.exec"]["dry_run"], "code has no plan")
        self.assertEqual(by_id["unity.scene.view"]["effect"], "read")
        self.assertEqual(by_id["unity.project.doctor"]["effect"], "read")

    def test_keeps_the_catalog_rules(self):
        self.assertEqual(ptk.OperationProvider.check(modules=[CATALOG.replace(os.sep, "/")]), [])

    @needs_dotnet()
    def test_the_engine_free_core_passes_its_dotnet_tests(self):
        done = subprocess.run(
            ["dotnet", "test", CORE_TESTS, "--nologo"],
            capture_output=True,
            text=True,
            timeout=600,
        )
        self.assertEqual(done.returncode, 0, done.stdout[-4000:] + done.stderr[-2000:])


class DotnetGateTest(unittest.TestCase):
    def test_a_missing_sdk_is_an_environment_skip(self):
        """A plain ``skipUnless`` hid a missing SDK from ``--strict-env``, so a
        release run passed with the core's .NET tests never run. Added: 2026-11-08"""

        class Case(unittest.TestCase):
            @needs_dotnet(lambda _name: None)
            def test_core(self):
                pass

        result = unittest.TestResult()
        Case("test_core").run(result)
        self.assertEqual(EnvSkip.count(result), 1, result.skipped)


if __name__ == "__main__":
    unittest.main()
