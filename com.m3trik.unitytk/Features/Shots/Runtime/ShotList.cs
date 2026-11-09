// The Shots feature's runtime half (M3trik.Unitytk.Shots): the component a model's
// shots are read from in a player. Its importer and actions are the feature's editor
// half (Editor/ShotsImporter.cs, Editor/ShotsActions.cs).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Shots
{
    /// <summary>
    /// A model's shots: per clip, what it shows and its own metadata. A shot is a
    /// clip -- the source writes one take per shot, and Unity's model importer makes
    /// one clip of each -- so this holds no frames, windows or player: an Animator,
    /// an Animation or a Timeline plays the clips, and a reader asks
    /// <see cref="Entry"/> what the playing clip is. The importer writes the source's
    /// entries onto the model at every import (Editor/ShotsImporter.cs); a project
    /// adds one by picking a clip (<c>ShotsActions.AddEntry</c>, or the inspector).
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's Timeline was weighed for the shots and not adopted here: it
    /// sequences clips, and the shots are already the clips, so a Timeline asset per
    /// model would restate what the record states (sequencing them is a door of its
    /// own). An AnimationClip's own data was weighed for the description and the
    /// metadata: it carries events and curves, nothing free-form, and an imported
    /// clip is read-only. The metadata's form: Unity serializes neither a
    /// Dictionary nor an arbitrary JSON value, and <c>[SerializeReference]</c> is
    /// barred from saved data; a typed union per value (a number, a string, a bool)
    /// loses arrays and objects, and one JSON text for the whole object hides its
    /// keys from the inspector and from a lookup. So each key is a
    /// <see cref="ShotField"/>, its value JSON text: every value keeps its type, the
    /// inspector lists the keys, and <see cref="Shot.TryGetMetadata"/> finds one.
    /// </remarks>
    [AddComponentMenu("unitytk/Shots")]
    [DisallowMultipleComponent]
    public sealed class ShotList : MonoBehaviour
    {
        [Tooltip("The shots, one entry per clip; an imported model's are the source's, rewritten at every import.")]
        public List<Shot> entries = new List<Shot>();

        /// <summary>The entry of <paramref name="clip"/> (the first, should two name it), or null when none does.</summary>
        /// <param name="clip">The clip, as an Animator or Animation plays it.</param>
        public Shot Entry(AnimationClip clip)
        {
            if (clip == null || entries == null) return null;
            foreach (Shot shot in entries)
                if (shot != null && shot.clip == clip)
                    return shot;
            return null;
        }
    }
}
