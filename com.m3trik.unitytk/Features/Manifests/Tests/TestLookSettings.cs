using System;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// The settings of the test-only behavior <c>test_look</c>, grouped under the member of <see cref="TestSettings"/> named
    /// as it (<see cref="TestSettings.test_look"/>): a row's setting resolves under it, by its own name.
    /// </summary>
    [Serializable]
    public sealed class TestLookSettings
    {
        /// <summary>How large it looks.</summary>
        public float size = 1f;

        /// <summary>Whether it looks solid.</summary>
        public bool solid;
    }
}
