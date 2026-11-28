using UnityEngine;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>A test-only kit's settings (<see cref="TestKit.Settings"/>): one value a manifest keeps of its own.</summary>
    public sealed class TestSettings : ScriptableObject
    {
        /// <summary>A value a manifest keeps.</summary>
        public float size = 1f;

        /// <summary>The size of the box <c>test_part</c> builds (a setting a part it built holds: a person's change kept).</summary>
        public Vector3 box = Vector3.one;

        /// <summary><c>test_look</c>'s own settings, grouped under its name: a row's <c>size</c> on a <c>test_look</c> row is <c>test_look.size</c>.</summary>
        public TestLookSettings test_look = new TestLookSettings();
    }
}
