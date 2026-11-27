using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>A base class holding an event of its own, which the object it is the base of saves with its own fields.</summary>
    public abstract class TestEventHolderBase : ScriptableObject
    {
        [SerializeField] UnityEvent _inherited = new UnityEvent();

        /// <summary>An event in a private serialized field of a base class.</summary>
        public UnityEvent Inherited => _inherited;
    }
}
