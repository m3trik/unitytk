using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Placement.Tests
{
    /// <summary>
    /// <see cref="Placements"/> places a spot, planned first, converging, one undo step, and keeps a person's
    /// adjustment from build to build (captured relative to the part it is placed by, into what the build's caller keeps).
    /// </summary>
    public class PlacementsTests : RoomFixture
    {
        GameObject Node(string name, Transform parent = null)
        {
            var node = new GameObject(name);
            node.transform.SetParent(parent != null ? parent : Room.transform);
            return node;
        }

        static readonly Pose Spot = new Pose(new Vector3(0, 0, 2), Quaternion.Euler(0, 180, 0));

        [Test]
        public void A_helper_someone_made_is_never_moved_to_be_marked_and_moved_by_a_person_is_an_adjustment()
        {
            // A viewpoint an artist made, moved in the scene, was kept by nothing the manifest knew: not listed, not carried
            // to a new scene, with nothing to put it back.
            var parent = new GameObject("HELPERS");
            var view = new GameObject("VIEW_B07");
            try
            {
                view.transform.SetParent(parent.transform, false);
                view.transform.localPosition = new Vector3(1f, 0f, 2f);
                var dry = new OpContext(dryRun: true);
                Assert.That(Placements.PlaceGiven(view, "where/VIEW_B07", null, dry), Is.Null, "a dry run plans it");
                var live = new OpContext();
                PlacedSpot spot = Placements.PlaceGiven(view, "where/VIEW_B07", null, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.action))));
                Assert.That(view.transform.localPosition, Is.EqualTo(new Vector3(1f, 0f, 2f)), "marked where it stands: nothing moves it");
                Assert.That(spot.Adjusted, Is.False);
                Assert.That(Placements.PlaceGiven(view, "where/VIEW_B07", null, new OpContext(dryRun: true)), Is.SameAs(spot));

                view.transform.position += new Vector3(0.5f, 0f, 0f);  // a person moves it nearer
                Vector3 moved = view.transform.position;
                Assert.That(spot.Adjusted, "moved from where its maker has it: an adjustment");
                var kept = new Placements.Tuning();
                Assert.That(Placements.Capture(kept, new[] { parent }), Is.True);
                var again = new OpContext(dryRun: true);
                Placements.PlaceGiven(view, "where/VIEW_B07", kept, again);
                Assert.That(again.Changes, Is.Empty, "a build leaves it where the person put it");
                Assert.That(view.transform.position, Is.EqualTo(moved));

                // A scene built anew has it where its maker made it: the kept adjustment puts it where the person had it.
                view.transform.localPosition = new Vector3(1f, 0f, 2f);
                Object.DestroyImmediate(spot);
                Placements.PlaceGiven(view, "where/VIEW_B07", kept, new OpContext());
                Assert.That(Vector3.Distance(view.transform.position, moved), Is.LessThan(1e-5f), "carried");
                Assert.That(Placements.Restore(view.GetComponent<PlacedSpot>(), new OpContext()), Is.True);
                Assert.That(view.transform.localPosition, Is.EqualTo(new Vector3(1f, 0f, 2f)), "and put back where its maker has it");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void A_helper_its_maker_moved_since_a_person_adjusted_it_keeps_the_adjustment_and_says_so()
        {
            // The real task's dials camera was moved in Unity, then framed in the DCC: the next delivery's framing was
            // overruled without a word, until the Unity adjustment was found and reverted by hand.
            const string Path = "Assets/PlacementsTests given helper.prefab";
            var made = new GameObject("DELIVERY");
            var cam = new GameObject("CAM_B02");
            cam.transform.SetParent(made.transform, false);
            cam.transform.localPosition = new Vector3(1f, 0f, 2f);
            GameObject placed = null;
            try
            {
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(made, Path);
                placed = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                Transform helper = placed.transform.Find("CAM_B02");
                Placements.PlaceGiven(helper.gameObject, "where/CAM_B02", null, new OpContext());
                helper.position += new Vector3(0.5f, 0f, 0f);  // a person's adjustment, here
                Vector3 theirs = helper.position;
                var kept = new Placements.Tuning();
                Placements.Capture(kept, new[] { placed });
                var quiet = new OpContext();
                Placements.PlaceGiven(helper.gameObject, "where/CAM_B02", kept, quiet);
                Assert.That(quiet.Issues.Where(i => i.code == Placements.MovedCode), Is.Empty, "nothing moved upstream: nothing to say");

                // The delivery comes again with its maker's camera framed elsewhere.
                GameObject contents = PrefabUtility.LoadPrefabContents(Path);
                contents.transform.Find("CAM_B02").localPosition = new Vector3(1f, 0.3f, 2.4f);
                PrefabUtility.SaveAsPrefabAsset(contents, Path);
                PrefabUtility.UnloadPrefabContents(contents);
                var build = new OpContext();
                Placements.PlaceGiven(helper.gameObject, "where/CAM_B02", kept, build);
                Assert.That(Vector3.Distance(helper.position, theirs), Is.LessThan(1e-5f), "the person's adjustment still wins");
                Assert.That(build.Issues.Single(i => i.code == Placements.MovedCode).message,
                            Does.Contain("moved 'CAM_B02' since it was adjusted here").And.Contain("Revert"));
                Assert.That(Placements.Restore(helper.GetComponent<PlacedSpot>(), new OpContext()), Is.True);
                Assert.That(Vector3.Distance(helper.localPosition, new Vector3(1f, 0.3f, 2.4f)), Is.LessThan(1e-5f), "a revert takes the delivery's new pose");
            }
            finally
            {
                Object.DestroyImmediate(made);
                if (placed != null) Object.DestroyImmediate(placed);
                AssetDatabase.DeleteAsset(Path);
            }
        }

        [Test]
        public void A_moved_spot_is_put_back_where_its_build_placed_it()
        {
            GameObject node = Node("VIEW");
            PlacedSpot spot = Placements.Place(node, "teleport/A01", Part, Spot, null, new OpContext());
            Assert.That(Placements.Restore(spot, new OpContext()), Is.False, "where it was placed: nothing to put back");
            node.transform.position += Vector3.right * 0.5f;
            node.transform.rotation = Quaternion.Euler(0f, 40f, 0f) * node.transform.rotation;
            var plan = new OpContext(dryRun: true);
            Assert.That(Placements.Restore(spot, plan), Is.True);
            Assert.That(spot.Adjusted, Is.True, "a plan moves nothing");
            Assert.That(plan.Changes.Count, Is.EqualTo(1));
            Assert.That(Placements.Restore(spot, new OpContext()), Is.True);
            Assert.That(spot.Adjusted, Is.False);
            Assert.That(Vector3.Distance(node.transform.position, Spot.position), Is.LessThan(1e-4f));
        }

        [Test]
        public void A_spot_is_placed_planned_first_once_and_keeps_its_adjustment()
        {
            GameObject node = Node("VIEW");
            var plan = new OpContext(dryRun: true);
            Assert.That(Placements.Place(node, "teleport/A01", Part, Spot, null, plan), Is.Null, "a plan makes nothing");
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "change" }), "the spot, and the move");
            Assert.That(node.GetComponent<PlacedSpot>() == null && node.transform.position == Vector3.zero);

            var live = new OpContext();
            PlacedSpot spot = Placements.Place(node, "teleport/A01", Part, Spot, null, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(node.transform.position, Is.EqualTo(Spot.position));
            Assert.That(spot.Adjusted, Is.False);
            var again = new OpContext(dryRun: true);
            Placements.Place(node, "teleport/A01", Part, Spot, null, again);
            Assert.That(again.Changes, Is.Empty, "placed already: nothing to do");

            // A person moves it half a metre; the capture keeps that, relative to the part.
            node.transform.position += Vector3.right * 0.5f;
            var kept = new Placements.Tuning();
            Assert.That(Placements.Capture(kept, null), Is.True, "changed: the caller keeps it again");
            Assert.That(kept.spots.Count, Is.EqualTo(1));
            Assert.That(kept.spots[0].key, Is.EqualTo("teleport/A01"));

            // A new delivery moves the part: the next build puts the spot where it was adjusted to, beside the part.
            Part.position += Vector3.forward;
            node.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Placements.Place(node, "teleport/A01", Part, new Pose(Spot.position + Vector3.forward, Spot.rotation), kept, new OpContext());
            Assert.That(Vector3.Distance(node.transform.position, new Vector3(0.5f, 0, 3)), Is.LessThan(1e-4f));
            Assert.That(spot.Adjusted, Is.True);

            // Moved back where the build puts it, the capture drops the adjustment.
            node.transform.position = Spot.position + Vector3.forward;
            Assert.That(Placements.Capture(kept, null), Is.True);
            Assert.That(kept.spots, Is.Empty);
        }

        [Test]
        public void A_spot_keeps_its_first_placement_following_its_part_until_it_is_placed_again()
        {
            // The search for a first spot runs once: a build whose search found another spot (the scene changed, a viewpoint
            // moved) moved a spot nobody asked to move (2026-11-26).
            GameObject node = Node("VIEW");
            Placements.Place(node, "teleport/A01", Part, Spot, null, new OpContext());
            var elsewhere = new Pose(new Vector3(1.5f, 0f, 1f), Quaternion.identity);
            var plan = new OpContext(dryRun: true);
            Placements.Place(node, "teleport/A01", Part, elsewhere, null, plan);
            Assert.That(plan.Changes, Is.Empty, "placed before: its first placement holds");
            Assert.That(node.transform.position, Is.EqualTo(Spot.position));

            Part.position += Vector3.forward;  // a new delivery moves the part: the spot follows it
            Placements.Place(node, "teleport/A01", Part, elsewhere, null, new OpContext());
            Assert.That(Vector3.Distance(node.transform.position, Spot.position + Vector3.forward), Is.LessThan(1e-4f));

            PlacedSpot spot = Placements.PlacedAs(node, "teleport/A01", Part);
            Assert.That(spot, Is.Not.Null);
            Assert.That(Placements.PlacedAs(node, "teleport/B01", Part), Is.Null, "another key: none");
            var again = new OpContext(dryRun: true);
            Assert.That(Placements.PlaceAgain(spot, again), Is.True);
            Assert.That(again.Changes.Single().kind, Is.EqualTo("delete"), "asked");
            Assert.That(node.GetComponent<PlacedSpot>() != null, "a dry run takes nothing off");
            OpContext.RunMenu("Place Again", ctx => Placements.PlaceAgain(spot, ctx));
            Assert.That(node.GetComponent<PlacedSpot>() == null);
            Placements.Place(node, "teleport/A01", Part, elsewhere, null, new OpContext());
            Assert.That(Vector3.Distance(node.transform.position, elsewhere.position), Is.LessThan(1e-4f), "placed anew where the search says");
        }

        [Test]
        public void A_spot_placed_anew_where_it_stands_asks_only_what_changes()
        {
            // Its key renamed with the node already where the build puts it: the plan said it would move it there too.
            GameObject node = Node("VIEW");
            Placements.Place(node, "teleport/A01", Part, Spot, null, new OpContext());
            var plan = new OpContext(dryRun: true);
            Placements.Place(node, "teleport/B01", Part, Spot, null, plan);
            Assert.That(plan.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(new[] { ("change", "place it as 'teleport/B01'") }), "no move: it is there");
            var live = new OpContext();
            PlacedSpot spot = Placements.Place(node, "teleport/B01", Part, Spot, null, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(spot.key, Is.EqualTo("teleport/B01"));

            node.transform.position += Vector3.right;  // a person's move; the spot as placed
            plan = new OpContext(dryRun: true);
            Placements.Place(node, "teleport/B01", Part, Spot, null, plan);
            Assert.That(plan.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(new[] { ("change", "move it there") }), "the spot as it is: the move alone");
            live = new OpContext();
            Placements.Place(node, "teleport/B01", Part, Spot, null, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(node.transform.position, Is.EqualTo(Spot.position));
        }

        [Test]
        public void Placing_a_fresh_spot_is_one_undo_step_its_move_too()
        {
            GameObject node = Node("VIEW");
            OpContext.RunMenu("Place", ctx => Placements.Place(node, "teleport/A01", Part, Spot, null, ctx));
            Assert.That(node.transform.position, Is.EqualTo(Spot.position));
            Undo.PerformUndo();
            Assert.That(node.GetComponent<PlacedSpot>() == null, "the spot is gone");
            Assert.That(node.transform.position, Is.EqualTo(Vector3.zero), "and the node back where it stood: its move was asked");
        }

        [Test]
        public void A_dry_run_of_a_node_it_only_planned_plans_what_the_live_run_does()
        {
            // Two nodes of one name under two parents, as each picture's "From": told apart by their paths.
            Transform first = Node("B01").transform, second = Node("B02").transform;
            var dry = new OpContext(dryRun: true);
            foreach (Transform parent in new[] { first, second })
                Placements.Place(new PlannedObject("From", parent), $"pip/{parent.name}", dry);
            Placements.Place(new PlannedObject("From", first), "pip/B01", dry);  // planned once
            var live = new OpContext();
            foreach (Transform parent in new[] { first, second })
                Placements.Place(Node("From", parent), $"pip/{parent.name}", Part, Spot, null, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(dry.Changes.Count(c => c.kind == "create"), Is.EqualTo(2), "a spot for each");
            Assert.That(OpContext.RunMenu("Again", ctx => Placements.Place(new PlannedObject("From", first), "pip/B01", ctx)), Is.Null,
                        "a live run has nothing to place on a planned node");
        }

        [Test]
        public void A_kept_spot_reads_back_as_it_was_written_short_and_a_text_that_is_none_reads_as_none()
        {
            var tuned = new Placements.Tuned { key = "teleport/A01", relativeTo = "PART", position = new Vector3(0.5f, 0f, 0.1f), rotation = Quaternion.Euler(0f, 90f, 0f) };
            string text = Placements.Write(tuned);
            Assert.That(text, Does.Contain("0.5").And.Contain("0.1").And.Not.Contain("0.10000000149"), "floats written as short as they read");
            Assert.That(Placements.TryRead("teleport/A01", text, out Placements.Tuned back), Is.True);
            Assert.That((back.key, back.relativeTo, back.position), Is.EqualTo((tuned.key, tuned.relativeTo, tuned.position)));
            Assert.That(PlacedSpot.Same(back.rotation, tuned.rotation), Is.True);
            Assert.That(Placements.TryRead("teleport/A01", "{\"relativeTo\": \"PART\"}", out _), Is.False, "no place in it");
            Assert.That(Placements.TryRead("teleport/A01", "not json", out _), Is.False);
            Assert.That(Placements.TryRead("", text, out _), Is.False, "no key");
        }

        [Test]
        public void A_capture_scoped_to_one_builds_roots_keeps_only_its_spots()
        {
            // Two builds in one scene, their spots keyed alike, each with its own store: each keeps only its own.
            GameObject a = Node("A"), b = Node("B");
            GameObject viewA = Node("VIEW", a.transform), viewB = Node("VIEW", b.transform);
            Placements.Place(viewA, "teleport/A01", Part, Spot, null, new OpContext());
            Placements.Place(viewB, "teleport/A01", Part, Spot, null, new OpContext());
            viewB.transform.position += Vector3.right;  // B's adjusted, A's not
            var keptA = new Placements.Tuning();
            keptA.spots.Add(new Placements.Tuned { key = "teleport/Z99", position = new Vector3(2, 0, 0) });
            Assert.That(Placements.Capture(keptA, new[] { a }), Is.False, "nothing of A's changed");
            Assert.That(keptA.spots.Select(t => t.key), Is.EqualTo(new[] { "teleport/Z99" }),
                        "B's adjustment is not A's, and what is kept for spots not under A stays");
            var keptB = new Placements.Tuning();
            Assert.That(Placements.Capture(keptB, new[] { b }), Is.True);
            Assert.That(keptB.spots.Single().position.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Placements.Capture(keptB, new[] { b }), Is.False, "kept already, standing where it is kept");
        }

        [Test]
        public void An_anchors_meshes_are_hidden_as_asked_once_and_undone()
        {
            Transform anchor = Box("UI_SCREEN", new Vector3(1, 1, 0), new Vector3(0.4f, 0.3f, 0.01f));
            var renderer = anchor.GetComponent<Renderer>();
            var dry = new OpContext(dryRun: true);
            Assert.That(Placements.HideAnchor(anchor.gameObject, dry), Is.EqualTo(1));
            Assert.That(dry.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(new[] { ("change", "room/UI_SCREEN") }), "by its path");
            Assert.That(renderer.enabled, Is.True, "a dry run hides nothing");
            OpContext.RunMenu("Hide anchor", ctx => Placements.HideAnchor(anchor.gameObject, ctx));
            Assert.That(renderer.enabled, Is.False);
            var again = new OpContext(dryRun: true);
            Assert.That(Placements.HideAnchor(anchor.gameObject, again), Is.Zero);
            Assert.That(again.Changes, Is.Empty, "hidden already: nothing to do");
            Undo.PerformUndo();
            Assert.That(renderer.enabled, Is.True, "one Ctrl+Z shows it again");
        }
    }
}
