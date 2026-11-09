using System.Collections;
using System.IO;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// The editor's one keeper of what an effect shows outside play mode, where Unity
    /// sends it no OnDisable, OnDestroy or LateUpdate, with no inspector open: an effect
    /// removed (or its Add undone), disabled or deactivated gives its renderers back on
    /// the next editor update; a scene saved or a prefab written while effects show holds
    /// only the originals;
    /// a closed scene frees the twins; an effect in a prefab asset shows nothing (a twin on
    /// an asset's renderer would save as a missing material); and while the Animation window or a Timeline
    /// previews a clip (Animation mode) every effect in the open scenes shows its values
    /// each update, each given back when the preview stops.
    /// </summary>
    public class RenderEffectsPreviewTests
    {
        const string Folder = "Assets/UnitytkRenderEffectsPreviewTests";

        Stage _stage;

        [SetUp]
        public void SetUp() => _stage = new Stage();

        [TearDown]
        public void TearDown()
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            _stage.Dispose();
            AssetDatabase.DeleteAsset(Folder);
        }

        // A root fading the one renderer under it to alpha.
        (Fade fade, MeshRenderer renderer, Material original) Faded(string name, float alpha, Material material = null)
        {
            material = material != null ? material : _stage.Lit(name + "_MAT");
            var root = _stage.Node(name);
            var fade = root.AddComponent<Fade>();
            fade.alpha = alpha;
            return (fade, _stage.Renderer(name + "_Body", root.transform, material), material);
        }

        static void AssertBack(Renderer renderer, Material original, string why)
        {
            Assert.That(renderer.sharedMaterial, Is.SameAs(original), why + ": its material as authored");
            Assert.That(renderer.forceRenderingOff, Is.False, why + ": shown");
        }

        // Two editor updates: the keeper's runs between them whatever order the updates take.
        static IEnumerator Updates()
        {
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator A_shown_effect_removed_gives_its_renderers_back_and_frees_its_twin()
        {
            var (half, halfRenderer, halfMaterial) = Faded("Half", 0.5f);
            var (gone, goneRenderer, goneMaterial) = Faded("Gone", 0f);
            half.Apply();
            gone.Apply();
            Material twin = halfRenderer.sharedMaterial;
            Assert.That(twin, Is.Not.SameAs(halfMaterial), "shown on its twin");
            Assert.That(goneRenderer.forceRenderingOff, Is.True, "shown hidden");

            Object.DestroyImmediate(half);
            Object.DestroyImmediate(gone);
            yield return Updates();
            AssertBack(halfRenderer, halfMaterial, "a removed Fade");
            AssertBack(goneRenderer, goneMaterial, "a removed Fade at 0");
            Assert.That(twin == null, Is.True, "its twin freed");
        }

        [UnityTest]
        public IEnumerator A_shown_effects_add_undone_gives_its_renderers_back()
        {
            var material = _stage.Lit("Crate_MAT");
            var root = _stage.Node("Crate");
            var renderer = _stage.Renderer("Crate_Body", root.transform, material);
            Undo.IncrementCurrentGroup();
            var fade = Undo.AddComponent<Fade>(root);
            fade.alpha = 0f;
            fade.Apply();
            Assert.That(renderer.forceRenderingOff, Is.True, "shown hidden");

            Undo.PerformUndo();
            Assert.That(fade == null, Is.True, "the Add undone");
            yield return Updates();
            AssertBack(renderer, material, "an undone Add");
        }

        [UnityTest]
        public IEnumerator A_shown_effect_disabled_or_deactivated_gives_its_renderers_back()
        {
            var (off, offRenderer, offMaterial) = Faded("Off", 0.5f);
            var (away, awayRenderer, awayMaterial) = Faded("Away", 0f);
            off.Apply();
            away.Apply();

            off.enabled = false;
            away.gameObject.SetActive(false);
            yield return Updates();
            AssertBack(offRenderer, offMaterial, "a disabled Fade");
            AssertBack(awayRenderer, awayMaterial, "a deactivated Fade");
        }

        [Test]
        public void A_scene_saved_while_an_effect_shows_holds_only_the_originals()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                string asset = Folder + "/Sign_MAT.mat";
                var material = new Material(Shader.Find(_stage.Host.LitShader)) { name = "Sign_MAT" };
                Assert.That(MaterialWriter.Write(material, new LitSurface(), _stage.Host), Is.True);
                AssetDatabase.CreateAsset(material, asset);
                var (fade, renderer, _) = Faded("Sign", 0.5f, material);
                var (hidden, hiddenRenderer, _) = Faded("Hidden", 0f, material);
                fade.Apply();
                hidden.Apply();
                Assert.That(renderer.sharedMaterial, Is.Not.SameAs(material), "shown on its twin");
                Assert.That(hiddenRenderer.forceRenderingOff, Is.True, "shown hidden");

                string path = Folder + "/Preview.unity";
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
                // The save writes the material asset again (measured), so it is read back by its path.
                foreach (Renderer r in new Renderer[] { renderer, hiddenRenderer })
                {
                    Assert.That(AssetDatabase.GetAssetPath(r.sharedMaterial), Is.EqualTo(asset), $"{r.name}: given back before the save");
                    Assert.That(r.forceRenderingOff, Is.False, $"{r.name}: shown");
                }
                string guid = AssetDatabase.AssetPathToGUID(asset);
                string[] slots = MaterialSlots(File.ReadAllText(path));
                Assert.That(slots, Has.Length.EqualTo(2), "two renderers, one slot each");
                Assert.That(slots, Has.All.Contains("guid: " + guid), "the saved scene references the original alone");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void An_effect_in_a_prefab_asset_shows_nothing()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            var material = new Material(Shader.Find(_stage.Host.LitShader)) { name = "Crate_MAT" };
            Assert.That(MaterialWriter.Write(material, new LitSurface(), _stage.Host), Is.True);
            AssetDatabase.CreateAsset(material, Folder + "/Crate_MAT.mat");
            var (fade, _, _) = Faded("Crate", 0.5f, material);
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(fade.gameObject, Folder + "/Crate.prefab");
            var held = RenderEffectsPreview.Count;

            asset.GetComponent<Fade>().Apply();
            Assert.That(AssetDatabase.GetAssetPath(asset.GetComponentInChildren<Renderer>().sharedMaterial),
                        Is.EqualTo(Folder + "/Crate_MAT.mat"), "a twin on an asset's renderer would save as a missing material");
            Assert.That(RenderEffectsPreview.Count, Is.EqualTo(held), "nothing held for it");
        }

        [Test]
        public void A_prefab_written_while_an_effect_shows_holds_the_materials_as_authored()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string asset = Folder + "/Crate_MAT.mat";
            var material = new Material(Shader.Find(_stage.Host.LitShader)) { name = "Crate_MAT" };
            Assert.That(MaterialWriter.Write(material, new LitSurface(), _stage.Host), Is.True);
            AssetDatabase.CreateAsset(material, asset);
            var (fade, renderer, _) = Faded("Crate", 0.5f, material);
            fade.Apply();
            Assert.That(renderer.sharedMaterial, Is.Not.SameAs(material), "shown on its twin");

            // As a drag into the Project window, or an instance's overrides applied, writes it.
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(fade.gameObject, Folder + "/Crate.prefab");
            Assert.That(AssetDatabase.GetAssetPath(prefab.GetComponentInChildren<Renderer>().sharedMaterial), Is.EqualTo(asset),
                        "the prefab holds the original: a twin would save as a missing material");
            Assert.That(RenderEffectsPreview.Showing(fade), Is.False, "given back before the write");
            Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial), Is.EqualTo(asset), "the scene's renderer given back too");
        }

        // Every material slot a scene file's renderers hold, as written.
        static string[] MaterialSlots(string yaml)
        {
            var slots = new System.Collections.Generic.List<string>();
            string[] lines = yaml.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "m_Materials:") continue;
                for (int j = i + 1; j < lines.Length && lines[j].TrimStart().StartsWith("- "); j++) slots.Add(lines[j].Trim());
            }
            return slots.ToArray();
        }

        [Test]
        public void A_scene_closed_while_an_effect_shows_frees_its_twins()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var (fade, renderer, material) = Faded("Lamp", 0.5f);
            fade.Apply();
            Material twin = renderer.sharedMaterial;
            Assert.That(twin, Is.Not.SameAs(material), "shown on its twin");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(twin == null, Is.True, "a twin outlives no scene");
        }

        [UnityTest]
        public IEnumerator In_animation_mode_every_effect_shows_its_values_and_is_given_back_when_it_stops()
        {
            var (a, aRenderer, aMaterial) = Faded("A", 0.5f);
            var (b, bRenderer, bMaterial) = Faded("B", 0f);
            var glowMaterial = _stage.Lit("Glow_MAT");
            var glow = _stage.Node("Glow");
            var highlight = glow.AddComponent<Highlight>();
            highlight.intensity = 1f;
            var glowRenderer = _stage.Renderer("Glow_Body", glow.transform, glowMaterial);

            AnimationMode.StartAnimationMode();
            yield return Updates();
            Assert.That(aRenderer.sharedMaterial.name, Does.EndWith("(Blended)"), "every effect shows, none inspected");
            Assert.That(bRenderer.forceRenderingOff, Is.True);
            Assert.That(glowRenderer.sharedMaterial.name, Does.EndWith("(Glowing)"));
            a.alpha = 0.25f;  // as a clip's sample writes it
            yield return Updates();
            Assert.That(_stage.BaseColor(aRenderer.sharedMaterial).a, Is.EqualTo(0.25f).Within(1e-5f), "each update shows the values now");

            AnimationMode.StopAnimationMode();
            yield return Updates();
            AssertBack(aRenderer, aMaterial, "the preview stopped");
            AssertBack(bRenderer, bMaterial, "the preview stopped");
            AssertBack(glowRenderer, glowMaterial, "the preview stopped");
            Assert.That(b.alpha, Is.EqualTo(0f), "the values themselves are the clip's, untouched");
        }
    }
}
