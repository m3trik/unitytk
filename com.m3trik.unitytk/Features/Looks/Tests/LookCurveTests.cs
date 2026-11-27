using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Looks.Tests
{
    /// <summary>The display transforms, held to three.js r169's own outputs (its ACESFilmicToneMapping, in numpy).</summary>
    public class LookCurveTests
    {
        [TestCase(0.18f, 1f, 0.21311f, 0.21311f, 0.2131f)]
        [TestCase(0.5f, 1f, 0.55759f, 0.55759f, 0.55758f)]
        [TestCase(1f, 1f, 0.7634f, 0.7634f, 0.76339f)]
        [TestCase(0.18f, 0.5f, 0.08026f, 0.08026f, 0.08026f)]
        public void A_grey_shows_as_three_js_shows_it(float grey, float exposure, float r, float g, float b)
        {
            Color shown = LookCurve.Show(new Color(grey, grey, grey, 1f), LookCurve.AcesFilmic, exposure);
            Assert.That(new[] { shown.r, shown.g, shown.b }, Is.EqualTo(new[] { r, g, b }).Within(2e-4f));
        }

        [Test]
        public void A_colour_crosses_its_channels_as_three_js_does_and_the_others_are_plain()
        {
            Color shown = LookCurve.Show(new Color(0.8f, 0.2f, 0.05f, 1f), LookCurve.AcesFilmic, 1f);
            Assert.That(new[] { shown.r, shown.g, shown.b }, Is.EqualTo(new[] { 0.7814f, 0.2748f, 0.06441f }).Within(2e-4f));
            Assert.That(LookCurve.Show(new Color(0.3f, 0.3f, 0.3f, 1f), LookCurve.Linear, 2f).r, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(LookCurve.Show(new Color(2f, 0.3f, 0.3f, 1f), LookCurve.None, 5f), Is.EqualTo(new Color(1f, 0.3f, 0.3f, 1f)), "no tone mapping: no exposure, clamped");
            Assert.That(LookCurve.Knows("AgX"), Is.False);
        }

        [Test]
        public void The_handoffs_default_tone_mapping_is_one_it_shows_by_the_same_name()
        {
            Assert.That(LookCurve.AcesFilmic, Is.EqualTo(HandoffRendering.Renderer.ToneMappingDefault), "the record's spelling");
            Assert.That(LookCurve.Knows(HandoffRendering.Renderer.ToneMappingDefault));
        }

        [Test]
        public void Its_table_maps_a_scaled_input_to_the_colour_it_shows()
        {
            const int size = 8;
            Texture2D table = LookCurve.Table(LookCurve.AcesFilmic, 1f, size);
            try
            {
                Assert.That((table.width, table.height), Is.EqualTo((size * size, size)));
                int r = 5, g = 2, b = 6;  // red across a strip, green up it, blue across the strips
                Color cell = table.GetPixel(b * size + r, g);
                float step = 1f / (size - 1);
                var scene = new Color(Mathf.GammaToLinearSpace(r * step), Mathf.GammaToLinearSpace(g * step), Mathf.GammaToLinearSpace(b * step), 1f) * LookCurve.Brightest;
                Color shown = LookCurve.Show(scene, LookCurve.AcesFilmic, 1f);
                Assert.That(cell.r, Is.EqualTo(Mathf.LinearToGammaSpace(shown.r)).Within(2e-3f));
                Assert.That(cell.g, Is.EqualTo(Mathf.LinearToGammaSpace(shown.g)).Within(2e-3f));
                Assert.That(cell.b, Is.EqualTo(Mathf.LinearToGammaSpace(shown.b)).Within(2e-3f));
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void Its_span_is_the_brightest_light_its_table_reaches()
        {
            const int size = 8;
            Texture2D table = LookCurve.Table(LookCurve.Linear, 1f, size, span: 1f);
            try
            {
                // Spanning 1, a cell's input is the scene's light itself (16 times dimmer than the default span's).
                int mid = 3;
                float step = 1f / (size - 1);
                Color cell = table.GetPixel(0 * size + mid, 0);
                float scene = Mathf.GammaToLinearSpace(mid * step);
                Assert.That(cell.r, Is.EqualTo(Mathf.LinearToGammaSpace(Mathf.Clamp01(scene))).Within(2e-3f));
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }
    }
}
