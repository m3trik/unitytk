using System;
using UnityEngine;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// What a window's fold arrow says (<see cref="Placard"/>, <see cref="PipView"/>, <see cref="DebugPanel"/>): its
    /// glyph while open and while folded, and the tooltip of each, so a project words and draws it in its own way.
    /// </summary>
    [Serializable]
    public sealed class WindowFold
    {
        [Tooltip("The arrow while the window is open (pressing it folds the window).")]
        public string open = "▾";

        [Tooltip("The arrow while the window is folded (pressing it opens the window).")]
        public string folded = "▸";

        [Tooltip("The open arrow's tooltip.")]
        public string foldTooltip = "Fold";

        [Tooltip("The folded arrow's tooltip.")]
        public string unfoldTooltip = "Unfold";

        /// <summary>The arrow's glyphs and tooltips, folding said as "Fold".</summary>
        public WindowFold()
        {
        }

        /// <summary>The arrow's glyphs and tooltips, folding said as <paramref name="foldTooltip"/>.</summary>
        /// <param name="foldTooltip">What the open arrow's tooltip says it does.</param>
        public WindowFold(string foldTooltip) => this.foldTooltip = foldTooltip;
    }
}
