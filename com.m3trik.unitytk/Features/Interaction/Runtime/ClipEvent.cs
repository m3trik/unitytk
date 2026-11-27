using System;
using UnityEngine.Events;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>An event about a clip, carrying its name.</summary>
    [Serializable]
    public sealed class ClipEvent : UnityEvent<string>
    {
    }
}
