using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// What a user holds things with: a controller in VR, a point in the camera's view on a
    /// desktop. It holds one <see cref="Holdable"/> at a time, at its anchor; taking another
    /// puts the first back where it rests.
    /// </summary>
    [AddComponentMenu("unitytk/Interaction/Hand")]
    [DisallowMultipleComponent]
    public sealed class Hand : MonoBehaviour
    {
        [Tooltip("Where a held thing sits (this transform when empty).")]
        public Transform anchor;

        /// <summary>Where a held thing sits.</summary>
        public Transform Anchor => anchor != null ? anchor : transform;

        /// <summary>What the hand holds; null when empty.</summary>
        public Holdable Held { get; private set; }

        /// <summary>Whether the hand holds <paramref name="holdable"/>.</summary>
        /// <param name="holdable">A holdable; null holds never.</param>
        public bool Holds(Holdable holdable) => holdable != null && Held == holdable;

        /// <summary>The scene's switched-on hand (the first found): who an input with no hand of its own selects for; null when none.</summary>
        public static Hand InScene() => SceneObjects.FirstEnabled<Hand>();

        /// <summary>
        /// Hold <paramref name="holdable"/>: what the hand held goes back where it rests, and
        /// another hand holding it lets go.
        /// </summary>
        /// <param name="holdable">What to hold; null does nothing.</param>
        public void Take(Holdable holdable)
        {
            if (holdable == null || Held == holdable) return;
            if (Held != null) Held.ReturnHome();
            if (holdable.Holder != null) holdable.Holder.Released(holdable);
            Held = holdable;
            holdable.HeldBy(this);
        }

        /// <summary>Put what the hand holds back where it rests.</summary>
        public void Release()
        {
            if (Held != null) Held.ReturnHome();
        }

        // The holdable left this hand (returned home, or taken by another).
        internal void Released(Holdable holdable)
        {
            if (Held == holdable) Held = null;
        }
    }
}
