using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Lightmaps;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Tools.Lightmaps.Tests
{
    /// <summary>
    /// The menu's Apply, whose clear of Unity's bake no undo brings back: planned first,
    /// it lists the files the clear deletes for the person to confirm, and declined it
    /// changes nothing; confirmed, it runs; with nothing to delete it asks nothing.
    /// </summary>
    public class LightmapsMenuTests
    {
        const string Folder = "Assets/UnitytkLightmapsMenuTests";
        const string Data = Folder + "/LightingData.asset";

        Texture2D _map;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            _map = new Texture2D(4, 4) { name = "Room_Lightmap" };
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Object.DestroyImmediate(_map);
            AssetDatabase.DeleteAsset(Folder);
        }

        // A set lighting one renderer, in the active scene.
        LightmapSet Room()
        {
            var room = new GameObject("Room");
            var part = new GameObject("Part");
            part.transform.SetParent(room.transform, false);
            Renderer r = part.AddComponent<MeshRenderer>();
            var set = room.AddComponent<LightmapSet>();
            set.maps.Add(new LightmapSet.Map { color = _map });
            set.receivers.Add(new LightmapSet.Receiver { renderer = r, map = 0 });
            return set;
        }

        // The active scene saved with a Lighting Data Asset of its own: a scene Unity's bake owns.
        static Scene Baked()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, Folder + "/Scene.unity");
            var bake = new LightingDataAsset(scene);
            AssetDatabase.CreateAsset(bake, Data);
            Lightmapping.SetLightingDataAssetForScene(scene, bake);
            return scene;
        }

        [Test]
        public void Declined_it_deletes_nothing_and_confirmed_it_clears_the_bake()
        {
            LightmapSet set = Room();
            Scene scene = Baked();
            var asked = new List<string>();

            Assert.That(LightmapsMenu.Apply((title, message) =>
            {
                asked.Add(message);
                return false;
            }), Is.EqualTo(0));
            Assert.That(asked, Has.Count.EqualTo(1), "the person is asked once");
            Assert.That(asked[0], Does.Contain(Data).And.Contain("No undo brings them back"), "the delete is named before it happens");
            Assert.That(File.Exists(Data), Is.True, "declined: the bake stays");
            Assert.That(Lightmapping.GetLightingDataAssetForScene(scene), Is.Not.Null);

            Assert.That(LightmapsMenu.Apply((title, message) => true), Is.EqualTo(1), "confirmed: it applies");
            Assert.That(File.Exists(Data), Is.False, "the bake cleared");
            Assert.That(Object.FindObjectsByType<LightmapSet>(FindObjectsSortMode.None)[0].Bound, Is.True);
            Assert.That(set, Is.Not.Null);
        }

        [Test]
        public void With_no_bake_to_clear_it_asks_nothing()
        {
            LightmapSet set = Room();
            int asked = 0;
            Assert.That(LightmapsMenu.Apply((title, message) =>
            {
                asked++;
                return false;
            }), Is.EqualTo(1));
            Assert.That(asked, Is.EqualTo(0), "nothing is deleted, so nothing is asked");
            Assert.That(set.Bound, Is.True);
        }
    }
}
