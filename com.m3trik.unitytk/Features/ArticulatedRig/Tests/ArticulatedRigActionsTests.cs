using System;
using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The ArticulatedRig actions held to the kernel's checks -- a dry run names every change
    /// and makes none, the live run's changes are the plan's, one undo takes the scene edits
    /// back, and a second run plans nothing: CreateRig puts a rig on a root; AddJoint puts a
    /// joint on a part, at rest where it stands, turning about its pivot, listed after its
    /// parent, so the rig poses as it stood; SetLimitsFromPose takes each value to its side's
    /// limit, or only widens; MakeXRGrabbable says so where the XR door is absent.
    /// </summary>
    public class ArticulatedRigActionsTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [TearDown]
        public void TearDown()
        {
            ArticulatedRigPreview.ReleaseAll();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // A lamp as a person builds one in Unity: a root, an arm turned up, a head on it.
        Transform Lamp(out Transform arm, out Transform head)
        {
            _root = new GameObject("Lamp");
            _root.transform.SetPositionAndRotation(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 30f, 0f));
            arm = ArmRig.Child("arm", _root.transform, new Vector3(0f, 0.1f, 0f));
            arm.localRotation = Quaternion.Euler(0f, 0f, 20f);
            head = ArmRig.Child("head", arm, new Vector3(0.3f, 0f, 0f));
            head.localRotation = Quaternion.Euler(0f, 0f, -45f);
            return _root.transform;
        }

        static (string, string, string)[] Of(OpContext ctx) => ctx.Changes.Select(c => (c.kind, c.target, c.action)).ToArray();

        [Test]
        public void CreateRig_and_AddJoint_plan_what_they_make_undo_in_one_step_and_run_once()
        {
            Transform lamp = Lamp(out Transform arm, out Transform head);
            Vector3 headAt = head.position;
            Quaternion headTurn = head.rotation;

            var plan = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.CreateRig(lamp.gameObject, plan), Is.Null);
            Assert.That(lamp.GetComponent<ArticulatedRig>() == null, "a dry run makes nothing");
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create" }));

            OpContext live = null;
            ArticulatedRig rig = OpContext.RunMenu("Create Rig", ctx =>
            {
                live = ctx;
                return ArticulatedRigActions.CreateRig(lamp.gameObject, ctx);
            });
            Assert.That(Of(live), Is.EqualTo(Of(plan)), "the run's changes are the plan's");
            Assert.That(rig, Is.Not.Null);

            var jointPlan = new OpContext(dryRun: true);
            ArticulatedRigActions.AddJoint(arm, null, new[] { ArticulatedJoint.Axis.Rz }, jointPlan);
            Assert.That(arm.GetComponent<ArticulatedJoint>() == null);
            Assert.That(jointPlan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "change" }));
            OpContext jointLive = null;
            ArticulatedJoint armJoint = OpContext.RunMenu("Add Joint", ctx =>
            {
                jointLive = ctx;
                return ArticulatedRigActions.AddJoint(arm, null, new[] { ArticulatedJoint.Axis.Rz }, ctx);
            });
            Assert.That(Of(jointLive), Is.EqualTo(Of(jointPlan)));
            ArticulatedJoint headJoint = OpContext.RunMenu("Add Joint", ctx => ArticulatedRigActions.AddJoint(
                head, armJoint, new[] { ArticulatedJoint.Axis.Rz, ArticulatedJoint.Axis.Tx }, ctx));

            Assert.That(rig.joints, Is.EqualTo(new[] { armJoint, headJoint }), "each after its parent");
            Assert.That(headJoint.parent, Is.SameAs(armJoint));
            Assert.That(headJoint.anchorPosition, Is.EqualTo(Vector3.zero), "the anchor at the part's pivot");
            Assert.That(headJoint.anchorRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(headJoint.channels.Select(c => (c.axis, c.hasMin, c.hasMax, c.weight)),
                        Is.EqualTo(new[] { (ArticulatedJoint.Axis.Rz, false, false, 1f), (ArticulatedJoint.Axis.Tx, false, false, 1f) }));
            Assert.That(rig.ApplyNow(), Is.True, rig.Problem);
            Assert.That(Vector3.Distance(head.position, headAt), Is.LessThan(1e-5f), "at rest where it stood");
            Assert.That(Quaternion.Angle(head.rotation, headTurn), Is.LessThan(1e-3f));

            var again = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.CreateRig(lamp.gameObject, again), Is.SameAs(rig));
            Assert.That(ArticulatedRigActions.AddJoint(head, armJoint, new[] { ArticulatedJoint.Axis.Rz }, again), Is.SameAs(headJoint));
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");

            Undo.PerformUndo();
            Assert.That(head.GetComponent<ArticulatedJoint>() == null, "one undo takes the joint back");
            Assert.That(rig.joints, Is.EqualTo(new[] { armJoint }), "and its listing");
        }

        [Test]
        public void AddJoint_refuses_a_part_with_no_rig_above_it_and_names_the_fix()
        {
            Lamp(out Transform arm, out _);
            var e = Assert.Throws<ArgumentException>(() => ArticulatedRigActions.AddJoint(arm, null, new[] { ArticulatedJoint.Axis.Rz }));
            Assert.That(e.Message, Does.Contain("CreateRig"));
        }

        [Test]
        public void CreateRig_undoes_in_one_step()
        {
            Transform lamp = Lamp(out _, out _);
            ArticulatedRig rig = OpContext.RunMenu("Create Rig", ctx => ArticulatedRigActions.CreateRig(lamp.gameObject, ctx));
            Assert.That(rig != null);
            Undo.PerformUndo();
            Assert.That(lamp.GetComponent<ArticulatedRig>() == null, "one undo takes the rig back");
        }

        [Test]
        public void AddJoint_refuses_the_rigs_root_a_foreign_parent_and_another_rigs_joint()
        {
            Transform lamp = Lamp(out Transform arm, out Transform head);
            ArticulatedRigActions.CreateRig(lamp.gameObject);
            var e = Assert.Throws<ArgumentException>(() => ArticulatedRigActions.AddJoint(lamp, null, new[] { ArticulatedJoint.Axis.Rz }));
            Assert.That(e.Message, Does.Contain("rig's own root"));

            // A second rig, with its own joint, beside the lamp.
            var other = new GameObject("Other");
            other.transform.SetParent(_root.transform.parent, false);
            ArticulatedRigActions.CreateRig(other);
            Transform part = ArmRig.Child("part", other.transform, Vector3.zero);
            ArticulatedJoint foreign = ArticulatedRigActions.AddJoint(part, null, new[] { ArticulatedJoint.Axis.Rx });
            e = Assert.Throws<ArgumentException>(() => ArticulatedRigActions.AddJoint(arm, foreign, new[] { ArticulatedJoint.Axis.Rz }));
            Assert.That(e.Message, Does.Contain("is not a joint of a rig above"), "a parent joint of another rig");

            // A part already a joint of another rig: the lamp's arm, listed by a rig on a node above it too.
            ArticulatedJoint armJoint = ArticulatedRigActions.AddJoint(arm, null, new[] { ArticulatedJoint.Axis.Rz });
            Transform inner = ArmRig.Child("inner", arm, Vector3.zero);
            ArticulatedRigActions.CreateRig(inner.gameObject);
            head.SetParent(inner, true);
            ArticulatedJoint headJoint = ArticulatedRigActions.AddJoint(head, armJoint, new[] { ArticulatedJoint.Axis.Rz });
            Assert.That(ArticulatedJointEditor.RigOf(headJoint), Is.SameAs(lamp.GetComponent<ArticulatedRig>()));
            e = Assert.Throws<ArgumentException>(() => ArticulatedRigActions.AddJoint(head, null, new[] { ArticulatedJoint.Axis.Rx }));
            Assert.That(e.Message, Does.Contain("already a joint of").And.Contain("Lamp"), "never listed by two rigs");
            Assert.That(lamp.GetComponent<ArticulatedRig>().joints, Is.EqualTo(new[] { armJoint, headJoint }));
            Assert.That(inner.GetComponent<ArticulatedRig>().joints, Is.Empty);
        }

        [Test]
        public void CaptureRest_makes_the_rest_what_poses_the_part_where_it_stands_and_undoes()
        {
            Transform lamp = Lamp(out Transform arm, out Transform head);
            ArticulatedRig rig = ArticulatedRigActions.CreateRig(lamp.gameObject);
            ArticulatedJoint armJoint = ArticulatedRigActions.AddJoint(arm, null, new[] { ArticulatedJoint.Axis.Rz });
            ArticulatedJoint headJoint = ArticulatedRigActions.AddJoint(head, armJoint, new[] { ArticulatedJoint.Axis.Rz, ArticulatedJoint.Axis.Tx });
            headJoint.anchorPosition = new Vector3(0.05f, 0f, 0f);
            headJoint.MarkChanged();
            Vector3 wasRest = headJoint.restPosition;
            headJoint.rz = 25f;
            headJoint.tx = 0.02f;
            head.localPosition = new Vector3(0.4f, 0.05f, -0.01f);  // a person moves it
            head.localRotation = Quaternion.Euler(5f, 0f, -10f);
            Vector3 at = head.position;
            Quaternion turn = head.rotation;

            var plan = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.CaptureRest(headJoint, plan), Is.True);
            Assert.That(headJoint.restPosition, Is.EqualTo(wasRest), "a dry run sets nothing");
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "change" }));

            Assert.That(OpContext.RunMenu("Capture Rest", ctx => ArticulatedRigActions.CaptureRest(headJoint, ctx)), Is.True);
            Assert.That(rig.ApplyNow(), Is.True, rig.Problem);
            Assert.That(Vector3.Distance(head.position, at), Is.LessThan(1e-5f), "at its values, it poses where it stood");
            Assert.That(Quaternion.Angle(head.rotation, turn), Is.LessThan(1e-2f));
            Assert.That((headJoint.rz, headJoint.tx), Is.EqualTo((25f, 0.02f)), "the values stay");
            var again = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.CaptureRest(headJoint, again), Is.False, "where it stands is its rest: nothing to do");
            Assert.That(again.Changes, Is.Empty);

            Undo.PerformUndo();
            Assert.That(headJoint.restPosition, Is.EqualTo(wasRest), "one undo takes the rest back");
        }

        [Test]
        public void A_joint_on_a_flat_models_part_rests_where_it_stands_in_its_parent_joints_frame()
        {
            Transform lamp = Lamp(out Transform arm, out _);
            Transform shade = ArmRig.Child("shade", lamp, new Vector3(0.5f, 0.4f, 0f));  // a sibling of the arm: flat
            shade.localRotation = Quaternion.Euler(10f, 0f, 0f);
            Vector3 at = shade.position;
            ArticulatedRig rig = ArticulatedRigActions.CreateRig(lamp.gameObject);
            ArticulatedJoint armJoint = ArticulatedRigActions.AddJoint(arm, null, new[] { ArticulatedJoint.Axis.Rz });
            ArticulatedJoint shadeJoint = ArticulatedRigActions.AddJoint(shade, armJoint, new[] { ArticulatedJoint.Axis.Rx });
            Assert.That(rig.ApplyNow(), Is.True, rig.Problem);
            Assert.That(Vector3.Distance(shade.position, at), Is.LessThan(1e-5f), "at rest where it stood");

            armJoint.rz = 30f;
            rig.ApplyNow();
            // The arm turns 30 more about its pivot; the shade, chained to it, swings with it.
            var pivot = new Vector3(0f, 0.1f, 0f);
            Vector3 swung = lamp.TransformPoint(pivot + Quaternion.Euler(0f, 0f, 30f) * (new Vector3(0.5f, 0.4f, 0f) - pivot));
            Assert.That(shade.parent, Is.SameAs(lamp), "it hangs where it hung");
            Assert.That(Vector3.Distance(shade.position, swung), Is.LessThan(1e-5f), "the arm's turn carries the shade");
            Assert.That(shadeJoint.parent, Is.SameAs(armJoint));
        }

        [Test]
        public void SetLimitsFromPose_takes_each_value_to_its_sides_limit_or_only_widens()
        {
            Transform lamp = Lamp(out Transform arm, out _);
            ArticulatedRigActions.CreateRig(lamp.gameObject);
            ArticulatedJoint joint = ArticulatedRigActions.AddJoint(arm, null, new[] { ArticulatedJoint.Axis.Rz, ArticulatedJoint.Axis.Ty });
            joint.rz = 35f;
            joint.ty = -0.02f;
            ArticulatedJoint.Channel rz = joint.channels[0], ty = joint.channels[1];
            ty.hasMin = true;
            ty.min = -0.5f;

            var widen = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.SetLimitsFromPose(joint, true, widen), Is.EqualTo(0),
                        "widening: an unbounded side stays so, and -0.02 is inside -0.5");

            var plan = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.SetLimitsFromPose(joint, false, plan), Is.EqualTo(2));
            Assert.That((rz.hasMax, ty.min), Is.EqualTo((false, -0.5f)), "a dry run sets nothing");
            Assert.That(plan.Changes.Select(c => c.action.Split(' ')[0]),
                        Is.EqualTo(new[] { ArticulatedRigActions.Narrow, ArticulatedRigActions.Narrow }), "each change names its kind");
            OpContext live = null;
            int set = OpContext.RunMenu("Set Limits From Pose", ctx =>
            {
                live = ctx;
                return ArticulatedRigActions.SetLimitsFromPose(joint, false, ctx);
            });
            Assert.That(set, Is.EqualTo(2));
            Assert.That(Of(live), Is.EqualTo(Of(plan)));
            Assert.That((rz.hasMax, rz.max, rz.hasMin), Is.EqualTo((true, 35f, false)), "a value above 0 is its max");
            Assert.That((ty.hasMin, ty.min), Is.EqualTo((true, -0.02f)), "below 0, its min");
            var again = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.SetLimitsFromPose(joint, false, again), Is.EqualTo(0));
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");

            joint.rz = 50f;
            Assert.That(ArticulatedRigActions.SetLimitsFromPose(joint, true), Is.EqualTo(1), "past the max: widened");
            Assert.That(rz.max, Is.EqualTo(50f));

            Undo.PerformUndo();
            Assert.That((rz.hasMax, ty.min), Is.EqualTo((false, -0.5f)), "one undo takes the menu's limits back");
        }

        [Test]
        public void MakeXRGrabbable_without_the_xr_toolkit_says_so_and_changes_nothing()
        {
            Transform lamp = Lamp(out _, out _);
            ArticulatedRig rig = ArticulatedRigActions.CreateRig(lamp.gameObject);
            IArticulatedRigXRDoor door = ArticulatedRigActions.XRDoor;
            ArticulatedRigActions.XRDoor = null;
            try
            {
                var ctx = new OpContext();
                Assert.That(ArticulatedRigActions.MakeXRGrabbable(rig, ctx), Is.False);
                Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { ArticulatedRigActions.NoXRCode }));
                Assert.That(ctx.Issues[0].message, Does.Contain("com.unity.xr.interaction.toolkit"));
                Assert.That(ctx.Changes, Is.Empty);
                LogAssert.Expect(LogType.Warning, new Regex("XR Interaction Toolkit"));
                ArticulatedRigActions.MakeXRGrabbable(rig);
            }
            finally
            {
                ArticulatedRigActions.XRDoor = door;
            }
            Assert.That(rig.GetComponents<Component>(), Has.Length.EqualTo(2), "the transform and the rig, nothing more");
        }
    }
}
