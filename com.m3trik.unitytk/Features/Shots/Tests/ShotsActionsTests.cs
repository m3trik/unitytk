using System;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.Shots.Tests
{
    /// <summary>
    /// Adding a shot by picking a clip, held to the kernel's three checks: one Ctrl+Z
    /// after a menu run restores the scene, the dry run's plan names every change the
    /// run makes, and a second run plans nothing.
    /// </summary>
    public class ShotsActionsTests
    {
        GameObject _actor;
        AnimationClip _clip;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _actor = new GameObject("Actor");
            _clip = new AnimationClip { name = "Walk" };
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.Object.DestroyImmediate(_clip);
        }

        [Test]
        public void Add_entry_is_undone_by_one_ctrl_z()
        {
            bool added = OpContext.RunMenu("Add Shot", ctx => ShotsActions.AddEntry(_actor, _clip, "a walk", ctx));
            Assert.That(added, Is.True);
            var shots = _actor.GetComponent<ShotList>();
            Assert.That(shots.Entry(_clip).description, Is.EqualTo("a walk"));
            Undo.PerformUndo();
            Assert.That(_actor.GetComponent<ShotList>() == null, "one Ctrl+Z took the component and its entry away");
        }

        [Test]
        public void Add_entry_to_existing_shots_is_undone_by_one_ctrl_z()
        {
            var shots = _actor.AddComponent<ShotList>();
            OpContext.RunMenu("Add Shot", ctx => ShotsActions.AddEntry(_actor, _clip, ctx: ctx));
            Assert.That(shots.Entry(_clip), Is.Not.Null);
            Undo.PerformUndo();
            Assert.That(shots.entries, Is.Empty, "one Ctrl+Z took the entry away");
        }

        [Test]
        public void Add_entry_plans_what_it_then_does()
        {
            var plan = new OpContext(dryRun: true);
            Assert.That(ShotsActions.AddEntry(_actor, _clip, ctx: plan), Is.True);
            Assert.That(_actor.GetComponent<ShotList>() == null, "a dry run adds nothing");
            var live = new OpContext();
            ShotsActions.AddEntry(_actor, _clip, ctx: live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)),
                        Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "change" }));
            Assert.That(plan.Changes[0].target, Is.EqualTo("Actor/ShotList"));
        }

        [Test]
        public void Add_entry_run_twice_plans_nothing()
        {
            ShotsActions.AddEntry(_actor, _clip, ctx: new OpContext());
            var again = new OpContext(dryRun: true);
            Assert.That(ShotsActions.AddEntry(_actor, _clip, ctx: again), Is.False);
            Assert.That(again.Changes, Is.Empty);
            Assert.That(_actor.GetComponent<ShotList>().entries, Has.Count.EqualTo(1));
        }

        [Test]
        public void With_no_context_it_adds_and_the_scene_saves_it()
        {
            Assert.That(ShotsActions.AddEntry(_actor, _clip), Is.True);
            Assert.That(_actor.GetComponent<ShotList>().Entry(_clip), Is.Not.Null);
            Assert.That(_actor.scene.isDirty, Is.True);
        }

        [Test]
        public void No_clip_or_no_target_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => ShotsActions.AddEntry(_actor, null));
            Assert.Throws<ArgumentNullException>(() => ShotsActions.AddEntry(null, _clip));
            Assert.That(_actor.GetComponent<ShotList>() == null, "refused before anything changed");
        }
    }
}
