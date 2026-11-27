using System;
using System.Collections.Generic;
using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Panels.Tests
{
    /// <summary>
    /// The command surface's contract (<see cref="ICommandSurface"/>), one fixture run against its stand-in
    /// (<see cref="PureCommandSurface"/>) and each provider (<see cref="DebugPanel"/>): commands put on in order, one per
    /// owner and label, run by label, taken away with their owner; one with no label or nothing to run refused, the
    /// surface left as it was; a status, none for null.
    /// </summary>
    public class CommandSurfaceContractTests
    {
        [Test]
        public void Its_stand_in_keeps_the_contract()
        {
            var surface = new PureCommandSurface();
            Keeps(surface, () => surface.Commands, () => surface.Status, surface.Run);
        }

        [Test]
        public void A_debug_panel_keeps_the_contract()
        {
            var node = new GameObject("Debug (contract test)");
            try
            {
                node.AddComponent<UIDocument>();
                var panel = node.AddComponent<DebugPanel>();
                Keeps(panel, () => panel.Commands, () => panel.Status, panel.Run);
            }
            finally
            {
                Object.DestroyImmediate(node);
            }
        }

        // The contract, read back through what the surface shows: its commands' labels, its status, a command run.
        static void Keeps(ICommandSurface surface, Func<IReadOnlyList<string>> commands, Func<string> status, Func<string, bool> run)
        {
            var first = new GameObject("first owner");
            var second = new GameObject("second owner");
            var ran = new List<string>();
            try
            {
                surface.Add(first, "Skip", () => ran.Add("skip"));
                surface.Add(second, "Lights", () => ran.Add("lights"));
                surface.Add(first, "Skip", () => ran.Add("skip again"));
                Assert.That(commands(), Is.EqualTo(new[] { "Skip", "Lights" }), "in the order put on; one owner's label replaced");
                Assert.That(run("Skip"), Is.True);
                Assert.That(ran, Is.EqualTo(new[] { "skip again" }));
                Assert.Throws<ArgumentException>(() => surface.Add(first, "", () => ran.Add("unlabelled")), "no label: refused");
                Assert.Throws<ArgumentException>(() => surface.Add(first, null, () => ran.Add("unlabelled")), "no label: refused");
                Assert.Throws<ArgumentNullException>(() => surface.Add(first, "Restart", null), "nothing to run: refused");
                Assert.That(commands(), Is.EqualTo(new[] { "Skip", "Lights" }), "a refused command is not put on");
                surface.Remove(first);
                Assert.That(commands(), Is.EqualTo(new[] { "Lights" }), "the owner's commands go with it");
                surface.SetStatus("Step A02");
                Assert.That(status(), Is.EqualTo("Step A02"));
                surface.SetStatus(null);
                Assert.That(status(), Is.EqualTo(""), "null: none");
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
