using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// The one keeper of what render effects show outside play mode, inspector or none.
    /// There Unity sends an effect no OnDisable, OnDestroy or LateUpdate, so each writer an
    /// <see cref="RenderEffect.Apply"/> leaves holding renderers (an inspector edit, a script,
    /// an agent's code, a clip's preview) is held here, and its renderers are given back --
    /// their materials as authored, what it hid shown, its twins freed -- when the effect is
    /// removed (or its Add undone), disabled or deactivated, and before a scene is saved or
    /// closed, before a prefab is written, before scripts reload, as play mode starts and when
    /// a clip's preview stops.
    /// While the Animation window or a Timeline previews a clip (Animation mode), every live
    /// effect in the open scenes shows the values it samples, each editor update. So nothing
    /// an effect showed reaches a saved scene or outlives a reload; an effect in a prefab or
    /// model asset shows nothing at all (open the prefab, or place it in a scene).
    /// </summary>
    /// <remarks>
    /// It holds the effects by reference: a destroyed one is null to Unity, but its writer is
    /// a plain field still reachable, so it is given back all the same. The list is the
    /// registry the C# rules allow: it is empty after every domain reload (each one is given
    /// back first), and holds an effect only while it shows.
    /// </remarks>
    [InitializeOnLoad]
    internal static class RenderEffectsPreview
    {
        static readonly List<RenderEffect> _held = new List<RenderEffect>();
        // The object each held effect was on: what it covered is looked at again through it once it goes.
        static readonly List<Transform> _on = new List<Transform>();
        static bool _previewing;

        static RenderEffectsPreview()
        {
            RenderEffect.editModeKeeper = Hold;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            PrefabStage.prefabSaving += OnPrefabSaving;
        }

        /// <summary>
        /// Before Unity writes a prefab -- one made from a scene object (dragged into the
        /// Project window, <c>PrefabUtility.SaveAsPrefabAsset</c>) or an instance's overrides
        /// applied -- every held effect is given back, so the asset holds the renderers'
        /// materials as authored, never an effect's twin (in memory only, it would save as a
        /// missing material).
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

        /// <summary>How many effects it holds writing now.</summary>
        internal static int Count => _held.Count;

        /// <summary>Whether <paramref name="effect"/>'s values show through a writer it holds (its own, or its root's).</summary>
        internal static bool Showing(RenderEffect effect)
        {
            if (effect == null) return false;
            foreach (RenderEffect writing in effect.Writers())
                if (IndexOf(writing) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Show <paramref name="effects"/> now, each a loaded scene's (an inspector's edit): none
        /// in play mode, where each plays itself, nor one in a prefab asset or Prefab Mode.
        /// </summary>
        internal static void Show(IEnumerable<Object> effects)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool changed = false;
            foreach (Object o in effects)
                if (o is RenderEffect effect && effect != null && InScene(effect)) changed |= effect.Show();
            if (changed) SceneView.RepaintAll();
        }

        /// <summary>Give back what <paramref name="effects"/> show (their inspector closing).</summary>
        internal static void End(IEnumerable<Object> effects)
        {
            foreach (Object o in effects)
            {
                if (!(o is RenderEffect effect) || effect == null) continue;
                foreach (RenderEffect writing in effect.Writers()) Release(writing);
                Release(effect);
            }
        }

        /// <summary>Give back every renderer every held effect holds.</summary>
        internal static void ReleaseAll()
        {
            for (int i = _held.Count - 1; i >= 0; i--) Drop(i);
        }

        // RenderEffect.editModeKeeper: an Apply outside play mode left it writing. One in an asset (a
        // prefab's, a model's) is given back at once: a twin on an asset's renderer would be written into
        // the asset by its next save as a missing material.
        static void Hold(RenderEffect effect)
        {
            if (EditorUtility.IsPersistent(effect))
            {
                effect.Release();
                return;
            }
            if (IndexOf(effect) >= 0) return;
            _held.Add(effect);
            _on.Add(effect.transform);
        }

        static int IndexOf(RenderEffect effect)
        {
            for (int i = 0; i < _held.Count; i++)
                if (ReferenceEquals(_held[i], effect)) return i;
            return -1;
        }

        static void Release(RenderEffect effect)
        {
            int i = IndexOf(effect);
            if (i >= 0) Drop(i);
            else if (effect.writer != null && !Application.isPlaying) effect.Release();
        }

        // Its renderers given back (a destroyed effect's too: its writer is a plain field), and
        // every effect in its hierarchy told to look again, as its OnDisable's Touch would.
        static void Drop(int i)
        {
            RenderEffect effect = _held[i];
            Transform on = _on[i];
            Forget(i);
            effect.Release();
            if (on == null) return;
            foreach (RenderEffect other in on.root.GetComponentsInChildren<RenderEffect>(true)) other.dirty = true;
        }

        static void Forget(int i)
        {
            _held.RemoveAt(i);
            _on.RemoveAt(i);
        }

        static void Update()
        {
            // Removed (or its Add undone), disabled or deactivated: given back. One whose writer a
            // root above it took over holds nothing: let go.
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                RenderEffect effect = _held[i];
                if (effect == null || !effect.Live) Drop(i);
                else if (effect.writer == null) Forget(i);
            }
            // A clip's preview (the Animation window's, a Timeline's): every live effect in the open
            // scenes shows the values it samples; all given back as it stops.
            bool previewing = AnimationMode.InAnimationMode() && !EditorApplication.isPlayingOrWillChangePlaymode;
            if (previewing) ShowAll();
            else if (_previewing) ReleaseAll();
            _previewing = previewing;
        }

        static void ShowAll()
        {
            bool changed = false;
            foreach (RenderEffect effect in Object.FindObjectsByType<RenderEffect>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (effect.Live && InScene(effect)) changed |= effect.Show();
            if (changed) SceneView.RepaintAll();
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) ReleaseAll();
        }

        static void OnSceneSaving(Scene scene, string path) => ReleaseIn(scene);

        static void OnSceneClosing(Scene scene, bool removing) => ReleaseIn(scene);

        static void OnPrefabSaving(GameObject prefab) => ReleaseAll();

        // Every held effect on an object of scene, or on none any more.
        static void ReleaseIn(Scene scene)
        {
            for (int i = _held.Count - 1; i >= 0; i--)
                if (_on[i] == null || _on[i].gameObject.scene == scene) Drop(i);
        }

        // A loaded scene's object, outside any prefab asset or Prefab Mode.
        static bool InScene(RenderEffect effect)
            => effect.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewSceneObject(effect.gameObject)
               && !EditorUtility.IsPersistent(effect);
    }
}
