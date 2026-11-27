using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// One page of what a <see cref="Placard"/> shows (a step's: its heading, its text, its
    /// choices, a key pad, its replies to a choice): switched on, it shows itself on the placard named, else the scene's switched-on
    /// one; switched off, it takes itself away again, unless another page has replaced it. It
    /// is made switched off, and whatever runs the scene switches it on (a step, while it runs).
    /// </summary>
    /// <remarks>Prior art: as <see cref="Placard"/>'s.</remarks>
    [AddComponentMenu("unitytk/Panels/Placard Page")]
    public sealed class PlacardPage : MonoBehaviour
    {
        [Tooltip("The placard it shows on (the scene's switched-on placard when empty).")]
        public Placard placard;

        [Tooltip("The heading (a step's title).")]
        public string heading = "";

        [Tooltip("What the user is told.")]
        [TextArea]
        public string text = "";

        [Tooltip("The choices offered, a button each.")]
        public List<string> choices = new List<string>();

        [Tooltip("Of the choices, those shown only once Reveal is called (its step complete): a Next that waits for the step's actions.")]
        public List<string> waiting = new List<string>();

        bool _revealed;

        /// <summary>The choices shown now: every one once revealed (<see cref="Reveal"/>), else all but those <see cref="waiting"/>.</summary>
        public List<string> Offered
        {
            get
            {
                var offered = new List<string>();
                foreach (string choice in choices)
                    if (_revealed || !waiting.Contains(choice)) offered.Add(choice);
                return offered;
            }
        }

        /// <summary>Show the choices that wait too (its step is complete), at once where the page is up.</summary>
        public void Reveal()
        {
            _revealed = true;
            if (isActiveAndEnabled) Show();
        }

        [Tooltip("The keys of a key pad, one a character (0123456789); empty: none.")]
        public string keys = "";

        [Tooltip("What to say once a label is chosen or keyed in that leaves the page shown (a wrong answer's \"Try again\").")]
        public List<PlacardReply> replies = new List<PlacardReply>();

        [Tooltip("What the page asks the user to act on, which the placard keeps from covering.")]
        public List<Transform> keepClear = new List<Transform>();

        [Tooltip("Come up at spot instead of the placard's own offset (a build placed it clear of the scene from where its step stands the user).")]
        public bool ownSpot;

        [Tooltip("Where the placard comes up for this page, from the eyes, in metres: right, up and ahead, turned with their heading.")]
        public Vector3 spot = new Vector3(-0.4f, -0.2f, 0.8f);

        /// <summary>The placard it shows on: the one named, else the scene's switched-on one; null when there is none.</summary>
        public Placard Placard => placard != null ? placard : Placard.InScene();

        /// <summary>Show the page on its placard.</summary>
        /// <returns>Whether there is a placard to show it on (none: a warning).</returns>
        public bool Show()
        {
            Placard on = Placard;
            if (on == null)
            {
                Debug.LogWarning($"{name}: no placard in the scene to show this page on; add one (PanelsActions.MakePlacard).", this);
                return false;
            }
            var said = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (PlacardReply reply in replies)
                if (reply != null && !string.IsNullOrEmpty(reply.choice)) said[reply.choice] = reply.text;
            on.Show(heading, text, Offered, this, keys, said, keepClear, ownSpot ? spot : (Vector3?)null);
            return true;
        }

        void OnEnable()
        {
            _revealed = false;
            Show();
        }

        void OnDisable()
        {
            Placard on = Placard;
            if (on != null) on.Clear(this);
        }
    }
}
