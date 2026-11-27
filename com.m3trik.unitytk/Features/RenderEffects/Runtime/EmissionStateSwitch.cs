using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// Switches its object's <see cref="EmissionTrack"/> to the named <see cref="state"/> each
    /// time it is enabled (a sequence's step enabling it as it starts: "the LED turns amber"),
    /// and leaves it there. Several may sit on one track (green, amber, red...), each enabled
    /// by its own moment.
    /// </summary>
    /// <remarks>
    /// Prior art: a keyed state (<c>key_state</c> in the authoring tool) plays at a time in a
    /// clip; a state a step asks for switches when the step gets there, which no clip knows.
    /// The track stays the one that shows the emission: a switch only names its state.
    /// glTF's <c>KHR_materials_variants</c> (a whole material swapped by a variant's name) was
    /// weighed and not adopted: a state tints one material's emission, so one track shows
    /// every state, a clip can key it, and no material is made per state; an Animator
    /// parameter would need a controller per indicator.
    /// </remarks>
    [AddComponentMenu("Rendering/Emission State Switch")]
    [RequireComponent(typeof(EmissionTrack))]
    public sealed class EmissionStateSwitch : MonoBehaviour
    {
        [Tooltip("The Emission Track's state it switches to, by name (off, green, amber, red...).")]
        public string state;

        /// <summary>Switch the track to <see cref="state"/> now (what enabling it does).</summary>
        /// <returns>False, the track unchanged, when it has no such state (said) or no track.</returns>
        public bool Switch()
        {
            var track = GetComponent<EmissionTrack>();
            if (track == null || !track.SetState(state)) return false;
            track.Apply();
            return true;
        }

        void OnEnable() => Switch();
    }
}
