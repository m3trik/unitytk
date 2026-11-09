using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig.XRI
{
    /// <summary>
    /// The XR Interaction Toolkit door, with the toolkit in the project: MakeXRGrabbable puts
    /// the XR setup on the rig's root (planned first, one undo, a second run plans nothing),
    /// the setup makes each grab part an XR interactable holding the rig, and the interactable
    /// hands its grab to the rig -- taken, followed to a target it lands on, and let go -- and
    /// once another hand has taken the rig, steers and releases nothing.
    /// </summary>
    public class ArticulatedRigXRTests
    {
        ArticulatedRig _rig;
        Transform _part;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Arm");
            var bottom = Child("base", root.transform, Vector3.zero);
            var elbow = Child("elbow", bottom, new Vector3(-0.1f, 0f, 0f));
            _part = Child("elbow_geo", elbow, new Vector3(-0.05f, 0f, 0f));
            _part.gameObject.AddComponent<BoxCollider>().size = Vector3.one * 0.02f;
            _rig = root.AddComponent<ArticulatedRig>();
            ArticulatedJoint first = Joint(bottom, null);
            ArticulatedJoint second = Joint(elbow, first);
            _rig.grabs.Add(new ArticulatedRig.GrabPart { part = _part, joint = second });
            _rig.joints[0].rz = 20f;
            _rig.joints[1].rz = 40f;
            Assert.That(_rig.ApplyNow(), Is.True, _rig.Problem);
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        ArticulatedJoint Joint(Transform part, ArticulatedJoint parent)
        {
            var joint = part.gameObject.AddComponent<ArticulatedJoint>();
            joint.parent = parent;
            joint.restPosition = part.localPosition;
            joint.channels = new List<ArticulatedJoint.Channel>
            {
                new ArticulatedJoint.Channel { axis = ArticulatedJoint.Axis.Rz, hasMin = true, min = -150f, hasMax = true, max = 150f },
            };
            _rig.joints.Add(joint);
            _rig.Rebuild();
            return joint;
        }

        static Transform Child(string name, Transform parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        [Test]
        public void MakeXRGrabbable_puts_the_setup_on_the_rig_planned_first_undone_in_one_step()
        {
            Assert.That(ArticulatedRigActions.XRDoor, Is.Not.Null, "the door registers as the editor loads");
            var plan = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.MakeXRGrabbable(_rig, plan), Is.True);
            Assert.That(_rig.GetComponent<ArticulatedRigXRSetup>() == null, "a dry run makes nothing");
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create" }));

            OpContext live = null;
            Assert.That(OpContext.RunMenu("Make XR Grabbable", ctx =>
            {
                live = ctx;
                return ArticulatedRigActions.MakeXRGrabbable(_rig, ctx);
            }), Is.True);
            Assert.That(live.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target))));
            ArticulatedRigXRSetup setup = _rig.GetComponent<ArticulatedRigXRSetup>();
            Assert.That(setup != null && setup.rig == _rig, "the setup, holding the rig");
            Assert.That(live.Issues, Is.Empty);

            var again = new OpContext(dryRun: true);
            Assert.That(ArticulatedRigActions.MakeXRGrabbable(_rig, again), Is.True);
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");

            Undo.PerformUndo();
            Assert.That(_rig.GetComponent<ArticulatedRigXRSetup>() == null, "one undo takes it back");
        }

        [Test]
        public void The_setup_makes_each_grab_part_an_interactable_that_hands_its_grab_to_the_rig()
        {
            var setup = _rig.gameObject.AddComponent<ArticulatedRigXRSetup>();
            IReadOnlyList<ArticulatedRigXRGrab> grabs = setup.Build();
            Assert.That(grabs, Has.Count.EqualTo(1));
            ArticulatedRigXRGrab grab = grabs[0];
            Assert.That(grab.transform, Is.SameAs(_part));
            Assert.That(grab.Rig, Is.SameAs(_rig));
            Assert.That(setup.Build(), Has.Count.EqualTo(1), "a second build finds its own");

            Assert.That(grab.GrabAt(_part.position, Quaternion.identity), Is.True);
            Assert.That(grab.IsHolding, Is.True);
            ArticulationModel model = _rig.Model;
            ArticulationModel.Vec there = model.Point(new[] { 35.0, 20.0 }, 1, _rig.GrabLocalPoint);
            Vector3 target = _rig.transform.TransformPoint(new Vector3((float)there.x, (float)there.y, (float)there.z));
            grab.FollowTo(target, Quaternion.identity);
            Assert.That(Vector3.Distance(_rig.GrabPointWorld, target), Is.LessThan(1e-4f), "the held point lands");
            grab.Release();
            Assert.That(_rig.IsGrabbing, Is.False);
            Assert.That(_rig.IsHoldingPose, Is.True, "the released pose holds");
        }

        [Test]
        public void Only_the_interactable_holding_the_rig_steers_it()
        {
            var setup = _rig.gameObject.AddComponent<ArticulatedRigXRSetup>();
            ArticulatedRigXRGrab grab = setup.Build()[0];
            Assert.That(grab.GrabAt(_part.position, Quaternion.identity), Is.True);
            // Another hand takes the rig by another part (the base's), so this one no longer holds it.
            Transform other = _rig.joints[0].transform;
            _rig.grabs.Add(new ArticulatedRig.GrabPart { part = other, joint = _rig.joints[0] });
            Assert.That(_rig.BeginGrab(other, other.position), Is.True);
            Assert.That(grab.IsHolding, Is.False);
            double[] before = _rig.GetState();
            grab.FollowTo(_part.position + new Vector3(0.05f, 0.05f, 0f), Quaternion.identity);
            Assert.That(_rig.GetState(), Is.EqualTo(before), "a hand that lost the rig moves nothing");
            grab.Release();
            Assert.That(_rig.IsGrabbing, Is.True, "nor lets go of the other hand's grab");
        }
    }
}
