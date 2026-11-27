using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// Something a user selects to act on: a door, a lock, a button. A selection by a hand
    /// that holds what the target requires (or any hand, where it requires nothing) raises
    /// <see cref="onSelected"/>; one by any other raises <see cref="onRefused"/>. What a
    /// selection does is wired to those events (a clip player's clip, a holdable taken). A press
    /// held down (<see cref="Press"/>, until <see cref="Release"/>) raises <see cref="onPressed"/>
    /// and <see cref="onReleased"/> by the same rule: what plays while it is held. A press and the
    /// selection that ends its gesture (a click's button down, then the click) are refused once.
    /// </summary>
    /// <remarks>
    /// Input is not its business: a pointer on a desktop, a ray or a poke in VR, or code,
    /// calls <see cref="Select"/> (a click), or <see cref="Press"/> and <see cref="Release"/>
    /// (a button held down and let go), with the hand it acts for.
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Target")]
    [DisallowMultipleComponent]
    public sealed class Target : MonoBehaviour
    {
        [Tooltip("What the selecting hand must hold (none: it may hold anything or nothing).")]
        public Holdable requires;

        [Tooltip("Selected by a hand that may.")]
        public HandEvent onSelected = new HandEvent();

        [Tooltip("Selected by a hand that does not hold what it requires.")]
        public HandEvent onRefused = new HandEvent();

        [Tooltip("Pressed and held by a hand that may (a press refused raises On Refused).")]
        public HandEvent onPressed = new HandEvent();

        [Tooltip("Let go after a press it took, or switched off while pressed.")]
        public HandEvent onReleased = new HandEvent();

        Hand _pressedBy;
        bool _pressRefused;  // the last press was refused: the selection ending its gesture says nothing more

        /// <summary>Whether a press it took is held.</summary>
        public bool IsPressed { get; private set; }

        /// <summary>Whether <paramref name="by"/> may select it.</summary>
        /// <param name="by">The selecting hand (null: no hand).</param>
        public bool Accepts(Hand by) => requires == null || (by != null && by.Holds(requires));

        /// <summary>
        /// Select it with <paramref name="by"/>: <see cref="onSelected"/> or <see cref="onRefused"/>, which a
        /// press of the same gesture refused has raised already (<see cref="Press"/> just before); a target
        /// switched off (disabled, as a step does outside its run) takes no selection.
        /// </summary>
        /// <param name="by">The selecting hand (null: no hand).</param>
        /// <returns>Whether the selection was accepted.</returns>
        public bool Select(Hand by)
        {
            if (!isActiveAndEnabled) return false;
            bool said = _pressRefused;
            _pressRefused = false;
            if (!Accepts(by))
            {
                if (!said) onRefused.Invoke(by);
                return false;
            }
            onSelected.Invoke(by);
            return true;
        }

        /// <summary>
        /// Press it with <paramref name="by"/> and hold: <see cref="onPressed"/>, or
        /// <see cref="onRefused"/>, by the rule <see cref="Select"/> goes by; a press held already
        /// takes no second one.
        /// </summary>
        /// <param name="by">The pressing hand (null: no hand).</param>
        /// <returns>Whether the press was taken.</returns>
        public bool Press(Hand by)
        {
            if (!isActiveAndEnabled || IsPressed) return false;
            _pressRefused = !Accepts(by);
            if (_pressRefused)
            {
                onRefused.Invoke(by);
                return false;
            }
            IsPressed = true;
            _pressedBy = by;
            onPressed.Invoke(by);
            return true;
        }

        /// <summary>Let go of a press it took: <see cref="onReleased"/>, with the hand that pressed.</summary>
        /// <returns>Whether a press was held.</returns>
        public bool Release()
        {
            if (!IsPressed) return false;
            IsPressed = false;
            Hand by = _pressedBy;
            _pressedBy = null;
            onReleased.Invoke(by);
            return true;
        }

        // Off from the scene's start: its colliders too.
        void Awake()
        {
            if (!enabled) SwitchColliders(false);
        }

        // The targets switched on now: a registry its owner rebuilds (a domain reload empties it, as play does below, and
        // each target's OnEnable and OnDisable keep it), the one static of Unity objects here, for a reason: what reads it
        // (a pointer's aim assist, in an input door above the feature) asks every frame a pointer moves, and only the
        // targets see themselves switched on and off; a scene search there would allocate each frame.
        static readonly List<Target> _enabled = new List<Target>();

        /// <summary>The targets switched on now, in play: what an input that helps aim (a pointer's assist) picks among.</summary>
        public static IReadOnlyList<Target> Enabled => _enabled;

        // Emptied as play starts, so a play mode entered without a domain reload starts with none left over.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetEnabled() => _enabled.Clear();

        void OnEnable()
        {
            if (!_enabled.Contains(this)) _enabled.Add(this);
            SwitchColliders(true);
        }

        void OnDisable()
        {
            _enabled.Remove(this);
            _pressRefused = false;
            Release();
            SwitchColliders(false);
        }

        // Its own colliders (none a target inside it owns) take clicks and rays only while it is on,
        // so a part not in play never catches what was meant for one behind it (a door swung open
        // in front of the parts on it); a held one's stay off, as its holdable switched them.
        void SwitchColliders(bool on)
        {
            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider.GetComponentInParent<Target>(true) != this) continue;
                if (on && Holdable.Held(collider)) continue;
                collider.enabled = on;
            }
        }

        /// <summary>Whether <paramref name="collider"/> may take clicks: the target owning it (none: any) is on.</summary>
        /// <param name="collider">A collider in the scene.</param>
        internal static bool Live(Collider collider)
        {
            Target owner = collider.GetComponentInParent<Target>(true);
            return owner == null || owner.enabled;
        }
    }
}
