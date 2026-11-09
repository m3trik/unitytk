using System;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// What an effect asks of a material while it is active: the state a twin
    /// (<see cref="MaterialWriter.Twin"/>) is made in. A material at rest is its
    /// own (<see cref="None"/>); each flag is one change the pipeline's own Lit
    /// makes through its own switches and keywords.
    /// </summary>
    [Flags]
    public enum TwinState
    {
        /// <summary>At rest: the material as authored, shared.</summary>
        None = 0,
        /// <summary>Its opacity below 1: blended over the background (the pipeline's blend switch).</summary>
        Blended = 1,
        /// <summary>An added glow: emission on and a mapped emission set aside, so the glow shows.</summary>
        Glowing = 2,
        /// <summary>An animated emission: emission on, its map kept.</summary>
        Emitting = 4,
    }
}
