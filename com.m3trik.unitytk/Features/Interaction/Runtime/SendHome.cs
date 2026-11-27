using UnityEngine;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// Sends its object's <see cref="Holdable"/> back where it rests each time it is enabled
    /// (a step enabling it as it ends: a tool put back where it was taken from). Made disabled
    /// by default, so nothing happens until something enables it.
    /// </summary>
    [AddComponentMenu("unitytk/Interaction/Send Home")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Holdable))]
    public sealed class SendHome : MonoBehaviour
    {
        void OnEnable()
        {
            var holdable = GetComponent<Holdable>();
            if (holdable != null) holdable.ReturnHome();
        }
    }
}
