using M3trik.Unitytk.Panels;
using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// How a <c>picture</c> row's picture comes up and how long it shows (<see cref="TaskSettings.picture"/>): the sheet's
    /// <c>until</c>, the angle of the camera the kit makes for it, and the task's one screen (its style and anchor, the
    /// task's alone, one for every row).
    /// </summary>
    [Serializable]
    public sealed class PictureSettings
    {
        [Tooltip("Where the task's picture comes up: beside the placard, at the delivery's screen (Anchored), or in a corner of " +
                 "the view. The task's one screen: one for every row.")]
        public PipStyle style = PipStyle.Beside;

        [Tooltip("Which node is the picture's screen, by name (a regular expression, ^UI_PIP for a UI_PIP placeholder), for the " +
                 "Anchored style. Empty: none. The task's one screen: one for every row.")]
        public string anchor = "";

        [Tooltip("The field of view of the camera the kit makes for a picture, in degrees, 1 to 179 (0: the picture view's). Set " +
                 "on that camera, it is kept as the row's; a camera the delivery brings keeps its own.")]
        [Range(0f, 179f)]
        public float fieldOfView;

        [Tooltip("How long the picture shows (a span): step, while its step runs; a step's ID, until that one starts; empty: " +
                 "until the next picture row's step, or to the end.")]
        public string until = "";
    }
}
