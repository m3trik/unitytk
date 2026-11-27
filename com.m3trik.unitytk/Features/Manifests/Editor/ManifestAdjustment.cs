using UnityEngine;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>One field of a part a build made that a person changed in the scene since (<see cref="ManifestsActions.Adjustments"/>).</summary>
    public sealed class ManifestAdjustment
    {
        /// <summary>The part, as the open scene holds it; null where it is not there.</summary>
        public Component part;

        /// <summary>Where the part is in the scene: its object's path from its root, then its type (<c>Placard (task)/A02#PlacardPage</c>).</summary>
        public string where = "";

        /// <summary>The field's name.</summary>
        public string field = "";

        /// <summary>The value the person gave it, as text.</summary>
        public string value = "";

        /// <summary>The value the build gave it, as text: what a revert puts back.</summary>
        public string built = "";

        /// <summary>Whether the manifest keeps it already; false: made since the last build, kept by the next.</summary>
        public bool kept;

        /// <summary>
        /// An adjustment a kit keeps itself (<see cref="IManifestKit.Kept"/>: a spot moved in the scene), named as the
        /// manifest names its own.
        /// </summary>
        /// <param name="part">The part, in the open scene.</param>
        /// <param name="field">What of it was adjusted, in a word (<c>place</c>).</param>
        /// <param name="value">What the person gave it, as text.</param>
        /// <param name="built">What the build gave it, as text.</param>
        /// <returns>The adjustment.</returns>
        public static ManifestAdjustment Of(Component part, string field, string value, string built)
            => new ManifestAdjustment { part = part, where = ManifestAdjustments.Where(part) ?? "", field = field, value = value, built = built };

        /// <summary>The adjustment in a few words: <c>PlacardPage.spot = 0.5,0.1,1.2 (built: -0.4,-0.2,0.8)</c>.</summary>
        public string Text() => $"{Type()}.{field} = {value} (built: {built})";

        // The part's type, by its place's name for it.
        string Type()
        {
            int at = where.LastIndexOf('#');
            string type = at < 0 ? "" : where.Substring(at + 1);
            int index = type.IndexOf('[');
            return index < 0 ? type : type.Substring(0, index);
        }
    }
}
