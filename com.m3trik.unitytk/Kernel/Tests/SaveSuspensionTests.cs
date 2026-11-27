using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// The kernel's one hook around a write: before a scene or a prefab is written each keeper gives back what it
    /// holds, and once it is written each one it asked takes it again, the last first -- exactly those it asked (one
    /// added while it gave back waits for the next write), a keeper added twice asked once.
    /// </summary>
    public class SaveSuspensionTests
    {
        const string Folder = "Assets/UnitytkSaveSuspensionTests";

        readonly List<string> _said = new List<string>();
        readonly List<Keeper> _keepers = new List<Keeper>();

        // A keeper that says what it is asked.
        sealed class Keeper : ISaveSuspended
        {
            readonly string _name;
            readonly List<string> _said;
            public System.Action whileSuspending;
            public bool breaks;  // it throws as it is asked, either way

            public Keeper(string name, List<string> said)
            {
                _name = name;
                _said = said;
            }

            public void Suspend()
            {
                _said.Add("suspend " + _name);
                whileSuspending?.Invoke();
                if (breaks) throw new System.InvalidOperationException(_name + " broke giving back");
            }

            public void Resume()
            {
                _said.Add("resume " + _name);
                if (breaks) throw new System.InvalidOperationException(_name + " broke taking again");
            }
        }

        Keeper Add(string name)
        {
            var keeper = new Keeper(name, _said);
            _keepers.Add(keeper);
            SaveSuspension.Add(keeper);
            return keeper;
        }

        [SetUp]
        public void SetUp()
        {
            _said.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Keeper keeper in _keepers) SaveSuspension.Remove(keeper);
            _keepers.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }

        void Save() => Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Scene.unity"), Is.True);

        [Test]
        public void Each_keeper_gives_back_before_the_write_and_takes_again_after_it_the_last_first()
        {
            Add("first");
            Add("second");
            Save();
            Assert.That(_said, Is.EqualTo(new[] { "suspend first", "suspend second", "resume second", "resume first" }));
        }

        [Test]
        public void A_keeper_added_twice_is_asked_once()
        {
            Keeper keeper = Add("once");
            SaveSuspension.Add(keeper);
            Save();
            Assert.That(_said, Is.EqualTo(new[] { "suspend once", "resume once" }));
        }

        [Test]
        public void It_resumes_exactly_what_it_suspended()
        {
            Keeper first = Add("first");
            first.whileSuspending = () =>
            {
                first.whileSuspending = null;
                Add("late");  // added while the write gives back: never given back, so never taken again
            };
            Save();
            Assert.That(_said, Is.EqualTo(new[] { "suspend first", "resume first" }));
            _said.Clear();
            Save();
            Assert.That(_said, Is.EqualTo(new[] { "suspend first", "suspend late", "resume late", "resume first" }), "asked from the next write on");
        }

        // One keeper's throw ends no asking: those after it still give back before the file is written, and on the way
        // back those before it are still taken again.
        [Test]
        public void A_keeper_that_throws_is_said_and_every_other_is_still_asked_both_ways()
        {
            Add("first");
            Add("broken").breaks = true;
            Add("last");
            LogAssert.Expect(LogType.Exception, new Regex("broken broke giving back"));
            LogAssert.Expect(LogType.Exception, new Regex("broken broke taking again"));
            Save();
            Assert.That(_said, Is.EqualTo(new[]
            {
                "suspend first", "suspend broken", "suspend last", "resume last", "resume broken", "resume first",
            }));
        }

        // A prefab's write is taken back once the editor is back from the call that wrote it, so an image taken in that
        // same call (an exec's) takes it back first: it shows the scene with its looks and previews, as a person sees it.
        [Test]
        public void An_image_taken_in_the_call_that_wrote_a_prefab_shows_what_the_write_gave_back_taken_again()
        {
            Add("keeper");
            var made = new GameObject("Made");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(made, Folder + "/Made.prefab");
                Assert.That(_said, Is.EqualTo(new[] { "suspend keeper" }), "premise: given back for the write");
                SceneCapture.View(width: 8, height: 8);
                Assert.That(_said, Is.EqualTo(new[] { "suspend keeper", "resume keeper" }), "taken again before the image");
            }
            finally
            {
                Object.DestroyImmediate(made);
            }
        }

        [UnityTest]
        public IEnumerator A_prefabs_write_is_given_back_first_and_taken_again_once_the_editor_is_back()
        {
            Add("keeper");
            var made = new GameObject("Made");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(made, Folder + "/Made.prefab");
                Assert.That(_said, Is.EqualTo(new[] { "suspend keeper" }), "given back before the prefab is written");
                bool back = false;
                EditorApplication.delayCall += () => back = true;  // after the kernel's, which the write queued first
                while (!back) yield return null;
                Assert.That(_said, Is.EqualTo(new[] { "suspend keeper", "resume keeper" }), "taken again once the editor is back from the write");
            }
            finally
            {
                Object.DestroyImmediate(made);
            }
        }

        // A scene's write that fails raises sceneSaving and no sceneSaved: what its sceneSaving runs takes each keeper
        // again on its own once the editor is back.
        [UnityTest]
        public IEnumerator A_write_no_hook_follows_is_taken_again_once_the_editor_is_back()
        {
            Add("first");
            Add("second");
            SaveSuspension.BeforeWrite();  // what sceneSaving runs; no sceneSaved follows
            Assert.That(_said, Is.EqualTo(new[] { "suspend first", "suspend second" }), "given back for the write");
            bool back = false;
            EditorApplication.delayCall += () => back = true;  // after the kernel's, which BeforeWrite queued first
            while (!back) yield return null;
            Assert.That(_said, Is.EqualTo(new[] { "suspend first", "suspend second", "resume second", "resume first" }),
                        "taken again, the last first, with no sceneSaved");
            _said.Clear();
            Save();
            Assert.That(_said, Is.EqualTo(new[] { "suspend first", "suspend second", "resume second", "resume first" }),
                        "the next write asks them again, once each");
        }
    }
}
