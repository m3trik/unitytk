using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.ShadowPlane;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ShadowPlaneComponent = M3trik.Unitytk.ShadowPlane.ShadowPlane;

namespace M3trik.Unitytk.Tools.ShadowPlane.Tests
{
    /// <summary>
    /// The menu's bakes, whose overwrite of a map on disk no undo brings back: planned
    /// first, they list the maps they write over for the person to confirm, and declined
    /// they change nothing; confirmed, they run; with nothing to overwrite they ask nothing.
    /// </summary>
    public class ShadowPlaneMenuTests
    {
        const string Folder = "Assets/UnitytkShadowPlaneMenuTests";

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Scene.unity");
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }

        static ShadowPlaneComponent Crate()
        {
            var target = new GameObject("Crate");
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(target.transform, false);
            box.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            return ShadowPlaneActions.Create(target);
        }

        [Test]
        public void A_bake_over_a_map_on_disk_asks_and_declined_changes_nothing()
        {
            ShadowPlaneComponent plane = Crate();
            string path = AssetDatabase.GetAssetPath(plane.horizonMap);
            byte[] was = File.ReadAllBytes(path);
            plane.target.GetChild(0).localScale = new Vector3(1f, 2f, 1f);  // the target grew: its map is stale
            var asked = new List<string>();

            Assert.That(ShadowPlaneMenu.RebakeStale((title, message) =>
            {
                asked.Add(message);
                return false;
            }), Is.EqualTo(0));
            Assert.That(asked, Has.Count.EqualTo(1), "the person is asked once");
            Assert.That(asked[0], Does.Contain(path).And.Contain("No undo brings"), "the overwrite is named before it happens");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(was), "declined: the map stays");

            Assert.That(ShadowPlaneMenu.Bake(new[] { plane }, (title, message) => true), Is.EqualTo(1), "confirmed: it bakes");
            Assert.That(File.ReadAllBytes(path), Is.Not.EqualTo(was), "the map rebaked from the grown target");
        }

        [Test]
        public void A_duplicated_planes_bake_writes_a_new_map_and_the_ask_says_so()
        {
            ShadowPlaneComponent plane = Crate();
            string own = AssetDatabase.GetAssetPath(plane.horizonMap);
            GameObject twin = Object.Instantiate(plane.target.gameObject);  // Ctrl+D
            ShadowPlaneComponent copy = twin.GetComponentInChildren<ShadowPlaneComponent>();
            byte[] shared = File.ReadAllBytes(own);
            plane.target.GetChild(0).localScale = new Vector3(1f, 2f, 1f);
            copy.target.GetChild(0).localScale = new Vector3(1f, 3f, 1f);
            var asked = new List<string>();

            Assert.That(ShadowPlaneMenu.Bake(new[] { copy }, (title, message) =>
            {
                asked.Add(message);
                return true;
            }), Is.EqualTo(1));
            Assert.That(asked, Is.Empty, "a new file overwrites nothing: nothing to ask");
            Assert.That(AssetDatabase.GetAssetPath(copy.horizonMap), Is.Not.EqualTo(own), "the copy's own map");
            Assert.That(File.ReadAllBytes(own), Is.EqualTo(shared), "the map the original holds stays");

            GameObject third = Object.Instantiate(plane.target.gameObject);
            ShadowPlaneComponent other = third.GetComponentInChildren<ShadowPlaneComponent>();
            other.name = "Crate_shadow_third";
            other.target.GetChild(0).localScale = new Vector3(1f, 4f, 1f);
            Assert.That(ShadowPlaneMenu.Bake(new[] { copy, other }, (title, message) =>
            {
                asked.Add(message);
                return false;
            }), Is.EqualTo(0));
            Assert.That(asked, Has.Count.EqualTo(1), "the copy's map is written over: asked");
            Assert.That(asked[0], Does.Contain(AssetDatabase.GetAssetPath(copy.horizonMap)), "the overwrite, named");
            Assert.That(asked[0], Does.Contain("another plane").And.Contain(other.name),
                        "...and the plane that gets a new map, since another holds its map");
        }

        [Test]
        public void With_no_map_to_overwrite_it_asks_nothing()
        {
            ShadowPlaneComponent plane = Crate();
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(plane.horizonMap));
            int asked = 0;
            Assert.That(ShadowPlaneMenu.Bake(new[] { plane }, (title, message) =>
            {
                asked++;
                return false;
            }), Is.EqualTo(1));
            Assert.That(asked, Is.EqualTo(0), "nothing is written over, so nothing is asked");
            Assert.That(plane.CanMarch, Is.True);
        }
    }
}
