using UnityEngine;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// Selects its <see cref="Target"/> by itself once what the target requires, held in a hand,
    /// comes within <see cref="reach"/> of it: a part brought to its slot. It selects for the hand
    /// that holds it (<see cref="Holdable.Holder"/>), whichever hand that is, and acts while enabled
    /// (a step enables it as it runs), once each time.
    /// </summary>
    /// <remarks>
    /// Prior art: the XR Interaction Toolkit's socket interactor snaps a grab interactable into
    /// place by its own physics and grab rules, which a <see cref="Holdable"/> (posed each frame,
    /// lent to clips) does not use; a slot here is a target, so coming near it is a selection, and
    /// what a selection plays is the target's own wiring.
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Select Near")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Target))]
    public sealed class SelectNear : MonoBehaviour
    {
        [Tooltip("How near what the target requires must come, in metres, from its origin to the target's.")]
        [Min(0f)]
        public float reach = 0.15f;

        bool _done;
        Target _target;

        Target Owner => _target != null ? _target : _target = GetComponent<Target>();

        /// <summary>Whether <paramref name="hand"/> holds what the target requires within reach of it.</summary>
        /// <param name="hand">A hand (null: none).</param>
        public bool InReach(Hand hand)
        {
            Target target = Owner;
            if (hand == null || target.requires == null || !hand.Holds(target.requires)) return false;
            return Vector3.Distance(target.requires.transform.position, transform.position) <= reach;
        }

        /// <summary>Select the target once what it requires is in reach in <paramref name="hand"/>.</summary>
        /// <param name="hand">The hand holding it.</param>
        /// <returns>Whether it selected the target now.</returns>
        public bool Check(Hand hand)
        {
            if (_done || !InReach(hand)) return false;
            _done = Owner.Select(hand);
            return _done;
        }

        /// <summary>
        /// <see cref="Check(Hand)"/> for the hand holding what the target requires, whichever hand that is (a player
        /// checks each frame); none holds it: nothing to check.
        /// </summary>
        /// <returns>Whether it selected the target now.</returns>
        public bool Check()
        {
            Holdable needed = Owner.requires;
            return needed != null && needed.Holder != null && Check(needed.Holder);
        }

        void OnEnable() => _done = false;

        void Update() => Check();
    }
}
