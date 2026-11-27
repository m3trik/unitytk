using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Looks.Tests
{
    /// <summary>A delivery's handoff record read into its model's look.</summary>
    public class LooksImporterTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("delivery");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        static string Record(string rendering) =>
            "{\"version\": " + HandoffBlock.CurrentVersion + ", \"instructions\": \"Read me.\", \"assembly\": [], \"rendering\": " + rendering + "}";

        [Test]
        public void The_records_renderer_is_the_models_look_and_a_setting_left_out_takes_the_policys()
        {
            SceneLook look = LooksImporter.Assemble(_root, Record("{\"renderer\": {\"toneMapping\": \"Linear\", \"toneMappingExposure\": 0.8}}"),
                                                    "Assets/delivery.fbx", new PureRecordSource());
            Assert.That(look, Is.SameAs(_root.GetComponent<SceneLook>()));
            Assert.That((look.toneMapping, look.exposure), Is.EqualTo((LookCurve.Linear, 0.8f)));

            Object.DestroyImmediate(look);
            look = LooksImporter.Assemble(_root, Record("{\"renderer\": {}}"), "Assets/delivery.fbx", new PureRecordSource());
            Assert.That((look.toneMapping, look.exposure),
                        Is.EqualTo((HandoffRendering.Renderer.ToneMappingDefault, HandoffRendering.Renderer.ToneMappingExposureDefault)));
        }

        [Test]
        public void A_record_stating_no_renderer_gives_no_look()
        {
            Assert.That(LooksImporter.Assemble(_root, Record("{\"environment\": {\"intensity\": 1.0}}"), "Assets/delivery.fbx", new PureRecordSource()),
                        Is.Null);
            Assert.That(_root.GetComponent<SceneLook>(), Is.Null);
        }
    }
}
