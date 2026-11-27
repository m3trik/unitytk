using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels.Tests
{
    /// <summary>
    /// A project's own placard, as a subclass draws one: its heading on a label of its own layout, in capitals, and none
    /// of the package's parts.
    /// </summary>
    public sealed class OwnLayoutPlacard : Placard
    {
        /// <summary>The name of the label it draws the heading on.</summary>
        public const string HeadingName = "own-heading";

        /// <inheritdoc/>
        protected override bool Render(VisualElement root)
        {
            Label heading = root.Q<Label>(HeadingName);
            if (heading == null) root.Add(heading = new Label { name = HeadingName });
            heading.text = Heading.ToUpperInvariant();
            return true;
        }
    }
}
