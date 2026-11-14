using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>The generated type of pythontk's catalog entry: its effects are pythontk's.</summary>
    public class OperationEntryTests
    {
        [Test]
        public void The_effects_are_pythontks()
        {
            foreach (string effect in new[] { "read", "state", "files", "code" })
                Assert.That(OperationEntry.Read(Entry(effect)).effect, Is.EqualTo(effect));
            Assert.Throws<FormatException>(() => OperationEntry.Read(Entry("bogus")));
        }

        static object Entry(string effect) => Json.Parse(
            "{\"id\": \"unity.a.b\", \"title\": \"T\", \"effect\": \"" + effect + "\", " +
            "\"args\": {\"type\": \"object\", \"properties\": {}}}");
    }
}
