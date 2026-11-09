using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// An emission track on one material: its colour times strength times weight on
    /// every renderer in its scope wearing the material (one twin shared), its map kept;
    /// the shared original where that is the material's own emission; and a Fade over one
    /// of those renderers takes the track's emission into its own twin.
    /// </summary>
    public class EmissionTrackTests
    {
        Stage _stage;

        [SetUp]
        public void SetUp() => _stage = new Stage();

        [TearDown]
        public void TearDown() => _stage.Dispose();

        EmissionTrack Track(Transform scope, Material material, float weight)
        {
            var track = _stage.Node(material.name, scope).AddComponent<EmissionTrack>();
            track.material = material;
            track.color = MaterialColor.Encode(new Color(1f, 0.5f, 0.25f, 1f));
            track.strength = 2f;
            track.weight = weight;
            return track;
        }

        [Test]
        public void A_track_scales_its_materials_emission_and_keeps_its_map()
        {
            var map = _stage.Made(new Texture2D(4, 4) { name = "Signal_Emissive" });
            var material = _stage.Lit("hull_signal", new LitSurface { emission = Color.white, emissionMap = map });
            var other = _stage.Lit("hull");
            var model = _stage.Node("Ship");
            model.AddComponent<Animator>();
            var hull = _stage.Renderer("Hull", model.transform, material, other);
            var mast = _stage.Renderer("Mast", model.transform, material);
            var track = Track(model.transform, material, 0.5f);

            track.Apply();
            Material twin = hull.sharedMaterials[0];
            Assert.That(twin, Is.Not.SameAs(material));
            Assert.That(mast.sharedMaterial, Is.SameAs(twin), "one twin for every renderer wearing the material");
            Assert.That(hull.sharedMaterials[1], Is.SameAs(other), "another material untouched");
            Stage.AssertRgb(_stage.Emission(twin), new Color(1f, 0.5f, 0.25f), "colour x strength 2 x weight 0.5");
            Assert.That(_stage.EmissionMap(twin), Is.SameAs(map), "its map kept");

            track.color = MaterialColor.Encode(Color.white);
            track.strength = 1f;
            track.weight = 1f;
            track.Apply();
            Assert.That(hull.sharedMaterials[0], Is.SameAs(material), "the material's own emission: its shared original");
            Assert.That(hull.HasPropertyBlock(), Is.False);
        }

        [Test]
        public void A_fade_over_a_renderer_takes_the_tracks_emission_into_its_twin()
        {
            var material = _stage.Lit("panel_signal");
            var model = _stage.Node("Ship");
            model.AddComponent<Animator>();
            var door = _stage.Node("Door", model.transform);
            var fade = door.AddComponent<Fade>();
            var panel = _stage.Renderer("Panel", door.transform, material);
            var loose = _stage.Renderer("Loose", model.transform, material);
            var track = Track(model.transform, material, 1f);

            fade.alpha = 0.5f;
            track.Apply();
            Material faded = panel.sharedMaterial;
            Assert.That(faded.name, Does.EndWith("(Blended+Emitting)"));
            Assert.That(_stage.BaseColor(faded).a, Is.EqualTo(0.5f).Within(1e-5f));
            Stage.AssertRgb(_stage.Emission(faded), new Color(2f, 1f, 0.5f), "the track's emission in the Fade's twin");
            Assert.That(loose.sharedMaterial.name, Does.EndWith("(Emitting)"), "the track writes the renderer no Fade covers");
            Assert.That(loose.sharedMaterial, Is.Not.SameAs(faded));
        }
    }
}
