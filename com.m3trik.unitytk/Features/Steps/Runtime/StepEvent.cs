using System;
using UnityEngine.Events;

namespace M3trik.Unitytk.Steps
{
    /// <summary>An event about a step, carrying it.</summary>
    [Serializable]
    public sealed class StepEvent : UnityEvent<Step>
    {
    }
}
