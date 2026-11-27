using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Placement.Tests
{
    /// <summary>
    /// <see cref="Placer"/> finds spots that see a part past what blocks it: where a person stands, where a
    /// camera looks from, where an arrow points from, where a panel comes up, and the floor around a part.
    /// A room of primitives: a floor, a part (a cube on a stand), and what each test puts around it.
    /// </summary>
    public class PlacerTests : RoomFixture
    {
        [Test]
        public void Things_closer_than_asked_cluster_into_one_group_and_none_cluster_at_zero()
        {
            Transform plugs = Group("PLUGS", ("LEFT", new Vector3(0, 1f, 0), Vector3.one * 0.04f),
                                    ("RIGHT", new Vector3(0.1f, 1f, 0), Vector3.one * 0.04f),
                                    ("FAR", new Vector3(0.5f, 1f, 0), Vector3.one * 0.04f));
            var items = Placer.Items(plugs);
            Assert.That(items.Count, Is.EqualTo(3), "premise: three things apart");
            var groups = Placer.Cluster(items, 0.15f);
            Assert.That(groups.Count, Is.EqualTo(2), "the two 6 cm apart share a group; the far one has its own");
            Assert.That(groups[0].nodes.ConvertAll(n => n.name), Is.EqualTo(new[] { "LEFT", "RIGHT" }), "in the order of its first thing");
            Assert.That(groups[0].seen.Contains(new Vector3(0.1f, 1f, 0)), Is.True, "its box holds them both");
            Assert.That(Placer.Cluster(items, 0f).Count, Is.EqualTo(3), "0: never");
            Assert.That(items[0].nodes.Count, Is.EqualTo(1), "the items are left as they are");
            Assert.That(Placer.Cluster(null, 1f), Is.Empty);
        }

        [Test]
        public void A_side_is_the_way_of_the_parts_axes_or_across_from_where_the_person_stood()
        {
            Part.rotation = Quaternion.Euler(0, 90, 0);  // its front faces +X
            Assert.That(Vector3.Distance(Placer.SideDirection(StandSide.Front, Part).Value, Vector3.right), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(Placer.SideDirection(StandSide.Back, Part).Value, Vector3.left), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(Placer.SideDirection(StandSide.Left, Part).Value, Vector3.forward), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(Placer.SideDirection(StandSide.Right, Part).Value, Vector3.back), Is.LessThan(1e-4f));
            Vector3 across = Placer.SideDirection(StandSide.Other, Part, new Vector3(0, 0, -3)).Value;
            Assert.That(Vector3.Dot(across.normalized, Vector3.forward), Is.GreaterThan(0.9f), "away from where they stood, past the part");
            Assert.That(Placer.SideDirection(StandSide.Other, Part), Is.Null, "the other side of nowhere: none");
            Assert.That(Placer.SideDirection(StandSide.Front, null), Is.Null);
        }

        [Test]
        public void The_floor_of_a_part_far_below_the_worlds_origin_is_found()
        {
            // The ray's reach was the part's top measured from the world's zero: a part 500 m down cast no ray at all.
            Room.transform.position = new Vector3(0, -500f, 0);
            Physics.SyncTransforms();
            Assert.That(Placer.FloorNear(PartBounds, out float floor), Is.True);
            Assert.That(floor, Is.EqualTo(-500f).Within(1e-3f));
        }

        [Test]
        public void A_person_needs_their_body_room_and_a_narrower_body_fits_where_a_wider_does_not()
        {
            // The part at the end of a corridor 0.5 m wide (walls along Z), closed behind it: a 0.35 m body has no room in it.
            Box("WALL_L", new Vector3(-0.3f, 1.5f, 1.5f), new Vector3(0.1f, 3f, 5f));
            Box("WALL_R", new Vector3(0.3f, 1.5f, 1.5f), new Vector3(0.1f, 3f, 5f));
            Box("BACK", new Vector3(0, 1.5f, -0.4f), new Vector3(3f, 3f, 0.1f));
            Box("ROOF", new Vector3(0, 3.05f, 0), new Vector3(30f, 0.1f, 30f));  // nothing seen past the walls from above
            Box("FRONT", new Vector3(0, 1.5f, 4.05f), new Vector3(3f, 3f, 0.1f));
            Box("SIDE_L", new Vector3(-1.5f, 1.5f, 1.8f), new Vector3(0.1f, 3f, 4.5f));
            Box("SIDE_R", new Vector3(1.5f, 1.5f, 1.8f), new Vector3(0.1f, 3f, 4.5f));
            Physics.SyncTransforms();
            Assert.That(Placer.Stand(PartBounds, Part, 0f, null, out Pose _), Is.False, "no room for the default body");
            Assert.That(Placer.Stand(PartBounds, Part, 0f, null, out Pose pose, bodyRadius: 0.2f), Is.True, "a narrower one fits");
            Assert.That(Mathf.Abs(pose.position.x), Is.LessThan(0.1f), "in the corridor");
            Assert.That(Placer.Stand(PartBounds, Part, 0f, null, out Pose _, bodyRadius: 0.2f, bodyHeight: 3.2f), Is.False,
                        "a body taller than the roof has no room");
        }

        [Test]
        public void A_hit_on_a_parts_own_collider_under_a_rigidbody_outside_it_sees_it()
        {
            // The part in a cabinet with a rigidbody: the part's collider is the cabinet's body's, so a hit's transform is the
            // cabinet, outside the part; its collider's object is the part.
            var cabinet = new GameObject("CABINET", typeof(Rigidbody));
            cabinet.transform.SetParent(Room.transform);
            cabinet.GetComponent<Rigidbody>().isKinematic = true;
            Part.SetParent(cabinet.transform, true);
            Physics.SyncTransforms();
            Assert.That(Placer.Seen(new Vector3(0, 1f, -2f), PartBounds, Part), Is.GreaterThan(0.5f), "its own collider is no wall");
        }

        // A group of boxes under a wrapper node, as a delivery holds two locks on a door.
        Transform Group(string name, params (string name, Vector3 at, Vector3 size)[] boxes)
        {
            var group = new GameObject(name).transform;
            group.SetParent(Room.transform);
            var wrapper = new GameObject(name + "_WRAP").transform;
            wrapper.SetParent(group);
            foreach (var (box, at, size) in boxes) Box(box, at, size).SetParent(wrapper, true);
            return group;
        }

        [Test]
        public void An_arrow_has_room_for_its_whole_bounce()
        {
            // A ceiling the arrow over the part clears at rest, but not at the top of a 3 cm bob. Added: 2026-11-22
            Box("CEILING", new Vector3(0, 1.37f, 0), new Vector3(0.3f, 0.1f, 0.3f));  // over the part alone: the eyes still see past it
            Physics.SyncTransforms();
            Vector3 eyes = new Vector3(0, 1.6f, -2f);
            Assert.That(Placer.Point(PartBounds, Part, eyes, out Pose still), Is.True);
            Assert.That(Vector3.Dot(still.rotation * Vector3.up, Vector3.up), Is.GreaterThan(0.99f), "at rest it fits over the part");
            Assert.That(Placer.Point(PartBounds, Part, eyes, out Pose bouncing, travel: 0.03f), Is.True);
            Assert.That(Vector3.Dot(bouncing.rotation * Vector3.up, Vector3.up), Is.LessThan(0.99f), "its bounce would reach the ceiling: another way");
        }

        [Test]
        public void A_group_of_things_apart_is_that_many_items_and_one_thing_is_one()
        {
            Transform locks = Group("LOCKS", ("UPPER", new Vector3(0, 1.6f, 0), Vector3.one * 0.03f), ("LOWER", new Vector3(0, 1.1f, 0), Vector3.one * 0.03f));
            var items = Placer.Items(locks);
            Assert.That(items.Count, Is.EqualTo(2), "two locks half a metre apart, under a wrapper");
            Assert.That(items[0].nodes[0].name, Is.EqualTo("UPPER"));
            Assert.That(items[1].seen.center.y, Is.EqualTo(1.1f).Within(1e-3f));

            Transform door = Group("DOOR", ("PANEL", new Vector3(2, 1f, 0), new Vector3(0.6f, 1.8f, 0.04f)), ("HANDLE", new Vector3(2.2f, 1f, 0.04f), new Vector3(0.03f, 0.15f, 0.04f)));
            Assert.That(Placer.Items(door).Count, Is.EqualTo(1), "a handle on its door: one thing");
            Assert.That(Placer.Items(door)[0].nodes[0], Is.EqualTo(door));
            Assert.That(Placer.Items(Part).Count, Is.EqualTo(1), "a lone part");

            Transform arrow = Box("ARROW", new Vector3(0, 2.5f, 0), Vector3.one * 0.1f);
            arrow.SetParent(locks, true);
            Assert.That(Placer.Items(locks, t => t == arrow).Count, Is.EqualTo(2), "what a build added beside the items is no item");
        }

        [Test]
        public void A_person_stands_where_thePart_is_seen_not_behind_a_wall()
        {
            // A wall on the part's -Z side, wide and tall: from there the part is hidden.
            Box("WALL", new Vector3(0, 1f, -0.6f), new Vector3(6, 3, 0.1f));
            Assert.That(Placer.Stand(PartBounds, Part, 0f, new Vector3(0, 0, -5), out Pose pose), Is.True);
            Assert.That(pose.position.z, Is.GreaterThan(-0.5f), "on the side the part is seen from, though it stood behind the wall before");
            Assert.That(Placer.Seen(pose.position + Vector3.up * Placer.DefaultEyeHeight, PartBounds, Part), Is.GreaterThan(0.5f));
            Assert.That(pose.position.y, Is.EqualTo(0f).Within(1e-4f), "on the floor");
            Vector3 to = PartBounds.center - pose.position;
            to.y = 0f;
            Assert.That(Vector3.Angle(pose.forward, to), Is.LessThan(0.5f), "facing the part, level");
            float away = new Vector2(pose.position.x, pose.position.z).magnitude - 0.1f;
            Assert.That(away, Is.InRange(Placer.DefaultStandOff - 0.1f, Placer.DefaultStandOff + 1.1f), "a stand-off from it");
        }

        [Test]
        public void A_person_stands_where_they_also_see_what_they_act_on_there()
        {
            // A second part behind a screen on the part's -X side: seen only from +X.
            Transform other = Box("OTHER", new Vector3(-0.6f, 1f, 0f), Vector3.one * 0.1f);
            Box("SCREEN", new Vector3(-1.0f, 1f, 0f), new Vector3(0.05f, 2f, 3f));
            Bounds seen = other.GetComponent<Renderer>().bounds;
            Assert.That(Placer.Stand(PartBounds, Part, 0f, new Vector3(-5, 0, 0), out Pose pose, also: new[] { (seen, other) }), Is.True);
            Assert.That(Placer.Seen(pose.position + Vector3.up * Placer.DefaultEyeHeight, seen, other), Is.GreaterThan(0.5f),
                        "where the part acted on there is seen too, though the person stood on the other side before");
        }

        [Test]
        public void A_person_stands_on_the_side_asked_for_where_it_sees_thePart()
        {
            Box("WALL", new Vector3(0, 1f, -0.6f), new Vector3(6, 3, 0.1f));
            Assert.That(Placer.Stand(PartBounds, Part, 0f, new Vector3(0, 0, 3), out Pose pose, side: Vector3.left), Is.True);
            Assert.That(pose.position.x, Is.LessThan(-1f), "its left, though it stood ahead of it before");
            Assert.That(Placer.Stand(PartBounds, Part, 0f, null, out pose, side: Vector3.back), Is.True);
            Assert.That(pose.position.z, Is.GreaterThan(-0.5f), "the side asked for is behind the wall: a side that sees it");
        }

        [Test]
        public void The_floor_is_what_rays_land_on_around_thePart_not_a_walls_foot_below_it()
        {
            Box("WALL", new Vector3(0, 1f, -0.6f), new Vector3(6, 3, 0.1f));  // its foot half a metre under the floor
            Box("TABLE", new Vector3(1.5f, 0.75f, 0), new Vector3(1, 0.05f, 1));
            Assert.That(Placer.FloorNear(PartBounds, out float floor), Is.True);
            Assert.That(floor, Is.EqualTo(0f).Within(1e-4f));

            // A part far larger than a person (a vehicle): the rays start past its side, not on its top.
            Transform large = Box("HULL", new Vector3(10f, 1.5f, 0f), new Vector3(8f, 3f, 8f));
            Assert.That(Placer.FloorNear(large.GetComponent<Renderer>().bounds, out floor), Is.True);
            Assert.That(floor, Is.EqualTo(0f).Within(1e-4f), "the floor beside it, not its roof");
        }

        // The real task's picture of a locker door was taken through the slotted door it hangs behind: a few rays passed
        // the slots, and the spot on the user's side won. Its indicator light, a centimetre across, was shown from 5 cm.
        [Test]
        public void A_camera_shows_aPart_behind_a_grille_from_its_clear_side_and_a_tiny_one_with_what_it_sits_on()
        {
            Object.DestroyImmediate(Part.gameObject);
            Part = Box("LOCKER_DOOR", new Vector3(0, 1.5f, 0), new Vector3(0.34f, 0.11f, 0.03f));
            // A grille between it and the eyes (on +Z): bars with slots a few rays pass.
            for (int i = -4; i <= 4; i++) Box("BAR" + i, new Vector3(i * 0.08f, 1.5f, 0.06f), new Vector3(0.05f, 0.6f, 0.02f));
            Physics.SyncTransforms();
            var eyes = new Vector3(0.1f, 1.6f, 1.5f);
            Assert.That(Placer.View(PartBounds, Part, eyes, 30f, 1.4f, out Pose pose), Is.True);
            // Which face of the part the picture shows is no search's to know (the spot is moved, or the row names a
            // camera): it promises a clear view, so never one through the grille, though the eyes are on that side.
            Assert.That(pose.position.z, Is.LessThan(0.03f), "never from the grille's side, though the eyes are there");
            Assert.That(Placer.Seen(pose.position, PartBounds, Part), Is.EqualTo(1f), "the whole of it seen");

            Object.DestroyImmediate(Part.gameObject);
            Part = Box("LIGHT", new Vector3(0, 1.5f, -0.5f), Vector3.one * 0.01f);
            Physics.SyncTransforms();
            Assert.That(Placer.View(PartBounds, Part, eyes, 30f, 1.4f, out pose), Is.True);
            Assert.That(Vector3.Distance(pose.position, PartBounds.center), Is.GreaterThan(0.3f), "far enough to show what it sits on, never a macro of a dot");
        }

        [Test]
        public void A_camera_sees_aPart_inside_a_box_through_its_open_side()
        {
            // A box around the part, open toward +X only; the eyes are on the -X side.
            Box("BACK", new Vector3(-0.3f, 1f, 0), new Vector3(0.05f, 0.6f, 0.6f));
            Box("LEFT", new Vector3(0, 1f, -0.3f), new Vector3(0.6f, 0.6f, 0.05f));
            Box("RIGHT", new Vector3(0, 1f, 0.3f), new Vector3(0.6f, 0.6f, 0.05f));
            Box("TOP", new Vector3(0, 1.3f, 0), new Vector3(0.6f, 0.05f, 0.6f));
            Box("BOTTOM", new Vector3(0, 0.7f, 0), new Vector3(0.6f, 0.05f, 0.6f));
            Assert.That(Placer.View(PartBounds, Part, new Vector3(-3, 1.6f, 0), 30f, 1.4f, out Pose pose), Is.True);
            Assert.That(pose.position.x, Is.GreaterThan(0.3f), "from the open side, not the eyes' side the box hides it from");
            Assert.That(Vector3.Angle(pose.forward, PartBounds.center - pose.position), Is.LessThan(0.5f), "looking at it");
            Assert.That(Placer.Seen(pose.position, PartBounds, Part), Is.GreaterThan(0.5f));
        }

        [Test]
        public void A_panel_brought_nearer_comes_in_toward_the_middle_and_never_stands_beyond_the_view()
        {
            // The real task's placard pages, pushed nearer where the machine stood close, came up 40 degrees aside on the
            // desktop rig: their text cut by the screen's edge.
            var eyes = new Vector3(0f, 1.6f, 0f);
            Box("WALL", new Vector3(0f, 1.5f, 0.9f), new Vector3(4f, 3f, 0.1f));  // close ahead: only a nearer page fits
            Physics.SyncTransforms();
            var half = new Vector2(0.15f, 0.12f);
            Assert.That(Placer.Panel(eyes, Quaternion.identity, new Vector3(0.4f, -0.05f, 1.2f), half, null, out Vector3 offset), Is.True);
            Assert.That(offset.z, Is.LessThan(0.85f), "brought nearer, in front of the wall");
            float aside = Mathf.Atan2(Mathf.Abs(offset.x), offset.z) * Mathf.Rad2Deg;
            Assert.That(aside, Is.EqualTo(Mathf.Atan2(0.4f, 1.2f) * Mathf.Rad2Deg).Within(0.01f), "the same way off the eyes' line as it would come up");
            // An offset that would stand it beyond the view's middle is brought in to it.
            Assert.That(Placer.Panel(eyes, Quaternion.identity, new Vector3(0.9f, -0.05f, 1.2f), half, null, out offset), Is.True);
            Assert.That(Mathf.Atan2(Mathf.Abs(offset.x), offset.z) * Mathf.Rad2Deg, Is.LessThanOrEqualTo(Placer.MostAside + 0.01f));
        }

        [Test]
        public void An_arrow_points_down_at_aPart_unless_a_shelf_is_over_it()
        {
            var eyes = new Vector3(0, 1.6f, 2f);
            Assert.That(Placer.Point(PartBounds, Part, eyes, out Pose pose), Is.True);
            Assert.That(pose.up, Is.EqualTo(Vector3.up), "over it, pointing down: the usual arrow");
            Assert.That(pose.position.y, Is.EqualTo(PartBounds.max.y + Placer.DefaultGap).Within(1e-4f));

            Box("SHELF", new Vector3(0, 1.15f, 0), new Vector3(1, 0.05f, 1));
            Assert.That(Placer.Point(PartBounds, Part, eyes, out pose), Is.True);
            Assert.That(pose.position.y, Is.LessThan(1.15f), "under the shelf");
            Assert.That(Mathf.Abs(Vector3.Dot(pose.up, (PartBounds.center - eyes).normalized)), Is.LessThan(0.8f),
                        "across the line of sight, never pointing straight at the person (seen end-on)");
        }

        // The real task's arrow at the lower of two locks on a door: straight up ran along the door, the next way (up
        // and toward the eyes) pointed at the eyes high above it, and the arrow, drawn flat and turned to face them,
        // sank into the door.
        [Test]
        public void An_arrow_at_aPart_low_on_a_wall_never_points_along_the_line_of_sight()
        {
            Object.DestroyImmediate(Part.gameObject);
            Box("DOOR", new Vector3(0, 1f, -0.05f), new Vector3(1f, 2f, 0.1f));
            Part = Box("LOCK", new Vector3(0, 0.68f, 0.01f), new Vector3(0.03f, 0.03f, 0.02f));
            Physics.SyncTransforms();
            var eyes = new Vector3(0.05f, 1.61f, 0.55f);
            Assert.That(Placer.Point(PartBounds, Part, eyes, out Pose pose), Is.True);
            Vector3 sight = (PartBounds.center - eyes).normalized;
            Assert.That(Mathf.Abs(Vector3.Dot(pose.up, sight)), Is.LessThan(0.866f), "30 degrees or more off the line of sight");
            Assert.That(pose.position.z, Is.GreaterThan(0f), "on the door's near side");
        }

        [Test]
        public void An_arrow_at_aPart_mounted_where_no_usual_way_is_free_points_straight_out_of_its_mount()
        {
            // A button on a shelf's underside, the eyes below it: up is the shelf, and every way across runs along its
            // underside. In one delivery such arrows pointed down through what the part sat on.
            Object.DestroyImmediate(Part.gameObject);
            Box("SHELF", new Vector3(0, 1.5f, 0), new Vector3(1.2f, 0.04f, 1.2f));
            Part = Box("BUTTON", new Vector3(0, 1.475f, 0), new Vector3(0.03f, 0.01f, 0.03f));
            Physics.SyncTransforms();
            var eyes = new Vector3(0, 1.1f, -1.5f);
            Assert.That(Placer.Point(PartBounds, Part, eyes, out Pose pose), Is.True, "a way is free: out of the shelf");
            Assert.That(Vector3.Dot(pose.up, Vector3.down), Is.GreaterThan(0.9f), "straight out of its mount, pointing up at it");
            Assert.That(pose.position.y, Is.LessThan(PartBounds.min.y), "below the button, clear of the shelf");
        }

        [Test]
        public void The_scenes_physics_places_as_the_model_does_over_the_same_boxes()
        {
            // The model is held to pythontk's golden cases over boxes (tests/models_dotnet); this holds what Placer hands
            // it, the scene's colliders, to the same boxes: a box scene placed both ways lands alike. Away from the room.
            var at = new Vector3(50f, 0f, 0f);
            var rays = new BoxRays();
            var bounds = new Dictionary<Transform, Bounds>();
            Transform Add(string name, Vector3 low, Vector3 high)
            {
                Transform made = Box(name, at + (low + high) * 0.5f, high - low);
                rays.Add(made, W(at + low), W(at + high));
                bounds[made] = new Bounds(at + (low + high) * 0.5f, high - low);
                return made;
            }
            Add("FLOOR_50", new Vector3(-6f, -0.1f, -6f), new Vector3(6f, 0f, 6f));
            Add("DOOR", new Vector3(-0.5f, 0f, -0.03f), new Vector3(0.5f, 2f, 0.03f));
            Transform latch = Add("LATCH", new Vector3(0.15f, 1.05f, 0.03f), new Vector3(0.25f, 1.15f, 0.07f));
            Add("PANEL", new Vector3(-0.4f, 1.2f, -0.43f), new Vector3(0.4f, 1.8f, -0.4f));
            Transform dials = Add("DIALS", new Vector3(-0.05f, 1.45f, -0.46f), new Vector3(0.05f, 1.55f, -0.43f));
            Add("SHELF", new Vector3(-0.5f, 1.17f, -0.1f), new Vector3(0.5f, 1.2f, 0.4f));
            Physics.SyncTransforms();
            var model = new SpotPlacementModel(rays);
            SpotPlacementModel.Box Of(Transform t) => new SpotPlacementModel.Box(W(bounds[t].min), W(bounds[t].max));
            System.Func<object, bool> Only(Transform t) => o => ReferenceEquals(o, t);
            void Same(string what, bool found, Pose pose, bool modelFound, SpotPlacementModel.Vec position)
            {
                Assert.That(found, Is.EqualTo(modelFound), what + ": found alike");
                Assert.That(Vector3.Distance(pose.position, new Vector3((float)position.x, (float)position.y, (float)position.z)),
                            Is.LessThan(2e-3f), $"{what}: {pose.position} by physics, {position} over boxes");
            }

            bool f = Placer.Stand(bounds[latch], latch, 0f, null, out Pose pose, side: Vector3.forward);
            bool m = model.Stand(Of(latch), out SpotPlacementModel.Vec p, out _, floor: 0.0, owners: Only(latch), side: new SpotPlacementModel.Vec(0, 0, 1), grounded: true);
            Same("a viewpoint", f, pose, m, p);

            Vector3 behind = at + new Vector3(0f, 1.6f, -2f);
            f = Placer.View(bounds[dials], dials, behind, 30f, 1.4f, out pose);
            m = model.View(Of(dials), out p, out _, eyes: W(behind), owners: Only(dials), fieldOfView: 30.0, margin: 1.4);
            Same("a picture", f, pose, m, p);

            Vector3 eyes = at + new Vector3(0.2f, 1.6f, 2f);
            f = Placer.Point(bounds[latch], latch, eyes, out pose);
            m = model.Point(Of(latch), out p, out _, eyes: W(eyes), owners: Only(latch));
            Same("an arrow", f, pose, m, p);
            f = Placer.Point(bounds[latch], latch, eyes, out pose, travel: 0.1f);
            m = model.Point(Of(latch), out p, out _, eyes: W(eyes), owners: Only(latch), travel: 0.1);
            Same("a bobbing arrow", f, pose, m, p);

            Assert.That(Placer.FloorNear(bounds[latch], out float floor), Is.EqualTo(model.FloorNear(Of(latch), out double boxFloor)));
            Assert.That(floor, Is.EqualTo((float)boxFloor).Within(1e-4f), "the floor alike");
        }

        [Test]
        public void A_spot_with_no_side_named_stands_on_the_deliverys_outer_side_and_a_floorless_one_by_sight_as_the_model_places_them()
        {
            // The model's rule (each DCC's): where no side is named, the delivery's outer side breaks the tie between spots that
            // see the part alike, and ground is needed only where rays find floor. Placer handed it no delivery and always asked
            // for ground: in an open room it kept the ring's first spot (+X), and a delivery with no floor got no spot at all.
            var rays = new BoxRays();
            rays.Add(Room.transform.Find("FLOOR"), new SpotPlacementModel.Vec(-10, -0.1, -10), new SpotPlacementModel.Vec(10, 0, 10));
            rays.Add(Part, W(PartBounds.min), W(PartBounds.max));
            var model = new SpotPlacementModel(rays);
            var part = new SpotPlacementModel.Box(W(PartBounds.min), W(PartBounds.max));
            System.Func<object, bool> own = o => ReferenceEquals(o, Part);
            var delivery = new Bounds(new Vector3(3f, 1f, 0f), new Vector3(8f, 2f, 8f));  // its middle 3 m to the part's +X

            Assert.That(Placer.Stand(PartBounds, Part, 0f, null, out Pose pose, delivery: delivery), Is.True);
            Assert.That(model.Stand(part, out SpotPlacementModel.Vec feet, out _, delivery: new SpotPlacementModel.Box(W(delivery.min), W(delivery.max)),
                                    floor: 0.0, owners: own), Is.True);
            Assert.That(Vector3.Distance(pose.position, new Vector3((float)feet.x, (float)feet.y, (float)feet.z)), Is.LessThan(2e-3f), "placed as the model places it");
            Assert.That(pose.position.x, Is.LessThan(-1f), "out from the delivery's middle: its outer side, -X");

            Object.DestroyImmediate(Room.transform.Find("FLOOR").gameObject);  // a delivery with no floor
            Physics.SyncTransforms();
            var bare = new BoxRays();
            bare.Add(Part, W(PartBounds.min), W(PartBounds.max));
            Assert.That(Placer.FloorNear(PartBounds, out float floor), Is.False, "premise: no floor around it");
            Assert.That(Placer.Stand(PartBounds, Part, floor, null, out pose), Is.True, "a spot by sight, at the part's lowest point");
            Assert.That(new SpotPlacementModel(bare).Stand(part, out feet, out _, floor: floor, owners: own), Is.True);
            Assert.That(Vector3.Distance(pose.position, new Vector3((float)feet.x, (float)feet.y, (float)feet.z)), Is.LessThan(2e-3f), "where the model stands it");
        }

        static SpotPlacementModel.Vec W(Vector3 v) => new SpotPlacementModel.Vec(v.x, v.y, v.z);

        [Test]
        public void A_panel_comes_up_clear_of_a_wall_beside_the_eyes_and_off_what_is_to_be_seen()
        {
            var eyes = new Vector3(0, 1.6f, 0);
            Quaternion heading = Quaternion.identity;  // facing +Z
            var preferred = new Vector3(-0.4f, -0.2f, 0.8f);
            Assert.That(Placer.Panel(eyes, heading, preferred, new Vector2(0.3f, 0.2f), null, out Vector3 offset), Is.True);
            Assert.That(offset, Is.EqualTo(preferred), "nothing in the way: where it comes up by itself");

            Box("WALL", new Vector3(-0.45f, 1.5f, 0.8f), new Vector3(0.1f, 3, 3));  // a wall where its left half would stand
            Assert.That(Placer.Panel(eyes, heading, preferred, new Vector2(0.3f, 0.2f), null, out offset), Is.True);
            Assert.That(offset.x, Is.GreaterThan(0f), "mirrored to the right, clear of the wall");

            Assert.That(Placer.Panel(eyes, heading, preferred, new Vector2(0.3f, 0.2f), new[] { new Vector3(1.2f, 1.0f, 2.4f) }, out offset), Is.True);  // a part behind the right-hand spot
            Assert.That(offset.x, Is.GreaterThan(0f));
            Assert.That(offset.z < preferred.z - 1e-4f || Mathf.Abs(offset.y - preferred.y) > 1e-4f, "moved off the part it would hide");
        }
    }
}
