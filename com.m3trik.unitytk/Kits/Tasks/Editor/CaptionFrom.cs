namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>Where a picture's caption comes from (<see cref="TaskSettings.captionFrom"/>).</summary>
    public enum CaptionFrom
    {
        /// <summary>Its row's Label, else what the row's own words say it shows (<see cref="TaskSettings.captionPattern"/>).</summary>
        Words = 0,
        /// <summary>Its row's Label alone: with none, no caption (a row written in cells, whose words only name its object).</summary>
        Label = 1,
    }
}
