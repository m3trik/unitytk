using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>The generated type of pythontk's request: read as pythontk validates it.</summary>
    public class OpRequestTests
    {
        [Test]
        public void A_request_breaking_the_payloads_rules_is_refused()
        {
            Assert.Throws<FormatException>(() => OpRequest.Read(Json.Parse("{\"format\": 1}")), "no op");
            Assert.Throws<FormatException>(() => OpRequest.Read(Json.Parse("{\"format\": 1, \"op\": \"a.b\", \"args\": null}")));
            Assert.Throws<FormatException>(() => OpRequest.Read(Json.Parse("{\"format\": 1, \"op\": 5}")));
            OpRequest request = OpRequest.Read(Json.Parse(
                "{\"format\": 1, \"op\": \"unity.scene.view\", \"args\": {\"width\": 64}, \"dry_run\": true, \"scenes\": [\"a.unity\"]}"));
            Assert.That(request.op, Is.EqualTo("unity.scene.view"));
            Assert.That(request.args["width"], Is.EqualTo(64L), "an integer reads as one");
            Assert.That(request.dry_run, Is.True);
            Assert.That(request.scenes, Is.EqualTo(new[] { "a.unity" }));
            Assert.That(OpRequest.Read("not an object"), Is.Null);
        }
    }
}
