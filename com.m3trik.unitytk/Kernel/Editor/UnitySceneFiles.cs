using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// This editor's scenes, as <see cref="RunRules"/> asks for them: a path a request
    /// names is this editor's project folder (nothing more to open) or a <c>.unity</c>
    /// scene inside it; the first opens alone, the rest beside it.
    /// </summary>
    internal sealed class UnitySceneFiles : ISceneFiles
    {
        /// <summary>This editor's project folder (never the process's working folder, which a launcher sets).</summary>
        public static string Project => Full(Path.GetDirectoryName(Application.dataPath));

        /// <inheritdoc/>
        /// <exception cref="ArgumentException">A path outside this project, or neither a scene nor the project.</exception>
        /// <exception cref="FileNotFoundException">No scene at a path.</exception>
        public void Open(IList<string> paths)
        {
            string project = Project;
            var scenes = new List<string>();
            foreach (string path in paths)  // every path checked before any is opened
            {
                string full = Full(path);
                if (Directory.Exists(full))
                {
                    if (!string.Equals(full, project, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException($"{path} is another project than this editor's ({project})");
                    continue;  // the project itself: nothing more to open
                }
                if (!full.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"{path} is neither a .unity scene nor this project");
                if (!full.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"{path} is outside this editor's project ({project})");
                if (!File.Exists(full)) throw new FileNotFoundException($"no scene at {path}");
                scenes.Add(full.Substring(project.Length + 1));
            }
            for (int i = 0; i < scenes.Count; i++)
                EditorSceneManager.OpenScene(scenes[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
        }

        /// <inheritdoc/>
        public IList<string> Modified()
            => Loaded().Where(s => s.isDirty && !string.IsNullOrEmpty(s.path)).Select(s => Full(s.path)).ToList();

        /// <inheritdoc/>
        public bool Unsaved() => Loaded().Any(s => s.isDirty);

        public bool Playing() => EditorApplication.isPlayingOrWillChangePlaymode;

        /// <inheritdoc/>
        public void Changed()
        {
            foreach (Scene scene in Loaded()) EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <inheritdoc/>
        /// <exception cref="IOException">The editor did not save one.</exception>
        public void Save(IList<string> paths)
        {
            var keys = new HashSet<string>(paths.Select(RunRules.Key));
            foreach (Scene scene in Loaded().Where(s => !string.IsNullOrEmpty(s.path) && keys.Contains(RunRules.Key(Full(s.path)))))
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException($"the editor did not save {scene.path}");
        }

        static IEnumerable<Scene> Loaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) yield return scene;
            }
        }

        /// <summary>A path in full, project-relative ones against the project, with one separator.</summary>
        static string Full(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string rooted = Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
            return Path.GetFullPath(rooted).Replace('\\', '/').TrimEnd('/');
        }
    }
}
