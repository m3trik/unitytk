using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>How a <c>hide</c> row takes its object away (<see cref="TaskSettings.hide"/>): the sheet's <c>fade</c>, and the kit's own.</summary>
    [Serializable]
    public sealed class HideSettings
    {
        [Tooltip("How long it takes to go, in seconds (0: at once).")]
        [Min(0f)]
        public float fade = RenderEffects.FadeTo.DefaultSeconds;

        [Tooltip("Hide an object its step also sends back where it rests, as the sheet says: it rests there hidden. Off: it " +
                 "goes back shown, the return being how it leaves the hand.")]
        public bool returned;
    }
}
