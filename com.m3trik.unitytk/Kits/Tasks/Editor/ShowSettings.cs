using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>How a <c>show</c> row brings its object in (<see cref="TaskSettings.show"/>): the sheet's <c>fade</c>.</summary>
    [Serializable]
    public sealed class ShowSettings
    {
        [Tooltip("How long it takes to appear, in seconds (0: at once).")]
        [Min(0f)]
        public float fade = RenderEffects.FadeTo.DefaultSeconds;
    }
}
