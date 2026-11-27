using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.Manifests.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// The Manifest window over the feature's stand-in behavior and kit: it shows a line a step and a
    /// line each of its objects, each with its behaviors and its status in words; its Build button
    /// counts what a build would change; Build, a scoped build, a lock, a behavior turned off and one
    /// given to an object go through the feature's actions; the Issues view hides only what has
    /// nothing to say; and a line lists the parts it holds.
    /// </summary>
    public class ManifestWindowTests
    {
        const string PrefabPath = "Assets/ManifestWindowTests delivery.prefab";

        const string Json =
            "{\"version\": 2, \"source\": {\"sheet\": \"test.csv\", \"delivery\": \"delivery.fbx\"}, \"steps\": [" +
            "{\"id\": \"S01\", \"name\": \"One\", \"section\": \"A\", \"section_title\": \"Access\", \"actions\": [" +
            "{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"]}]}," +
            "{\"id\": \"S02\", \"name\": \"Two\", \"actions\": [" +
            "{\"behavior\": \"test_part\", \"text\": \"Part B\", \"targets\": [\"GROUP|B\"]}," +
            "{\"behavior\": \"nothing_builds_this\", \"text\": \"Unknown\"}]}]}";

        Manifest _manifest;
        ManifestWindow _window;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("delivery");
            new GameObject("A").transform.SetParent(root.transform);
            var group = new GameObject("GROUP");
            group.transform.SetParent(root.transform);
            new GameObject("B").transform.SetParent(group.transform);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            _manifest = ScriptableObject.CreateInstance<Manifest>();
            _manifest.hideFlags = HideFlags.HideAndDontSave;
            _manifest.plan = new TextAsset(Json) { hideFlags = HideFlags.HideAndDontSave };
            _manifest.delivery = prefab;
            _window = ManifestWindow.Open();
            _window.IssuesOnly = false;
            _window.Manifest = _manifest;
        }

        [TearDown]
        public void TearDown()
        {
            _window.IssuesOnly = false;
            _window.Manifest = null;
            _window.Close();
            Object.DestroyImmediate(_manifest.plan);
            Object.DestroyImmediate(_manifest);
            AssetDatabase.DeleteAsset(PrefabPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void It_shows_each_step_and_row_with_its_status_and_builds_what_is_to_build()
        {
            Assert.That(_window.Assessed, Is.Not.Null, "opened on a manifest, it assesses");
            Assert.That(_window.Lines.Select(l => (l.step, l.section, l.description, l.acts)), Is.EqualTo(new[]
            {
                ("S01", "A: Access", "One", "1 object"), ("S02", "", "Two", "1 object"),
            }));
            ManifestLines.Line part = _window.Lines[0].rows[0];
            Assert.That((part.description, part.behaviors, part.target, part.label), Is.EqualTo(("A", "Test Part", "A", "not built")));
            Assert.That((_window.Lines[1].rows[0].description, _window.Lines[1].rows[0].target), Is.EqualTo(("B", "GROUP|B")), "a node by its own name; its path kept");
            Assert.That(_window.Lines[1].tokens.Single().status, Is.EqualTo(ManifestStatus.UnknownBehavior), "a row naming no object stands on its step's line");
            Assert.That(_window.Lines[1].label, Is.EqualTo("unknown behavior"), "a step says its worst row");
            Assert.That(_window.Lines[1].tooltip, Does.Contain("▲ Unknown: unknown behavior"), "its tooltip lists every issue, each led by its glyph");

            _window.Build();
            Assert.That(ManifestsActions.Placed(_manifest.delivery).transform.Find("A").GetComponent<BoxCollider>(), Is.Not.Null, "built through the feature's action");
            Assert.That(_window.Lines[0].rows[0].label, Is.EqualTo(""), "built: nothing to say");
            Assert.That(_window.Lines[0].Quiet && !_window.Lines[1].Quiet);
            Assert.That(_window.Parts(_window.Lines[0].rows[0]).Single(), Is.InstanceOf<BoxCollider>(), "a row lists the parts it holds");
            Assert.That(ManifestWindow.PartName(_window.Parts(_window.Lines[0].rows[0]).Single()), Is.EqualTo("Box Collider  (A)"));
            Assert.That(_window.Parts(_window.Lines[0]).Single().name, Is.EqualTo("S01"), "a step lists its own");

            _window.IssuesOnly = true;
            Assert.That(_window.Lines.Select(l => l.step), Is.EqualTo(new[] { "S02" }), "the Issues view hides what has nothing to say");
            Assert.That(_window.Table(), Does.StartWith("Step\tStatus").And.Contain("\tunknown behavior\t"));
        }

        [Test]
        public void A_scoped_build_and_a_lock_go_through_the_features_actions()
        {
            _window.Build(new[] { "S02" });
            Transform delivery = ManifestsActions.Placed(_manifest.delivery).transform;
            Assert.That(delivery.Find("A").GetComponent<BoxCollider>(), Is.Null, "S01 was not asked for");
            Assert.That(delivery.Find("GROUP/B").GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(_window.Lines[0].rows[0].label, Is.EqualTo("not built"));

            _window.SetLocked(new[] { "S01" }, true);
            Assert.That(_window.Lines[0].label, Is.EqualTo("locked"));
            _window.Build();
            Assert.That(delivery.Find("A").GetComponent<BoxCollider>(), Is.Null, "a build leaves a locked step as it is");
            _window.SetLocked(new[] { "S01" }, false);
            _window.Build();
            Assert.That(delivery.Find("A").GetComponent<BoxCollider>(), Is.Not.Null);
        }

        [Test]
        public void A_manifest_kept_as_an_asset_is_still_shown_after_a_build_in_a_new_scene()
        {
            const string planPath = "Assets/ManifestWindowTests.plan.json";
            System.IO.File.WriteAllText(planPath, Json);
            AssetDatabase.ImportAsset(planPath);
            try
            {
                Manifest asset = ManifestsActions.Create(AssetDatabase.LoadAssetAtPath<TextAsset>(planPath));
                asset.delivery = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                asset.scene = "";  // the new scene stays unsaved
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                _window.Manifest = asset;

                _window.Reset(discard: true);  // a new scene unloads an asset nothing in it holds
                Assert.That(_window.Manifest != null, "the window still holds its manifest");
                Assert.That(_window.Assessed, Is.Not.Null);
                Assert.That(_window.Lines[0].rows[0].label, Is.EqualTo(""), "built in the new scene, and shown so");
            }
            finally
            {
                _window.Manifest = _manifest;
                AssetDatabase.DeleteAsset("Assets/ManifestWindowTests.manifest.asset");
                AssetDatabase.DeleteAsset(planPath);
            }
        }

        [Test]
        public void A_behavior_turned_off_and_one_given_to_an_object_are_the_manifests_and_a_build_goes_by_them()
        {
            _window.Assign(_window.Lines[0].rows[0], "test_part", false);  // S01 leaves Part A out
            Assert.That(_manifest.edits.Select(e => (e.step, e.target, e.behavior, e.add)), Is.EqualTo(new[] { ("S01", "A", "test_part", false) }));
            ManifestLines.Token left = _window.Lines[0].rows[0].tokens.Single();
            Assert.That(left.off && !left.offEverywhere, "shown on its object's line, off");
            _window.Build();
            Transform delivery = ManifestsActions.Placed(_manifest.delivery).transform;
            Assert.That(delivery.Find("A").GetComponent<BoxCollider>(), Is.Null, "a build leaves it out");
            Assert.That(delivery.Find("GROUP/B").GetComponent<BoxCollider>(), Is.Not.Null);

            _window.Assign(_window.Lines[0].rows[0], "test_part", true);  // back as the sheet says: the edit goes
            Assert.That(_manifest.edits, Is.Empty);
            ManifestsActions.SetAssigned(_manifest, "S02", "A", "test_part", true);  // S02 gets a part the sheet does not give it
            _window.Assess();
            ManifestLines.Line added = _window.Lines[1].rows.Single(r => r.target == "A");
            Assert.That(added.tokens.Single().added, "what this project added stands on the object's line");
            Assert.That(ManifestsActions.Assignable(ManifestsActions.SceneOf(_manifest), "A"), Does.Contain("test_part"), "a behavior its object alone can build is offered");

            _window.SetOff("test_part", true);
            Assert.That(_window.IsOff("test_part") && _manifest.off.Contains("test_part"));
            Assert.That(_window.Lines.SelectMany(l => l.rows).SelectMany(r => r.tokens).All(k => k.off && k.offEverywhere), "every row of it, off");
            Assert.That(_window.Assessed.Plan().Any, Is.False, "nothing of it is left to build");
            _window.SetOff("test_part", false);
            Assert.That(_window.Lines[0].rows[0].tokens.Single().off, Is.False);
        }

        [Test]
        public void A_states_mark_is_the_Shot_Manifests_drawn_icon()
        {
            // The user, 2026-11-24: "copy/use the same for the unity manifest" -- the Shot Manifest's icons, not glyphs.
            foreach (string level in ManifestStatus.Levels)
            {
                string icon = ManifestWindow.IconOf(level);
                Assert.That(icon, Is.Not.Null, level);
                Assert.That(VectorIcons.Has(icon), $"{level}: its icon '{icon}' is in the package");
            }
            Assert.That(ManifestWindow.IconOf(""), Is.Null, "no state: no mark");
            _window.Assess();
            var marks = _window.rootVisualElement.Query<VectorIcon>("mark").ToList();
            Assert.That(marks.Any(m => m.vectorImage != null), "the assessed rows' marks are drawn");
        }

        [Test]
        public void One_switch_turns_a_whole_family_off_and_on()
        {
            // As the Shot Manifest's one Key / Marker / Off for Fade: a family is one behavior. 2026-11-24
            _window.SetOff("test_swing_shut", true);
            Assert.That(_manifest.off, Is.SupersetOf(new[] { "test_swing_open", "test_swing_shut" }));
            _window.SetOff("test_swing_open", false);
            Assert.That(_manifest.off, Has.None.StartsWith("test_swing_"));
        }

        [Test]
        public void A_build_in_a_new_scene_that_cannot_run_says_why_in_the_window()
        {
            _manifest.locked.Add("S01");
            LogAssert.Expect(LogType.Error, new Regex("locked"));  // the run's issue, in the Console too
            _window.Reset(discard: false);
            Assert.That(_window.Reported.Single().message, Does.Contain("step(s) are locked (S01)"), "said in the window, never silently");
            Assert.That(ManifestStatus.Level(_window.Reported.Single()), Is.EqualTo("error"), "marked as the refusal it is");
            _manifest.locked.Clear();
            _window.Build();
            Assert.That(_window.Reported, Is.Empty, "until the next run");
        }

        [Test]
        public void A_note_a_row_says_as_it_builds_is_said_on_its_row_and_never_again_above_the_table()
        {
            // Measured 2026-11-25 on a production task: the kit's notes (an arrow with no free spot seen, a fade its step's
            // return leaves out) each stood above the table with an error's cross as well as on its row as a warning, which
            // read as a build with errors.
            Object.DestroyImmediate(_manifest.plan);
            _manifest.plan = new TextAsset(
                "{\"version\": 2, \"source\": {\"sheet\": \"test.csv\", \"delivery\": \"delivery.fbx\"}, \"steps\": [" +
                "{\"id\": \"S01\", \"name\": \"One\", \"actions\": [" +
                "{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"], \"fields\": {\"value\": \"note\"}}]}]}")
            { hideFlags = HideFlags.HideAndDontSave };
            _window.Manifest = null;
            _window.Manifest = _manifest;
            _window.Build();
            Assert.That(_window.Reported, Is.Empty, "its row says it");
            ManifestAssessment.Row row = _window.Assessed.rows.Single();
            Assert.That(row.notes.Single(), Does.Contain(TestPartBehavior.NoteWords), "said on its row");
            Assert.That(row.Status, Is.EqualTo(ManifestStatus.Warned));
        }

        [Test]
        public void An_issue_is_marked_by_its_own_severity()
        {
            Assert.That(ManifestStatus.Level(new Core.Issue { severity = "error" }), Is.EqualTo("error"));
            Assert.That(ManifestStatus.Level(new Core.Issue { severity = "warning" }), Is.EqualTo("warn"));
            Assert.That(ManifestStatus.Level(new Core.Issue { severity = "info" }), Is.EqualTo("info"));
            Assert.That(ManifestStatus.Level(new Core.Issue()), Is.EqualTo("warn"), "one that names none is the kernel's default");
        }

        [Test]
        public void Its_build_button_counts_what_a_build_would_change()
        {
            var build = _window.rootVisualElement.Q<Button>("build");
            if (build == null) Assert.Inconclusive("this editor lays no window out (no graphics): the button is not made");
            Assert.That(build.text, Is.EqualTo("Build (2)"), "the steps a build would change");
            Assert.That(build.tooltip, Does.Contain("builds 2 row(s)").And.Contain("1 row(s) wait"));
            _window.Build();
            Assert.That(build.text, Is.EqualTo("Build"));
            Assert.That(build.enabledSelf, Is.False, "nothing left to build");
            Assert.That(_window.rootVisualElement.Q<Label>("summary").text, Does.Contain("2 built").And.Contain("1 need you"));
        }
    }
}
