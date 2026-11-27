using UnityEditor;
using UnityEditor.SceneManagement;

namespace M3trik.Unitytk.Looks
{
    /// <summary>
    /// The editor's part of a <see cref="SceneLook"/>: a look in a scene the editor only previews in (a model's
    /// import, Prefab Mode) is never shown, by the editor's own answer to which scenes those are.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>EditorSceneManager.IsPreviewScene</c>, the editor's own answer to which scenes it
    /// only previews in; showing a look only in Play Mode was weighed and not adopted: a person tunes it in the
    /// Scene and Game views.
    /// </remarks>
    internal static class SceneLookPreviewScenes
    {
        [InitializeOnLoadMethod]
        static void Register() => SceneLook.Previewed = EditorSceneManager.IsPreviewScene;
    }
}
