using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// How a row's take plays (<see cref="UseSettings"/>, <see cref="PlaySettings"/>): its speed and the stretch of it played,
    /// the row's as a whole (the clip player keeps one entry a take, whatever objects the row names).
    /// </summary>
    [Serializable]
    public class ClipSettings
    {
        [Tooltip("How fast the take plays: 1 as it was animated, 2 twice as fast (the sheet's speed). Set on its clip's entry " +
                 "of the Clip Player, it is kept as the row's.")]
        [Min(0.01f)]
        public float speed = 1f;

        [Tooltip("Seconds into the take it plays from (below 0: found from the take's motion). Set on its clip's entry of the " +
                 "Clip Player, it is kept as the row's, and the entry's Auto goes off.")]
        public float start = -1f;

        [Tooltip("Seconds into the take it plays to, and holds (below 0: found from the take's motion). Set on its clip's entry " +
                 "of the Clip Player, it is kept as the row's, and the entry's Auto goes off.")]
        public float stop = -1f;
    }
}
