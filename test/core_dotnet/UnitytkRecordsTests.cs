using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>
    /// The one record reader: a record of the current version read through its
    /// shape, anything else refused whole with a reason saying what to do.
    /// </summary>
    public class UnitytkRecordsTests
    {
        sealed class Probe
        {
            public object Record;
        }

        static Probe Keep(object record) => new Probe { Record = record };

        [Test]
        public void Empty_text_is_no_record_and_no_refusal()
        {
            Assert.That(UnitytkRecords.Read("shadows", "", 1, Keep, "a.fbx", out string refusal), Is.Null);
            Assert.That(refusal, Is.Null);
            Assert.That(UnitytkRecords.Read("shadows", null, 1, Keep, "a.fbx", out refusal), Is.Null);
            Assert.That(refusal, Is.Null);
        }

        [Test]
        public void A_record_of_the_current_version_is_read_through_its_shape()
        {
            Probe read = UnitytkRecords.Read("shadows", "{\"version\": 2, \"planes\": []}", 2, Keep, "a.fbx", out string refusal);
            Assert.That(refusal, Is.Null);
            var fields = (IDictionary<string, object>)read.Record;
            Assert.That(fields["version"], Is.EqualTo(2.0));
            Assert.That(fields.ContainsKey("planes"), Is.True);
        }

        [Test]
        public void Text_that_is_not_json_is_refused_and_never_read()
        {
            bool called = false;
            Probe read = UnitytkRecords.Read("shadows", "{not json", 1, r => { called = true; return Keep(r); }, "a.fbx", out string refusal);
            Assert.That(read, Is.Null);
            Assert.That(called, Is.False);
            Assert.That(refusal, Does.StartWith("'a.fbx' carries a shadows record that is not JSON ("));
            Assert.That(refusal, Does.EndWith("re-export the file from a current DCC exporter."));
        }

        [Test]
        public void Another_version_is_refused_with_the_refusal_for_it()
        {
            bool called = false;
            Probe read = UnitytkRecords.Read("shadows", "{\"version\": 1}", 2, r => { called = true; return Keep(r); }, "a.fbx", out string refusal);
            Assert.That(read, Is.Null);
            Assert.That(called, Is.False);
            Assert.That(refusal, Is.EqualTo(UnitytkRecords.Refusal("shadows", 1, 2, "a.fbx")));
            UnitytkRecords.Read("shadows", "{}", 2, Keep, "a.fbx", out refusal);
            Assert.That(refusal, Is.EqualTo(UnitytkRecords.Refusal("shadows", 0, 2, "a.fbx")), "no version stated reads as 0");
        }

        [Test]
        public void A_refusal_says_re_export_for_an_older_file_and_update_for_a_newer_one()
        {
            Assert.That(
                UnitytkRecords.Refusal("audio", 1, 2, "b.fbx"),
                Is.EqualTo("'b.fbx' carries the audio record v1; unitytk reads v2 only, so it is not read -- " +
                           "re-export the file from a current DCC exporter."));
            Assert.That(
                UnitytkRecords.Refusal("audio", 3, 2, "b.fbx"),
                Is.EqualTo("'b.fbx' carries the audio record v3; unitytk reads v2 only, so it is not read -- " +
                           "update the com.m3trik.unitytk version in the project's Packages/manifest.json to read it."));
        }

        [TestCase(typeof(FormatException))]
        [TestCase(typeof(InvalidCastException))]
        [TestCase(typeof(OverflowException))]
        public void A_record_that_fails_its_shape_is_refused_whole(Type error)
        {
            Probe read = UnitytkRecords.Read<Probe>(
                "lightmaps", "{\"version\": 1}", 1, r => throw (Exception)Activator.CreateInstance(error, "bad field"),
                "c.fbx", out string refusal);
            Assert.That(read, Is.Null);
            Assert.That(refusal, Is.EqualTo(
                "'c.fbx' carries a lightmaps record v1 that fails its declared shape (bad field), " +
                "so it is not read -- re-export the file from a current DCC exporter."));
        }

        [Test]
        public void A_reader_bug_is_not_taken_for_a_bad_record()
        {
            Assert.Throws<NullReferenceException>(() => UnitytkRecords.Read<Probe>(
                "lightmaps", "{\"version\": 1}", 1, r => throw new NullReferenceException(), "c.fbx", out _));
        }

        [TestCase("{\"version\": 3}", 3)]
        [TestCase("{\"version\": -2}", -2)]
        [TestCase("{\"version\": 2.5}", -1)]
        [TestCase("{\"version\": \"2\"}", -1)]
        [TestCase("{\"version\": 1e12}", -1)]
        [TestCase("{\"version\": null}", 0)]
        [TestCase("{\"other\": 1}", 0)]
        [TestCase("[1, 2]", 0)]
        [TestCase("4", 0)]
        public void VersionOf_reads_a_whole_number_under_version(string json, int expected)
        {
            Assert.That(UnitytkRecords.VersionOf(M3trik.Unitytk.Core.Json.Parse(json, false)), Is.EqualTo(expected));
        }

        [Test]
        public void VersionOf_null_is_no_version()
        {
            Assert.That(UnitytkRecords.VersionOf(null), Is.EqualTo(0));
        }

        [Test]
        public void TryGetProperty_matches_the_key_ignoring_case()
        {
            string[] names = { "Other", "SHADOWS" };
            object[] values = { "{}", "{\"version\": 1}" };
            Assert.That(UnitytkRecords.TryGetProperty(names, values, "shadows", out string json), Is.True);
            Assert.That(json, Is.EqualTo("{\"version\": 1}"));
        }

        [Test]
        public void TryGetProperty_is_false_for_a_missing_empty_or_non_text_value()
        {
            Assert.That(UnitytkRecords.TryGetProperty(new[] { "audio" }, new object[] { "x" }, "shadows", out string json), Is.False);
            Assert.That(json, Is.Null);
            Assert.That(UnitytkRecords.TryGetProperty(new[] { "shadows" }, new object[] { "" }, "shadows", out json), Is.False);
            Assert.That(UnitytkRecords.TryGetProperty(new[] { "shadows" }, new object[] { 3.0 }, "shadows", out json), Is.False);
            Assert.That(json, Is.Null);
            Assert.That(UnitytkRecords.TryGetProperty(new string[0], new object[0], "shadows", out json), Is.False);
        }

        [Test]
        public void TryGetProperty_reads_the_first_property_of_the_name()
        {
            string[] names = { "shadows", "Shadows" };
            object[] values = { "", "{\"version\": 1}" };
            Assert.That(UnitytkRecords.TryGetProperty(names, values, "shadows", out string json), Is.False,
                "the first property of the name decides, as every record source reads a node");
            Assert.That(json, Is.EqualTo(""));
        }
    }
}
