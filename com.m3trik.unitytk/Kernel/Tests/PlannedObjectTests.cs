using System.Linq;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// A planned object stands in for one a dry run has not made: its parts' keys are the ones its live run
    /// asks with once it is made, so a dry run and its live run plan the same changes.
    /// </summary>
    public class PlannedObjectTests
    {
        [SetUp]
        public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // What an action handed either does: a part on the object there, or the planned part on the stand-in.
        static void AddLight(GameObject node, OpContext ctx)
        {
            string key = OpContextEdits.Key(node, typeof(Light));
            if (node.GetComponent<Light>() != null || ctx.Planned("create", key)) return;
            if (ctx != null && !ctx.ShouldCreate(key, "light it")) return;
            ctx.AddComponent<Light>(node);
        }

        static void AddLight(PlannedObject node, OpContext ctx) => ctx.PlanCreate(OpContextEdits.Key(node, typeof(Light)), "light it");

        [Test]
        public void Its_parts_keys_are_the_ones_its_live_run_asks_with()
        {
            var holder = new GameObject("Holder");
            new GameObject("Spot");  // a top-level object of the name: nothing either run names changes

            var plan = new OpContext(dryRun: true);
            var planned = new PlannedObject("Spot", holder.transform);
            AddLight(planned, plan);
            AddLight(planned, plan);

            var live = new OpContext();
            var made = new GameObject("Spot");
            made.transform.SetParent(holder.transform);
            AddLight(made, live);

            Assert.That(plan.Changes.Select(Said), Is.EqualTo(live.Changes.Select(Said)), "planned once, as made");
            Assert.That(plan.Changes.Single().target, Is.EqualTo("Holder/Spot/Light"));
        }

        [Test]
        public void Two_planned_objects_of_one_name_under_different_parents_plan_apart_as_their_live_run_makes_them()
        {
            var plan = new OpContext(dryRun: true);
            AddLight(new PlannedObject("Spot", new PlannedObject("Left")), plan);
            AddLight(new PlannedObject("Spot", new PlannedObject("Right")), plan);

            var live = new OpContext();
            foreach (string side in new[] { "Left", "Right" })
            {
                var made = new GameObject("Spot");
                made.transform.SetParent(new GameObject(side).transform);
                AddLight(made, live);
            }

            Assert.That(plan.Changes.Select(c => c.target), Is.EqualTo(new[] { "Left/Spot/Light", "Right/Spot/Light" }));
            Assert.That(plan.Changes.Select(Said), Is.EqualTo(live.Changes.Select(Said)), "dry as live");
        }

        [Test]
        public void Its_label_is_its_path_through_planned_parents()
        {
            var holder = new GameObject("Holder");
            var top = new PlannedObject("Group");
            var under = new PlannedObject("Spot", top);
            var placed = new PlannedObject("Spot", holder.transform);
            Assert.That(top.Path, Is.EqualTo("Group"));
            Assert.That(under.Path, Is.EqualTo("Group/Spot"));
            Assert.That(placed.Path, Is.EqualTo("Holder/Spot"));
            Assert.That(placed.ToString(), Is.EqualTo("Holder/Spot"));
            Assert.Throws<System.ArgumentException>(() => new PlannedObject(""));
            Assert.Throws<System.ArgumentNullException>(() => new PlannedObject("Spot", (PlannedObject)null));
        }

        // A change whole: what was asked, on what, saying what.
        static (string, string, string) Said(Change change) => (change.kind, change.target, change.action);
    }
}
