using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// The one keeper of what shadow planes show outside play mode. There a plane gets
    /// no LateUpdate, so an inspector that shows one (<see cref="Show"/>) hands it here:
    /// each editor update it is placed and faded from where its source and contact stand
    /// now, so dragging the light moves its shadow, and its placement is given back --
    /// its transform as it was, its block cleared, its cache slot freed -- when the
    /// inspector closes, the plane is removed, disabled or deactivated, and before a scene
    /// is saved or closed, before a prefab is written, before scripts reload and as play
    /// mode starts. So nothing a preview placed reaches a saved scene or prefab. A plane
    /// that does not follow its source is only faded: its transform is never held, so a
    /// move made to it stays. A plane in a prefab or model asset shows nothing (open the
    /// prefab, or place it in a scene).
    /// </summary>
    /// <remarks>
    /// The list is the registry the C# rules allow: empty after every domain reload (each
    /// plane is given back first), holding a plane only while it shows.
    /// </remarks>
    [InitializeOnLoad]
    internal static class ShadowPlanePreview
    {
        sealed class Held
        {
            public ShadowPlane plane;
            public Transform transform;
            /// <summary>The preview placed the plane, and holds its transform as it was.</summary>
            public bool placed;
            public Vector3 position, scale;
            public Quaternion rotation;
        }

        static readonly List<Held> _held = new List<Held>();

        static ShadowPlanePreview()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.sceneSaving += (scene, path) => ReleaseIn(scene);
            EditorSceneManager.sceneClosing += (scene, removing) => ReleaseIn(scene);
            PrefabStage.prefabSaving += prefab => ReleaseAll();
        }

        /// <summary>
        /// Before Unity writes a prefab -- one made from a scene object (dragged into the
        /// Project window, <c>PrefabUtility.SaveAsPrefabAsset</c>) or an instance's overrides
        /// applied -- every plane is given back, so the asset holds the plane's own transform,
        /// never the preview's placement.
        /// </summary>
        sealed class PrefabSaves : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                foreach (string path in paths)
                    if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                    {
                        ReleaseAll();
                        break;
                    }
                return paths;
            }
        }

        /// <summary>Whether <paramref name="plane"/> shows through the preview.</summary>
        internal static bool Showing(ShadowPlane plane) => IndexOf(plane) >= 0;

        /// <summary>
        /// Show <paramref name="planes"/> now, each a loaded scene's: none in play mode,
        /// where each places itself, nor one in a prefab asset or Prefab Mode's preview.
        /// </summary>
        internal static void Show(IEnumerable<Object> planes)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool shown = false;
            foreach (Object o in planes)
            {
                if (!(o is ShadowPlane plane) || plane == null || !InScene(plane) || !Live(plane)) continue;
                int i = IndexOf(plane);
                if (i < 0)
                {
                    _held.Add(new Held { plane = plane, transform = plane.transform });
                    i = _held.Count - 1;
                }
                Step(_held[i]);
                shown = true;
            }
            if (shown) SceneView.RepaintAll();
        }

        /// <summary>
        /// Apply the plane now. Its transform is held only while the preview places it
        /// (<see cref="ShadowPlane.Places"/>): taken as it is before the first placement,
        /// given back the moment it stops placing (follow source turned off, its contact or
        /// source gone), so a move made while only its fade follows is the person's, and stays.
        /// </summary>
        static void Step(Held held)
        {
            ShadowPlane plane = held.plane;
            if (plane.Places && !held.placed)
            {
                Transform t = held.transform;
                held.position = t.localPosition;
                held.rotation = t.localRotation;
                held.scale = t.localScale;
                held.placed = true;
            }
            else if (!plane.Places && held.placed)
            {
                GiveBack(held);
            }
            plane.ApplyNow();
        }

        // The transform as it was before the preview placed it.
        static void GiveBack(Held held)
        {
            if (held.placed && held.transform != null)
            {
                held.transform.localPosition = held.position;
                held.transform.localRotation = held.rotation;
                held.transform.localScale = held.scale;
            }
            held.placed = false;
        }

        /// <summary>Give back what <paramref name="planes"/> show (their inspector closing).</summary>
        internal static void End(IEnumerable<Object> planes)
        {
            foreach (Object o in planes)
            {
                int i = o is ShadowPlane plane ? IndexOf(plane) : -1;
                if (i >= 0) Drop(i);
            }
        }

        /// <summary>
        /// Before scripts reload: every plane given back, and the cache's atlas freed
        /// (<see cref="ShadowPlaneCache.Dispose"/>), which the reload would orphan: no static
        /// holds it after.
        /// </summary>
        internal static void BeforeReload()
        {
            ReleaseAll();
            ShadowPlaneCache.Dispose();
        }

        /// <summary>Give back every plane it shows.</summary>
        internal static void ReleaseAll()
        {
            for (int i = _held.Count - 1; i >= 0; i--) Drop(i);
        }

        static int IndexOf(ShadowPlane plane)
        {
            for (int i = 0; i < _held.Count; i++)
                if (ReferenceEquals(_held[i].plane, plane)) return i;
            return -1;
        }

        // Its transform as it was (where the preview placed it), its block cleared and its cache slot freed.
        static void Drop(int i)
        {
            Held held = _held[i];
            _held.RemoveAt(i);
            GiveBack(held);
            if (held.plane != null)
            {
                ShadowPlaneCache.Release(held.plane);
                Renderer r = held.plane.GetComponent<Renderer>();
                if (r != null) r.SetPropertyBlock(null);
            }
            SceneView.RepaintAll();
        }

        static void Update()
        {
            if (_held.Count == 0) return;
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                ShadowPlane plane = _held[i].plane;
                if (plane == null || !Live(plane)) Drop(i);
                else Step(_held[i]);
            }
        }

        internal static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) ReleaseAll();
        }

        // Every held plane on an object of scene, or on none any more.
        static void ReleaseIn(Scene scene)
        {
            for (int i = _held.Count - 1; i >= 0; i--)
                if (_held[i].transform == null || _held[i].transform.gameObject.scene == scene) Drop(i);
        }

        // Enabled on an active object (outside play mode, Unity sends it no OnEnable).
        static bool Live(ShadowPlane plane) => plane.enabled && plane.gameObject.activeInHierarchy;

        // A loaded scene's object, outside any prefab asset or Prefab Mode's preview.
        static bool InScene(ShadowPlane plane)
            => plane.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewSceneObject(plane.gameObject)
               && !EditorUtility.IsPersistent(plane);
    }
}
