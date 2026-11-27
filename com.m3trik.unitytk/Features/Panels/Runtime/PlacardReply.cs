using System;
using UnityEngine;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// What a <see cref="PlacardPage"/> says once a label is chosen or keyed in that leaves it shown:
    /// a wrong answer's "Try again", in place of its text.
    /// </summary>
    [Serializable]
    public sealed class PlacardReply
    {
        [Tooltip("The choice's label, or the value keyed in (ignoring case).")]
        public string choice = "";

        [Tooltip("What the placard says then, in place of the page's text.")]
        [TextArea]
        public string text = "";
    }
}
