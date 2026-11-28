using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>When a <c>button</c> row holds its step, and what a wrong answer says (<see cref="TaskSettings.button"/>): the sheet's <c>when</c> and <c>reply</c>.</summary>
    [Serializable]
    public sealed class ButtonSettings
    {
        [Tooltip("Where a button that goes on (To next) holds its step. End: as it finishes, offered once its actions are done. " +
                 "Start: at its start, the button starting them (read first, then go).")]
        public Moment when = Moment.End;

        [Tooltip("What a wrong answer (To retry) says; the page stays.")]
        public string reply = "Try again.";
    }
}
