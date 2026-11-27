using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The loaded scenes' switched-on component of a kind: who a part with no one named of its own acts
    /// for. The one lookup every part asks, so "switched on" means
    /// the same everywhere: on an active object and enabled.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>Object.FindAnyObjectByType</c> finds a component on an active object, enabled
    /// or not; this adds the enabled test. Which of several switched-on ones is found is unspecified, as
    /// with <c>FindObjectsByType</c>'s unsorted order.
    /// </remarks>
    public static class SceneObjects
    {
        /// <summary>The first switched-on <typeparamref name="T"/> found in the loaded scenes; null when none.</summary>
        /// <typeparam name="T">The component's type.</typeparam>
        public static T FirstEnabled<T>() where T : Behaviour
        {
            foreach (T each in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                if (each.isActiveAndEnabled) return each;
            return null;
        }
    }
}
