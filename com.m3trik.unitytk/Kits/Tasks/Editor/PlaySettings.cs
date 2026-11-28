using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// How a <c>play</c> row's take plays by itself (<see cref="TaskSettings.play"/>): the sheet's <c>after</c> and <c>speed</c>,
    /// and the stretch of it played (<see cref="ClipSettings"/>).
    /// </summary>
    [Serializable]
    public sealed class PlaySettings : ClipSettings
    {
        [Tooltip("The take of its step it plays after (the sheet's after); empty: the take of the row before it in its step.")]
        public string after = "";
    }
}
