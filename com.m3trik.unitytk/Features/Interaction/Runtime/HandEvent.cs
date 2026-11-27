using System;
using UnityEngine.Events;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>An event a hand raised, carrying the hand (null where none was).</summary>
    [Serializable]
    public sealed class HandEvent : UnityEvent<Hand>
    {
    }
}
