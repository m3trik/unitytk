# !/usr/bin/python
# coding=utf-8
"""Integration test: the ShadowPlane component inside a real Unity (no DCC).

The IMPORT half against a real export is the workspace's end-to-end
suite's; this suite covers what the package does at RUNTIME, with a batch
Unity and no DCC:

    1. Create a Unity project, install the real package (component + shader)
       and copy one generated fixture into Assets: a baked height-field map
       (``Fixture_horizon.png``: two grounded poles and a floating slab,
       baked by pythontk's own ``ShadowHorizon.bake``).
    2. An editor script builds a unit-quad plane on ``m3trik/ShadowPlane`` with
       a ``ShadowPlane`` component holding its source Light and its contact by
       reference, and drives ``ApplyNow()``:
         a. placement and fade at four point-source cases, a directional
            Light and the scene's sun -- read back as position / yaw / scale
            / fade;
         b. the ``_horizon`` PNG's pinned import flags and the block wiring;
         c. pixels: a top-down orthographic render of the horizon (a sizeless
            and a finite source, at a fade of 1; again faded; again through
            the cache), read back through a linear RenderTexture -- the
            finite one again under a moved, turned and scaled model (the
            space's frame: placement, ground and the map's lengths all follow
            the instance);
         d. a layout the WebXR viewer would refuse, or no map, draws nothing;
         e. ``ShadowPlaneModel`` (placement and fade) through every golden
            case of ``ptk.Conformance.cases("shadow_projection")``.
    3. Python asserts the placements against ``pythontk.ShadowProjection.model
       (...).placement(...)`` within 1e-3, the fades against
       ``ShadowProjection.fade``, and the horizon pixels against
       ``HeightFieldMap.alpha`` sampling the very same PNG.

The editor runs ``-batchmode`` WITHOUT ``-nographics``: the pixel half needs a
graphics device (``Camera.Render`` + ``ReadPixels``). A machine that cannot
initialise one reports ``readback_ok = false`` and the pixel tests env-skip,
while the placement, fade, flag and wiring tests still run.

Requires:
    - Unity Hub with at least one editor installed (skips on license wall)
"""

import os
import json
import math
import unittest
import logging
import importlib.util
from functools import lru_cache
from unittest import mock

import numpy as np

logger = logging.getLogger(__name__)

import base_test  # noqa: F401 — sys.path bootstrap for the sibling repos
from base_test import EnvSkip, TempProjects, UnityEnv

import pythontk as ptk

from unitytk.unity_host import UnityHost


TWO_PI = 2.0 * math.pi
HALF_PI = 0.5 * math.pi

#: The plane the verifier fills the component with (metres: the scene IS the
#: delivery's, unit 1). The canvas is lopsided -- its near edge, far edge and
#: sides each off the default (``ShadowModel.CANVAS``) -- so a reader of any
#: one term wrong is off; the component takes it in its own terms (an XZ quad
#: made in Unity). The source ramp is not one unit, so a reader ramping over
#: the unit fades the "below_top" case wrong (0.6 for 0.8).
RECORD = dict(
    ground=0.0,
    radius=1.0,
    height=1.2,
    max_stretch=6.0,
    canvas=(-0.8, 1.2, -0.6, 0.45),
    source_ramp=0.75,
)
GROUND_OFFSET = 0.01  # ShadowPlaneModel.GroundOffset, times the plane's unit (1)
CONTACT = (0.0, 0.0, 0.0)

#: Source cases the verifier drives (name -> world position). "overhead" sits
#: within ShadowProjection.EPS of the contact's vertical; "below_top" has the
#: source under the target's top (k_top's 1e-4 floor + reach cap); "below"
#: puts it under the contact itself (both factors clamp to zero).
SOURCE_CASES = {
    "generic": (3.0, 4.0, -2.0),
    "overhead": (1e-8, 5.0, 0.0),
    "below_top": (2.0, 0.6, 1.5),
    "below": (2.0, -0.5, 1.0),
}
#: Asymmetric Euler rotation for the directional Light (degrees).
DIRECTIONAL_EULER = (50.0, -35.0, 20.0)

#: Where the model-frame cases put the model root: lifted (a prefab placed a
#: metre up must keep its shadow on ITS ground, not the world's), turned and
#: uniformly scaled -- every part of an instance's placement at once.
MOVED_MODEL = dict(position=(0.5, 1.0, -2.0), yaw=30.0, scale=1.5)


@lru_cache(maxsize=1)
def _conformance() -> dict:
    """pythontk's golden cases for every port of the shadow model, generated
    from the reference here (never a checked-in copy, so they cannot drift
    from it): the verifier runs each through ``ShadowPlaneModel`` -- the
    model, the placement and the fade from the reference's own source point,
    a sun's through ``EvaluateDirectional`` and ``FadeDirectional`` as well.
    Built on first use, by the Unity half: ``ptk.Conformance`` is new in
    pythontk 0.13.0, and an older one must still load the module."""
    return ptk.Conformance.cases("shadow_projection", seed=3, per_kind=8)


#: The horizon render: source position and the finite source's diameter.
HORIZON_SOURCE = (2.5, 2.0, -1.5)
HORIZON_SOURCE_SIZE = 0.3
#: The faded render's falloff power: its fade is the model's, under 1.
FADED_FALLOFF = 1.2

#: Top-down render: orthographic half-size (world) and pixels per side.
RENDER_HALF = 2.0
RENDER_PX = 96


# ======================================================================
# Fixtures: the baked horizon map
# ======================================================================


class HorizonFixture:
    """Two poles and a floating slab, baked by pythontk's own bake into the
    height-field map the contract ships, and the oracle that samples it.

    The first pole stands at local ``(0.5, 0.3)``, off-centre on purpose so a
    flipped bearing axis or a swapped frame vector moves its shadow and fails
    the pixel comparison; the second stands beside the first's light ray. The
    slab hovers between two heights over open ground, so a ray under it must
    pass and one through it must block -- the span a plain height field
    cannot hold.

    There was an analytic map here, and before it a numpy transcription of
    the HLSL; both pinned the shader to something other than the one oracle
    every consumer answers to. The map is now the real bake, and ``alpha`` is
    ``HeightFieldMap.alpha`` decoding the very bytes the PNG holds (the map's
    PNG round trip is exact).
    """

    SIZE = 64
    SPANS = 2
    #: One fixed footprint for every variant (a pole alone, no slab), so the
    #: texture layout and the frame never change between them.
    BOUNDS = (-0.5, 2.5, -0.5, 2.0)
    #: Grounded poles: (base (x, z), half-width, height) -- square posts.
    POLES = (((0.5, 0.3), 0.06, 1.2), ((0.214, 1.028), 0.05, 0.8))
    POLE = POLES[0][0]
    #: The floating slab: centre (x, z), half-width, underside and top heights
    #: -- placed so its shadow clears the first pole's shadow line.
    DISC = (1.5, 0.9)
    DISC_RADIUS = 0.5
    DISC_LO = 0.7
    DISC_HI = 0.8
    #: The bearing frame: a = local +X, b = local +Z (identity contact).
    FRAME_A = (1.0, 0.0, 0.0)
    FRAME_B = (0.0, 0.0, 1.0)

    @staticmethod
    def _box(x0, x1, y0, y1, z0, z1):
        pts = np.array(
            [[x, y, z] for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)],
            dtype=float,
        )
        quads = [
            (0, 1, 3, 2),
            (4, 6, 7, 5),
            (0, 4, 5, 1),
            (2, 3, 7, 6),
            (0, 2, 6, 4),
            (1, 5, 7, 3),
        ]
        tris = []
        for a, b, c, d in quads:
            tris += [(a, b, c), (a, c, d)]
        return pts, np.array(tris, dtype=np.int64)

    @classmethod
    def meshes(cls, poles=None, disc=True):
        poles = cls.POLES if poles is None else poles
        parts = [
            cls._box(bx - r, bx + r, 0.0, h, bz - r, bz + r) for (bx, bz), r, h in poles
        ]
        if disc:
            cx, cz = cls.DISC
            r = cls.DISC_RADIUS
            parts.append(
                cls._box(cx - r, cx + r, cls.DISC_LO, cls.DISC_HI, cz - r, cz + r)
            )
        return parts

    @classmethod
    def map(cls, poles=None, disc=True) -> "ptk.HeightFieldMap":
        """The baked map -- the reference itself."""
        return ptk.ShadowHorizon.bake(
            cls.meshes(poles, disc),
            up=1,
            size=cls.SIZE,
            spans=cls.SPANS,
            bounds=cls.BOUNDS,
        )

    @classmethod
    def bake(cls, poles=None, disc=True) -> np.ndarray:
        """The map as the ``(S, S x (K + 2), 4)`` uint8 image the contract
        ships, row 0 at the top (PNG order)."""
        return cls.map(poles, disc).to_rgba()

    @classmethod
    def alpha(cls, points, source, source_size=0.0, *, poles=None, disc=True):
        """``HeightFieldMap.alpha`` at contact-local ground ``points`` (N, 3)
        for a positional ``source`` (contact-local) of diameter
        ``source_size``, decoding the image ``bake`` writes for the same
        variant."""
        hmap = cls.map(poles, disc)
        back = ptk.HeightFieldMap.from_rgba(
            hmap.to_rgba(),
            size=cls.SIZE,
            spans=cls.SPANS,
            bounds=cls.BOUNDS,
            ground=0.0,
            up=1,
            height_scale=hmap.height_scale,
        )
        return back.alpha(points, light=source, source_size=source_size)


def _write_png(path: str, img: np.ndarray) -> None:
    from PIL import Image

    Image.fromarray(np.ascontiguousarray(img), "RGBA").save(path)


# ======================================================================
# Unity-side verifier
# ======================================================================

UNITY_VERIFIER_CS = r"""
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.ShadowPlane;
using ShadowPlaneComponent = M3trik.Unitytk.ShadowPlane.ShadowPlane;

public class ShadowPlaneRuntimeVerifier
{
    /// <summary>Invariant-culture float, so a European locale can't emit
    /// "0,5" and hand the Python side unparseable JSON.</summary>
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V3(Vector3 v) => "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + "]";
    static string V4(Vector4 v) => "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ", " + F(v.w) + "]";
    static string B(bool b) => b ? "true" : "false";
    static string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    const int RENDER_PX = __RENDER_PX__;
    const float RENDER_HALF = __RENDER_HALF__f;

    static Mesh UnitQuad()
    {
        // A unit quad in XZ: v = 1 on the local -Z edge.
        var m = new Mesh();
        m.vertices = new[] {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
        m.uv = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f),
                       new Vector2(0f, 0f), new Vector2(1f, 0f) };
        m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static string Placement(ShadowPlaneComponent c)
    {
        var t = c.transform;
        Light light = c.SourceLight;
        return "{\"position\": " + V3(t.position) + ", \"yaw\": " + F(t.eulerAngles.y)
            + ", \"scale\": " + V3(t.localScale)
            + ", \"resolved\": " + B(light != null && c.contact != null)
            + ", \"fade\": " + F(c.Fade)
            + ", \"shine\": " + V3(c.ShineDirection()) + "}";
    }

    // The plane in its space's frame: its position, its yaw and its extents
    // along its own X and Z in that frame's units -- what Placement reports in
    // world terms, for a space that has been moved.
    static string InModel(ShadowPlaneComponent c, Transform root)
    {
        Matrix4x4 m = root.worldToLocalMatrix * c.transform.localToWorldMatrix;
        Vector3 x = m.GetColumn(0), z = m.GetColumn(2);
        float yaw = Mathf.Atan2(z.x, z.z) * Mathf.Rad2Deg;
        return "{\"position\": " + V3(m.GetColumn(3)) + ", \"yaw\": " + F(yaw)
            + ", \"scale\": " + V3(new Vector3(x.magnitude, 1f, z.magnitude))
            + ", \"resolved\": " + B(c.SourceLight != null && c.contact != null)
            + ", \"fade\": " + F(c.Fade) + "}";
    }

    // Top-down orthographic render of [-RENDER_HALF, RENDER_HALF]^2 into a
    // LINEAR RGBA32 target (no sRGB conversion on the readback), cleared to
    // white: the plane's black RGB blends to (1 - alpha). Returns the red
    // channel row-major from the BOTTOM (ReadPixels' origin), plus the max
    // |r - g| as a sanity check (an error shader is magenta).
    static string Render(Camera cam, out int maxRG, out int cornerR)
    {
        var rt = new RenderTexture(RENDER_PX, RENDER_PX, 24,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        rt.antiAliasing = 1;
        rt.filterMode = FilterMode.Point;
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(RENDER_PX, RENDER_PX, TextureFormat.RGBA32, false, true);
        tex.ReadPixels(new Rect(0, 0, RENDER_PX, RENDER_PX), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        var px = tex.GetPixels32();
        var sb = new StringBuilder("[");
        maxRG = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append(px[i].r);
            maxRG = Mathf.Max(maxRG, Mathf.Abs(px[i].r - px[i].g));
        }
        sb.Append("]");
        cornerR = px[0].r;
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
        return sb.ToString();
    }

    // ---- conformance: pythontk's golden cases through ShadowPlaneModel -----
    static string G(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
    static double[] Arr(object o)
    {
        var list = (IList<object>)o;
        var items = new double[list.Count];
        for (int i = 0; i < list.Count; i++) items[i] = (double)list[i];
        return items;
    }
    static string Terms(ShadowPlaneModel.Result m) => "[" + G(m.anchorX) + ", " + G(m.anchorZ)
        + ", " + G(m.bearingX) + ", " + G(m.bearingZ) + ", " + G(m.kBase) + ", " + G(m.kTop)
        + ", " + G(m.reach) + ", " + G(m.baseRadius) + ", " + G(m.topRadius) + ", " + G(m.width)
        + ", " + (m.overhead ? "1" : "0") + "]";

    static string Conformance(string json)
    {
        var doc = (IDictionary<string, object>)M3trik.Unitytk.Core.Json.Parse(json, false);
        var rows = new List<string>();
        foreach (var raw in (IList<object>)doc["cases"])
        {
            var c = (IDictionary<string, object>)raw;
            var given = (IDictionary<string, object>)c["input"];
            double[] contact = Arr(given["contact"]), source = Arr(c["source"]), canvas = Arr(given["canvas"]);
            double ground = (double)given["ground"], radius = (double)given["radius"];
            double height = (double)given["height"], maxStretch = (double)given["max_stretch"];
            double intensity = (double)given["intensity"], power = (double)given["falloff_power"];
            double fadeHeight = (double)given["fade_height"], sourceRamp = (double)given["source_ramp"];
            var m = ShadowPlaneModel.Evaluate(contact[0], contact[1], contact[2],
                source[0], source[1], source[2], ground, radius, height, maxStretch);
            m.Placement(canvas[0], canvas[1], canvas[2], canvas[3],
                out double cx, out double cz, out double along, out double across);
            string sun = "null";
            double fade;
            if (given["direction"] is IList<object>)
            {
                double[] d = Arr(given["direction"]);
                sun = Terms(ShadowPlaneModel.EvaluateDirectional(contact[0], contact[1], contact[2],
                    d[0], d[1], d[2], ground, radius, height, maxStretch));
                fade = ShadowPlaneModel.FadeDirectional(contact[0], contact[1], contact[2], d[0], d[1], d[2],
                    ground, radius, height, maxStretch, intensity, power, fadeHeight, sourceRamp);
            }
            else
            {
                fade = ShadowPlaneModel.Fade(contact[0], contact[1], contact[2], source[0], source[1], source[2],
                    ground, radius, height, maxStretch, intensity, power, fadeHeight, sourceRamp);
            }
            rows.Add("{\"model\": " + Terms(m) + ", \"placement\": [" + G(cx) + ", " + G(cz) + ", "
                + G(along) + ", " + G(across) + "], \"directional\": " + sun + ", \"fade\": " + G(fade) + "}");
        }
        return "[" + string.Join(", ", rows) + "]";
    }

    // A disabled Light: only its transform and type matter; it lights nothing.
    static Light Lamp(Transform parent, string name, LightType type)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var light = go.AddComponent<Light>();
        light.type = type;
        light.enabled = false;
        return light;
    }

    public static void Verify()
    {
        string resultsFile = @"__RESULTS_PATH__";
        var sb = new StringBuilder();
        sb.AppendLine("{");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        RenderSettings.sun = null;

        // ---- the package shader and the fixture map ---------------------
        var shader = Shader.Find("m3trik/ShadowPlane");
        sb.AppendLine("  \"shader_found\": " + B(shader != null) + ",");
        sb.AppendLine("  \"shader_has_error\": "
            + B(shader != null && ShaderUtil.ShaderHasError(shader)) + ",");
        var horizon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Fixture_horizon.png");
        sb.AppendLine("  \"horizon_loaded\": " + B(horizon != null) + ",");

        // ---- the _horizon PNG's pinned import settings ---------------------
        var ti = AssetImporter.GetAtPath("Assets/Fixture_horizon.png") as TextureImporter;
        if (ti != null)
        {
            var standalone = ti.GetPlatformTextureSettings("Standalone");
            var android = ti.GetPlatformTextureSettings("Android");
            sb.AppendLine("  \"horizon_import\": {"
                + "\"srgb\": " + B(ti.sRGBTexture)
                + ", \"alpha_is_transparency\": " + B(ti.alphaIsTransparency)
                + ", \"mipmaps\": " + B(ti.mipmapEnabled)
                + ", \"wrap\": " + S(ti.wrapMode.ToString())
                + ", \"filter\": " + S(ti.filterMode.ToString())
                + ", \"npot\": " + S(ti.npotScale.ToString())
                + ", \"compression\": " + S(ti.textureCompression.ToString())
                + ", \"max_size\": " + ti.maxTextureSize
                + ", \"standalone_overridden\": " + B(standalone.overridden)
                + ", \"standalone_format\": " + S(standalone.format.ToString())
                + ", \"android_overridden\": " + B(android.overridden)
                + ", \"android_format\": " + S(android.format.ToString())
                + "},");
        }
        if (horizon != null)
        {
            sb.AppendLine("  \"horizon_texture\": {"
                + "\"width\": " + horizon.width + ", \"height\": " + horizon.height
                + ", \"mip_count\": " + horizon.mipmapCount
                + ", \"format\": " + S(horizon.format.ToString())
                + "},");
        }

        // ---- the scene: model root > plane, source, sun, contact ---------
        var model = new GameObject("Fixture_model");
        var contact = new GameObject("Fixture_contact").transform;
        contact.SetParent(model.transform, false);
        contact.position = new Vector3(__CX__f, __CY__f, __CZ__f);
        Light lamp = Lamp(model.transform, "Fixture_source", LightType.Point);
        var source = lamp.transform;
        Light sun = Lamp(model.transform, "Fixture_sun", LightType.Directional);

        var group = new GameObject("Fixture_shadow_grp");
        group.transform.SetParent(model.transform, false);
        var planeGo = new GameObject("Fixture_shadow");
        planeGo.transform.SetParent(group.transform, false);
        var filter = planeGo.AddComponent<MeshFilter>();
        filter.sharedMesh = UnitQuad();
        var rend = planeGo.AddComponent<MeshRenderer>();
        var mat = new Material(shader != null ? shader : Shader.Find("Unlit/Transparent"));
        mat.name = "Fixture_shadow_mat";
        mat.enableInstancing = true;
        if (horizon != null) mat.SetTexture("_HorizonMap", horizon);
        rend.sharedMaterial = mat;
        sb.AppendLine("  \"mat_instancing\": " + B(mat.enableInstancing) + ",");

        var plane = planeGo.AddComponent<ShadowPlaneComponent>();
        plane.source = lamp;
        plane.contact = contact;
        plane.followSource = true;
        plane.ground = __GROUND__f;
        plane.radius = __RADIUS__f;
        plane.height = __HEIGHT__f;
        plane.maxStretch = __MAX_STRETCH__f;
        plane.unit = 1f;
        plane.canvas = new Vector4(__CV0__f, __CV1__f, __CV2__f, __CV3__f);
        plane.sourceRamp = __SOURCE_RAMP__f;
        // The fade at 1 wherever the pixels are compared with the oracle: no
        // falloff, the source above its ramp, a target that never rises out.
        plane.intensity = 1f;
        plane.falloffPower = 0f;
        plane.fadeHeight = 1000f;
        plane.horizonMap = horizon;
        plane.horizonSize = __SIZE__;
        plane.horizonSpans = __SPANS__;
        plane.horizonBounds = new Vector4(__BND0__f, __BND1__f, __BND2__f, __BND3__f);
        plane.horizonHeightScale = __HSCALE__f;
        plane.horizonFrameA = new Vector3(__A0__f, __A1__f, __A2__f);
        plane.horizonFrameB = new Vector3(__B0__f, __B1__f, __B2__f);
        plane.horizonRect = new Vector4(1f, 1f, 0f, 0f);
        plane.cacheShadow = false;
        plane.BindSpace(model.transform);

        // ---- placement at each source case -------------------------------
        sb.AppendLine("  \"placement\": {");
        var cases = new List<string>();
__CASES__
        // Directional: a Light shines along its +Z.
        plane.source = sun;
        sun.transform.rotation = Quaternion.Euler(__E0__f, __E1__f, __E2__f);
        plane.ApplyNow();
        cases.Add("    \"directional_light\": " + Placement(plane));
        // No source: the scene's sun.
        plane.source = null;
        RenderSettings.sun = sun;
        plane.ApplyNow();
        cases.Add("    \"scene_sun\": " + Placement(plane));
        RenderSettings.sun = null;
        sb.AppendLine(string.Join(",\n", cases));
        sb.AppendLine("  },");
        plane.source = lamp;

        // ---- the space's frame: moving, turning and scaling the instance
        // carries the shadow, its ground included.
        source.localPosition = new Vector3(__GX__f, __GY__f, __GZ__f);
        model.transform.SetPositionAndRotation(
            new Vector3(__MX__f, __MY__f, __MZ__f), Quaternion.Euler(0f, __MYAW__f, 0f));
        model.transform.localScale = Vector3.one * __MSCALE__f;
        plane.ApplyNow();
        sb.AppendLine("  \"moved_model\": " + InModel(plane, model.transform) + ",");
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = Vector3.one;

        // ---- the camera for the pixel half -------------------------------
        var camGo = new GameObject("Fixture_camera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = RENDER_HALF;
        cam.aspect = 1f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.white;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.transform.position = new Vector3(0f, 10f, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);  // down; +Z up the image, +X right

        int maxRG, cornerR;
        var mpb = new MaterialPropertyBlock();

        // (h1, h2) a sizeless source, then a finite one -- the shader's own
        // per-pixel answer (the cache is h4).
        plane.sourceSize = 0f;
        source.position = new Vector3(__HX__f, __HY__f, __HZ__f);
        plane.ApplyNow();
        rend.GetPropertyBlock(mpb);
        sb.AppendLine("  \"block_fade\": " + F(mpb.GetFloat("_Fade")) + ",");
        sb.AppendLine("  \"block_source\": " + V4(mpb.GetVector("_Source")) + ",");
        sb.AppendLine("  \"block_origin\": " + V4(mpb.GetVector("_Origin")) + ",");
        sb.AppendLine("  \"block_axis_a\": " + V4(mpb.GetVector("_AxisA")) + ",");
        sb.AppendLine("  \"block_axis_b\": " + V4(mpb.GetVector("_AxisB")) + ",");
        sb.AppendLine("  \"block_axis_up\": " + V4(mpb.GetVector("_AxisUp")) + ",");
        sb.AppendLine("  \"block_horizon_params\": " + V4(mpb.GetVector("_HorizonParams")) + ",");
        sb.AppendLine("  \"block_horizon_bounds\": " + V4(mpb.GetVector("_HorizonBounds")) + ",");
        sb.AppendLine("  \"block_horizon_meta\": " + V4(mpb.GetVector("_HorizonMeta")) + ",");
        sb.AppendLine("  \"block_horizon_map_overridden\": "
            + B(mpb.GetTexture("_HorizonMap") != null) + ",");
        string h1 = Render(cam, out maxRG, out cornerR);
        sb.AppendLine("  \"h1_placement\": " + Placement(plane) + ",");
        sb.AppendLine("  \"h1_max_rg\": " + maxRG + ",");
        sb.AppendLine("  \"h1_r\": " + h1 + ",");
        plane.sourceSize = __SOURCE_SIZE__f;
        plane.ApplyNow();
        string h2 = Render(cam, out maxRG, out cornerR);
        sb.AppendLine("  \"h2_placement\": " + Placement(plane) + ",");
        sb.AppendLine("  \"h2_r\": " + h2 + ",");

        // (h5) the same, faded: the falloff on, so the fade the component
        // computes multiplies the march.
        plane.falloffPower = __FALLOFF__f;
        plane.ApplyNow();
        string h5 = Render(cam, out maxRG, out cornerR);
        sb.AppendLine("  \"h5_placement\": " + Placement(plane) + ",");
        sb.AppendLine("  \"h5_r\": " + h5 + ",");
        plane.falloffPower = 0f;

        // (h3) the finite-source horizon under a moved, turned and scaled
        // model, rendered by a camera the model carries: in the model's frame
        // nothing changed, so neither may the pixels.
        model.transform.SetPositionAndRotation(
            new Vector3(__MX__f, __MY__f, __MZ__f), Quaternion.Euler(0f, __MYAW__f, 0f));
        model.transform.localScale = Vector3.one * __MSCALE__f;
        plane.ApplyNow();
        cam.orthographicSize = RENDER_HALF * __MSCALE__f;
        cam.transform.SetPositionAndRotation(
            model.transform.position + Vector3.up * 10f,
            model.transform.rotation * Quaternion.Euler(90f, 0f, 0f));
        string h3 = Render(cam, out maxRG, out cornerR);
        rend.GetPropertyBlock(mpb);
        sb.AppendLine("  \"h3_block_horizon_meta\": " + V4(mpb.GetVector("_HorizonMeta")) + ",");
        sb.AppendLine("  \"h3_block_horizon_bounds\": " + V4(mpb.GetVector("_HorizonBounds")) + ",");
        sb.AppendLine("  \"h3_block_source_radius\": " + V4(mpb.GetVector("_SourceRadius")) + ",");
        sb.AppendLine("  \"h3_r\": " + h3 + ",");
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = Vector3.one;
        cam.orthographicSize = RENDER_HALF;
        cam.transform.SetPositionAndRotation(
            new Vector3(0f, 10f, 0f), Quaternion.Euler(90f, 0f, 0f));

        // (h4) the finite-source horizon through the cache: marched once into
        // its rect of the atlas and sampled; a still source re-marches
        // nothing, a moved one once.
        plane.cacheShadow = true;
        plane.ApplyNow();
        int bakes0 = ShadowPlaneCache.Bakes;
        sb.AppendLine("  \"h4_global_is_atlas\": "
            + B(Shader.GetGlobalTexture("_ShadowCache") == ShadowPlaneCache.Atlas) + ",");
        string h4 = Render(cam, out maxRG, out cornerR);
        rend.GetPropertyBlock(mpb);
        sb.AppendLine("  \"h4_cached\": " + F(mpb.GetFloat("_Cached")) + ",");
        sb.AppendLine("  \"h4_cache_rect\": " + V4(mpb.GetVector("_CacheRect")) + ",");
        sb.AppendLine("  \"h4_atlas\": " + (ShadowPlaneCache.Atlas != null ? ShadowPlaneCache.Atlas.width : 0) + ",");
        sb.AppendLine("  \"h4_placement\": " + Placement(plane) + ",");
        sb.AppendLine("  \"h4_r\": " + h4 + ",");
        plane.ApplyNow();
        int bakes1 = ShadowPlaneCache.Bakes;
        source.position += new Vector3(0.3f, 0f, 0f);
        plane.ApplyNow();
        int bakes2 = ShadowPlaneCache.Bakes;
        source.position -= new Vector3(0.3f, 0f, 0f);
        plane.cacheShadow = false;
        plane.ApplyNow();
        sb.AppendLine("  \"cache_bakes\": [" + bakes0 + ", " + bakes1 + ", " + bakes2 + "],");

        // ---- a layout the shader cannot march -- the WebXR viewer's
        // horizonSpec refuses it, term for term -- or no map: the plane draws
        // nothing (_Fade 0). Each case breaks ONE term of the valid plane.
        var unmarched = new List<string>();
        System.Action<string, System.Action, System.Action> fadeOf = (label, breakIt, restore) =>
        {
            breakIt();
            plane.ApplyNow();
            rend.GetPropertyBlock(mpb);
            unmarched.Add(S(label) + ": " + F(mpb.GetFloat("_Fade")));
            restore();
        };
        fadeOf("valid", () => { }, () => { });
        fadeOf("no_map", () => plane.horizonMap = null, () => plane.horizonMap = horizon);
        fadeOf("size_under_8", () => plane.horizonSize = 4, () => plane.horizonSize = __SIZE__);
        fadeOf("no_spans", () => plane.horizonSpans = 0, () => plane.horizonSpans = __SPANS__);
        fadeOf("no_height_scale", () => plane.horizonHeightScale = 0f,
            () => plane.horizonHeightScale = __HSCALE__f);
        fadeOf("unordered_bounds",
            () => plane.horizonBounds = new Vector4(__BND1__f, __BND0__f, __BND2__f, __BND3__f),
            () => plane.horizonBounds = new Vector4(__BND0__f, __BND1__f, __BND2__f, __BND3__f));
        sb.AppendLine("  \"unmarched\": {" + string.Join(", ", unmarched) + "},");

        sb.AppendLine("  \"conformance\": " + Conformance(File.ReadAllText(@"__CONFORMANCE_PATH__")) + ",");

        // Readback sanity: the corner (outside every plane) must be the clear
        // colour, and a shadow shader is grey, never magenta.
        sb.AppendLine("  \"readback_ok\": " + B(cornerR == 255) + ",");
        sb.AppendLine("  \"render_px\": " + RENDER_PX + ",");
        sb.AppendLine("  \"render_half\": " + F(RENDER_HALF));
        sb.AppendLine("}");

        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(model);
        File.WriteAllText(resultsFile, sb.ToString());
        Debug.Log("SHADOW PLANE RUNTIME TEST COMPLETE. Results at: " + resultsFile);
        EditorApplication.Exit(0);
    }
}
"""


def _cases_cs() -> str:
    lines = []
    for name, (x, y, z) in SOURCE_CASES.items():
        lines.append(
            f"        source.position = new Vector3({x!r}f, {y!r}f, {z!r}f);\n"
            f"        plane.ApplyNow();\n"
            f'        cases.Add("    \\"{name}\\": " + Placement(plane));'
        )
    return "\n".join(lines)


def _verifier_source(results_path: str, conformance_path: str) -> str:
    hz = HorizonFixture
    subs = {
        "__RESULTS_PATH__": results_path.replace("\\", "\\\\"),
        "__CONFORMANCE_PATH__": conformance_path.replace("\\", "\\\\"),
        "__RENDER_PX__": str(RENDER_PX),
        "__RENDER_HALF__": repr(RENDER_HALF),
        "__CX__": repr(CONTACT[0]),
        "__CY__": repr(CONTACT[1]),
        "__CZ__": repr(CONTACT[2]),
        "__GROUND__": repr(RECORD["ground"]),
        "__RADIUS__": repr(RECORD["radius"]),
        "__HEIGHT__": repr(RECORD["height"]),
        "__MAX_STRETCH__": repr(RECORD["max_stretch"]),
        "__CV0__": repr(RECORD["canvas"][0]),
        "__CV1__": repr(RECORD["canvas"][1]),
        "__CV2__": repr(RECORD["canvas"][2]),
        "__CV3__": repr(RECORD["canvas"][3]),
        "__SOURCE_RAMP__": repr(RECORD["source_ramp"]),
        "__CASES__": _cases_cs(),
        "__E0__": repr(DIRECTIONAL_EULER[0]),
        "__E1__": repr(DIRECTIONAL_EULER[1]),
        "__E2__": repr(DIRECTIONAL_EULER[2]),
        "__GX__": repr(SOURCE_CASES["generic"][0]),
        "__GY__": repr(SOURCE_CASES["generic"][1]),
        "__GZ__": repr(SOURCE_CASES["generic"][2]),
        "__HX__": repr(HORIZON_SOURCE[0]),
        "__HY__": repr(HORIZON_SOURCE[1]),
        "__HZ__": repr(HORIZON_SOURCE[2]),
        "__SIZE__": str(hz.SIZE),
        "__SPANS__": str(hz.SPANS),
        "__BND0__": repr(hz.BOUNDS[0]),
        "__BND1__": repr(hz.BOUNDS[1]),
        "__BND2__": repr(hz.BOUNDS[2]),
        "__BND3__": repr(hz.BOUNDS[3]),
        "__HSCALE__": repr(float(hz.map().height_scale)),
        "__A0__": repr(hz.FRAME_A[0]),
        "__A1__": repr(hz.FRAME_A[1]),
        "__A2__": repr(hz.FRAME_A[2]),
        "__B0__": repr(hz.FRAME_B[0]),
        "__B1__": repr(hz.FRAME_B[1]),
        "__B2__": repr(hz.FRAME_B[2]),
        "__SOURCE_SIZE__": repr(HORIZON_SOURCE_SIZE),
        "__FALLOFF__": repr(FADED_FALLOFF),
        "__MX__": repr(MOVED_MODEL["position"][0]),
        "__MY__": repr(MOVED_MODEL["position"][1]),
        "__MZ__": repr(MOVED_MODEL["position"][2]),
        "__MYAW__": repr(MOVED_MODEL["yaw"]),
        "__MSCALE__": repr(MOVED_MODEL["scale"]),
    }
    src = UNITY_VERIFIER_CS
    for token, value in subs.items():
        src = src.replace(token, value)
    assert "__" not in src.replace("__RESULTS", ""), (
        "unsubstituted token in the verifier"
    )
    return src


# ======================================================================
# Expected values (pythontk) and pixel geometry
# ======================================================================


def _expected_placement(light=None, direction=None):
    """``(position, yaw_deg, scale)`` from pythontk for the RECORD at CONTACT."""
    from pythontk import ShadowProjection

    m = ShadowProjection.model(
        CONTACT,
        light=light,
        direction=direction,
        ground=RECORD["ground"],
        radius=RECORD["radius"],
        height=RECORD["height"],
        max_stretch=RECORD["max_stretch"],
    )
    centre, along, across = m.placement(RECORD["canvas"])
    yaw = math.degrees(math.atan2(m.bearing[0], m.bearing[1]))
    return (
        (centre[0], RECORD["ground"] + GROUND_OFFSET, centre[1]),
        yaw,
        (max(1e-4, across), 1.0, max(1e-4, along)),
    )


def _expected_fade(light=None, direction=None, falloff_power=0.0):
    """``ShadowProjection.fade`` for the verifier's plane at CONTACT."""
    from pythontk import ShadowProjection

    return ShadowProjection.fade(
        CONTACT,
        light=light,
        direction=direction,
        ground=RECORD["ground"],
        radius=RECORD["radius"],
        height=RECORD["height"],
        max_stretch=RECORD["max_stretch"],
        intensity=1.0,
        falloff_power=falloff_power,
        fade_height=1000.0,
        source_ramp=RECORD["source_ramp"],
    )


def _angle_diff(a, b):
    return abs((a - b + 180.0) % 360.0 - 180.0)


def _pixel_grid(unity):
    """World (x, z) of every pixel centre of a render, row-major from the
    bottom (the ReadPixels order the verifier wrote)."""
    n = int(unity["render_px"])
    half = float(unity["render_half"])
    coords = -half + (np.arange(n) + 0.5) * (2.0 * half / n)
    zz, xx = np.meshgrid(coords, coords, indexing="ij")  # row -> z, col -> x
    return xx.ravel(), zz.ravel()


def _inside_plane(placement, xs, zs):
    """Mask of the pixels a placed unit quad covers."""
    px, _py, pz = placement["position"]
    yaw = math.radians(placement["yaw"])
    sx, _sy, sz = placement["scale"]
    dx, dz = xs - px, zs - pz
    # world = pos + R(yaw) local; R maps local +Z -> (sin, cos), +X -> (cos, -sin)
    lx = dx * math.cos(yaw) - dz * math.sin(yaw)
    lz = dx * math.sin(yaw) + dz * math.cos(yaw)
    return (np.abs(lx) <= 0.5 * sx) & (np.abs(lz) <= 0.5 * sz), lz


def _alpha(unity, key):
    return 1.0 - np.asarray(unity[key], dtype=float) / 255.0


# ======================================================================
# Test class
# ======================================================================


class TestShadowPlaneRuntimeIntegration(unittest.TestCase):
    """ShadowPlane + ShadowPlane.shader, read back from a live Unity."""

    @classmethod
    def setUpClass(cls):
        cls.temp_dir = TempProjects.allocate("shadow_runtime_integ", cls)
        cls.unity_results = None
        cls.horizon_img = HorizonFixture.bake()
        logger.info(f"Temp dir: {cls.temp_dir}")

    def _results(self):
        unity = self.__class__.unity_results
        if not unity:
            self.skipTest("Unity results not available — test_01 must run first")
        return unity

    def _pixels_or_skip(self):
        unity = self._results()
        if not unity.get("readback_ok"):
            self.skipTest(
                EnvSkip.reason(
                    "no usable graphics device in the batch editor (readback_ok false) — "
                    "the pixel half needs -batchmode without -nographics on a GPU session"
                )
            )
        return unity

    # ---- Step 1: Unity run --------------------------------------------

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_01_unity_runtime(self):
        """Build the fixture and the scene in a real Unity; capture the readback."""
        project_path = TempProjects.project(self.temp_dir)
        UnityEnv.create_project(self, project_path)

        # The real package, shader included -- this checkout's, by one manifest line.
        UnityHost.install(project_path, "local")

        assets_path = os.path.join(project_path, "Assets")
        _write_png(os.path.join(assets_path, "Fixture_horizon.png"), self.horizon_img)
        # Keep a copy beside the results for post-mortems.
        _write_png(os.path.join(self.temp_dir, "Fixture_horizon.png"), self.horizon_img)

        editor_path = os.path.join(assets_path, "Editor")
        os.makedirs(editor_path, exist_ok=True)
        results_path = os.path.join(self.temp_dir, "unity_results.json")
        conformance_path = os.path.join(self.temp_dir, "conformance.json")
        with open(conformance_path, "w", encoding="utf-8") as f:
            json.dump(_conformance(), f)
        with open(
            os.path.join(editor_path, "ShadowPlaneRuntimeVerifier.cs"),
            "w",
            encoding="utf-8",
        ) as f:
            f.write(_verifier_source(results_path, conformance_path))

        log_path = os.path.join(self.temp_dir, "unity_log.txt")
        # -batchmode WITHOUT -nographics (UnityEnv.run adds none): the pixel
        # half renders through a camera, which needs a graphics device.
        returncode = UnityEnv.run(
            self, project_path, "ShadowPlaneRuntimeVerifier.Verify", log_path, timeout=480
        )

        if not os.path.exists(results_path):
            log_text = ""
            if os.path.exists(log_path):
                with open(log_path, encoding="utf-8", errors="ignore") as fh:
                    log_text = fh.read()
            if UnityEnv.is_licence_failure(returncode, log_text):
                logger.info(
                    "Unity batch license failure (rc=%s) — see %s",
                    returncode,
                    log_path,
                )
                self.skipTest(UnityEnv.license_reason())
            self.fail(
                f"unity_results.json not created (Unity rc={returncode}) — "
                f"check {log_path}"
            )
        with open(results_path) as f:
            self.__class__.unity_results = json.load(f)
        summary = {
            k: v
            for k, v in self.__class__.unity_results.items()
            if not k.endswith("_r")
        }
        logger.info(f"Unity results:\n{json.dumps(summary, indent=2)}")

    # ---- Step 2: the model, the placement and the fade against pythontk -

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_02a_the_model_matches_every_conformance_case(self):
        """``ShadowPlaneModel`` against the reference's golden cases
        (``ptk.Conformance.cases("shadow_projection")``) -- the same document
        the WebXR port is held to: every term of the model from the
        reference's source point, a sun's from its direction too, the
        placement of the case's canvas, and the fade."""
        rows = self._results()["conformance"]
        doc = _conformance()
        cases, tolerance = doc["cases"], doc["tolerance"]
        self.assertEqual(len(rows), len(cases))
        worst = {"model": 0.0, "placement": 0.0, "fade": 0.0}
        for case, row in zip(cases, rows):
            e = case["model"]
            want = [*e["anchor"], *e["bearing"], e["k_base"], e["k_top"], e["reach"]]
            want += [e["base"], e["top"], e["width"]]
            for got in [row["model"]] + (
                [row["directional"]] if row["directional"] else []
            ):
                self.assertEqual(bool(got[-1]), e["overhead"], case["name"])
                gap = max(abs(a - b) for a, b in zip(got[:-1], want))
                worst["model"] = max(worst["model"], gap)
            p = case["placement"]
            want = [*p["centre"], p["along"], p["across"]]
            gap = max(abs(a - b) for a, b in zip(row["placement"], want))
            worst["placement"] = max(worst["placement"], gap)
            worst["fade"] = max(worst["fade"], abs(row["fade"] - case["fade"]))
        for quantity, gap in worst.items():
            self.assertLessEqual(gap, tolerance[quantity], (quantity, worst))

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_02_placement_and_fade_match_pythontk(self):
        """ApplyNow() places the plane where ShadowProjection.model + placement
        say, and fades it as ShadowProjection.fade does."""
        unity = self._results()
        self.assertTrue(unity.get("shader_found"), "m3trik/ShadowPlane not found")
        self.assertFalse(unity.get("shader_has_error"), "the shader failed to compile")
        placement = unity["placement"]

        def check(name, got, expected, fade):
            pos, yaw, scale = expected
            self.assertTrue(got["resolved"], f"{name}: no source or contact")
            for axis, (g, e) in enumerate(zip(got["position"], pos)):
                self.assertAlmostEqual(
                    g, e, delta=1e-3, msg=f"{name}: position[{axis}] {g} != {e}"
                )
            self.assertLess(
                _angle_diff(got["yaw"], yaw), 1e-2, f"{name}: yaw {got['yaw']} != {yaw}"
            )
            for axis, (g, e) in enumerate(zip(got["scale"], scale)):
                self.assertAlmostEqual(
                    g, e, delta=1e-3, msg=f"{name}: scale[{axis}] {g} != {e}"
                )
            self.assertAlmostEqual(got["fade"], fade, delta=1e-5, msg=f"{name}: fade")

        for name, light in SOURCE_CASES.items():
            check(
                name,
                placement[name],
                _expected_placement(light=light),
                _expected_fade(light=light),
            )

        # Directional: a Light shines along its +Z, and the model must agree
        # with pythontk driven by that direction; with no source, the scene's sun.
        for name in ("directional_light", "scene_sun"):
            got = placement[name]
            check(
                name,
                got,
                _expected_placement(direction=got["shine"]),
                _expected_fade(direction=got["shine"]),
            )
            shine = np.asarray(got["shine"])
            # The rotation is asymmetric, so a wrong axis choice cannot hide.
            self.assertLess(shine[1], -0.1, f"{name}: the fixture sun must shine downward")
            self.assertGreater(
                abs(shine[0]) + abs(shine[2]), 0.3, f"{name}: the fixture sun must be oblique"
            )

    # ---- Step 3: the horizon PNG's import flags + wiring ---------------

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_03_horizon_import_pinned_and_wired(self):
        """`_horizon` PNG imported as data; the block carries the rig uniforms."""
        unity = self._results()
        self.assertTrue(unity.get("horizon_loaded"))
        imp = unity["horizon_import"]
        self.assertFalse(imp["srgb"], "horizon map must import linear")
        self.assertFalse(imp["alpha_is_transparency"], "alpha is a data channel")
        self.assertFalse(imp["mipmaps"], "no mipmaps (tiles must not blend)")
        self.assertEqual(imp["wrap"], "Clamp")
        self.assertEqual(imp["filter"], "Point")
        self.assertEqual(imp["npot"], "None")
        self.assertEqual(imp["compression"], "Uncompressed")
        self.assertTrue(imp["standalone_overridden"])
        self.assertEqual(imp["standalone_format"], "RGBA32")
        self.assertTrue(imp["android_overridden"])
        self.assertEqual(imp["android_format"], "RGBA32")
        tex = unity["horizon_texture"]
        hz = HorizonFixture
        self.assertEqual(
            (tex["width"], tex["height"]),
            (hz.SIZE * (hz.SPANS + 2), hz.SIZE),
            "the NPOT tile grid must keep its exact size",
        )
        self.assertEqual(tex["mip_count"], 1)
        self.assertEqual(tex["format"], "RGBA32")

        # Wiring: the block carries the fade and the rig uniforms, and the
        # shared material's own map is NOT duplicated into it.
        self.assertTrue(unity.get("mat_instancing"))
        self.assertAlmostEqual(unity["block_fade"], 1.0, places=5, msg="the fixture's fade is 1")
        params = unity["block_horizon_params"]
        self.assertEqual(
            [int(v) for v in params], [hz.SIZE, hz.SPANS, hz.map().levels, 0]
        )
        # The footprint and the height scale arrive in world units; the
        # ground plane's height in the frame rides in the meta vector.
        bounds = unity["block_horizon_bounds"]
        for got, want in zip(bounds, hz.BOUNDS):
            self.assertAlmostEqual(got, want, places=5)
        meta = unity["block_horizon_meta"]
        self.assertAlmostEqual(meta[0], hz.map().height_scale, places=5)
        self.assertAlmostEqual(meta[1], RECORD["ground"], places=5)
        # The frame travels as an origin and three WORLD axes; the source
        # travels in world space too. The fixture's contact is the identity,
        # so each is its own contact-local value.
        src = unity["block_source"]
        self.assertEqual(int(round(src[3])), 1, "positional source: w = 1")
        for got, exp in zip(src[:3], HORIZON_SOURCE):
            self.assertAlmostEqual(got, exp, places=4)
        for got, exp in zip(unity["block_origin"][:3], (0.0, 0.0, 0.0)):
            self.assertAlmostEqual(got, exp, places=4)
        for name, exp in (
            ("block_axis_a", hz.FRAME_A),
            ("block_axis_b", hz.FRAME_B),
            ("block_axis_up", (0.0, 1.0, 0.0)),
        ):
            for got, want in zip(unity[name][:3], exp):
                self.assertAlmostEqual(got, want, places=4, msg=name)
        self.assertFalse(
            unity["block_horizon_map_overridden"],
            "the material binds the map; the block must not duplicate it",
        )

    # ---- Step 4: horizon pixels against the oracle --------------------

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_05_horizon_pixels_match_oracle(self):
        """Shader alpha == the contract's math on the same PNG (a sizeless
        and a finite source), and the pole's shadow lies on the far side of
        the light."""
        unity = self._pixels_or_skip()
        self.assertLessEqual(unity["h1_max_rg"], 2, "not a grey shadow (error shader?)")
        xs, zs = _pixel_grid(unity)
        points = np.column_stack([xs, np.zeros_like(xs), zs])

        for key, size in (("h1", 0.0), ("h2", HORIZON_SOURCE_SIZE)):
            got = _alpha(unity, f"{key}_r")
            inside, _ = _inside_plane(unity[f"{key}_placement"], xs, zs)
            expected = HorizonFixture.alpha(points, HORIZON_SOURCE, size)
            expected = np.where(inside, expected, 0.0)
            n = int(inside.sum())
            self.assertGreater(n, 200, f"{key}: the plane must cover pixels")
            diff = np.abs(got - expected)
            in_shadow = int((expected > 0.5).sum())
            self.assertGreater(
                in_shadow, 40, f"{key}: the fixture casts too little shadow to test"
            )
            logger.info(
                "%s: %d px in plane, %d in shadow (oracle), mean|d| %.4f, "
                "p98 %.3f, >0.05: %.2f%%",
                key,
                n,
                in_shadow,
                diff.mean(),
                np.percentile(diff, 98),
                100.0 * (diff > 0.05).mean(),
            )
            self.assertLess(float(diff.mean()), 0.01, f"{key}: mean pixel error")
            self.assertLess(
                float((diff > 0.05).mean()),
                0.03,
                f"{key}: too many pixels disagree with the oracle",
            )
            self.assertGreater(
                float(got[expected > 0.5].mean()),
                0.8,
                f"{key}: the umbra must be dark in Unity too",
            )

        # Independent of the oracle: the alpha-weighted centroid of the pole's
        # shadow (the grounded layer, sizeless source) sits on the far side of
        # the pole from the light, along the bearing.
        got = _alpha(unity, "h1_r")
        pole = np.asarray(HorizonFixture.POLE)
        light = np.asarray([HORIZON_SOURCE[0], HORIZON_SOURCE[2]])
        bearing = pole - light
        bearing /= np.linalg.norm(bearing)
        # Keep only the pixels the first pole alone accounts for: the full map
        # minus a pole-1-only bake leaves the disc's and the second pole's
        # shadows, which are masked out of the centroid.
        only1 = HorizonFixture.alpha(
            points, HORIZON_SOURCE, 0.0, poles=HorizonFixture.POLES[:1], disc=False
        )
        full = HorizonFixture.alpha(points, HORIZON_SOURCE, 0.0)
        inside1, _ = _inside_plane(unity["h1_placement"], xs, zs)
        w = np.where(inside1 & (np.abs(full - only1) < 0.01), got, 0.0)
        self.assertGreater(float(w.sum()), 1.0, "no pole shadow to locate")
        centroid = np.array([(w * xs).sum(), (w * zs).sum()]) / w.sum()
        self.assertGreater(
            float((centroid - pole) @ bearing),
            0.15,
            "the pole's shadow must fall away from the light",
        )
        across = np.array([bearing[1], -bearing[0]])
        self.assertLess(
            abs(float((centroid - pole) @ across)),
            0.15,
            "the pole's shadow must lie along the bearing",
        )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_05a_the_fade_multiplies_the_march(self):
        """With its falloff on, the plane draws the oracle's shadow times the
        fade the component computed -- ShadowProjection.fade's, under 1."""
        unity = self._pixels_or_skip()
        xs, zs = _pixel_grid(unity)
        points = np.column_stack([xs, np.zeros_like(xs), zs])
        placement = unity["h5_placement"]
        fade = _expected_fade(light=HORIZON_SOURCE, falloff_power=FADED_FALLOFF)
        self.assertLess(fade, 0.9, "the fixture must fade measurably")
        self.assertAlmostEqual(placement["fade"], fade, delta=1e-5)
        got = _alpha(unity, "h5_r")
        inside, _ = _inside_plane(placement, xs, zs)
        expected = np.where(
            inside, HorizonFixture.alpha(points, HORIZON_SOURCE, HORIZON_SOURCE_SIZE), 0.0
        )
        diff = np.abs(got - expected * fade)
        self.assertLess(float(diff.mean()), 0.01, f"mean |d| {diff.mean():.4f}")
        self.assertAlmostEqual(
            float(got.sum() / (expected.sum() * fade)), 1.0, delta=0.03, msg="shadow mass"
        )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_05b_the_cached_horizon_matches_the_oracle(self):
        """The real-time path: the finite-source horizon marched once into the
        cache atlas and sampled bilinearly -- the oracle's shadow within a
        blend of cached texels -- and marched again only when the source
        moved (a still source's ApplyNow re-marches nothing)."""
        unity = self._pixels_or_skip()
        self.assertEqual(unity["h4_cached"], 1.0, "the plane draws its cache")
        self.assertGreater(unity["h4_atlas"], 0, "no atlas")
        self.assertTrue(unity["h4_global_is_atlas"], "the plane reads another texture")
        bakes0, bakes1, bakes2 = unity["cache_bakes"]
        self.assertGreaterEqual(bakes0, 1, "the cache was never marched")
        self.assertEqual(bakes1, bakes0, "a still source re-marched")
        self.assertEqual(bakes2, bakes1 + 1, "a moved source re-marches once")
        xs, zs = _pixel_grid(unity)
        points = np.column_stack([xs, np.zeros_like(xs), zs])
        got = _alpha(unity, "h4_r")
        inside, _ = _inside_plane(unity["h4_placement"], xs, zs)
        expected = HorizonFixture.alpha(points, HORIZON_SOURCE, HORIZON_SOURCE_SIZE)
        expected = np.where(inside, expected, 0.0)
        self.assertGreater(int((expected > 0.5).sum()), 40, "nothing in shadow")
        diff = np.abs(got - expected)
        logger.info(
            "h4 (cached): mean|d| %.4f, p98 %.3f, >0.05: %.2f%%, rect %s",
            diff.mean(),
            np.percentile(diff, 98),
            100.0 * (diff > 0.05).mean(),
            unity["h4_cache_rect"],
        )
        # Measured 2026-10-10 (6000.3, D3D11): mean 0.015, 1.5 % of pixels
        # over 0.25 -- a bilinear blend of 12 cm texels (three pixels of this
        # coarse 64-pixel map) softening the poles' edges -- and the shadow's
        # mass within 1 %: the cache softens, it never moves or loses shadow.
        self.assertLess(float(diff.mean()), 0.025, f"mean |d| {diff.mean():.4f}")
        self.assertLess(float((diff > 0.25).mean()), 0.03, "too many pixels off")
        self.assertAlmostEqual(
            float(got.sum() / expected.sum()), 1.0, delta=0.03, msg="shadow mass"
        )
        self.assertGreater(float(got[expected > 0.5].mean()), 0.8, "the umbra is dark")

    # ---- Step 5: the space's frame -------------------------------------

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_06_a_moved_model_carries_its_plane(self):
        """The plane is placed in its space's frame (the WebXR viewer's model
        space): lifted a metre, turned and scaled, the model's plane sits
        where pythontk puts it in the MODEL's terms -- on the model's ground,
        not the world's y = 0 -- and keeps its fade."""
        unity = self._results()
        got = unity["moved_model"]
        self.assertTrue(got["resolved"])
        pos, yaw, scale = _expected_placement(light=SOURCE_CASES["generic"])
        for axis, (g, e) in enumerate(zip(got["position"], pos)):
            self.assertAlmostEqual(g, e, delta=1e-3, msg=f"position[{axis}]: {got}")
        self.assertLess(_angle_diff(got["yaw"], yaw), 1e-2, got)
        for axis in (0, 2):
            self.assertAlmostEqual(
                got["scale"][axis], scale[axis], delta=1e-3, msg=f"scale: {got}"
            )
        self.assertAlmostEqual(
            got["fade"], _expected_fade(light=SOURCE_CASES["generic"]), delta=1e-5
        )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_07_a_moved_model_scales_its_horizon_block(self):
        """The horizon block's lengths are space lengths: the map's footprint,
        its height scale and the source's size follow the instance's scale,
        and the ground plane's height in the frame is the space's ground."""
        unity = self._results()
        s = MOVED_MODEL["scale"]
        meta = unity["h3_block_horizon_meta"]
        self.assertAlmostEqual(meta[0], HorizonFixture.map().height_scale * s, places=4)
        self.assertAlmostEqual(meta[1], RECORD["ground"] * s, places=4, msg=meta)
        for got, want in zip(unity["h3_block_horizon_bounds"], HorizonFixture.BOUNDS):
            self.assertAlmostEqual(got, want * s, places=4)
        self.assertAlmostEqual(
            unity["h3_block_source_radius"][0], HORIZON_SOURCE_SIZE * s, places=4
        )

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_08_a_moved_model_renders_the_same_horizon(self):
        """Seen by a camera the model carries, the moved, turned and scaled
        model's horizon shadow is pixel for pixel the unmoved one's."""
        unity = self._pixels_or_skip()
        before = _alpha(unity, "h2_r")
        after = _alpha(unity, "h3_r")
        self.assertGreater(
            int((before > 0.5).sum()), 40, "nothing in shadow to compare"
        )
        diff = np.abs(after - before)
        self.assertLess(float(diff.mean()), 0.01, f"mean |d| {diff.mean():.4f}")
        self.assertLess(float((diff > 0.05).mean()), 0.03)

    # ---- Step 6: an unmarchable layout ---------------------------------

    @EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
    def test_09_an_unmarchable_horizon_draws_nothing(self):
        """The block marches only a map the WebXR viewer would (S >= 8,
        K >= 1, a positive height scale, ordered bounds) that is the plane's
        own; anything else draws nothing (_Fade 0) -- Unity has no silhouette
        to fall back to, and never another plane's map off the shared
        material."""
        fades = self._results()["unmarched"]
        self.assertEqual(fades.pop("valid"), 1.0)
        for case, fade in fades.items():
            self.assertEqual(fade, 0.0, f"{case}: still draws")


class TestModuleImport(unittest.TestCase):
    """The module itself, apart from the Unity run."""

    def test_imports_on_a_pythontk_without_conformance(self):
        """``ptk.Conformance`` is new in pythontk 0.13.0. Asked for at import,
        it failed the module's load on an older pythontk -- a load error, which
        the runner counts as a failure, where a machine without Unity reports
        environment skips. Only the Unity half asks for the cases now."""
        spec = importlib.util.spec_from_file_location("_shadow_runtime_probe", __file__)
        module = importlib.util.module_from_spec(spec)
        with mock.patch.object(ptk, "Conformance", mock.NonCallableMock(spec=[])):
            spec.loader.exec_module(module)


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
    unittest.main(verbosity=2)
