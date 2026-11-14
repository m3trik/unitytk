using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>The generated type of pythontk's outcome: written as pythontk writes it.</summary>
    public class OutcomeTests
    {
        [Test]
        public void An_outcome_writes_the_fields_set_and_no_others()
        {
            var outcome = new Outcome
            {
                op = "unity.project.doctor", status = "done", dry_run = false, result = null,
                changes = new List<Change> { new Change { kind = "write", target = "a.unity", action = "save the scene" } },
                issues = new List<Issue> { new Issue { code = "c", message = "m", severity = "warning",
                                                       fix = new OpCall { op = "unity_project.install", args = new Dictionary<string, object> { ["project"] = "P" } } } },
            };
            string text = Json.Write(outcome.ToJson());
            Assert.That(text, Does.Not.Contain("\"error\"").And.Not.Contain("\"result\""));
            Outcome back = Outcome.Read(Json.Parse(text));
            Assert.That(back.changes[0].target, Is.EqualTo("a.unity"));
            Assert.That(back.issues[0].fix.op, Is.EqualTo("unity_project.install"));
            Assert.That(Json.Write(back.ToJson()), Is.EqualTo(text));
        }
    }
}
