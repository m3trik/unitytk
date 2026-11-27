namespace M3trik.Unitytk.Placement
{
    /// <summary>A side of a part a person stands on (<see cref="Placer.SideDirection"/>, <see cref="Placer.Stand"/>'s <c>side</c>).</summary>
    public enum StandSide
    {
        /// <summary>Its front: the way its <c>+Z</c> faces.</summary>
        Front,
        /// <summary>Its back: against its <c>+Z</c>.</summary>
        Back,
        /// <summary>Its left: against its <c>+X</c>.</summary>
        Left,
        /// <summary>Its right: the way its <c>+X</c> faces.</summary>
        Right,
        /// <summary>The other side from where the person stood: across the part from them.</summary>
        Other,
    }
}
