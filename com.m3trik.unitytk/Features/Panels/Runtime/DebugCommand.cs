using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// A command on the scene's <see cref="DebugPanel"/>, wired in the scene: a button labelled
    /// <see cref="label"/> that runs <see cref="onRun"/>, there while this is switched on. A build
    /// or a person adds one where a component's own method is the command (a door's Open).
    /// </summary>
    /// <remarks>Prior art: as <see cref="DebugPanel"/>'s.</remarks>
    [AddComponentMenu("unitytk/Panels/Debug Command")]
    public sealed class DebugCommand : MonoBehaviour
    {
        [Tooltip("The debug panel it is on (the scene's switched-on one when empty).")]
        public DebugPanel panel;

        [Tooltip("The button's label.")]
        public string label = "";

        [Tooltip("What pressing it does.")]
        public UnityEvent onRun = new UnityEvent();

        /// <summary>The panel it is on: the one named, else the scene's switched-on one; null when there is none.</summary>
        public DebugPanel Panel => panel != null ? panel : DebugPanel.InScene();

        void OnEnable()
        {
            DebugPanel on = Panel;
            if (on != null && !string.IsNullOrEmpty(label)) on.Add(this, label, onRun.Invoke);
        }

        void OnDisable()
        {
            DebugPanel on = Panel;
            if (on != null) on.Remove(this);
        }
    }
}
