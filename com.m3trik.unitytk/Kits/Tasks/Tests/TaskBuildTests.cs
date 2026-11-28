using M3trik.Unitytk.Manifests;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Placement;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// <see cref="ManifestsActions"/> over the package's Tasks sample as a task: a rebuild places the delivery and builds it, a second plans
    /// nothing, an adjusted spot survives a rebuild, a reset that discards puts it back where the build
    /// puts it.
    /// </summary>
    public class TaskBuildTests
    {
        Manifest _task;
        TaskSettings _settings;
        string _twins;
        static int _setups;  // how many times a reset ran this test assembly's own setup

        // A project's own setup of a task's fresh scene, as a [ManifestSceneSetup] method is found and run.
        [ManifestSceneSetup(typeof(TaskBuild))]
        static void CountSetup(Manifest task, ManifestScene scene, OpContext ctx)
        {
            if (scene != null && scene.Root != null) _setups++;
        }

        [SetUp]
        public void SetUp()
        {
            Sample sample = Sample.FindByPackage("com.m3trik.unitytk", null).First(s => s.displayName == "Tasks");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string folder = "Assets" + full.Substring(Path.GetFullPath(Application.dataPath).Replace('\\', '/').Length);
            _task = ScriptableObject.CreateInstance<Manifest>();
            _task.hideFlags = HideFlags.HideAndDontSave;  // kept across the new scenes a reset opens, as a task asset is
            _task.plan = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/cabinet.plan.json");
            _task.delivery = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/cabinet.fbx");
            _settings = ScriptableObject.CreateInstance<TaskSettings>();
            _settings.hideFlags = HideFlags.HideAndDontSave;
            _settings.debugTools = true;  // the sample's own choices, as its demo recipe sets them: its debug tools, a page a step
            _settings.pages = PlacardPages.EveryStep;
            _task.settings.Add(_settings);
            _twins = TaskScene.TwinsPath(_task);
            Assert.That(_task.plan != null && _task.delivery != null, "the Tasks sample is imported");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_task);
            Object.DestroyImmediate(_settings);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(_twins);  // the twins a build prepared beside the sample's plan
        }

        // An arrow the build placed for the sample's first step.
        static PlacedSpot Arrow() => Object.FindObjectsByType<PlacedSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                           .First(s => s.key.StartsWith("indicate/"));

        [Test]
        public void A_rebuild_places_and_builds_the_task_converges_and_keeps_what_was_adjusted()
        {
            ManifestAssessment built = ManifestsActions.Build(_task, new OpContext());
            Assert.That(built.rows.All(r => r.state == ManifestAssessment.State.Ready), built.Summary());
            Assert.That(ManifestsActions.Placed(_task.delivery), Is.Not.Null, "its delivery placed in the open scene");
            Assert.That(Object.FindAnyObjectByType<M3trik.Unitytk.Steps.SequenceDebugCommands>(), Is.Not.Null, "with its debug tools");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, again);
            Assert.That(again.Changes, Is.Empty, "built already: nothing to do");

            PlacedSpot arrow = Arrow();
            arrow.transform.position += Vector3.up * 0.2f;  // a person moves it
            Vector3 moved = arrow.transform.position;
            ManifestsActions.Build(_task, new OpContext());
            Assert.That(Vector3.Distance(Arrow().transform.position, moved), Is.LessThan(1e-4f), "kept where it was moved");
            Assert.That(_task.moved.Select(v => v.name), Has.Some.StartsWith("indicate/"), "the adjustment kept in the task's manifest");

            _settings.debugTools = false;  // the switch, off: a build takes the debug tools away
            ManifestsActions.Build(_task, new OpContext());
            Assert.That(Object.FindAnyObjectByType<M3trik.Unitytk.Steps.SequenceDebugCommands>() == null, "debug tools off: none left in the scene");
            _settings.debugTools = true;

            int before = _setups;
            ManifestsActions.Reset(_task, discard: true, new OpContext());
            Assert.That(_setups, Is.EqualTo(before + 1), "a project's own setup runs once on a reset, with the task built");
            Assert.That(_task.moved, Is.Empty, "discarded");
            Assert.That(Vector3.Distance(Arrow().transform.position, moved), Is.GreaterThan(0.1f), "back where the build puts it, in a new scene");
        }

        [Test]
        public void A_highlights_style_and_colour_a_fades_seconds_and_a_played_takes_speed_reach_what_they_build()
        {
            // The sheet's format declares all four, and no builder took them: a row that wrote one built as if it had not.
            ManifestsActions.Build(_task, new OpContext());
            const string latches = "CABINET|CABINET_LATCHES_GRP", bolt = "CABINET|BOLT_GRP";
            var glow = ManifestsActions.SceneOf(_task).Node(latches).GetComponent<M3trik.Unitytk.RenderEffects.Highlight>();
            Assert.That((glow.pulse, glow.color), Is.EqualTo((false, M3trik.Unitytk.RenderEffects.Highlight.DefaultColor)),
                        "by default: steady where the user only looks (they act on each latch, not on the pair), in the delivery's own highlight colour");
            var own = new OpContext();
            ManifestsActions.SetTune(_task, "A01", latches, "highlight", "highlight.style", "pulse", own);  // as a sheet writes it
            ManifestsActions.SetTune(_task, "A01", latches, "highlight", "highlight.color", "#3399FF", own);
            ManifestsActions.SetTune(_task, "B01", bolt, "hide", "hide.fade", "2.5", own);
            ManifestsActions.SetTune(_task, "B01", "", "play", "play.speed", "2", own);
            Assert.That(own.Issues.Select(i => i.message), Is.Empty);
            ManifestsActions.Build(_task, new OpContext());

            Assert.That(glow.pulse, Is.True, "pulsing, as its style says");
            Assert.That(ColorUtility.ToHtmlStringRGB(glow.color), Is.EqualTo("3399FF"));
            Assert.That(glow.colorDim, Is.EqualTo(M3trik.Unitytk.RenderEffects.Highlight.DimOf(glow.color)), "its dim colour with it");
            var fade = ManifestsActions.SceneOf(_task).Node(bolt).GetComponents<M3trik.Unitytk.RenderEffects.FadeTo>().Single(f => f.alpha == 0f);
            Assert.That(fade.seconds, Is.EqualTo(2.5f));
            var player = ManifestsActions.Placed(_task.delivery).GetComponent<M3trik.Unitytk.Interaction.ClipPlayer>();
            Assert.That(player.Find("B01_2").speed, Is.EqualTo(2f));
            Assert.That(player.Find("B01_1").speed, Is.EqualTo(1f), "the take before it plays as it was animated");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, again);
            Assert.That(again.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "and a second build changes nothing");
        }

        // A person's change of a plain field, by its kind: a value no build gives it.
        static object Changed(object value)
        {
            switch (value)
            {
                case float f: return f + 0.37f;
                case int i: return i + 3;
                case bool b: return !b;
                case string s: return s + " (edited)";
                case Color c: return new Color(1f - c.r, c.g, 0.25f, c.a);
                case Vector2 v: return v + new Vector2(0.11f, 0.07f);
                case Vector3 v: return v + new Vector3(0.11f, 0.07f, 0.05f);
                default:
                    System.Array names = System.Enum.GetValues(value.GetType());
                    return names.GetValue((System.Array.IndexOf(names, value) + 1) % names.Length);
            }
        }

        [Test]
        public void What_a_person_changes_on_any_part_a_build_made_is_kept_listed_and_reverted()
        {
            // A page's text and spot and a press's seconds were put back by the next build with nothing said; what
            // survived (a stand-off, a reach) was unknown to the manifest, so a new scene lost it and nothing reverted it.
            _settings.debugTools = false;
            ManifestAssessment built = ManifestsActions.Build(_task, new OpContext());
            Assert.That(ManifestsActions.Adjustments(_task), Is.Empty, "a build's own work is no adjustment");

            // Every plain field of every part the build holds, changed as an inspector changes it.
            var parts = built.shared.parts.Concat(built.steps.SelectMany(s => s.parts.Concat(s.rows.SelectMany(r => r.parts))))
                             .OfType<MonoBehaviour>().Where(p => p != null).Distinct().ToList();
            Assert.That(parts.Select(p => p.GetType().Name).Distinct().Count(), Is.GreaterThan(6), "the task's parts, of many kinds");
            var set = new System.Collections.Generic.List<(MonoBehaviour part, System.Reflection.FieldInfo field, object value)>();
            foreach (MonoBehaviour part in parts)
                foreach (System.Reflection.FieldInfo field in part.GetType().GetFields())
                {
                    System.Type kind = field.FieldType;
                    bool plain = kind == typeof(float) || kind == typeof(int) || kind == typeof(bool) || kind == typeof(string) || kind.IsEnum
                                 || kind == typeof(Color) || kind == typeof(Vector2) || kind == typeof(Vector3);
                    if (!plain || field.IsStatic || field.IsLiteral || field.IsInitOnly || field.IsNotSerialized) continue;
                    if (System.Attribute.IsDefined(field, typeof(HideInInspector))) continue;
                    object value = Changed(field.GetValue(part));
                    field.SetValue(part, value);
                    set.Add((part, field, value));
                }
            Assert.That(set.Count, Is.GreaterThan(40), "many fields of many parts");

            var quiet = ManifestsActions.Assess(_task);
            var busy = quiet.steps.SelectMany(s => s.rows).Where(r => r.NeedsBuild)
                            .Select(r => $"{r.step} {r.behavior}: {string.Join(" | ", r.changes.Select(c => c.action))}").ToList();
            Assert.That(busy.Where(row => !row.Contains(" indicate: ")), Is.Empty,
                        "what a person adjusted is no work for a build; only a field a behavior keeps as its object's own setting " +
                        "(an arrow's size) waits on one, which keeps it as that");
            Assert.That(quiet.adjusted.Count, Is.GreaterThan(40), "each is listed, before any build keeps it");

            var kept = new OpContext();
            ManifestsActions.Build(_task, kept);
            // (A field a behavior keeps as its object's setting is kept as text: a colour to the nearest of 255 steps.)
            bool Same(object now, object was) => now is Color a && was is Color b
                ? Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 0.02f : Equals(now, was);
            var lost = set.Where(each => each.part == null || !Same(each.field.GetValue(each.part), each.value))
                          .Select(each => $"{(each.part == null ? "(gone)" : each.part.GetType().Name)}.{each.field.Name}").Distinct().ToList();
            Assert.That(lost, Is.Empty, "a build keeps every one");
            Assert.That(kept.Changes.Select(c => c.action), Has.Some.StartsWith("keep what was adjusted in the scene"));
            Assert.That(_task.adjusted.Count, Is.GreaterThan(40), "kept in the manifest");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, again);
            Assert.That(again.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "and a second build changes nothing");

            // A new scene built with the adjustments kept has them; a revert gives each part what the build gave it.
            ManifestsActions.Reset(_task, discard: false, new OpContext());
            System.Collections.Generic.List<ManifestAdjustment> carried = ManifestsActions.Adjustments(_task);
            Assert.That(carried.Count(a => a.part != null), Is.GreaterThan(40), "carried into the new scene");
            ManifestAdjustment page = carried.First(a => a.part is M3trik.Unitytk.Panels.PlacardPage && a.field == nameof(M3trik.Unitytk.Panels.PlacardPage.text));
            Assert.That(((M3trik.Unitytk.Panels.PlacardPage)page.part).text, Does.EndWith(" (edited)"));
            Assert.That(ManifestsActions.RevertAdjustments(_task, page.part, new OpContext()), Is.GreaterThan(0));
            Assert.That(((M3trik.Unitytk.Panels.PlacardPage)page.part).text, Is.EqualTo(page.built), "one part's, put back");
            Assert.That(ManifestsActions.Revert(_task, ctx: new OpContext()), Is.GreaterThan(40), "Revert All Edits takes the rest");
            Assert.That(ManifestsActions.Adjustments(_task), Is.Empty);
            Assert.That(_task.adjusted, Is.Empty);
            ManifestsActions.Build(_task, new OpContext());  // an arrow's own size and colour were settings of its object: a build gives it the kit's again
            var settled = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, settled);
            Assert.That(settled.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "the scene stands as built again");
            Assert.That(ManifestsActions.Adjustments(_task), Is.Empty, "with nothing adjusted");
        }

        [Test]
        public void A_takes_window_and_speed_set_on_the_clip_player_are_its_rows_kept_into_a_new_scene_and_reverted()
        {
            // The real task (2026-11-26): a take given Auto off, From 0.1 and Speed 2 on the clip player was listed nowhere,
            // and a scene built anew played it from 0 at 1.
            _settings.debugTools = false;
            ManifestsActions.Build(_task, new OpContext());
            M3trik.Unitytk.Interaction.ClipPlayer.Entry Entry()
                => ManifestsActions.Placed(_task.delivery).GetComponent<M3trik.Unitytk.Interaction.ClipPlayer>().Find("B01_1");
            float found = Entry().from, until = Entry().until;
            Assert.That(Entry().auto, Is.True, "its window found from its motion");
            Entry().auto = false;  // as the inspector sets them
            Entry().from = found + 0.1f;
            Entry().speed = 2f;

            ManifestAssessment quiet = ManifestsActions.Assess(_task);
            Assert.That(quiet.steps.SelectMany(s => s.rows).Where(r => r.NeedsBuild).Select(r => $"{r.step} {r.behavior}"),
                        Is.EqualTo(new[] { "B01 use" }), "said on its row, which a build keeps them for");
            ManifestsActions.Build(_task, new OpContext());
            string[] kept = _task.tunes.Where(t => t.step == "B01").SelectMany(t => t.values).Select(v => v.name).ToArray();
            Assert.That(kept, Is.SupersetOf(new[] { "use.start", "use.speed" }), "kept as the row's own");
            Assert.That(kept, Has.No.Member("use.stop"), "what was not changed is not kept");
            Assert.That(_task.tunes.Where(t => t.step == "B01").Select(t => (t.target, t.behavior, t.row)), Is.EqualTo(new[] { ("", "use", 3) }),
                        "under the row itself, never its object: the take is the row's whatever objects it names");
            var settled = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, settled);
            Assert.That(settled.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "and a second build changes nothing");

            ManifestsActions.Reset(_task, discard: false, new OpContext());
            Assert.That((Entry().auto, Entry().speed), Is.EqualTo((false, 2f)), "a scene built anew gives them again");
            Assert.That(Entry().from, Is.EqualTo(found + 0.1f).Within(1e-5f));
            Assert.That(Entry().until, Is.EqualTo(until).Within(1e-5f), "its end found from its motion, as before");

            Assert.That(ManifestsActions.Revert(_task, "B01", ctx: new OpContext()), Is.GreaterThan(0));
            ManifestsActions.Build(_task, new OpContext());
            Assert.That((Entry().auto, Entry().speed), Is.EqualTo((true, 1f)), "reverted: found from its motion, at its speed");
            Assert.That(Entry().from, Is.EqualTo(found).Within(1e-5f));
        }

        [Test]
        public void A_rows_take_is_set_on_the_row_never_on_one_of_its_objects_and_reaches_its_clip()
        {
            // A Use row offered its take's window and speed on every object it names (Settings for 'X'), and the build read
            // them for its first object alone: a value set on any other did nothing. The clip player keeps one entry a take.
            const string bolt = "CABINET|BOLT_GRP";
            string[] take = { "use.speed", "use.start", "use.stop" };
            string[] offered = ManifestsActions.Offered(_task, "B01", bolt, "use", 3);
            Assert.That(offered, Is.Not.Empty, "an object of a use row is offered settings of its own");
            Assert.That(offered.Intersect(take), Is.Empty, "but never its row's take");
            Assert.That(ManifestsActions.Offered(_task, "B01", bolt, "use", 3, wholeRow: true), Is.EquivalentTo(take), "its row is");
            Assert.That(offered, Does.Contain("use.trigger"), "how one object plays it stays its own");
            Assert.That(ManifestsActions.Offered(_task, "B01", "", "play", 4), Is.EquivalentTo(new[] { "play.after", "play.speed", "play.start", "play.stop" }),
                        "a Play row, which names no object, is offered its own");
            var refused = new OpContext();
            Assert.That(ManifestsActions.SetTune(_task, "B01", bolt, "use", "use.speed", "3", refused, 3), Is.False);
            Assert.That(refused.Issues.Select(i => i.code), Is.EqualTo(new[] { ManifestsActions.SettingCode }), "refused for one object, and said");

            var own = new OpContext();
            Assert.That(ManifestsActions.SetTune(_task, "B01", "", "use", "use.speed", "2", own, 3), Is.True);
            Assert.That(ManifestsActions.SetTune(_task, "B01", "", "play", "play.speed", "3", own), Is.True, "every Play row of the step");
            Assert.That(own.Issues, Is.Empty);
            ManifestsActions.Build(_task, new OpContext());
            M3trik.Unitytk.Interaction.ClipPlayer Player() => ManifestsActions.Placed(_task.delivery).GetComponent<M3trik.Unitytk.Interaction.ClipPlayer>();
            Assert.That(Player().Find("B01_1").speed, Is.EqualTo(2f), "the row's take, at its row's speed");
            Assert.That(Player().Find("B01_2").speed, Is.EqualTo(3f), "a row of the step's own beside it under the same empty target keeps its own");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, again);
            Assert.That(again.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "a second build changes nothing");

            // An object whose row is its own (a take of its own) keeps its take as its own: it is that row.
            Assert.That(ManifestsActions.SetRowField(_task, "B01", bolt, "use", "clip", "B01_1", row: 3), Is.True);
            Assert.That(ManifestsActions.Offered(_task, "B01", bolt, "use", 3), Is.SupersetOf(take), "its row is the object's own");
            Assert.That(ManifestsActions.Offered(_task, "B01", bolt, "use", 3, wholeRow: true), Is.Empty);
            Assert.That(ManifestsActions.SetTune(_task, "B01", bolt, "use", "use.speed", "4", row: 3), Is.True);
            ManifestsActions.Build(_task, new OpContext());
            Assert.That(Player().Find("B01_1").speed, Is.EqualTo(4f), "its own row's take, at its own speed");
        }

        [Test]
        public void A_helper_a_person_moves_is_kept_listed_on_its_row_and_put_back_by_a_revert()
        {
            // A moved arrow was kept by the next build (then in a tuning file of its own), but the manifest listed no adjustment for
            // it and offered no way back short of forgetting every one.
            _settings.debugTools = false;
            ManifestsActions.Build(_task, new OpContext());
            PlacedSpot spot = Object.FindObjectsByType<PlacedSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                    .First(each => each.key.StartsWith("indicate/"));
            Vector3 placed = spot.transform.position;
            spot.transform.position += Vector3.up * 0.25f;

            Assert.That(ManifestsActions.Adjustments(_task).Select(a => (a.part, a.field)), Is.EqualTo(new[] { ((Component)spot, "place") }),
                        "listed, before any build keeps it");
            ManifestAssessment quiet = ManifestsActions.Assess(_task);
            Assert.That(quiet.steps.SelectMany(s => s.rows).Where(r => r.NeedsBuild), Is.Empty, "a moved helper is no work for a build");
            var rows = quiet.steps.SelectMany(s => s.rows).Where(r => r.adjusted.Any(a => a.part == spot)).ToList();
            Assert.That(rows.Select(r => r.behavior), Is.EqualTo(new[] { "indicate" }), "said on the row that placed it");
            Assert.That(rows[0].Status, Is.EqualTo(ManifestStatus.Adjusted));
            Assert.That(rows[0].parts, Does.Contain(spot), "and held by it: Select, Edit and Revert reach it");

            ManifestsActions.Build(_task, new OpContext());
            Assert.That(Vector3.Distance(spot.transform.position, placed + Vector3.up * 0.25f), Is.LessThan(1e-4f), "a build keeps it there");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, again);
            Assert.That(again.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "and a second build changes nothing");

            Assert.That(ManifestsActions.RevertAdjustments(_task, spot, new OpContext()), Is.EqualTo(1));
            Assert.That(Vector3.Distance(spot.transform.position, placed), Is.LessThan(1e-4f), "a revert puts it back where the build placed it");
            Assert.That(ManifestsActions.Adjustments(_task), Is.Empty);
            var settled = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, settled);
            Assert.That(settled.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "the scene stands as built again");
        }

        [Test]
        public void Place_again_drops_a_moved_spots_keep_and_its_first_placement_and_the_next_build_searches_anew()
        {
            // Place Again was tested only for a spot no one had moved: that the move its manifest kept goes with it, so the
            // next build puts the spot where the search finds it and not back where the person had it, was never seen.
            _settings.debugTools = false;
            ManifestsActions.Build(_task, new OpContext());
            PlacedSpot spot = Arrow();
            Transform arrow = spot.transform;
            string key = spot.key;
            Vector3 placed = arrow.position;
            arrow.position += Vector3.up * 0.25f;  // a person moves it
            ManifestsActions.Build(_task, new OpContext());
            Assert.That(_task.moved.Select(v => v.name), Does.Contain(key), "premise: its manifest keeps the move");

            var dry = new OpContext(dryRun: true);
            Assert.That(ManifestsActions.PlaceAgain(_task, spot, dry), Is.EqualTo(1));
            Assert.That(_task.moved.Select(v => v.name), Does.Contain(key), "a dry run plans it, and changes nothing");
            Assert.That(ManifestsActions.PlaceAgain(_task, spot, new OpContext()), Is.EqualTo(1));
            Assert.That(_task.moved.Select(v => v.name), Has.No.Member(key), "the move a person made of it goes");
            Assert.That(arrow.GetComponent<PlacedSpot>() == null, "and its first placement");

            ManifestsActions.Build(_task, new OpContext());
            PlacedSpot again = arrow.GetComponent<PlacedSpot>();
            Assert.That(again != null && again.key == key, "placed anew, under its key");
            Assert.That(again.Adjusted, Is.False, "where the search put it");
            Assert.That(Vector3.Distance(arrow.position, placed), Is.LessThan(1e-3f), "found as first found, the scene as it was: not where it was moved");
            Assert.That(_task.moved.Select(v => v.name), Has.No.Member(key), "and nothing of the move comes back");
        }

        [Test]
        public void A_pictures_field_of_view_set_on_its_own_camera_is_its_rows_kept_into_a_new_scene()
        {
            // The picture's camera was bound to its row's field of view, but no test set one on the camera and built again:
            // that the next build keeps a person's angle as the row's, and a scene built anew gives it again, was never seen.
            _settings.debugTools = false;
            const string door = "CABINET|DOOR_GRP";
            Assert.That(ManifestsActions.SetAssigned(_task, "A02", door, "picture", true, new OpContext()), Is.True, "a picture of the door, added here");
            ManifestsActions.Build(_task, new OpContext());
            string picture = TaskKit.PictureName(ManifestsActions.Plan(_task, out _), _settings);
            Camera Lens() => GameObject.Find(picture).transform.Find("A02").GetComponent<M3trik.Unitytk.Panels.PipShot>().from.GetComponent<Camera>();
            Assert.That(Lens().fieldOfView, Is.EqualTo(M3trik.Unitytk.Panels.PipView.DefaultFieldOfView).Within(1e-4f), "premise: the view's");
            Lens().fieldOfView = 35f;  // as its inspector sets it

            ManifestsActions.Build(_task, new OpContext());
            Assert.That(_task.TuneOf("A02", door, "picture")?.Get("picture.fieldOfView"), Is.EqualTo("35"), "kept as the row's own");
            Assert.That(Lens().fieldOfView, Is.EqualTo(35f).Within(1e-4f), "and the camera keeps it");
            var settled = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, settled);
            Assert.That(settled.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "a second build changes nothing");

            ManifestsActions.Reset(_task, discard: false, new OpContext());
            Assert.That(Lens().fieldOfView, Is.EqualTo(35f).Within(1e-4f), "a scene built anew gives it again");
        }

        [Test]
        public void A_tools_fit_set_on_the_tool_after_a_build_is_kept_listed_on_its_rows_carried_and_put_back()
        {
            // The real task's screwdriver was rigged across its screws, its pivot at its middle: a rig's fault no build can
            // know, so a person's to set, and after a build that is the tool's own tip fields. Set there, they stood in the
            // scene, but no row held the tool: the manifest listed nothing, offered no way back, and a scene built anew
            // lost them.
            _settings.debugTools = false;
            ManifestsActions.Build(_task, new OpContext());
            Interaction.Holdable tool = Object.FindObjectsByType<Interaction.Holdable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                              .OrderBy(each => each.name).First();
            string name = tool.name;
            Vector3 tip = new Vector3(0f, 0f, 0.111f), turn = new Vector3(0f, -90f, 0f);
            Assert.That((tool.tipPosition, tool.tipRotation), Is.EqualTo((Vector3.zero, Vector3.zero)), "as rigged: nothing fits it yet");
            tool.tipPosition = tip;  // as its inspector sets them
            tool.tipRotation = turn;

            Assert.That(ManifestsActions.Adjustments(_task).Where(a => a.part == tool).Select(a => a.field),
                        Is.EquivalentTo(new[] { nameof(Interaction.Holdable.tipPosition), nameof(Interaction.Holdable.tipRotation) }),
                        "listed, before any build keeps it");
            ManifestAssessment quiet = ManifestsActions.Assess(_task);
            Assert.That(quiet.steps.SelectMany(s => s.rows).Where(r => r.NeedsBuild), Is.Empty, "a fitted tool is no work for a build");
            var rows = quiet.steps.SelectMany(s => s.rows).Where(r => r.adjusted.Any(a => a.part == tool)).ToList();
            Assert.That(rows, Is.Not.Empty, "said on the rows that put it in hand or work with it");
            Assert.That(rows.Select(r => r.Status).Distinct(), Is.EqualTo(new[] { ManifestStatus.Adjusted }));

            ManifestsActions.Build(_task, new OpContext());
            Assert.That((tool.tipPosition, tool.tipRotation), Is.EqualTo((tip, turn)), "a build keeps it");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, again);
            Assert.That(again.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "and a second build changes nothing");

            ManifestsActions.Reset(_task, discard: false, new OpContext());
            tool = Object.FindObjectsByType<Interaction.Holdable>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(each => each.name == name);
            Assert.That((tool.tipPosition, tool.tipRotation), Is.EqualTo((tip, turn)), "carried into a scene built anew");
            Assert.That(ManifestsActions.RevertAdjustments(_task, tool, new OpContext()), Is.EqualTo(2));
            Assert.That((tool.tipPosition, tool.tipRotation), Is.EqualTo((Vector3.zero, Vector3.zero)), "a revert gives it what the build gave it");
            Assert.That(ManifestsActions.Adjustments(_task), Is.Empty);
        }

        [Test]
        public void A_manifests_build_prepares_the_twins_of_the_fades_and_highlights_it_makes_as_planned_and_once()
        {
            Assert.That(_twins, Does.EndWith("/cabinet.twins.asset"), "named for the plan, beside it");
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, dry);
            Assert.That(File.Exists(_twins), Is.False, "a dry run writes nothing");
            var live = new OpContext();
            ManifestsActions.Build(_task, live);
            // The twins' part of the plan: what it writes into the table, and the effects it hands the table to.
            System.Func<OpContext, string[]> twinsOf = ctx => ctx.Changes
                .Where(c => c.target == _twins || c.action.StartsWith("give it the twins", System.StringComparison.Ordinal))
                .Select(c => $"{c.kind} {c.target}: {c.action}").ToArray();
            Assert.That(twinsOf(live), Is.Not.Empty);
            Assert.That(twinsOf(live), Is.EqualTo(twinsOf(dry)), "the plan is the run, the twins of the effects it only planned with it");
            var table = AssetDatabase.LoadAssetAtPath<RenderEffects.MaterialTwins>(_twins);
            Assert.That(table, Is.Not.Null);
            Assert.That(table.twins, Is.Not.Empty);
            var made = ManifestsActions.Placed(_task.delivery).GetComponentsInChildren<RenderEffects.SubtreeEffect>(true);
            Assert.That(made, Is.Not.Empty, "the task glows or fades something");
            Assert.That(made.Where(e => e.twins == null).Select(e => e.name), Is.Empty, "every effect it made has its twins");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, again);
            Assert.That(again.Changes, Is.Empty, "a second build changes nothing");
        }

        [Test]
        public void A_manifests_build_gives_a_scene_with_no_rig_its_user_as_planned_and_once()
        {
            var plan = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, plan);  // the delivery is not placed yet: the plan is to place it
            ManifestsActions.Build(_task, new OpContext());
            Camera eyes = Camera.main;
            Assert.That(eyes, Is.Not.Null, "an empty scene: the user's eyes made");
            var traveler = eyes.GetComponent<Viewpoints.Traveler>();
            Assert.That(traveler, Is.Not.Null, "the steps' viewpoints move it");
            Assert.That(traveler.eyeHeight, Is.EqualTo(_settings.eyeHeight));
            Interaction.Hand hand = Object.FindObjectsByType<Interaction.Hand>(FindObjectsSortMode.None).Single();
            Assert.That(hand.transform.parent, Is.SameAs(eyes.transform), "a hand in its view");
            Assert.That(hand.transform.localPosition, Is.EqualTo(_settings.handInView));
            Assert.That(eyes.nearClipPlane, Is.EqualTo(Interaction.Interactions.HeldNearClip), "near enough to see what it holds");
            // It starts before the task's first viewpoint, facing the way that does.
            Transform front = ManifestsActions.Placed(_task.delivery).GetComponentsInChildren<Transform>(true).First(t => t.name == "VIEW_FRONT");
            Vector3 back = front.position - front.forward * _settings.startBack;
            Assert.That(new Vector2(eyes.transform.position.x, eyes.transform.position.z),
                        Is.EqualTo(new Vector2(back.x, back.z)).Using(UnityEngine.TestTools.Utils.Vector2EqualityComparer.Instance));
            Assert.That(Vector3.Dot(eyes.transform.forward, front.forward), Is.GreaterThan(0.99f));

            eyes.transform.position += Vector3.right;  // a person moves their camera
            Vector3 moved = eyes.transform.position;
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_task, again);
            Assert.That(again.Changes, Is.Empty, "the scene has its rig: nothing to do");
            ManifestsActions.Build(_task, new OpContext());
            Assert.That(eyes.transform.position, Is.EqualTo(moved), "and it stays where it was put");
        }

        [Test]
        public void The_rig_is_planned_as_it_is_made_and_a_scene_with_its_own_or_asking_for_none_gets_none()
        {
            GameObject placed = (GameObject)PrefabUtility.InstantiatePrefab(_task.delivery);

            // No camera in the scene: the plan is the run's, name for name.
            var dry = new OpContext(dryRun: true);
            Assert.That(TaskKit.MakeRig(Scene(placed), dry), Is.Null, "a dry run makes nothing");
            Assert.That(Camera.main, Is.Null);
            Assert.That(dry.Changes.Select(c => c.target).Take(3), Is.EqualTo(new[] { Viewpoints.ViewpointsActions.EyesName, "Main Camera/Traveler", "Main Camera/HAND" }));
            var live = new OpContext();
            Camera eyes = TaskKit.MakeRig(Scene(placed), live);
            Assert.That(eyes, Is.SameAs(Camera.main));
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(TaskKit.MakeRig(Scene(placed), new OpContext()), Is.Null, "the scene has its rig now");

            // A scene with a rig of its own: left as it is.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            placed = (GameObject)PrefabUtility.InstantiatePrefab(_task.delivery);
            new GameObject("XR Origin").AddComponent<Viewpoints.Traveler>();
            var own = new OpContext(dryRun: true);
            Assert.That(TaskKit.MakeRig(Scene(placed), own), Is.Null);
            Assert.That(own.Changes, Is.Empty);

            // And one whose settings ask for none.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            placed = (GameObject)PrefabUtility.InstantiatePrefab(_task.delivery);
            _settings.rig = TaskRig.None;
            var none = new OpContext(dryRun: true);
            Assert.That(TaskKit.MakeRig(Scene(placed), none), Is.Null);
            Assert.That(none.Changes, Is.Empty);
        }

        ManifestScene Scene(GameObject placed)
            => new ManifestScene(placed) { Manifest = _task };

        [Test]
        public void A_build_with_its_rig_and_debug_tools_plans_what_it_then_does()
        {
            // The rig's start was asked by the camera's name, which its near clip's change had planned already: a dry run
            // took it as planned and left it out. Added: 2026-11-24
            GameObject placed = (GameObject)PrefabUtility.InstantiatePrefab(_task.delivery);
            SheetPlan.Shape.Payload plan = ManifestsActions.Plan(_task, out string refusal);
            Assert.That(plan, Is.Not.Null, refusal);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, Scene(placed), dry);
            Assert.That(Camera.main, Is.Null, "a dry run makes nothing");
            var live = new OpContext();
            ManifestsActions.Build(plan, Scene(placed), live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(Camera.main.transform.position, Is.Not.EqualTo(Vector3.zero), "the rig made, and stood before the first viewpoint");
        }

        [Test]
        public void A_task_kept_as_an_asset_resets_though_the_new_scene_unloads_it()
        {
            const string asset = "Assets/TaskBuildTests task.asset";
            var saved = Object.Instantiate(_task);
            saved.hideFlags = HideFlags.None;  // an ordinary asset: a new scene unloads it, nothing in that scene holding it
            AssetDatabase.CreateAsset(saved, asset);
            try
            {
                ManifestAssessment built = ManifestsActions.Reset(AssetDatabase.LoadAssetAtPath<Manifest>(asset), discard: true, new OpContext());
                Assert.That(built, Is.Not.Null, "read again after the new scene");
                Assert.That(built.rows.All(r => r.state == ManifestAssessment.State.Ready), built.Summary());
            }
            finally
            {
                AssetDatabase.DeleteAsset(asset);
            }
        }

        [Test]
        public void A_built_task_says_what_each_row_step_and_the_task_holds()
        {
            ManifestsActions.Build(_task, new OpContext());
            ManifestAssessment assessed = ManifestsActions.Assess(_task);

            Assert.That(assessed.Plan().Any, Is.False, assessed.Plan().Text());
            Assert.That(assessed.rows.Select(r => r.Status).Distinct(), Is.SubsetOf(new[] { ManifestStatus.Valid, ManifestStatus.Warned }));
            foreach (ManifestAssessment.Row row in assessed.rows.Where(r => r.behavior == "highlight"))
                Assert.That(row.parts.OfType<M3trik.Unitytk.RenderEffects.Highlight>(), Is.Not.Empty, $"{row.step}: a highlight row holds its highlight");
            foreach (ManifestAssessment.Row row in assessed.rows.Where(r => r.behavior == "indicate"))
                Assert.That(row.parts.OfType<M3trik.Unitytk.Indicators.Indicator>(), Is.Not.Empty, $"{row.step}: an arrow row holds its arrow");
            Assert.That(assessed.rows.Any(r => r.behavior == "highlight") && assessed.rows.Any(r => r.behavior == "indicate"), "the sample has both");
            foreach (ManifestAssessment.Step step in assessed.steps)
            {
                Assert.That(step.parts.OfType<M3trik.Unitytk.Steps.Step>().Select(s => s.id), Is.EqualTo(new[] { step.id }), "a step holds its own step");
                Assert.That(step.parts.OfType<M3trik.Unitytk.Panels.PlacardPage>(), Is.Not.Empty, $"{step.id}: and its page");
            }
            Assert.That(assessed.shared.parts.OfType<M3trik.Unitytk.Steps.Sequence>().Count(), Is.EqualTo(1), "the task holds its sequence");
            Assert.That(assessed.shared.parts.OfType<M3trik.Unitytk.Panels.Placard>().Count(), Is.EqualTo(1), "its placard");
            Assert.That(assessed.shared.parts.OfType<M3trik.Unitytk.Steps.SequenceDebugCommands>().Count(), Is.EqualTo(1), "and its debug tools");

            PlacedSpot arrow = Arrow();
            arrow.transform.position += Vector3.up * 0.2f;  // a person moves it
            ManifestAssessment moved = ManifestsActions.Assess(_task);
            Assert.That(moved.rows.Select(r => r.Status), Has.None.EqualTo(ManifestStatus.Update), "an adjustment is kept, never to rebuild");
            Assert.That(moved.shared.changes.Select(c => c.action), Is.EqualTo(new[] { "keep 1 moved helper" }),
                        "the build would keep it in the manifest: " + string.Join("; ", moved.shared.changes.Select(c => $"{c.kind} {c.target}: {c.action}")));
            Assert.That(_task.moved, Is.Empty, "an assessment keeps nothing");
        }

        [Test]
        public void An_arrow_at_a_tool_goes_once_the_tool_is_taken()
        {
            ManifestsActions.Build(_task, new OpContext());
            var taken = Object.FindObjectsByType<M3trik.Unitytk.Interaction.Holdable>(FindObjectsSortMode.None)
                              .Where(h => h.GetComponentsInChildren<M3trik.Unitytk.Indicators.Indicator>(true).Length > 0).ToList();
            Assert.That(taken, Is.Not.Empty, "the sample points an arrow at a tool");
            foreach (var held in taken)
            {
                var arrow = held.GetComponentsInChildren<M3trik.Unitytk.Indicators.Indicator>(true).First();
                bool wired = Enumerable.Range(0, held.onTaken.GetPersistentEventCount())
                                       .Any(i => held.onTaken.GetPersistentTarget(i) == arrow && held.onTaken.GetPersistentMethodName(i) == "set_enabled");
                Assert.That(wired, $"{held.name}: taking it switches its arrow off");
            }
        }

        [Test]
        public void Two_plans_built_into_one_scene_each_keep_only_their_own_adjustments()
        {
            // Two tasks with the same step IDs in one scene: a spot's key names its step, not its plan, so each task's
            // manifest must keep only the spots its own build made. Added: 2026-11-24
            var made = new System.Collections.Generic.List<Object>();
            (Manifest manifest, ManifestScene scene) Task(string name, float x)
            {
                var root = new GameObject($"cabinet_{name}");
                root.transform.position = new Vector3(x, 0f, 0f);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "FLOOR";
                floor.transform.SetParent(root.transform, false);
                floor.transform.localPosition = new Vector3(0f, -0.05f, 0f);
                floor.transform.localScale = new Vector3(8f, 0.1f, 8f);
                var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
                door.name = "DOOR_GRP";
                door.transform.SetParent(root.transform, false);
                door.transform.localPosition = new Vector3(0f, 1f, 0f);
                door.transform.localScale = Vector3.one * 0.4f;
                var settings = ScriptableObject.CreateInstance<TaskSettings>();
                settings.rig = TaskRig.None;
                var manifest = ScriptableObject.CreateInstance<Manifest>();
                manifest.settings.Add(settings);
                made.AddRange(new Object[] { root, settings, manifest });
                var plan = SheetPlan.Read("{\"version\": 2, \"source\": {\"sheet\": \"" + name + ".csv\"}, \"steps\": [{\"id\": \"A01\", \"actions\": [" +
                                          "{\"behavior\": \"travel\", \"text\": \"The user is brought to face the door\", \"targets\": [\"DOOR_GRP\"]}]}]}",
                                          name, out string refusal);
                Assert.That(refusal, Is.Null);
                var scene = new ManifestScene(root) { Manifest = manifest };
                ManifestsActions.Build(plan, scene, new OpContext());
                return (manifest, scene);
            }
            PlacedSpot Spot(ManifestScene scene)
                => SceneRoots.Find(TaskKit.ViewpointsName(scene)).transform.Find(TaskKit.ViewpointName("A01")).GetComponent<PlacedSpot>();
            var a = Task("a", 0f);
            var b = Task("b", 20f);
            try
            {
                PlacedSpot spotA = Spot(a.scene), spotB = Spot(b.scene);
                Assert.That((spotA.key, spotB.key), Is.EqualTo(("travel/A01", "travel/A01")), "one key in both tasks");
                spotA.transform.position += Vector3.right * 0.5f;  // a person moves each
                spotB.transform.position += Vector3.forward * 1f;
                new TaskBuild().Keep(a.manifest, a.scene, new OpContext());
                Placements.Tuning kept = TaskScene.Kept(a.manifest, out _);
                Assert.That(b.manifest.moved, Is.Empty, "b's manifest untouched");
                Assert.That(kept.spots.Select(s => s.key), Is.EqualTo(new[] { "travel/A01" }));
                Assert.That(Vector3.Distance(kept.spots[0].position, spotA.Relative.position), Is.LessThan(1e-4f), "task a's own spot, not b's");
            }
            finally
            {
                foreach (var scene in new[] { a.scene, b.scene })
                {
                    GameObject views = SceneRoots.Find(TaskKit.ViewpointsName(scene)), sequence = SceneRoots.Find(TaskKit.SequenceName(scene));
                    if (views != null) Object.DestroyImmediate(views);
                    if (sequence != null) Object.DestroyImmediate(sequence);
                }
                foreach (Object each in made) Object.DestroyImmediate(each);
            }
        }
    }
}
