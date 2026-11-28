using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>How long a <c>list</c> row's list stays on the placard (<see cref="TaskSettings.list"/>): the sheet's <c>until</c>.</summary>
    [Serializable]
    public sealed class ListSettings
    {
        [Tooltip("How long the list shows (a span): step, while its step runs; a step's ID, until that one starts; empty: " +
                 "until the next list row's step, or to the end.")]
        public string until = "";
    }
}
