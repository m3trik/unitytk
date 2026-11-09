using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The edit-mode preview: an open inspector (the rig's or a joint's) poses the parts from
    /// the values, and every part's transform is given back as it was when the inspector
    /// closes, as play mode starts, before scripts reload, before the scene is saved and
    /// before a prefab is written -- so no preview pose reaches a saved file -- while the
    /// values stay; it shows again after a save while an inspector is open, and while any
    /// of the rig's inspectors is; it holds only a part a value moves off its rest, yields a
    /// part the person moves (the move stays) until a value moves again, and leaves a rig
    /// that follows its animation to it.
    /// </summary>
    public class ArticulatedRigPreviewTests
    {
        const string Folder = "Assets/UnitytkArticulatedRigPreviewTests";

        ArticulatedRig _rig;
        Transform _elbow;
        Quaternion _rest;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = ArmRig.Build(out Transform part);
            _rig = ArmRig.Rig(root, part);
            _elbow = _rig.joints[1].transform;
            _rest = _elbow.localRotation;
            _rig.joints[1].rz = 40f;
        }

        [TearDown]
        public void TearDown()
        {
            ArticulatedRigPreview.ReleaseAll();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        bool Posed => Quaternion.Angle(_elbow.localRotation, Quaternion.Euler(0f, 0f, 40f)) < 1e-2f;

        bool AtRest => _elbow.localRotation == _rest;

        [Test]
        public void The_inspectors_preview_poses_the_parts_and_gives_them_back_as_it_closes()
        {
            var editor = UnityEditor.Editor.CreateEditor(_rig);
            try
            {
                Assert.That(ArticulatedRigPreview.Showing(_rig), Is.True, "the open inspector shows it");
                Assert.That(Posed, Is.True, "posed from its values");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            Assert.That(ArticulatedRigPreview.Showing(_rig), Is.False, "closed: given back");
            Assert.That(AtRest, Is.True, "its transform as it was");
            Assert.That(_rig.joints[1].rz, Is.EqualTo(40f), "the value stays");
        }

        [Test]
        public void A_joints_inspector_shows_its_rig_too()
        {
            var editor = UnityEditor.Editor.CreateEditor(_rig.joints[1]);
            try
            {
                Assert.That(ArticulatedRigPreview.Showing(_rig), Is.True);
                Assert.That(Posed, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            Assert.That(AtRest, Is.True);
        }

        [Test]
        public void The_preview_gives_the_parts_back_as_play_starts_and_before_a_reload()
        {
            var editor = UnityEditor.Editor.CreateEditor(_rig);
            try
            {
                ArticulatedRigPreview.OnPlayMode(PlayModeStateChange.ExitingEditMode);
                Assert.That(ArticulatedRigPreview.Showing(_rig), Is.False, "play mode: given back");
                Assert.That(AtRest, Is.True);

                ArticulatedRigPreview.Show(new[] { _rig });
                Assert.That(Posed, Is.True);
                ArticulatedRigPreview.ReleaseAll();  // AssemblyReloadEvents.beforeAssemblyReload's handler
                Assert.That(ArticulatedRigPreview.Showing(_rig), Is.False, "a reload: given back");
                Assert.That(AtRest, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void A_part_moved_while_its_inspector_is_open_keeps_the_move_after_it_closes()
        {
            var editor = UnityEditor.Editor.CreateEditor(_rig.joints[1]);
            var moved = new Vector3(-0.1f, 0.03f, 0.02f);
            Quaternion turned = Quaternion.Euler(0f, 15f, 40f);
            try
            {
                Assert.That(Posed, Is.True);
                _elbow.localPosition = moved;  // the Move and Rotate tools
                _elbow.localRotation = turned;
                ArticulatedRigPreview.Update();
                ArticulatedRigPreview.Update();
                Assert.That(_elbow.localPosition, Is.EqualTo(moved), "the preview yields to the person's move");
                Assert.That(_elbow.localRotation, Is.EqualTo(turned));
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            Assert.That(_elbow.localPosition, Is.EqualTo(moved), "closed: the move stays");
            Assert.That(_elbow.localRotation, Is.EqualTo(turned));
        }

        [Test]
        public void A_part_at_rest_is_never_held_so_a_move_made_to_it_stays()
        {
            Transform bottom = _rig.joints[0].transform;  // its value is 0: the preview does not move it
            var editor = UnityEditor.Editor.CreateEditor(_rig);
            var moved = new Vector3(0.2f, 0f, 0f);
            try
            {
                bottom.localPosition = moved;
                ArticulatedRigPreview.Update();
                Assert.That(bottom.localPosition, Is.EqualTo(moved), "not posed: at its rest value it is left alone");
                Assert.That(Posed, Is.True, "the elbow, off its rest, still is");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            Assert.That(bottom.localPosition, Is.EqualTo(moved), "and nothing gives it back");
        }

        [Test]
        public void A_value_moved_again_after_a_yield_poses_the_part_once_more()
        {
            var editor = UnityEditor.Editor.CreateEditor(_rig.joints[1]);
            try
            {
                _elbow.localRotation = Quaternion.Euler(0f, 0f, 5f);
                ArticulatedRigPreview.Update();
                Assert.That(Quaternion.Angle(_elbow.localRotation, Quaternion.Euler(0f, 0f, 5f)), Is.LessThan(1e-3f), "yielded");
                _rig.joints[1].rz = 70f;
                ArticulatedRigPreview.Update();
                Assert.That(Quaternion.Angle(_elbow.localRotation, Quaternion.Euler(0f, 0f, 70f)), Is.LessThan(1e-2f),
                            "a value moved again: posed again");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            Assert.That(Quaternion.Angle(_elbow.localRotation, Quaternion.Euler(0f, 0f, 5f)), Is.LessThan(1e-3f),
                        "closed: the transform as the person left it");
        }

        [Test]
        public void The_preview_shows_again_after_a_save_while_an_inspector_is_open()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            var editor = UnityEditor.Editor.CreateEditor(_rig);
            try
            {
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Scene.unity"), Is.True);
                Assert.That(AtRest, Is.True, "the save gave it back first");
                ArticulatedRigPreview.Update();
                Assert.That(ArticulatedRigPreview.Showing(_rig), Is.True, "the inspector is still open: shown again");
                Assert.That(Posed, Is.True);

                PrefabUtility.SaveAsPrefabAsset(_rig.transform.root.gameObject, Folder + "/Arm.prefab");
                ArticulatedRigPreview.Update();
                Assert.That(Posed, Is.True, "and after a prefab's write");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            ArticulatedRigPreview.Update();
            Assert.That(ArticulatedRigPreview.Showing(_rig), Is.False, "closed: not shown again");
            Assert.That(AtRest, Is.True);
        }

        [Test]
        public void The_rig_shows_while_any_of_its_inspectors_is_open_whichever_closes_first()
        {
            foreach (bool rigFirst in new[] { true, false })
            {
                var rigEditor = UnityEditor.Editor.CreateEditor(_rig);
                var jointEditor = UnityEditor.Editor.CreateEditor(_rig.joints[1]);
                Object.DestroyImmediate(rigFirst ? rigEditor : jointEditor);
                Assert.That(ArticulatedRigPreview.Showing(_rig), Is.True, "one inspector still open");
                Assert.That(Posed, Is.True);
                Object.DestroyImmediate(rigFirst ? jointEditor : rigEditor);
                Assert.That(ArticulatedRigPreview.Showing(_rig), Is.False, "both closed");
                Assert.That(AtRest, Is.True);
            }
            // The selection's hop from the rig to a joint (measured: Unity disables the old
            // inspector, then enables the new one): the rig shows on, at the values' pose.
            var hopFrom = UnityEditor.Editor.CreateEditor(_rig);
            Object.DestroyImmediate(hopFrom);
            var hopTo = UnityEditor.Editor.CreateEditor(_rig.joints[1]);
            try
            {
                Assert.That(ArticulatedRigPreview.Showing(_rig) && Posed, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(hopTo);
            }
            Assert.That(AtRest, Is.True);
        }

        [Test]
        public void A_rig_that_follows_its_animation_is_left_to_it()
        {
            _rig.followAnimation = true;
            _rig.joints[1].rz = 0f;  // stale: the clip, not the field, poses a following rig
            var clip = new AnimationClip();
            Quaternion frame = Quaternion.Euler(0f, 0f, 50f);
            string path = "arm_RIG/base/elbow";
            clip.SetCurve(path, typeof(Transform), "localRotation.x", AnimationCurve.Constant(0f, 1f, frame.x));
            clip.SetCurve(path, typeof(Transform), "localRotation.y", AnimationCurve.Constant(0f, 1f, frame.y));
            clip.SetCurve(path, typeof(Transform), "localRotation.z", AnimationCurve.Constant(0f, 1f, frame.z));
            clip.SetCurve(path, typeof(Transform), "localRotation.w", AnimationCurve.Constant(0f, 1f, frame.w));
            AnimationMode.StartAnimationMode();
            UnityEditor.Editor editor = null;
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(_rig.transform.root.gameObject, clip, 0.5f);
                AnimationMode.EndSampling();
                Assert.That(Quaternion.Angle(_elbow.localRotation, frame), Is.LessThan(1e-2f), "the Animation window's frame");
                editor = UnityEditor.Editor.CreateEditor(_rig);
                ArticulatedRigPreview.Update();
                Assert.That(Quaternion.Angle(_elbow.localRotation, frame), Is.LessThan(1e-2f), "the preview leaves it be");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(clip);
            }
            Assert.That(AtRest, Is.True, "the Animation window gives its parts back itself");
        }

        [Test]
        public void A_saved_scene_and_a_written_prefab_hold_the_parts_own_transforms()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string scene = Folder + "/Scene.unity";
            var editor = UnityEditor.Editor.CreateEditor(_rig);
            try
            {
                Assert.That(Posed, Is.True);
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scene), Is.True);
                Assert.That(AtRest, Is.True, "the save gave it back first");

                ArticulatedRigPreview.Show(new[] { _rig });
                Assert.That(Posed, Is.True);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(_rig.transform.root.gameObject, Folder + "/Arm.prefab");
                Assert.That(AtRest, Is.True, "the prefab's write gave it back first");
                Transform elbow = saved.GetComponentInChildren<ArticulatedRig>().joints[1].transform;
                Assert.That(elbow.localRotation, Is.EqualTo(_rest), "the prefab holds the rest");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            var reopened = EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            ArticulatedRig rig = Object.FindAnyObjectByType<ArticulatedRig>();
            Assert.That(reopened.IsValid() && rig != null);
            Assert.That(rig.joints[1].transform.localRotation, Is.EqualTo(_rest), "the saved scene holds the rest");
            Assert.That(rig.joints[1].rz, Is.EqualTo(40f), "and the value");
        }
    }
}
