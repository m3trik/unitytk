using UnityEngine;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>A test-only kit's settings (<see cref="ToolTestKit.Settings"/>): the ways its behaviors are read.</summary>
    public sealed class ToolTestSettings : ScriptableObject
    {
        /// <summary>Which way a <c>test_sign</c> row's sign faces.</summary>
        public enum Face
        {
            /// <summary>To the front.</summary>
            Front,
            /// <summary>To the back.</summary>
            Back,
        }

        /// <summary>How a <c>test_press</c> row is pressed.</summary>
        public enum Press
        {
            /// <summary>A click.</summary>
            Click,
            /// <summary>Held down while it plays.</summary>
            HeldDown,
            /// <summary>Held for a while.</summary>
            TimedHold,
        }

        /// <summary>A sign's way (<c>test_sign</c>'s own setting, said on its chip): a flat setting, by its own name.</summary>
        public Face face = Face.Front;

        /// <summary><c>test_press</c>'s own settings, grouped under its name.</summary>
        public TestPressSettings test_press = new TestPressSettings();
    }
}
