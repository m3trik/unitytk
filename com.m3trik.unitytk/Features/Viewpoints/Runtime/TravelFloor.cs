namespace M3trik.Unitytk.Viewpoints
{
    /// <summary>
    /// Where a user brought to a viewpoint stands (<see cref="TravelTo.floorRule"/>): the rule that finds the
    /// floor under the spot.
    /// </summary>
    public enum TravelFloor
    {
        /// <summary>The top of the viewpoint's own mesh (a destination disc, its pivot often below it), else its parent's, else its pivot.</summary>
        Mesh = 0,

        /// <summary>Its pivot, as it is placed.</summary>
        Pivot = 1,

        /// <summary>The first surface a ray straight down from just above the spot meets (a floor under a roof), else its pivot.</summary>
        Below = 2,

        /// <summary>A height given in the world (<see cref="TravelTo.floorHeight"/>), under the spot.</summary>
        Height = 3,
    }
}
