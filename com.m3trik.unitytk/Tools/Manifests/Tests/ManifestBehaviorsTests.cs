using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.Manifests.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// The extension model across two kits (<see cref="ManifestBehaviors"/>): the feature's test kit and this
    /// assembly's (<see cref="ToolTestKit"/>), each with its own behaviors, settings and data in one plan. Each kit
    /// takes part where a row is its own, builds by its own data, owns its behaviors' settings, and runs its own
    /// setups of a fresh scene alone.
    /// </summary>
    public class ManifestBehaviorsTests
    {
        const string PrefabPath = "Assets/ManifestBehaviorsTests delivery.prefab";

        const string Both =
            "{\"version\": 2, \"extensions\": {\"test\": {\"root\": \"Feature Kit Root\"}, \"tool_test\": {\"holder\": \"Tool Kit Holder\"}}, " +
            "\"steps\": [{\"id\": \"S01\", \"actions\": [" +
            "{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"]}," +
            "{\"behavior\": \"test_sign\", \"text\": \"The sign faces back\", \"settings\": {\"face\": \"Back\"}}]}]}";

        const string FeatureOnly =
            "{\"version\": 2, \"steps\": [{\"id\": \"S01\", \"actions\": [{\"behavior\": \"test_part\", \"text\": \"Part A\", \"targets\": [\"A\"]}]}]}";

        static int _ownKit, _named, _withContext, _misnamed;  // the setups below, run

        // A setup naming a type that is no kit: never run, and said.
        [ManifestSceneSetup(typeof(ToolTestSettings))]
        static void MisnamedSetup(Manifest manifest, ManifestScene scene, OpContext ctx) => _misnamed++;

        // A setup of the manifests the kit beside it (this assembly's) takes part in.
        [ManifestSceneSetup]
        static void OwnKitSetup(Manifest manifest, ManifestScene scene, OpContext ctx) => _ownKit++;

        // A setup of the manifests the kit it names takes part in.
        [ManifestSceneSetup(typeof(ToolTestKit))]
        static void NamedSetup(Manifest manifest, ManifestScene scene, OpContext ctx)
        {
            _named++;
            if (ctx != null) _withContext++;
        }

        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _root = new GameObject("delivery");
            new GameObject("A").transform.SetParent(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(PrefabPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void Two_kits_build_their_own_rows_by_their_own_data_and_settings_in_one_manifest()
        {
            SheetPlan.Shape.Payload plan = SheetPlan.Read(Both, "test", out string refusal);
            Assert.That(refusal, Is.Null);
            Assert.That(ManifestBehaviors.Kits(plan).Select(k => k.GetType()), Is.EqualTo(new[] { typeof(TestKit), typeof(ToolTestKit) }));
            Assert.That(ManifestBehaviors.Kits(SheetPlan.Read(FeatureOnly, "test", out _)).Select(k => k.GetType()), Is.EqualTo(new[] { typeof(TestKit) }),
                        "a kit with no row of its own takes no part");
            Assert.That((ManifestBehaviors.KitOf("test_part")?.GetType(), ManifestBehaviors.KitOf("test_sign")?.GetType()),
                        Is.EqualTo((typeof(TestKit), typeof(ToolTestKit))));
            Assert.That((ManifestBehaviors.SettingsOf("test_part"), ManifestBehaviors.SettingsOf("test_sign"), ManifestBehaviors.SettingsOf("test_choice")),
                        Is.EqualTo((typeof(TestSettings), typeof(ToolTestSettings), (System.Type)null)),
                        "a behavior's settings are its own kit's, never the first kit holding a field of the name; none where it names none");

            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                var scene = new ManifestScene(_root, new AnimationClip[0]) { Manifest = manifest };
                ManifestAssessment built = ManifestsActions.Build(plan, scene, new OpContext());
                Assert.That(built.rows.All(r => r.state == ManifestAssessment.State.Ready), built.Summary());
                Assert.That(SceneRoots.Find("Feature Kit Root") != null && SceneRoots.Find("Tool Kit Holder") != null, "each kit built by its own data");
                Assert.That(SceneRoots.Find(TestKit.Root) == null && SceneRoots.Find(ToolTestKit.Holder) == null, "and by no other kit's");
                Assert.That(built.shared.parts.Select(p => p.name), Is.EquivalentTo(new[] { "Feature Kit Root", "Tool Kit Holder" }));
                Assert.That(_root.transform.Find("A").GetComponent<BoxCollider>(), Is.Not.Null);

                Assert.That(ManifestsActions.SetTune(manifest, "S01", "", "test_sign", "face", "Front"), Is.True, "its own kit's setting");
                Assert.That(ManifestsActions.SetTune(manifest, "S01", "A", "test_part", "face", "Front"), Is.False, "another kit's is not the behavior's");
                Assert.That(ManifestsActions.AddSettings(manifest, typeof(TestSettings)) != null && ManifestsActions.AddSettings(manifest, typeof(ToolTestSettings)) != null);
                Assert.That(manifest.settings.Select(s => s.GetType()), Is.EquivalentTo(new[] { typeof(TestSettings), typeof(ToolTestSettings) }), "each kit's kept apart");
            }
            finally
            {
                foreach (ScriptableObject kept in manifest.settings) Object.DestroyImmediate(kept);
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_fresh_scenes_setups_are_its_kits_own_and_given_the_runs_context()
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(_root, PrefabPath);
            Object.DestroyImmediate(_root);
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            manifest.hideFlags = HideFlags.HideAndDontSave;  // kept across the new scenes a reset opens, as an asset is
            manifest.plan = new TextAsset(FeatureOnly) { hideFlags = HideFlags.HideAndDontSave };
            manifest.delivery = prefab;
            try
            {
                int own = _ownKit, named = _named, context = _withContext, misnamed = _misnamed;
                var first = new OpContext();
                Assert.That(ManifestsActions.Reset(manifest, discard: true, first), Is.Not.Null);
                Assert.That((_ownKit, _named), Is.EqualTo((own, named)), "this kit takes no part: its setups do not run");
                Assert.That(first.Issues.Select(i => i.message), Has.None.Contains("MisnamedSetup"),
                            "nor is its misnamed setup this manifest's to hear of (it was said in every project's builds)");
                Assert.That(_misnamed, Is.EqualTo(misnamed));

                Object.DestroyImmediate(manifest.plan);
                manifest.plan = new TextAsset(Both) { hideFlags = HideFlags.HideAndDontSave };
                var second = new OpContext();
                Assert.That(ManifestsActions.Reset(manifest, discard: true, second), Is.Not.Null);
                Assert.That((_ownKit, _named, _withContext), Is.EqualTo((own + 1, named + 1, context + 1)), "it takes part: each runs once, given the run's context");
                Assert.That(second.Issues.Select(i => i.message), Has.Some.Contains("MisnamedSetup names ToolTestSettings as its kit, which is no IManifestKit"),
                            "a setup naming no kit is said to the builds its kit takes part in, never silently skipped");
                Assert.That(_misnamed, Is.EqualTo(misnamed), "and never run");
            }
            finally
            {
                Object.DestroyImmediate(manifest.plan);
                Object.DestroyImmediate(manifest);
            }
        }
    }
}
