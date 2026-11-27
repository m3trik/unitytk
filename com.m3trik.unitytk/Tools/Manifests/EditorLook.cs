using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Tools.Manifests.Tests")]

namespace M3trik.Unitytk.Tools.Manifests
{
    /// <summary>
    /// The manifest windows' look: one style sheet they wear, a dark, muted panel style: its colours as
    /// variables a window's own sheet reads (<c>--unitytk-error</c> and the rest of the status palette,
    /// the menu's), and the menus <see cref="WindowMenu"/> draws. A window adds its own sheet after it for
    /// what is its alone. Internal to this tool, its only user: it moves down once a second part wears it.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own editor sheets give a window the editor's skin, and a <c>ThemeStyleSheet</c>
    /// replaces a whole panel's theme (a runtime panel's). A window keeps Unity's controls as they are
    /// and adds this sheet for what the package draws itself: one file, read by path.
    /// </remarks>
    internal static class EditorLook
    {
        /// <summary>Where the look's style sheet is, as the asset database reads it.</summary>
        internal const string StylePath = "Packages/com.m3trik.unitytk/Tools/Manifests/EditorLook.uss";

        /// <summary>The class a window's root takes to wear the look.</summary>
        internal const string RootClass = "unitytk";

        /// <summary>The look's style sheet; null where the package is not installed by its name.</summary>
        internal static StyleSheet Sheet => AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);

        /// <summary>Have <paramref name="root"/> (a window's root) wear the look, once.</summary>
        /// <param name="root">The window's root element.</param>
        /// <returns>Whether the look was found (it is added once however often this is called).</returns>
        internal static bool Apply(VisualElement root)
        {
            if (root == null) return false;
            root.AddToClassList(RootClass);
            StyleSheet sheet = Sheet;
            if (sheet == null) return false;
            if (!root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            return true;
        }
    }
}
