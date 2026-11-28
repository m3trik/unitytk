using NUnit.Framework;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// <see cref="SheetPlan"/>: a plan file read through its declared shape, of the one
    /// version the kit reads, or refused whole with what to do.
    /// </summary>
    public class SheetPlanTests
    {
        [Test]
        public void Read_reads_a_plan_of_its_version()
        {
            var plan = SheetPlan.Read(
                "{\"version\": 3, \"source\": {\"sheet\": \"s.csv\", \"delivery\": \"d.fbx\"}, \"steps\": [" +
                "{\"id\": \"A01\", \"name\": \"Open\", \"fields\": {\"voice\": \"Open it.\"}, \"actions\": [" +
                "{\"behavior\": \"use\", \"text\": \"Selecting the door plays A01\", " +
                "\"targets\": [\"CABINET|DOOR_GRP\"], \"clip\": \"A01\", \"fields\": {\"with\": \"TOOLS|KEY_GRP\"}}]}]}",
                "plan.json", out string refusal);
            Assert.That(refusal, Is.Null);
            Assert.That(plan.source.delivery, Is.EqualTo("d.fbx"));
            var action = plan.steps[0].actions[0];
            Assert.That((action.behavior, action.clip, action.Field("with")), Is.EqualTo(("use", "A01", "TOOLS|KEY_GRP")));
            Assert.That(plan.steps[0].Field("voice"), Is.EqualTo("Open it."));
            Assert.That(action.targets, Is.EqualTo(new[] { "CABINET|DOOR_GRP" }));
            Assert.That(action.to, Is.Null, "an optional key left out reads as null");
        }

        [TestCase("not json", "is not JSON")]
        [TestCase("[1, 2]", "is not a sheet plan (no JSON object)")]
        [TestCase("{\"version\": 1}", "is not a sheet plan")]
        [TestCase("{\"version\": 1, \"steps\": []}", "is a version 1 plan; this editor reads 2 only")]
        [TestCase("{\"version\": 3, \"steps\": [{\"actions\": []}]}", "is not a sheet plan")]
        public void Read_refuses_what_is_no_plan_of_its_version_whole(string json, string says)
        {
            Assert.That(SheetPlan.Read(json, "plan.json", out string refusal), Is.Null);
            Assert.That(refusal, Does.Contain(says));
            Assert.That(refusal, Does.Contain("sheet.sync"), "the refusal says what to do: stage it again");
        }

        [Test]
        public void A_rows_fields_and_a_kits_extension_are_read_by_name()
        {
            var plan = SheetPlan.Read(
                "{\"version\": 3, \"steps\": [{\"id\": \"A01\", \"actions\": [" +
                "{\"behavior\": \"indicate\", \"text\": \"Arrows point at the latches\", \"targets\": [\"A\", \"B\"], " +
                "\"fields\": {\"where\": [\"POINTS|A\", \"\"], \"phrase\": \"the latches\"}}]}], " +
                "\"extensions\": {\"some_kit\": {\"speed\": 2}}}", "plan.json", out string refusal);
            Assert.That(refusal, Is.Null);
            var action = plan.steps[0].actions[0];
            Assert.That(action.FieldList("where"), Is.EqualTo(new[] { "POINTS|A", "" }), "one per target, empty where none");
            Assert.That(action.Field("where"), Is.Null, "a list is no text");
            Assert.That(action.FieldList("phrase"), Is.EqualTo(new[] { "the latches" }), "a text is a list of one");
            Assert.That((action.Field("absent"), plan.steps[0].Field("absent")), Is.EqualTo(((string)null, (string)null)));
            Assert.That(plan.Extension("some_kit")["speed"], Is.EqualTo(2.0));
            Assert.That(plan.Extension("other_kit"), Is.Null);

            action.SetField("clip", "A01");
            action.SetField("label", "Next");
            action.SetField("phrase", null);
            Assert.That((action.clip, action.Field("label"), action.Field("phrase")), Is.EqualTo(("A01", "Next", (string)null)),
                        "a declared field by its name, any other in the row's fields");
            Assert.That(SheetPlan.DeclaredFields, Is.EquivalentTo(new[] { "clip", "to" }));
            Assert.That(SheetPlan.Read("{\"version\": 3, \"steps\": [], \"extensions\": {\"kit\": 3}}", "p", out refusal), Is.Null,
                        "a kit's entry is an object");
        }
    }
}
