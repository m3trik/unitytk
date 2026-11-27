using UnityEngine;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>A test-only kit's settings (<see cref="ToolTestKit.Settings"/>): the ways its behaviors are read.</summary>
    public sealed class ToolTestSettings : ScriptableObject
    {
        /// <summary>How a <c>test_marker</c> row shows its marker.</summary>
        public enum Show
        {
            /// <summary>It appears.</summary>
            Appear,
            /// <summary>It goes.</summary>
            Hide,
        }

        /// <summary>How a <c>test_press</c> row is pressed.</summary>
        public enum Press
        {
            /// <summary>A click.</summary>
            Click,
            /// <summary>Held while it plays.</summary>
            HoldToPlay,
            /// <summary>Held for a while.</summary>
            TimedPress,
        }

        /// <summary>A marker's way (<c>test_marker</c>'s own setting, said on its chip).</summary>
        public Show show = Show.Appear;

        /// <summary>A press's way (<c>test_press</c>'s own setting, said on its chip).</summary>
        public Press press = Press.Click;
    }
}
