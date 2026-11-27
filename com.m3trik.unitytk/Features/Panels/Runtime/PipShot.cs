using UnityEngine;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// One picture of a <see cref="PipView"/> (a step's: a part close up, captioned):
    /// switched on, it shows its part with its caption on the view named, else the scene's
    /// switched-on one; switched off, it takes it away again, unless another shot has replaced it.
    /// It is made switched off, and whatever runs the scene switches it on (a step).
    /// </summary>
    /// <remarks>Prior art: as <see cref="PipView"/>'s.</remarks>
    [AddComponentMenu("unitytk/Panels/PiP Shot")]
    public sealed class PipShot : MonoBehaviour
    {
        [Tooltip("The view it shows on (the scene's switched-on PiP when empty).")]
        public PipView view;

        [Tooltip("The part it shows.")]
        public Transform target;

        [Tooltip("An authored spot to look from (the eyes' side, cutting away what stands between, when empty).")]
        public Transform from;

        [Tooltip("What the picture shows, in words.")]
        public string caption = "";

        /// <summary>The view it shows on: the one named, else the scene's switched-on one; null when there is none.</summary>
        public PipView View => view != null ? view : PipView.InScene();

        /// <summary>Show its part on its view.</summary>
        /// <returns>Whether there is a view and a part to show (none: a warning).</returns>
        public bool Show()
        {
            PipView on = View;
            if (on == null || target == null)
            {
                Debug.LogWarning($"{name}: {(on == null ? "no PiP view in the scene to show on; add one (PanelsActions.MakePip)" : "no part to show; name it")}.", this);
                return false;
            }
            on.Show(target, caption, this, from);
            return true;
        }

        void OnEnable() => Show();

        void OnDisable()
        {
            PipView on = View;
            if (on != null) on.Clear(this);
        }
    }
}
