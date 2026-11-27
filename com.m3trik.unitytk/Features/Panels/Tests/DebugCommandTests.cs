using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels.Tests
{
    /// <summary>
    /// <see cref="DebugCommand"/>: a scene's own command, its button on the debug panel while it is
    /// switched on, pressing it running its event, gone when it is switched off.
    /// </summary>
    public class DebugCommandTests
    {
        GameObject _node;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_node);

        // What Unity calls as a component is switched on or off (Edit Mode calls neither, nor takes a message for it).
        static void Call(DebugCommand command, string method)
            => typeof(DebugCommand).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(command, null);

        [Test]
        public void A_command_is_on_the_panel_while_it_is_on_and_runs_its_event()
        {
            _node = new GameObject("Debug");
            _node.AddComponent<UIDocument>();
            var panel = _node.AddComponent<DebugPanel>();
            var command = new GameObject("Lights").AddComponent<DebugCommand>();
            command.transform.SetParent(_node.transform, false);
            command.panel = panel;
            command.label = "Lights";
            int ran = 0;
            command.onRun.AddListener(() => ran++);
            Call(command, "OnEnable");  // Edit Mode never calls it
            Assert.That(panel.Commands, Is.EqualTo(new[] { "Lights" }));
            Assert.That(panel.Run("Lights"), Is.True);
            Assert.That(ran, Is.EqualTo(1));
            Call(command, "OnDisable");
            Assert.That(panel.Commands, Is.Empty, "switched off: its button goes");
        }
    }
}
