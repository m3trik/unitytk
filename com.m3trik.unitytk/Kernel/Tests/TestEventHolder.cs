using System;
using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// A stand-in for an object holding events, and the methods their calls call: an asset, since an editor-only test
    /// assembly's components cannot be added to objects.
    /// </summary>
    public sealed class TestEventHolder : TestEventHolderBase
    {
        /// <summary>An event with nothing to carry.</summary>
        public UnityEvent clicked = new UnityEvent();

        /// <summary>An event carrying a name.</summary>
        public UnityEvent<string> named = new UnityEvent<string>();

        [SerializeField] UnityEvent _kept = new UnityEvent();

        /// <summary>An event no serialized field holds.</summary>
        [NonSerialized] public UnityEvent unsaved = new UnityEvent();

        /// <summary>An event in a private serialized field.</summary>
        public UnityEvent Kept => _kept;

        /// <summary>A call's method taking a string.</summary>
        public void Say(string text) { }

        /// <summary>A call's method taking a bool.</summary>
        public void Switch(bool on) { }

        /// <summary>A call's method taking a float.</summary>
        public void Scale(float by) { }

        /// <summary>A call's method taking an int.</summary>
        public void Count(int times) { }

        /// <summary>A call's method taking nothing.</summary>
        public void Ping() { }
    }
}
