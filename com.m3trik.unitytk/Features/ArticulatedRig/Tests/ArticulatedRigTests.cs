using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// What the rig guarantees without the player loop (each frame is its own
    /// <c>Step</c>, run by hand, as LateUpdate would): it poses every part where the ported
    /// model puts it, from the joint chain -- a flat model's parts chain by their parent
    /// joints, and an anchored hinge turns about its anchor -- a grab lands on its target
    /// within the limits and holds until the rig returns to its animation, the rig follows
    /// the parts' animation until a value is set (a flat model's off-chain parts read in their
    /// parent joints' frames), a released pose gives way when the Animator moves where the rig
    /// says so, each root's chain poses and grabs in its own root's space, a scaled rig's
    /// values are in its space's units, each value's move fires its event, and only bare
    /// meshes get a grab collider.
    /// </summary>
    public class ArticulatedRigTests
    {
        readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        ArticulatedRig Arm(out Transform part)
        {
            GameObject root = ArmRig.Build(out part);
            _made.Add(root);
            return ArmRig.Rig(root, part);
        }

        ArticulatedRig Flat(out ArticulatedJoint hub, out ArticulatedJoint upper, out ArticulatedJoint lower)
        {
            ArticulatedRig rig = ArmRig.Flat(out hub, out upper, out lower);
            _made.Add(rig.gameObject);
            return rig;
        }

        static void Near(Vector3 got, Vector3 want, string what, float tolerance = 1e-5f)
            => Assert.That(Vector3.Distance(got, want), Is.LessThan(tolerance), $"{what}: {got:F6} != {want:F6}");

        [Test]
        public void The_rig_poses_each_part_where_the_model_puts_it()
        {
            ArticulatedRig rig = Arm(out _);
            rig.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f));
            rig.joints[0].rz = 30f;
            rig.joints[1].rz = -45f;
            Assert.That(rig.ApplyNow(), Is.True, rig.Problem);
            ArticulationModel model = rig.Model;
            ArticulationModel.Frame[] world = model.World(rig.GetState());
            for (int j = 0; j < world.Length; j++)
            {
                Transform part = rig.joints[j].transform;
                var at = new Vector3((float)world[j].t.x, (float)world[j].t.y, (float)world[j].t.z);
                Near(part.position, rig.transform.TransformPoint(at), $"joint {j}'s place");
                var turn = new Quaternion((float)world[j].q.x, (float)world[j].q.y, (float)world[j].q.z, (float)world[j].q.w);
                Assert.That(Quaternion.Angle(part.rotation, rig.transform.rotation * turn), Is.LessThan(1e-3f), $"joint {j}'s turn");
            }
            // In Unity's terms: rz turns as Unity's own rotate tool does about z.
            Assert.That(Quaternion.Angle(rig.joints[0].transform.localRotation, Quaternion.Euler(0f, 0f, 30f)), Is.LessThan(1e-3f));
        }

        [Test]
        public void A_flat_model_chains_from_its_parent_joints_not_its_scene_parents()
        {
            ArticulatedRig rig = Flat(out ArticulatedJoint hub, out ArticulatedJoint upper, out ArticulatedJoint lower);
            Vector3 tip = new Vector3(0.6f, 0f, 0f);  // a point of the lower part, in the root's frame at rest

            hub.rz = 90f;
            Assert.That(rig.ApplyNow(), Is.True, rig.Problem);
            Near(lower.transform.TransformPoint(tip), new Vector3(0f, 0.6f, 0f), "the hub's turn carries the lower part");
            Near(upper.transform.TransformPoint(new Vector3(0.3f, 0f, 0f)), new Vector3(0f, 0.3f, 0f), "and the upper");
            Assert.That(lower.transform.parent, Is.SameAs(rig.transform), "the parts stay where they hang");

            hub.rz = 0f;
            upper.rz = 90f;
            rig.ApplyNow();
            Near(hub.transform.TransformPoint(tip), tip, "the hub stays");
            Near(lower.transform.TransformPoint(tip), new Vector3(0.2f, 0.4f, 0f), "the upper's turn, about its anchor, carries the lower");

            hub.rz = 90f;
            upper.rz = 90f;
            lower.rz = 90f;
            rig.ApplyNow();
            // The tip, turned about the lower's anchor, then the upper's, then the hub's.
            Near(lower.transform.TransformPoint(tip), new Vector3(-0.3f, 0.1f, 0f), "three turns, composed down the chain");
        }

        [Test]
        public void An_anchored_hinge_turns_about_its_anchor()
        {
            ArticulatedRig rig = Arm(out Transform part);
            ArticulatedJoint elbow = rig.joints[1];
            elbow.anchorPosition = new Vector3(-0.05f, 0f, 0f);       // the hinge at the grab part, not the elbow's pivot
            elbow.anchorRotation = Quaternion.Euler(90f, 0f, 0f);      // turning about the elbow's y, its anchor's z
            elbow.MarkChanged();
            Vector3 pivot = elbow.transform.TransformPoint(elbow.anchorPosition);
            Vector3 off = elbow.transform.TransformPoint(new Vector3(-0.1f, 0f, 0f));

            elbow.rz = 90f;
            Assert.That(rig.ApplyNow(), Is.True, rig.Problem);
            Near(elbow.transform.TransformPoint(elbow.anchorPosition), pivot, "the anchor stays put");
            Vector3 turned = Quaternion.AngleAxis(90f, elbow.transform.parent.rotation * (Quaternion.Euler(90f, 0f, 0f) * Vector3.forward))
                             * (off - pivot) + pivot;
            Near(elbow.transform.TransformPoint(new Vector3(-0.1f, 0f, 0f)), turned, "a point of the part turns about it");
            Near(elbow.transform.position, Quaternion.AngleAxis(90f, Vector3.down) * (new Vector3(-0.1f, 0f, 0f) - pivot) + pivot,
                 "the pivot swings round the anchor");
        }

        [Test]
        public void A_grab_lands_on_its_target_within_the_limits_and_holds_until_returned()
        {
            ArticulatedRig rig = Arm(out Transform part);
            rig.joints[0].rz = 20f;
            rig.joints[1].rz = 40f;   // bent, away from the straight arm's singularity
            rig.ApplyNow();
            Assert.That(rig.BeginGrab(part, part.position), Is.True);
            Assert.That(rig.GrabbedPart, Is.SameAs(part));
            Assert.That(rig.GrabbedJoint, Is.SameAs(rig.joints[1]));

            // Where another pose puts the held point: reachable by construction.
            ArticulationModel model = rig.Model;
            ArticulationModel.Vec there = model.Point(new[] { 35.0, 20.0 }, 1, rig.GrabLocalPoint);
            Vector3 target = rig.transform.TransformPoint(new Vector3((float)there.x, (float)there.y, (float)there.z));
            rig.UpdateGrab(target);
            Assert.That(Vector3.Distance(rig.GrabPointWorld, target), Is.LessThan(1e-4f));
            Transform elbow = rig.joints[1].transform;
            ArticulationModel.Vec held = rig.GrabLocalPoint;
            Assert.That(Vector3.Distance(elbow.TransformPoint(new Vector3((float)held.x, (float)held.y, (float)held.z)), target),
                        Is.LessThan(1e-4f), "the transforms carry the solve");
            double[] state = rig.GetState();
            for (int slot = 0; slot < model.ChannelCount; slot++)
            {
                model.Limits(slot, out double? lo, out double? hi);
                Assert.That(state[slot], Is.InRange(lo.Value, hi.Value), $"slot {slot} left its limits");
            }
            Assert.That(rig.joints[1].rz, Is.EqualTo((float)state[1]), "the values are the solve's");

            rig.EndGrab();
            Assert.That(rig.IsGrabbing, Is.False);
            Assert.That(rig.IsHoldingPose, Is.True, "the released pose holds");
            rig.ReturnToAnimation();
            Assert.That(rig.IsHoldingPose, Is.False);
        }

        [Test]
        public void A_part_outside_every_grab_part_is_not_grabbed()
        {
            ArticulatedRig rig = Arm(out Transform part);
            Transform elbow = part.parent;
            Assert.That(rig.BeginGrab(elbow, elbow.position), Is.False);
            Assert.That(rig.IsGrabbing, Is.False);
        }

        [Test]
        public void Following_the_animation_reads_the_parts_until_a_value_is_set_and_a_grab_holds_over_it()
        {
            ArticulatedRig rig = Arm(out Transform part);
            rig.followAnimation = true;
            Transform elbow = rig.joints[1].transform;

            elbow.localRotation = Quaternion.Euler(0f, 0f, 25f);  // a clip's frame
            rig.Step();
            Assert.That(rig.joints[1].rz, Is.EqualTo(25f).Within(1e-3f), "the value follows the clip");
            Assert.That(rig.IsHoldingPose, Is.False);

            rig.joints[0].rz = 10f;  // a script's value
            rig.Step();
            Assert.That(rig.IsHoldingPose, Is.True, "a value set over the clip poses the rig");
            elbow.localRotation = Quaternion.Euler(0f, 0f, -70f);  // the clip's next frame
            rig.Step();
            Assert.That(Quaternion.Angle(elbow.localRotation, Quaternion.Euler(0f, 0f, 25f)), Is.LessThan(1e-2f),
                        "the held pose wins over the clip");
            rig.ReturnToAnimation();
            elbow.localRotation = Quaternion.Euler(0f, 0f, -70f);
            rig.Step();
            Assert.That(rig.joints[1].rz, Is.EqualTo(-70f).Within(1e-3f), "returned: the clip again");

            Assert.That(rig.BeginGrab(part, part.position), Is.True);
            elbow.localRotation = Quaternion.Euler(0f, 0f, 5f);
            rig.Step();
            Assert.That(rig.joints[1].rz, Is.EqualTo(-70f).Within(1e-3f), "a grab holds over the clip");
        }

        [Test]
        public void Each_value_that_moves_fires_its_event_once()
        {
            ArticulatedRig rig = Arm(out _);
            var heard = new List<(ArticulatedJoint.Axis, float)>();
            rig.joints[1].changed.AddListener((axis, value) => heard.Add((axis, value)));
            rig.ApplyNow();
            Assert.That(heard, Is.Empty, "nothing moved");
            rig.joints[1].rz = 12f;
            rig.Step();
            rig.Step();
            Assert.That(heard, Is.EqualTo(new[] { (ArticulatedJoint.Axis.Rz, 12f) }));
        }

        [Test]
        public void A_joint_listed_before_its_parent_makes_no_rig_and_says_so()
        {
            ArticulatedRig rig = Arm(out _);
            rig.joints.Reverse();
            rig.Rebuild();
            Assert.That(rig.IsValid, Is.False);
            Assert.That(rig.Problem, Does.Contain("before its parent"));
            Assert.That(rig.ApplyNow(), Is.False);
        }

        // Two roots under different nodes, each posed in its own: "one" at the rig's origin,
        // "two" a metre out and turned a quarter about y, each root an unbounded z hinge whose
        // rest is its place under its own node, the right root holding a grab part 0.2 out.
        ArticulatedRig SplitRoots(out ArticulatedJoint left, out ArticulatedJoint right, out Transform grab)
        {
            var root = new GameObject("Pair");
            _made.Add(root);
            var rig = root.AddComponent<ArticulatedRig>();
            Transform one = ArmRig.Child("one", root.transform, Vector3.zero);
            Transform two = ArmRig.Child("two", root.transform, new Vector3(1f, 0f, 0f));
            two.localRotation = Quaternion.Euler(0f, 90f, 0f);
            left = ArmRig.Joint(rig, ArmRig.Child("left", one, new Vector3(0f, 0.1f, 0f)), null, ArmRig.Free(ArticulatedJoint.Axis.Rz));
            right = ArmRig.Joint(rig, ArmRig.Child("right", two, new Vector3(0f, 0.1f, 0f)), null, ArmRig.Free(ArticulatedJoint.Axis.Rz));
            grab = ArmRig.Child("right_geo", right.transform, new Vector3(0.2f, 0f, 0f));
            rig.grabs.Add(new ArticulatedRig.GrabPart { part = grab, joint = right });
            rig.Rebuild();
            return rig;
        }

        [Test]
        public void Roots_under_different_nodes_each_pose_in_their_own_roots_space()
        {
            ArticulatedRig rig = SplitRoots(out ArticulatedJoint left, out ArticulatedJoint right, out _);
            Assert.That(rig.IsValid, Is.True, rig.Problem);
            right.rz = 90f;
            left.rz = -30f;
            Assert.That(rig.ApplyNow(), Is.True, rig.Problem);
            Near(right.transform.localPosition, new Vector3(0f, 0.1f, 0f), "the right root at its rest, under its own node");
            Assert.That(Quaternion.Angle(right.transform.localRotation, Quaternion.Euler(0f, 0f, 90f)), Is.LessThan(1e-3f),
                        "and turned about its own node's z");
            Assert.That(Quaternion.Angle(left.transform.localRotation, Quaternion.Euler(0f, 0f, -30f)), Is.LessThan(1e-3f));
            Assert.That(rig.SpaceOf(right), Is.SameAs(right.transform.parent), "each chain's space: its root's node");
            Assert.That(rig.SpaceOf(left), Is.SameAs(left.transform.parent));
        }

        [Test]
        public void A_grab_on_a_split_rig_solves_in_its_chains_own_space()
        {
            ArticulatedRig rig = SplitRoots(out _, out ArticulatedJoint right, out Transform grab);
            rig.ApplyNow();
            Transform two = right.transform.parent;
            Assert.That(rig.BeginGrab(grab, grab.position), Is.True);
            // The grab point turned 30 degrees about the right hinge: reachable by construction.
            Vector3 target = two.TransformPoint(new Vector3(0f, 0.1f, 0f) + Quaternion.Euler(0f, 0f, 30f) * new Vector3(0.2f, 0f, 0f));
            rig.UpdateGrab(target);
            Assert.That(Vector3.Distance(rig.GrabPointWorld, target), Is.LessThan(1e-4f), "the held point lands");
            Assert.That(Vector3.Distance(grab.position, target), Is.LessThan(1e-4f), "and the part with it");
            Assert.That(right.rz, Is.EqualTo(30f).Within(1e-2f), "the right hinge alone turns");
        }

        [Test]
        public void A_scaled_rigs_values_are_in_its_spaces_units()
        {
            // A space that carries a scale of 100 (an export's root may): tx = 0.5 is 50 m in the world.
            var root = new GameObject("Scaled");
            _made.Add(root);
            root.transform.localScale = Vector3.one * 100f;
            var rig = root.AddComponent<ArticulatedRig>();
            Transform drawer = ArmRig.Child("drawer", root.transform, Vector3.zero);
            ArticulatedJoint slide = ArmRig.Joint(rig, drawer, null, ArmRig.Free(ArticulatedJoint.Axis.Tx));
            Transform handle = ArmRig.Child("handle", drawer, new Vector3(0f, 0.01f, 0f));
            rig.grabs.Add(new ArticulatedRig.GrabPart { part = handle, joint = slide });
            slide.tx = 0.5f;
            Assert.That(rig.ApplyNow(), Is.True, rig.Problem);
            Near(drawer.position, new Vector3(50f, 0f, 0f), "0.5 of the rig's units, each 100 m");

            Assert.That(rig.BeginGrab(handle, handle.position), Is.True);
            Vector3 target = handle.position + new Vector3(-20f, 0f, 0f);
            // A drag's frames: each solve travels at most MaxStep of the reach per iteration
            // (the reference's), and this slide's reach is its short lever.
            for (int frame = 0; frame < 8; frame++) rig.UpdateGrab(target);
            Near(rig.GrabPointWorld, target, "a grab in a scaled space lands", 1e-2f);
            Assert.That(slide.tx, Is.EqualTo(0.3f).Within(1e-4f), "and reads back in the rig's units");
        }

        [Test]
        public void Following_reads_a_flat_models_off_chain_parts_back_into_the_values()
        {
            ArticulatedRig rig = Flat(out ArticulatedJoint hub, out ArticulatedJoint upper, out ArticulatedJoint lower);
            // A clip's frame: the parts placed where these values put them.
            hub.rz = 20f;
            upper.rz = -35f;
            lower.rz = 60f;
            rig.ApplyNow();
            hub.rz = upper.rz = lower.rz = 0f;
            rig.followAnimation = true;
            rig.Rebuild();
            rig.Step();
            Assert.That(hub.rz, Is.EqualTo(20f).Within(1e-3f));
            Assert.That(upper.rz, Is.EqualTo(-35f).Within(1e-3f), "an off-chain part read in its parent joint's frame");
            Assert.That(lower.rz, Is.EqualTo(60f).Within(1e-3f));
            Assert.That(rig.IsHoldingPose, Is.False, "still following");
        }

        [Test]
        public void ResumeOnAnimatorChange_returns_to_the_animation_once_the_animator_moves()
        {
            ArticulatedRig rig = Arm(out Transform part);
            var clip = new AnimationClip { name = "idle" };
            clip.SetCurve("arm_RIG/base", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 0f));
            var controller = new UnityEditor.Animations.AnimatorController { name = "arm" };
            controller.AddLayer("Base");
            controller.layers[0].stateMachine.AddState("idle").motion = clip;
            var animator = rig.transform.root.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            _made.Add(controller);
            _made.Add(clip);
            animator.Rebind();
            animator.Update(0f);
            Assert.That(animator.isInitialized, Is.True, "an animator that plays in edit mode, stepped by hand");
            rig.followAnimation = true;
            rig.resumeOnAnimatorChange = true;

            Assert.That(rig.BeginGrab(part, part.position), Is.True);
            rig.EndGrab();
            rig.Step();
            Assert.That(rig.IsHoldingPose, Is.True, "released, the Animator where it was: the pose holds");
            animator.Update(0.25f);
            rig.Step();
            Assert.That(rig.IsHoldingPose, Is.False, "the Animator's time moved: back to the animation");

            rig.resumeOnAnimatorChange = false;
            Assert.That(rig.BeginGrab(part, part.position), Is.True);
            rig.EndGrab();
            animator.Update(0.25f);
            rig.Step();
            Assert.That(rig.IsHoldingPose, Is.True, "off: the released pose holds until ReturnToAnimation");
        }

        [Test]
        public void Grab_colliders_go_only_on_bare_meshes()
        {
            Arm(out Transform part);
            var covered = new GameObject("covered");
            covered.transform.SetParent(part, false);
            covered.AddComponent<MeshFilter>().sharedMesh = ArmRig.Cube();
            covered.AddComponent<BoxCollider>();

            Assert.That(ArticulatedRig.AddGrabColliders(part), Is.EqualTo(1));
            var added = part.GetComponent<MeshCollider>();
            Assert.That(added, Is.Not.Null);
            Assert.That(added.convex, Is.True);
            Assert.That(added.sharedMesh, Is.SameAs(part.GetComponent<MeshFilter>().sharedMesh));
            Assert.That(covered.GetComponent<MeshCollider>() == null, "a mesh with a collider keeps its own");
            Assert.That(ArticulatedRig.AddGrabColliders(part), Is.EqualTo(0), "a second pass adds nothing");
            Assert.That(ArticulatedRig.AddGrabColliders(null), Is.EqualTo(0));
        }
    }
}
