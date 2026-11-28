using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>How a <c>highlight</c> row glows (<see cref="TaskSettings.highlight"/>): the sheet's <c>style</c> and <c>color</c>, and the kit's own.</summary>
    [Serializable]
    public sealed class HighlightSettings
    {
        [Tooltip("How it glows. Auto: pulsing where the task has the user act on the object, steady where they only look. " +
                 "Pulse and Steady say it outright (Steady: a lamp turned on).")]
        public HighlightStyle style = HighlightStyle.Auto;

        [Tooltip("The colour it glows in. By default the colour the delivery's own highlights are keyed in, so one built here " +
                 "and one built there read alike.")]
        [ColorUsage(false, true)]
        public Color color = RenderEffects.Highlight.DefaultColor;

        [Tooltip("A pulse goes on as long as its step runs; off: it pulses once, then glows steady.")]
        public bool loop = true;

        [Tooltip("When it shows at something the user acts on. Ready: once what that needs is in hand, until they have acted on " +
                 "it (the keys are pointed out first, then the locks). Step: for the whole step.")]
        public CueWhen cue = CueWhen.Ready;
    }
}
