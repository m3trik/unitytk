using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// An indicator light read from where a person stands (the user, 2026-11-24: "the user must notice the led
    /// light blinking from where they are standing"): a lit Emission Track with states is drawn a glow card at
    /// each renderer wearing its material, in the emission's colour (as its shader is handed it), never under MinPixels
    /// across, the light's size its mesh's bounding sphere; an off one, or a track without states, none. The card is
    /// made once, said once where it cannot be, and goes with the last indicator. The WebXR viewer's twin is held to the
    /// same rule (its constants: unitytk/tests/test_indicator_glow_twin.py).
    /// </summary>
    public class IndicatorGlowTests
    {
        Stage _stage;

        [SetUp]
        public void SetUp() => _stage = new Stage();

        [TearDown]
        public void TearDown() => _stage.Dispose();

        [Test]
        public void A_lit_indicator_is_carded_in_its_colour_and_an_off_one_or_a_plain_track_is_not()
        {
            var material = _stage.Lit("lens_led", new LitSurface { emission = Color.white });
            var model = _stage.Node("Board");
            model.AddComponent<Animator>();
            var lens = _stage.Renderer("Lens", model.transform, material);
            var track = _stage.Node("lens_led", model.transform).AddComponent<EmissionTrack>();
            track.material = material;
            track.states.Add(new EmissionState { name = "off", tint = Color.white, weight = 0f });
            track.states.Add(new EmissionState { name = "green", tint = new Color(0f, 1f, 0f), weight = 1f });
            IndicatorGlow.Join(track);  // its OnEnable's, which an Edit Mode test is not sent
            try
            {
                track.SetState("off");
                track.Apply();
                Assert.That(IndicatorGlow.Cards().Where(c => c.track == track), Is.Empty, "off: no glow");

                track.SetState("green");
                track.Apply();
                var card = IndicatorGlow.Cards().Single(c => c.track == track);
                Assert.That(card.renderer, Is.SameAs(lens), "at the renderer wearing its material");
                Stage.AssertRgb(card.colour, new Color(0f, 1f, 0f), "the emission's colour");
                Assert.That(card.colour.a, Is.EqualTo(1f));

                track.states.Clear();  // a track without states is no indicator
                Assert.That(IndicatorGlow.Cards().Where(c => c.track == track), Is.Empty);
            }
            finally
            {
                IndicatorGlow.Leave(track);
            }
        }

        [Test]
        public void A_card_sits_just_before_its_light_and_never_under_its_least_size()
        {
            var target = _stage.Made(new RenderTexture(100, 200, 16));
            var camera = _stage.Node("Eye").AddComponent<Camera>();
            camera.targetTexture = target;
            camera.fieldOfView = 60f;
            camera.transform.position = Vector3.zero;
            const float radius = 0.002f;  // a 4 mm lens, 10 m off

            IndicatorGlow.Place(camera, new Vector3(0f, 0f, 10f), radius, out Vector3 position, out float size);
            Assert.That(position.z, Is.EqualTo(10f - radius).Within(1e-5f), "before the lens by its own radius");
            float least = IndicatorGlow.MinPixels / 200f * 2f * Mathf.Tan(30f * Mathf.Deg2Rad) * (10f - radius);
            Assert.That(size, Is.EqualTo(least).Within(1e-5f), "far off: MinPixels across");
            Assert.That(size, Is.GreaterThan(2f * radius * IndicatorGlow.Halo));

            IndicatorGlow.Place(camera, new Vector3(0f, 0f, 0.5f), 0.02f, out _, out float halo);  // close up: the halo
            Assert.That(halo, Is.EqualTo(2f * 0.02f * IndicatorGlow.Halo).Within(1e-6f));
        }

        // The twin of the viewer's rule measured a light by its world box's half-diagonal: a round lens (a sphere, a
        // cylinder) came out up to sqrt(3) times the viewer's, its card bigger and nearer the eye.
        [Test]
        public void A_lights_size_is_its_meshs_bounding_sphere_times_its_largest_scale_as_the_viewer_measures_it()
        {
            var lens = _stage.Made(GameObject.CreatePrimitive(PrimitiveType.Sphere));  // its mesh: a sphere of radius 0.5
            lens.transform.position = new Vector3(1f, 2f, 3f);
            lens.transform.localScale = new Vector3(2f, 3f, 1f);
            IndicatorGlow.Sphere(lens.GetComponent<Renderer>(), out Vector3 centre, out float radius);
            Assert.That(Vector3.Distance(centre, lens.transform.position), Is.LessThan(1e-4f), "about its mesh's centre, placed");
            Assert.That(radius, Is.EqualTo(0.5f * 3f).Within(1e-3f), "its farthest vertex times its largest scale");
            Assert.That(radius, Is.LessThan(lens.GetComponent<Renderer>().bounds.extents.magnitude - 0.1f), "never its box's corner");
        }

        // The card's colour was handed to its shader as a linear colour through SetColor, which Unity takes for sRGB and
        // converts in a Linear project: an amber (1, 0.5, 0) glowed (1, 0.21, 0).
        [Test]
        public void A_cards_colour_reaches_its_shader_as_the_emission_it_glows()
        {
            var track = _stage.Node("lens_led").AddComponent<EmissionTrack>();
            var amber = new Color(1f, 0.5f, 0f, 0.8f);
            MaterialPropertyBlock block = IndicatorGlow.Colour(track, amber, "_BaseColor");
            Vector4 stored = block.GetVector("_BaseColor");  // what the shader is handed, as Unity stored it
            Color expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? amber : MaterialColor.Encode(amber);
            Assert.That(new[] { stored.x, stored.y, stored.z, stored.w },
                        Is.EqualTo(new[] { expected.r, expected.g, expected.b, expected.a }).Within(2e-3f),
                        "the emission as a material's colour reaches its shader, its alpha the opacity");
            IndicatorGlow.Release();
        }

        // Where the pipeline's unlit shader was missing, every camera's every frame made a material and a disc again, and
        // the disc was never freed; and the card's material outlived the last indicator.
        [Test]
        public void A_card_it_cannot_make_is_said_once_and_leaves_nothing_and_one_made_goes_with_the_last_indicator()
        {
            int Discs() => Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t.name == "Indicator Glow Disc");
            int Cards() => Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name == "Indicator Glow");
            IndicatorGlow.Release();
            int discs = Discs(), cards = Cards();
            var shaderless = new PureRenderPipelineHost("Unlit-less") { Lit = LitTable.HighDefinition };  // no project here has HDRP
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("Unlit-less pipeline's unlit shader"));
                Assert.That(IndicatorGlow.Ready(shaderless), Is.False);
                Assert.That(IndicatorGlow.Ready(shaderless), Is.False, "tried once for its pipeline");
                LogAssert.NoUnexpectedReceived();
                Assert.That((Discs(), Cards()), Is.EqualTo((discs, cards)), "nothing left behind");

                var adds = new PureRenderPipelineHost("Adds") { Lit = _stage.Table };  // the project's shaders, an additive switch
                Assert.That(IndicatorGlow.Ready(adds), Is.True);
                Assert.That(adds.Added.Select(m => m.name), Is.EqualTo(new[] { "Indicator Glow" }), "added over the scene, as the viewer's");

                var track = _stage.Node("lens_led").AddComponent<EmissionTrack>();
                IndicatorGlow.Join(track);
                IndicatorGlow.Leave(track);  // the last indicator gone
                Assert.That((Discs(), Cards()), Is.EqualTo((discs, cards)), "its card's material and disc with it");
            }
            finally
            {
                IndicatorGlow.Release();
            }
        }
    }
}
