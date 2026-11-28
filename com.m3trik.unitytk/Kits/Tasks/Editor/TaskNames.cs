using System;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// The names of the objects a task's build makes in the scene's top level (<see cref="TaskSettings.names"/>),
    /// each followed by its task's name, its settings file's (<c>Task (task)</c>; the sheet's where it was staged with
    /// none), so a sheet renamed or replaced is the same task, and the name of a
    /// viewpoint the build places for a step (<see cref="viewpoint"/>, then the step's ID).
    /// </summary>
    [Serializable]
    public sealed class TaskNames
    {
        [Tooltip("The sequence the task's steps run in.")]
        public string sequence = "Task";

        [Tooltip("The task's placard: a page per step.")]
        public string placard = "Placard";

        [Tooltip("The task's picture: the close-up a picture row shows.")]
        public string picture = "Picture";

        [Tooltip("What holds the steps' sounds.")]
        public string voice = "Voice";

        [Tooltip("What holds the viewpoints the build places.")]
        public string viewpoints = "Viewpoints";

        [Tooltip("The task's debug panel.")]
        public string debug = "Debug";

        [Tooltip("What a viewpoint the build places for a step is named before the step's ID (VIEW_A01).")]
        public string viewpoint = "VIEW_";
    }
}
