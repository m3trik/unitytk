# !/usr/bin/python
# coding=utf-8
"""The indicator glow's two twins hold one rule.

An indicator light is drawn a glow card by every runtime that reads a
delivery's indicators: the WebXR viewer (pythontk's
``net_utils/preview/kernel/indicators.js``) and the Unity package
(``RenderEffects/Runtime/IndicatorGlow.cs``), whose summary says it draws the
same. Nothing held them to it: the C# restated the viewer's least on-screen
size, halo and lit threshold by hand. These read both files and compare: the
three constants, the card added over the scene as the viewer's additive
blending draws it, and the light's size measured the same way (its mesh's
bounding sphere times its largest world scale).
"""
import importlib.util
import os
import re
import unittest

_PACKAGE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "com.m3trik.unitytk")
CSHARP = os.path.join(_PACKAGE, "Features", "RenderEffects", "Runtime", "IndicatorGlow.cs")


def _viewer() -> str:
    """The viewer's twin, where pythontk (unitytk's automation's one dependency) holds it."""
    spec = importlib.util.find_spec("pythontk")
    if spec is None or not spec.submodule_search_locations:
        raise AssertionError("pythontk is not importable: install unitytk's dependencies")
    return os.path.join(list(spec.submodule_search_locations)[0], "net_utils", "preview", "kernel", "indicators.js")


def _read(path: str) -> str:
    with open(path, encoding="utf-8") as handle:
        return handle.read()


def _js_constant(text: str, name: str) -> float:
    match = re.search(rf"\bconst\s+{name}\s*=\s*([0-9.]+)\s*;", text)
    if match is None:
        raise AssertionError(f"indicators.js declares no {name}: the viewer's rule moved; read it again")
    return float(match.group(1))


def _cs_constant(text: str, name: str) -> float:
    match = re.search(rf"\bconst\s+float\s+{name}\s*=\s*([0-9.]+)f?\s*;", text)
    if match is None:
        raise AssertionError(f"IndicatorGlow.cs declares no {name}")
    return float(match.group(1))


class TestIndicatorGlowTwin(unittest.TestCase):
    def setUp(self):
        self.js = _read(_viewer())
        self.cs = _read(CSHARP)

    def test_its_constants_are_the_viewers(self):
        """The least on-screen size, the halo and the lit threshold: one value
        each, on both sides. Added: 2026-11-26"""
        for js, cs in (("MIN_PIXELS", "MinPixels"), ("HALO", "Halo"), ("LIT", "LitAbove")):
            with self.subTest(constant=js):
                self.assertEqual(_cs_constant(self.cs, cs), _js_constant(self.js, js))

    def test_the_card_is_added_over_the_scene_as_the_viewers(self):
        """The viewer adds its card (``THREE.AdditiveBlending``); the package asks
        its pipeline's additive switch. Added: 2026-11-26"""
        self.assertIn("THREE.AdditiveBlending", self.js)
        self.assertRegex(self.cs, r"\.SetAdditive\(")

    def test_a_lights_size_is_measured_as_the_viewer_measures_it(self):
        """The viewer's radius is its mesh's bounding sphere times its largest
        world scale; the package's must be too, never its box's half-diagonal.
        Added: 2026-11-26"""
        self.assertIn("boundingSphere.radius", self.js)
        self.assertRegex(self.js, r"radius\s*\*\s*Math\.max\(scale\.x,\s*scale\.y,\s*scale\.z\)")
        self.assertRegex(self.cs, r"local\.radius\s*\*\s*Mathf\.Max\(")


if __name__ == "__main__":
    unittest.main()
