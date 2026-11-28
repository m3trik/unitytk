using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>What a <c>set</c> row switches its object to (<see cref="TaskSettings.set"/>): the sheet's <c>state</c>.</summary>
    [Serializable]
    public sealed class SetSettings
    {
        [Tooltip("The state its indicator light turns to, as the delivery names its states (off, green, blue, amber, red...). Required.")]
        public string state = "";
    }
}
