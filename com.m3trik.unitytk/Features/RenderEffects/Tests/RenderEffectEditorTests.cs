using System.IO;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// The inspector's word on unprepared twins and its Prepare Materials button measure
    /// and prepare from one root: an Emission Track's scope, wherever in it the track sits
    /// (an imported one sits under the carrier), so the button clears its own warning.
    /// </summary>
    public class RenderEffectEditorTests
    {
        const string Folder = "Assets/UnitytkRenderEffectEditorTests";

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
        public void Prepare_materials_on_a_track_off_its_scopes_root_clears_its_own_warning()
        {
            var material = _stage.Lit("Hull_Signal");
            var ship = _stage.Node("Ship");
            ship.AddComponent<UnityEngine.Animator>();
            _stage.Renderer("Hull", ship.transform, material);
            var carrier = _stage.Node("data_export", ship.transform);
            var track = _stage.Node("Hull_Signal", carrier.transform).AddComponent<EmissionTrack>();
            track.material = material;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));

            Assert.That(RenderEffectEditor.PreparedFrom(track), Is.SameAs(ship.transform), "a track's scope");
            Assert.That(RenderEffectEditor.Unprepared(track), Does.StartWith("1 twin(s)"), "its Emitting twin is missing");
            Assert.That(RenderEffectEditor.Prepare(track, assetPath: Folder + "/Ship Twins.asset"), Is.GreaterThan(0));
            Assert.That(RenderEffectEditor.Unprepared(track), Is.Null, "the button clears its own warning");
            Assert.That(track.twins.Template(material, TwinState.Emitting), Is.Not.Null, "the twin the warning named");
        }
    }
}
