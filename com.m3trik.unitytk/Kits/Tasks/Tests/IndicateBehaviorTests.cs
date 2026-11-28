using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// <see cref="IndicateBehavior"/>: its arrows are named one way (the step's, and a thing's of several), things of
    /// one target closer together than the object's merge distance share one arrow (two latches side by side), those
    /// further apart keep one each, and a build that merges them no longer takes the arrows it made before away.
    /// </summary>
    public class IndicateBehaviorTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            foreach (string made in new[] { "Task", Viewpoints.ViewpointsActions.EyesName })
            {
                var node = GameObject.Find(made);
                if (node != null) Object.DestroyImmediate(node);
            }
        }

        [Test]
        public void An_arrow_the_kit_made_is_known_by_its_key_whatever_its_name_and_by_the_names_builds_gave_before()
        {
            Assert.That(IndicateBehavior.ArrowName("A01"), Is.EqualTo("Indicator A01"));
            Assert.That(IndicateBehavior.ArrowName("A01", "LATCH_UPPER_GRP"), Is.EqualTo("Indicator A01 LATCH_UPPER_GRP"));
            _root = new GameObject("arrows");
            Transform Child(string name, string key = null)
            {
                var made = new GameObject(name);
                made.transform.SetParent(_root.transform);
                if (key != null) made.AddComponent<Placement.PlacedSpot>().key = key;
                return made.transform;
            }
            Assert.That(IndicateBehavior.IsArrow(Child("Indicator A01"), "A01") && IndicateBehavior.IsArrow(Child("Indicator A01 LATCH_UPPER_GRP"), "A01"),
                        "named as a build named them before");
            Assert.That(IndicateBehavior.IsArrow(Child("POINT_A01_LATCH", "indicate/A01/CABINET|LATCH"), "A01"),
                        "named as every tool names a row's own helper: known by the key it was placed under");
            Assert.That(IndicateBehavior.IsArrow(Child("Indicator A015"), "A01") || IndicateBehavior.IsArrow(Child("Indicator A02"), "A01")
                        || IndicateBehavior.IsArrow(Child("POINT_A02_LATCH", "indicate/A02/CABINET|LATCH"), "A01"), Is.False,
                        "another step's arrow is not this step's");
            Assert.That(IndicateBehavior.IsArrow(Child("POINT_A01_LATCH", "where/CABINET|POINT_A01_LATCH"), "A01"), Is.False,
                        "a helper the delivery brought is none of the kit's");
        }

        [Test]
        public void A_row_whose_where_is_an_empty_object_gets_the_kits_arrow_on_it_and_searches_for_no_spot()
        {
            // A nut shut inside a box: no spot beside it is both free and seen, so a search says so.
            _root = new GameObject("cabinet_2026");
            GameObject Cube(string name, Vector3 at, Vector3 size)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = name;
                cube.transform.SetParent(_root.transform);
                cube.transform.SetPositionAndRotation(at, Quaternion.identity);
                cube.transform.localScale = size;
                return cube;
            }
            Cube("FLOOR", new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20));
            Cube("BOX", new Vector3(0, 1f, 0), Vector3.one);
            GameObject nut = Cube("NUT", new Vector3(0, 1f, 0), Vector3.one * 0.02f);
            var place = new GameObject("POINT_NUT").transform;
            place.SetParent(_root.transform);
            place.SetPositionAndRotation(new Vector3(0, 1.6f, -0.2f), Quaternion.LookRotation(Vector3.down));
            string Plan(string fields) => "{\"version\": 3, \"steps\": [{\"id\": \"B11\", \"actions\": [{\"behavior\": \"indicate\", " +
                                          "\"text\": \"Arrow points at the nut\", \"targets\": [\"NUT\"]" + fields + "}]}, " +
                                          "{\"id\": \"C02\", \"actions\": [{\"behavior\": \"indicate\", \"text\": \"Arrow points at the nut\", " +
                                          "\"targets\": [\"NUT\"]" + fields + "}]}]}";
            var searched = SheetPlan.Read(Plan(""), "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            var first = new OpContext();
            ManifestsActions.Build(searched, new ManifestScene(_root), first);
            Assert.That(first.Issues.Count(i => i.code == TaskKit.SpotCode), Is.EqualTo(2), "no where: each step's arrow is searched for, and none is found");
            Assert.That(nut.GetComponentsInChildren<Indicators.Indicator>(true).Length, Is.EqualTo(2));
            Assert.That(nut.GetComponentsInChildren<Indicators.Indicator>(true).Select(a => a.GetComponent<RenderEffects.EffectExempt>() != null), Is.All.True,
                        "an arrow under a part is left out of the part's fades and highlights");

            var named = SheetPlan.Read(Plan(", \"fields\": {\"where\": [\"POINT_NUT\"]}"), "test plan", out refusal);
            Assert.That(refusal, Is.Null);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(named, new ManifestScene(_root), dry);
            var live = new OpContext();
            ManifestsActions.Build(named, new ManifestScene(_root), live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(live.Issues.Where(i => i.code == TaskKit.SpotCode), Is.Empty, "its where is where it is: nothing is searched for");
            Assert.That(nut.GetComponentsInChildren<Indicators.Indicator>(true), Is.Empty, "the arrows a build searched for go");
            Indicators.Indicator arrow = place.GetComponentsInChildren<Indicators.Indicator>(true).Single();
            Assert.That(arrow.Target, Is.SameAs(nut.transform), "one arrow on the place, for both steps, pointing at the nut");
            Assert.That(arrow.GetComponent<RenderEffects.EffectExempt>(), Is.Not.Null, "no part of the look of what it sits under");
            Assert.That(place.position, Is.EqualTo(new Vector3(0, 1.6f, -0.2f)), "the place stands where it was put");
            var steps = GameObject.Find("Task").GetComponent<Steps.Sequence>();
            Assert.That(steps.steps.Select(s => s.activates.Contains(arrow)), Is.EqualTo(new[] { true, true }), "shown while each of its steps runs");

            var again = new OpContext();
            ManifestsActions.Build(named, new ManifestScene(_root), again);
            Assert.That(again.Changes, Is.Empty, "a second build changes nothing");
        }

        [Test]
        public void An_arrows_style_size_and_colour_are_its_rows_settings_by_the_sheets_names()
        {
            // indicate's style, size and color, as a row's Settings cell gives them: under the word's own settings.
            _root = new GameObject("cabinet_2026");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "FLOOR";
            floor.transform.SetParent(_root.transform);
            floor.transform.SetPositionAndRotation(new Vector3(0, -0.05f, 0), Quaternion.identity);
            floor.transform.localScale = new Vector3(20, 0.1f, 20);
            var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lid.name = "LID";
            lid.transform.SetParent(_root.transform);
            lid.transform.SetPositionAndRotation(new Vector3(0, 1f, 0), Quaternion.identity);
            lid.transform.localScale = Vector3.one * 0.2f;
            var plan = SheetPlan.Read("{\"version\": 3, \"steps\": [{\"id\": \"A01\", \"actions\": [{\"behavior\": \"indicate\", " +
                                      "\"text\": \"Arrow points at the lid\", \"targets\": [\"LID\"], " +
                                      "\"settings\": {\"style\": \"solid\", \"size\": \"0.3\", \"color\": \"#FF0000\"}}]}]}", "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, new ManifestScene(_root), dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_root), live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(live.Issues.Where(i => i.code == ManifestsActions.NoteCode), Is.Empty, "every setting is one of the word's");
            Indicators.Indicator arrow = lid.GetComponentInChildren<Indicators.Indicator>(true);
            Assert.That((arrow.flat, arrow.size, arrow.color), Is.EqualTo((false, 0.3f, Color.red)), "solid, 0.3 m, red");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, new ManifestScene(_root), again);
            Assert.That(again.Changes, Is.Empty, "a second build changes nothing");
        }

        [Test]
        public void Things_close_together_share_an_arrow_and_an_arrow_no_longer_made_goes()
        {
            // Two latches 6 cm apart under one group, a floor below.
            _root = new GameObject("cabinet_2026");
            var latches = new GameObject("LATCHES_GRP").transform;
            latches.SetParent(_root.transform);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "FLOOR";
            floor.transform.SetParent(_root.transform);
            floor.transform.SetPositionAndRotation(new Vector3(0, -0.05f, 0), Quaternion.identity);
            floor.transform.localScale = new Vector3(20, 0.1f, 20);
            foreach (var (name, y) in new[] { ("LATCH_UPPER", 1.11f), ("LATCH_LOWER", 1f) })
            {
                var latch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                latch.name = name;
                latch.transform.SetParent(latches);
                latch.transform.SetPositionAndRotation(new Vector3(0, y, 0), Quaternion.identity);
                latch.transform.localScale = Vector3.one * 0.05f;
            }
            var plan = SheetPlan.Read("{\"version\": 3, \"steps\": [{\"id\": \"A01\", \"actions\": [" +
                                      "{\"behavior\": \"indicate\", \"text\": \"Arrows point at both latches\", \"targets\": [\"LATCHES_GRP\"]}]}]}",
                                      "test plan", out string refusal);
            Assert.That(refusal, Is.Null);

            var apart = ScriptableObject.CreateInstance<TaskSettings>();
            apart.indicate.merge = 0f;  // an arrow at each
            try
            {
                ManifestsActions.Build(plan, new ManifestScene(_root).Use(apart), new OpContext());
                Assert.That(latches.GetComponentsInChildren<Indicators.Indicator>().Select(a => a.name),
                            Is.EquivalentTo(new[] { "Indicator A01 LATCH_UPPER", "Indicator A01 LATCH_LOWER" }), "merge 0: an arrow at each");

                // The task's merge distance (0.15 m): the two share one, and the arrows made apart go.
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, new ManifestScene(_root), dry);
                var live = new OpContext();
                ManifestsActions.Build(plan, new ManifestScene(_root), live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(live.Changes.Count(c => c.kind == "delete"), Is.EqualTo(2), "the two arrows made apart are taken away");
                Assert.That(latches.GetComponentsInChildren<Indicators.Indicator>().Select(a => a.name), Is.EqualTo(new[] { "Indicator A01" }),
                            "one arrow, the target's own name");
            }
            finally
            {
                Object.DestroyImmediate(apart);
            }
        }
    }
}
