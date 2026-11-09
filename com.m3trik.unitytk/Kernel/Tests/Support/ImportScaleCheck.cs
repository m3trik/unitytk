using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Testing
{
    /// <summary>
    /// A model imported at the model importer's defaults, then again with one setting
    /// changed (the Scale Factor, Convert Units), and what an importer made of it each time:
    /// the scaffold every test of an import's scale shares.
    /// </summary>
    public static class ImportScaleCheck
    {
        /// <summary>
        /// <paramref name="model"/> copied into <paramref name="scratch"/> and imported, read by
        /// <paramref name="read"/>; then <paramref name="set"/> applied to its importer, reimported
        /// and read again. <c>scale</c> is how much the import scaled the meshes (the renderers
        /// <paramref name="counts"/> keeps; every mesh renderer when null), asserted not 1 and,
        /// where <paramref name="expected"/> is not 0, that.
        /// </summary>
        public static (T before, T after, float scale) Rescaled<T>(string model, string scratch, Action<ModelImporter> set,
                                                                   Func<GameObject, T> read, float expected = 0f,
                                                                   Func<Renderer, bool> counts = null)
        {
            if (!AssetDatabase.IsValidFolder(scratch))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(scratch).Replace('\\', '/'), System.IO.Path.GetFileName(scratch));
            string copy = scratch + "/" + System.IO.Path.GetFileName(model);
            Assert.That(AssetDatabase.CopyAsset(model, copy), Is.True, $"copy {model}");
            AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(copy);
            T before = read(imported);
            float extent = Extent(imported, counts);

            var importer = (ModelImporter)AssetImporter.GetAtPath(copy);
            set(importer);
            importer.SaveAndReimport();
            imported = AssetDatabase.LoadAssetAtPath<GameObject>(copy);
            float scale = Extent(imported, counts) / extent;
            if (expected != 0f) Assert.That(scale, Is.EqualTo(expected).Within(1e-3f), "the import scaled the meshes");
            Assert.That(scale, Is.Not.EqualTo(1f).Within(1e-3f), "the setting scales the meshes");
            return (before, read(imported), scale);
        }

        /// <summary>The size of the mesh renderers' bounds (those <paramref name="counts"/> keeps), the model placed at the origin.</summary>
        public static float Extent(GameObject prefab, Func<Renderer, bool> counts = null)
        {
            GameObject placed = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Bounds? bounds = null;
                foreach (Renderer r in placed.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (counts != null && !counts(r)) continue;
                    if (bounds == null) bounds = r.bounds;
                    else
                    {
                        Bounds b = bounds.Value;
                        b.Encapsulate(r.bounds);
                        bounds = b;
                    }
                }
                Assert.That(bounds, Is.Not.Null, "the model's meshes");
                return bounds.Value.size.magnitude;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(placed);
            }
        }

        /// <summary>Assert <paramref name="actual"/> is <paramref name="expected"/>, within a hundredth of a percent of its size.</summary>
        public static void Near(Vector3 actual, Vector3 expected, string why)
            => Assert.That((actual - expected).magnitude, Is.LessThan(1e-4f * Mathf.Max(1f, expected.magnitude)),
                           $"{why}: {actual} for {expected}");
    }
}
