using System;
using UnityEngine;

namespace M3trik.Unitytk.Shots
{
    /// <summary>
    /// One key of a shot's metadata: the key, and its value as JSON text, so any JSON
    /// value (a number, a string, a boolean, an array, an object) keeps its type.
    /// </summary>
    [Serializable]
    public sealed class ShotField
    {
        [Tooltip("The metadata key, as the source names it (keys are case-sensitive, as JSON's are).")]
        public string key = "";

        [Tooltip("The value as JSON text: 12, \"take two\", true, [1, 2] or {\"a\": 1}.")]
        public string json = "";
    }
}
