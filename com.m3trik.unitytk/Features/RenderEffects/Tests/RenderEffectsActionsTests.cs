using System;
using System.IO;
using System.Linq;
using System.Text;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// The feature's actions: a fade and a pulse keyed onto a clip of the project's own
    /// at the path the Animator binds, the component added where missing, a second run
    /// changing nothing, a dry run planning what its run makes and changing nothing, an
    /// imported clip, a target with no Animator and one in a prefab asset refused; and the
    /// twins prepared into an asset once, given to the effects, with a second run and a dry
    /// run writing nothing.
    /// </summary>
    public class RenderEffectsActionsTests
    {
        const string Folder = "Assets/UnitytkRenderEffectsActionsTests";

        Stage _stage;
        GameObject _model, _door;
        AnimationClip _clip;

        [SetUp]
        public void SetUp()
        {
            _stage = new Stage();
            _model = _stage.Node("Shed");
            _model.AddComponent<Animator>();
            var frame = _stage.Node("Frame", _model.transform);
            _door = _stage.Node("Door", frame.transform);
            _stage.Renderer("Panel", _door.transform, _stage.Lit("Wood"));
            _clip = _stage.Made(new AnimationClip { name = "Open" });
        }

        [TearDown]
        public void TearDown()
        {
            _stage.Dispose();
            AssetDatabase.DeleteAsset(Folder);
        }

        AnimationCurve Keys<T>(string field)
            => AnimationUtility.GetEditorCurve(_clip, EditorCurveBinding.FloatCurve("Frame/Door", typeof(T), field));

        [Test]
        public void Key_fade_keys_the_fade_on_the_animators_path_once()
        {
            Assert.That(RenderEffectsActions.KeyFade(_door, _clip, 1f, 2f), Is.True);
            Assert.That(_door.GetComponent<Fade>(), Is.Not.Null, "a Fade added where it had none");
            AnimationCurve alpha = Keys<Fade>("alpha");
            Assert.That(alpha.keys.Select(k => (k.time, k.value)), Is.EqualTo(new[] { (1f, 1f), (2f, 0f) }));
            Assert.That(alpha.Evaluate(1.5f), Is.EqualTo(0.5f).Within(1e-4f), "linear");
            Assert.That(RenderEffectsActions.KeyFade(_door, _clip, 1f, 2f), Is.False, "a second run changes nothing");

            Assert.That(RenderEffectsActions.KeyFade(_door, _clip, 3f, 4f, FadeDirection.In), Is.True);
            Assert.That(Keys<Fade>("alpha").keys.Select(k => (k.time, k.value)),
                        Is.EqualTo(new[] { (1f, 1f), (2f, 0f), (3f, 0f), (4f, 1f) }), "keys outside the window kept");
        }

        [Test]
        public void A_dry_run_plans_the_fade_and_changes_nothing()
        {
            var ctx = new OpContext(dryRun: true);
            Assert.That(RenderEffectsActions.KeyFade(_door, _clip, 0f, 1f, ctx: ctx), Is.True);
            Assert.That(ctx.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "change" }));
            Assert.That(_door.GetComponent<Fade>(), Is.Null);
            Assert.That(AnimationUtility.GetCurveBindings(_clip), Is.Empty);
        }

        [Test]
        public void Key_pulse_keys_a_triangle_every_period_and_sets_its_colour()
        {
            Color blue = MaterialColor.Encode(new Color(0f, 0f, 1f, 1f));
            Assert.That(RenderEffectsActions.KeyPulse(_door, _clip, 0f, 2f, 1f, blue), Is.True);
            var highlight = _door.GetComponent<Highlight>();
            Assert.That(highlight.color, Is.EqualTo(blue));
            Assert.That(Keys<Highlight>("intensity").keys.Select(k => (k.time, k.value)),
                        Is.EqualTo(new[] { (0f, 0f), (0.5f, 1f), (1f, 0f), (1.5f, 1f), (2f, 0f) }));
            Assert.That(RenderEffectsActions.KeyPulse(_door, _clip, 0f, 2f, 1f, blue), Is.False, "a second run changes nothing");
        }

        [Test]
        public void A_dry_run_plans_the_pulse_and_its_colour_as_the_run_makes_them()
        {
            Color blue = MaterialColor.Encode(new Color(0f, 0f, 1f, 1f));
            var plan = new OpContext(dryRun: true);
            Assert.That(RenderEffectsActions.KeyPulse(_door, _clip, 0f, 2f, 1f, blue, plan), Is.True);
            Assert.That(_door.GetComponent<Highlight>(), Is.Null, "a dry run adds nothing");
            Assert.That(AnimationUtility.GetCurveBindings(_clip), Is.Empty, "and keys nothing");

            var run = new OpContext();
            Assert.That(RenderEffectsActions.KeyPulse(_door, _clip, 0f, 2f, 1f, blue, run), Is.True);
            Assert.That(_door.GetComponent<Highlight>().color, Is.EqualTo(blue));
            Assert.That(plan.Changes.Select(c => (c.kind, c.target, c.action)),
                        Is.EqualTo(run.Changes.Select(c => (c.kind, c.target, c.action))), "the plan is the run");
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "change", "change" }),
                        "the Highlight added, its colour set, the clip keyed");
        }

        [Test]
        public void A_target_in_a_prefab_asset_is_refused_with_its_fix_and_the_asset_left_alone()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string path = Folder + "/Shed.prefab";
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(_model, path);
            GameObject door = asset.transform.Find("Frame/Door").gameObject;
            string saved = File.ReadAllText(path);

            // Measured: with no context the component went onto the asset in memory, unsaved, and the next
            // AssetDatabase.SaveAssets wrote it, with no undo; a context's Undo.AddComponent did the same.
            var refused = Assert.Throws<ArgumentException>(() => RenderEffectsActions.KeyFade(door, _clip, 0f, 1f));
            Assert.That(refused.Message, Does.Contain("Prefab Mode").And.Contain("an instance of it in a scene"));
            Assert.Throws<ArgumentException>(() => RenderEffectsActions.KeyPulse(door, _clip, 0f, 1f, 0.5f, ctx: new OpContext()));
            Assert.Throws<ArgumentException>(() => RenderEffectsActions.KeyFade(door, _clip, 0f, 1f, ctx: new OpContext(dryRun: true)),
                                             "a dry run refuses as its run does");
            Assert.That(door.GetComponent<Fade>(), Is.Null, "nothing added to the asset");
            Assert.That(door.GetComponent<Highlight>(), Is.Null);
            Assert.That(AnimationUtility.GetCurveBindings(_clip), Is.Empty, "nothing keyed");
            AssetDatabase.SaveAssets();
            Assert.That(File.ReadAllText(path), Is.EqualTo(saved), "the asset as it was");
        }

        [Test]
        public void An_imported_clip_and_a_target_no_animator_plays_are_refused()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string model = Folder + "/effects.fbx";
            File.WriteAllText(model, EffectsFbx.Write(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
            AnimationClip imported = AssetDatabase.LoadAllAssetsAtPath(model).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var refused = Assert.Throws<ArgumentException>(() => RenderEffectsActions.KeyFade(_door, imported, 0f, 1f));
            Assert.That(refused.Message, Does.Contain("duplicate the clip"));
            var loose = _stage.Node("Loose");
            Assert.Throws<ArgumentException>(() => RenderEffectsActions.KeyFade(loose, _clip, 0f, 1f));
            Assert.Throws<ArgumentException>(() => RenderEffectsActions.KeyFade(_door, _clip, 1f, 1f), "an empty window");
        }

        [Test]
        public void Prepare_materials_makes_each_twin_once_and_gives_the_effects_its_table()
        {
            var fade = _door.AddComponent<Fade>();
            _door.AddComponent<Highlight>();
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string path = Folder + "/Shed Twins.asset";

            var plan = new OpContext(dryRun: true);
            Assert.That(RenderEffectsActions.PrepareMaterials(_model, path, plan), Is.EqualTo(3 + 2), "the plan is the run");
            Assert.That(plan.Changes.Select(c => c.kind),
                        Is.EqualTo(new[] { "write", "write", "write", "write", "change", "change" }),
                        "the table and its three twins written, two effects given it");
            Assert.That(File.Exists(path), Is.False, "a dry run writes nothing");
            Assert.That(fade.twins, Is.Null);

            Assert.That(RenderEffectsActions.PrepareMaterials(_model, path), Is.EqualTo(3 + 2), "three twins, two effects given them");
            var table = AssetDatabase.LoadAssetAtPath<MaterialTwins>(path);
            Assert.That(table.twins.Select(t => t.state), Is.EquivalentTo(new[]
            {
                TwinState.Blended, TwinState.Glowing, TwinState.Blended | TwinState.Glowing,
            }), "every state a Fade and a Highlight combine to");
            Assert.That(fade.twins, Is.SameAs(table));
            Assert.That(_door.GetComponent<Highlight>().twins, Is.SameAs(table));
            Assert.That(RenderEffectsActions.PrepareMaterials(_model, path), Is.Zero, "a second run changes nothing");
        }

        [Test]
        public void A_dry_run_prepares_the_twins_of_the_effects_it_only_planned_as_its_live_run_does()
        {
            // A build adds its effects and prepares their twins in one run; its dry run has added nothing yet.
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string path = Folder + "/Shed Twins.asset";
            void Run(OpContext ctx)
            {
                RenderEffectsActions.AddHighlight(_door, true, ctx: ctx);
                RenderEffectsActions.AddFadeTo(_door, 1f, ctx: ctx);
                RenderEffectsActions.PrepareMaterials(_model, path, ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            Assert.That(File.Exists(path), Is.False, "a dry run writes nothing");
            var live = new OpContext();
            Run(live);
            Assert.That(dry.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(live.Changes.Select(c => (c.kind, c.target, c.action))),
                        "the plan is the run, the twins of the planned effects with it");
            Assert.That(live.Changes.Count(c => c.action.StartsWith("add the twin", StringComparison.Ordinal)), Is.EqualTo(3),
                        "a Fade's and a Highlight's three states");
            Assert.That(_door.GetComponent<Highlight>().twins, Is.SameAs(AssetDatabase.LoadAssetAtPath<MaterialTwins>(path)));
        }

        [Test]
        public void A_table_already_at_the_path_is_added_to_and_given_to_new_effects_never_a_second_beside_it()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string path = Folder + "/Shed Twins.asset";
            _door.AddComponent<Highlight>();
            Assert.That(RenderEffectsActions.PrepareMaterials(_model, path), Is.EqualTo(1 + 1));
            // The scene built anew: the same effect made again, with no table.
            UnityEngine.Object.DestroyImmediate(_door.GetComponent<Highlight>());
            var again = _door.AddComponent<Highlight>();
            var ctx = new OpContext();
            Assert.That(RenderEffectsActions.PrepareMaterials(_model, path, ctx), Is.EqualTo(1), "its twin is there: the table is only given to it");
            Assert.That(ctx.Changes.Select(c => c.kind), Is.EqualTo(new[] { "change" }));
            Assert.That(again.twins, Is.SameAs(AssetDatabase.LoadAssetAtPath<MaterialTwins>(path)));
            Assert.That(AssetDatabase.FindAssets("t:MaterialTwins", new[] { Folder }).Length, Is.EqualTo(1), "one table");
        }

        [Test]
        public void What_is_left_out_of_the_effects_above_it_needs_no_twin_of_theirs()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            _door.AddComponent<Highlight>();
            var arrow = _stage.Node("Arrow", _door.transform);
            arrow.AddComponent<EffectExempt>();
            _stage.Renderer("Shaft", arrow.transform, _stage.Lit("Blue"));
            string path = Folder + "/Shed Twins.asset";
            RenderEffectsActions.PrepareMaterials(_model, path);
            Assert.That(AssetDatabase.LoadAssetAtPath<MaterialTwins>(path).twins.Select(t => t.original.name), Is.EqualTo(new[] { "Wood" }),
                        "the door's panel glows; the arrow under it does not");
        }

        [Test]
        public void Prepare_materials_saves_what_it_wrote_and_no_other_asset()
        {
            _door.AddComponent<Fade>();
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            var other = new Material(Shader.Find(_stage.Host.LitShader)) { name = "Other" };
            AssetDatabase.CreateAsset(other, Folder + "/Other.mat");
            other.renderQueue = 2001;  // a person's edit, not saved yet
            EditorUtility.SetDirty(other);

            string path = Folder + "/Shed Twins.asset";
            Assert.That(RenderEffectsActions.PrepareMaterials(_model, path), Is.GreaterThan(0));
            Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<MaterialTwins>(path)), Is.False, "the table it wrote is saved");
            Assert.That(EditorUtility.IsDirty(other), Is.True, "another asset's unsaved edit is the person's to save");
        }

        [Test]
        public void A_highlight_and_fades_for_a_step_are_planned_made_disabled_once_and_undone()
        {
            void Run(OpContext ctx)
            {
                RenderEffectsActions.AddHighlight(_door, true, ctx: ctx);
                RenderEffectsActions.AddFadeTo(_door, 1f, ctx: ctx);
                RenderEffectsActions.AddFadeTo(_door, 0f, ctx: ctx);
                RenderEffectsActions.AddFadeTo(_door, 0f, ctx: ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            Assert.That(_door.GetComponent<Highlight>() == null && _door.GetComponent<Fade>() == null, "a dry run makes nothing");
            OpContext.RunMenu("Effects for a step", ctx =>
            {
                Run(ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target))));
            });
            var highlight = _door.GetComponent<Highlight>();
            Assert.That(!highlight.enabled && highlight.intensity == 1f && highlight.pulse, "made disabled: a step enables it");
            Assert.That(_door.GetComponent<Fade>().alpha, Is.EqualTo(0f), "a fade in starts hidden");
            Assert.That(_door.GetComponents<FadeTo>().Select(f => (f.alpha, f.enabled)), Is.EqualTo(new[] { (1f, false), (0f, false) }));
            Assert.That(_door.GetComponent<FadeTo>().seconds, Is.EqualTo(FadeTo.DefaultSeconds), "the effect recipe's length");
            var again = new OpContext(dryRun: true);
            Run(again);
            Assert.That(again.Changes, Is.Empty);
            Undo.PerformUndo();
            Assert.That(_door.GetComponent<Highlight>() == null && _door.GetComponent<FadeTo>() == null, "one Ctrl+Z took them away");
        }

        [Test]
        public void A_state_switch_for_a_step_is_planned_made_disabled_once_and_switches_its_track()
        {
            Material lensMat = _stage.Lit("lens_led", new LitSurface { emission = Color.white });
            Renderer lens = _stage.Renderer("Lens", _door.transform, lensMat);
            var track = _stage.Node("lens_led", _model.transform).AddComponent<EmissionTrack>();
            track.material = lensMat;
            track.color = MaterialColor.Encode(Color.white);
            track.strength = 2f;
            track.weight = 0f;
            track.states.Add(new EmissionState { name = "off", tint = Color.white, weight = 0f });
            track.states.Add(new EmissionState { name = "amber", tint = new Color(1f, 0.5f, 0f), weight = 1f });

            var dry = new OpContext(dryRun: true);
            Assert.That(RenderEffectsActions.AddStateSwitch(track, "amber", ctx: dry), Is.Null);
            Assert.That(track.GetComponent<EmissionStateSwitch>(), Is.Null, "a dry run makes nothing");
            Assert.That(dry.Changes.Count, Is.EqualTo(1));
            EmissionStateSwitch amber = RenderEffectsActions.AddStateSwitch(track, "amber");
            Assert.That(amber.enabled, Is.False, "made disabled: a step enables it");
            Assert.That(RenderEffectsActions.AddStateSwitch(track, "amber"), Is.SameAs(amber), "once per state");
            Assert.That(RenderEffectsActions.AddStateSwitch(track, "off"), Is.Not.SameAs(amber), "a switch per state");

            Assert.That(amber.Switch(), Is.True, "what enabling it does");
            Assert.That(track.weight, Is.EqualTo(1f));
            Stage.AssertRgb(_stage.Emission(lens.sharedMaterial), new Color(2f, 1f, 0f), "white x amber x strength 2");
            var refused = Assert.Throws<ArgumentException>(() => RenderEffectsActions.AddStateSwitch(track, "blue"));
            Assert.That(refused.Message, Does.Contain("off, amber"), "its states named");
        }

        [Test]
        public void Each_adds_switched_on_where_asked_and_names_a_repeated_node_by_its_path()
        {
            Highlight glow = RenderEffectsActions.AddHighlight(_door, false, startEnabled: true);
            FadeTo fade = RenderEffectsActions.AddFadeTo(_door, 0f, 2f, startEnabled: true);
            Assert.That(glow.enabled && fade.enabled, Is.True, "switched on from the start");
            Assert.That(fade.seconds, Is.EqualTo(2f));

            var twin = new GameObject(_door.name);  // another object of its name: plans name each by its path
            try
            {
                var plan = new OpContext(dryRun: true);
                RenderEffectsActions.AddHighlight(twin, ctx: plan);
                Assert.That(plan.Changes.Single().target, Is.EqualTo($"{twin.name}/{nameof(Highlight)}"), "a root's path is its name");
                Assert.That(OpContextEdits.Key(_door, typeof(Highlight)), Does.Contain("/" + _door.name + "/"), "the nested one by its path");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(twin);
            }
        }
    }
}
