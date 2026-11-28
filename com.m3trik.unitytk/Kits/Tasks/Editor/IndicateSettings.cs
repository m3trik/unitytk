using System;
using M3trik.Unitytk.Indicators;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>How an <c>indicate</c> row's arrow looks (<see cref="TaskSettings.indicate"/>): the sheet's <c>style</c>, <c>size</c> and <c>color</c>, and the kit's own.</summary>
    [Serializable]
    public sealed class IndicateSettings
    {
        /// <summary>The <see cref="merge"/> of new settings: things a hand's width apart share an arrow.</summary>
        public const float DefaultMerge = 0.15f;

        [Tooltip("How the arrow the kit makes is drawn. Flat: it turns about its own axis to face the user's eyes. Solid: the same from every side.")]
        public IndicateStyle style = IndicateStyle.Flat;

        [Tooltip("How long the arrow is, tip to tail, in metres (the scale of the parts it points at).")]
        [Min(0.01f)]
        public float size = Indicator.DefaultSize;

        [Tooltip("The arrow's colour (the highlight's, so the task's cues read as one).")]
        public Color color = RenderEffects.Highlight.DefaultColor;

        [Tooltip("Things one row points at that stand this close together (metres between them) share one arrow, pointed at " +
                 "them all: two latches side by side. 0: an arrow at each.")]
        [Min(0f)]
        public float merge = DefaultMerge;

        [Tooltip("When it shows at something the user acts on. Ready: once what that needs is in hand, until they have acted on " +
                 "it. Step: for the whole step.")]
        public CueWhen cue = CueWhen.Ready;
    }
}
