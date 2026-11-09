using System.Linq;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// The kernel's run context in the editor: a live ask records undo, a menu's run
    /// is one undo step, a cancel or failure undoes its edits, and a dry run plans
    /// without changing anything.
    /// </summary>
    public class OpContextTests
    {
        GameObject _target;

        [SetUp]
        public void SetUp() => _target = new GameObject("before");

        [TearDown]
        public void TearDown()
        {
            if (_target != null) Object.DestroyImmediate(_target);
        }

        [Test]
        public void A_menu_run_is_one_undo_step()
        {
            int result = OpContext.RunMenu("Rename", ctx =>
            {
                if (ctx.ShouldChange(_target, "rename")) _target.name = "after";
                if (ctx.ShouldChange(_target.transform, "move")) _target.transform.position = Vector3.one;
                return 7;
            });
            Assert.That(result, Is.EqualTo(7));
            Assert.That(_target.name, Is.EqualTo("after"));
            Undo.PerformUndo();
            Assert.That(_target.name, Is.EqualTo("before"), "one Ctrl+Z undid the rename");
            Assert.That(_target.transform.position, Is.EqualTo(Vector3.zero), "...and the move with it");
        }

        [Test]
        public void A_cancelled_menu_run_undoes_its_edits()
        {
            OpContext.RunMenu("Cancelled", ctx =>
            {
                if (ctx.ShouldChange(_target, "rename")) _target.name = "lost";
                throw new OperationCancelledException("cancelled");
            });
            Assert.That(_target.name, Is.EqualTo("before"));
        }

        [Test]
        public void A_dry_run_plans_each_change_and_makes_none()
        {
            var plan = new OpContext(dryRun: true);
            if (plan.ShouldChange(_target, "rename")) _target.name = "after";
            if (plan.ShouldWrite("Assets/a.txt", "write a")) Assert.Fail("a dry run writes nothing");
            Assert.That(_target.name, Is.EqualTo("before"));
            Assert.That(plan.Changes.Count, Is.EqualTo(2));
            Assert.That(plan.Changes[0].kind, Is.EqualTo("change"));
            Assert.That(plan.Changes[1].kind, Is.EqualTo("write"));
        }

        [Test]
        public void The_edits_work_with_no_context()
        {
            OpContext none = null;
            var child = new GameObject("child");
            none.Created(child);
            Rigidbody body = none.AddComponent<Rigidbody>(child);
            Assert.That(body, Is.Not.Null);
            none.Destroy(child);
            Assert.That(child == null, "destroyed outright");
        }

        [Test]
        public void An_edit_with_no_context_marks_its_scene_changed()
        {
            const string folder = "Assets/UnitytkOpContextTests";
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var go = new GameObject("edited");
                AssetDatabase.CreateFolder("Assets", "UnitytkOpContextTests");
                Assert.That(EditorSceneManager.SaveScene(scene, folder + "/Edited.unity"), Is.True);
                OpContext none = null;
                Rigidbody body = none.AddComponent<Rigidbody>(go);
                Assert.That(scene.isDirty, Is.True, "the scene saves the component");
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                none.Destroy(body);
                Assert.That(scene.isDirty, Is.True, "...and the delete");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void An_edited_object_is_marked_and_its_scene_saves_it_with_or_without_a_context()
        {
            const string folder = "Assets/UnitytkOpContextEdited";
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var go = new GameObject("edited");
                AssetDatabase.CreateFolder("Assets", "UnitytkOpContextEdited");
                Assert.That(EditorSceneManager.SaveScene(scene, folder + "/Edited.unity"), Is.True);
                OpContext none = null;
                go.transform.position = Vector3.one;
                none.Edited(go.transform);
                Assert.That(scene.isDirty, Is.True, "the scene saves the edit");
                Assert.That(EditorUtility.IsDirty(go.transform), Is.True);

                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                var ctx = new OpContext();
                ctx.Edited(go.transform);
                Assert.That(EditorUtility.IsDirty(go.transform), Is.True, "the object is marked either way");
                Assert.That(scene.isDirty, Is.True, "and its scene saves it");
                none.Edited(null);  // nothing to mark
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void A_problem_raised_with_no_context_is_said_in_the_console_as_a_menu_says_it()
        {
            var ctx = new OpContext();
            ctx.Raise("unity.test.found", "Found one.", location: "Assets/a.fbx");
            Assert.That(ctx.Issues.Single().code, Is.EqualTo("unity.test.found"));
            Assert.That(ctx.Issues.Single().severity, Is.EqualTo("warning"), "a raised problem is a warning by default");
            LogAssert.NoUnexpectedReceived();

            // A menu's run says the issue under its title; a call with no context, the same words under unitytk's.
            LogAssert.Expect(LogType.Warning, "Find: Found one. (Assets/a.fbx)");
            OpContext.RunMenu("Find", run => run.Raise("unity.test.found", "Found one.", location: "Assets/a.fbx"));
            OpContext none = null;
            LogAssert.Expect(LogType.Warning, "unitytk: Found one. (Assets/a.fbx)");
            none.Raise("unity.test.found", "Found one.", location: "Assets/a.fbx");
            LogAssert.Expect(LogType.Log, "unitytk: Noted.");
            none.Raise("unity.test.noted", "Noted.", "info");
        }
    }
}
