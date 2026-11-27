using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Indicators.Tests
{
    /// <summary>
    /// <see cref="IndicatorsActions.AddIndicator(GameObject, bool, OpContext)"/>, held to the kernel's three checks: one
    /// Ctrl+Z restores the scene, the dry run's plan names every change, a second run plans nothing.
    /// </summary>
    public class IndicatorsActionsTests
    {
        GameObject _key;

        [SetUp]
        public void SetUp() => _key = new GameObject("KEY");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_key);

        [Test]
        public void An_indicator_is_planned_made_disabled_once_and_undone()
        {
            var dry = new OpContext(dryRun: true);
            Assert.That(IndicatorsActions.AddIndicator(_key, ctx: dry), Is.Null);
            Assert.That(IndicatorsActions.AddIndicator(_key, ctx: dry), Is.Null);
            Assert.That(_key.transform.childCount, Is.Zero, "a dry run makes nothing");
            Indicator made = null;
            OpContext.RunMenu("Point at the key", ctx =>
            {
                made = IndicatorsActions.AddIndicator(_key, ctx: ctx);
                IndicatorsActions.AddIndicator(_key, ctx: ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(made != null && !made.enabled && made.transform.parent == _key.transform);
            var again = new OpContext(dryRun: true);
            Assert.That(IndicatorsActions.AddIndicator(_key, ctx: again), Is.SameAs(made));
            Assert.That(again.Changes, Is.Empty);
            Undo.PerformUndo();
            Assert.That(_key.transform.childCount, Is.Zero, "one Ctrl+Z took it away");
        }

        [Test]
        public void A_marker_is_planned_made_disabled_once_and_undone_and_rings_its_targets_foot()
        {
            var dry = new OpContext(dryRun: true);
            Assert.That(IndicatorsActions.AddMarker(_key, ctx: dry), Is.Null);
            Assert.That(IndicatorsActions.AddMarker(_key, ctx: dry), Is.Null);
            Assert.That(dry.Changes.Select(c => c.target), Is.EqualTo(new[] { "KEY/Marker" }), "planned once");
            Marker made = null;
            OpContext.RunMenu("Mark the key", ctx =>
            {
                made = IndicatorsActions.AddMarker(_key, ctx: ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(made != null && !made.enabled && made.transform.parent == _key.transform);
            Assert.That(IndicatorsActions.AddMarker(_key, ctx: new OpContext(dryRun: true)), Is.SameAs(made));

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(_key.transform, false);
            cube.transform.localPosition = new Vector3(2f, 1.5f, 0f);
            made.Show();
            Assert.That(Vector3.Distance(made.Spot(), new Vector3(2f, 1f, 0f)), Is.LessThan(1e-4f), "under its renderers, at their bottom");
            Assert.That(made.ring.gameObject.activeSelf, Is.True);
            made.Hide();
            Assert.That(made.ring.gameObject.activeSelf, Is.False);
            Undo.PerformUndo();
            Assert.That(_key.transform.Find(IndicatorsActions.MarkerName), Is.Null, "one Ctrl+Z took it away");
        }

        [Test]
        public void An_authored_arrow_is_shown_in_place_hidden_until_then_and_undone()
        {
            var arrow = new GameObject("KEY_ARROW");
            try
            {
                var dry = new OpContext(dryRun: true);
                Assert.That(IndicatorsActions.AddIndicator(_key, arrow.transform, ctx: dry), Is.Null);
                Assert.That(IndicatorsActions.AddIndicator(_key, arrow.transform, ctx: dry), Is.Null, "a second step's ask: its hide planned once");
                Assert.That(arrow.activeSelf && _key.transform.childCount == 0, "a dry run changes nothing");
                Indicator made = null;
                OpContext.RunMenu("Point at the key", ctx =>
                {
                    made = IndicatorsActions.AddIndicator(_key, arrow.transform, ctx: ctx);
                    IndicatorsActions.AddIndicator(_key, arrow.transform, ctx: ctx);
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                });
                Assert.That((made.arrow, made.follow, made.enabled), Is.EqualTo((arrow.transform, false, false)));
                Assert.That(arrow.activeSelf, Is.False, "hidden until its step shows it");
                var again = new OpContext(dryRun: true);
                Assert.That(IndicatorsActions.AddIndicator(_key, arrow.transform, ctx: again), Is.SameAs(made));
                Assert.That(again.Changes, Is.Empty);
                Undo.PerformUndo();
                Assert.That(_key.transform.childCount == 0 && arrow.activeSelf, "one Ctrl+Z took it away and showed the arrow again");
            }
            finally
            {
                Object.DestroyImmediate(arrow);
            }
        }

        [Test]
        public void An_indicator_at_a_place_rests_its_arrow_on_it_pointing_along_it_and_is_undone()
        {
            var place = new GameObject("ARROW_KEY");
            place.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.LookRotation(new Vector3(1f, -1f, 0f)));
            try
            {
                var dry = new OpContext(dryRun: true);
                Assert.That(IndicatorsActions.AddIndicatorAt(_key, place.transform, ctx: dry), Is.Null);
                Assert.That(place.transform.childCount, Is.EqualTo(0), "a dry run changes nothing");
                Indicator made = null;
                OpContext.RunMenu("Point at the key from its place", ctx =>
                {
                    made = IndicatorsActions.AddIndicatorAt(_key, place.transform, ctx: ctx);
                    Assert.That(IndicatorsActions.AddIndicatorAt(_key, place.transform, ctx: ctx), Is.SameAs(made), "a second step's ask: the place's one arrow");
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                });
                Assert.That(made.transform.parent, Is.SameAs(place.transform), "on the place, so it goes where the place is moved");
                Assert.That((made.name, made.follow, made.enabled, made.Target), Is.EqualTo((IndicatorsActions.ChildName, false, false, _key.transform)));
                Assert.That(_key.transform.childCount, Is.EqualTo(0), "nothing on what it points at");
                Assert.That(place.activeSelf, Is.True, "the place is left as it stands");
                made.Show();
                Assert.That(Vector3.Angle(-made.arrow.up, place.transform.forward), Is.LessThan(0.01f), "pointing along the place's forward");
                made.bob = 0f;
                made.Place(0f);
                Assert.That(Vector3.Distance(made.arrow.position, place.transform.position), Is.LessThan(1e-4f), "its tip on the place (it bobs back from there)");
                made.Hide();
                Undo.PerformUndo();
                Assert.That(place.transform.childCount, Is.EqualTo(0), "one Ctrl+Z took it away");
            }
            finally
            {
                Object.DestroyImmediate(place);
            }
        }

        [Test]
        public void Two_targets_of_one_name_get_a_marker_and_an_indicator_each_planned_apart()
        {
            var parent = new GameObject("ROOM");
            var twin = new GameObject("KEY");
            twin.transform.SetParent(parent.transform, false);
            try
            {
                void Run(OpContext ctx)
                {
                    IndicatorsActions.AddMarker(_key, ctx: ctx);
                    IndicatorsActions.AddMarker(twin, ctx: ctx);
                    IndicatorsActions.AddIndicator(_key, ctx: ctx);
                    IndicatorsActions.AddIndicator(twin, ctx: ctx);
                }
                var dry = new OpContext(dryRun: true);
                Run(dry);
                var live = new OpContext();
                Run(live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(dry.Changes.Count, Is.EqualTo(4), "each target's own, by its path");
                Assert.That(twin.GetComponentInChildren<Marker>(true) != null && twin.GetComponentInChildren<Indicator>(true) != null);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void A_marker_and_an_indicator_start_switched_on_and_show_what_they_are_asked()
        {
            Marker marker = IndicatorsActions.AddMarker(_key, ownMesh: true, startEnabled: true);
            Assert.That(marker.enabled && marker.ownMesh, Is.True);
            Indicator follows = IndicatorsActions.AddIndicator(_key, startEnabled: true);
            Assert.That(follows.enabled && follows.follow, Is.True, "the target's own follows its top");
            Indicator placed = IndicatorsActions.AddIndicator(_key, "Indicator S1");
            Assert.That(placed.follow, Is.False, "a named one stays where it is placed unless asked");
            Indicator named = IndicatorsActions.AddIndicator(_key, "Pointer", follow: true);
            Assert.That(named.follow, Is.True, "whatever its name");
        }

        [Test]
        public void A_marker_on_a_planned_object_is_planned_under_the_key_its_live_run_asks()
        {
            var room = new GameObject("ROOM");
            try
            {
                var dry = new OpContext(dryRun: true);
                var planned = new PlannedObject("SPOT", room.transform);
                Assert.That(IndicatorsActions.AddMarker(planned, dry), Is.Null);
                Assert.That(IndicatorsActions.AddMarker(planned, dry), Is.Null);
                var spot = new GameObject("SPOT");
                spot.transform.SetParent(room.transform, false);
                var live = new OpContext();
                IndicatorsActions.AddMarker(spot, ctx: live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))), "planned once, as made");
                Assert.That(IndicatorsActions.AddMarker(planned, new OpContext()), Is.Null, "a live run makes nothing on a planned object");
            }
            finally
            {
                Object.DestroyImmediate(room);
            }
        }

        [Test]
        public void An_indicators_look_is_set_as_planned_once_also_for_one_only_planned()
        {
            const string name = "Indicator S1";
            var dry = new OpContext(dryRun: true);
            IndicatorsActions.AddIndicator(_key, name, ctx: dry);
            Assert.That(IndicatorsActions.SetStyle(_key, name, Color.red, 0.2f, dry), Is.True, "a new one would change: planned");
            Assert.That(IndicatorsActions.SetStyle(_key, name, Color.red, 0.2f, dry), Is.False, "once");
            var live = new OpContext();
            Indicator made = IndicatorsActions.AddIndicator(_key, name, ctx: live);
            IndicatorsActions.SetStyle(_key, name, Color.red, 0.2f, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That((made.color, made.size), Is.EqualTo((Color.red, 0.2f)));
            Assert.That(IndicatorsActions.SetStyle(made, Color.red, 0.2f, new OpContext(dryRun: true)), Is.False, "so already");
            var fresh = new OpContext(dryRun: true);
            IndicatorsActions.AddIndicator(_key, "Indicator S2", ctx: fresh);
            Assert.That(IndicatorsActions.SetStyle(_key, "Indicator S2", Indicator.DefaultColor, Indicator.DefaultSize, fresh), Is.False,
                        "a new one's own look: nothing to change");
            Assert.That(IndicatorsActions.SetStyle(_key, "Nothing", Color.red, 0.2f, new OpContext()), Is.False, "none there: nothing");
        }

        [Test]
        public void Two_same_named_indicators_are_styled_and_taken_away_apart_in_one_dry_run()
        {
            const string name = "Indicator S1";
            var parent = new GameObject("ROOM");
            var twin = new GameObject("KEY");
            twin.transform.SetParent(parent.transform, false);
            GameObject[] targets = { _key, twin };
            try
            {
                var dry = new OpContext(dryRun: true);
                foreach (GameObject target in targets)
                {
                    IndicatorsActions.AddIndicator(target, name, ctx: dry);
                    Assert.That(IndicatorsActions.SetStyle(target, name, Color.red, 0.2f, dry), Is.True, "each its own, only planned");
                }
                var live = new OpContext();
                foreach (GameObject target in targets)
                {
                    IndicatorsActions.AddIndicator(target, name, ctx: live);
                    IndicatorsActions.SetStyle(target, name, Color.red, 0.2f, live);
                }
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));

                var removal = new OpContext(dryRun: true);
                foreach (GameObject target in targets)
                    Assert.That(IndicatorsActions.RemoveIndicator(target, name, removal), Is.True, "each its own");
                Assert.That(removal.Changes.Select(c => c.target), Is.EqualTo(new[] { "KEY/Indicator S1", "ROOM/KEY/Indicator S1" }));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void An_indicator_taken_away_is_planned_once_and_undone()
        {
            Indicator made = IndicatorsActions.AddIndicator(_key, "Indicator S1");
            var dry = new OpContext(dryRun: true);
            Assert.That(IndicatorsActions.RemoveIndicator(_key, "Indicator S1", dry), Is.True);
            Assert.That(IndicatorsActions.RemoveIndicator(_key, "Indicator S1", dry), Is.False, "once");
            Assert.That(made != null, "a dry run takes nothing away");
            OpContext.RunMenu("Take it away", ctx =>
            {
                IndicatorsActions.RemoveIndicator(_key, "Indicator S1", ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(_key.transform.Find("Indicator S1"), Is.Null);
            Assert.That(IndicatorsActions.RemoveIndicator(_key, "Indicator S1"), Is.False, "none there: nothing");
            Undo.PerformUndo();
            Assert.That(_key.transform.Find("Indicator S1"), Is.Not.Null, "one Ctrl+Z put it back");
        }
    }
}
