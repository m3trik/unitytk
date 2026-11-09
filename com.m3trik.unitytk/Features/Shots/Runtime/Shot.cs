using System;
using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Shots
{
    /// <summary>One shot: the clip that plays it, what it shows, and its own metadata.</summary>
    [Serializable]
    public sealed class Shot
    {
        [Tooltip("The clip that plays the shot.")]
        public AnimationClip clip;

        [Tooltip("What the shot shows, as free text.")]
        [TextArea]
        public string description = "";

        [Tooltip("The key/value data the shot's author wrote for it (the source's shot manifest), each value JSON text.")]
        public List<ShotField> metadata = new List<ShotField>();

        /// <summary>
        /// The JSON text of the metadata value under <paramref name="key"/> (the first
        /// such key, matched exactly).
        /// </summary>
        /// <param name="key">The metadata key.</param>
        /// <param name="json">The value as JSON text, or null when the shot has no such key.</param>
        /// <returns>Whether the shot has the key.</returns>
        public bool TryGetMetadata(string key, out string json)
        {
            if (metadata != null)
                foreach (ShotField field in metadata)
                    if (field != null && string.Equals(field.key, key, StringComparison.Ordinal))
                    {
                        json = field.json;
                        return true;
                    }
            json = null;
            return false;
        }
    }
}
