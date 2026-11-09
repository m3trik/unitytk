using System.Collections;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// The effects in Play Mode, where Unity drives them: a disabled effect gives its
    /// renderers back at once (what it hid shown) and shows again when enabled; the end of
    /// each frame rebuilds what changed (a renderer added as a root's child, one moved
    /// between roots, one deeper after <see cref="RenderEffect.Refresh"/>); a track hands
    /// its renderer to a Fade added over it and takes it back when the Fade goes; a nested
    /// Fade enabled mid-play multiplies its parent's alpha, and writes alone when its parent
    /// is off; of two tracks on one material in a scope the first plays, the second once
    /// the first is off, and two scopes play their own; and a destroyed effect, or its
    /// object, gives everything back and frees its twins.
    /// </summary>
    public class RenderEffectTests
    {
        Stage _stage;

        [SetUp]
        public void SetUp() => _stage = new Stage();

        [TearDown]
        public void TearDown() => _stage.Dispose();

        // The end of a frame: every effect's LateUpdate has written.
        static object Frame() => null;

        (Fade fade, MeshRenderer renderer, Material original) Faded(string name, float alpha)
        {
            var material = _stage.Lit(name + "_MAT");
            var root = _stage.Node(name);
            var fade = root.AddComponent<Fade>();
            fade.alpha = alpha;
            return (fade, _stage.Renderer(name + "_Body", root.transform, material), material);
        }

        EmissionTrack Track(Transform scope, string name, Material material, Color color, float weight)
        {
            var track = _stage.Node(name, scope).AddComponent<EmissionTrack>();
            track.material = material;
            track.color = MaterialColor.Encode(color);
            track.weight = weight;
            return track;
        }

        GameObject Model(string name)
        {
            var model = _stage.Node(name);
            model.AddComponent<Animator>();
            return model;
        }

        float Alpha(Renderer renderer) => _stage.BaseColor(renderer.sharedMaterial).a;

        [UnityTest]
        public IEnumerator A_disabled_effect_gives_its_renderers_back_at_once_and_shows_again_when_enabled()
        {
            var (fade, renderer, material) = Faded("Crate", 0.5f);
            yield return Frame();
            Material twin = renderer.sharedMaterial;
            Assert.That(twin.name, Does.EndWith("(Blended)"), "below 1 at the frame's end: its twin");
            fade.alpha = 0f;
            yield return Frame();
            Assert.That(renderer.forceRenderingOff, Is.True, "at 0: hidden");

            fade.enabled = false;
            Assert.That(renderer.forceRenderingOff, Is.False, "disabled: shown again at once");
            Assert.That(renderer.sharedMaterial, Is.SameAs(material), "on its material as authored");
            yield return Frame();
            Assert.That(twin == null, Is.True, "its twin freed");

            fade.alpha = 0.5f;
            fade.enabled = true;
            yield return Frame();
            Assert.That(renderer.sharedMaterial.name, Does.EndWith("(Blended)"), "enabled: its twin again");
            Assert.That(Alpha(renderer), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [UnityTest]
        public IEnumerator Each_frame_rebuilds_what_changed_under_a_root()
        {
            var (fade, hall, material) = Faded("Building", 0.5f);
            var annex = Faded("Annex", 0f).fade;
            yield return Frame();
            Assert.That(hall.sharedMaterial.name, Does.EndWith("(Blended)"));

            var shelf = _stage.Renderer("Shelf", fade.transform, material);
            yield return Frame();
            Assert.That(shelf.sharedMaterial, Is.SameAs(hall.sharedMaterial), "a child added: on the root's one twin");

            var wing = _stage.Node("Wing", fade.transform);
            yield return Frame();
            var door = _stage.Renderer("Door", wing.transform, material);
            yield return Frame();
            Assert.That(door.sharedMaterial, Is.SameAs(material), "added deeper than its children: Unity tells the root nothing");
            fade.Refresh();
            yield return Frame();
            Assert.That(door.sharedMaterial, Is.SameAs(hall.sharedMaterial), "after Refresh: covered");

            // Moved under another root, which shows it before the frame's end (a script's Apply):
            // it takes the material as authored, never the twin its first root still has on it.
            wing.transform.SetParent(annex.transform, false);
            annex.Apply();
            yield return Frame();
            Assert.That(door.forceRenderingOff, Is.True, "under the root at 0: hidden");
            Assert.That(door.sharedMaterial, Is.SameAs(material), "its first root let it go");
            annex.alpha = 0.5f;
            yield return Frame();
            Assert.That(door.sharedMaterial.name, Is.EqualTo(material.name + " (Blended)"), "the moved renderer's twin is its own material's");
            Assert.That(Alpha(door), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(fade.writer.HoldsUnder(wing.transform), Is.False, "its first root holds it no more");
        }

        [UnityTest]
        public IEnumerator A_track_hands_its_renderer_to_a_fade_added_over_it_and_takes_it_back()
        {
            var material = _stage.Lit("Panel_Signal");
            var ship = Model("Ship");
            var door = _stage.Node("Door", ship.transform);
            var panel = _stage.Renderer("Panel", door.transform, material);
            var track = Track(ship.transform, "Panel_Signal", material, new Color(1f, 0.5f, 0.25f, 1f), 0.5f);
            yield return Frame();
            Assert.That(panel.sharedMaterial.name, Does.EndWith("(Emitting)"));
            Assert.That(track.writer.HoldsUnder(door.transform), Is.True, "the track writes it");

            var fade = door.AddComponent<Fade>();
            yield return Frame();
            Assert.That(fade.writer, Is.Not.Null, "the Fade over it writes it now");
            Assert.That(track.writer == null || !track.writer.HoldsUnder(door.transform), Is.True, "the track let it go");
            Assert.That(panel.sharedMaterial.name, Does.EndWith("(Emitting)"), "the track's emission in the Fade's own twin");
            Stage.AssertRgb(_stage.Emission(panel.sharedMaterial), new Color(0.5f, 0.25f, 0.125f), "colour x weight 0.5");
            fade.alpha = 0.5f;
            yield return Frame();
            Assert.That(panel.sharedMaterial.name, Does.EndWith("(Blended+Emitting)"));

            fade.enabled = false;
            yield return Frame();
            Assert.That(panel.sharedMaterial.name, Does.EndWith("(Emitting)"), "the Fade off: the track takes it back");
            Assert.That(track.writer.HoldsUnder(door.transform), Is.True);
            Stage.AssertRgb(_stage.Emission(panel.sharedMaterial), new Color(0.5f, 0.25f, 0.125f), "its emission kept");
        }

        [UnityTest]
        public IEnumerator A_nested_fade_enabled_mid_play_multiplies_and_writes_alone_when_its_parent_is_off()
        {
            var material = _stage.Lit("Wall_MAT");
            var (outer, hall, _) = Faded("Building", 0.5f);
            var wing = _stage.Node("Wing", outer.transform);
            var inner = wing.AddComponent<Fade>();
            inner.alpha = 0.5f;
            inner.enabled = false;
            var door = _stage.Renderer("Door", wing.transform, material);
            yield return Frame();
            Assert.That(Alpha(door), Is.EqualTo(0.5f).Within(1e-5f), "the outer alone");

            inner.enabled = true;
            yield return Frame();
            Assert.That(Alpha(door), Is.EqualTo(0.25f).Within(1e-5f), "enabled: times its parent's");
            Assert.That(Alpha(hall), Is.EqualTo(0.5f).Within(1e-5f), "its parent's own renderers as they were");
            Assert.That((outer.writer != null, inner.writer == null), Is.EqualTo((true, true)), "the outermost writes");

            outer.enabled = false;
            yield return Frame();
            Assert.That(Alpha(door), Is.EqualTo(0.5f).Within(1e-5f), "its parent off: its own alpha");
            Assert.That(inner.writer, Is.Not.Null, "the nested Fade is the root now");
            Assert.That(hall.sharedMaterial.name, Does.Not.Contain("("), "its parent's renderers as authored");

            inner.enabled = false;
            yield return Frame();
            Assert.That(door.sharedMaterial, Is.SameAs(material), "both off: as authored");
        }

        [UnityTest]
        public IEnumerator Of_two_tracks_on_a_material_in_a_scope_the_first_plays_and_two_scopes_play_their_own()
        {
            var lamp = _stage.Lit("Lamp_Signal");
            var ship = Model("Ship");
            var bulb = _stage.Renderer("Bulb", ship.transform, lamp);
            var first = Track(ship.transform, "First", lamp, new Color(1f, 0f, 0f, 1f), 1f);
            Track(ship.transform, "Second", lamp, new Color(0f, 0f, 1f, 1f), 1f);
            yield return Frame();
            Stage.AssertRgb(_stage.Emission(bulb.sharedMaterial), new Color(1f, 0f, 0f), "the first plays");
            first.enabled = false;
            yield return Frame();
            Stage.AssertRgb(_stage.Emission(bulb.sharedMaterial), new Color(0f, 0f, 1f), "the first off: the second plays");

            var hull = _stage.Lit("Hull_Signal");
            var a = Model("ShipA");
            var b = Model("ShipB");
            var hullA = _stage.Renderer("HullA", a.transform, hull);
            var hullB = _stage.Renderer("HullB", b.transform, hull);
            Track(a.transform, "Hull_Signal", hull, Color.white, 1f);
            Track(b.transform, "Hull_Signal", hull, Color.white, 0.25f);
            yield return Frame();
            Stage.AssertRgb(_stage.Emission(hullA.sharedMaterial), Color.white, "each scope its own track");
            Stage.AssertRgb(_stage.Emission(hullB.sharedMaterial), new Color(0.25f, 0.25f, 0.25f), "each scope its own track");
            Assert.That(hullA.sharedMaterial, Is.Not.SameAs(hullB.sharedMaterial), "a twin per scope");
        }

        [UnityTest]
        public IEnumerator A_destroyed_effect_or_its_object_gives_everything_back_and_frees_its_twins()
        {
            var (hidden, hiddenRenderer, hiddenMaterial) = Faded("Hidden", 0f);
            var (faded, fadedRenderer, _) = Faded("Faded", 0.5f);
            yield return Frame();
            Assert.That(hiddenRenderer.forceRenderingOff, Is.True);
            Material twin = fadedRenderer.sharedMaterial;
            Assert.That(twin.name, Does.EndWith("(Blended)"));

            Object.Destroy(hidden);
            Object.Destroy(faded.gameObject);
            yield return Frame();
            Assert.That(hiddenRenderer.forceRenderingOff, Is.False, "its Fade destroyed: shown");
            Assert.That(hiddenRenderer.sharedMaterial, Is.SameAs(hiddenMaterial));
            yield return Frame();
            Assert.That(twin == null, Is.True, "the twins of a destroyed object's Fade freed");
        }
    }
}
