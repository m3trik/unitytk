using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// The Manifests actions, over a stand-in behavior and kit (<see cref="TestPartBehavior"/>,
    /// <see cref="TestKit"/>): an assessment says where each row stands and changes nothing, a build
    /// brings the scene in line and is held to the kernel's three checks (one Ctrl+Z restores the
    /// scene, the dry run's plan names every change the run makes, a second run plans nothing), a
    /// locked step is left alone, and a manifest places its delivery, asks its kits to keep or
    /// forget a person's adjustments, and builds anew in a new scene.
    /// </summary>
    public class ManifestsActionsTests
    {
        const string PrefabPath = "Assets/ManifestsActionsTests delivery.prefab";
        const string PlanPath = "Assets/ManifestsActionsTests.plan.json";

        const string Json =
            "{\"version\": 3, \"source\": {\"sheet\": \"test.csv\", \"delivery\": \"delivery.fbx\"}, \"steps\": [" +
            "{\"id\": \"S01\", \"name\": \"One\", \"actions\": [" +
            "{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"]}," +
            "{\"behavior\": \"test_part\", \"text\": \"Part B\", \"targets\": [\"B\"], \"fields\": {\"value\": \"trigger\"}}]}," +
            "{\"id\": \"S02\", \"name\": \"Two\", \"actions\": [" +
            "{\"behavior\": \"test_part\", \"text\": \"Part C\", \"targets\": [\"NOPE\"]}," +
            "{\"behavior\": \"nothing_builds_this\", \"text\": \"Unknown\"}," +
            "{\"behavior\": \"test_part\", \"text\": \"Unread\", \"targets\": [\"A\"], " +
            "\"issues\": [\"row 3 (Step Contents): no phrase reads it: write an Action\"]}]}]}";

        GameObject _root;
        ManifestScene _scene;
        SheetPlan.Shape.Payload _plan;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _root = Delivery();
            _scene = new ManifestScene(_root, new AnimationClip[0]);
            _plan = SheetPlan.Read(Json, "test", out string refusal);
            Assert.That(refusal, Is.Null);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(PrefabPath);
            AssetDatabase.DeleteAsset(PlanPath);
            AssetDatabase.DeleteAsset("Assets/ManifestsActionsTests.manifest.asset");
            AssetDatabase.DeleteAsset("Assets/ManifestsActionsTests.unity");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static GameObject Delivery()
        {
            var root = new GameObject("delivery");
            foreach (string name in new[] { "A", "B" }) new GameObject(name).transform.SetParent(root.transform);
            return root;
        }

        BoxCollider Part(string name) => _root.transform.Find(name).GetComponent<BoxCollider>();

        [Test]
        public void An_assessment_says_where_each_row_stands_and_changes_nothing()
        {
            ManifestAssessment assessed = ManifestsActions.Assess(_plan, _scene);

            Assert.That(assessed.rows.Select(r => r.Status), Is.EqualTo(new[]
            {
                ManifestStatus.Missing, ManifestStatus.Missing,
                ManifestStatus.MissingObject, ManifestStatus.UnknownBehavior, ManifestStatus.Sheet,
            }));
            Assert.That(assessed.steps.Select(s => s.id), Is.EqualTo(new[] { "S01", "S02" }));
            Assert.That(assessed.steps[0].rows, Is.EqualTo(assessed.rows.Take(2)), "a step's rows are the plan's, in order");
            Assert.That(assessed.rows[0].changes.Select(c => (c.kind, c.target)), Is.EqualTo(new[] { ("create", $"{_root.name}/A/BoxCollider") }),
                        "a row's changes are what a build makes for it");
            Assert.That(assessed.steps[0].changes.Select(c => c.target), Is.EqualTo(new[] { $"{TestKit.Root}/S01" }), "a step's own part is the step's");
            Assert.That(assessed.shared.changes.Select(c => c.target), Is.EqualTo(new[] { TestKit.Root }), "what the steps share is no step's");
            Assert.That(assessed.steps[0].Status, Is.EqualTo(ManifestStatus.Missing));
            Assert.That(assessed.steps[1].Status, Is.EqualTo(ManifestStatus.MissingObject), "a step says its worst row");
            Assert.That(assessed.steps[1].NeedsYou && assessed.steps[1].NeedsBuild, "its own part is still to build");

            ManifestAssessment.BuildPlan plan = assessed.Plan();
            Assert.That((plan.steps, plan.create, plan.update, plan.needsYou, plan.shared), Is.EqualTo((2, 2, 0, 3, 1)));
            Assert.That(plan.Count, Is.EqualTo(3), "the steps a build changes, and one for what they share");
            Assert.That(plan.Text(), Does.Contain("builds 2 row(s)").And.Contain("3 row(s) wait"));
            Assert.That(assessed.Summary(), Does.Contain("2 to build").And.Contain("3 need you"));

            Assert.That(_root.GetComponentsInChildren<BoxCollider>(), Is.Empty, "an assessment changes nothing");
            Assert.That(SceneRoots.Find(TestKit.Root), Is.Null);
        }

        [Test]
        public void A_build_brings_the_scene_in_line_and_each_row_holds_its_parts()
        {
            var ctx = new OpContext();
            ManifestAssessment built = ManifestsActions.Build(_plan, _scene, ctx);

            Assert.That(Part("A"), Is.Not.Null);
            Assert.That(Part("B").isTrigger, "built as the row says");
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[]
            {
                ManifestsActions.UnresolvedCode, ManifestsActions.UnsupportedCode, ManifestsActions.UnresolvedCode,
            }), "a row that cannot be built is raised, never skipped silently");
            Assert.That(built.rows[0].Status, Is.EqualTo(ManifestStatus.Valid), "what a build made is no longer to do");
            Assert.That(built.rows[0].parts, Is.EqualTo(new Object[] { Part("A") }));
            Assert.That(built.rows.Skip(2).SelectMany(r => r.notes), Is.Empty, "the build's own word on a row it left out is no note");

            ManifestAssessment after = ManifestsActions.Assess(_plan, _scene);
            Assert.That(after.rows.Take(2).Select(r => r.Status), Is.All.EqualTo(ManifestStatus.Valid));
            Assert.That(after.rows[1].parts, Is.EqualTo(new Object[] { Part("B") }), "found made: the row still holds it");
            Assert.That(after.steps[0].parts.Single().name, Is.EqualTo("S01"));
            Assert.That(after.shared.parts.Single().name, Is.EqualTo(TestKit.Root));
            Assert.That(after.Plan().Any, Is.False);

            Part("B").isTrigger = false;  // a person changes it
            ManifestAssessment drifted = ManifestsActions.Assess(_plan, _scene);
            Assert.That(drifted.rows[1].Status, Is.EqualTo(ManifestStatus.Update), "there, but not as the plan says");
            Assert.That(drifted.Plan().update, Is.EqualTo(1));
        }

        [Test]
        public void A_build_is_planned_undone_and_converges()
        {
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(_plan, _scene, dry);
            Assert.That(_root.GetComponentsInChildren<BoxCollider>(), Is.Empty, "a dry run makes nothing");

            OpContext live = null;
            OpContext.RunMenu("Build Manifest", ctx =>
            {
                live = ctx;
                ManifestsActions.Build(_plan, _scene, ctx);
            });
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))),
                        "the plan names every change the run makes");

            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_plan, _scene, again);
            Assert.That(again.Changes, Is.Empty, "a second build plans nothing");

            Undo.PerformUndo();
            Assert.That(_root.GetComponentsInChildren<BoxCollider>(), Is.Empty, "one undo takes the build back");
            Assert.That(SceneRoots.Find(TestKit.Root), Is.Null);
        }

        [Test]
        public void The_plan_is_built_less_what_the_manifest_turned_off_and_plus_what_it_added()
        {
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            var text = new TextAsset(Json);
            try
            {
                manifest.plan = text;
                Assert.That(ManifestsActions.Effective(_plan, manifest), Is.SameAs(_plan), "nothing turned off or added: the sheet's plan itself");

                // S01 leaves Part B out; S02 gives B a part the sheet does not.
                Assert.That(ManifestsActions.SetAssigned(manifest, "S01", "B", "test_part", false), Is.True);
                Assert.That(ManifestsActions.SetAssigned(manifest, "S01", "B", "test_part", false), Is.False, "said twice: nothing more to keep");
                Assert.That(ManifestsActions.SetAssigned(manifest, "S02", "B", "test_part", true), Is.True);
                Assert.That(manifest.edits.Select(e => (e.step, e.target, e.add)), Is.EqualTo(new[] { ("S01", "B", false), ("S02", "B", true) }));
                SheetPlan.Shape.Payload built = ManifestsActions.Effective(_plan, manifest);
                Assert.That(built.steps[0].actions.Select(a => a.text), Is.EqualTo(new[] { "Part A" }));
                Assert.That(built.steps[1].actions.Select(a => a.behavior + " " + string.Join(",", a.targets ?? new System.Collections.Generic.List<string>())).Last(),
                            Is.EqualTo("test_part B"), "what it added, after the sheet's rows");
                Assert.That(_plan.steps[0].actions.Count, Is.EqualTo(2), "the sheet's plan is left as it was read");

                _scene.Manifest = manifest;
                ManifestAssessment assessed = ManifestsActions.Build(_plan, _scene, new OpContext());
                Assert.That(Part("A"), Is.Not.Null);
                Assert.That(Part("B"), Is.Not.Null, "built for S02, where this project added it");
                Assert.That(Part("B").isTrigger, Is.False, "as the added row says, not as the row left out did");
                ManifestAssessment.Step one = assessed.steps[0], two = assessed.steps[1];
                Assert.That(one.rows.Select(r => (r.text, r.source)), Is.EqualTo(new[] { ("Part A", 0) }));
                Assert.That(one.off.Select(r => (r.text, r.source, r.Status)), Is.EqualTo(new[] { ("Part B", 1, ManifestStatus.Off) }), "the row left out is listed, never built");
                Assert.That(two.rows.Last().added && two.rows.Last().source == -1);
                Assert.That(_scene.Source("S01"), Is.EqualTo("test_part A"), "what a step is built from: its rows as built");

                // A behavior off for the whole manifest: every row of it left out, what it added too.
                Assert.That(ManifestsActions.SetOff(manifest, new[] { "test_part" }, true), Is.EqualTo(1));
                Assert.That(ManifestsActions.SetOff(manifest, new[] { "test_part" }, true), Is.EqualTo(0));
                built = ManifestsActions.Effective(_plan, manifest);
                Assert.That(built.steps.SelectMany(s => s.actions).Select(a => a.behavior), Is.EqualTo(new[] { "nothing_builds_this" }));
                var plan = new OpContext(dryRun: true);
                Assert.That(ManifestsActions.SetOff(manifest, new[] { "test_part" }, false, plan), Is.EqualTo(0), "a dry run plans it");
                Assert.That(plan.Changes.Single().action, Is.EqualTo("turn test_part on"));
                Assert.That(manifest.off, Is.EqualTo(new[] { "test_part" }));
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                Object.DestroyImmediate(text);
            }
        }

        [Test]
        public void Revert_drops_an_objects_a_steps_or_every_edit()
        {
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                manifest.edits.Add(new Manifest.Edit { step = "S01", target = "B", behavior = "test_part" });
                manifest.edits.Add(new Manifest.Edit { step = "S02", target = "B", behavior = "test_part", add = true });
                manifest.edits.Add(new Manifest.Edit { step = "S02", target = "A", behavior = "test_part" });
                var plan = new OpContext(dryRun: true);
                Assert.That(ManifestsActions.Revert(manifest, "S02", "B", plan), Is.EqualTo(1));
                Assert.That(manifest.edits.Count, Is.EqualTo(3), "a dry run keeps them");
                Assert.That(ManifestsActions.Revert(manifest, "S02", "B"), Is.EqualTo(1));
                Assert.That(manifest.edits.Select(e => (e.step, e.target)), Is.EqualTo(new[] { ("S01", "B"), ("S02", "A") }));
                Assert.That(ManifestsActions.Revert(manifest, "S02"), Is.EqualTo(1), "every object of the step");
                Assert.That(ManifestsActions.Revert(manifest), Is.EqualTo(1), "every edit");
                Assert.That(manifest.edits, Is.Empty);
                Assert.That(ManifestsActions.Revert(manifest), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void An_objects_own_values_override_its_kits_split_it_off_its_row_complete_an_added_row_and_revert()
        {
            // The user's decision: per object, optionally, over the global setting; any behavior can be added, and has settings.
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            var kit = ScriptableObject.CreateInstance<TestSettings>();
            TestSettings ownB = null;  // the scene's copy for B (a build's run destroys its copies as it ends; this scene has no run)
            try
            {
                manifest.settings.Add(kit);
                string two = Json.Replace("\"targets\": [\"A\"]}", "\"targets\": [\"A\", \"B\"]}");
                var plan = SheetPlan.Read(two, "test", out _);

                Assert.That(ManifestsActions.SetTune(manifest, "S01", "B", "test_part", "size", "2.5", new OpContext(dryRun: true)), Is.True);
                Assert.That(manifest.tunes, Is.Empty, "a dry run keeps none");
                Assert.That(ManifestsActions.SetTune(manifest, "S01", "B", "test_part", "size", "2.5"), Is.True);
                Assert.That(ManifestsActions.SetTune(manifest, "S01", "B", "test_part", "size", "2.5"), Is.False, "kept already");
                var scene = new ManifestScene(_root, new AnimationClip[0]) { Manifest = manifest };
                ownB = scene.Settings<TestSettings>("S01", "B");
                Assert.That(ownB.size, Is.EqualTo(2.5f), "its own over its kit's");
                Assert.That(scene.Settings<TestSettings>("S01", "A").size, Is.EqualTo(1f), "another object: its kit's");
                Assert.That(ownB, Is.Not.SameAs(kit), "the kit's settings untouched");

                ManifestsActions.SetRowField(manifest, "S01", "A", "test_part", "value", "own");
                var effective = ManifestsActions.Effective(plan, manifest).steps[0].actions;
                Assert.That(effective.Select(a => (string.Join(",", a.targets), a.Field("value"))), Is.EqualTo(new[] { ("B", (string)null), ("A", "own"), ("B", "trigger") }),
                            "an object with a field of its own stands as a row of its own; the sheet's row keeps the rest");

                manifest.edits.Add(new Manifest.Edit { step = "S02", target = "B", behavior = "test_part", add = true });
                ManifestsActions.SetRowField(manifest, "S02", "B", "test_part", "value", "added");
                Assert.That(ManifestsActions.Effective(plan, manifest).steps[1].actions.Last().Field("value"), Is.EqualTo("added"), "an added row has its fields");

                var own = (TestSettings)ManifestsActions.OwnSettings(manifest, typeof(TestSettings), "S01", "B");
                own.size = 1f;  // back to the kit's value: no longer its own
                Assert.That(ManifestsActions.SetTunes(manifest, "S01", "B", "test_part", own, new[] { "size" }), Is.EqualTo(1));
                Object.DestroyImmediate(own);
                Assert.That(manifest.TuneOf("S01", "B", "test_part"), Is.Null, "nothing of its own left");
                Assert.That(ManifestsActions.Revert(manifest, "S01", "A", new OpContext(dryRun: true)), Is.EqualTo(1),
                            "a dry run counts what it would drop, its own values too (what the window's Revert offers by)");
                Assert.That(ManifestsActions.Revert(manifest, "S01", "A"), Is.EqualTo(1), "its own values go with its edits");
                Assert.That(manifest.TuneOf("S01", "A", "test_part"), Is.Null);
            }
            finally
            {
                if (ownB != null && ownB != kit) Object.DestroyImmediate(ownB);
                Object.DestroyImmediate(manifest);
                Object.DestroyImmediate(kit);
            }
        }

        [Test]
        public void Two_rows_of_one_behavior_on_one_object_are_edited_apart_by_their_place_in_the_step()
        {
            // A marker shown then hidden in one step: one behavior, two rows on one line; an edit or a value is one row's.
            const string twice =
                "{\"version\": 3, \"steps\": [{\"id\": \"S01\", \"actions\": [" +
                "{\"behavior\": \"test_part\", \"text\": \"First\", \"targets\": [\"A\"], \"fields\": {\"value\": \"one\"}}," +
                "{\"behavior\": \"test_part\", \"text\": \"Second\", \"targets\": [\"A\"], \"fields\": {\"value\": \"trigger\"}}]}]}";
            var plan = SheetPlan.Read(twice, "test", out _);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            var text = new TextAsset(twice);
            try
            {
                manifest.plan = text;
                Assert.That(ManifestsActions.SetAssigned(manifest, "S01", "A", "test_part", false, null, 1), Is.True);
                Assert.That(ManifestsActions.Effective(plan, manifest).steps[0].actions.Select(a => a.Field("value")), Is.EqualTo(new[] { "one" }),
                            "the second row alone is taken away");
                ManifestsActions.SetRowField(manifest, "S01", "A", "test_part", "value", "own", null, 0);
                Assert.That(ManifestsActions.Effective(plan, manifest).steps[0].actions.Single().Field("value"), Is.EqualTo("own"), "the first row's own value");
                Assert.That(ManifestsActions.SetAssigned(manifest, "S01", "A", "test_part", true, null, 1), Is.True, "given back");
                Assert.That(ManifestsActions.Effective(plan, manifest).steps[0].actions.Select(a => a.Field("value")), Is.EqualTo(new[] { "own", "trigger" }),
                            "the second row as the sheet writes it; the first keeps its own");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                Object.DestroyImmediate(text);
            }
        }

        [Test]
        public void A_steps_own_behavior_is_turned_off_and_back_as_an_objects_is()
        {
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            var text = new TextAsset(Json);
            try
            {
                manifest.plan = text;
                // S02's "Unknown" row names no object: the step's own, edited by an empty target.
                Assert.That(ManifestsActions.SetAssigned(manifest, "S02", "", "nothing_builds_this", false), Is.True);
                Assert.That(ManifestsActions.Effective(_plan, manifest).steps[1].actions.Exists(a => a.behavior == "nothing_builds_this"), Is.False,
                            "left out of the step");
                Assert.That(ManifestsActions.SetAssigned(manifest, "S02", null, "nothing_builds_this", true), Is.True, "given back: the edit goes");
                Assert.That(manifest.edits, Is.Empty);
                Assert.That(ManifestsActions.SetAssigned(manifest, "S01", "", "nothing_builds_this", true), Is.True);
                var added = ManifestsActions.Effective(_plan, manifest).steps[0].actions.Last();
                Assert.That((added.behavior, added.targets.Count), Is.EqualTo(("nothing_builds_this", 0)), "a step's own added: no object");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                Object.DestroyImmediate(text);
            }
        }

        [Test]
        public void The_objects_the_delivery_made_a_row_on_are_left_to_it_and_the_rest_built()
        {
            var row = _plan.steps[0].actions[0];
            row.targets = new System.Collections.Generic.List<string> { "A", "B" };
            row.upstream = new System.Collections.Generic.List<string> { "B" };
            var built = ManifestsActions.Effective(_plan, null).steps[0].actions[0];
            Assert.That(built.targets, Is.EqualTo(new[] { "A" }), "only what the delivery did not make");
            Assert.That(built.upstream, Is.EqualTo(new[] { "B" }));
            Assert.That(row.targets, Is.EqualTo(new[] { "A", "B" }), "the sheet's plan is left as it was read");
        }

        [Test]
        public void A_row_the_deliverys_own_build_made_is_left_to_it()
        {
            // The plan says the delivery's own build keyed S01's Part B (its manifest's 'keyed'): built once, there.
            _plan.steps[0].actions[1].upstream = new System.Collections.Generic.List<string> { "B" };
            ManifestAssessment assessed = ManifestsActions.Build(_plan, _scene, new OpContext());
            Assert.That(Part("A"), Is.Not.Null, "its other row is built here");
            Assert.That(Part("B"), Is.Null, "the delivery's is never built again");
            ManifestAssessment.Row row = assessed.steps[0].rows[1];
            Assert.That((row.state, row.Status, ManifestStatus.Label(row.Status)),
                        Is.EqualTo((ManifestAssessment.State.Upstream, ManifestStatus.Upstream, "in the delivery")));
            Assert.That(row.NeedsBuild, Is.False);
        }

        [Test]
        public void A_locked_step_and_a_step_not_asked_for_are_left_as_they_are()
        {
            ManifestsActions.Build(_plan, _scene, new OpContext(), only: new[] { "S02" });
            Assert.That(Part("A"), Is.Null, "S01 was not asked for");
            Assert.That(SceneRoots.Find(TestKit.Root).transform.Find("S01"), Is.Null, "nor its own part");
            Assert.That(SceneRoots.Find(TestKit.Root).transform.Find("S02"), Is.Not.Null);

            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                Assert.That(ManifestsActions.SetLocked(manifest, new[] { "S01", "S01" }, true, new OpContext()), Is.EqualTo(1));
                _scene.Manifest = manifest;
                ManifestAssessment built = ManifestsActions.Build(_plan, _scene, new OpContext());
                Assert.That(Part("A"), Is.Null, "a build leaves a locked step as it is");
                Assert.That(built.steps[0].Status, Is.EqualTo(ManifestStatus.Locked));
                Assert.That(ManifestsActions.Assess(_plan, _scene).Plan().locked, Is.EqualTo(1));

                Assert.That(ManifestsActions.SetLocked(manifest, new[] { "S01" }, false, new OpContext()), Is.EqualTo(1));
                ManifestsActions.Build(_plan, _scene, new OpContext());
                Assert.That(Part("A"), Is.Not.Null, "unlocked: built");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_locked_step_still_says_what_it_holds_and_a_scoped_build_marks_only_what_it_built()
        {
            ManifestAssessment scoped = ManifestsActions.Build(_plan, _scene, new OpContext(), only: new[] { "S01" });
            Assert.That(scoped.steps.Select(s => s.made), Is.EqualTo(new[] { true, false }), "only the steps it built are made");

            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                manifest.locked.Add("S01");
                _scene.Manifest = manifest;
                ManifestAssessment assessed = ManifestsActions.Assess(_plan, _scene);
                Assert.That(assessed.steps[0].Status, Is.EqualTo(ManifestStatus.Locked));
                Assert.That(assessed.rows[0].parts, Is.EqualTo(new Object[] { Part("A") }), "a locked row still lists its part, to select and edit");
                Assert.That(assessed.steps[0].parts.Single().name, Is.EqualTo("S01"));
                Assert.That(assessed.steps[0].NeedsBuild, Is.False, "and a build still leaves it as it is");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_manifest_places_its_delivery_keeps_adjustments_and_builds_anew()
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, PrefabPath);
            Object.DestroyImmediate(_root);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            manifest.hideFlags = HideFlags.HideAndDontSave;  // kept across the new scene a reset opens, as an asset is
            manifest.plan = new TextAsset(Json) { hideFlags = HideFlags.HideAndDontSave };
            manifest.delivery = prefab;
            try
            {
                ManifestAssessment before = ManifestsActions.Assess(manifest, new OpContext());
                Assert.That(before.rows.Select(r => r.Status), Is.EqualTo(new[]
                {
                    ManifestStatus.Missing, ManifestStatus.Missing, ManifestStatus.Missing,
                    ManifestStatus.UnknownBehavior, ManifestStatus.Sheet,
                }), "before its delivery is placed, the plan alone says what it can");
                Assert.That(before.Plan().Any, "everything is still to build");

                int kept = TestKit.Kept, forgotten = TestKit.Forgotten;
                ManifestAssessment built = ManifestsActions.Build(manifest, new OpContext());
                _root = ManifestsActions.Placed(prefab);
                Assert.That(_root, Is.Not.Null, "its delivery placed in the open scene");
                Assert.That(Part("A"), Is.Not.Null);
                Assert.That(TestKit.Kept, Is.EqualTo(kept), "nothing of it was in the scene yet: nothing of a person's to keep");
                Assert.That(built.rows[2].Status, Is.EqualTo(ManifestStatus.MissingObject), "placed: a name the delivery lacks shows");
                ManifestsActions.Build(manifest, new OpContext());
                Assert.That(TestKit.Kept, Is.EqualTo(kept + 1), "built again where it stands: its kits keep a person's adjustments first");
                Assert.That(ManifestsActions.Assess(manifest).Plan().Any, Is.False, "built: nothing left to do");

                ManifestsActions.Reset(manifest, discard: true, new OpContext());
                Assert.That(TestKit.Forgotten, Is.EqualTo(forgotten + 1), "a reset that discards asks its kits to forget");
                _root = ManifestsActions.Placed(prefab);
                Assert.That(_root != null && Part("A") != null, "built anew in a new scene");

                kept = TestKit.Kept;
                manifest.locked.Add("S01");
                var refused = new OpContext();
                Assert.That(ManifestsActions.Reset(manifest, discard: false, refused), Is.Null, "a new scene holds nothing of a locked step");
                Assert.That(refused.Issues.Single().message, Does.Contain("locked (S01)").And.Contain("Unlock them first"));
                Assert.That(ManifestsActions.Placed(prefab), Is.SameAs(_root), "the open scene is left as it was");
                Assert.That(TestKit.Kept, Is.EqualTo(kept), "and nothing was kept or forgotten for it");
                manifest.locked.Clear();

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);  // another's scene: nothing of this manifest in it
                ManifestsActions.Build(manifest, new OpContext());
                Assert.That(TestKit.Kept, Is.EqualTo(kept), "a build where nothing of it is placed keeps nothing from what is open");
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_move_onto_a_re_export_removes_the_staged_copy_it_moved_off_and_nothing_else()
        {
            // A dated re-export staged beside the plan added 190 MB a time: the copies the manifest no longer built stayed.
            const string Folder = "Assets/ManifestsActionsTests staged";
            AssetDatabase.CreateFolder("Assets", "ManifestsActionsTests staged");
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            manifest.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                GameObject v1 = PrefabUtility.SaveAsPrefabAsset(_root, Folder + "/delivery v1.prefab");
                GameObject v2 = PrefabUtility.SaveAsPrefabAsset(_root, Folder + "/delivery v2.prefab");
                AssetDatabase.CreateFolder(Folder, "delivery v1_Media");
                Object.DestroyImmediate(_root);
                File.WriteAllText(Folder + "/task.plan.json", Json.Replace("delivery.fbx", "delivery v1.prefab"));
                AssetDatabase.ImportAsset(Folder + "/task.plan.json");
                manifest.plan = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/task.plan.json");
                manifest.delivery = v1;
                ManifestsActions.Build(manifest, new OpContext());
                File.WriteAllText(Folder + "/task.plan.json", Json.Replace("delivery.fbx", "delivery v2.prefab"));  // staged again, a re-export
                AssetDatabase.ImportAsset(Folder + "/task.plan.json");

                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, dry);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(Folder + "/delivery v1.prefab"), Is.Not.Null, "a dry run plans it");
                var live = new OpContext();
                ManifestsActions.Build(manifest, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.action))), "the plan is the run");
                Assert.That(manifest.delivery, Is.SameAs(v2));
                Assert.That(AssetDatabase.LoadMainAssetAtPath(Folder + "/delivery v1.prefab"), Is.Null, "the copy it moved off is gone");
                Assert.That(AssetDatabase.IsValidFolder(Folder + "/delivery v1_Media"), Is.False, "with its media");
                Assert.That(AssetDatabase.LoadMainAssetAtPath(Folder + "/delivery v2.prefab"), Is.Not.Null);
                _root = ManifestsActions.Placed(v2);
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [Test]
        public void A_move_onto_a_re_export_keeps_the_staged_copy_a_scene_saved_with_it_still_places()
        {
            // Built while another scene was open, the move deleted the old model the manifest's saved scene still placed: that
            // scene lost its instance, and every adjustment on it, with no undo.
            const string Folder = "Assets/ManifestsActionsTests kept";
            AssetDatabase.CreateFolder("Assets", "ManifestsActionsTests kept");
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            manifest.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                GameObject v1 = PrefabUtility.SaveAsPrefabAsset(_root, Folder + "/delivery v1.prefab");
                GameObject v2 = PrefabUtility.SaveAsPrefabAsset(_root, Folder + "/delivery v2.prefab");
                Object.DestroyImmediate(_root);
                File.WriteAllText(Folder + "/task.plan.json", Json.Replace("delivery.fbx", "delivery v1.prefab"));
                AssetDatabase.ImportAsset(Folder + "/task.plan.json");
                manifest.plan = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/task.plan.json");
                manifest.delivery = v1;
                manifest.scene = Folder + "/task.unity";
                ManifestsActions.Build(manifest, new OpContext());
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), manifest.scene);  // saved with v1 in it
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);  // another scene open
                File.WriteAllText(Folder + "/task.plan.json", Json.Replace("delivery.fbx", "delivery v2.prefab"));  // staged again, a re-export
                AssetDatabase.ImportAsset(Folder + "/task.plan.json");

                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, dry);
                var live = new OpContext();
                ManifestsActions.Build(manifest, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.action))), "the plan is the run");
                // The same asset, not the same reference: the new scene unloaded v2 (nothing in it held it), and the asset
                // loaded again is another managed object of it.
                Assert.That(manifest.delivery, Is.EqualTo(v2), "it builds into the re-export");
                Assert.That(AssetDatabase.LoadMainAssetAtPath(Folder + "/delivery v1.prefab"), Is.Not.Null, "the copy its saved scene places is kept");
                Assert.That(live.Changes.Any(c => c.kind == "delete"), Is.False, "nothing is deleted");
                Assert.That(live.Issues.Any(i => i.code == ManifestsActions.ManifestCode && i.message.Contains("is kept") && i.message.Contains("task.unity")),
                            "and it says what still places it");
                _root = ManifestsActions.Placed(v2);
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [Test]
        public void A_move_onto_a_re_export_renames_its_roots_records_and_no_other_roots()
        {
            // The real task's delivery came again as a dated re-export: its placed root took the new file's name, and
            // every adjustment kept on its parts (named by their path from that root) was lost with the old name.
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                manifest.built.Add(new Manifest.Value { name = "@task_v1/TOOL#Holdable", value = "{}" });
                manifest.built.Add(new Manifest.Value { name = "@task_v1#Marker", value = "{}" });
                manifest.adjusted.Add(new Manifest.Value { name = "task_v1/TOOL#Holdable.tipPosition", value = "(0, 0, 0.1)" });
                manifest.adjusted.Add(new Manifest.Value { name = "task_v10/TOOL#Holdable.tipPosition", value = "(0, 0, 0.2)" });
                manifest.adjusted.Add(new Manifest.Value { name = "Task (task)/A01#Step.dwell", value = "3" });
                Assert.That(ManifestAdjustments.Rename(manifest, "task_v1", "task_v2"), Is.EqualTo(3));
                Assert.That(manifest.built.Select(v => v.name), Is.EqualTo(new[] { "@task_v2/TOOL#Holdable", "@task_v2#Marker" }));
                Assert.That(manifest.adjusted.Select(v => v.name), Is.EqualTo(new[]
                {
                    "task_v2/TOOL#Holdable.tipPosition",
                    "task_v10/TOOL#Holdable.tipPosition",  // another root that only starts with the name
                    "Task (task)/A01#Step.dwell",          // the kit's own objects keep theirs
                }));
                Assert.That(manifest.adjusted[0].value, Is.EqualTo("(0, 0, 0.1)"), "the value goes with it");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_plan_read_against_a_re_export_moves_the_manifest_onto_it_keeping_what_is_on_its_nodes()
        {
            // A task's deliveries are dated (assembly_2026-11-24.fbx, then _11-25): staged and read again, the plan
            // named the new one and the manifest went on building the old.
            const string NextPath = "Assets/ManifestsActionsTests delivery v2.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, PrefabPath);
            GameObject next = PrefabUtility.SaveAsPrefabAsset(_root, NextPath);
            Object.DestroyImmediate(_root);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            manifest.hideFlags = HideFlags.HideAndDontSave;
            TextAsset Read(string file) => new TextAsset(Json.Replace("delivery.fbx", file)) { hideFlags = HideFlags.HideAndDontSave };
            manifest.plan = Read("ManifestsActionsTests delivery.prefab");
            manifest.delivery = prefab;
            try
            {
                ManifestsActions.Build(manifest, new OpContext());
                _root = ManifestsActions.Placed(prefab);
                _root.transform.Find("B").gameObject.AddComponent<Light>();  // a person's, on a node of it
                Object.DestroyImmediate(manifest.plan);
                manifest.plan = Read("ManifestsActionsTests delivery v2.prefab");  // the task re-synced against the re-export

                Assert.That(ManifestsActions.Assess(manifest).planIssues, Has.Some.Contains("read against ManifestsActionsTests delivery v2"), "said before a build");
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, dry);
                Assert.That(manifest.delivery, Is.SameAs(prefab), "a dry run plans the move");
                Assert.That(dry.Changes.Select(c => c.action), Has.Some.StartWith("build into ManifestsActionsTests delivery v2"));

                var live = new OpContext();
                ManifestsActions.Build(manifest, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.action))),
                            "the plan is the run: the swap of what is placed is planned with the move");
                Assert.That(manifest.delivery, Is.SameAs(next), "it builds into the model its plan was read against");
                Assert.That(ManifestsActions.Placed(prefab), Is.Null, "no instance of the old one is left");
                _root = ManifestsActions.Placed(next);
                Assert.That(_root, Is.Not.Null, "the one placed moved over");
                Assert.That(_root.transform.Find("B").GetComponent<Light>(), Is.Not.Null, "what a person put on its nodes stays");
                Assert.That(Part("A"), Is.Not.Null, "and what the build did");
                var settled = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, settled);
                Assert.That(settled.Changes, Is.Empty, "a second build changes nothing");
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
                AssetDatabase.DeleteAsset(NextPath);
            }
        }

        [Test]
        public void A_change_made_in_the_scene_to_what_a_behavior_built_is_kept_as_the_objects_own_and_a_kit_settings_change_still_reaches_the_rest()
        {
            // The user: arrows (and every behavior's parts) adjusted after building keep what was adjusted, as before building.
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, PrefabPath);
            Object.DestroyImmediate(_root);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            var kit = ScriptableObject.CreateInstance<TestSettings>();
            manifest.plan = new TextAsset(Json);
            manifest.delivery = prefab;
            manifest.settings.Add(kit);
            try
            {
                ManifestsActions.Build(manifest, new OpContext());
                _root = ManifestsActions.Placed(prefab);
                Assert.That(manifest.built.Count, Is.EqualTo(2), "what the build gave each part, recorded");
                Part("B").size = new Vector3(3f, 3f, 3f);  // a person resizes B's box in the scene
                kit.box = new Vector3(2f, 2f, 2f);      // and the kit's setting changes too
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, dry);
                Assert.That(dry.Changes.Any(c => c.action.Contains("its own box")), "the plan says B keeps its own");
                var live = new OpContext();
                ManifestsActions.Build(manifest, live);
                Assert.That(dry.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(live.Changes.Select(c => (c.kind, c.target, c.action))),
                            "the plan is the run: B, kept as it was resized, is not sized back");
                Assert.That(Part("B").size, Is.EqualTo(new Vector3(3f, 3f, 3f)), "the person's change kept");
                Assert.That(manifest.TuneOf("S01", "B", "test_part", 1).Get("box"), Is.EqualTo("3,3,3"), "as B's own value for its row, an edit");
                Assert.That(Part("A").size, Is.EqualTo(new Vector3(2f, 2f, 2f)), "the kit's change reaches what no one changed");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, again);
                Assert.That(again.Changes.Where(c => c.target.StartsWith(OpContextEdits.Label(_root) + "/")), Is.Empty, "a second build changes no part");
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
                Object.DestroyImmediate(kit);
            }
        }

        static string Sha(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
        }

        // A fixed folder of a project's clause templates (one with CRLF line endings) and the digest the sheet's reader makes of
        // it: test_sheet_plan.py's ProjectClausesTestCase holds the same pair, so the two readers agree on one golden value.
        static readonly (string name, string text)[] FixedClauses =
        {
            ("go.json", "{\"word\": \"button\",\r\n \"detect\": [\"^go on$\"]}\r\n"),
            ("stop.json", "{\"word\": \"button\", \"detect\": [\"^stop$\"]}\n"),
        };
        const string FixedClausesDigest = "4973bc23b2555913d22f2c98ab3b3fb764f5bc3334f0b12a2a421df68049af90";

        [Test]
        public void A_manifest_reads_its_sheet_again_by_running_the_readers_command_and_says_what_came_of_it()
        {
            // Updating a project was: copy the command from the window, find a terminal, paste it, come back, Build.
            string folder = FileUtil.GetUniqueTempPathInProject();
            Directory.CreateDirectory(folder);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                // The runner: a program and its arguments, its exit code and what it printed.
                bool windows = Application.platform == RuntimePlatform.WindowsEditor;
                string shell = windows ? "cmd.exe" : "/bin/sh", flag = windows ? "/c" : "-c";
                SheetReader.Result echoed = SheetReader.Run(shell, new[] { flag, "echo staged" }, folder);
                Assert.That((echoed.ran, echoed.exitCode), Is.EqualTo((true, 0)));
                Assert.That(echoed.printed, Does.Contain("staged"));
                Assert.That(SheetReader.Run(shell, new[] { flag, "exit 3" }, folder).exitCode, Is.EqualTo(3));
                Assert.That(SheetReader.Run("no-such-reader-program", new string[0], folder).ran, Is.False, "no such program: it did not run");

                // A reader's outcome, as its operations print it.
                Core.Outcome outcome = SheetReader.Outcome(
                    "a line before\n{\"op\": \"sheet.sync\", \"status\": \"done\", \"result\": {\"written\": 1}, " +
                    "\"issues\": [{\"code\": \"sheet.silent\", \"message\": \"B02 has no sound\", \"severity\": \"warning\"}]}\n");
                Assert.That((outcome.status, outcome.error), Is.EqualTo(("done", (string)null)));
                Assert.That(outcome.issues.Select(i => i.message), Is.EqualTo(new[] { "B02 has no sound" }));
                Assert.That(SheetReader.Outcome("no reader answered"), Is.Null);

                // Read, and what it said of the sheet raised; a reader that failed is an error naming the command to run by hand.
                var said = new OpContext();
                Assert.That(ManifestsActions.Read(new SheetReader.Result { ran = true, exitCode = 0, printed = Printed("done", "B02 has no sound") },
                                                  "the command", said), Is.True);
                Assert.That(said.Issues.Select(i => (i.code, i.message, i.severity)), Is.EqualTo(new[] { (ManifestsActions.ReadCode, "B02 has no sound", "warning") }));
                var failed = new OpContext();
                Assert.That(ManifestsActions.Read(new SheetReader.Result { ran = true, exitCode = 1, printed = "'python' is not recognized" }, "the command", failed), Is.False);
                Assert.That(failed.Issues.Single().severity, Is.EqualTo("error"));
                Assert.That(failed.Issues.Single().message, Does.Contain("'python' is not recognized").And.Contain("the command"), "why, and what to run by hand");

                // A manifest that does not say where its sheet's settings are has nothing to read again.
                manifest.plan = new TextAsset(Json);
                var none = new OpContext();
                Assert.That(ManifestsActions.ReadAgain(manifest, none), Is.False);
                Assert.That(none.Issues.Single().code, Is.EqualTo(ManifestsActions.ReadCode));
                Assert.That(none.Issues.Single().message, Does.Contain("Setup"));

                // One that does: a dry run plans the read, and runs nothing.
                string settings = Path.Combine(folder, "test.json");
                File.WriteAllText(settings, "{\"version\": 1, \"sheet\": \"test.csv\"}");
                Object.DestroyImmediate(manifest.plan);
                manifest.plan = new TextAsset(Json.Replace("\"sheet\": \"test.csv\"", $"\"sheet\": \"test.csv\", \"settings\": \"{settings.Replace('\\', '/')}\""));
                var dry = new OpContext(dryRun: true);
                Assert.That(ManifestsActions.ReadAgain(manifest, dry), Is.False, "planned, not run");
                Assert.That(dry.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(new[] { ("write", "read its sheet again and stage it (sheet.sync)") }));
                Assert.That(dry.Issues, Is.Empty);
            }
            finally
            {
                if (manifest.plan != null) Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
                Directory.Delete(folder, true);
            }
        }

        // What a reader's operation prints: its outcome, with one issue rated severity.
        static string Printed(string status, string issue, string severity = "warning")
            => $"{{\"op\": \"sheet.sync\", \"status\": \"{status}\", \"issues\": [{{\"code\": \"sheet.test\", \"message\": \"{issue}\", " +
               $"\"severity\": \"{severity}\"}}]}}";

        [Test]
        public void A_readers_outcome_is_said_as_it_is_and_its_arguments_reach_it_as_written()
        {
            // A Python error printed "{" with no "}" after it: the slice was of a negative length, and the exception it threw
            // left the window with no notice and no assessment.
            Assert.That(SheetReader.Outcome("SyntaxError: '{' was never closed"), Is.Null, "a brace never closed: no outcome, nothing thrown");
            Assert.That(SheetReader.Outcome("} before {"), Is.Null);

            // Each issue keeps the severity the reader gave it; a run in part imports what it staged and says so; a
            // cancelled one imports nothing.
            var errors = new OpContext();
            Assert.That(ManifestsActions.Read(new SheetReader.Result { ran = true, printed = Printed("done", "B02 names no take", "error") }, "the command", errors),
                        Is.True);
            Assert.That(errors.Issues.Single().severity, Is.EqualTo("error"), "rated as the reader rated it");
            var part = new OpContext();
            Assert.That(ManifestsActions.Read(new SheetReader.Result { ran = true, printed = Printed("partial", "B03 was left out") }, "the command", part),
                        Is.True, "what it staged is imported");
            Assert.That(part.Issues.Select(i => i.message), Has.Some.Contains("staged in part"));
            var cancelled = new OpContext();
            Assert.That(ManifestsActions.Read(new SheetReader.Result { ran = true, printed = Printed("cancelled", "stopped at B04") }, "the command", cancelled),
                        Is.False, "a cancelled read imports nothing");
            Assert.That(cancelled.Issues.Select(i => i.message), Has.Some.Contains("cancelled"));

            // No shell reads the arguments: a path with what a shell expands, a space or a quote reaches the reader as written.
            Assert.That(SheetReader.CommandLine(new[] { "-m", "settings=C:/100%/$HOME/`x`/it's.json" }),
                        Is.EqualTo("-m \"settings=C:/100%/$HOME/`x`/it's.json\""));
            Assert.That(SheetReader.CommandLine(new[] { "a b", "say \"hi\"", @"C:\dir\", "" }),
                        Is.EqualTo("\"a b\" \"say \\\"hi\\\"\" \"C:\\dir\\\\\" \"\""));
        }

        [Test]
        public void A_reader_that_is_a_batch_file_on_the_path_is_found_and_handed_its_arguments_as_written()
        {
            // pyenv-win's python is a shim, python.bat: a run with no shell looked for python.exe alone, so the window said no
            // Python answered where a terminal ran it.
            var files = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                @"C:\tools\python.exe", @"C:\shims\python.bat", @"C:\shims\reader.cmd", @"C:\shims\script.vbs",
            };
            const string endings = ".COM;.EXE;.BAT;.CMD;.VBS;.JS";
            Assert.That(SheetReader.Resolve("python", @"C:\shims;C:\tools", endings, files.Contains), Is.EqualTo(@"C:\shims\python.BAT"),
                        "the first folder on the path that has it, by PATHEXT's endings");
            Assert.That(SheetReader.Resolve("python", @"""C:\tools\"";C:\shims", endings, files.Contains), Is.EqualTo(@"C:\tools\python.EXE"),
                        "a folder quoted, or ending in a backslash");
            Assert.That(SheetReader.Resolve("python.exe", @"C:\shims;C:\tools", endings, files.Contains), Is.EqualTo(@"C:\tools\python.exe"),
                        "a name with its ending, as named");
            Assert.That(SheetReader.Resolve("reader", @";C:\shims", ".EXE;.CMD", files.Contains), Is.EqualTo(@"C:\shims\reader.CMD"));
            Assert.That(SheetReader.Resolve("script", @"C:\shims", endings, files.Contains), Is.EqualTo("script"), "nothing a shell's file association runs");
            Assert.That(SheetReader.Resolve(@"C:\elsewhere\python", @"C:\shims", endings, files.Contains), Is.EqualTo(@"C:\elsewhere\python"), "a path is not searched for");
            Assert.That(SheetReader.Resolve("missing", @"C:\shims", endings, files.Contains), Is.EqualTo("missing"), "none: as named, and the run says so");
            Assert.That((SheetReader.IsBatch(@"C:\shims\python.BAT"), SheetReader.IsBatch(@"C:\shims\reader.cmd"), SheetReader.IsBatch(@"C:\tools\python.exe")),
                        Is.EqualTo((true, true, false)));

            // cmd runs a batch file: every character it reads otherwise (& ^ % ( ) and the rest) quoted, a % past its expansion.
            Assert.That(SheetReader.BatchLine(@"C:\shims\python.bat", new[] { "-m", "pythontk", "settings=C:/100%/a&b^c (x)/it's.json" }),
                        Is.EqualTo("/d /e:on /v:off /s /c \"\"C:\\shims\\python.bat\" -m pythontk \"settings=C:/100%%cd:~,%/a&b^c (x)/it's.json\"\""));
            Assert.That(SheetReader.BatchLine(@"C:\shims\python.bat", new[] { "say \"hi\"", @"C:\dir\", "" }),
                        Is.EqualTo("/d /e:on /v:off /s /c \"\"C:\\shims\\python.bat\" \"say \"\"hi\"\"\" C:\\dir\\ \"\"\""));
            Assert.Throws<System.ArgumentException>(() => SheetReader.BatchLine(@"C:\shims\python.bat", new[] { "a\nb" }), "a line break, which no batch file is handed");

            if (Application.platform != RuntimePlatform.WindowsEditor) return;
            // A batch file run: what it is handed is what was written.
            string folder = FileUtil.GetUniqueTempPathInProject();
            Directory.CreateDirectory(folder);
            try
            {
                string shim = Path.GetFullPath(Path.Combine(folder, "shim.bat"));
                File.WriteAllText(shim, "@echo off\r\necho %*\r\n");
                SheetReader.Result echoed = SheetReader.Run(shim, new[] { "-m", "a&b", "100%PATH%" }, folder);
                Assert.That((echoed.ran, echoed.exitCode), Is.EqualTo((true, 0)), echoed.printed);
                Assert.That(echoed.printed.Trim(), Is.EqualTo("-m \"a&b\" \"100%PATH%\""), "neither split at its & nor expanded");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void A_manifest_says_when_what_its_plan_was_read_from_changed()
        {
            string folder = FileUtil.GetUniqueTempPathInProject();  // a path from the project's folder, as a staged plan carries
            Directory.CreateDirectory(folder);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                const string sheetText = "Step,Step Contents\nS01,Part A\n", settingsText = "{\"version\": 1, \"sheet\": \"test.csv\"}";
                string sheet = Path.Combine(folder, "test.csv"), settings = Path.Combine(folder, "test.json");
                File.WriteAllText(sheet, sheetText.Replace("\n", "\r\n"));  // another checkout's line endings
                File.WriteAllText(settings, settingsText);
                string stamped = Json.Replace("\"sheet\": \"test.csv\"",
                    $"\"sheet\": \"test.csv\", \"sheet_digest\": \"{Sha(sheetText)}\", " +
                    $"\"settings\": \"{settings.Replace('\\', '/')}\", \"settings_digest\": \"{Sha(settingsText)}\"");
                manifest.plan = new TextAsset(stamped);

                Assert.That(ManifestsActions.Stale(manifest, out string why), Is.False, why);
                Assert.That(ManifestsActions.SheetFile(manifest), Is.EqualTo(Path.GetFullPath(settings)), "the path its plan was staged with");
                Assert.That(ManifestsActions.SyncCommand(manifest),
                            Does.StartWith("python -m pythontk ops apply sheet.sync \"settings=").And.Contain("/test.json\" \"project="));

                File.WriteAllText(sheet, sheetText.Replace("Part A", "Part B"));
                Assert.That(ManifestsActions.Stale(manifest, out why), Is.True);
                Assert.That(why, Is.EqualTo("the sheet (test.csv) changed since the plan was read"));
                File.WriteAllText(sheet, sheetText);
                using (new FileStream(sheet, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))  // open in its editor
                    Assert.That(ManifestsActions.Stale(manifest, out why), Is.False, "read though another program has it open");
                using (new FileStream(sheet, FileMode.Open, FileAccess.ReadWrite, FileShare.None))  // held outright
                {
                    Assert.That(ManifestsActions.Stale(manifest, out why), Is.False, "a file it cannot read: nothing thrown");
                    Assert.That(why, Does.StartWith("what the plan was read from cannot be read now"), "and said: it is unchecked");
                }
                Assert.That(ManifestsActions.Stale(manifest, out why) || why.Length > 0, Is.False, "let go: all told, nothing changed");
                File.WriteAllText(settings, settingsText.Replace("1,", "1, \"mapping\": \"linear\","));
                Assert.That(ManifestsActions.Stale(manifest, out why) && why.Contains("sheet's settings (test.json)"), why);

                Object.DestroyImmediate(manifest.plan);
                manifest.plan = new TextAsset(Json);
                Assert.That(ManifestsActions.Stale(manifest, out _), Is.False, "a plan that does not say where it was read from: nothing to tell");
                Assert.That(ManifestsActions.SyncCommand(manifest), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void A_settings_file_this_editor_cannot_read_leaves_the_rest_unchecked_and_says_so()
        {
            string folder = FileUtil.GetUniqueTempPathInProject();
            Directory.CreateDirectory(folder);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                string settings = Path.Combine(folder, "test.json");
                foreach (var (text, said) in new[]
                         {
                             ("{\"version\": 1, \"sheet\": ", "cannot be read here ("),  // not JSON
                             ("{\"version\": 99, \"sheet\": \"test.csv\"}", "version 99; this editor reads"),
                         })
                {
                    File.WriteAllText(settings, text);  // as the plan was read from it: its digest the plan's
                    Object.DestroyImmediate(manifest.plan);
                    manifest.plan = new TextAsset(Json.Replace("\"sheet\": \"test.csv\"",
                        $"\"sheet\": \"test.csv\", \"settings\": \"{settings.Replace('\\', '/')}\", \"settings_digest\": \"{Sha(text)}\""));
                    Assert.That(ManifestsActions.Stale(manifest, out string why), Is.False, "nothing it can tell changed");
                    Assert.That(why, Does.Contain(said).And.Contain("are unchecked"), "never read as all told");
                }
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void A_delivery_file_newer_than_its_staged_copy_is_a_change_only_where_its_bytes_differ()
        {
            string folder = FileUtil.GetUniqueTempPathInProject();
            Directory.CreateDirectory(folder);
            const string name = "ManifestsActionsTests delivery.bin";
            string source = Path.Combine(folder, name), copy = "Assets/" + name;  // the copy staged beside the plan
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                string settingsText = $"{{\"version\": 1, \"sheet\": \"test.csv\", \"deliveries\": [\"{name}\"]}}";
                string settings = Path.Combine(folder, "test.json");
                File.WriteAllText(settings, settingsText);
                File.WriteAllText(PlanPath, Json.Replace("\"sheet\": \"test.csv\"",
                    $"\"sheet\": \"test.csv\", \"settings\": \"{settings.Replace('\\', '/')}\", \"settings_digest\": \"{Sha(settingsText)}\""));
                AssetDatabase.ImportAsset(PlanPath);
                manifest.plan = AssetDatabase.LoadAssetAtPath<TextAsset>(PlanPath);
                bool Stale(byte[] now, out string why)
                {
                    File.WriteAllBytes(copy, new byte[] { 1, 2, 3, 4 });
                    File.WriteAllBytes(source, now);
                    File.SetLastWriteTimeUtc(copy, System.DateTime.UtcNow.AddHours(-1));  // the delivery written since it was staged
                    return ManifestsActions.Stale(manifest, out why);
                }

                Assert.That(Stale(new byte[] { 1, 2, 3, 4 }, out string same), Is.False, "written again, the same bytes: " + same);
                Assert.That(Stale(new byte[] { 1, 2, 3, 4, 5 }, out string longer), Is.True, "another length");
                Assert.That(longer, Is.EqualTo($"the delivery's file {name} changed since it was staged"));
                Assert.That(Stale(new byte[] { 1, 2, 3, 9 }, out _), Is.True, "the same length, other bytes");
                File.SetLastWriteTimeUtc(copy, System.DateTime.UtcNow.AddHours(1));
                Assert.That(ManifestsActions.Stale(manifest, out _), Is.False, "staged since: its copy is the file");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                AssetDatabase.DeleteAsset(copy);
                if (File.Exists(copy)) File.Delete(copy);
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void An_edited_clause_template_of_the_project_makes_its_plan_stale()
        {
            string folder = FileUtil.GetUniqueTempPathInProject();
            Directory.CreateDirectory(Path.Combine(folder, "clauses"));
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                const string settingsText = "{\"version\": 1, \"sheet\": \"test.csv\"}";
                string settings = Path.Combine(folder, "test.json"), clause = Path.Combine(folder, "clauses", "go.json");
                File.WriteAllText(settings, settingsText);
                foreach (var (name, text) in FixedClauses) File.WriteAllText(Path.Combine(folder, "clauses", name), text);
                manifest.plan = new TextAsset(Json.Replace("\"sheet\": \"test.csv\"",
                    $"\"sheet\": \"test.csv\", \"settings\": \"{settings.Replace('\\', '/')}\", " +
                    $"\"settings_digest\": \"{Sha(settingsText)}\", \"clauses\": \"clauses\", \"clauses_digest\": \"{FixedClausesDigest}\""));

                Assert.That(ManifestsActions.Stale(manifest, out string why), Is.False, "the reader's golden digest of the fixed folder: " + why);
                File.WriteAllText(clause, FixedClauses[0].text.Replace("go on", "carry on"));
                Assert.That(ManifestsActions.Stale(manifest, out why), Is.True);
                Assert.That(why, Is.EqualTo("the sheet's clause templates (clauses) changed since the plan was read"));
                File.Delete(clause);
                Assert.That(ManifestsActions.Stale(manifest, out _), Is.True, "a template gone is a change too");
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void A_plan_read_against_several_files_names_its_delivery_first_then_its_extras()
        {
            Assert.That(ManifestsActions.DeliveryFiles("assembly.fbx, arrows.fbx, anchors.fbx"),
                        Is.EqualTo(new[] { "assembly.fbx", "arrows.fbx", "anchors.fbx" }));
            Assert.That(ManifestsActions.DeliveryFiles("delivery.fbx"), Is.EqualTo(new[] { "delivery.fbx" }));
            Assert.That(ManifestsActions.DeliveryFiles(null), Is.Empty);
        }

        [Test]
        public void A_manifest_is_made_from_its_plan_and_keeps_each_kits_settings()
        {
            File.WriteAllText(PlanPath, Json);
            AssetDatabase.ImportAsset(PlanPath);
            var ctx = new OpContext();
            Manifest made = ManifestsActions.Create(AssetDatabase.LoadAssetAtPath<TextAsset>(PlanPath), ctx);

            Assert.That(AssetDatabase.GetAssetPath(made), Is.EqualTo("Assets/ManifestsActionsTests.manifest.asset"), "beside its plan");
            Assert.That(made.scene, Is.EqualTo("Assets/ManifestsActionsTests.unity"));
            Assert.That(made.delivery == null && ctx.Issues.Single().code == ManifestsActions.ManifestCode, "no model of the plan's delivery: said, not guessed");
            Assert.That(ManifestsActions.Create(AssetDatabase.LoadAssetAtPath<TextAsset>(PlanPath)), Is.SameAs(made), "the one there is the plan's manifest");
            Assert.That(ManifestsActions.Plan(made, out _).steps.Count, Is.EqualTo(2));

            Assert.That(made.Settings<TestSettings>(), Is.Null, "none kept: the kit's defaults");
            var scene = new ManifestScene(_root, new AnimationClip[0]) { Manifest = made };
            Assert.That(scene.Settings<TestSettings>().size, Is.EqualTo(1f), "the defaults");
            var own = (TestSettings)ManifestsActions.AddSettings(made, typeof(TestSettings), new OpContext());
            own.size = 3f;
            Assert.That(ManifestsActions.AddSettings(made, typeof(TestSettings)), Is.SameAs(own), "one of a type");
            Assert.That(AssetDatabase.GetAssetPath(own), Is.EqualTo(AssetDatabase.GetAssetPath(made)), "inside the manifest's own file");
            Assert.That(new ManifestScene(_root, new AnimationClip[0]) { Manifest = made }.Settings<TestSettings>().size, Is.EqualTo(3f));
            Assert.That(ManifestBehaviors.Kits(_plan).Select(k => k.GetType()), Is.EqualTo(new[] { typeof(TestKit) }),
                        "the kit whose rows the plan holds, and no other");
        }
        [Test]
        public void Keeping_a_kits_settings_in_the_manifest_is_undone_with_what_it_made()
        {
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                ScriptableObject made = null;
                OpContext.RunMenu("Keep Test Settings", ctx => made = ManifestsActions.AddSettings(manifest, typeof(TestSettings), ctx));
                Assert.That(manifest.settings, Is.EqualTo(new[] { made }));
                Undo.PerformUndo();
                Assert.That(manifest.settings, Is.Empty, "the manifest keeps it no more");
                Assert.That(made == null, "and what it made is gone with it");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void What_a_step_is_built_from_names_every_field_and_setting_of_its_rows()
        {
            // A kit rebuilds a step whose source changed: a row's jump, words or settings changed must change it.
            string Source(string json)
            {
                var scene = new ManifestScene(_root, new AnimationClip[0]);
                ManifestsActions.Assess(SheetPlan.Read(json, "test", out _), scene);
                return scene.Source("S01");
            }
            string row(string extra) =>
                "{\"version\": 3, \"steps\": [{\"id\": \"S01\", \"actions\": [{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"]" +
                extra + "}]}]}";
            string plain = Source(row(""));
            Assert.That(Source(row(", \"to\": \"S02\"")), Is.Not.EqualTo(plain), "the step it goes to");
            Assert.That(Source(row(", \"to\": \"S03\"")), Is.Not.EqualTo(Source(row(", \"to\": \"S02\""))), "another step");
            Assert.That(Source(row(", \"fields\": {\"label\": \"YES\"}")), Is.Not.EqualTo(Source(row(", \"fields\": {\"label\": \"NO\"}"))), "its words");
            Assert.That(Source(row(", \"settings\": {\"size\": 2}")), Is.Not.EqualTo(plain), "a setting its words give it");
            Assert.That(Source(row("")), Is.EqualTo(plain), "the same rows: the same source");
        }

        [Test]
        public void An_assessment_finds_only_the_fields_a_behavior_says_name_nodes_or_takes()
        {
            const string rows =
                "{\"version\": 3, \"steps\": [{\"id\": \"S01\", \"actions\": [" +
                "{\"behavior\": \"test_part\", \"text\": \"Its with is a word\", \"targets\": [\"A\"], \"fields\": {\"with\": \"no node\"}}," +
                "{\"behavior\": \"test_part\", \"text\": \"Its anchor is a node\", \"targets\": [\"A\"], \"fields\": {\"anchor\": \"NOPE\"}}," +
                "{\"behavior\": \"test_part\", \"text\": \"It plays a take\", \"targets\": [\"A\"], \"clip\": \"Z99\"}]}]}";
            ManifestAssessment assessed = ManifestsActions.Assess(SheetPlan.Read(rows, "test", out _), _scene);
            Assert.That(assessed.rows[0].Status, Is.EqualTo(ManifestStatus.Missing), "a field its behavior does not declare a node is never looked up");
            Assert.That(assessed.rows[1].Status, Is.EqualTo(ManifestStatus.MissingObject), "its NodeFields are");
            Assert.That(assessed.rows[1].problems.Single(), Does.Contain("anchor 'NOPE'"));
            Assert.That(assessed.rows[2].Status, Is.EqualTo(ManifestStatus.MissingTake), "the contract's take, by default");
        }

        [Test]
        public void A_setting_no_field_of_its_kit_holds_is_said_and_never_shares_a_name_with_a_rows_field()
        {
            const string rows =
                "{\"version\": 3, \"steps\": [{\"id\": \"S01\", \"actions\": [" +
                "{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"], \"settings\": {\"sise\": 2}}]}]}";
            var build = new OpContext();
            ManifestAssessment built = ManifestsActions.Build(SheetPlan.Read(rows, "test", out _), _scene, build);
            Assert.That(built.rows[0].notes.Single(), Does.Contain("sise is no setting of TestSettings"), "said on its row, not dropped");
            Assert.That(build.Issues.Single().code, Is.EqualTo(ManifestsActions.NoteCode));
            Assert.That((built.rows[0].Status, Part("A").size), Is.EqualTo((ManifestStatus.Warned, Vector3.one)), "built anyway, by what its kit holds");

            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                var ctx = new OpContext();
                Assert.That(ManifestsActions.SetTune(manifest, "S01", "A", "test_part", "value", "trigger", ctx), Is.False, "a row's field is no setting");
                Assert.That(ctx.Issues.Single().code, Is.EqualTo(ManifestsActions.SettingCode));
                Assert.That(ctx.Issues.Single().message, Does.Contain("SetRowField"), "and the refusal says where it goes");
                Assert.That(ManifestsActions.SetRowField(manifest, "S01", "A", "test_part", "size", "2", ctx), Is.False, "a setting is no row's field");
                Assert.That(manifest.tunes, Is.Empty);

                Assert.That(ManifestsActions.SetTune(manifest, "S01", "A", "test_part", "size", "2"), Is.True);
                Assert.That(ManifestsActions.SetRowField(manifest, "S01", "A", "test_part", "value", "trigger"), Is.True);
                Manifest.Tune tune = manifest.TuneOf("S01", "A", "test_part");
                Assert.That((tune.Get("size"), tune.Field("value"), tune.values.Count, tune.fields.Count), Is.EqualTo(("2", "trigger", 1, 1)), "kept apart");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void One_objects_settings_start_from_what_its_row_of_the_sheet_says()
        {
            string json = Json.Replace("{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"]}",
                                       "{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"], \"settings\": {\"size\": 4}}");
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            var text = new TextAsset(json);
            try
            {
                manifest.plan = text;
                var own = (TestSettings)ManifestsActions.OwnSettings(manifest, typeof(TestSettings), "S01", "A", 0);
                Assert.That(own.size, Is.EqualTo(4f), "the row's own words over its kit's");
                Assert.That(ManifestsActions.SetTunes(manifest, "S01", "A", "test_part", own, new[] { "size" }, row: 0), Is.Zero, "as the row says: nothing of its own");
                own.size = 5f;
                Assert.That(ManifestsActions.SetTunes(manifest, "S01", "A", "test_part", own, new[] { "size" }, row: 0), Is.EqualTo(1));
                Assert.That(manifest.TuneOf("S01", "A", "test_part", 0).Get("size"), Is.EqualTo("5"));
                Object.DestroyImmediate(own);
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                Object.DestroyImmediate(text);
            }
        }

        [Test]
        public void An_assessment_leaves_no_settings_behind()
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, PrefabPath);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            manifest.plan = new TextAsset(Json);
            manifest.delivery = prefab;
            try
            {
                ManifestsActions.Build(manifest, new OpContext());
                int before = Resources.FindObjectsOfTypeAll<TestSettings>().Length;
                for (int i = 0; i < 3; i++) ManifestsActions.Assess(manifest, new OpContext());
                Assert.That(Resources.FindObjectsOfTypeAll<TestSettings>().Length, Is.EqualTo(before), "the kit's defaults made for a run go with it");
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_manifests_build_from_nothing_placed_is_planned_undone_and_converges()
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, PrefabPath);
            Object.DestroyImmediate(_root);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            var kit = ScriptableObject.CreateInstance<TestSettings>();
            manifest.plan = new TextAsset(Json);
            manifest.delivery = prefab;
            manifest.settings.Add(kit);
            try
            {
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, dry);
                Assert.That(ManifestsActions.Placed(prefab), Is.Null, "a dry run places nothing");
                Assert.That(dry.Changes.Select(c => (c.kind, c.target)), Does.Contain(("create", $"{Path.GetFileNameWithoutExtension(PrefabPath)}/A/BoxCollider")),
                            "it plans the rows too, not the placement alone");

                OpContext live = null;
                OpContext.RunMenu("Build Manifest", ctx =>
                {
                    live = ctx;
                    ManifestsActions.Build(manifest, ctx);
                });
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))),
                            "the plan names every change the run makes, the build's record too");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, again);
                Assert.That(again.Changes, Is.Empty, "a second build plans nothing");

                kit.box = new Vector3(2f, 2f, 2f);  // the kit's setting changes, and a build carries it
                OpContext.RunMenu("Build Manifest", ctx => ManifestsActions.Build(manifest, ctx));
                _root = ManifestsActions.Placed(prefab);
                Assert.That(Part("A").size, Is.EqualTo(new Vector3(2f, 2f, 2f)));
                Undo.PerformUndo();
                Assert.That(Part("A").size, Is.EqualTo(Vector3.one), "one undo takes that build back");
                Assert.That(manifest.built.Single(v => v.name.Contains("|A|")).value, Is.EqualTo("1,1,1"), "its record of what it built too");
                ManifestsActions.Build(manifest, new OpContext());
                Assert.That(manifest.tunes, Is.Empty, "so the undone part is no person's change to keep");
                Assert.That(Part("A").size, Is.EqualTo(new Vector3(2f, 2f, 2f)), "and the kit's setting still reaches it");
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
                Object.DestroyImmediate(kit);
            }
        }

        [Test]
        public void A_manifest_asset_handed_to_a_reset_is_still_its_callers_after_it()
        {
            // A script's own reference kept nothing loaded: the new scene a reset opens unloaded the manifest asset
            // under it, and the caller's next call on it read "the manifest names no plan".
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, PrefabPath);
            Object.DestroyImmediate(_root);
            File.WriteAllText(PlanPath, Json);
            AssetDatabase.ImportAsset(PlanPath);
            const string ManifestPath = "Assets/ManifestsActionsTests.manifest.asset";
            var made = ScriptableObject.CreateInstance<Manifest>();
            made.plan = AssetDatabase.LoadAssetAtPath<TextAsset>(PlanPath);
            made.delivery = prefab;
            made.scene = "Assets/ManifestsActionsTests.unity";
            AssetDatabase.CreateAsset(made, ManifestPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(made);
            Manifest manifest = AssetDatabase.LoadAssetAtPath<Manifest>(ManifestPath);

            Assert.That(ManifestsActions.Reset(manifest, discard: true, new OpContext()), Is.Not.Null);
            Assert.That(manifest != null, "the caller's manifest is still loaded");
            var again = new OpContext(dryRun: true);
            Assert.That(ManifestsActions.Build(manifest, again), Is.Not.Null, "and builds again by the same reference");
            Assert.That(again.Issues.Select(i => i.message), Has.None.Contains("the manifest"), "its plan is read, as before the reset");

            // One that is gone says so, never that it names no plan.
            Object.DestroyImmediate(manifest, true);
            var gone = new OpContext();
            Assert.That(ManifestsActions.Build(manifest, gone), Is.Null);
            Assert.That(gone.Issues.Single().message, Does.Contain("is no longer loaded"));
        }

        [Test]
        public void A_reset_asks_before_saving_and_creating_from_a_plan_says_why_it_cannot()
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, PrefabPath);
            Object.DestroyImmediate(_root);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            manifest.hideFlags = HideFlags.HideAndDontSave;  // kept across the new scene a reset opens, as an asset is
            manifest.plan = new TextAsset(Json) { hideFlags = HideFlags.HideAndDontSave };
            manifest.delivery = prefab;
            manifest.scene = "Assets/ManifestsActionsTests.unity";
            try
            {
                var ctx = new OpContext();
                Assert.That(ManifestsActions.Reset(manifest, discard: true, ctx), Is.Not.Null);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target)), Does.Contain(("write", manifest.scene)), "its save asked");
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target)), Does.Contain(("create", $"{Path.GetFileNameWithoutExtension(PrefabPath)}/A/BoxCollider")),
                            "its build too");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(manifest, again);
                Assert.That(again.Changes.Select(c => (c.kind, c.target, c.action)), Is.Empty,
                            "a build in its own scene that changes nothing plans nothing, its save neither");
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
            }

            File.WriteAllText(PlanPath, "{\"version\": 1, \"steps\": []}");
            AssetDatabase.ImportAsset(PlanPath);
            var refused = new OpContext();
            Assert.That(ManifestsActions.Create(AssetDatabase.LoadAssetAtPath<TextAsset>(PlanPath), refused), Is.Null);
            Assert.That(refused.Issues.Single().message, Does.Contain("version 1 plan"), "the plan's own refusal, not a guess");
        }

        [Test]
        public void A_sheet_picked_as_a_plan_is_refused_saying_how_to_stage_it()
        {
            const string SheetPath = "Assets/ManifestsActionsTests sheet.csv";
            File.WriteAllText(SheetPath, "Step,Text\nA01,Open the door\n");
            AssetDatabase.ImportAsset(SheetPath);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                var sheet = AssetDatabase.LoadAssetAtPath<TextAsset>(SheetPath);
                manifest.plan = sheet;
                Assert.That(ManifestsActions.Plan(manifest, out string refusal), Is.Null);
                Assert.That(refusal, Does.Contain("is a sheet, not its plan").And.Contain("sheet.sync"), "what it is and how to stage it");
                var refused = new OpContext();
                Assert.That(ManifestsActions.Create(sheet, refused), Is.Null, "New... refuses it alike");
                Assert.That(refused.Issues.Single().message, Is.EqualTo(refusal));
            }
            finally
            {
                Object.DestroyImmediate(manifest);
                AssetDatabase.DeleteAsset(SheetPath);
            }
        }
    }
}
