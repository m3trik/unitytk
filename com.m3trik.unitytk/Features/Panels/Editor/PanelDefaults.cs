using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// What a window a person adds by hand (Add Component) is given, as the actions give the windows they
    /// make (<see cref="PanelsActions.MakePlacard"/>, <see cref="PanelsActions.MakePip"/>,
    /// <see cref="PanelsActions.MakeDebugPanel"/>): its document in the package's world space (<see cref="WorldSpace"/>:
    /// the world panel settings, whose theme holds the package's look, and a size that follows its content) and the
    /// placard's layout. Each only where it has none, so a person's own choice stays.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>ObjectFactory.componentWasAdded</c>, raised for the Add Component menu, is the
    /// editor's own hook for a component's first values from assets; a runtime <c>Reset</c> would need the
    /// asset database in the component's player code.
    /// </remarks>
    static class PanelDefaults
    {
        [InitializeOnLoadMethod]
        static void Register() => ObjectFactory.componentWasAdded += Added;

        /// <summary>Give <paramref name="added"/>, a window just added, what it lacks of the package's defaults.</summary>
        /// <param name="added">A component just added.</param>
        internal static void Added(Component added)
        {
            if (added is Placard || added is PipView || added is DebugPanel) Document(added, added is Placard);
        }

        /// <summary>
        /// Put <paramref name="document"/> in the package's world space, the one place a window's document is given it: the
        /// world panel settings (<see cref="PanelsActions.WorldPanelPath"/>) and a size that follows its content (Unity's own
        /// default is a fixed 1920 by 1080 panel pixels, nearly two metres by one at the package's scale).
        /// </summary>
        /// <param name="document">A window's document.</param>
        internal static void WorldSpace(UIDocument document)
        {
            document.panelSettings = PanelsActions.Load<PanelSettings>(PanelsActions.WorldPanelPath);
            document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Dynamic;
        }

        // The window's document: the package's world space where it has no panel settings, the placard's layout where it has none.
        static void Document(Component window, bool layout)
        {
            if (!window.TryGetComponent(out UIDocument document)) return;
            if (document.panelSettings == null) WorldSpace(document);
            if (layout && document.visualTreeAsset == null) document.visualTreeAsset = PanelsActions.Load<VisualTreeAsset>(PanelsActions.PlacardLayoutPath);
            OpContextEdits.Edited(null, document);  // part of the add, no context's: undoing it takes the window away whole
        }
    }
}
