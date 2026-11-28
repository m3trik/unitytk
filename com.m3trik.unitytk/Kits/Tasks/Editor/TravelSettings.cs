using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>When a <c>travel</c> row brings the user, and how long its ring shows (<see cref="TaskSettings.travel"/>): the sheet's <c>when</c> and <c>ring</c>.</summary>
    [Serializable]
    public sealed class TravelSettings
    {
        [Tooltip("When the user is brought there: as its step starts, or as it ends (once its choice is made).")]
        public Moment when = Moment.Start;

        [Tooltip("How long the ring on the floor where the user stands shows (a span): step, while its step runs; a step's ID, " +
                 "from its step until that one starts; empty: no ring.")]
        public string ring = "";
    }
}
