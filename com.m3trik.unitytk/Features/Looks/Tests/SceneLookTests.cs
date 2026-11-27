using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Looks.Tests
{
    /// <summary>
    /// The look on a scene object: shown through the pipeline's host while enabled, put away when not and around
    /// every save; its weight, quieted effects and cameras its own; a table of its own in place of a named curve.
    /// </summary>
    public class SceneLookTests
    {
        GameObject _model;

        [SetUp]
        public void SetUp() => _model = new GameObject("MODEL");

        [TearDown]
        public void TearDown()
        {
            if (_model != null) Object.DestroyImmediate(_model);
        }

        [Test]
        public void It_shows_through_the_host_while_enabled_and_puts_it_away_after()
        {
            var host = new PureRenderPipelineHost();
            SceneLook look = _model.AddComponent<SceneLook>();
            look.host = host;  // a test's host: production shows through the pipeline drawing now
            look.Show();
            Assert.That(look.Shown, Is.True);
            Assert.That(host.Shown, Has.Count.EqualTo(1));
            Assert.That(host.Shown[0].scale, Is.EqualTo(1f / LookCurve.Brightest));
            Assert.That((host.Shown[0].display.width, host.Shown[0].display.height), Is.EqualTo((host.DisplaySize * host.DisplaySize, host.DisplaySize)),
                        "a table the size the pipeline reads");
            look.enabled = false;
            Assert.That(look.Shown, Is.False);
            Assert.That(host.Shown, Is.Empty, "the pipeline's own look is back");
        }

        [Test]
        public void Its_weight_effects_and_cameras_are_its_own_and_a_table_of_its_own_takes_the_curves_place()
        {
            var host = new PureRenderPipelineHost();
            SceneLook look = _model.AddComponent<SceneLook>();
            Camera camera = _model.AddComponent<Camera>();
            var own = new Texture2D(host.DisplaySize * host.DisplaySize, host.DisplaySize);
            try
            {
                look.priority = 5f;
                look.bloomOff = false;
                look.cameras = new[] { camera };
                look.table = own;
                look.span = 8f;
                look.exposure = 2f;
                look.toneMapping = "AgX";  // a table of its own needs no curve it knows
                look.host = host;  // a test's host: production shows through the pipeline drawing now
                look.Show();
                Assert.That(host.Shown, Has.Count.EqualTo(1));
                var (display, scale, options) = host.Shown[0];
                Assert.That(display, Is.SameAs(own), "its own table");
                Assert.That(scale, Is.EqualTo(2f / 8f), "the exposure on the way in, over the span");
                Assert.That((options.priority, options.bloomOff, options.vignetteOff), Is.EqualTo((5f, false, true)));
                Assert.That(options.cameras, Is.EqualTo(new[] { camera }));
                look.enabled = false;
                Assert.That(own != null, Is.True, "its own table is never destroyed");
            }
            finally
            {
                Object.DestroyImmediate(own);
            }
        }

        [Test]
        public void A_look_in_a_scene_the_editor_only_previews_is_never_shown()
        {
            // A model's import builds it in a preview scene: nothing is shown there, nor said.
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var model = new GameObject("IMPORTED");
                SceneManager.MoveGameObjectToScene(model, preview);
                var host = new PureRenderPipelineHost();
                SceneLook look = model.AddComponent<SceneLook>();
                look.host = host;  // a test's host: production shows through the pipeline drawing now
                look.Show();
                Assert.That(host.Shown, Is.Empty);
                Assert.That(look.Shown, Is.False);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [Test]
        public void A_pipeline_that_reads_no_table_shows_nothing_and_each_copy_says_nothing()
        {
            SceneLook look = _model.AddComponent<SceneLook>();
            look.host = RenderPipelineHosts.BuiltIn;  // a test's host: production shows through the pipeline drawing now
            look.Show();  // its import says so once; a placed copy is quiet
            Assert.That(look.Shown, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void A_save_puts_it_away_and_shows_it_again_once_saved()
        {
            const string folder = "Assets/UnitytkSceneLookTests";
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var model = new GameObject("MODEL");
            var host = new PureRenderPipelineHost();
            SceneLook look = model.AddComponent<SceneLook>();
            look.host = host;  // a test's host: production shows through the pipeline drawing now
            look.Show();
            int whileSaving = -1;
            void Saving(Scene saved, string path) => whileSaving = host.Shown.Count;
            EditorSceneManager.sceneSaving += Saving;  // after the kernel's save hook: it sees what the file gets
            try
            {
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "UnitytkSceneLookTests");
                Assert.That(EditorSceneManager.SaveScene(scene, folder + "/Look.unity"), Is.True);
                Assert.That(whileSaving, Is.EqualTo(0), "put away while the scene is written");
                Assert.That(host.Shown, Has.Count.EqualTo(1), "shown again once saved, through the same host");
                Assert.That(look.Shown, Is.True);
            }
            finally
            {
                EditorSceneManager.sceneSaving -= Saving;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void A_tone_mapping_it_does_not_show_is_said_and_shows_nothing()
        {
            var host = new PureRenderPipelineHost();
            SceneLook look = _model.AddComponent<SceneLook>();
            look.toneMapping = "AgX";
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not one unitytk shows"));
            look.host = host;  // a test's host: production shows through the pipeline drawing now
            look.Show();
            Assert.That(host.Shown, Is.Empty);
        }
    }
}
