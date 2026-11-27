using UnityEngine;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// Ticks one item of a <see cref="PlacardList"/> as it is switched on (a step switches it on
    /// as it ends: the item done). It is made switched off.
    /// </summary>
    /// <remarks>Prior art: as <see cref="Placard"/>'s.</remarks>
    [AddComponentMenu("unitytk/Panels/Placard Tick")]
    public sealed class PlacardTick : MonoBehaviour
    {
        [Tooltip("The list (the one above it when empty).")]
        public PlacardList list;

        [Tooltip("The item it ticks.")]
        public string item = "";

        /// <summary>The list it ticks: the one named, else the one above it; null when there is none.</summary>
        public PlacardList List => list != null ? list : GetComponentInParent<PlacardList>(true);

        /// <summary>Tick the item on its list (what switching it on does).</summary>
        /// <returns>Whether the list has the item (no list, or no such item: a warning).</returns>
        public bool Tick()
        {
            PlacardList on = List;
            if (on != null) return on.Tick(item);
            Debug.LogWarning($"{name}: no list to tick '{item}' on: name one", this);
            return false;
        }

        void OnEnable() => Tick();
    }
}
