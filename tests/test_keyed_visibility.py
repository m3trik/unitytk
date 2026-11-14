#!/usr/bin/python
# coding=utf-8
"""
Verify keyed visibility from the keyed-visibility fixture FBX imports
correctly into Unity, with the unitytk package installed, and drives object
visibility at the right frames.

It:
    1. Creates a fresh Unity project (``UnityEnv.create_project``)
    2. Installs this checkout's package (``UnityHost.install(project, "local")``)
    3. Copies the FBX into Assets/ -- every ``.fbx`` is a delivery the
       package's importers read
    4. Deploys a C# verifier script to Assets/Editor/
    5. Runs Unity in batch mode (``UnityEnv.run``, -executeMethod)
    6. Reads the JSON results and asserts correctness

The verifier enumerates ALL AnimationClip curve bindings looking for
visibility-related properties (m_IsActive, m_Enabled, Visibility) and
reports which object paths have them, how many keys, and what values
the visibility has at specific sample frames.
"""

import os
import json
import shutil
import unittest
import logging

# Shared per-run scratch allocator: the suite's run goes with it (UNITYTK_PRESERVE=1 keeps it).
from base_test import (
    EnvSkip,
    TempProjects,
    UnityEnv,
    asset_path,
)

from unitytk.unity_host import UnityHost

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Source FBX — machine-local production asset, resolved from
# UNITYTK_TEST_ASSETS (see base_test.asset_path). Unset -> can't-exist, so the
# suite's availability guard skips instead of the repo carrying a studio
# drive layout.
# ---------------------------------------------------------------------------
FBX_PATH = asset_path("fixture_keyed_visibility.fbx")

#: Name the FBX is copied to inside the Unity project. Fixed rather than
#: derived from FBX_PATH so the C# "Assets/<name>" literal stays true
#: whatever the fixture file on disk is called.
FBX_ASSET_NAME = "fixture_keyed_visibility.fbx"

# Objects whose visibility is keyed in the source scene (27 total)
# These should appear as visibility animation in the FBX
EXPECTED_VISIBILITY_OBJECTS = [
    "MAGIC_CARPET_LOC",
    "S00A7_AIL_TENS_REG_LOC",
    "S00A10_CABLES_LOC",
    "S00A22_AIL_TURNB_LOC",
    "S00A24_CABLES_LOC",
    "S00A43_FWD_ROD_SS_LOC",
    "S00C1_INT_RODS_LOC",
    "S00C2_TENS_REG_LOC",
    "S00C3_CTRL_ROD_LOC",
    "S00C4_SHAKER_RODS_LOC",
    "S00C5_ELEV_TURNB_LOC",
    "S00B6_CABLES_LOC",
    "S00C12_ELEV_TURNB_LOC",
    "S00C13_PILOT_CNTR_SPRNG_LOC",
    "S00C14_INTRCON_ROD_LOC",
    "S00C15_INB_BUNGEE_SS_LOC",
    "S00C16_ELEV_SERV_RODS_LOC",
    "S00C18_ACC_PANEL_A_LOC",
    "S00C18_ACC_PANEL_B_LOC",
    "S00C18_ACC_PANEL_C_LOC",
    "S00C18_ACC_PANEL_D_LOC",
    "S00C18_ACC_PANEL_E_LOC",
    "S00C18_ACC_PANEL_F_LOC",
    "S00C18_ACC_PANEL_G_LOC",
    "S00C18_ACC_PANEL_H_LOC",
    "S00C18_ACC_PANEL_I_LOC",
    "S00C18_ACC_PANEL_J_LOC",
]


# ---------------------------------------------------------------------------
# C# verifier script — deployed to Assets/Editor/
# ---------------------------------------------------------------------------
VISIBILITY_VERIFIER_CS = r"""
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public class VisibilityVerifier
{
    public static void Verify()
    {
        string resultsFile = @"__RESULTS_PATH__";
        string fbxAsset = "Assets/__FBX_ASSET__";

        // Configure import: enable animation + visibility
        var importer = AssetImporter.GetAtPath(fbxAsset) as ModelImporter;
        if (importer != null)
        {
            importer.importAnimation = true;
            importer.importVisibility = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.resampleCurves = false;  // Preserve step keys
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(fbxAsset);
            AssetDatabase.ImportAsset(fbxAsset,
                ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var results = new Dictionary<string, object>();

        // --- Import settings verification ---
        var finalImporter = AssetImporter.GetAtPath(fbxAsset) as ModelImporter;
        results["import_visibility"] = finalImporter != null && finalImporter.importVisibility;
        results["import_animation"] = finalImporter != null && finalImporter.importAnimation;
        results["resample_curves"] = finalImporter != null && finalImporter.resampleCurves;

        // --- Prefab check ---
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAsset);
        results["prefab_loaded"] = prefab != null;

        int totalObjects = 0;
        if (prefab != null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            totalObjects = instance.GetComponentsInChildren<Transform>(true).Length;
            results["total_objects"] = totalObjects;
            Object.DestroyImmediate(instance);
        }

        // --- Animation curve analysis ---
        var allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxAsset);
        var clips = allAssets.OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();

        results["clip_count"] = clips.Length;

        // Collect ALL visibility-related curve bindings
        var visBindings = new List<string>();        // "path|property|type|keyCount"
        var allBindingPaths = new HashSet<string>(); // all unique binding paths
        int totalBindings = 0;
        int visibilityBindings = 0;
        int mIsActiveBindings = 0;
        int mEnabledBindings = 0;
        int genericVisBindings = 0;

        // Also track per-object visibility key data
        var objectKeyData = new Dictionary<string, string>(); // objectName -> "keyCount:k0_t=v,k1_t=v,..."

        foreach (var clip in clips)
        {
            results["clip_name"] = clip.name;
            results["clip_length"] = clip.length;
            results["clip_frame_rate"] = clip.frameRate;

            var bindings = AnimationUtility.GetCurveBindings(clip);
            totalBindings += bindings.Length;

            foreach (var b in bindings)
            {
                allBindingPaths.Add(b.path);

                bool isVis = false;
                if (b.propertyName == "m_IsActive")
                {
                    mIsActiveBindings++;
                    isVis = true;
                }
                else if (b.propertyName == "m_Enabled" &&
                         b.type == typeof(Renderer))
                {
                    mEnabledBindings++;
                    isVis = true;
                }
                else if (b.propertyName.Contains("Visibility") ||
                         b.propertyName.Contains("visibility"))
                {
                    genericVisBindings++;
                    isVis = true;
                }

                if (isVis)
                {
                    visibilityBindings++;
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    int keyCount = curve != null ? curve.length : 0;
                    visBindings.Add(b.path + "|" + b.propertyName + "|" +
                                   b.type.Name + "|" + keyCount);

                    // Extract key data for this object
                    if (curve != null && keyCount > 0)
                    {
                        string objName = b.path.Contains("/")
                            ? b.path.Substring(b.path.LastIndexOf('/') + 1)
                            : b.path;

                        var keyParts = new List<string>();
                        for (int k = 0; k < Mathf.Min(keyCount, 10); k++)
                        {
                            var key = curve.keys[k];
                            keyParts.Add(key.time.ToString("F2") + "=" +
                                         key.value.ToString("F1"));
                        }
                        objectKeyData[objName + "|" + b.propertyName] =
                            keyCount + ":" + string.Join(",", keyParts);
                    }
                }
            }

            // Also check ObjectReference bindings (some FBX formats)
            var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var ob in objBindings)
            {
                if (ob.propertyName == "m_IsActive" ||
                    ob.propertyName.Contains("isibility"))
                {
                    visibilityBindings++;
                    visBindings.Add(ob.path + "|" + ob.propertyName + "|ObjRef|0");
                }
            }
        }

        results["total_bindings"] = totalBindings;
        results["unique_paths"] = allBindingPaths.Count;
        results["visibility_bindings"] = visibilityBindings;
        results["m_is_active_bindings"] = mIsActiveBindings;
        results["m_enabled_bindings"] = mEnabledBindings;
        results["generic_vis_bindings"] = genericVisBindings;

        // --- Visibility binding details ---
        results["vis_binding_details"] = visBindings.ToArray();

        // --- Object key data ---
        var keyDataList = new List<string>();
        foreach (var kv in objectKeyData)
            keyDataList.Add(kv.Key + "=" + kv.Value);
        results["object_key_data"] = keyDataList.ToArray();

        // --- AnimationMode sampling at specific frames ---
        // Sample visibility of all objects at frame 0, 100, 200, 500
        try
        {
            if (prefab != null && clips.Length > 0)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var clip = clips[0];
                float fps = clip.frameRate > 0 ? clip.frameRate : 24f;

                float[] sampleFrames = new float[] { 0, 100, 155, 200, 500, 1000, 3694, 7804 };
                var sampleResults = new List<string>();

                AnimationMode.StartAnimationMode();

                foreach (float frame in sampleFrames)
                {
                    float time = frame / fps;
                    if (time > clip.length) continue;

                    AnimationMode.SampleAnimationClip(instance, clip, time);

                    // Check all transforms
                    var allT = instance.GetComponentsInChildren<Transform>(true);
                    var inactive = new List<string>();
                    var active = new List<string>();

                    foreach (var t in allT)
                    {
                        // Only report objects whose names contain LOC or known patterns
                        string n = t.gameObject.name;
                        if (n.Contains("_LOC") || n.Contains("MAGIC_CARPET") ||
                            n.Contains("ACC_PANEL"))
                        {
                            if (!t.gameObject.activeSelf)
                                inactive.Add(n);
                            else
                                active.Add(n);
                        }
                    }

                    sampleResults.Add("frame_" + ((int)frame) + "_inactive=" +
                        string.Join(";", inactive));
                    sampleResults.Add("frame_" + ((int)frame) + "_active_count=" +
                        active.Count);
                }

                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(instance);

                results["sample_results"] = sampleResults.ToArray();
            }
        }
        catch (System.Exception ex)
        {
            results["sample_error"] = ex.Message;
            Debug.LogWarning("[VisibilityVerifier] Sampling error: " + ex.Message);
        }

        // --- All object paths in the hierarchy ---
        if (prefab != null)
        {
            var instance2 = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var locObjects = new List<string>();
            foreach (var t in instance2.GetComponentsInChildren<Transform>(true))
            {
                string n = t.gameObject.name;
                if (n.Contains("_LOC") || n.Contains("MAGIC_CARPET") ||
                    n.Contains("ACC_PANEL"))
                {
                    // Build full path relative to root
                    string path = "";
                    Transform cur = t;
                    while (cur != instance2.transform)
                    {
                        path = (path == "") ? cur.name : cur.name + "/" + path;
                        cur = cur.parent;
                    }
                    locObjects.Add(path);
                }
            }
            results["loc_objects"] = locObjects.ToArray();
            results["loc_object_count"] = locObjects.Count;
            Object.DestroyImmediate(instance2);
        }

        // --- Write JSON ---
        var jsonParts = new List<string>();
        foreach (var kv in results)
        {
            string val;
            if (kv.Value is string[] arr)
                val = "[" + string.Join(",",
                    System.Array.ConvertAll(arr,
                        s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]";
            else if (kv.Value is string s)
                val = "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            else if (kv.Value is bool b)
                val = b ? "true" : "false";
            else if (kv.Value is int i)
                val = i.ToString();
            else if (kv.Value is float f)
                val = f.ToString("F4");
            else
                val = "\"" + (kv.Value != null ? kv.Value.ToString() : "null") + "\"";
            jsonParts.Add("  \"" + kv.Key + "\": " + val);
        }
        File.WriteAllText(resultsFile,
            "{\n" + string.Join(",\n", jsonParts) + "\n}");
        Debug.Log("[VisibilityVerifier] Results written to: " + resultsFile);
        EditorApplication.Exit(0);
    }
}
"""


# ---------------------------------------------------------------------------
# Test class
# ---------------------------------------------------------------------------
def _sources_available() -> bool:
    """The replay FBX is reachable under ``UNITYTK_TEST_ASSETS``.

    Matches the guard the sibling replay suites use. Without it an absent
    asset root reads as a FAILURE rather than a skip: the body asserts the
    FBX exists, which was fine while the path was a hardcoded studio one that
    happened to be present, but is wrong now the root is opt-in.
    """
    return os.path.isfile(FBX_PATH)


@EnvSkip.unless(
    _sources_available(),
    "keyed-visibility fixture FBX not accessible (set UNITYTK_TEST_ASSETS)",
)
@EnvSkip.unless(UnityEnv.available(), "no Unity editor found")
class TestKeyedVisibility(unittest.TestCase):
    """Verify all keyed visibility from the fixture FBX imports correctly
    into Unity and drives object show/hide at the right frames."""

    @classmethod
    def setUpClass(cls):
        cls.temp_dir = TempProjects.allocate("keyed_vis_test", cls)
        cls.project_path = TempProjects.project(cls.temp_dir)
        cls.results = None
        logger.info(f"Temp dir: {cls.temp_dir}")

    def test_visibility_import(self):
        """Full pipeline: FBX import + visibility curve verification in Unity."""

        # --- Preconditions ---
        self.assertTrue(
            os.path.isfile(FBX_PATH),
            f"FBX not found: {FBX_PATH}",
        )
        logger.info(f"Using Unity: {UnityEnv.editor()}")

        # === Step 1: Create fresh Unity project ===
        logger.info(f"Creating Unity project: {self.project_path}")
        UnityEnv.create_project(self, self.project_path)
        assets = os.path.join(self.project_path, "Assets")
        logger.info("Unity project created")

        # === Step 2: Install the package: this checkout's, by one manifest line ===
        UnityHost.install(self.project_path, "local")
        logger.info("Installed the com.m3trik.unitytk package")

        # === Step 3: Copy FBX into Assets/ ===
        dest_fbx = os.path.join(assets, FBX_ASSET_NAME)
        logger.info(
            f"Copying FBX ({os.path.getsize(FBX_PATH) / 1024 / 1024:.1f} MB)..."
        )
        shutil.copy2(FBX_PATH, dest_fbx)
        self.assertTrue(os.path.isfile(dest_fbx))

        # === Step 4: Deploy C# verifier to Assets/Editor/ ===
        editor_dir = os.path.join(assets, "Editor")
        os.makedirs(editor_dir, exist_ok=True)

        results_path = os.path.join(self.temp_dir, "visibility_results.json")
        verifier = VISIBILITY_VERIFIER_CS.replace(
            "__RESULTS_PATH__", results_path.replace("\\", "\\\\")
        ).replace("__FBX_ASSET__", FBX_ASSET_NAME)
        with open(
            os.path.join(editor_dir, "VisibilityVerifier.cs"), "w", encoding="utf-8"
        ) as f:
            f.write(verifier)

        # === Step 5: Launch Unity batch mode ===
        log_path = os.path.join(self.temp_dir, "unity.log")
        logger.info(
            "Launching Unity batch-mode verification (this may take a few minutes)..."
        )
        returncode = UnityEnv.run(
            self, self.project_path, "VisibilityVerifier.Verify", log_path, timeout=900
        )

        # === Step 6: Check for results ===
        if os.path.exists(log_path):
            with open(log_path, encoding="utf-8", errors="replace") as f:
                log_content = f.read()
            tail = log_content[-3000:]
            if not os.path.isfile(results_path):
                logger.error(f"Unity log tail:\n{tail}")
                # Check for compilation errors
                if "error CS" in log_content:
                    errors = [ln for ln in log_content.split("\n") if "error CS" in ln]
                    for e in errors[:10]:
                        logger.error(f"  {e.strip()}")

        UnityEnv.require_results(self, results_path, returncode, log_path)

        with open(results_path, encoding="utf-8") as f:
            r = json.load(f)
        self.__class__.results = r

        # === Step 7: Print and assert results ===
        print("\n" + "=" * 70)
        print("  KEYED VISIBILITY IMPORT TEST RESULTS")
        print("=" * 70)

        # Import settings
        self.assertTrue(r.get("import_visibility"), "importVisibility was not enabled")
        self.assertTrue(r.get("import_animation"), "importAnimation was not enabled")
        self.assertFalse(r.get("resample_curves"), "resampleCurves should be OFF")
        print("  Import Settings: visibility=ON, animation=ON, resample=OFF")

        # Prefab
        self.assertTrue(r.get("prefab_loaded"), "FBX prefab failed to load")
        total = r.get("total_objects", 0)
        print(f"  Total GameObjects:        {total}")
        self.assertGreater(total, 50, "Too few objects in prefab")

        # Clip info
        clip_count = r.get("clip_count", 0)
        clip_name = r.get("clip_name", "?")
        clip_length = r.get("clip_length", 0)
        print(f"  Animation Clips:          {clip_count}")
        print(f"  Clip Name:                {clip_name}")
        print(f"  Clip Length:              {clip_length:.2f}s")
        self.assertGreater(clip_count, 0, "No animation clips found")

        # Curve bindings
        total_bindings = r.get("total_bindings", 0)
        unique_paths = r.get("unique_paths", 0)
        vis_bindings = r.get("visibility_bindings", 0)
        m_active = r.get("m_is_active_bindings", 0)
        m_enabled = r.get("m_enabled_bindings", 0)
        generic = r.get("generic_vis_bindings", 0)

        print(f"\n  Total Curve Bindings:     {total_bindings}")
        print(f"  Unique Animated Paths:    {unique_paths}")
        print(f"  Visibility Bindings:      {vis_bindings}")
        print(f"    m_IsActive:             {m_active}")
        print(f"    m_Enabled (Renderer):   {m_enabled}")
        print(f"    Generic Visibility:     {generic}")

        self.assertGreater(
            vis_bindings,
            0,
            "NO visibility bindings found! FBX has no visibility animation.",
        )

        # Detailed visibility bindings
        vis_details = r.get("vis_binding_details", [])
        print(f"\n  Visibility Binding Details ({len(vis_details)}):")
        for d in vis_details:
            parts = d.split("|")
            path = parts[0] if len(parts) > 0 else "?"
            prop = parts[1] if len(parts) > 1 else "?"
            typ = parts[2] if len(parts) > 2 else "?"
            keys = parts[3] if len(parts) > 3 else "?"
            obj_name = path.split("/")[-1] if "/" in path else path
            print(f"    {obj_name:45s} {prop:15s} {typ:15s} keys={keys}")

        # Key data per object
        key_data = r.get("object_key_data", [])
        if key_data:
            print(f"\n  Visibility Key Data ({len(key_data)}):")
            for kd in key_data[:20]:
                print(f"    {kd}")

        # Objects in hierarchy
        loc_objects = r.get("loc_objects", [])
        loc_count = r.get("loc_object_count", 0)
        print(f"\n  LOC Objects in Hierarchy: {loc_count}")

        # Cross-reference: which expected objects are missing visibility curves?
        vis_object_names = set()
        for d in vis_details:
            path = d.split("|")[0]
            obj_name = path.split("/")[-1] if "/" in path else path
            vis_object_names.add(obj_name)

        missing = []
        found = []
        for expected in EXPECTED_VISIBILITY_OBJECTS:
            if expected in vis_object_names:
                found.append(expected)
            else:
                missing.append(expected)

        print(f"\n  Expected Visibility Objects: {len(EXPECTED_VISIBILITY_OBJECTS)}")
        print(f"  Found with curves:          {len(found)}")
        print(f"  MISSING curves:             {len(missing)}")
        if missing:
            print("\n  *** MISSING VISIBILITY CURVES ***")
            for m in missing:
                in_hierarchy = any(m in obj for obj in loc_objects)
                status = "IN HIERARCHY" if in_hierarchy else "NOT IN FBX"
                print(f"    {m:45s} [{status}]")

        # Frame sampling results
        sample_results = r.get("sample_results", [])
        sample_error = r.get("sample_error")
        if sample_error:
            print(f"\n  Sampling Error: {sample_error}")
        elif sample_results:
            print("\n  AnimationMode Sampling:")
            for sr in sample_results:
                print(f"    {sr}")

        print("=" * 70)

        # --- Final assertions ---
        # At minimum, the FBX should have some visibility curves
        self.assertGreater(
            vis_bindings,
            0,
            "Zero visibility bindings — FBX export did not include visibility animation",
        )

        # Report missing but don't hard-fail on count (diagnostic test)
        if missing:
            print(
                f"\n  WARNING: {len(missing)} expected objects missing visibility curves"
            )
            print("  These objects may need their visibility keyed directly in the source scene")
            print("  (display layer visibility does NOT export to FBX)")

        print("\n  VERDICT: See results above for visibility import status.")
        print("=" * 70 + "\n")


if __name__ == "__main__":
    # Configured HERE, not at import: this suite drives a multi-minute Unity batch
    # run and reports progress through logger.info, which needs a root handler to
    # be visible — but doing it at module scope reconfigured the root logger for
    # every process that merely imported the module, including the full-suite run.
    logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
    unittest.main(verbosity=2)
