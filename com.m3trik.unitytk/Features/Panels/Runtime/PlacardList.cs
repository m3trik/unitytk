using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// A list a <see cref="Placard"/> shows below its pages (a checklist): switched on, it shows
    /// its items, none ticked, on the placard named, else the scene's switched-on one, and stays
    /// as pages change; <see cref="Tick"/> ticks an item; switched off, it takes itself away
    /// again, unless another list has replaced it. It is made switched off, and whatever runs
    /// the scene switches it on (a step, from the one that shows it on).
    /// </summary>
    /// <remarks>Prior art: as <see cref="Placard"/>'s.</remarks>
    [AddComponentMenu("unitytk/Panels/Placard List")]
    public sealed class PlacardList : MonoBehaviour
    {
        [Tooltip("The placard it shows on (the scene's switched-on placard when empty).")]
        public Placard placard;

        [Tooltip("The items, in order.")]
        public List<string> items = new List<string>();

        readonly HashSet<string> _ticked = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>The placard it shows on: the one named, else the scene's switched-on one; null when there is none.</summary>
        public Placard Placard => placard != null ? placard : Placard.InScene();

        /// <summary>The items ticked since it was switched on.</summary>
        public IReadOnlyCollection<string> Ticked => _ticked;

        /// <summary>Tick <paramref name="item"/>, shown at once while the list is on its placard.</summary>
        /// <param name="item">One of its items (ignoring case).</param>
        /// <returns>Whether it is one of its items (none: a warning).</returns>
        public bool Tick(string item)
        {
            if (!items.Exists(each => string.Equals(each, item, System.StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogWarning($"{name}: no item '{item}' in the list to tick: add it, or name another", this);
                return false;
            }
            _ticked.Add(item);
            Placard on = Placard;
            if (on != null && on.ListedBy == this) on.ShowList(items, _ticked, this);
            return true;
        }

        /// <summary>Show the list on its placard, none of its items ticked (what switching it on does).</summary>
        /// <returns>Whether there is a placard to show it on (none: a warning).</returns>
        public bool Show()
        {
            _ticked.Clear();
            Placard on = Placard;
            if (on == null)
            {
                Debug.LogWarning($"{name}: no placard in the scene to show this list on; add one (PanelsActions.MakePlacard).", this);
                return false;
            }
            on.ShowList(items, _ticked, this);
            return true;
        }

        /// <summary>Take the list away from its placard, unless another has replaced it (what switching it off does).</summary>
        public void Hide()
        {
            Placard on = Placard;
            if (on != null) on.ClearList(this);
        }

        void OnEnable() => Show();

        void OnDisable() => Hide();
    }
}
