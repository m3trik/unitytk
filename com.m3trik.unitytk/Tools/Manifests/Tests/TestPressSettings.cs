using System;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>The settings of <c>test_press</c>, grouped under its name (<see cref="ToolTestSettings.test_press"/>).</summary>
    [Serializable]
    public sealed class TestPressSettings
    {
        /// <summary>A press's way (said on its chip).</summary>
        public ToolTestSettings.Press way = ToolTestSettings.Press.Click;
    }
}
