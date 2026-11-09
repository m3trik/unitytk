using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// A Fade over the renderers under it, on the project's own pipeline: it nests as a
    /// CanvasGroup does; the renderers wear their materials as authored at 1 (opaque
    /// queue, depth, shadows) and a blended twin only below 1; they are hidden at 0; one
    /// twin per material is shared by every renderer the root covers; each slot of a
    /// two-material renderer keeps its own colour; and a renderer no value drives wears
    /// its shared material with no property block.
    /// </summary>
    public class FadeTests
    {
        const string Folder = "Assets/UnitytkRenderEffectsFadeTests";

        Stage _stage;

        [SetUp]
        public void SetUp() => _stage = new Stage();

        [TearDown]
        public void TearDown()
        {
            _stage.Dispose();
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void A_fade_nests_its_alpha_multiplying_its_parents()
        {
            var material = _stage.Lit("Wall", new Color(0.5f, 0.5f, 0.5f, 1f));
            var root = _stage.Node("Building");
            var outer = root.AddComponent<Fade>();
            var hall = _stage.Renderer("Hall", root.transform, material);
            var wing = _stage.Node("Wing", root.transform);
            var inner = wing.AddComponent<Fade>();
            var door = _stage.Renderer("Door", wing.transform, material);

            outer.alpha = 0.5f;
            inner.alpha = 0.5f;
            inner.Apply();
            Assert.That(inner.EffectiveAlpha, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(_stage.BaseColor(hall.sharedMaterial).a, Is.EqualTo(0.5f).Within(1e-5f), "the root's alpha");
            Assert.That(_stage.BaseColor(door.sharedMaterial).a, Is.EqualTo(0.25f).Within(1e-5f), "the nested one's times its parent's");
            Assert.That(inner.writer, Is.Null, "the outermost root writes; a nested Fade holds its value");
            Assert.That(outer.writer, Is.Not.Null);

            outer.alpha = 1f;
            outer.Apply();
            Assert.That(hall.sharedMaterial, Is.SameAs(material), "the root at 1: its own renderers as authored");
            Assert.That(_stage.BaseColor(door.sharedMaterial).a, Is.EqualTo(0.5f).Within(1e-5f), "the nested one alone");
        }

        [Test]
        public void A_material_is_opaque_at_rest_and_blended_only_while_below_one()
        {
            var material = _stage.Lit("Crate", new Color(0.8f, 0.4f, 0.2f, 1f));
            int queue = material.renderQueue;
            float zwrite = material.GetFloat("_ZWrite");
            var root = _stage.Node("Crate");
            var fade = root.AddComponent<Fade>();
            var renderer = _stage.Renderer("Body", root.transform, material);
            renderer.shadowCastingMode = ShadowCastingMode.On;

            fade.Apply();
            Assert.That(renderer.sharedMaterial, Is.SameAs(material), "at 1: as authored, shared");
            Assert.That(queue, Is.LessThanOrEqualTo((int)RenderQueue.GeometryLast), "sorted with the opaque pass");

            fade.alpha = 0.5f;
            fade.Apply();
            Material twin = renderer.sharedMaterial;
            Assert.That(twin, Is.Not.SameAs(material), "below 1: its twin");
            Assert.That(twin.renderQueue, Is.GreaterThan((int)RenderQueue.GeometryLast), "sorted with the transparent pass");
            Assert.That(twin.GetFloat("_ZWrite"), Is.EqualTo(0f), "no depth while it fades");
            Assert.That(_stage.BaseColor(twin).a, Is.EqualTo(0.5f).Within(1e-5f));
            Stage.AssertRgb(_stage.BaseColor(twin), _stage.BaseColor(material), "its colour kept");
            Assert.That(material.renderQueue, Is.EqualTo(queue), "the original never changes");

            fade.alpha = 1f;
            fade.Apply();
            Assert.That(renderer.sharedMaterial, Is.SameAs(material), "back at 1: the shared original");
            Assert.That(renderer.sharedMaterial.renderQueue, Is.EqualTo(queue), "sorted as opaque again");
            Assert.That(renderer.sharedMaterial.GetFloat("_ZWrite"), Is.EqualTo(zwrite), "writing depth again");
            Assert.That(renderer.sharedMaterial.GetShaderPassEnabled("ShadowCaster"), Is.True, "casting shadows again");
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On), "the renderer's own setting untouched");
            Assert.That(renderer.HasPropertyBlock(), Is.False, "no property block, ever");
        }

        [Test]
        public void At_zero_the_renderers_are_hidden_and_shown_again_above_it()
        {
            var material = _stage.Lit("Crate");
            var root = _stage.Node("Crate");
            var fade = root.AddComponent<Fade>();
            var renderer = _stage.Renderer("Body", root.transform, material);

            fade.alpha = 0f;
            fade.Apply();
            Assert.That(renderer.forceRenderingOff, Is.True, "hidden at 0");
            Assert.That(renderer.enabled, Is.True, "through its own switch, which no curve fights: the renderer stays enabled");
            Assert.That(renderer.sharedMaterial, Is.SameAs(material), "no twin held while hidden");

            fade.alpha = 0.25f;
            fade.Apply();
            Assert.That(renderer.forceRenderingOff, Is.False);
            Assert.That(_stage.BaseColor(renderer.sharedMaterial).a, Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void One_twin_per_material_is_shared_by_every_renderer_the_root_covers()
        {
            const int materials = 20, each = 10;
            var root = _stage.Node("Building");
            var fade = root.AddComponent<Fade>();
            var renderers = new List<MeshRenderer>();
            for (int m = 0; m < materials; m++)
            {
                var material = _stage.Lit($"Mat{m}", new Color(m / 20f, 0.5f, 0.5f, 1f));
                for (int r = 0; r < each; r++) renderers.Add(_stage.Renderer($"Part{m}_{r}", root.transform, material));
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            RenderEffectsActions.PrepareMaterials(root, Folder + "/Building Twins.asset");
            Assert.That(fade.twins, Is.Not.Null, "prepared in the editor");
            Assert.That(fade.twins.twins, Has.Count.EqualTo(materials), "one template per material and state");

            fade.alpha = 0.5f;
            fade.Apply();
            var worn = renderers.Select(r => r.sharedMaterial).Distinct().ToList();
            Assert.That(worn, Has.Count.EqualTo(materials), $"{renderers.Count} renderers, {materials} twins");
            Assert.That(fade.writer.InstanceCount, Is.EqualTo(materials));
            foreach (Material twin in worn)
            {
                Assert.That(twin.name, Does.EndWith(" (Blended)"), "an instance of the prepared template");
                Assert.That(fade.twins.twins.Select(t => t.template), Has.None.SameAs(twin), "the template itself stays unworn");
            }
        }

        [Test]
        public void Each_slot_of_a_two_material_renderer_keeps_its_own_colour()
        {
            var red = _stage.Lit("Red", new Color(0.9f, 0.1f, 0.1f, 1f));
            var green = _stage.Lit("Green", new Color(0.1f, 0.9f, 0.1f, 1f));
            var root = _stage.Node("Sign");
            var fade = root.AddComponent<Fade>();
            var renderer = _stage.Renderer("Board", root.transform, red, green);

            fade.alpha = 0.5f;
            fade.Apply();
            Material[] worn = renderer.sharedMaterials;
            Assert.That(worn, Has.Length.EqualTo(2));
            Stage.AssertRgb(_stage.BaseColor(worn[0]), _stage.BaseColor(red), "slot 0 keeps its own colour");
            Stage.AssertRgb(_stage.BaseColor(worn[1]), _stage.BaseColor(green), "slot 1 keeps its own colour, never slot 0's");
            Assert.That(_stage.BaseColor(worn[0]).a, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(_stage.BaseColor(worn[1]).a, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(renderer.HasPropertyBlock(), Is.False);
        }

        [Test]
        public void A_renderer_no_value_drives_wears_its_shared_material_and_no_property_block()
        {
            var material = _stage.Lit("Crate");
            var root = _stage.Node("Crate");
            var fade = root.AddComponent<Fade>();
            root.AddComponent<Highlight>();
            var covered = _stage.Renderer("Body", root.transform, material);
            var outside = _stage.Renderer("Shelf", null, material);

            fade.Apply();
            foreach (var renderer in new[] { covered, outside })
            {
                Assert.That(renderer.sharedMaterial, Is.SameAs(material), $"{renderer.name}: no twin at rest");
                Assert.That(renderer.HasPropertyBlock(), Is.False, $"{renderer.name}: no property block");
            }
            Assert.That(fade.writer.InstanceCount, Is.Zero, "nothing made for a value at rest");
        }
    }
}
