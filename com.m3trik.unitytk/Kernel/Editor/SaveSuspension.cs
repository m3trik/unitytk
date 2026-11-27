using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Kernel.Tests")]

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The editor's one hook around a write of a scene or a prefab: before Unity writes one, every
    /// <see cref="ISaveSuspended"/> gives back what it holds -- each component implementing it on an active object
    /// in the open scenes, then each keeper <see cref="Add"/> was given -- and once it is written, each one asked
    /// takes it again, in the reverse order. So a saved scene or prefab holds none of what a binding, a preview or a
    /// look changed, whichever open scene it changed.
    /// </summary>
    /// <remarks>
    /// A component disabled or deactivated gives its edits back itself (its <c>OnDisable</c>), so only active ones
    /// are asked. Exactly those a write asked take theirs again: one made, enabled or added while it gave back has
    /// nothing given back, and is asked from the next write on. Giving back is each one's to repeat (the contract's
    /// "nothing where it holds none"), so a write asks every one, one an unfinished prefab write asked too. The
    /// keepers are the registry the C# rules allow: plain objects, each added again by its owner as the editor's
    /// domain loads. What a write asked is held from its start to its end alone, which no domain reload interrupts.
    /// One that throws is said (the Console, with its stack) and the rest are still asked, so one broken keeper never
    /// leaves another's edits in the file nor another's state not taken again. A scene's write that fails raises no
    /// <c>sceneSaved</c>, so each is taken again once the editor is back from the call that wrote too (nothing then,
    /// where <c>sceneSaved</c> took it). A prefab's write has no hook once written: code that writes one and reads the
    /// scene within one call (an exec, a test) finds it given back until the editor is back from that call, but for
    /// <see cref="SceneCapture"/>, which takes each again first.
    ///
    /// Prior art: Unity's <c>EditorSceneManager.sceneSaving</c> and <c>sceneSaved</c>, the documented hooks around a
    /// scene's save, and <c>AssetModificationProcessor.OnWillSaveAssets</c>, the one call before Unity writes a
    /// prefab (one made from a scene object, an instance's overrides applied), which has no counterpart once the
    /// file is written: a prefab's write is taken back once the editor is back from the call that writes it
    /// (<c>EditorApplication.delayCall</c>). <c>PrefabStage.prefabSaving</c>, which the previews joined before, was
    /// not taken: they show nothing in Prefab Mode's preview scene.
    /// </remarks>
    public static class SaveSuspension
    {
        static readonly List<ISaveSuspended> _keepers = new List<ISaveSuspended>();

        // What the write under way asked, in the order it asked them: taken again the last first.
        static readonly List<ISaveSuspended> _asked = new List<ISaveSuspended>();

        [InitializeOnLoadMethod]
        static void Register()
        {
            EditorSceneManager.sceneSaving += (scene, path) =>
            {
                Suspend();
                EditorApplication.delayCall += Resume;  // a write that failed raises no sceneSaved
            };
            EditorSceneManager.sceneSaved += scene => Resume();
        }

        /// <summary>
        /// Before Unity writes a prefab: every one there is gives back what it holds, so the asset holds the objects'
        /// own state; taken again once the editor is back from the call that writes it.
        /// </summary>
        sealed class PrefabWrites : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                foreach (string path in paths)
                    if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        Suspend();
                        EditorApplication.delayCall += Resume;
                        break;
                    }
                return paths;
            }
        }

        /// <summary>
        /// Add <paramref name="keeper"/>, an editor keeper of what it shows on components (no component itself),
        /// asked around every write from now on; one added already is not added twice. Its owner adds it as the
        /// editor's domain loads (<c>[InitializeOnLoad]</c>).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="keeper"/> is null.</exception>
        public static void Add(ISaveSuspended keeper)
        {
            if (keeper == null) throw new ArgumentNullException(nameof(keeper), "name the keeper a save asks");
            if (!_keepers.Contains(keeper)) _keepers.Add(keeper);
        }

        /// <summary>Take <paramref name="keeper"/> out again, asked nothing more; whether it was there (a test's).</summary>
        internal static bool Remove(ISaveSuspended keeper)
        {
            _asked.Remove(keeper);
            return _keepers.Remove(keeper);
        }

        /// <summary>
        /// Every one there is gives back what a write would save of it: what a write runs first. Each is asked at every
        /// write, one a write not yet taken back asked too (a keeper's preview may have shown again since: giving back is
        /// its own to repeat), and is taken again once.
        /// </summary>
        internal static void Suspend()
        {
            var all = new List<ISaveSuspended>();
            foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (component is ISaveSuspended held) all.Add(held);
            all.AddRange(_keepers);
            foreach (ISaveSuspended held in all)
            {
                if (!_asked.Contains(held)) _asked.Add(held);
                Ask(held, true);
            }
        }

        /// <summary>
        /// Each one the write asked takes again what it gave back, the last first: what a write runs once written. A
        /// component destroyed since has nothing to take.
        /// </summary>
        internal static void Resume()
        {
            ISaveSuspended[] asked = _asked.ToArray();
            _asked.Clear();
            for (int i = asked.Length - 1; i >= 0; i--)
            {
                if (asked[i] is Object unity && unity == null) continue;
                Ask(asked[i], false);
            }
        }

        // One's turn: what it throws is said, and the next is asked all the same.
        static void Ask(ISaveSuspended held, bool suspend)
        {
            try
            {
                if (suspend) held.Suspend();
                else held.Resume();
            }
            catch (Exception thrown)
            {
                Debug.LogException(thrown, held as Object);
            }
        }
    }
}
