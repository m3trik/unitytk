using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.RenderEffects;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// <see cref="SetBehavior"/> (the user, 2026-11-24: "use the manifest behavior for emissive state
    /// changes"): a row "The solenoid LED turns amber" puts a switch to amber on the Emission Track that shows the
    /// LED's material, which its step enables; a state the track lacks, or a target no track shows, is said.
    /// </summary>
    public class SetBehaviorTests
    {
        GameObject _root;
        Material _material;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_material != null) Object.DestroyImmediate(_material);
            var made = GameObject.Find("Task");
            if (made != null) Object.DestroyImmediate(made);
        }

        static SheetPlan.Shape.Payload Plan(string state, string target = "SOLENOID_LED")
        {
            var plan = SheetPlan.Read("{\"version\": 3, \"steps\": [{\"id\": \"A01\", \"actions\": [" +
                                      $"{{\"behavior\": \"set\", \"text\": \"The solenoid LED turns {state}\", \"targets\": [\"{target}\"], " +
                                      $"\"settings\": {{\"state\": \"{state}\"}}}}]}}]}}", "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            return plan;
        }

        EmissionTrack Board()
        {
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            _material = new Material(Shader.Find(host.LitShader)) { name = "PCB_LED_MAT_solenoid" };
            MaterialWriter.Write(_material, new LitSurface { emission = Color.white }, host);
            _root = new GameObject("board_2026");
            var led = GameObject.CreatePrimitive(PrimitiveType.Cube);
            led.name = "SOLENOID_LED";
            led.transform.SetParent(_root.transform);
            led.GetComponent<Renderer>().sharedMaterial = _material;
            var bare = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bare.name = "PANEL";
            bare.transform.SetParent(_root.transform);
            var carrier = new GameObject("data_export");
            carrier.transform.SetParent(_root.transform);
            var track = carrier.AddComponent<EmissionTrack>();
            track.material = _material;
            track.states.Add(new EmissionState { name = "off", tint = Color.white, weight = 0f });
            track.states.Add(new EmissionState { name = "amber", tint = new Color(1f, 0.5f, 0f), weight = 1f });
            return track;
        }

        [Test]
        public void A_row_switches_its_LED_to_its_state_as_its_step_starts()
        {
            EmissionTrack track = Board();
            Assert.That(RenderEffectsActions.TrackOf(_root.transform.Find("SOLENOID_LED").gameObject), Is.SameAs(track), "the track showing its material");
            Assert.That(RenderEffectsActions.TrackOf(_root.transform.Find("PANEL").gameObject), Is.Null, "a part no track shows");

            SheetPlan.Shape.Payload plan = Plan("amber");
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, new ManifestScene(_root), dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_root), live);
            Assert.That(live.Issues, Is.Empty);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var change = track.GetComponents<EmissionStateSwitch>().Single();
            Assert.That(change.state, Is.EqualTo("amber"));
            Assert.That(change.enabled, Is.False, "off until its step enables it");
            Assert.That(change.Switch(), Is.True);  // what its step's enabling it does as the step starts
            Assert.That(track.weight, Is.EqualTo(1f));
            Assert.That(track.tint, Is.EqualTo(new Color(1f, 0.5f, 0f)));
        }

        [Test]
        public void A_state_the_LED_lacks_or_a_part_that_emits_nothing_is_said()
        {
            Board();
            var behavior = new SetBehavior();
            SheetPlan.Shape.PlanAction purple = Plan("purple").steps[0].actions[0];
            Assert.That(behavior.Check(purple, new ManifestScene(_root)), Has.Some.Contains("no state 'purple'").And.Some.Contains("off, amber"));
            SheetPlan.Shape.PlanAction panel = Plan("amber", "PANEL").steps[0].actions[0];
            Assert.That(behavior.Check(panel, new ManifestScene(_root)), Has.Some.Contains("wears no material that emits"));
        }

        [Test]
        public void A_part_someone_gave_an_emissive_material_by_hand_switches_by_name_with_no_delivered_track()
        {
            // The kit switched states only where a delivery's record (the authoring tool's Emissive Groups) had made the
            // track: a part with an emission of its own, authored by hand, had "no states".
            EmissionTrack delivered = Board();
            Object.DestroyImmediate(delivered.gameObject);  // no record came: nothing made a track
            GameObject led = _root.transform.Find("SOLENOID_LED").gameObject;
            Assert.That(RenderEffectsActions.TrackOf(led), Is.Null);
            Assert.That(RenderEffectsActions.Emitting(led), Is.SameAs(_material), "it emits: a track can be made for it");
            Assert.That(RenderEffectsActions.Emitting(_root.transform.Find("PANEL").gameObject), Is.Null);
            var behavior = new SetBehavior();
            Assert.That(behavior.Check(Plan("amber").steps[0].actions[0], new ManifestScene(_root)), Is.Empty, "one of the default states");
            Assert.That(behavior.Check(Plan("purple").steps[0].actions[0], new ManifestScene(_root)),
                        Has.Some.Contains("no state 'purple'").And.Some.Contains("off, green, blue, amber, red"));

            SheetPlan.Shape.Payload plan = Plan("amber");
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, new ManifestScene(_root), dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_root), live);
            Assert.That(live.Issues, Is.Empty);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))), "the plan is the run");
            EmissionTrack track = RenderEffectsActions.TrackOf(led);
            Assert.That(track, Is.Not.Null, "the build made its track");
            Assert.That(track.name, Is.EqualTo(RenderEffectsActions.TrackName(_material.name)));
            Assert.That(track.transform.parent, Is.SameAs(_root.transform), "under the delivery's root: its scope");
            Assert.That(track.material, Is.SameAs(_material));
            Assert.That((track.weight, track.tint, track.strength), Is.EqualTo((1f, Color.white, 1f)), "as the material stands: nothing changes until a state says so");
            Assert.That(track.states.Select(s => s.name), Is.EqualTo(new[] { "off", "green", "blue", "amber", "red" }), "the authoring tool's default states");
            var change = track.GetComponents<EmissionStateSwitch>().Single();
            Assert.That((change.state, change.enabled), Is.EqualTo(("amber", false)));
            Assert.That(change.Switch(), Is.True);
            Assert.That((track.weight, track.tint), Is.EqualTo((1f, new Color(1f, 0.5f, 0f, 1f))));
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, new ManifestScene(_root), again);
            Assert.That(again.Changes, Is.Empty, "a second build changes nothing");

            // An emission that is mapped can only be dimmed: off and on.
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            var map = new Texture2D(2, 2) { name = "LED_Emissive" };
            try
            {
                _material.SetTexture(host.Lit.EmissionMap, map);
                Assert.That(RenderEffectsActions.DefaultStates(_material).Select(s => s.name), Is.EqualTo(new[] { "off", "on" }));
            }
            finally
            {
                _material.SetTexture(host.Lit.EmissionMap, null);
                Object.DestroyImmediate(map);
            }
        }
    }
}
