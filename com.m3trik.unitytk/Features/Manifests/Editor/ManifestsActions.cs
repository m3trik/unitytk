using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// The Manifests feature's actions: a sheet's plan built into the scene holding its delivery,
    /// one registered builder (<see cref="IManifestBehavior"/>) per row, the kits whose rows it holds
    /// building around them (<see cref="IManifestKit"/>). <see cref="Assess(SheetPlan.Shape.Payload, ManifestScene, OpContext)"/>
    /// says where every row stands and changes nothing; <see cref="Build(SheetPlan.Shape.Payload, ManifestScene, OpContext, IEnumerable{string})"/>
    /// brings the scene in line. The same over a <see cref="Manifest"/> asset (its plan, delivery,
    /// scene, locked steps and settings), which also places the delivery, keeps a person's
    /// adjustments and builds anew in a fresh scene (<see cref="Reset"/>). Each takes an optional
    /// <see cref="OpContext"/>: a dry run plans, a menu's run is one undo step, and none just runs;
    /// a second build changes nothing.
    /// </summary>
    /// <remarks>
    /// Prior art: the producer's own manifest builds a sheet's shots where the delivery is made the same
    /// way (assess, plan, apply, again on every change of the sheet or the scene), and its assessment is its
    /// dry run; this is that loop for a Unity scene, over the plan its reader writes. Authoring tools that
    /// keep a procedure in their own graph editor (VR Builder and its kin) save typed .NET objects
    /// over their own scene components; Timeline and Visual Scripting author a sequence by hand. A
    /// sheet is what a studio already writes, so the scene is built from it, and a row no builder
    /// has is reported, never skipped silently.
    /// </remarks>
    public static class ManifestsActions
    {
        /// <summary>The issue a manifest that cannot be built raises (no plan, no delivery, a setup that failed).</summary>
        public const string ManifestCode = "unity.manifest.manifest";
        /// <summary>A plan action's behavior has no builder in this editor.</summary>
        public const string UnsupportedCode = "unity.manifest.unsupported";
        /// <summary>A plan action names a node or take the scene lacks, or could not be read.</summary>
        public const string UnresolvedCode = "unity.manifest.unresolved";
        /// <summary>An object's own value refused: a name that is no setting of its behavior's kit, or no field its row may set.</summary>
        public const string SettingCode = "unity.manifest.setting";
        /// <summary>What a row's build says of it, built anyway (a setting its kit's settings lack, left out): a note on the row.</summary>
        public const string NoteCode = "unity.manifest.note";

        // -- a manifest's parts --------------------------------------------------------------

        /// <summary>The manifest's plan, read; null where it has none or cannot be read (the reason in <paramref name="refusal"/>).</summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="refusal">Why there is no plan; null when there is one.</param>
        public static SheetPlan.Shape.Payload Plan(Manifest manifest, out string refusal)
        {
            refusal = null;
            if (manifest == null)
            {
                // A reference Unity has unloaded or destroyed (an asset nothing held as a scene was replaced).
                refusal = "the manifest is no longer loaded: load it again by its path (AssetDatabase.LoadAssetAtPath)";
                return null;
            }
            if (manifest.plan == null)
            {
                refusal = "the manifest names no plan: set its Plan (the JSON the sheet's reader writes)";
                return null;
            }
            string path = AssetDatabase.GetAssetPath(manifest.plan);
            if (string.IsNullOrEmpty(path)) return Read(manifest.plan, path, out refusal);  // no asset (a script's, a test's): read each time
            // Read once per import of the plan's file: an assessment, the window and a build each ask for it, and a large
            // plan's parse is most of an assessment's time.
            Hash128 imported = AssetDatabase.GetAssetDependencyHash(path);
            int id = manifest.plan.GetInstanceID();
            if (Plans.TryGetValue(id, out var known) && known.imported == imported)
            {
                refusal = known.refusal;
                return known.plan;
            }
            SheetPlan.Shape.Payload read = Read(manifest.plan, path, out refusal);
            Plans[id] = (imported, read, refusal);
            return read;
        }

        // Each plan asset read (by its instance), with the import it was read from: a cache, rebuilt after a domain reload.
        // What a build makes of a plan is a copy (Effective), so the plan read is never changed.
        static readonly Dictionary<int, (Hash128 imported, SheetPlan.Shape.Payload plan, string refusal)> Plans =
            new Dictionary<int, (Hash128, SheetPlan.Shape.Payload, string)>();

        // The plan asset at path, read; a sheet itself (a CSV picked where its plan goes) is refused with how to stage it.
        static SheetPlan.Shape.Payload Read(TextAsset plan, string path, out string refusal)
        {
            if (IsSheet(path))
            {
                refusal = $"'{path}' is a sheet, not its plan: stage it from its settings file " +
                          $"({SyncCommand("<its settings file>", ProjectFolder, Path.GetDirectoryName(path).Replace('\\', '/'))}), " +
                          "then set Plan to the plan it writes";
                return null;
            }
            return SheetPlan.Read(plan.text, path, out refusal);
        }

        /// <summary>The asset folder the files the manifest's plan names are under (a step's sound): its own, else its plan's folder.</summary>
        /// <param name="manifest">The manifest.</param>
        public static string AssetFolder(Manifest manifest)
        {
            if (manifest == null) return "";
            if (!string.IsNullOrEmpty(manifest.assetFolder)) return manifest.assetFolder;
            string plan = manifest.plan != null ? AssetDatabase.GetAssetPath(manifest.plan) : "";
            return string.IsNullOrEmpty(plan) ? "" : Path.GetDirectoryName(plan).Replace('\\', '/');
        }

        /// <summary>The instance of <paramref name="model"/> at the open scene's top level; null where none is placed.</summary>
        /// <param name="model">A model asset (an imported FBX).</param>
        public static GameObject Placed(GameObject model)
        {
            if (model == null) return null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (PrefabUtility.GetCorrespondingObjectFromSource(root) == model) return root;
            return null;
        }

        /// <summary>The manifest's delivery as the open scene holds it; null where its model is not placed there.</summary>
        /// <param name="manifest">The manifest.</param>
        public static ManifestScene SceneOf(Manifest manifest)
        {
            GameObject root = manifest == null ? null : Placed(manifest.delivery);
            if (root == null) return null;
            var extras = new List<GameObject>();
            foreach (GameObject extra in manifest.extras)
            {
                GameObject placed = Placed(extra);
                if (placed != null) extras.Add(placed);
            }
            return new ManifestScene(root, extras) { Manifest = manifest, AssetFolder = AssetFolder(manifest) };
        }

        /// <summary>
        /// A manifest for <paramref name="plan"/>, made beside it (<c>&lt;plan&gt;.manifest.asset</c>): its
        /// delivery the model in the plan's folder (else the project) with the file name the plan was
        /// read against, its scene <c>&lt;plan&gt;.unity</c> beside it. Where the plan was read against
        /// several files (a delivery and its extras, named in order: <c>a.fbx, b.fbx</c>), the first is its
        /// delivery and each other one found is an extra. The one there already is returned as it is.
        /// </summary>
        /// <param name="plan">The plan's asset (the JSON the sheet's reader writes).</param>
        /// <param name="ctx">The run's context: asked before the asset is written.</param>
        /// <returns>The manifest; null where the plan cannot be read (an issue says why) or a dry run plans it.</returns>
        public static Manifest Create(TextAsset plan, OpContext ctx = null)
        {
            string planPath = plan != null ? AssetDatabase.GetAssetPath(plan) : "";
            if (string.IsNullOrEmpty(planPath))
            {
                ctx.Raise(ManifestCode, "that is no plan asset: pick the JSON the sheet's reader writes, under Assets", "error");
                return null;
            }
            SheetPlan.Shape.Payload read = Read(plan, planPath, out string refusal);
            if (read == null)
            {
                ctx.Raise(ManifestCode, refusal, "error", planPath);
                return null;
            }
            string stem = Stem(planPath);
            string path = stem + ".manifest.asset";
            var known = AssetDatabase.LoadAssetAtPath<Manifest>(path);
            if (known != null) return known;
            if (ctx != null && !ctx.ShouldWrite(path, "make the plan's manifest")) return null;
            var made = ScriptableObject.CreateInstance<Manifest>();
            made.plan = plan;
            // Read against several files (a delivery, then its extras, named in order), the first is its delivery.
            string folder = Path.GetDirectoryName(planPath).Replace('\\', '/');
            string[] files = DeliveryFiles(read.source != null ? read.source.delivery : null);
            made.delivery = files.Length > 0 ? Model(files[0], folder) : null;
            for (int i = 1; i < files.Length; i++)
            {
                GameObject extra = Model(files[i], folder);
                if (extra != null) made.extras.Add(extra);
            }
            made.scene = stem + ".unity";
            AssetDatabase.CreateAsset(made, path);
            if (made.delivery == null)
                ctx.Raise(ManifestCode, $"no model named '{(files.Length > 0 ? files[0] : "")}' is in the project: " +
                                        "import the delivery, then set the manifest's Delivery");
            return made;
        }

        /// <summary>
        /// The manifest's plan's path without its extensions (<c>Assets/lamp.plan.json</c>: <c>Assets/lamp</c>): what each
        /// file named for the plan is named from (its manifest, its scene, a kit's own file beside it). Empty where it names
        /// no plan asset.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        public static string PlanStem(Manifest manifest)
        {
            string plan = manifest != null && manifest.plan != null ? AssetDatabase.GetAssetPath(manifest.plan) : "";
            return string.IsNullOrEmpty(plan) ? "" : Stem(plan);
        }

        // A plan file's path without its extensions (lamp.plan.json -> lamp).
        static string Stem(string planPath)
        {
            string stem = Path.ChangeExtension(planPath, null);
            return stem.EndsWith(".plan", StringComparison.Ordinal) ? stem.Substring(0, stem.Length - ".plan".Length) : stem;
        }

        /// <summary>
        /// The file names a plan says it was read against (<c>source.delivery</c>), in order: one, or a delivery and
        /// its extras as the sheet's reader names them together (<c>a.fbx, b.fbx</c>).
        /// </summary>
        /// <param name="delivery">The plan's delivery (null or empty: none).</param>
        internal static string[] DeliveryFiles(string delivery)
            => (delivery ?? "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

        // The model (or prefab) asset with file name, in folder first, then anywhere in the project; null where none is.
        static GameObject Model(string file, string folder)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string name = Path.GetFileNameWithoutExtension(file);
            GameObject anywhere = null;
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:GameObject"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileName(path), file, StringComparison.OrdinalIgnoreCase)) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (Path.GetDirectoryName(path).Replace('\\', '/') == folder) return model;
                if (anywhere == null) anywhere = model;
            }
            return anywhere;
        }

        // -- the delivery the plan was read against -----------------------------------------------

        // Each model the plan was read against (source.delivery: its delivery, then its extras in order) that the manifest
        // does not build yet, with the one it builds in its place and its place: a re-export under a new file name (a dated
        // one) staged and read again. None where the plan names none, the manifest builds none there yet (Create sets it, or
        // a person does), or the project holds no model of the name.
        static List<(GameObject was, GameObject now, int index)> Moved(Manifest manifest, SheetPlan.Shape.Payload plan)
        {
            var moved = new List<(GameObject, GameObject, int)>();
            if (manifest == null || plan == null || plan.source == null) return moved;
            string planPath = manifest.plan != null ? AssetDatabase.GetAssetPath(manifest.plan) : "";
            string folder = string.IsNullOrEmpty(planPath) ? "" : Path.GetDirectoryName(planPath).Replace('\\', '/');
            string[] files = DeliveryFiles(plan.source.delivery);
            for (int i = 0; i < files.Length; i++)
            {
                GameObject was = i == 0 ? manifest.delivery : i - 1 < manifest.extras.Count ? manifest.extras[i - 1] : null;
                if (was == null || string.Equals(Path.GetFileName(AssetDatabase.GetAssetPath(was)), files[i], StringComparison.OrdinalIgnoreCase)) continue;
                GameObject now = Model(files[i], folder);
                if (now != null && now != was) moved.Add((was, now, i));
            }
            return moved;
        }

        // A manifest builds into the delivery its plan was read against (Moved): it moves over to the new one, and the
        // instance the open scene holds of the old one is swapped over (Unity's Replace Prefab Asset, each node matched by its
        // place in the hierarchy), so what a build and a person put on its nodes stays; a node the new one lacks loses it,
        // which the export's hierarchy check keeps from shipping.
        static void Follow(Manifest manifest, SheetPlan.Shape.Payload plan, OpContext ctx)
        {
            foreach (var (was, now, index) in Moved(manifest, plan))
            {
                // Each asked apart, before either is done: a dry run plans both, as the live run makes both.
                bool into = ctx == null || ctx.ShouldChange(manifest, $"build into {now.name}, the model its plan was read against (was {was.name})");
                GameObject placed = Placed(was);
                bool over = placed != null && (ctx == null || ctx.ShouldChange(placed, $"swap it over to {now.name}, keeping what is on its nodes"));
                string staged = Staged(manifest, was);
                // Kept while anything else places it (a scene saved with it that is not open, another manifest beside it): a
                // missing model there loses every instance and what was put on it, and a deleted asset is not undone.
                string used = staged == null ? null : StillPlaced(manifest, was, placed, staged);
                if (used != null)
                    ctx.Raise(ManifestCode, $"the staged copy {staged} is kept: {used} still places it. Delete it, with its media folder, " +
                                            "once nothing does", "info", staged);
                bool drop = staged != null && used == null && (ctx == null || ctx.ShouldDelete(staged, SupersededAction));
                if (!into) continue;
                if (over)
                {
                    string before = placed.name;
                    var swap = new PrefabReplacingSettings
                    {
                        objectMatchMode = ObjectMatchMode.ByHierarchy,
                        prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides,
                    };
                    PrefabUtility.ReplacePrefabAssetOfPrefabInstance(placed, now, swap, ctx != null ? InteractionMode.UserAction : InteractionMode.AutomatedAction);
                    ctx.Edited(placed);
                    // Its root is renamed for the new model: what a person adjusted on its parts, and what the build gave
                    // them, go with it, or a re-export would lose every adjustment.
                    ManifestAdjustments.Rename(manifest, before, placed.name);
                }
                if (index == 0) manifest.delivery = now;
                else manifest.extras[index - 1] = now;
                ctx.Edited(manifest);
                if (drop && Placed(was) == null)  // nothing open places it still (a swap refused leaves it)
                {
                    string media = Path.Combine(Path.GetDirectoryName(staged), Path.GetFileNameWithoutExtension(staged) + "_Media").Replace(Path.DirectorySeparatorChar, '/');
                    AssetDatabase.DeleteAsset(staged);
                    if (AssetDatabase.IsValidFolder(media)) AssetDatabase.DeleteAsset(media);
                }
            }
        }

        // The staged copy of was, a model the manifest moves off: its path where it was staged beside the plan (the reader's
        // copy of a file the settings name, which staging again brings back); null for a model of the project's own
        // elsewhere, which is never touched. It goes with its media folder once nothing open places it.
        static string Staged(Manifest manifest, GameObject was)
        {
            string path = AssetDatabase.GetAssetPath(was), plan = manifest.plan != null ? AssetDatabase.GetAssetPath(manifest.plan) : "";
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(plan)) return null;
            return string.Equals(Path.GetDirectoryName(path), Path.GetDirectoryName(plan), StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        const string SupersededAction = "the staged copy of the model the manifest moved off, with its media folder (staging again brings it back)";

        // What places was (its staged copy at staged) besides placed, the instance a move swaps over: an object of any open
        // scene, the manifest's own scene saved with it where that is not open, another manifest beside it that builds it;
        // null where nothing does.
        static string StillPlaced(Manifest manifest, GameObject was, GameObject placed, string staged)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (!open.isLoaded) continue;
                foreach (GameObject root in open.GetRootGameObjects())
                    foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                        if (node.gameObject != placed && PrefabUtility.GetCorrespondingObjectFromSource(node.gameObject) == was)
                            return $"the open scene '{(string.IsNullOrEmpty(open.path) ? open.name : open.path)}'";
            }
            string saved = manifest.scene;
            if (!string.IsNullOrEmpty(saved) && !SceneManager.GetSceneByPath(saved).isLoaded
                && AssetDatabase.LoadAssetAtPath<SceneAsset>(saved) != null && AssetDatabase.GetDependencies(saved, true).Contains(staged))
                return $"its scene '{saved}'";
            string folder = Path.GetDirectoryName(staged).Replace('\\', '/');
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(Manifest), new[] { folder }))
            {
                var other = AssetDatabase.LoadAssetAtPath<Manifest>(AssetDatabase.GUIDToAssetPath(guid));
                if (other != null && other != manifest && (other.delivery == was || other.extras.Contains(was)))
                    return $"the manifest '{other.name}'";
            }
            return null;
        }

        // What an assessment says of a model the plan was read against that the manifest does not build yet (Moved).
        static void SayMoved(ManifestAssessment assessment, Manifest manifest, SheetPlan.Shape.Payload plan)
        {
            if (assessment == null) return;
            foreach (var (was, now, _) in Moved(manifest, plan))
                assessment.planIssues.Add($"the plan was read against {now.name}, and the manifest builds {was.name}: " +
                                          "a build moves it over, keeping what is on its nodes");
        }

        // -- what the plan was read from ---------------------------------------------------------

        /// <summary>
        /// Whether what the manifest's plan was read from has changed since: the sheet's settings file,
        /// the sheet itself or the project's own clause templates (their digests, kept in the plan), or a
        /// delivery file beside the settings that is newer than, and differs from, its staged copy beside the plan. False where the plan
        /// does not say where it was read from (<see cref="SheetFile"/>): nothing can be told.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="why">What changed, in a sentence; where nothing did but not all of it could be told (a file another program
        /// holds, a settings file this editor cannot read), what was left unchecked and why; empty when nothing changed.</param>
        public static bool Stale(Manifest manifest, out string why)
        {
            why = "";
            SheetPlan.Shape.Payload plan = Plan(manifest, out _);
            string file = SheetFile(manifest, plan);
            if (plan == null || plan.source == null || string.IsNullOrEmpty(file) || !File.Exists(file)) return false;
            try
            {
                return Changed(manifest, plan, file, ref why);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                why = $"what the plan was read from cannot be read now ({e.Message}): whether it changed is told once it can be";
                return false;
            }
        }

        // Stale's comparison, over files that can be read.
        static bool Changed(Manifest manifest, SheetPlan.Shape.Payload plan, string file, ref string why)
        {
            if (IsSheet(file)) return Differs(file, plan.source.sheet_digest, "sheet", ref why);
            if (Differs(file, plan.source.settings_digest, "sheet's settings", ref why)) return true;
            SheetSettings.Shape.Payload settings = SettingsOf(file, out string unread);
            if (settings == null)
            {
                why = $"the sheet's settings ({Path.GetFileName(file)}) cannot be read here ({unread}): its sheet, its clause templates " +
                      "and its delivery's files are unchecked";
                return false;
            }
            string folder = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(settings.sheet) && !settings.sheet.Contains("://")
                && Differs(Path.Combine(folder, settings.sheet), plan.source.sheet_digest, "sheet", ref why)) return true;
            if (!string.IsNullOrEmpty(plan.source.clauses)
                && ClausesDigest(Path.Combine(folder, plan.source.clauses)) != (plan.source.clauses_digest ?? ""))
            {
                why = $"the sheet's clause templates ({plan.source.clauses}) changed since the plan was read";
                return true;
            }
            string planPath = manifest.plan != null ? AssetDatabase.GetAssetPath(manifest.plan) : "";
            string staged = string.IsNullOrEmpty(planPath) ? "" : Path.GetDirectoryName(planPath);
            foreach (List<string> files in new[] { settings.deliveries, settings.extras })
            {
                if (files == null) continue;
                foreach (string name in files)
                {
                    string source = string.IsNullOrEmpty(name) ? "" : Path.Combine(folder, name);
                    string copy = string.IsNullOrEmpty(source) || string.IsNullOrEmpty(staged) ? "" : Path.Combine(staged, Path.GetFileName(source));
                    if (!File.Exists(source) || !File.Exists(copy)) continue;
                    if (File.GetLastWriteTimeUtc(source) <= File.GetLastWriteTimeUtc(copy)) continue;  // staged since: the same file
                    if (Same(source, copy)) continue;
                    why = $"the delivery's file {Path.GetFileName(source)} changed since it was staged";
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Where the manifest's sheet settings file (or its sheet) is, as a full path: the manifest's own
        /// <see cref="Manifest.sheet"/>, else the path its plan was staged with; a path that is not rooted
        /// is the project folder's. Empty where neither says.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        public static string SheetFile(Manifest manifest) => manifest == null ? "" : SheetFile(manifest, Plan(manifest, out _));

        // A sheet itself, not its settings file: a CSV (a settings file is JSON under any name).
        static bool IsSheet(string file) => file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

        static string SheetFile(Manifest manifest, SheetPlan.Shape.Payload plan)
        {
            if (manifest == null) return "";
            string named = manifest.sheet;
            if (string.IsNullOrEmpty(named)) named = plan == null || plan.source == null ? "" : plan.source.settings;
            if (string.IsNullOrEmpty(named)) return "";
            return Path.GetFullPath(Path.IsPathRooted(named) ? named : Path.Combine(ProjectFolder, named));
        }

        // This project's folder (the one holding Assets), with forward slashes.
        static string ProjectFolder => Path.GetDirectoryName(Path.GetFullPath(Application.dataPath)).Replace('\\', '/');

        // The sheet reader's operation that stages a sheet into a project, and its arguments in order: as its catalog
        // (unitytk/operations.json) declares them, which test_sheet_sync.py holds these to.
        internal const string SyncOperation = "sheet.sync";
        internal static readonly string[] SyncArguments = { "settings", "project", "folder" };

        /// <summary>
        /// The command that reads the manifest's sheet again and stages it into this project, beside its
        /// plan (the sheet's reader's <c>sheet.sync</c>, run in a terminal); empty where the manifest does
        /// not say where its sheet's settings file is.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        public static string SyncCommand(Manifest manifest)
            => SyncOf(manifest, out string settings, out string folder) ? SyncCommand(settings, ProjectFolder, folder) : "";

        // What the manifest's sheet is read again from and staged to: its settings file and its plan's folder; false where
        // it does not say where its settings file is.
        static bool SyncOf(Manifest manifest, out string settings, out string folder)
        {
            settings = SheetFile(manifest).Replace('\\', '/');
            folder = "";
            if (settings.Length == 0 || IsSheet(settings)) return false;
            string plan = manifest.plan != null ? AssetDatabase.GetAssetPath(manifest.plan) : "";
            folder = string.IsNullOrEmpty(plan) ? "" : Path.GetDirectoryName(plan).Replace('\\', '/');
            return true;
        }

        /// <summary>
        /// The command that reads a sheet and stages it into a project (the sheet's reader's <c>sheet.sync</c>, run in a
        /// terminal), each argument given where it is not empty.
        /// </summary>
        /// <param name="settings">The sheet's settings file.</param>
        /// <param name="project">The project's folder.</param>
        /// <param name="folder">The asset folder to stage it to; empty: the reader's own.</param>
        public static string SyncCommand(string settings, string project, string folder = "")
        {
            var command = new System.Text.StringBuilder(SyncProgram);
            foreach (string argument in SyncArgs(settings, project, folder))
                command.Append(' ').Append(argument.Contains("=") ? $"\"{argument}\"" : argument);
            return command.ToString();
        }

        // The program the reader runs in, and its arguments for sheet.sync after it: each of SyncArguments given as
        // name=value where its value is not empty. What the window runs and what it shows to copy are the same.
        const string SyncProgram = "python";

        static List<string> SyncArgs(string settings, string project, string folder)
        {
            var arguments = new List<string> { "-m", "pythontk", "ops", "apply", SyncOperation };
            string[] values = { settings, project, folder };
            for (int i = 0; i < SyncArguments.Length; i++)
                if (!string.IsNullOrEmpty(values[i])) arguments.Add(SyncArguments[i] + "=" + values[i]);
            return arguments;
        }

        /// <summary>The sheet's reader could not be run, or said the sheet cannot be staged: read the message, or run <see cref="SyncCommand(Manifest)"/> in a terminal.</summary>
        public const string ReadCode = "unity.manifest.read";

        const string ReadAction = "read its sheet again and stage it (sheet.sync)";

        /// <summary>
        /// Read the manifest's sheet again and stage it into this project, beside its plan: runs the sheet reader's
        /// <c>sheet.sync</c> (<see cref="SyncCommand(Manifest)"/>) as a person would in a terminal, then imports what it
        /// staged (the plan, the delivery, the steps' files), so a build builds the sheet as it now reads. The scene
        /// is not touched: <see cref="Build(Manifest, OpContext, IEnumerable{string})"/> does that. Where the reader
        /// cannot be run (no Python on the path, the automation not installed), or it refuses the sheet, that is
        /// raised (<see cref="ReadCode"/>) with what it printed, and nothing is imported.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="ctx">The run's context: asked before the plan is written (a dry run plans it); what the reader said of the sheet is raised through it.</param>
        /// <returns>Whether the sheet was read and staged.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is null.</exception>
        public static bool ReadAgain(Manifest manifest, OpContext ctx = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest), "name the manifest");
            if (!SyncOf(manifest, out string settings, out string folder))
            {
                ctx.Raise(ReadCode, "the manifest does not say where its sheet's settings file is: set its Sheet in Setup, then read it again");
                return false;
            }
            string plan = manifest.plan != null ? AssetDatabase.GetAssetPath(manifest.plan) : "";
            if (ctx != null && !ctx.ShouldWrite(plan.Length > 0 ? plan : ProjectFolder, ReadAction)) return false;
            var arguments = SyncArgs(settings, ProjectFolder, folder);
            return Read(SheetReader.Run(SyncProgram, arguments, ProjectFolder, ctx), SyncCommand(settings, ProjectFolder, folder), ctx);
        }

        // What a reader's run came to, said: its issues raised as the reader rated them, the staged files imported where it
        // staged them (all of the sheet, or, partial, what it could: its issues say what it left). One that failed, printed
        // no outcome or was cancelled imports nothing.
        internal static bool Read(SheetReader.Result result, string command, OpContext ctx)
        {
            Core.Outcome outcome = SheetReader.Outcome(result.printed);
            string status = outcome?.status;
            if (outcome?.issues != null)
                foreach (Core.Issue issue in outcome.issues)
                    if (issue != null) ctx.Raise(ReadCode, issue.message, string.IsNullOrEmpty(issue.severity) ? "warning" : issue.severity);
            if (!result.ran || status == null || status == "failed")
            {
                string printed = (result.printed ?? "").Trim();
                string said = !string.IsNullOrEmpty(outcome?.error) ? outcome.error : printed.Length > 600 ? "..." + printed.Substring(printed.Length - 600) : printed;
                ctx.Raise(ReadCode, "the sheet could not be read again" + (said.Length > 0 ? $" ({said})" : "") +
                                    $": run it in a terminal to see why: {command}", "error");
                return false;
            }
            if (status == "cancelled")
            {
                ctx.Raise(ReadCode, "the sheet's reader was cancelled before it finished: nothing it staged is imported; read it again" +
                                    (string.IsNullOrEmpty(outcome.error) ? "" : $" ({outcome.error})"));
                return false;
            }
            if (status == "partial")
                ctx.Raise(ReadCode, "the sheet was staged in part: what it staged is imported, and its issues say what it left; " +
                                    $"correct them and read it again, or run it in a terminal: {command}");
            AssetDatabase.Refresh();
            return true;
        }

        // Whether the text file at path no longer has digest (the plan's); why says so, naming it as what.
        static bool Differs(string path, string digest, string what, ref string why)
        {
            if (string.IsNullOrEmpty(digest) || !File.Exists(path) || TextDigest(path) == digest) return false;
            why = $"the {what} ({Path.GetFileName(path)}) changed since the plan was read";
            return true;
        }

        // The SHA-256 of a text file (hex), its line endings aside (CRLF read as LF), as the plan's digests are made.
        static string TextDigest(string path) => Digest(path, "text", file =>
        {
            byte[] bytes = Bytes(file);
            var folded = new List<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
                if (!(bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')) folded.Add(bytes[i]);
            using (var sha = System.Security.Cryptography.SHA256.Create()) return Hex(sha.ComputeHash(folded.ToArray()));
        });

        // Whether two files hold the same bytes: their lengths first, then their SHA-256s, each file read as a stream (a
        // delivery is large, and an assessment asks this of every one that is newer than its staged copy).
        static bool Same(string a, string b)
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            return Digest(a, "bytes", BytesDigest) == Digest(b, "bytes", BytesDigest);
        }

        static string BytesDigest(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = Open(path))
                return Hex(sha.ComputeHash(stream));
        }

        // A file's digest of a kind, made by make, kept while the file's length and last write stay as they were: an
        // assessment asks every time, and a sheet or a delivery rarely changes between two. A file written in the last
        // seconds is digested every time (its clock may not have ticked since: a second write in that time reads alike).
        static string Digest(string path, string kind, Func<string, string> make)
        {
            var file = new FileInfo(path);
            string key = kind + "|" + file.FullName;
            DateTime written = file.LastWriteTimeUtc;
            if (Digests.TryGetValue(key, out var known) && known.length == file.Length && known.written == written) return known.digest;
            string digest = make(path);
            if (DateTime.UtcNow - written > TimeSpan.FromSeconds(2)) Digests[key] = (file.Length, written, digest);
            else Digests.Remove(key);
            return digest;
        }

        // The digests Digest keeps, by kind and full path: a cache, rebuilt after a domain reload.
        static readonly Dictionary<string, (long length, DateTime written, string digest)> Digests =
            new Dictionary<string, (long, DateTime, string)>(StringComparer.Ordinal);

        // The digest of a folder of clause templates, as the plan's clauses_digest is made: a line per JSON file in name
        // order, its name and its own digest (TextDigest) apart by a space, joined by newlines; empty where it holds none.
        static string ClausesDigest(string folder)
        {
            if (!Directory.Exists(folder)) return "";
            string[] lines = Directory.GetFiles(folder, "*.json").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => $"{n} {TextDigest(Path.Combine(folder, n))}").ToArray();
            if (lines.Length == 0) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return Hex(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines))));
        }

        // A digest's bytes as lowercase hex.
        static string Hex(byte[] digest)
        {
            var hex = new System.Text.StringBuilder(digest.Length * 2);
            foreach (byte each in digest) hex.Append(each.ToString("x2"));
            return hex.ToString();
        }

        // A file opened to read, though another program has it open for writing (a sheet open in its editor).
        static FileStream Open(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        // A file's bytes, read as Open reads it.
        static byte[] Bytes(string path)
        {
            using (FileStream stream = Open(path))
            using (var bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                return bytes.ToArray();
            }
        }

        // A settings file as its declaration reads it (SheetSettings); null where it is not one this editor reads, unread
        // saying why (not JSON, not its shape, another version).
        static SheetSettings.Shape.Payload SettingsOf(string file, out string unread)
        {
            unread = null;
            SheetSettings.Shape.Payload read;
            try
            {
                read = SheetSettings.Shape.Payload.Read(
                    Core.Json.Parse(new System.Text.UTF8Encoding(false).GetString(Bytes(file)).TrimStart('\uFEFF'), false));
            }
            catch (FormatException e)
            {
                unread = e.Message;
                return null;
            }
            if (read == null) unread = "no JSON object";
            else if (read.version != SheetSettings.CurrentVersion) unread = $"version {read.version}; this editor reads {SheetSettings.CurrentVersion}";
            return unread == null ? read : null;
        }

        /// <summary>
        /// The settings of <paramref name="type"/> the manifest keeps, made where it keeps none (inside
        /// the manifest's own asset file, so a manifest stays one file): what a person then edits in
        /// place of the kit's defaults.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="type">A kit's settings type (<see cref="IManifestKit.Settings"/>).</param>
        /// <param name="ctx">The run's context: asked before the manifest is changed.</param>
        /// <returns>The settings; null in a dry run that plans them.</returns>
        public static ScriptableObject AddSettings(Manifest manifest, Type type, OpContext ctx = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
                throw new ArgumentException("a kit's settings type is a ScriptableObject's", nameof(type));
            ScriptableObject known = manifest.Settings(type);
            if (known != null) return known;
            if (ctx != null && !ctx.ShouldChange(manifest, $"keep its own {type.Name}")) return null;
            ScriptableObject made = ScriptableObject.CreateInstance(type);
            made.name = type.Name;
            bool kept = AssetDatabase.Contains(manifest);
            if (kept) AssetDatabase.AddObjectToAsset(made, manifest);
            manifest.settings.Add(made);
            ctx.Edited(manifest);
            ctx.Created(made);  // after the manifest's change, which its record (ShouldChange) takes up to here
            if (kept) AssetDatabase.SaveAssetIfDirty(manifest);
            return made;
        }

        /// <summary>Lock or unlock <paramref name="steps"/>: a build leaves a locked step as it is.</summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="steps">Step IDs.</param>
        /// <param name="locked">Lock them (true) or unlock them.</param>
        /// <param name="ctx">The run's context: asked before the manifest is changed.</param>
        /// <returns>How many steps changed.</returns>
        public static int SetLocked(Manifest manifest, IEnumerable<string> steps, bool locked, OpContext ctx = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            return Switch(manifest, manifest.locked, steps, locked, change => $"{(locked ? "lock" : "unlock")} {change}", ctx);
        }

        // Put names in list (on) or take them out: the ones that change, asked of ctx as one change of manifest.
        static int Switch(Manifest manifest, List<string> list, IEnumerable<string> names, bool on, Func<string, string> action, OpContext ctx)
        {
            var change = new List<string>();
            foreach (string name in names ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(name) && list.Contains(name) != on && !change.Contains(name)) change.Add(name);
            if (change.Count == 0) return 0;
            if (ctx != null && !ctx.ShouldChange(manifest, action(string.Join(", ", change)))) return 0;
            foreach (string name in change)
            {
                if (on) list.Add(name);
                else list.Remove(name);
            }
            ctx.Edited(manifest);
            return change.Count;
        }

        // -- behaviors: off, and an object's own -------------------------------------------------

        /// <summary>
        /// Turn <paramref name="behaviors"/> off for the manifest, or on again: a build leaves every row
        /// of a behavior that is off out, and a step that had one is built anew without it.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="behaviors">Behavior names.</param>
        /// <param name="off">Turn them off (true) or on again.</param>
        /// <param name="ctx">The run's context: asked before the manifest is changed.</param>
        /// <returns>How many behaviors changed.</returns>
        public static int SetOff(Manifest manifest, IEnumerable<string> behaviors, bool off, OpContext ctx = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            return Switch(manifest, manifest.off, behaviors, off, change => $"turn {change} {(off ? "off" : "on")}", ctx);
        }

        /// <summary>
        /// Go back to what the sheet says: drop the manifest's edits (<see cref="Manifest.edits"/>) of
        /// <paramref name="step"/>'s object <paramref name="target"/>, of every object of the step where
        /// <paramref name="target"/> is null, or every edit where <paramref name="step"/> is null too. The
        /// next build builds the rows as the sheet writes them. Which behaviors are off for the whole
        /// manifest is its switch, not an edit: <see cref="SetOff"/>.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="step">The step's ID; null: every step.</param>
        /// <param name="target">The object, as the sheet's rows name it; null: every object of the step.</param>
        /// <param name="ctx">The run's context: asked before the manifest is changed.</param>
        /// <returns>How many edits went (or would, in a dry run).</returns>
        public static int Revert(Manifest manifest, string step = null, string target = null, OpContext ctx = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            var gone = manifest.edits.FindAll(e => e != null && (step == null || e.step == step) && (target == null || e.target == target));
            var own = manifest.tunes.FindAll(t => t != null && (step == null || t.step == step) && (target == null || t.target == target));
            // Every edit: what was adjusted in the scene goes back to what the build gave it too.
            int adjusted = step == null && target == null ? RevertAdjustments(manifest, null, ctx) : 0;
            if (gone.Count + own.Count == 0) return adjusted;
            string what = step == null ? "go back to the sheet: every edit" : target == null ? $"go back to the sheet in {step}" : $"go back to the sheet for {target} in {step}";
            if (ctx != null && !ctx.ShouldChange(manifest, what)) return gone.Count + own.Count;
            foreach (Manifest.Edit edit in gone) manifest.edits.Remove(edit);
            foreach (Manifest.Tune tune in own) manifest.tunes.Remove(tune);
            ctx.Edited(manifest);
            return gone.Count + own.Count + adjusted;
        }

        /// <summary>
        /// What a person changed in the scene on the parts the manifest's build made: each plain field of a part a row,
        /// a step or the build itself holds (a number, a flag, a text, a choice, a colour, a vector) whose value differs
        /// from what the last build gave it. Every build keeps them (<see cref="Manifest.adjusted"/>) over what it
        /// gives the part; one made since the last build is kept by the next. A field a behavior keeps as its object's
        /// setting (<see cref="IManifestBound"/>: an arrow's size) is that setting's, and listed with the edits. What a
        /// kit keeps itself (<see cref="IManifestKit.Kept"/>: a helper moved in the scene) is listed with them.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <returns>The adjustments, in the order the build holds their parts; those of a scene that is not open by what the manifest keeps.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is null.</exception>
        public static List<ManifestAdjustment> Adjustments(Manifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            ManifestScene scene = SceneOf(manifest);
            var plan = scene == null ? null : Plan(manifest, out _);
            List<ManifestAdjustment> found = ManifestAdjustments.Read(manifest, scene == null ? default : scene.Root.scene,
                                                                      plan == null ? null : BoundProperties(manifest, plan, scene));
            found.AddRange(Kept(manifest, plan, scene));
            return found;
        }

        // What the plan's kits keep of the open scene themselves (a spot a person moved): listed with the fields'.
        static List<ManifestAdjustment> Kept(Manifest manifest, SheetPlan.Shape.Payload plan, ManifestScene scene)
        {
            var kept = new List<ManifestAdjustment>();
            if (plan == null || scene == null) return kept;
            foreach (IManifestKit kit in ManifestBehaviors.Kits(plan)) kept.AddRange(kit.Kept(manifest, scene));
            return kept;
        }

        // Whether parts hold part: the part itself, or its object. A row holds what it built, a kit's placed spot with it,
        // so an adjustment is said on the rows that hold that part and on no other row about the same object (a tool's tip
        // offset is the rows' that put it in hand or work with it, never a highlight's on it).
        static bool Holds(List<UnityEngine.Object> parts, Component part)
            => parts.Contains(part) || parts.Contains(part.gameObject);

        /// <summary>
        /// Give what a person adjusted on <paramref name="part"/> (null: on every part) what the build gave it again, and
        /// drop it from the manifest (<see cref="Adjustments"/>).
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="part">A part of the open scene; null: every part.</param>
        /// <param name="ctx">The run's context: asked before each part and the manifest are changed.</param>
        /// <returns>How many adjustments went (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is null.</exception>
        public static int RevertAdjustments(Manifest manifest, Component part = null, OpContext ctx = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            ManifestScene scene = SceneOf(manifest);
            int gone = ManifestAdjustments.Revert(manifest, scene == null ? default : scene.Root.scene, part, ctx);
            var plan = scene == null ? null : Plan(manifest, out _);
            if (plan != null)
                foreach (IManifestKit kit in ManifestBehaviors.Kits(plan)) gone += kit.Revert(manifest, scene, part, ctx);
            return gone;
        }

        /// <summary>
        /// Have what the plan's kits placed by a search on <paramref name="part"/> (null: every part) placed anew by the next
        /// build (<see cref="IManifestKit.PlaceAgain"/>): its first placement, and any move a person made of it, go. A
        /// placed spot keeps its first placement on every build otherwise (the search runs once).
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="part">A part of the open scene; null: every part.</param>
        /// <param name="ctx">The run's context: asked before each change.</param>
        /// <returns>How many will be placed anew (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is null.</exception>
        public static int PlaceAgain(Manifest manifest, Component part = null, OpContext ctx = null)
            => PlaceAgain(manifest, new[] { part }, ctx);

        /// <summary>
        /// <see cref="PlaceAgain(Manifest, Component, OpContext)"/> for each of <paramref name="parts"/> (a row's), the
        /// manifest's scene and plan found once.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="parts">Parts of the open scene (a null one: every part).</param>
        /// <param name="ctx">The run's context: asked before each change.</param>
        /// <returns>How many will be placed anew (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="manifest"/> or <paramref name="parts"/> is null.</exception>
        public static int PlaceAgain(Manifest manifest, IEnumerable<Component> parts, OpContext ctx = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (parts == null) throw new ArgumentNullException(nameof(parts), "name the parts (a null one: every part)");
            ManifestScene scene = SceneOf(manifest);
            var plan = scene == null ? null : Plan(manifest, out _);
            int again = 0;
            if (plan == null) return again;
            IReadOnlyList<IManifestKit> kits = ManifestBehaviors.Kits(plan);
            foreach (Component part in parts)
                foreach (IManifestKit kit in kits)
                {
                    // A part placed anew (a spot taken off) is gone, and a gone part reads as none: never every part.
                    if (!ReferenceEquals(part, null) && part == null) break;
                    again += kit.PlaceAgain(manifest, scene, part, ctx);
                }
            return again;
        }

        /// <summary>
        /// Give the object <paramref name="target"/> its own setting <paramref name="name"/> for <paramref name="behavior"/> in
        /// <paramref name="step"/>, over its kit's (an arrow's colour, a button held to play); null <paramref name="value"/> drops it
        /// (the kit's again). Kept in the manifest (<see cref="Manifest.Tune.values"/>), marked and reverted as an edit; every build
        /// goes by it. A name that is no field of the behavior's kit's settings (<see cref="ManifestBehaviors.KitOf"/>) is refused, and
        /// said: a row's own fields are <see cref="SetRowField"/>'s. So is a setting of the row's as a whole
        /// (<see cref="ManifestBehaviorAttribute.RowSettings"/>: a take's speed) given to one of its objects: it is kept under the row,
        /// or under the object where that object's row is its own (<see cref="IsOwnRow"/>: added here, or a take of its own).
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: the row itself (the step's own row, or a row's settings as a whole).</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="name">A setting's name, a field of the behavior's kit's settings (<see cref="ManifestBehaviorAttribute.Settings"/> lists its own).</param>
        /// <param name="value">Its value as text (a number invariant, a colour r,g,b,a or #RRGGBBAA, a choice by name); null: none of its own.</param>
        /// <param name="ctx">The run's context: asked before the manifest is changed.</param>
        /// <param name="row">Which of the step's rows of the sheet it is (its place among them); -1: every row of the behavior the object has, or one added here.</param>
        /// <returns>Whether the manifest changed (or would, in a dry run); false for a refused name.</returns>
        public static bool SetTune(Manifest manifest, string step, string target, string behavior, string name, string value, OpContext ctx = null, int row = -1)
        {
            Require(manifest, step, behavior, name);
            Type type = ManifestBehaviors.KitOf(behavior)?.Settings;
            if (value != null && !ManifestValues.HasSetting(type, name))
            {
                ctx.Raise(SettingCode, type == null
                    ? $"'{behavior}' belongs to no kit with settings: '{name}' cannot be its own setting"
                    : $"'{name}' is no setting of {type.Name} ('{behavior}'): name one of its fields" +
                      (IsRowField(behavior, name) ? $", or set the row's field '{name}' (SetRowField)" : ""));
                return false;
            }
            if (value != null && ManifestBehaviors.Mark(behavior)?.IsRowSetting(name) == true && !string.IsNullOrEmpty(target)
                && !IsOwnRow(manifest, step, target, behavior, row))
            {
                ctx.Raise(SettingCode, $"'{name}' is its row's as a whole ('{behavior}', {step}), never one of its objects': give it to the " +
                                       "row (an empty target), which every object of it plays by");
                return false;
            }
            return Keep(manifest, step, target, behavior, row, ctx, tune => tune.Get(name), (tune, kept) => tune.Set(name, kept), value,
                        leaf => value == null ? $"give '{leaf}' its kit's {name} again ({behavior}, {step})"
                                              : $"give '{leaf}' its own {name}: {value} ({behavior}, {step})");
        }

        /// <summary>
        /// Give the object <paramref name="target"/>'s row of <paramref name="behavior"/> in <paramref name="step"/> its own field
        /// <paramref name="name"/> over the sheet's (the take it plays, the step it goes to: what a row added here needs); null
        /// <paramref name="value"/> drops it (the sheet's again). An object with a field of its own stands as a row of its own
        /// (<see cref="Effective(SheetPlan.Shape.Payload, Manifest)"/>). Kept in the manifest (<see cref="Manifest.Tune.fields"/>), marked and reverted as an edit. A name
        /// that is no row field of the behavior (<see cref="IsRowField"/>) is refused, and said.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: the step's own row.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="name">A row field's name (<see cref="SheetPlan.DeclaredFields"/>, <see cref="ManifestBehaviorAttribute.Fields"/>).</param>
        /// <param name="value">Its value as text; null: the sheet's.</param>
        /// <param name="ctx">The run's context: asked before the manifest is changed.</param>
        /// <param name="row">Which of the step's rows of the sheet it is; -1: every row of the behavior the object has, or one added here.</param>
        /// <returns>Whether the manifest changed (or would, in a dry run); false for a refused name.</returns>
        public static bool SetRowField(Manifest manifest, string step, string target, string behavior, string name, string value, OpContext ctx = null,
                                       int row = -1)
        {
            Require(manifest, step, behavior, name);
            if (value != null && !IsRowField(behavior, name))
            {
                ctx.Raise(SettingCode, $"'{name}' is no field a '{behavior}' row may set: name one the plan declares on every row " +
                                       $"({string.Join(", ", SheetPlan.DeclaredFields)}) or one of its behavior's Fields");
                return false;
            }
            return Keep(manifest, step, target, behavior, row, ctx, tune => tune.Field(name), (tune, kept) => tune.SetField(name, kept), value,
                        leaf => value == null ? $"give '{leaf}' the sheet's {name} again ({behavior}, {step})"
                                              : $"give '{leaf}' its own {name}: {value} ({behavior}, {step})");
        }

        // The arguments every edit of an object's own values names.
        static void Require(Manifest manifest, string step, string behavior, string name)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(step) || string.IsNullOrEmpty(behavior) || string.IsNullOrEmpty(name))
                throw new ArgumentException("name the step, the behavior and the value");
        }

        // Keep value in the object's own values (read and write: which of them), asked of ctx; an emptied entry goes.
        static bool Keep(Manifest manifest, string step, string target, string behavior, int row, OpContext ctx,
                         Func<Manifest.Tune, string> read, Action<Manifest.Tune, string> write, string value, Func<string, string> action)
        {
            target ??= "";
            Manifest.Tune tune = manifest.tunes.Find(t => t != null && t.step == step && t.target == target && t.behavior == behavior && t.row == row);
            if ((tune == null ? null : read(tune)) == value) return false;
            if (ctx != null && !ctx.ShouldChange(manifest, action(target.Length == 0 ? step : target.Substring(target.LastIndexOf('|') + 1)))) return true;
            if (tune == null) manifest.tunes.Add(tune = new Manifest.Tune { step = step, target = target, behavior = behavior, row = row });
            write(tune, value);
            if (tune.Empty) manifest.tunes.Remove(tune);
            ctx.Edited(manifest);
            return true;
        }

        /// <summary>
        /// A copy of the kit's settings of <paramref name="type"/> as one object of a step is built by them: the manifest's
        /// (else the type's defaults), with what the sheet's row says over them (<see cref="SheetPlan.Shape.PlanAction.settings"/>)
        /// and the object's own values for <paramref name="behavior"/> over those (<see cref="Manifest.Tune.values"/>). The caller
        /// destroys it (what a front end edits, then hands to <see cref="SetTunes"/>).
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="type">A kit's settings type.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: the row itself (the step's own row, or a row's settings as a whole).</param>
        /// <param name="row">Which of the step's rows of the sheet it is; -1: every row, or one added here (no row's words).</param>
        /// <param name="behavior">The behavior whose own values are read, as a build reads them; null: every behavior's.</param>
        public static ScriptableObject OwnSettings(Manifest manifest, Type type, string step, string target, int row = -1, string behavior = null)
        {
            ScriptableObject own = Usual(manifest, type, step, row);
            ManifestValues.WriteAll(own, ManifestValues.Over(manifest, step, target, row, null, behavior: behavior));
            return own;
        }

        // A copy of the kit's settings of type as a row of the sheet builds them before any object's own: the manifest's (else the
        // defaults) with the row's words over them (row: its place in step; -1: none). The caller destroys it.
        static ScriptableObject Usual(Manifest manifest, Type type, string step, int row)
        {
            if (manifest == null || type == null) throw new ArgumentNullException(manifest == null ? nameof(manifest) : nameof(type));
            ScriptableObject kit = manifest.Settings(type);
            ScriptableObject usual = kit != null ? UnityEngine.Object.Instantiate(kit) : ScriptableObject.CreateInstance(type);
            usual.hideFlags = HideFlags.HideAndDontSave;
            SheetPlan.Shape.PlanAction sheet = Plan(manifest, out _).Row(step, row);
            if (sheet != null && sheet.settings != null) ManifestValues.WriteAll(usual, ManifestValues.Over(null, step, "", -1, sheet.settings));
            return usual;
        }

        /// <summary>
        /// Keep each of <paramref name="settings"/> that <paramref name="own"/> holds otherwise than its row of the sheet builds it
        /// (its kit's settings with the row's words over them) as the object's own value for <paramref name="behavior"/>
        /// (<see cref="SetTune"/>), and drop the ones it holds as the row does: what a front end saves from a copy it edited
        /// (<see cref="OwnSettings"/>).
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: the step's own row.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="own">The edited copy.</param>
        /// <param name="settings">The settings it may hold of its own (the behavior's).</param>
        /// <param name="ctx">The run's context: asked before the manifest is changed.</param>
        /// <param name="row">Which of the step's rows of the sheet it is; -1: every row, or one added here.</param>
        /// <returns>How many values changed (or would, in a dry run).</returns>
        public static int SetTunes(Manifest manifest, string step, string target, string behavior, ScriptableObject own,
                                   IEnumerable<string> settings, OpContext ctx = null, int row = -1)
        {
            if (manifest == null || own == null) throw new ArgumentNullException(manifest == null ? nameof(manifest) : nameof(own));
            ScriptableObject usual = Usual(manifest, own.GetType(), step, row);
            int changed = 0;
            try
            {
                foreach (string name in settings ?? Array.Empty<string>())
                {
                    string value = ManifestValues.Read(own, name);
                    if (value == null) continue;
                    if (SetTune(manifest, step, target, behavior, name, value == ManifestValues.Read(usual, name) ? null : value, ctx, row)) changed++;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(usual);
            }
            return changed;
        }

        /// <summary>
        /// Whether <paramref name="name"/> is a field of a <paramref name="behavior"/> row a person may give an object of its own:
        /// a text field the plan declares on every row (<see cref="SheetPlan.DeclaredFields"/>: its take, the step it goes to),
        /// or one of the behavior's own (<see cref="ManifestBehaviorAttribute.Fields"/>); any other value is a setting.
        /// </summary>
        /// <param name="behavior">The row's behavior.</param>
        /// <param name="name">A value's name.</param>
        public static bool IsRowField(string behavior, string name)
            => !string.IsNullOrEmpty(name) && (SheetPlan.DeclaredFields.Contains(name)
                                               || Array.IndexOf(ManifestBehaviors.Mark(behavior)?.Fields ?? Array.Empty<string>(), name) >= 0);

        // A copy of action with the row fields tune keeps for it (none: action itself); the copy's fields its own.
        static SheetPlan.Shape.PlanAction Tuned(SheetPlan.Shape.PlanAction action, Manifest.Tune tune)
        {
            if (tune == null) return action;
            SheetPlan.Shape.PlanAction made = null;
            foreach (Manifest.Value value in tune.fields)
            {
                if (value == null) continue;
                if (made == null)
                {
                    made = Copy(action);
                    made.fields = action.fields == null ? null : new Dictionary<string, object>(action.fields, StringComparer.Ordinal);
                }
                made.SetField(value.name, value.value);
            }
            return made ?? action;
        }

        // Whether tune keeps any row field.
        static bool HasFields(Manifest.Tune tune) => tune != null && tune.fields.Count > 0;

        /// <summary>
        /// Whether the manifest builds <paramref name="target"/>'s row of <paramref name="behavior"/> in <paramref name="step"/> as a
        /// row of its own: one added here for it, or one it has fields of its own for (the take it plays), which stands apart from
        /// the rest of its sheet's row (<see cref="Effective(SheetPlan.Shape.Payload, Manifest)"/>). That row's settings as a whole
        /// (<see cref="ManifestBehaviorAttribute.RowSettings"/>) are then the object's own, kept under it with its fields.
        /// </summary>
        /// <param name="manifest">The manifest; null: none is.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: none is.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="row">Which of the step's rows of the sheet it is; -1: one added here.</param>
        public static bool IsOwnRow(Manifest manifest, string step, string target, string behavior, int row = -1)
            => manifest != null && !string.IsNullOrEmpty(target)
               && (manifest.EditOf(step, target, behavior, row) is Manifest.Edit edit && edit.add || HasFields(manifest.TuneOf(step, target, behavior, row)));

        /// <summary>
        /// The settings of <paramref name="behavior"/> a front end offers as <paramref name="target"/>'s own values in
        /// <paramref name="step"/> (what it edits with <see cref="OwnSettings"/> and keeps with <see cref="SetTunes"/>): for a row
        /// that names no object (an empty target) every one of the behavior's <see cref="ManifestBehaviorAttribute.Settings"/>;
        /// for one object of a row its <see cref="ManifestBehaviorAttribute.ObjectSettings"/>, the row's as a whole
        /// (<see cref="ManifestBehaviorAttribute.RowSettings"/>: a take's window and speed) offered for the row alone
        /// (<paramref name="wholeRow"/>, kept under an empty target); every one for an object whose row is its own
        /// (<see cref="IsOwnRow"/>). Empty where it offers none.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: a row that names none.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="row">Which of the step's rows of the sheet it is; -1: one added here.</param>
        /// <param name="wholeRow">The settings of the object's row as a whole, which every object of it builds by, in place of the object's.</param>
        public static string[] Offered(Manifest manifest, string step, string target, string behavior, int row = -1, bool wholeRow = false)
        {
            ManifestBehaviorAttribute mark = ManifestBehaviors.Mark(behavior);
            if (mark?.Settings == null) return Array.Empty<string>();
            if (string.IsNullOrEmpty(target) || IsOwnRow(manifest, step, target, behavior, row)) return wholeRow ? Array.Empty<string>() : mark.Settings;
            return wholeRow ? mark.RowSettings ?? Array.Empty<string>() : mark.ObjectSettings();
        }

        // The target action's own values as a row are kept under (row: its place among the step's rows in the sheet; -1: added
        // here): its one object where the manifest builds it as that object's row of its own (IsOwnRow), else the row's (empty).
        internal static string RowTarget(Manifest manifest, string step, SheetPlan.Shape.PlanAction action, int row)
            => action?.targets != null && action.targets.Count == 1 && IsOwnRow(manifest, step, action.targets[0], action.behavior, row) ? action.targets[0] : "";

        /// <summary>
        /// Give the object <paramref name="target"/> the behavior <paramref name="behavior"/> in step
        /// <paramref name="step"/>, or take it away: where the sheet gives it, taking it away is kept as
        /// an edit and giving it back drops that edit; where the sheet does not, giving it is the edit.
        /// The sheet is not changed: the manifest keeps the difference (<see cref="Manifest.edits"/>),
        /// every build goes by it, and the window's Export lists it for the sheet's owner.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: the step's own (a behavior that names no object: going on, a note).</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="on">The object has it (true) or not.</param>
        /// <param name="ctx">The run's context: asked before the manifest is changed.</param>
        /// <returns>Whether the manifest changed (or would, in a dry run).</returns>
        /// <param name="row">Which of the step's rows of the sheet it is (its place among them); -1: every row of the behavior the object has.</param>
        public static bool SetAssigned(Manifest manifest, string step, string target, string behavior, bool on, OpContext ctx = null, int row = -1)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            target ??= "";
            if (string.IsNullOrEmpty(step) || string.IsNullOrEmpty(behavior))
                throw new ArgumentException("name the step and the behavior");
            bool sheets = false;  // whether the sheet gives the object this behavior in this step
            SheetPlan.Shape.Payload plan = Plan(manifest, out _);
            if (plan != null)
                foreach (var each in plan.steps)
                    if (each.id == step && each.actions != null)
                        for (int i = 0; i < each.actions.Count; i++)
                        {
                            var action = each.actions[i];
                            if (row >= 0 && i != row) continue;
                            sheets |= action.behavior == behavior && (target.Length == 0
                                ? action.targets == null || action.targets.Count == 0
                                : action.targets != null && action.targets.Contains(target));
                        }
            Manifest.Edit known = manifest.edits.Find(e => e != null && e.step == step && e.target == target && e.behavior == behavior && e.row == row);
            // Given back where an edit for every row of it took it away: that edit goes (every row of it again).
            if (known == null && on && sheets && row >= 0)
                known = manifest.edits.Find(e => e != null && e.step == step && e.target == target && e.behavior == behavior && e.row == -1 && !e.add);
            bool wanted = sheets ? !on : on;  // an edit is kept only where it differs from the sheet
            if (wanted == (known != null)) return false;
            string leaf = target.Length == 0 ? step : target.Substring(target.LastIndexOf('|') + 1);
            string what = wanted ? (on ? $"give '{leaf}' {behavior} in {step}" : $"leave {behavior} out for '{leaf}' in {step}")
                                 : $"go by the sheet again for {behavior} on '{leaf}' in {step}";
            if (ctx != null && !ctx.ShouldChange(manifest, what)) return true;
            if (wanted) manifest.edits.Add(new Manifest.Edit { step = step, target = target, behavior = behavior, add = on, row = on && !sheets ? -1 : row });
            else manifest.edits.Remove(known);
            ctx.Edited(manifest);
            return true;
        }

        /// <summary>
        /// The behaviors <paramref name="target"/> can be given in <paramref name="scene"/> beyond what
        /// the sheet says: each registered behavior whose rows name objects
        /// (<see cref="ManifestBehaviorAttribute.NamesObjects"/>; a step's own, as going on, never). One that needs
        /// more than the object (a take, a tool) is given those as its row's own fields once added
        /// (<see cref="SetRowField"/>): its row says what it lacks until then. Sorted.
        /// </summary>
        /// <param name="scene">The delivery the manifest is built into.</param>
        /// <param name="target">The object, as the sheet's rows name it.</param>
        public static List<string> Assignable(ManifestScene scene, string target)
        {
            var names = new List<string>();
            if (scene == null || string.IsNullOrEmpty(target) || scene.Node(target) == null) return names;
            foreach (string name in ManifestBehaviors.Names())
            {
                if (ManifestBehaviors.Mark(name)?.NamesObjects == false) continue;  // its step's own: nothing to give an object
                names.Add(name);
            }
            return names;
        }

        /// <summary>
        /// The plan as the manifest builds it: the sheet's rows, less every row of a behavior that is off
        /// (<see cref="Manifest.off"/>) and each object an edit leaves a row out for, plus a row for each
        /// behavior an edit adds (<see cref="Manifest.edits"/>). The plan itself where the manifest keeps
        /// neither.
        /// </summary>
        /// <param name="plan">The sheet's plan.</param>
        /// <param name="manifest">The manifest (null: the plan as it is).</param>
        public static SheetPlan.Shape.Payload Effective(SheetPlan.Shape.Payload plan, Manifest manifest)
            => Effective(plan, manifest, null);

        // Effective, with where each row came from (and the rows left out) noted per step for the assessment.
        static SheetPlan.Shape.Payload Effective(SheetPlan.Shape.Payload plan, Manifest manifest, Dictionary<string, Origin> origins)
        {
            if (plan == null) return null;
            bool edited = manifest != null && (manifest.off.Count > 0 || manifest.edits.Count > 0 || manifest.tunes.Count > 0);
            bool delivered = plan.steps.Exists(s => s.actions != null && s.actions.Exists(a => a.upstream != null && a.upstream.Count > 0));
            if (!edited && !delivered && origins == null) return plan;
            SheetPlan.Shape.Payload built = Copy(plan);
            built.steps = new List<SheetPlan.Shape.PlanStep>();
            foreach (var step in plan.steps)
            {
                var origin = new Origin();
                SheetPlan.Shape.PlanStep made = Copy(step);
                made.actions = new List<SheetPlan.Shape.PlanAction>();
                for (int i = 0; step.actions != null && i < step.actions.Count; i++)
                {
                    var action = step.actions[i];
                    var dropped = new List<string>();
                    bool own = action.targets == null || action.targets.Count == 0;  // the step's own: no object
                    if (manifest != null && !own)
                        foreach (string target in action.targets)
                        {
                            Manifest.Edit edit = manifest.EditOf(step.id, target, action.behavior, i);
                            if (edit != null && !edit.add) dropped.Add(target);
                        }
                    bool off = manifest != null && manifest.off.Contains(action.behavior);
                    off |= !own && dropped.Count > 0 && dropped.Count == action.targets.Count;
                    off |= own && manifest != null && manifest.EditOf(step.id, "", action.behavior, i) is Manifest.Edit gone && !gone.add;
                    if (off)
                    {
                        origin.off.Add((i, action, dropped));
                        continue;
                    }
                    var kept = action;
                    // The objects the delivery's own build made it on are left to it (built once between the two
                    // manifests); a row it made on every object stands whole, as the delivery's.
                    List<string> upstream = action.upstream == null || own ? new List<string>()
                                          : action.upstream.FindAll(t => action.targets.Contains(t) && !dropped.Contains(t));
                    if (dropped.Count > 0 || upstream.Count > 0)
                    {
                        kept = Copy(action);
                        kept.targets = action.targets.FindAll(target => !dropped.Contains(target) && !upstream.Contains(target));
                        if (kept.targets.Count == 0) kept.targets = upstream;  // the delivery's whole
                        kept.upstream = upstream;
                    }
                    // An object with fields of its own (the take it plays) stands as a row of its own, the rest as the sheet's.
                    var split = new List<SheetPlan.Shape.PlanAction>();
                    if (manifest != null && manifest.tunes.Count > 0)
                    {
                        if (own) kept = Tuned(kept, manifest.TuneOf(step.id, "", action.behavior, i));
                        else
                            foreach (string target in kept.targets.ToList())
                            {
                                Manifest.Tune tune = manifest.TuneOf(step.id, target, action.behavior, i);
                                if (!HasFields(tune) || (kept.upstream != null && kept.upstream.Contains(target))) continue;
                                if (kept.targets.Count == 1)
                                {
                                    kept = Tuned(kept, tune);
                                    break;
                                }
                                if (ReferenceEquals(kept, action)) kept = Copy(action);
                                kept.targets = kept.targets.FindAll(t => t != target);
                                SheetPlan.Shape.PlanAction one = Copy(action);
                                one.targets = new List<string> { target };
                                one.upstream = new List<string>();
                                split.Add(Tuned(one, tune));
                            }
                    }
                    made.actions.Add(kept);
                    origin.rows.Add((i, false, dropped));
                    foreach (SheetPlan.Shape.PlanAction one in split)
                    {
                        made.actions.Add(one);
                        origin.rows.Add((i, false, new List<string>()));
                    }
                }
                if (manifest != null)
                    foreach (Manifest.Edit edit in manifest.edits)
                    {
                        if (edit == null || !edit.add || edit.step != step.id || manifest.off.Contains(edit.behavior)) continue;
                        bool own = string.IsNullOrEmpty(edit.target);
                        if (made.actions.Exists(a => a.behavior == edit.behavior && (own ? a.targets == null || a.targets.Count == 0
                                                                                      : a.targets != null && a.targets.Contains(edit.target)))) continue;
                        string leaf = own ? step.id : edit.target.Substring(edit.target.LastIndexOf('|') + 1);
                        made.actions.Add(Tuned(new SheetPlan.Shape.PlanAction
                        {
                            behavior = edit.behavior,
                            targets = own ? new List<string>() : new List<string> { edit.target },
                            text = $"{edit.behavior} {leaf} (added in this project)",
                        }, manifest.TuneOf(step.id, edit.target ?? "", edit.behavior)));
                        origin.rows.Add((-1, true, new List<string>()));
                    }
                built.steps.Add(made);
                if (origins != null) origins[step.id] = origin;
            }
            return built;
        }

        // Where a step's rows as built came from: per row its place in the sheet (-1: added), and the sheet's rows left out.
        sealed class Origin
        {
            internal readonly List<(int source, bool added, List<string> dropped)> rows = new List<(int, bool, List<string>)>();
            internal readonly List<(int source, SheetPlan.Shape.PlanAction action, List<string> dropped)> off =
                new List<(int, SheetPlan.Shape.PlanAction, List<string>)>();
        }

        // A copy of one of the plan's objects, field for field (its lists shared until one is replaced).
        static T Copy<T>(T from) where T : class, new()
        {
            var to = new T();
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)) field.SetValue(to, field.GetValue(from));
            return to;
        }

        // What a step is built from, as one line: each row's behavior with what it names, its take, the step it goes
        // to, and every field and setting its words gave it, by name.
        static string Source(SheetPlan.Shape.PlanStep step)
        {
            var text = new System.Text.StringBuilder();
            if (step.actions != null)
                foreach (var action in step.actions)
                {
                    if (text.Length > 0) text.Append("; ");
                    text.Append(action.behavior);
                    if (action.targets != null && action.targets.Count > 0) text.Append(' ').Append(string.Join(",", action.targets));
                    if (!string.IsNullOrEmpty(action.clip)) text.Append(" plays ").Append(action.clip);
                    if (!string.IsNullOrEmpty(action.to)) text.Append(" to ").Append(action.to);
                    if (action.fields != null)
                        foreach (string name in action.fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
                        {
                            List<string> values = action.FieldList(name);
                            text.Append(' ').Append(name).Append('=').Append(values == null ? "" : string.Join(",", values));
                        }
                    if (action.settings != null)
                        foreach (string name in action.settings.Keys.OrderBy(k => k, StringComparer.Ordinal))
                            text.Append(" @").Append(name).Append('=')
                                .Append(Convert.ToString(action.settings[name], System.Globalization.CultureInfo.InvariantCulture));
                }
            return text.ToString();
        }

        // -- assess ---------------------------------------------------------------------------

        /// <summary>
        /// Where every row of the manifest stands against the open scene; null, with an issue, where
        /// it has no plan. Before its delivery is placed (nothing is built yet) the rows say what the
        /// plan alone can: a row the sheet's reader could not read, a behavior no builder has, and
        /// every other row <see cref="ManifestStatus.Missing"/>.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="ctx">The run's context: its issues get why there is nothing to assess.</param>
        public static ManifestAssessment Assess(Manifest manifest, OpContext ctx = null)
        {
            var plan = Plan(manifest, out string refusal);
            if (plan == null)
            {
                ctx.Raise(ManifestCode, refusal, "error");
                return null;
            }
            ManifestScene scene = SceneOf(manifest);
            if (scene == null)
            {
                ManifestAssessment unplaced = Rows(ref plan, null, manifest);
                SayMoved(unplaced, manifest, plan);
                return unplaced;
            }
            List<ManifestAdjustment> adjusted = ManifestAdjustments.Read(manifest, scene.Root.scene, BoundProperties(manifest, plan, scene));
            var assessment = Rows(ref plan, scene, manifest);
            SayMoved(assessment, manifest, plan);
            var dry = new OpContext(dryRun: true);
            scene.Begin(assessment, dry, null, manifest.locked);
            try
            {
                foreach (IManifestKit kit in ManifestBehaviors.Kits(plan)) kit.Keep(manifest, scene, dry);
            }
            finally
            {
                scene.End();
            }
            // What a person adjusted is no work for a build: the builders' dry run sees the parts as the last build left them.
            var lifted = ManifestAdjustments.Lift(adjusted, dry);
            try
            {
                Run(plan, scene, assessment, dry, null, assessing: true);
            }
            finally
            {
                ManifestAdjustments.Lower(lifted);
            }
            adjusted.AddRange(Kept(manifest, plan, scene));  // a kit's own (a moved spot): never lifted, the kit builds by it
            assessment.adjusted = adjusted;
            // Each with the row and the step that hold its part, so a front end says it where the part is listed.
            foreach (ManifestAdjustment each in adjusted)
            {
                if (each.part == null) continue;
                foreach (ManifestAssessment.Step step in assessment.steps)
                {
                    if (Holds(step.parts, each.part)) step.adjusted.Add(each);
                    foreach (ManifestAssessment.Row row in step.rows)
                        if (Holds(row.parts, each.part)) row.adjusted.Add(each);
                }
                if (Holds(assessment.shared.parts, each.part)) assessment.shared.adjusted.Add(each);
            }
            return assessment;
        }

        /// <summary>
        /// Where every row of <paramref name="plan"/> stands in <paramref name="scene"/>: whether it can be
        /// built (every node and take it names found, a builder for its behavior), what a build would
        /// change for it (the build's own dry run, row by row: none means built) and what it holds
        /// there. Changes nothing.
        /// </summary>
        /// <param name="plan">The plan (<see cref="SheetPlan.Read"/>, <see cref="SheetPlan.Load"/>).</param>
        /// <param name="scene">The delivery it is built into.</param>
        /// <param name="ctx">The run's context (unused: an assessment asks nothing of it).</param>
        /// <returns>The assessment, step by step and row by row.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="plan"/> or <paramref name="scene"/> is null.</exception>
        public static ManifestAssessment Assess(SheetPlan.Shape.Payload plan, ManifestScene scene, OpContext ctx = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan), "read the plan first (SheetPlan.Load)");
            if (scene == null) throw new ArgumentNullException(nameof(scene), "name the delivery (new ManifestScene(root))");
            var assessment = Rows(ref plan, scene, scene.Manifest);
            Run(plan, scene, assessment, new OpContext(dryRun: true), null, assessing: true);
            return assessment;
        }

        // The steps and rows the manifest builds of plan (which becomes the plan as built: Effective), each row
        // checked against scene (null: against the plan alone); the rows it leaves out listed with their step.
        static ManifestAssessment Rows(ref SheetPlan.Shape.Payload plan, ManifestScene scene, Manifest manifest)
        {
            var origins = new Dictionary<string, Origin>(StringComparer.Ordinal);
            plan = Effective(plan, manifest, origins);
            List<string> locked = manifest != null ? manifest.locked : null;
            if (scene != null)
            {
                scene.Plan = plan;
                scene.Sources.Clear();
                scene.Rows.Clear();
            }
            var assessment = new ManifestAssessment();
            if (plan.issues != null) assessment.planIssues.AddRange(plan.issues);
            foreach (var step in plan.steps)
            {
                var entry = new ManifestAssessment.Step { id = step.id, locked = locked != null && locked.Contains(step.id) };
                assessment.steps.Add(entry);
                if (scene != null) scene.Sources[step.id] = Source(step);
                origins.TryGetValue(step.id, out Origin origin);
                for (int i = 0; step.actions != null && i < step.actions.Count; i++)
                {
                    ManifestAssessment.Row row = Check(step, i, scene);
                    row.locked = entry.locked;
                    if (origin != null && i < origin.rows.Count)
                    {
                        if (scene != null) scene.Rows[step.actions[i]] = origin.rows[i].added ? -1 : origin.rows[i].source;
                        row.source = origin.rows[i].source;
                        row.added = origin.rows[i].added;
                        row.dropped.AddRange(origin.rows[i].dropped);
                    }
                    entry.rows.Add(row);
                    assessment.rows.Add(row);
                }
                if (origin == null) continue;
                foreach (var (source, action, dropped) in origin.off)
                {
                    var row = new ManifestAssessment.Row
                    {
                        step = step.id, index = -1, source = source, behavior = action.behavior, text = action.text, off = true,
                        locked = entry.locked, unplaced = scene == null,
                    };
                    if (action.targets != null) row.targets.AddRange(action.targets);
                    row.dropped.AddRange(dropped);
                    entry.off.Add(row);
                }
            }
            return assessment;
        }

        static ManifestAssessment.Row Check(SheetPlan.Shape.PlanStep step, int index, ManifestScene scene)
        {
            var action = step.actions[index];
            var row = new ManifestAssessment.Row { step = step.id, index = index, behavior = action.behavior, text = action.text, unplaced = scene == null };
            if (action.targets != null) row.targets.AddRange(action.targets);
            if (action.warnings != null) row.warnings.AddRange(action.warnings);
            if (action.upstream != null && action.upstream.Count > 0)
            {
                row.upstream.AddRange(action.upstream);
                bool whole = action.targets != null && action.targets.TrueForAll(t => action.upstream.Contains(t));
                if (whole && (action.issues == null || action.issues.Count == 0))
                {
                    row.state = ManifestAssessment.State.Upstream;  // the delivery made it on every object: nothing to build here
                    return row;
                }
            }
            IManifestBehavior builder = ManifestBehaviors.Find(action.behavior);
            foreach (var (problem, cause) in Problems(action, scene, builder))
            {
                row.problems.Add(problem);
                row.cause ??= cause;
            }
            row.state = builder == null ? ManifestAssessment.State.Unsupported
                      : row.problems.Count == 0 ? ManifestAssessment.State.Ready : ManifestAssessment.State.Unresolved;
            return row;
        }

        /// <summary>
        /// What keeps <paramref name="action"/> from being built in <paramref name="scene"/>, as its assessment says it: the
        /// plan's own issues on it, each node and take its behavior names that the delivery lacks, then (where nothing else
        /// holds it back) its builder's check. None where it can be built, or where no builder has its behavior. A builder
        /// asks it of a row its own row depends on (a marker of a later teleport's viewpoint), so the one is held back with
        /// the other.
        /// </summary>
        /// <param name="action">A row of the plan.</param>
        /// <param name="scene">The delivery it is built into.</param>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> or <paramref name="scene"/> is null.</exception>
        public static List<string> Problems(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (action == null) throw new ArgumentNullException(nameof(action), "name the row");
            if (scene == null) throw new ArgumentNullException(nameof(scene), "name the delivery (new ManifestScene(root))");
            return Problems(action, scene, ManifestBehaviors.Find(action.behavior)).Select(each => each.problem).ToList();
        }

        // Each problem that keeps action from being built in scene (null: the plan alone), with its cause (a ManifestStatus),
        // in the order an assessment says them: the plan's own issues, the nodes and takes its behavior names, its builder's check.
        static IEnumerable<(string problem, string cause)> Problems(SheetPlan.Shape.PlanAction action, ManifestScene scene, IManifestBehavior builder)
        {
            int found = 0;
            foreach (string issue in action.issues ?? new List<string>())
            {
                found++;
                yield return (issue, ManifestStatus.Sheet);
            }
            if (scene == null) yield break;
            ManifestBehaviorAttribute mark = ManifestBehaviors.Mark(action.behavior);
            var named = new List<(string path, string what)>();
            if (action.targets != null)
                foreach (string target in action.targets) named.Add((target, "target"));
            // The fields its behavior says name nodes or takes, and no other: a field of another meaning is never looked up.
            foreach (string field in mark?.NodeFields ?? Array.Empty<string>())
                foreach (string path in action.FieldValues(field) ?? new List<string>())
                    if (!string.IsNullOrEmpty(path)) named.Add((path, field.Replace('_', ' ')));
            foreach (var (path, what) in named)
            {
                string problem = scene.Problem(path, what, out string status);
                if (string.IsNullOrEmpty(problem)) continue;
                found++;
                yield return (problem, status);
            }
            foreach (string field in mark?.TakeFields ?? Array.Empty<string>())
                foreach (string take in action.FieldValues(field) ?? new List<string>())
                    if (!string.IsNullOrEmpty(take) && scene.Clip(take) == null)
                    {
                        found++;
                        yield return ($"the delivery carries no take '{take}'", ManifestStatus.MissingTake);
                    }
            if (found > 0 || builder == null) yield break;
            foreach (string problem in builder.Check(action, scene) ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(problem)) yield return (problem, ManifestStatus.Unbuildable);
        }

        // -- build ----------------------------------------------------------------------------

        /// <summary>
        /// Build the manifest in the open scene, keeping what a person adjusted: each kit keeps the
        /// adjustments made to what it built (<see cref="IManifestKit.Keep"/>), its delivery is placed
        /// where the scene holds none, then the plan is built (<see cref="Build(SheetPlan.Shape.Payload, ManifestScene, OpContext, IEnumerable{string})"/>,
        /// which changes only what differs from it) and the scene saved where it is the manifest's and the run changed
        /// something (a build that changes nothing plans nothing, its save neither). A dry
        /// run of a manifest whose delivery is not placed yet plans the placement and every row the build
        /// then makes (over an instance in a preview scene, gone as it ends).
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <param name="only">The steps to build, by ID (what the steps share is built with them); null: every step.</param>
        /// <returns>The assessment the build went by, each built row with the changes made for it; null where there is no plan or delivery (an issue says why).</returns>
        public static ManifestAssessment Build(Manifest manifest, OpContext ctx = null, IEnumerable<string> only = null)
        {
            var plan = Plan(manifest, out string refusal);
            if (plan == null)
            {
                ctx.Raise(ManifestCode, refusal, "error");
                return null;
            }
            int asked = ctx == null ? 0 : ctx.Changes.Count;
            Follow(manifest, plan, ctx);
            ManifestScene scene = SceneOf(manifest);
            List<ManifestAdjustment> adjusted = null;
            if (scene != null)  // else nothing of it is in the open scene: what is there is another's to keep
            {
                foreach (IManifestKit kit in ManifestBehaviors.Kits(plan)) kit.Keep(manifest, scene, ctx);
                KeepChanged(manifest, plan, scene, ctx);
                adjusted = ManifestAdjustments.Keep(manifest, scene.Root.scene, ctx, BoundProperties(manifest, plan, scene));
            }
            Scene preview = default;
            ManifestAssessment built;
            List<(Component part, FieldInfo field, object value)> lifted = null;
            try
            {
                if (scene == null)
                {
                    scene = Place(manifest, ctx);
                    if (scene == null && ctx != null && ctx.DryRun && manifest.delivery != null) scene = Preview(manifest, out preview);
                }
                if (scene == null) return null;
                // The builders work over the parts as the last build left them; what a person adjusted goes back after.
                if (adjusted != null) lifted = ManifestAdjustments.Lift(adjusted, ctx);
                built = Build(plan, scene, ctx, only);
                RecordBuilt(manifest, plan, scene, built, ctx, ctx != null && ctx.Changes.Count > asked);
            }
            finally
            {
                ManifestAdjustments.Lower(lifted);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
            if (ctx == null || ctx.Changes.Count > asked) Save(manifest, ctx);  // what the run changed: a build that changes nothing writes nothing
            if (ctx != null && ctx.DryRun) return built;  // a plan: nothing was built to report
            int ran = 0;
            foreach (ManifestAssessment.Step step in built.steps)
                if (step.made) ran++;
            ctx.Log(ran == built.steps.Count ? $"Manifest '{manifest.name}': {built.Summary()}"
                                             : $"Manifest '{manifest.name}': built {ran} of {built.steps.Count} step(s)");
            return built;
        }

        // Each setting a behavior built into a part for an object or its row (IManifestBound), with its key in Manifest.built.
        static IEnumerable<(string key, string step, string target, string behavior, string setting, UnityEngine.Object part, string property, int row)>
            Bounds(Manifest manifest, SheetPlan.Shape.Payload plan, ManifestScene scene)
        {
            var origins = new Dictionary<string, Origin>(StringComparer.Ordinal);
            SheetPlan.Shape.Payload effective = Effective(plan, manifest, origins);
            // Its builders are asked of these rows as an assessment asks them (Rows): over the plan they are rows of, each row
            // with its place. Before a build the scene holds no plan, and a builder that reads beyond its row (a picture's
            // top-level object, named by the plan) found nothing: a person's change there was never kept.
            scene.Plan = effective;
            scene.Rows.Clear();
            foreach (var step in effective.steps)
            {
                if (step.actions == null) continue;
                origins.TryGetValue(step.id, out Origin origin);
                for (int i = 0; i < step.actions.Count; i++)
                {
                    var action = step.actions[i];
                    int row = origin != null && i < origin.rows.Count && !origin.rows[i].added ? origin.rows[i].source : -1;
                    scene.Rows[action] = row;
                    if (!(ManifestBehaviors.Find(action.behavior) is IManifestBound bound)) continue;
                    // Only a row a build builds: one the plan or its builder holds back made nothing to keep.
                    if (action.issues != null && action.issues.Count > 0) continue;
                    if (((IManifestBehavior)bound).Check(action, scene)?.Any(problem => !string.IsNullOrEmpty(problem)) == true) continue;
                    // The row itself where it names no object or keeps settings as a whole (a take's: RowSettings), then each object.
                    bool own = action.targets == null || action.targets.Count == 0;
                    var targets = new List<string>();
                    if (own || ManifestBehaviors.Mark(action.behavior)?.RowSettings?.Length > 0) targets.Add("");
                    if (!own) targets.AddRange(action.targets);
                    foreach (string target in targets)
                    {
                        if (target.Length > 0 && scene.Node(target) == null) continue;
                        string kept = target.Length == 0 ? RowTarget(manifest, step.id, action, row) : target;  // a row of one object's own: kept by it
                        foreach (var (setting, part, property) in bound.Bound(action, step, scene, target))
                            if (part != null)
                                yield return ($"{step.id}|{kept}|{action.behavior}|{row}|{setting}", step.id, kept, action.behavior, setting, part, property, row);
                    }
                }
            }
        }

        // The properties of built parts that behaviors keep as their objects' settings (IManifestBound): a person's change
        // of one is that setting's (KeepChanged), never an adjustment of the part beside it.
        static HashSet<(UnityEngine.Object, string)> BoundProperties(Manifest manifest, SheetPlan.Shape.Payload plan, ManifestScene scene)
        {
            var bound = new HashSet<(UnityEngine.Object, string)>();
            foreach (var (_, _, _, _, _, part, property, _) in Bounds(manifest, plan, scene)) bound.Add((part, property));
            return bound;
        }

        // What a person changed in the scene since the last build, on a part a behavior builds from its settings (an
        // arrow's colour), kept as the object's own value, so the build that follows keeps it: in a dry run, which writes
        // none, kept on the scene for its build (ManifestScene.Planned).
        static void KeepChanged(Manifest manifest, SheetPlan.Shape.Payload plan, ManifestScene scene, OpContext ctx)
        {
            if (manifest.built.Count == 0) return;
            var built = new Dictionary<string, string>();
            foreach (Manifest.Value value in manifest.built)
                if (value != null && !ManifestAdjustments.IsRecord(value)) built[value.name] = value.value;
            foreach (var (key, step, target, behavior, setting, part, property, row) in Bounds(manifest, plan, scene).ToList())
            {
                string now = ManifestValues.Read(part, property);
                if (now == null || !built.TryGetValue(key, out string was) || now == was) continue;
                if (!SetTune(manifest, step, target, behavior, setting, now, ctx, row) || ctx == null || !ctx.DryRun) continue;
                var planned = new Manifest.Tune { step = step, target = target, behavior = behavior, row = row };
                planned.Set(setting, now);
                scene.Planned.Add(planned);
            }
        }

        // The manifest's delivery and extras as instances in a new preview scene, which the caller closes: what a dry run
        // of a delivery not placed yet builds over, as its live run builds over the instances it places.
        static ManifestScene Preview(Manifest manifest, out Scene preview)
        {
            preview = EditorSceneManager.NewPreviewScene();
            var root = (GameObject)PrefabUtility.InstantiatePrefab(manifest.delivery, preview);
            var extras = new List<GameObject>();
            foreach (GameObject extra in manifest.extras)
                if (extra != null) extras.Add((GameObject)PrefabUtility.InstantiatePrefab(extra, preview));
            return new ManifestScene(root, extras) { Manifest = manifest, AssetFolder = AssetFolder(manifest) };
        }

        // What this build gave the parts it holds: each setting a behavior builds a part from (IManifestBound), then every
        // held part's plain fields (ManifestAdjustments), read while the parts stand as the build left them. The next build
        // tells a person's change from its own work by it. A part this pass did not walk (a step left out of a partial
        // build, a locked one) keeps its record while it is in the scene. Asked of ctx (so an undo takes the record back
        // with the build) wherever the build changed anything or the parts differ from the record: a dry run, which cannot
        // know what a build gives, asks by the same rule, so its plan names what its live run asks.
        static void RecordBuilt(Manifest manifest, SheetPlan.Shape.Payload plan, ManifestScene scene, ManifestAssessment built, OpContext ctx,
                                bool changed)
        {
            var made = new List<Manifest.Value>();
            foreach (var (key, _, _, _, _, part, property, _) in Bounds(manifest, plan, scene))
            {
                string value = ManifestValues.Read(part, property);
                if (value != null) made.Add(new Manifest.Value { name = key, value = value });
            }
            List<Manifest.Value> parts = ManifestAdjustments.Record(built);
            var walked = new HashSet<string>(StringComparer.Ordinal);
            foreach (Manifest.Value record in parts) walked.Add(record.name);
            foreach (Manifest.Value record in manifest.built)
                if (ManifestAdjustments.IsRecord(record) && !walked.Contains(record.name)
                    && ManifestAdjustments.Find(record.name.Substring(1), scene.Root.scene) != null)
                    parts.Add(record);
            made.AddRange(parts);
            bool same = made.Count == manifest.built.Count;
            for (int i = 0; same && i < made.Count; i++)
                same = manifest.built[i] != null && manifest.built[i].name == made[i].name && manifest.built[i].value == made[i].value;
            if (same && (!changed || ctx == null)) return;
            if (manifest.built.Count == 0 && made.Count == 0 && !changed) return;  // nothing built, nothing to record
            if (ctx != null && !ctx.ShouldChange(manifest, "record what the build gave the parts it holds")) return;
            if (same) return;
            manifest.built = made;
            ctx.Edited(manifest);
        }

        // Whether a row of plan is built by a behavior whose settings are properties of what it builds.
        static bool Bound(SheetPlan.Shape.Payload plan)
        {
            foreach (var step in plan.steps)
                if (step.actions != null && step.actions.Exists(a => a != null && ManifestBehaviors.Find(a.behavior) is IManifestBound)) return true;
            return false;
        }

        /// <summary>
        /// Build every row of <paramref name="plan"/> that can be into <paramref name="scene"/>: the kits
        /// whose rows it holds first build what those share (<see cref="IManifestKit.Begin"/>), each ready
        /// row is built through its behavior's builder, in plan order, and the kits build what follows
        /// (<see cref="IManifestKit.End"/>). An unresolved or unsupported row is raised as an issue
        /// (<see cref="UnresolvedCode"/>, <see cref="UnsupportedCode"/>) and left out; a locked step is
        /// left as it is.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="scene">The delivery it is built into.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <param name="only">The steps to build, by ID; null: every step.</param>
        /// <returns>The assessment the build went by, each built row with the changes made for it and what it holds.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="plan"/> or <paramref name="scene"/> is null.</exception>
        public static ManifestAssessment Build(SheetPlan.Shape.Payload plan, ManifestScene scene, OpContext ctx = null, IEnumerable<string> only = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan), "read the plan first (SheetPlan.Load)");
            if (scene == null) throw new ArgumentNullException(nameof(scene), "name the delivery (new ManifestScene(root))");
            var assessment = Rows(ref plan, scene, scene.Manifest);
            Run(plan, scene, assessment, ctx, only, assessing: false);
            return assessment;
        }

        // One pass of the build over the plan, dry or live: the kits, then each ready row, then the kits.
        // An assessment walks a locked step too, so the step still says what it holds; a build leaves it.
        static void Run(SheetPlan.Shape.Payload plan, ManifestScene scene, ManifestAssessment assessment, OpContext ctx, IEnumerable<string> only,
                        bool assessing)
        {
            bool live = ctx == null || !ctx.DryRun;
            IReadOnlyList<IManifestKit> kits = ManifestBehaviors.Kits(plan);
            scene.Begin(assessment, ctx, only, assessing || scene.Manifest == null ? null : scene.Manifest.locked);
            try
            {
                foreach (IManifestKit kit in kits) kit.Begin(scene, ctx);
                int at = 0, done = 0;
                foreach (var step in plan.steps)
                {
                    if (ctx != null) ctx.Progress((double)done++ / plan.steps.Count, step.id);  // and a cancel checkpoint
                    if (step.actions == null) continue;
                    bool builds = scene.Builds(step.id);
                    foreach (var action in step.actions)
                    {
                        ManifestAssessment.Row row = assessment.rows[at++];
                        if (!builds) continue;
                        switch (row.state)
                        {
                            case ManifestAssessment.State.Ready:
                                using (scene.For(row))
                                    ManifestBehaviors.Find(action.behavior).Apply(action, step, scene, ctx);
                                row.made = live;
                                break;
                            case ManifestAssessment.State.Upstream:
                                break;  // the delivery's own build made it
                            case ManifestAssessment.State.Unsupported:
                                scene.Flush();
                                ctx.Raise(UnsupportedCode, $"{step.id}: '{action.text}' ({action.behavior}): no builder for {action.behavior} yet");
                                scene.Skip();
                                break;
                            default:
                                scene.Flush();
                                ctx.Raise(UnresolvedCode, $"{step.id}: '{action.text}': {string.Join("; ", row.problems)}");
                                scene.Skip();
                                break;
                        }
                    }
                }
                foreach (IManifestKit kit in kits) kit.End(scene, ctx);
                if (live)
                {
                    assessment.shared.made = true;
                    foreach (ManifestAssessment.Step step in assessment.steps)
                        if (scene.Builds(step.id)) step.made = true;  // the steps this run built, and no other
                }
            }
            finally
            {
                scene.End();
            }
        }

        /// <summary>
        /// Build the manifest anew in a new scene: its delivery and extras placed, the plan built, then
        /// the setups of its kits (<see cref="ManifestSceneSetupAttribute"/>: a rig, the delivery's
        /// look), and the scene saved as the manifest's. A person's adjustments are discarded where
        /// <paramref name="discard"/> says (<see cref="IManifestKit.Forget"/>), else kept from the open
        /// scene first (<see cref="IManifestKit.Keep"/>) and applied.
        /// </summary>
        /// <remarks>
        /// The open scene is replaced: its unsaved changes go with it, so a front end asks first. Refused
        /// while a step is locked: a new scene holds nothing of a locked step to leave as it is.
        /// </remarks>
        /// <param name="manifest">The manifest.</param>
        /// <param name="discard">Discard the adjustments first: every part where the build puts it.</param>
        /// <param name="ctx">The run's context, asked before the scene is made and saved, and handed to the placement, the build and
        /// each setup: a dry run plans the reset itself (a new scene is not planned row by row).</param>
        /// <returns>The assessment the build went by; null in a dry run, or where there is no plan or delivery.</returns>
        public static ManifestAssessment Reset(Manifest manifest, bool discard, OpContext ctx = null)
        {
            var plan = Plan(manifest, out string refusal);
            if (plan == null)
            {
                ctx.Raise(ManifestCode, refusal, "error");
                return null;
            }
            if (manifest.delivery == null)
            {
                ctx.Raise(ManifestCode, "the manifest names no delivery: set its Delivery (the imported model)", "error");
                return null;
            }
            if (manifest.locked.Count > 0)
            {
                ctx.Raise(ManifestCode, $"{manifest.locked.Count} step(s) are locked ({string.Join(", ", manifest.locked)}): a new scene holds nothing " +
                                        "of them to leave as it is. Unlock them first, or build in the open scene", "error");
                return null;
            }
            Follow(manifest, plan, ctx);
            bool here = Placed(manifest.delivery) != null;  // else the open scene is another's: nothing of this one to keep
            foreach (IManifestKit kit in ManifestBehaviors.Kits(plan))
            {
                if (discard) kit.Forget(manifest, ctx);
                else if (here) kit.Keep(manifest, null, ctx);  // what was moved in the scene it replaces
            }
            if (discard)
            {
                if (manifest.adjusted.Count > 0 && (ctx == null || ctx.ShouldChange(manifest, $"drop {manifest.adjusted.Count} adjustment(s) made in the scene")))
                {
                    manifest.adjusted.Clear();
                    ctx.Edited(manifest);
                }
            }
            else if (here)
            {
                ManifestScene was = SceneOf(manifest);
                ManifestAdjustments.Keep(manifest, was.Root.scene, ctx, BoundProperties(manifest, plan, was));
            }
            string where = string.IsNullOrEmpty(manifest.scene) ? "a new scene" : manifest.scene;
            if (ctx != null && !ctx.ShouldCreate(where, "build the manifest anew")) return null;
            string asset = AssetDatabase.GetAssetPath(manifest);
            // A new scene unloads the assets nothing in it holds, a script's own reference counting for nothing:
            // the manifest is held across it, so the caller's reference is still the manifest afterwards.
            HideFlags flags = manifest.hideFlags;
            manifest.hideFlags = flags | HideFlags.DontUnloadUnusedAsset;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            }
            finally
            {
                if (manifest != null) manifest.hideFlags = flags;
            }
            if (manifest == null && !string.IsNullOrEmpty(asset)) manifest = AssetDatabase.LoadAssetAtPath<Manifest>(asset);
            if (manifest == null)
            {
                ctx.Raise(ManifestCode, "the manifest was unloaded with the scene it was in: keep it as an asset", "error");
                return null;
            }
            ManifestScene scene = Place(manifest, ctx);
            if (scene == null) return null;
            ManifestAssessment built = Build(plan, scene, ctx);
            RecordBuilt(manifest, plan, scene, built, ctx, true);
            ManifestAdjustments.Apply(manifest, scene.Root.scene, ctx);  // a new scene's parts stand as built: what was adjusted goes on
            Setup(manifest, plan, scene, ctx);
            if (!string.IsNullOrEmpty(manifest.scene) && (ctx == null || ctx.ShouldWrite(manifest.scene, "save the manifest's scene")))
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), manifest.scene);
            ctx.Log($"Manifest '{manifest.name}': {built.Summary()}");
            return built;
        }

        // The setups of a fresh scene (ManifestSceneSetupAttribute) of the kits that take part in plan's build, each given ctx.
        static void Setup(Manifest manifest, SheetPlan.Shape.Payload plan, ManifestScene scene, OpContext ctx)
        {
            var kits = new HashSet<Type>();
            foreach (IManifestKit kit in ManifestBehaviors.Kits(plan)) kits.Add(kit.GetType());
            foreach (MethodInfo setup in TypeCache.GetMethodsWithAttribute<ManifestSceneSetupAttribute>())
            {
                Type kit = setup.GetCustomAttribute<ManifestSceneSetupAttribute>().Kit;
                if (kit != null && !typeof(IManifestKit).IsAssignableFrom(kit))
                {
                    // Said where it can have been meant: to the builds the kit beside it takes part in, and, where its
                    // assembly holds no kit, to every build. (A package's test of this mistake was said in every
                    // project's builds that compiled its tests.)
                    Type beside = ManifestBehaviors.KitIn(setup.DeclaringType.Assembly)?.GetType();
                    if (beside != null && !kits.Contains(beside)) continue;
                    ctx.Raise(ManifestCode, $"{setup.DeclaringType.Name}.{setup.Name} names {kit.Name} as its kit, which is no IManifestKit: " +
                                            "name the kit whose manifests it sets up");
                    continue;
                }
                if (kit == null) kit =ManifestBehaviors.KitIn(setup.DeclaringType.Assembly)?.GetType();
                if (kit != null && !kits.Contains(kit)) continue;  // another kit's manifests'
                ParameterInfo[] wants = setup.GetParameters();
                if (!setup.IsStatic || wants.Length != 3 || wants[0].ParameterType != typeof(Manifest)
                    || wants[1].ParameterType != typeof(ManifestScene) || wants[2].ParameterType != typeof(OpContext))
                {
                    ctx.Raise(ManifestCode, $"{setup.DeclaringType.Name}.{setup.Name} is no scene setup: make it static void (Manifest, ManifestScene, OpContext)");
                    continue;
                }
                try
                {
                    setup.Invoke(null, new object[] { manifest, scene, ctx });
                }
                catch (TargetInvocationException e) when (!(e.InnerException is Core.OperationCancelledException))
                {
                    ctx.Raise(ManifestCode, $"{setup.DeclaringType.Name}.{setup.Name} failed ({e.InnerException?.Message}): the scene is built without it", "error");
                }
            }
        }

        // The manifest's delivery and extras placed in the open scene, as a ManifestScene; null in a dry run that plans them.
        static ManifestScene Place(Manifest manifest, OpContext ctx)
        {
            if (manifest.delivery == null)
            {
                ctx.Raise(ManifestCode, "the manifest names no delivery: set its Delivery (the imported model)", "error");
                return null;
            }
            foreach (GameObject model in new List<GameObject>(manifest.extras) { manifest.delivery })
            {
                if (model == null || Placed(model) != null) continue;
                if (ctx != null && !ctx.ShouldCreate(model.name, "place it in the scene")) continue;
                ctx.Created((GameObject)PrefabUtility.InstantiatePrefab(model));
            }
            return ctx != null && ctx.DryRun && Placed(manifest.delivery) == null ? null : SceneOf(manifest);
        }

        // The open scene saved, where it is the manifest's own.
        static void Save(Manifest manifest, OpContext ctx)
        {
            Scene open = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(manifest.scene) || open.path != manifest.scene) return;
            if (ctx != null && !ctx.ShouldWrite(manifest.scene, "save the manifest's scene")) return;
            EditorSceneManager.SaveScene(open);
        }
    }
}
