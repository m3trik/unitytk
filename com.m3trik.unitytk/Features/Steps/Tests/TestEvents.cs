using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Steps.Tests
{
    /// <summary>
    /// A stand-in for another part holding events (a target's selection, a clip's end): an
    /// asset, since an editor-only test assembly's components cannot be added to objects.
    /// </summary>
    public sealed class TestEvents : ScriptableObject
    {
        /// <summary>An event with nothing to carry.</summary>
        public UnityEvent clicked = new UnityEvent();

        /// <summary>An event carrying a name.</summary>
        public UnityEvent<string> ended = new UnityEvent<string>();

        /// <summary>An event carrying a label chosen.</summary>
        public UnityEvent<string> chosen = new UnityEvent<string>();
    }
}
