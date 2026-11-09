using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// An image of the open scenes, for an agent to see what it built: this host's
    /// half of the <c>&lt;host&gt;.scene.view</c> operation every host of the tooling
    /// serves, with the same arguments and the same answer (a PNG's path in
    /// pythontk's temp root).
    /// </summary>
    /// <remarks>
    /// Prior art: <c>ScreenCapture</c> and the Scene view's own capture need a window,
    /// which a batch-mode editor has none of; a camera rendered into a
    /// <c>RenderTexture</c> needs none, as the built-in pipeline's and URP's own preview
    /// cameras do. It renders the way the active pipeline documents
    /// (<see cref="IRenderPipelineHost.Render"/>: <c>Camera.Render</c> on Built-in, a
    /// render request on URP); a pipeline no host serves renders as Built-in does.
    /// </remarks>
    public static class SceneCapture
    {
        /// <summary>
        /// The direction a framing camera looks from, toward what it frames: front
        /// right, above (the one every host's scene view frames from, in Unity's Y-up,
        /// Z-forward axes).
        /// </summary>
        public static readonly Vector3 CaptureFrom = new Vector3(1f, 0.75f, -1f);

        /// <summary>
        /// An image of the scene: a render of the open scenes to a PNG in pythontk's
        /// temp root, with the scene left as it was (no camera, object or setting kept).
        /// </summary>
        /// <returns>The PNG's path (the temp root sweeps it by age).</returns>
        /// <exception cref="ArgumentException"><paramref name="camera"/> names no camera, or <paramref name="objects"/> a name no object has.</exception>
        [Operation("unity.scene.view",
            "An image of the scene: a render of the open scenes to a PNG in pythontk's temp root, with the scene " +
            "left as it was (no camera, object or setting kept).",
            Effect = Effect.Read, Title = "An image of the scene")]
        public static string View(
            [Description("The objects to frame, by name or hierarchy path, from the front-right above (default: " +
                         "every renderer). Ignored with *camera*.")]
            string[] objects = null,
            [Description("A camera to look through (its object's name or path) instead.")]
            string camera = null,
            [Description("The image's width, in pixels.")]
            int width = 960,
            [Description("The image's height, in pixels.")]
            int height = 540)
        {
            if (width < 1 || height < 1 || width > 8192 || height > 8192)
                throw new ArgumentException($"the image is {width} x {height}: each side is 1 to 8192 pixels");
            Camera shot = null;
            if (camera != null)
            {
                GameObject named = Find(camera);
                if (named != null) shot = named.GetComponent<Camera>();
                if (shot == null) throw new ArgumentException($"No camera named '{camera}'.");
            }
            List<GameObject> targets = null;
            if (camera == null && objects != null && objects.Length > 0)
            {
                targets = objects.Select(Find).ToList();
                string[] missing = objects.Where((name, i) => targets[i] == null).ToArray();
                if (missing.Length > 0)
                    throw new ArgumentException($"No object named {string.Join(", ", missing.Select(n => $"'{n}'"))}.");
            }
            string path = CapturePath();
            GameObject temp = null;
            RenderTexture target = null, active = RenderTexture.active;
            Texture2D image = null;
            try
            {
                // Always a temporary camera: one set on a scene camera marks its scene changed.
                temp = EditorUtility.CreateGameObjectWithHideFlags("view_capture", HideFlags.HideAndDontSave, typeof(Camera));
                Camera view = temp.GetComponent<Camera>();
                if (shot != null) view.CopyFrom(shot);  // its settings and its transform
                else Frame(view, Extent(targets), width / (float)height);
                view.enabled = false;
                target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                (RenderPipelineHosts.Current ?? RenderPipelineHosts.BuiltIn).Render(view, target);
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = active;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (image != null) Object.DestroyImmediate(image);
                if (temp != null) Object.DestroyImmediate(temp);
            }
            return path;
        }

        /// <summary>Aim <paramref name="shot"/> from <see cref="CaptureFrom"/> so <paramref name="bounds"/> fills the shorter side.</summary>
        static void Frame(Camera shot, Bounds? bounds, float aspect)
        {
            Vector3 center = bounds?.center ?? Vector3.zero;
            float radius = Mathf.Max(bounds?.extents.magnitude ?? 1f, 1e-3f);
            // The field of view spans the height; when the image is taller than wide, the width must hold it.
            float half = Mathf.Tan(shot.fieldOfView * Mathf.Deg2Rad / 2f);
            float fov = 2f * Mathf.Atan(aspect >= 1f ? half : half * aspect);
            float distance = radius / Mathf.Sin(fov / 2f) * 1.05f;
            Vector3 facing = CaptureFrom.normalized;
            shot.transform.position = center + facing * distance;
            shot.transform.rotation = Quaternion.LookRotation(-facing, Vector3.up);
            shot.nearClipPlane = Mathf.Max(distance - radius * 2f, distance * 1e-3f);
            shot.farClipPlane = distance + radius * 2f;
            shot.clearFlags = CameraClearFlags.Skybox;
        }

        /// <summary>The bounds of every renderer under <paramref name="targets"/> (all renderers when null); null when none.</summary>
        static Bounds? Extent(List<GameObject> targets)
        {
            IEnumerable<Renderer> renderers = targets == null
                ? Roots().SelectMany(r => r.GetComponentsInChildren<Renderer>(false))
                : targets.SelectMany(t => t.GetComponentsInChildren<Renderer>(false));
            Bounds? all = null;
            foreach (Renderer renderer in renderers.Where(r => r.enabled))
            {
                if (all == null) all = renderer.bounds;
                else
                {
                    Bounds grown = all.Value;
                    grown.Encapsulate(renderer.bounds);
                    all = grown;
                }
            }
            if (all == null && targets != null && targets.Count > 0)
            {
                var around = new Bounds(targets[0].transform.position, Vector3.one);
                foreach (GameObject t in targets.Skip(1)) around.Encapsulate(t.transform.position);
                all = around;
            }
            return all;
        }

        /// <summary>The object <paramref name="name"/> names (its name, or its path from a root), inactive ones too.</summary>
        static GameObject Find(string name)
        {
            foreach (GameObject root in Roots())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name || PathOf(t) == name.TrimStart('/'))
                        return t.gameObject;
            return null;
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static IEnumerable<GameObject> Roots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    foreach (GameObject root in scene.GetRootGameObjects())
                        yield return root;
            }
        }

        /// <summary>A new PNG path in pythontk's temp root, under <c>view_capture/</c> (its folder made).</summary>
        static string CapturePath()
        {
            string folder = Path.Combine(TempRoot(), "view_capture");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"view_capture_{DateTime.UtcNow:yyyyMMddHHmmss}-{System.Diagnostics.Process.GetCurrentProcess().Id}-{Guid.NewGuid():N}.png");
        }

        /// <summary>
        /// pythontk's temp root: two folders above the run folder (<c>&lt;root&gt;/ops_run/&lt;run&gt;</c>)
        /// or the session folder (<c>&lt;root&gt;/ops_session/&lt;session&gt;</c>); else where
        /// pythontk puts it (<c>$PYTHONTK_TEMP_ROOT</c>, or <c>ptk</c> in the temp folder).
        /// </summary>
        static string TempRoot()
        {
            string near = RunFolder.Current ?? SessionServer.Folder;
            string root = near == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(near)));
            if (!string.IsNullOrEmpty(root)) return root;
            string set = Environment.GetEnvironmentVariable("PYTHONTK_TEMP_ROOT");
            return string.IsNullOrEmpty(set)
                ? Path.Combine(Path.GetTempPath(), "ptk")
                : Environment.ExpandEnvironmentVariables(set);
        }
    }
}
