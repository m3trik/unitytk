using System;
using System.Collections.Generic;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>The stand-in that holds no scenes (pythontk's <c>PureSceneFiles</c>).</summary>
    public class PureSceneFilesTests
    {
        [Test]
        public void It_holds_nothing_and_refuses_a_scene()
        {
            var files = new PureSceneFiles();
            files.Open(new List<string>());
            files.Changed();
            Assert.That(files.Modified(), Is.Empty);
            Assert.That(files.Unsaved(), Is.False);
            var error = Assert.Throws<ArgumentException>(() => files.Open(new List<string> { "a.unity" }));
            Assert.That(error.Message, Is.EqualTo("this host opens no scenes (the request names 1)"));
            Assert.Throws<ArgumentException>(() => files.Save(new List<string> { "a.unity" }));
        }
    }
}
