using System.Collections.Generic;
using M3trik.Unitytk.ArticulatedRig;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Rig = M3trik.Unitytk.ArticulatedRig.ArticulatedRig;

namespace M3trik.Unitytk.Tools.ArticulatedRig.Tests
{
    /// <summary>
    /// The menu's Set Limits From Pose, which may narrow limits a person set: planned first, it
    /// lists the limits it narrows for the person to confirm, and declined it changes nothing;
    /// confirmed, it runs as one undo step; where it only widens or bounds nothing new it asks
    /// nothing. Add Joint hangs each selected part from the joint above it, parents first.
    /// </summary>
    public class ArticulatedRigMenuTests
    {
        Rig _rig;
        ArticulatedJoint _arm;
        ArticulatedJoint _head;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Lamp");
            Transform arm = new GameObject("arm").transform;
            arm.SetParent(root.transform, false);
            Transform head = new GameObject("head").transform;
            head.SetParent(arm, false);
            head.localPosition = new Vector3(0.3f, 0f, 0f);
            _rig = ArticulatedRigActions.CreateRig(root);
            int added = ArticulatedRigMenu.AddJoints("Add Hinge Joint", new[] { arm, head }, ArticulatedJoint.Axis.Rz);
            Assert.That(added, Is.EqualTo(2));
            _arm = arm.GetComponent<ArticulatedJoint>();
            _head = head.GetComponent<ArticulatedJoint>();
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void Add_joint_hangs_each_part_from_the_joint_above_it()
        {
            Assert.That(_rig.joints, Is.EqualTo(new[] { _arm, _head }));
            Assert.That(_arm.parent, Is.Null, "the first, a root");
            Assert.That(_head.parent, Is.SameAs(_arm));
        }

        [Test]
        public void A_narrowing_asks_and_declined_changes_nothing_confirmed_it_runs()
        {
            _arm.rz = 40f;
            _head.rz = -30f;
            var asked = new List<string>();
            Assert.That(ArticulatedRigMenu.SetLimits(new[] { _arm, _head }, (title, message) =>
            {
                asked.Add(message);
                return false;
            }), Is.EqualTo(0));
            Assert.That(asked, Has.Count.EqualTo(1), "the person is asked once");
            Assert.That(asked[0], Does.Contain("arm Rz max unbounded -> 40").And.Contain("head Rz min unbounded -> -30"));
            Assert.That(_arm.channels[0].hasMax || _head.channels[0].hasMin, Is.False, "declined: the limits stay");

            Assert.That(ArticulatedRigMenu.SetLimits(new[] { _arm, _head }, (title, message) => true), Is.EqualTo(2));
            Assert.That((_arm.channels[0].hasMax, _arm.channels[0].max), Is.EqualTo((true, 40f)));
            Assert.That((_head.channels[0].hasMin, _head.channels[0].min), Is.EqualTo((true, -30f)));
            Undo.PerformUndo();
            Assert.That(_arm.channels[0].hasMax, Is.False, "one undo takes the menu's run back");
        }

        [Test]
        public void Limits_the_pose_widens_ask_nothing()
        {
            _arm.channels[0].hasMax = true;
            _arm.channels[0].max = 20f;
            _arm.rz = 40f;
            bool asked = false;
            Assert.That(ArticulatedRigMenu.SetLimits(new[] { _arm }, (title, message) => asked = true), Is.EqualTo(1));
            Assert.That(asked, Is.False, "widening narrows nothing");
            Assert.That(_arm.channels[0].max, Is.EqualTo(40f));
        }
    }
}
