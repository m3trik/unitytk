// The Tasks sample's demo recipe: builds its demo scene from the imported sample.
using M3trik.Unitytk.Manifests;
using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Kits.Tasks;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Samples.Tasks
{
    /// <summary>
    /// Builds the Tasks demo scene from the imported sample: the task's manifest made beside
    /// its plan (Tools > unitytk > Manifest then shows each row of the sheet with what it built),
    /// with the sample's own settings (<see cref="Choose"/>: its debug tools and a page for every step,
    /// which the kit's defaults leave out), its delivery placed on a floor and its task built from
    /// its plan (the steps, their highlights, arrows, viewpoints, targets and clips). The kit's build
    /// gives the scene what a person plays it with: the scene's camera made the user's rig
    /// (<see cref="TaskKit.MakeRig"/>: the viewpoints move it, a right-button drag turns it, its clicks
    /// reach the scene and a hand in its view holds what is taken), the task's placard, which comes up
    /// in front of the user with each step, and its debug tools (a panel beside the eyes with the step it
    /// is on, Skip, Back, Restart and Autopilot). Enter Play Mode in the scene to take the task. The
    /// kit's Edit Mode tests play this same scene through.
    /// </summary>
    public static class TaskDemoRecipe
    {
        /// <summary>The package the sample ships in.</summary>
        public const string Package = "com.m3trik.unitytk";
        /// <summary>The sample's name in the Package Manager's Samples tab.</summary>
        public const string SampleName = "Tasks";
        /// <summary>The task's delivery, in the sample's folder.</summary>
        public const string ModelFile = "cabinet.fbx";
        /// <summary>The task's plan (read from its sheet and delivery), in the sample's folder.</summary>
        public const string PlanFile = "cabinet.plan.json";
        /// <summary>The scene this builds, in the sample's folder.</summary>
        public const string SceneFile = "TaskDemo.unity";

        /// <summary>
        /// The folder the sample was imported to (<c>Assets/Samples/unitytk/&lt;version&gt;/Tasks</c>),
        /// or null when it is not imported.
        /// </summary>
        public static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            if (sample.displayName == null || !sample.isImported) return null;
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        /// <summary>Build the demo scene, save it beside the task and open it.</summary>
        [MenuItem("Tools/unitytk/Samples/Task Demo")]
        static void BuildAndOpen()
        {
            string folder = Folder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog("Task Demo",
                    "Import the Tasks sample first: Window > Package Manager > unitytk > Samples > Tasks > Import.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            OpContext.RunMenu("Task Demo", ctx => Build(folder, ctx));
        }

        /// <summary>
        /// The sample's own choices, over the kit's defaults (which assume no task): its debug tools, for trying it in the
        /// editor, and a page on the placard for every step, its voice line where it has no placard text.
        /// </summary>
        /// <param name="settings">The task's settings (its manifest's).</param>
        /// <param name="ctx">The run's context: asked before the settings are changed.</param>
        /// <returns>Whether it changed them (or would, in a dry run).</returns>
        public static bool Choose(TaskSettings settings, OpContext ctx = null)
        {
            if (settings == null || settings.debugTools && settings.pages == PlacardPages.EveryStep) return false;
            if (ctx != null && !ctx.ShouldChange(settings, "the sample's own choices: its debug tools, a page for every step")) return true;
            settings.debugTools = true;
            settings.pages = PlacardPages.EveryStep;
            ctx.Edited(settings);
            return true;
        }

        /// <summary>
        /// Build the demo scene from the sample in <paramref name="folder"/>: a new scene (a sun on the
        /// cabinet's front and a fill on its back), a floor, the task's
        /// manifest with the sample's own settings (<see cref="Choose"/>), its delivery placed in it and its
        /// task built from the plan (<see cref="ManifestsActions.Build(Manifest, OpContext, System.Collections.Generic.IEnumerable{string})"/>:
        /// the user's rig and the debug tools with it); saved there as <see cref="SceneFile"/> and left open.
        /// </summary>
        /// <param name="folder">The imported sample's folder (<see cref="Folder"/>).</param>
        /// <param name="ctx">The run's context: asked before the scene is written and each change in it; a dry run plans each
        /// over the open scene (an empty one plans what a live run makes in its new scene), opening and saving none, and
        /// plans no build before the task's manifest is made.</param>
        /// <returns>The scene, saved; an invalid one in a dry run.</returns>
        /// <exception cref="FileNotFoundException">The folder holds no delivery or no plan.</exception>
        /// <exception cref="InvalidOperationException">The plan cannot be read, or its manifest names no delivery.</exception>
        public static Scene Build(string folder, OpContext ctx = null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{ModelFile}");
            if (model == null) throw new FileNotFoundException($"no {ModelFile} in {folder}: import the Tasks sample again");
            var planText = AssetDatabase.LoadAssetAtPath<TextAsset>($"{folder}/{PlanFile}");
            if (planText == null) throw new FileNotFoundException($"no {PlanFile} in {folder}: import the Tasks sample again");
            if (SheetPlan.Read(planText.text, PlanFile, out string refusal) == null) throw new InvalidOperationException(refusal);
            string path = $"{folder}/{SceneFile}";
            bool live = ctx == null || !ctx.DryRun;
            if (ctx != null && !ctx.ShouldWrite(path, "the Tasks demo scene: a new scene with the task built in it") && live) return default;

            // A new scene, empty: its camera is the user's rig the task's build makes (TaskKit.MakeRig).
            Scene scene = live ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single) : default;
            // The sun on the cabinet's front (section A's side), a softer fill on its back.
            AddLight(ctx, "Directional Light", "the sun on the cabinet's front", 1f, LightShadows.Soft, Quaternion.Euler(50f, 150f, 0f));
            AddLight(ctx, "Fill Light", "a softer fill on the cabinet's back", 0.5f, LightShadows.None, Quaternion.Euler(50f, -30f, 0f));
            if (ctx == null || ctx.ShouldCreate("FLOOR", "a floor to stand on"))
            {
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "FLOOR";
                floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                ctx.Created(floor);
            }
            // The task's manifest, beside its plan: what Tools > unitytk > Manifest opens on, to see each row
            // of the sheet with what it built here, and to build again. Made once the new scene is open: a new
            // scene unloads an asset nothing in it holds.
            Manifest manifest = ManifestsActions.Create(planText, ctx);
            if (manifest == null)
            {
                if (live) throw new InvalidOperationException($"{PlanFile} in {folder} made no manifest: see the Console");
                return default;  // a dry run's: the manifest is planned, nothing yet to build by
            }
            if (manifest.scene != path && (ctx == null || ctx.ShouldChange(manifest, "build in the demo scene")))
            {
                manifest.scene = path;
                ctx.Edited(manifest);
            }
            var settings = (TaskSettings)ManifestsActions.AddSettings(manifest, typeof(TaskSettings), ctx);
            Choose(settings, ctx);
            if (live)
            {
                // The manifest's file with its settings in it: a change of the settings alone dirties their own object, so
                // each is saved by its own (a dry run writes nothing).
                AssetDatabase.SaveAssetIfDirty(manifest);
                if (settings != null) AssetDatabase.SaveAssetIfDirty(settings);
            }
            ManifestsActions.Build(manifest, ctx);  // its delivery placed, every row built, the user's rig, its debug tools
            if (!live) return default;
            if (ManifestsActions.Placed(manifest.delivery) == null)
                throw new InvalidOperationException($"the task was not built: {PlanFile}'s manifest names no delivery in {folder}");
            EditorSceneManager.SaveScene(scene, path);
            return scene;
        }

        // A directional light named name, turned to facing, made where ctx says so.
        static void AddLight(OpContext ctx, string name, string why, float intensity, LightShadows shadows, Quaternion facing)
        {
            if (ctx != null && !ctx.ShouldCreate(name, why)) return;
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = shadows;
            light.transform.rotation = facing;
            ctx.Created(light.gameObject);
        }
    }
}
