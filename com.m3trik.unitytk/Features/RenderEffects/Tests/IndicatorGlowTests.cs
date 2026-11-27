using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// An indicator light read from where a person stands: a lit Emission Track with states is drawn a glow card at
    /// each renderer wearing its material, in the emission's colour (as its shader is handed it), never under MinPixels
    /// across, the light's size its mesh's bounding sphere; an off one, or a track without states, none. Each track's
    /// card is its own, made once, said once where it cannot be (however many ask), blended where the pipeline has no
    /// additive switch, and gone, with its render callback, as its track is switched off. The WebXR viewer's twin is
    /// held to the same rule (its constants: unitytk/tests/test_indicator_glow_twin.py).
    /// </summary>
    public class IndicatorGlowTests
    {
        Stage _stage;

        [SetUp]
        public void SetUp() => _stage = new Stage();

        [TearDown]
        public void TearDown() => _stage.Dispose();

        static int Discs() => Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t.name == "Indicator Glow Disc");

        static int Cards() => Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name == "Indicator Glow");

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
            track.ShowGlow();  // its OnEnable's, which an Edit Mode test is not sent
            try
            {
                track.SetState("off");
                track.Apply();
                Assert.That(track.Glow.Cards(), Is.Empty, "off: no glow");

                track.SetState("green");
                track.Apply();
                var card = track.Glow.Cards().Single();
                Assert.That(card.renderer, Is.SameAs(lens), "at the renderer wearing its material");
                Stage.AssertRgb(card.colour, new Color(0f, 1f, 0f), "the emission's colour");
                Assert.That(card.colour.a, Is.EqualTo(1f));

                track.states.Clear();  // a track without states is no indicator
                Assert.That(track.Glow.Cards(), Is.Empty);
            }
            finally
            {
                track.HideGlow();
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

        // The viewer measures a light by its mesh's bounding sphere times its largest scale: a round lens (a sphere, a
        // cylinder) measured by its world box's half-diagonal would come out up to sqrt(3) times as large.
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

        // Unity takes a colour handed through SetColor for sRGB and converts it in a Linear project, so the emission is
        // handed as a material stores it: an amber (1, 0.5, 0) handed as linear would glow (1, 0.21, 0).
        [Test]
        public void A_cards_colour_reaches_its_shader_as_the_emission_it_glows()
        {
            var track = _stage.Node("lens_led").AddComponent<EmissionTrack>();
            var amber = new Color(1f, 0.5f, 0f, 0.8f);
            track.ShowGlow();
            try
            {
                MaterialPropertyBlock block = track.Glow.Colour(amber, "_BaseColor");
                Vector4 stored = block.GetVector("_BaseColor");  // what the shader is handed, as Unity stored it
                Color expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? amber : MaterialColor.Encode(amber);
                Assert.That(new[] { stored.x, stored.y, stored.z, stored.w },
                            Is.EqualTo(new[] { expected.r, expected.g, expected.b, expected.a }).Within(2e-3f),
                            "the emission as a material's colour reaches its shader, its alpha the opacity");
            }
            finally
            {
                track.HideGlow();
            }
        }

        // A card that cannot be made is tried once for its pipeline and said once, however many indicators ask, so a
        // camera's every frame makes nothing again; one made is its indicator's own and goes with it.
        [Test]
        public void A_card_it_cannot_make_is_said_once_and_leaves_nothing_and_one_made_goes_with_its_indicator()
        {
            int discs = Discs(), cards = Cards();
            var first = _stage.Node("lens_led").AddComponent<EmissionTrack>();
            var second = _stage.Node("lamp_led").AddComponent<EmissionTrack>();
            string pipeline = "Unlit-less " + System.Guid.NewGuid().ToString("N");  // a warning this session has not said
            var shaderless = new PureRenderPipelineHost(pipeline) { Lit = LitTable.HighDefinition };  // no project here has HDRP
            first.ShowGlow();
            second.ShowGlow();
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(pipeline) + " pipeline's unlit shader"));
                Assert.That(first.Glow.Ready(shaderless), Is.False);
                Assert.That(first.Glow.Ready(shaderless), Is.False, "tried once for its pipeline");
                Assert.That(second.Glow.Ready(shaderless), Is.False);
                LogAssert.NoUnexpectedReceived();  // once, however many indicators ask
                Assert.That((Discs(), Cards()), Is.EqualTo((discs, cards)), "nothing left behind");

                var adds = new PureRenderPipelineHost("Adds") { Lit = _stage.Table };  // the project's shaders, an additive switch
                Assert.That(first.Glow.Ready(adds), Is.True);
                Assert.That(adds.Added.Select(m => m.name), Is.EqualTo(new[] { "Indicator Glow" }), "added over the scene, as the viewer's");
                Assert.That((Discs(), Cards()), Is.EqualTo((discs + 1, cards + 1)), "its own card");

                first.HideGlow();  // switched off: its OnDisable's, which a scene's unload and a domain reload send too
                Assert.That((Discs(), Cards()), Is.EqualTo((discs, cards)), "its card's material and disc with it");
            }
            finally
            {
                first.HideGlow();
                second.HideGlow();
            }
        }

        // Its pipeline's host answering that it has no additive switch is no failure: the card is drawn blended.
        [Test]
        public void A_card_is_blended_over_the_scene_where_its_pipelines_host_has_no_additive_switch()
        {
            var track = _stage.Node("lens_led").AddComponent<EmissionTrack>();
            track.ShowGlow();
            try
            {
                var blends = new PureRenderPipelineHost("Blends") { Lit = _stage.Table, Adds = false };
                Assert.That(track.Glow.Ready(blends), Is.True, "made all the same");
                Assert.That(blends.Added, Is.EqualTo(new[] { track.Glow.Card }), "asked for its additive switch");

                IRenderPipelineHost builtIn = RenderPipelineHosts.BuiltIn;  // the real host with none
                Assert.That(track.Glow.Ready(builtIn), Is.True, "made all the same");
                Assert.That(track.Glow.Card.shader.name, Is.EqualTo(builtIn.UnlitShader(true)),
                            "on the pipeline's unlit shader that blends by its alpha");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                track.HideGlow();
            }
        }

        [Test]
        public void An_indicator_listens_for_each_camera_while_switched_on_and_lets_go_as_it_is_switched_off()
        {
            var track = _stage.Node("lens_led").AddComponent<EmissionTrack>();
            int listening = CameraDrawing.Listeners;
            track.ShowGlow();  // its OnEnable's
            track.ShowGlow();
            Assert.That(CameraDrawing.Listeners, Is.EqualTo(listening + 1), "drawn for each camera while switched on, once");
            track.HideGlow();  // its OnDisable's
            Assert.That(CameraDrawing.Listeners, Is.EqualTo(listening), "let go as it is switched off");
            Assert.That(track.Glow, Is.Null);
        }

        [Test]
        public void A_glow_whose_track_was_destroyed_with_no_switching_off_lets_go_at_the_next_camera()
        {
            var material = _stage.Lit("lens_led", new LitSurface { emission = Color.white });
            var model = _stage.Node("Board");
            model.AddComponent<Animator>();
            _stage.Renderer("Lens", model.transform, material);
            var track = _stage.Node("lens_led", model.transform).AddComponent<EmissionTrack>();
            track.material = material;
            track.states.Add(new EmissionState { name = "green", tint = new Color(0f, 1f, 0f), weight = 1f });
            var target = _stage.Made(new RenderTexture(16, 16, 16));
            var camera = _stage.Node("Eye").AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = target;
            int listening = CameraDrawing.Listeners, discs = Discs(), cards = Cards();
            track.ShowGlow();
            track.SetState("green");
            track.Apply();
            camera.Render();
            Assert.That((Discs(), Cards()), Is.EqualTo((discs + 1, cards + 1)), "premise: lit, its card made as a camera drew");
            Object.DestroyImmediate(track);  // an Edit Mode test's: no OnDisable sent
            camera.Render();
            Assert.That(CameraDrawing.Listeners, Is.EqualTo(listening), "let go at the next camera");
            Assert.That((Discs(), Cards()), Is.EqualTo((discs, cards)), "its card with it");
        }
    }
}
