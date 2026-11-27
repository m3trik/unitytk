using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Steps
{
    /// <summary>
    /// Gives <see cref="Sequence.Restart"/> the editor's Play Mode load: a scene the build does not list
    /// is loaded again from its asset (alone, or beside the scenes loaded with it, as the sequence's reload mode says), so starting over (and a debug panel's Back) undoes what the steps
    /// changed in the editor as in a player.
    /// </summary>
    /// <remarks>
    /// Prior art: <c>SceneManager.LoadScene</c> loads only a scene the build lists; Play Mode loads any
    /// scene asset through <c>EditorSceneManager.LoadSceneInPlayMode</c>, Unity's documented way, which a
    /// player's code cannot call.
    /// </remarks>
    internal static class SequenceReload
    {
        [InitializeOnLoadMethod]
        static void Register() => Sequence.LoadAgain = Load;

        static bool Load(Scene scene, LoadSceneMode mode)
        {
            if (!EditorApplication.isPlaying || string.IsNullOrEmpty(scene.path)) return false;
            string path = scene.path;
            if (mode == LoadSceneMode.Single) EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            else Sequence.ReplaceScene(scene, () => EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive)));
            return true;
        }
    }
}
