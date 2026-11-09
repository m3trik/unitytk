using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// A file a record names, found in its model's media: the folders the model's media
    /// lives in, asked in order (the contract <c>IModelMedia.MediaFolders</c> gives them,
    /// nearest first), by the file's name alone.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own search by name (<c>AssetDatabase.FindAssets</c>) is
    /// project-wide, so of two models in one folder embedding a map of one name the
    /// second bound the first's copy; the folders are asked instead. Three
    /// importers (Lightmaps, Materials, ShadowPlane) each kept a copy, which drifted:
    /// one read a record naming a folder for its file name alone, two refused no
    /// folders; this is their union, moved down.
    /// </remarks>
    public static class MediaFiles
    {
        /// <summary>
        /// The asset path of <paramref name="file"/> (its name alone: a record naming a
        /// folder, by either separator, is read for the file in it) in the first of
        /// <paramref name="folders"/> holding a texture by that name, or null: no folders,
        /// no name, or none holds it.
        /// </summary>
        public static string Nearest(string[] folders, string file) => Nearest<Texture>(folders, file);

        /// <summary>
        /// <see cref="Nearest(string[], string)"/> for an asset of type
        /// <typeparamref name="T"/>: the first folder holding one by that name.
        /// </summary>
        public static string Nearest<T>(string[] folders, string file) where T : Object
            => First(folders, file, path => AssetDatabase.LoadAssetAtPath<T>(path) != null);

        /// <summary>
        /// The asset path of <paramref name="file"/> (its name alone, as <see cref="Nearest(string[], string)"/>
        /// reads it) in the first of <paramref name="folders"/> whose folder holds the file on
        /// disk, loaded or not (an import can run before the file's own), or null.
        /// </summary>
        public static string OnDisk(string[] folders, string file) => First(folders, file, System.IO.File.Exists);

        // The first folder's path for the file's name that holds, by the one rule.
        static string First(string[] folders, string file, System.Func<string, bool> holds)
        {
            if (string.IsNullOrEmpty(file) || folders == null) return null;
            string name = file.Substring(file.LastIndexOfAny(new[] { '/', '\\' }) + 1);
            if (name.Length == 0) return null;
            foreach (string dir in folders)
            {
                if (dir == null) continue;
                string path = dir.Length > 0 ? dir.TrimEnd('/') + "/" + name : name;
                if (holds(path)) return path;
            }
            return null;
        }
    }
}
