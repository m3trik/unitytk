using System.Collections.Generic;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// The scene files of the application a run is answered in: pythontk's
    /// <c>SceneFiles</c>, the seam between <see cref="RunRules"/> (host-free) and an
    /// editor. <see cref="PureSceneFiles"/> is the stand-in that holds none.
    /// </summary>
    public interface ISceneFiles
    {
        /// <summary>
        /// Open <paramref name="paths"/>, the first as the main scene; throw, having
        /// opened none, when one cannot be.
        /// </summary>
        void Open(IList<string> paths);

        /// <summary>The open scene files that have unsaved changes (full paths).</summary>
        IList<string> Modified();

        /// <summary>Whether what it holds has changes no file has (a scene never saved included).</summary>
        bool Unsaved();

        /// <summary>
        /// Mark what it holds as changed: an editor's own flag misses an edit made
        /// from code. Called before a live run that keeps no journal (a save the
        /// code makes clears the mark) and after one whose journal names a change.
        /// </summary>
        void Changed();

        /// <summary>Save <paramref name="paths"/> where they are.</summary>
        void Save(IList<string> paths);

        /// <summary>
        /// Whether the application is playing its scenes (Unity's play mode), where
        /// what a call changes in them is lost when play ends and nothing can be saved.
        /// </summary>
        bool Playing();
    }
}
