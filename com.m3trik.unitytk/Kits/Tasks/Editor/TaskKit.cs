using M3trik.Unitytk.Manifests;
using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;
using M3trik.Unitytk.Placement;
using M3trik.Unitytk.Steps;
using M3trik.Unitytk.Viewpoints;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("M3trik.Unitytk.Kits.Tasks.Tests")]

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// The Tasks kit's recipes: what makes a sheet's plan a task. The kit registers the
    /// behaviors a task's rows read as (<see cref="TaskBehaviorAttribute"/>: take, use,
    /// teleport, a placard's pages...), each built through the features' own actions, and builds
    /// what they share around them (<see cref="TaskBuild"/>: the sequence the steps run in, the
    /// task's placard, its viewpoints and voices). The manifest runs them
    /// (<see cref="ManifestsActions"/>: assess, build, again on every change of the sheet or the
    /// delivery); what is here is what a behavior or a project's script asks of the kit: the names
    /// of what it makes, a step's reading time, its debug tools.
    /// </summary>
    /// <remarks>
    /// Prior art: task authoring tools (VR Builder and its kin) keep a procedure as a graph
    /// in their own editor, saved as typed .NET objects (Newtonsoft <c>TypeNameHandling.All</c>)
    /// over their own scene components and rig, and their guidance, LMS and desktop parts are
    /// paid tiers; Unity's own Tutorial Framework (<c>com.unity.learn.iet-framework</c>) walks a
    /// person through the Editor, its pages and their completion criteria authored by hand as
    /// assets: it teaches the editor, not a scene a player plays; Timeline and Visual Scripting
    /// author a sequence in Unity by hand. The studio writes its tasks as sequence sheets, and
    /// the Shot Manifest already builds a sheet's shots in the DCC, so the plan is read from the
    /// sheet (by the same parser), and the scene is built from it the way the DCC side is: assess,
    /// plan, apply, again on every change of the sheet or the delivery. A behavior the kit cannot
    /// build yet is reported, never skipped silently.
    /// </remarks>
    public static class TaskKit
    {
        /// <summary>A step's sound the scene does not hold (its plan names a file not staged beside the plan).</summary>
        public const string SoundCode = "unity.task.sound";

        /// <summary>The plan's extension for the kit (<see cref="Extension"/>) holds data the kit does not read.</summary>
        public const string ExtensionCode = "unity.task.extension";

        /// <summary>The issue a spot the kit places raises where none does its job (no spot on the floor sees what a teleport faces).</summary>
        public const string SpotCode = "unity.task.spot";

        /// <summary>The issue a moved helper's place its manifest keeps raises where it reads as none (a hand edit): kept as it is, it puts nothing back.</summary>
        public const string KeptCode = "unity.task.kept";

        /// <summary>The note a fade the task leaves out raises (an object its step returns goes back shown: <see cref="TaskSettings.fadeReturned"/>).</summary>
        public const string FadeReturnedCode = "unity.task.fade_returned";

        /// <summary>A row acts on a target with another thing in hand, or with none, than the target asks for in every step (the last a row names).</summary>
        public const string OneToolCode = "unity.task.one_tool";

        /// <summary>A viewpoint a row names faces away from what the row says the user is brought to face: the user would stand with it behind them.</summary>
        public const string FacesAwayCode = "unity.task.faces_away";

        /// <summary>A pattern setting of the task (<see cref="TaskSettings.anchorPattern"/>, <see cref="TaskSettings.pipAnchorPattern"/>, <see cref="TaskSettings.captionPattern"/>) that is no regular expression: it matches nothing.</summary>
        public const string PatternCode = "unity.task.pattern";

        /// <summary>A <c>play</c> row with no row before it in its step whose clip it can follow.</summary>
        public const string FollowsNothingCode = "unity.task.play_follows_nothing";

        /// <summary>The value the plan gives a question's right answer (an <c>option</c> row's <c>value</c>).</summary>
        public const string Correct = "correct";

        /// <summary>
        /// The kit's name in a plan's extensions (<see cref="SheetPlan.Extension"/>): what a sheet's settings file would give
        /// the kit (its <c>extensions.task</c>). The kit reads nothing there, and says what it finds
        /// (<see cref="ExtensionCode"/>): a behavior builds the ideal setup, and what a person adjusts from it (a tool's tip
        /// offset, its travel time, a spot) is adjusted in the scene, on the part, and kept by the manifest.
        /// </summary>
        public const string Extension = "task";

        // Say what the plan's extension for the kit holds, which the kit does not read: once a run, as it begins.
        internal static void SayUnread(SheetPlan.Shape.Payload plan, OpContext ctx)
        {
            IDictionary<string, object> data = plan.Extension(Extension);
            if (data == null) return;
            foreach (string key in data.Keys)
                ctx.Raise(ExtensionCode, key == "tools"
                    ? "the settings file's task tools are not read: a tool rigged off its work is adjusted on the tool after a build " +
                      "(its Holdable's Tip Position and Tip Rotation), which the manifest keeps; take them out of the settings file"
                    : $"the settings file's task extension holds '{key}', which the kit does not read: take it out");
        }

        // How the kit wires a step's rows, by number: raised when it comes to wire the same rows another way (2: a cue
        // at what the user acts on is switched by a TargetCue, where the step switched it itself).
        const string Wiring = "3";

        // What the kit marks a step as built from (StepsActions.BuiltFrom): its rows as the manifest builds them, and the
        // way the kit wires rows. So a scene an earlier kit built has every step wired anew once, and nothing that kit
        // had a step switch stays behind to be switched twice.
        internal static string BuiltFrom(string rows) => string.IsNullOrEmpty(rows) ? rows : $"{rows} #wiring {Wiring}";

        // The plan's values the kit reads by name: a row's (its clause template's groups) and a step's (the sheet's reading).
        internal static class Fields
        {
            internal const string Tool = "tool", Facing = "facing", Side = "side", SideOf = "side_of", Arrows = "arrows",
                From = "from", Faces = "faces", Label = "label", Value = "value", Voice = "voice", Placard = "placard", Sound = "sound",
                Helpers = "helpers";
        }

        /// <summary>
        /// How long <paramref name="step"/>, going on by itself, runs at least in <paramref name="scene"/>:
        /// <see cref="DwellOf(SheetPlan.Shape.PlanStep, TaskSettings)"/>, or its sound's length and
        /// the task's <see cref="TaskSettings.voiceTail"/> where the scene holds a longer one.
        /// </summary>
        /// <param name="step">The plan's step.</param>
        /// <param name="scene">The delivery, which finds the step's sound (<see cref="ManifestScene.Asset{T}"/>).</param>
        public static float DwellOf(SheetPlan.Shape.PlanStep step, ManifestScene scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene), "name the delivery (its settings and sounds)");
            TaskSettings settings = scene.Settings<TaskSettings>();
            float reading = DwellOf(step, settings);
            AudioClip sound = scene.Asset<AudioClip>(step.Field(Fields.Sound));
            return sound == null ? reading : Mathf.Max(reading, sound.length + settings.voiceTail);
        }

        /// <summary>
        /// How long <paramref name="step"/>, going on by itself, runs at least: its voice line (else its
        /// placard) read at the task's <see cref="TaskSettings.wordsPerSecond"/>, and no less than its
        /// <see cref="TaskSettings.minimumDwell"/>.
        /// </summary>
        /// <param name="step">The plan's step.</param>
        /// <param name="settings">The task's settings.</param>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public static float DwellOf(SheetPlan.Shape.PlanStep step, TaskSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings), "name the task's settings (ManifestScene.Settings<TaskSettings>())");
            string voice = step.Field(Fields.Voice);
            string said = string.IsNullOrWhiteSpace(voice) ? step.Field(Fields.Placard) : voice;
            int words = string.IsNullOrWhiteSpace(said) ? 0 : said.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
            return Mathf.Max(settings.minimumDwell, words / Mathf.Max(settings.wordsPerSecond, 0.1f));
        }

        // -- the names of what the task makes ------------------------------------------------

        /// <summary>
        /// The name of the sequence <paramref name="plan"/>'s steps run in, in the scene's top level:
        /// <c>Task (&lt;task&gt;)</c>, or <c>Task</c> for a plan that names no task (its settings file, else its sheet)
        /// (<see cref="TaskNames.sequence"/>).
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="settings">The task's settings (null: the defaults).</param>
        public static string SequenceName(SheetPlan.Shape.Payload plan, TaskSettings settings = null)
            => Owned(plan, Named(settings, n => n.sequence));

        /// <summary><see cref="SequenceName(SheetPlan.Shape.Payload, TaskSettings)"/> of <paramref name="scene"/>'s plan, by its settings.</summary>
        /// <param name="scene">The delivery.</param>
        public static string SequenceName(ManifestScene scene) => SequenceName(PlanOf(scene), scene.Settings<TaskSettings>());

        /// <summary>
        /// The name of <paramref name="plan"/>'s placard, in the scene's top level: <c>Placard (&lt;task&gt;)</c>,
        /// or <c>Placard</c> for a plan that names no task (its settings file, else its sheet) (<see cref="TaskNames.placard"/>).
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="settings">The task's settings (null: the defaults).</param>
        public static string PlacardName(SheetPlan.Shape.Payload plan, TaskSettings settings = null)
            => Owned(plan, Named(settings, n => n.placard));

        /// <summary><see cref="PlacardName(SheetPlan.Shape.Payload, TaskSettings)"/> of <paramref name="scene"/>'s plan, by its settings.</summary>
        /// <param name="scene">The delivery.</param>
        public static string PlacardName(ManifestScene scene) => PlacardName(PlanOf(scene), scene.Settings<TaskSettings>());

        /// <summary>
        /// The name of <paramref name="plan"/>'s picture-in-picture, in the scene's top level: <c>PiP (&lt;task&gt;)</c>,
        /// or <c>PiP</c> for a plan that names no task (its settings file, else its sheet) (<see cref="TaskNames.pip"/>).
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="settings">The task's settings (null: the defaults).</param>
        public static string PipName(SheetPlan.Shape.Payload plan, TaskSettings settings = null)
            => Owned(plan, Named(settings, n => n.pip));

        /// <summary><see cref="PipName(SheetPlan.Shape.Payload, TaskSettings)"/> of <paramref name="scene"/>'s plan, by its settings.</summary>
        /// <param name="scene">The delivery.</param>
        public static string PipName(ManifestScene scene) => PipName(PlanOf(scene), scene.Settings<TaskSettings>());

        /// <summary>
        /// The name of the top-level object holding <paramref name="plan"/>'s step sounds: <c>Voice (&lt;task&gt;)</c>,
        /// or <c>Voice</c> for a plan that names no task (its settings file, else its sheet) (<see cref="TaskNames.voice"/>).
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="settings">The task's settings (null: the defaults).</param>
        public static string VoiceName(SheetPlan.Shape.Payload plan, TaskSettings settings = null)
            => Owned(plan, Named(settings, n => n.voice));

        /// <summary><see cref="VoiceName(SheetPlan.Shape.Payload, TaskSettings)"/> of <paramref name="scene"/>'s plan, by its settings.</summary>
        /// <param name="scene">The delivery.</param>
        public static string VoiceName(ManifestScene scene) => VoiceName(PlanOf(scene), scene.Settings<TaskSettings>());

        /// <summary>
        /// The name of the scene's top-level object holding the viewpoints the kit places (a row that
        /// <see cref="TaskBehaviorAttribute.Teleports"/> and says only what the user faces):
        /// <c>Viewpoints (&lt;task&gt;)</c>, or <c>Viewpoints</c> for a plan that names no task (its settings file, else its sheet)
        /// (<see cref="TaskNames.viewpoints"/>).
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="settings">The task's settings (null: the defaults).</param>
        public static string ViewpointsName(SheetPlan.Shape.Payload plan, TaskSettings settings = null)
            => Owned(plan, Named(settings, n => n.viewpoints));

        /// <summary><see cref="ViewpointsName(SheetPlan.Shape.Payload, TaskSettings)"/> of <paramref name="scene"/>'s plan, by its settings.</summary>
        /// <param name="scene">The delivery.</param>
        public static string ViewpointsName(ManifestScene scene) => ViewpointsName(PlanOf(scene), scene.Settings<TaskSettings>());

        /// <summary>The name of the viewpoint the kit places for step <paramref name="id"/>: <c>VIEW_&lt;id&gt;</c> (<see cref="TaskNames.viewpoint"/>).</summary>
        /// <param name="id">The step's ID.</param>
        /// <param name="settings">The task's settings (null: the defaults).</param>
        public static string ViewpointName(string id, TaskSettings settings = null) => Named(settings, n => n.viewpoint) + id;

        /// <summary>
        /// The name of the viewpoint the kit places for <paramref name="teleport"/>, a row of <paramref name="step"/> that names
        /// none: its own helper's name as every tool names it (<c>VIEW_&lt;step&gt;_&lt;target&gt;</c>, the plan's
        /// <c>helpers</c>: the Shot Manifest makes it under that name); a plan read from the old sentence format carries
        /// none, and gets <see cref="ViewpointName(string, TaskSettings)"/>, with <c>_&lt;n&gt;</c> after it for the step's
        /// <c>n</c>th such teleport past its first (one as the step ends beside one as it starts: a viewpoint each).
        /// </summary>
        /// <param name="step">The row's step.</param>
        /// <param name="teleport">The row.</param>
        /// <param name="settings">The task's settings (null: the defaults).</param>
        public static string ViewpointName(SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction teleport, TaskSettings settings = null)
        {
            string own = Helper(teleport, 0);
            if (own != null) return own;
            int at = Spots.PlacedAt(step, teleport);
            return ViewpointName(step.id, settings) + (at > 1 ? $"_{at}" : "");
        }

        /// <summary>
        /// The name of <paramref name="action"/>'s own helper for its target at <paramref name="index"/> as every tool names
        /// it (the plan's <c>helpers</c>, the reader's <c>ptk.ManifestPlacement.helper_name</c>); null where the plan
        /// carries none (the row names its helper, or was read from the old sentence format).
        /// </summary>
        /// <param name="action">A placing row.</param>
        /// <param name="index">Its target's index.</param>
        public static string Helper(SheetPlan.Shape.PlanAction action, int index)
        {
            List<string> names = action == null ? null : action.FieldList(Fields.Helpers);
            return names != null && index >= 0 && index < names.Count && !string.IsNullOrEmpty(names[index]) ? names[index] : null;
        }

        /// <summary>
        /// The name of the debug panel <see cref="AddDebugTools(SheetPlan.Shape.Payload, TaskSettings, OpContext)"/> makes,
        /// in the scene's top level: <c>Debug (&lt;task&gt;)</c>, or <c>Debug</c> for a plan that names no task (its settings file, else its sheet)
        /// (<see cref="TaskNames.debug"/>).
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="settings">The task's settings (null: the defaults).</param>
        public static string DebugName(SheetPlan.Shape.Payload plan, TaskSettings settings = null)
            => Owned(plan, Named(settings, n => n.debug));

        /// <summary><see cref="DebugName(SheetPlan.Shape.Payload, TaskSettings)"/> of <paramref name="scene"/>'s plan, by its settings.</summary>
        /// <param name="scene">The delivery.</param>
        public static string DebugName(ManifestScene scene) => DebugName(PlanOf(scene), scene.Settings<TaskSettings>());

        /// <summary>The name of the list step <paramref name="step"/>'s checklist row makes on the placard: <c>&lt;step&gt; checklist</c>.</summary>
        /// <param name="step">The step's ID.</param>
        public static string ChecklistName(string step) => $"{step} checklist";

        /// <summary>
        /// The signal a selection of <paramref name="node"/> sends the task's sequence (<c>select:&lt;node&gt;</c>, the node
        /// by its path under the delivery, as the plan names it), which the step its row is in awaits. The delivery's own
        /// name is no part of it: a re-export under a new file name is the same task, and renames no signal.
        /// </summary>
        /// <param name="scene">The delivery.</param>
        /// <param name="node">The target's object.</param>
        /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="node"/> is null.</exception>
        public static string SelectedSignal(ManifestScene scene, GameObject node)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene), "name the delivery");
            if (node == null) throw new ArgumentNullException(nameof(node), "name the target's object");
            return $"select:{scene.Under(node.transform)}";
        }

        // A name of the task's settings (null: the defaults'), else the default's where a person emptied it.
        static string Named(TaskSettings settings, Func<TaskNames, string> name)
        {
            string own = settings != null && settings.names != null ? name(settings.names) : null;
            return string.IsNullOrEmpty(own) ? name(new TaskNames()) : own;
        }

        // The name of a top-level object the task makes: <what> (<task>), the task named by its settings file (what
        // its manifest and plan are named by too), else by its sheet; <what> for a plan that names neither. Never by the sheet
        // where a settings file is: a sheet renamed or replaced is the same task, and a build finds what it made by name.
        static string Owned(SheetPlan.Shape.Payload plan, string what)
        {
            string task = plan == null || plan.source == null ? null
                : !string.IsNullOrEmpty(plan.source.settings) ? plan.source.settings : plan.source.sheet;
            return string.IsNullOrEmpty(task) ? what : $"{what} ({System.IO.Path.GetFileNameWithoutExtension(task)})";
        }

        static SheetPlan.Shape.Payload PlanOf(ManifestScene scene)
            => scene != null ? scene.Plan : throw new ArgumentNullException(nameof(scene), "name the delivery (new ManifestScene(root))");

        // -- what the build makes around the rows ----------------------------------------------

        // Each step with a sound plays it while it runs (its cue under the task's Voice object);
        // a sound the scene does not hold is raised, the step built without it. The same line delivered
        // in a take (the Shot Manifest keys a step's narration onto its first take) is left to the step,
        // so it plays once, as the step starts: every step's that plays it, a step this run leaves as it
        // stands (locked, or another than those it builds) by the cue it has.
        internal static void BuildVoices(SheetPlan.Shape.Payload plan, ManifestScene scene, OpContext ctx)
        {
            string owner = VoiceName(scene);
            var voiced = new List<string>();
            foreach (var step in plan.steps)
            {
                string file = step.Field(Fields.Sound);
                if (string.IsNullOrEmpty(file)) continue;
                if (!scene.Builds(step.id))
                {
                    string kept = CuedSound(owner, step.id);
                    if (kept != null) voiced.Add(kept);
                    continue;
                }
                using (scene.For(step.id))
                {
                    AudioClip sound = scene.Asset<AudioClip>(file);
                    if (sound == null)
                    {
                        ctx.Raise(SoundCode, $"{step.id}: no sound at '{file}': stage it beside the plan", "warning");
                        continue;
                    }
                    voiced.Add(sound.name);
                    AnimationAudio.SoundCue cue = AnimationAudio.AnimationAudioActions.AddSoundCue(owner, step.id, sound, ctx: ctx);
                    if (cue != null) scene.EnableDuring(step.id, cue.gameObject, cue, ctx);
                    else scene.EnableDuring<AnimationAudio.SoundCue>(step.id, PlannedCue(owner, step.id), ctx);
                }
            }
            foreach (GameObject root in scene.Roots)
                if (root != null) AnimationAudio.AnimationAudioActions.Leave(root, voiced, ctx);
        }

        // The name of the sound step id's cue under owner plays (an earlier build's); null where it has none.
        static string CuedSound(string owner, string id)
        {
            GameObject root = SceneRoots.Find(owner);
            Transform node = root == null ? null : root.transform.Find(id);
            AnimationAudio.SoundCue cue = node == null ? null : node.GetComponent<AnimationAudio.SoundCue>();
            return cue == null || cue.clip == null ? null : cue.clip.name;
        }

        // The sound cue AddSoundCue makes for step id under owner, as a dry run that only planned it names it.
        static PlannedObject PlannedCue(string owner, string id)
        {
            GameObject root = SceneRoots.Find(owner);
            return root != null ? new PlannedObject(id, root.transform) : new PlannedObject(id, new PlannedObject(owner));
        }

        /// <summary>The delivery's picture-in-picture screen (<see cref="TaskSettings.pipAnchorPattern"/>); null when it has none.</summary>
        /// <param name="scene">The delivery.</param>
        /// <param name="ctx">The run's context, told of a pattern that does not parse (<see cref="PatternCode"/>).</param>
        public static Transform PipAnchor(ManifestScene scene, OpContext ctx = null)
        {
            TaskSettings settings = scene.Settings<TaskSettings>();
            foreach (Transform node in Matching(scene, Pattern(scene, nameof(settings.pipAnchorPattern), settings.pipAnchorPattern, ctx))) return node;
            return null;
        }

        /// <summary>
        /// <paramref name="pattern"/>, the task's setting <paramref name="setting"/>, as a regular expression, parsed once a
        /// run of <paramref name="scene"/> for each setting: one that does not parse is raised (<see cref="PatternCode"/>, naming
        /// the setting, each setting that holds it) and matches nothing.
        /// </summary>
        /// <param name="scene">The delivery, whose run keeps what it parsed.</param>
        /// <param name="setting">The setting's name, for the issue.</param>
        /// <param name="pattern">The setting's value; empty: none.</param>
        /// <param name="ctx">The run's context, told of one that does not parse.</param>
        /// <param name="options">How it matches (a caption's ignores case).</param>
        /// <returns>The expression; null where the pattern is empty or does not parse.</returns>
        public static System.Text.RegularExpressions.Regex Pattern(ManifestScene scene, string setting, string pattern, OpContext ctx = null,
                                                                   System.Text.RegularExpressions.RegexOptions options = System.Text.RegularExpressions.RegexOptions.None)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            var known = scene.State<TaskState>().Patterns;
            string key = $"{setting}:{(int)options}:{pattern}";  // by setting too: the same text in two settings is said of each
            if (known.TryGetValue(key, out var parsed)) return parsed;
            try
            {
                parsed = new System.Text.RegularExpressions.Regex(pattern, options);
            }
            catch (ArgumentException e)  // what the setting says is no regular expression: said, and it matches nothing
            {
                ctx.Raise(PatternCode, $"the task's {setting} '{pattern}' is no regular expression ({e.Message}): correct it in its settings", "warning");
                parsed = null;
            }
            known[key] = parsed;
            return parsed;
        }

        // Every node of the delivery (its extras' too) whose name matches pattern; none for no pattern.
        static IEnumerable<Transform> Matching(ManifestScene scene, System.Text.RegularExpressions.Regex pattern)
        {
            if (pattern == null) yield break;
            foreach (GameObject root in scene.Roots)
                foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                    if (pattern.IsMatch(node.name)) yield return node;
        }

        // The delivery's authoring anchors (the task's anchorPattern) are places, not things to see: their meshes hidden.
        internal static void HideAnchors(ManifestScene scene, OpContext ctx)
        {
            TaskSettings settings = scene.Settings<TaskSettings>();
            foreach (Transform node in Matching(scene, Pattern(scene, nameof(settings.anchorPattern), settings.anchorPattern, ctx)))
                Placements.HideAnchor(node.gameObject, ctx);
        }

        // The viewpoints the kit places (rows that teleport naming none), in plan order, before any row is
        // built: every other spot (an arrow, a picture's) is placed from where the user then stands.
        internal static void PlaceViewpoints(SheetPlan.Shape.Payload plan, ManifestScene scene, ManifestAssessment assessment, OpContext ctx)
        {
            foreach (var (step, action, assessed) in PlanOrder.Assessed(plan, assessment))
                if (assessed.state == ManifestAssessment.State.Ready && TaskBehaviors.Traits(action.behavior).Teleports
                    && Spots.Placed(action) && scene.Builds(step.id))
                    using (scene.For(step.id))
                        Spots.Viewpoint(scene, step, action, ctx);
            // A viewpoint an earlier build placed for a step that its rows no longer place under that name (its teleport
            // names its own now, or is gone, or its helper's name changed) goes: nothing brings the user there, and it stood
            // about in the scene looking like a place of the task's. Each is known by the key it was placed under.
            TaskSettings settings = scene.Settings<TaskSettings>();
            string holder = ViewpointsName(scene);
            GameObject root = SceneRoots.Find(holder);
            if (root == null) return;
            foreach (var step in plan.steps)
            {
                if (!scene.Builds(step.id)) continue;
                var placed = new HashSet<string>(StringComparer.Ordinal);
                if (step.actions != null)
                    foreach (var action in step.actions)
                        if (action != null && TaskBehaviors.Traits(action.behavior).Teleports && Spots.Placed(action))
                            placed.Add(ViewpointName(step, action, settings));
                var gone = new List<string>();
                foreach (Transform view in root.transform)
                    if (!placed.Contains(view.name) && (view.TryGetComponent(out PlacedSpot spot) && Spots.IsTeleportKey(spot.key, step)
                                                         || view.name == ViewpointName(step.id, settings)))
                        gone.Add(view.name);
                foreach (string name in gone) ViewpointsActions.RemoveViewpoint(holder, name, scene.Root.scene, ctx);
            }
        }

        /// <summary>
        /// Give the scene a user where it has none (<see cref="TaskSettings.rig"/>): its main camera (one made
        /// where it has none, <see cref="ViewpointsActions.AddEyes"/>) gets a <see cref="Traveler"/>, so the steps'
        /// viewpoints move it, its eyes at the settings' height (<see cref="ViewpointsActions.AddTraveler(GameObject, float, OpContext)"/>);
        /// a hand in its view, each input the project has, and a near clip plane close enough to see what it holds
        /// (<see cref="Interaction.Interactions.EquipUser(Camera, Vector3, float, OpContext)"/>). A scene with a rig of its
        /// own, which it declares by one that travels (anything with a Traveler: a headset's origin given one), is left as it
        /// is, and so is one whose settings ask for none.
        /// </summary>
        /// <param name="scene">The delivery, whose scene gets the rig.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The user's camera; null where the scene has a rig, none is asked for, or a dry run plans its camera.</returns>
        public static Camera MakeRig(ManifestScene scene, OpContext ctx = null)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene), "name the delivery (new ManifestScene(root))");
            TaskSettings settings = scene.Settings<TaskSettings>();
            if (settings.rig == TaskRig.None) return null;
            Scene home = scene.Root.scene;
            foreach (Traveler each in UnityEngine.Object.FindObjectsByType<Traveler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (each.gameObject.scene == home) return null;  // the scene's own rig
            Camera eyes = ViewpointsActions.AddEyes(home, ViewpointsActions.EyesName, ctx);
            TaskState state = scene.State<TaskState>();
            if (eyes == null)
            {
                if (ctx == null || !ctx.DryRun) return null;
                // A dry run's: the eyes are planned, a top-level object of the scene: what its live run puts on them.
                state.Rig = new PlannedObject(ViewpointsActions.EyesName, home);
                ViewpointsActions.AddTraveler(state.Rig, ctx);
                Interaction.Interactions.EquipUser(state.Rig, settings.nearClip, ctx);
                return null;
            }
            state.Rig = new PlannedObject(eyes.name, eyes.transform.parent);
            ViewpointsActions.AddTraveler(eyes.gameObject, settings.eyeHeight, ctx);
            Interaction.Interactions.EquipUser(eyes, settings.handInView, settings.nearClip, ctx);
            return eyes;
        }

        // A rig this build made starts a little behind the task's first viewpoint, facing the way it
        // does, so its marker shows ahead; a rig the scene had stays where a person put it. A dry run, the rig
        // or the viewpoint's travel only planned, plans the move its live run makes.
        internal static void StartRig(SheetPlan.Shape.Payload plan, ManifestScene scene, OpContext ctx)
        {
            PlannedObject rig = scene.State<TaskState>().Rig;
            if (rig == null) return;
            float back = scene.Settings<TaskSettings>().startBack;
            foreach (var (step, action, assessed) in PlanOrder.Assessed(plan, scene.Assessment))
            {
                if (assessed.state != ManifestAssessment.State.Ready || !TaskBehaviors.Traits(action.behavior).Teleports) continue;
                var (made, planned) = Spots.Travel(scene, step, action);
                Traveler traveler = TravelerOf(scene, rig);
                // A dry run that only planned to move the viewpoint (a spot placed anew) plans the rig's move after it:
                // where the viewpoint stands now, the rig may already be.
                Transform view = Spots.Existing(scene, step, action);
                bool moving = view != null && Placement.Placements.Moving(view.gameObject, ctx);
                if (traveler != null && made != null && !moving) ViewpointsActions.GoTo(traveler, made, back, ctx);
                else if (made != null) ViewpointsActions.GoTo(rig, made, back, ctx);
                else if (ctx.Planned("create", OpContextEdits.Key(planned, typeof(TravelTo))))  // its builder planned the travel
                    ViewpointsActions.GoTo(rig, planned, back, ctx);
                return;
            }
        }

        // The traveler on rig in scene's own scene; null where none is (a dry run's, only planned).
        static Traveler TravelerOf(ManifestScene scene, PlannedObject rig)
        {
            foreach (Traveler each in UnityEngine.Object.FindObjectsByType<Traveler>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (each.gameObject.scene == scene.Root.scene && OpContextEdits.Label(each.gameObject) == rig.Path) return each;
            return null;
        }

        /// <summary><see cref="AddDebugTools(SheetPlan.Shape.Payload, TaskSettings, OpContext)"/> by the default names.</summary>
        /// <param name="plan">The plan the task was built from.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The task's commands; null in a dry run that would make them.</returns>
        public static SequenceDebugCommands AddDebugTools(SheetPlan.Shape.Payload plan, OpContext ctx = null) => AddDebugTools(plan, null, ctx);

        /// <summary>
        /// Give the task <paramref name="plan"/> built its debug tools, for trying it in the editor or reviewing it in a
        /// headset: a debug panel beside the user's eyes (<see cref="DebugName(SheetPlan.Shape.Payload, TaskSettings)"/>,
        /// <see cref="PanelsActions.MakeDebugPanel"/>) with the task's commands (<see cref="SequenceDebugCommands"/>: the
        /// step it is on, Skip, Back, Restart, Autopilot) and an autopilot, off, that plays the task by itself when
        /// switched on (<see cref="StepsActions.AddDebugCommands(GameObject, string, OpContext)"/>); the Interaction feature's
        /// actor acts for the autopilot (<see cref="Interaction.Interactions.AddStepActor(GameObject, OpContext)"/>), and the
        /// panel is the surface the commands go on. A shipped
        /// task leaves them out (<see cref="TaskSettings.debugTools"/> off, or never call this).
        /// </summary>
        /// <param name="plan">The plan the task was built from.</param>
        /// <param name="settings">The task's settings, which name what it makes (null: the defaults).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The task's commands; null in a dry run that would make them.</returns>
        public static SequenceDebugCommands AddDebugTools(SheetPlan.Shape.Payload plan, TaskSettings settings, OpContext ctx = null)
        {
            string name = DebugName(plan, settings);
            DebugPanel panel = PanelsActions.MakeDebugPanel(name, ctx);
            if (panel == null)
            {
                // A dry run's: the panel is planned, a top-level object of its name: what its live run puts on it.
                var planned = new PlannedObject(name);
                StepsActions.AddDebugCommands(planned, ctx);
                Interaction.Interactions.AddStepActor(planned, ctx);
                return null;
            }
            SequenceDebugCommands commands = StepsActions.AddDebugCommands(panel.gameObject, SequenceName(plan, settings), ctx);
            Interaction.Interactions.AddStepActor(panel.gameObject, ctx);
            return commands;
        }

        // Every ready row's tool and every take or hold row's targets made holdable before any row is built,
        // so a clip added by an early row lends every holdable it moves, however late its own row comes.
        internal static void MakeHoldables(SheetPlan.Shape.Payload plan, ManifestScene scene, ManifestAssessment assessment, OpContext ctx)
        {
            foreach (var (step, action, assessed) in PlanOrder.Assessed(plan, assessment))
            {
                if (assessed.state != ManifestAssessment.State.Ready || !scene.Builds(step.id)) continue;
                using (scene.For(step.id))
                {
                    string tool = action.Field(Fields.Tool);
                    if (!string.IsNullOrEmpty(tool)) Interaction.Interactions.MakeHoldable(scene.Node(tool).gameObject, ctx);
                    if (TaskBehaviors.Traits(action.behavior).Holds)
                        foreach (string target in action.targets)
                            Interaction.Interactions.MakeHoldable(scene.Node(target).gameObject, ctx);
                }
            }
        }

    }
}
