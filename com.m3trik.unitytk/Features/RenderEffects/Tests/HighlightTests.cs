using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// A Highlight over the renderers under it: over a mapped emission the map is set
    /// aside only while it is above 0 (the glow alone shows) and is back at rest; over a
    /// uniform emission the glow adds to the material's own; with a Fade on the same
    /// subtree one twin carries both; and its pulse swings from 0 to the keyed intensity
    /// and back, at its rate (sampled at known times).
    /// </summary>
    public class HighlightTests
    {
        Stage _stage;

        [SetUp]
        public void SetUp() => _stage = new Stage();

        [TearDown]
        public void TearDown() => _stage.Dispose();

        [Test]
        public void Over_a_mapped_emission_the_map_is_set_aside_only_while_above_zero()
        {
            var map = _stage.Made(new Texture2D(4, 4) { name = "Screen_Emissive" });
            var material = _stage.Lit("Screen", new LitSurface { emission = Color.white, emissionMap = map });
            Assert.That(_stage.EmissionMap(material), Is.SameAs(map));
            var root = _stage.Node("Board");
            var highlight = root.AddComponent<Highlight>();
            highlight.color = MaterialColor.Encode(new Color(0f, 0f, 1f, 1f));
            var renderer = _stage.Renderer("Screen", root.transform, material);

            highlight.Apply();
            Assert.That(renderer.sharedMaterial, Is.SameAs(material), "at rest: the map, as authored");

            highlight.intensity = 0.5f;
            highlight.Apply();
            Material twin = renderer.sharedMaterial;
            Assert.That(twin, Is.Not.SameAs(material));
            Assert.That(_stage.EmissionMap(twin), Is.Null, "the map set aside while it glows");
            Stage.AssertRgb(_stage.Emission(twin), new Color(0f, 0f, 0.5f), "the glow alone, between its stops");
            Assert.That(twin.renderQueue, Is.EqualTo(material.renderQueue), "a glow blends nothing");

            highlight.intensity = 0f;
            highlight.Apply();
            Assert.That(renderer.sharedMaterial, Is.SameAs(material), "at rest the map is back");
            Assert.That(_stage.EmissionMap(material), Is.SameAs(map), "and never dropped from the material");
        }

        [Test]
        public void Over_a_uniform_emission_the_glow_adds_to_the_materials_own()
        {
            var material = _stage.Lit("Led", new LitSurface { emission = new Color(0.2f, 0.2f, 0.2f, 1f) });
            var root = _stage.Node("Panel");
            var highlight = root.AddComponent<Highlight>();
            highlight.color = MaterialColor.Encode(new Color(1f, 0f, 0f, 1f));
            var renderer = _stage.Renderer("Led", root.transform, material);

            highlight.intensity = 1f;
            highlight.Apply();
            Stage.AssertRgb(_stage.Emission(renderer.sharedMaterial), new Color(1.2f, 0.2f, 0.2f), "its own emission plus the glow");
        }

        [Test]
        public void A_fade_and_a_highlight_on_one_subtree_share_one_twin_per_material()
        {
            var material = _stage.Lit("Crate", new Color(0.5f, 0.5f, 0.5f, 1f));
            var root = _stage.Node("Crate");
            var fade = root.AddComponent<Fade>();
            var highlight = root.AddComponent<Highlight>();
            var a = _stage.Renderer("A", root.transform, material);
            var b = _stage.Renderer("B", root.transform, material);

            fade.alpha = 0.5f;
            highlight.intensity = 1f;
            highlight.Apply();
            Assert.That(a.sharedMaterial, Is.SameAs(b.sharedMaterial), "one twin for both renderers");
            Assert.That(a.sharedMaterial.name, Does.EndWith("(Blended+Glowing)"));
            Assert.That(_stage.BaseColor(a.sharedMaterial).a, Is.EqualTo(0.5f).Within(1e-5f));
            Stage.AssertRgb(_stage.Emission(a.sharedMaterial), EffectRecipeDefaults.PulseBright, "the default glow, the effect recipe's");
            Assert.That(fade.writer, Is.Not.Null, "on one node the Fade writes");
            Assert.That(highlight.writer, Is.Null);
        }

        [Test]
        public void A_pulse_rises_holds_bright_falls_and_holds_dim_within_the_keyed_intensity()
        {
            var root = _stage.Node("Beacon");
            var highlight = root.AddComponent<Highlight>();
            highlight.intensity = 0.8f;
            // A one-second swing: a quarter up, a quarter bright, a quarter down, a quarter dim.
            highlight.pulsePeriod = 1f;
            highlight.pulseDuty = 0.5f;
            highlight.pulseRamp = 0.25f;
            Assert.That(highlight.CurrentAt(0.125f), Is.EqualTo(0.8f), "no pulse: the intensity itself, at any time");
            highlight.pulse = true;
            Assert.That(highlight.CurrentAt(0f), Is.EqualTo(0f).Within(1e-5f), "it starts dark, as it is switched on");
            Assert.That(highlight.CurrentAt(0.125f), Is.EqualTo(0.4f).Within(1e-5f), "half way up");
            Assert.That(highlight.CurrentAt(0.3f), Is.EqualTo(0.8f).Within(1e-5f), "held at the keyed intensity");
            Assert.That(highlight.CurrentAt(0.625f), Is.EqualTo(0.4f).Within(1e-5f), "half way down");
            Assert.That(highlight.CurrentAt(0.875f), Is.EqualTo(0f).Within(1e-5f), "held dark");
            Assert.That(highlight.CurrentAt(10.3f), Is.EqualTo(0.8f).Within(1e-4f), "and so on, never past it");
            highlight.intensity = 0.5f;
            Assert.That(highlight.CurrentAt(0.3f), Is.EqualTo(0.5f).Within(1e-5f), "within the intensity keyed now");
            Assert.That(highlight.On, Is.True, "a pulsing highlight stays on: the map stays aside the whole time");
        }

        [Test]
        public void A_duty_and_ramp_past_the_swing_still_fall_to_dim_before_the_next_rise()
        {
            var highlight = _stage.Node("Beacon").AddComponent<Highlight>();
            highlight.intensity = 1f;
            highlight.pulse = true;
            highlight.pulsePeriod = 1f;
            highlight.pulseDuty = 0.99f;
            highlight.pulseRamp = 0.5f;
            // Up over half the swing, down over the other: no hold, and dim again just as the next rise starts.
            Assert.That(highlight.CurrentAt(0.5f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(highlight.CurrentAt(0.999f), Is.EqualTo(highlight.CurrentAt(0f)).Within(0.01f), "no jump at the wrap");
        }

        [Test]
        public void A_highlight_starts_from_the_effect_recipe_the_dcc_keys_with()
        {
            var highlight = _stage.Node("Beacon").AddComponent<Highlight>();
            Stage.AssertRgb(MaterialColor.Decode(highlight.color), EffectRecipeDefaults.PulseBright, "the recipe's bright colour, linear");
            Assert.That((highlight.pulsePeriod, highlight.pulseDuty, highlight.pulseRamp),
                        Is.EqualTo((EffectRecipeDefaults.PulsePeriod, EffectRecipeDefaults.PulseDuty, EffectRecipeDefaults.PulseRamp)));
        }
    }
}
