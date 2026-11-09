using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// The Render Effects sample, imported as the Package Manager imports it, through
    /// whichever pipeline the project draws with: two shots at 24 fps, FadeOut (frames
    /// 1-24) and FadeIn (25-48), one clip each. The box's Fade alpha is keyed out over
    /// frames 4-16 and back in over 28-40; the sphere's Highlight pulses once a shot over
    /// its mapped emission, which it sets aside while it glows; the panel blinks off on
    /// frame 8 and on again on 12, on its own active state, stepped; and the lamp group's
    /// weight, an Emission Track on the group's copy material, switches it on over
    /// frames 28-34. One twin per material and state is made into the model, no proxy is
    /// left, and the demo recipe builds its scene. The project these run in imports every
    /// sample of the package, so a sample that is not there fails: it is never skipped.
    /// </summary>
    public class RenderEffectsSampleTests
    {
        const string Package = "com.m3trik.unitytk";
        const string SampleName = "RenderEffects";
        const string Recipe = "M3trik.Unitytk.Samples.RenderEffects.RenderEffectsDemoRecipe, M3trik.Unitytk.Samples.RenderEffects.Editor";
        const float Fps = 24f;
        const string Lamp = "Lamp_MAT_Signal";

        static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            Assert.That(sample.displayName, Is.EqualTo(SampleName),
                        "the package lists no RenderEffects sample: its package.json's samples are written by the workspace's sample sync");
            Assert.That(sample.isImported, Is.True,
                        $"the RenderEffects sample is not imported at {sample.importPath}: import it from the Package Manager's " +
                        "Samples tab (the owned project's builder copies every sample in)");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        static string Model() => Folder() + "/RenderEffects.fbx";

        static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(Model());

        static AnimationClip Clip(string name)
            => AssetDatabase.LoadAllAssetsAtPath(Model()).OfType<AnimationClip>().Single(c => c.name == name);

        static AnimationCurve Curve(AnimationClip clip, string path, Type type, string property)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property));
            Assert.That(curve, Is.Not.Null, $"{clip.name}: {path} {type.Name}.{property}");
            return curve;
        }

        /// <summary><paramref name="curve"/> at <paramref name="frame"/> frames into its clip.</summary>
        static float At(AnimationCurve curve, float frame) => curve.Evaluate(frame / Fps);

        static IRenderPipelineHost Host()
        {
            var host = RenderPipelineHosts.Current;
            Assume.That(host != null, "a pipeline a unitytk host serves");
            return host;
        }

        /// <summary>Run <paramref name="body"/> over an instance of the model, its effects released and it gone after.</summary>
        static void OverAnInstance(Action<GameObject> body)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Prefab());
            try
            {
                body(instance);
            }
            finally
            {
                // Edit Mode calls no OnDisable: each writer gives its renderers back and frees its twins.
                foreach (RenderEffect effect in instance.GetComponentsInChildren<RenderEffect>(true)) effect.Release();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void The_box_fades_out_in_the_first_clip_and_back_in_the_second()
        {
            Assert.That(Prefab().transform.Find("Box").GetComponent<Fade>(), Is.Not.Null, "the record's opacity channel");
            AnimationCurve fadeOut = Curve(Clip("FadeOut"), "Box", typeof(Fade), nameof(Fade.alpha));
            AnimationCurve fadeIn = Curve(Clip("FadeIn"), "Box", typeof(Fade), nameof(Fade.alpha));
            Assert.That(At(fadeOut, 0f), Is.EqualTo(1f).Within(1e-4f), "shown at the first clip's start");
            Assert.That(At(fadeOut, 3f), Is.EqualTo(1f).Within(1e-4f), "frame 4, three frames in: the fade starts");
            Assert.That(At(fadeOut, 9f), Is.EqualTo(0.5f).Within(1e-3f), "half way, linear");
            Assert.That(At(fadeOut, 15f), Is.EqualTo(0f).Within(1e-4f), "frame 16: hidden");
            Assert.That(At(fadeIn, 0f), Is.EqualTo(0f).Within(1e-4f), "the second clip opens hidden, from its own start");
            Assert.That(At(fadeIn, 15f), Is.EqualTo(1f).Within(1e-4f), "frame 40, fifteen frames into it: shown");
            foreach (string clip in new[] { "FadeOut", "FadeIn" })
                Assert.That(AnimationUtility.GetCurveBindings(Clip(clip)).Where(b => b.propertyName == "m_Enabled" && b.path.StartsWith("Box")),
                            Is.Empty, $"{clip}: the Fade owns its presence, not Unity's renderer curve");

            OverAnInstance(instance =>
            {
                var renderer = instance.transform.Find("Box").GetComponent<Renderer>();
                Material original = renderer.sharedMaterial;
                var fade = instance.transform.Find("Box").GetComponent<Fade>();
                fade.alpha = 0.5f;
                fade.Apply();
                Assert.That(renderer.sharedMaterial.name, Is.EqualTo("Box_MAT (Blended)"), "its blended twin below 1");
                Assert.That(renderer.sharedMaterial.color.a, Is.EqualTo(original.color.a * 0.5f).Within(1e-4f));
                fade.alpha = 0f;
                fade.Apply();
                Assert.That(renderer.forceRenderingOff, Is.True, "hidden at 0");
                fade.alpha = 1f;
                fade.Apply();
                Assert.That(renderer.sharedMaterial, Is.SameAs(original), "opaque at rest: the shared original");
                Assert.That(renderer.forceRenderingOff, Is.False);
            });
        }

        [Test]
        public void The_sphere_glows_over_its_mapped_emission_and_rests_on_its_map()
        {
            var host = Host();
            var highlight = Prefab().transform.Find("Sphere").GetComponent<Highlight>();
            Assert.That(highlight, Is.Not.Null, "the record's highlight channel");
            Stage.AssertRgb(highlight.color, MaterialColor.Encode(new Color(0.2f, 0.6f, 1f)), "the record's colour, linear, as the field stores it");
            Stage.AssertRgb(highlight.colorDim, MaterialColor.Encode(Color.black), "the record's dim colour: black adds nothing");
            foreach (string name in new[] { "FadeOut", "FadeIn" })
            {
                AnimationCurve pulse = Curve(Clip(name), "Sphere", typeof(Highlight), nameof(Highlight.intensity));
                Assert.That(At(pulse, 0f), Is.EqualTo(0f).Within(1e-4f), $"{name}: dim at its start");
                Assert.That(At(pulse, 12f), Is.EqualTo(1f).Within(1e-4f), $"{name}: bright mid-shot");
                Assert.That(At(pulse, 23f), Is.EqualTo(0f).Within(1e-4f), $"{name}: dim at its end");
            }

            OverAnInstance(instance =>
            {
                var renderer = instance.transform.Find("Sphere").GetComponent<Renderer>();
                Material original = renderer.sharedMaterial;
                Assert.That(original.name, Is.EqualTo("Sphere_MAT"));
                Texture map = original.GetTexture(host.Lit.EmissionMap);
                Assert.That(map != null ? map.name : null, Is.EqualTo("Sphere_Emission"), "its emission is mapped (the materials record)");
                var glow = instance.transform.Find("Sphere").GetComponent<Highlight>();
                glow.intensity = 1f;
                glow.Apply();
                Material lit = renderer.sharedMaterial;
                Assert.That(lit.name, Is.EqualTo("Sphere_MAT (Glowing)"));
                Assert.That(lit.GetTexture(host.Lit.EmissionMap), Is.Null, "the map set aside while it glows");
                Stage.AssertRgb(MaterialColor.Decode(lit.GetColor(host.Lit.EmissionColor)), new Color(0.2f, 0.6f, 1f),
                                "over a map, the glow alone");
                glow.intensity = 0f;
                glow.Apply();
                Assert.That(renderer.sharedMaterial, Is.SameAs(original), "at rest the original, its map back");
            });
        }

        [Test]
        public void The_lamp_groups_weight_is_an_emission_track_on_its_copy_material()
        {
            var host = Host();
            var track = Prefab().transform.Find("data_export/" + Lamp).GetComponent<EmissionTrack>();
            Assert.That(track, Is.Not.Null, "the record's material entry, on an object of its own under the carrier");
            Assert.That(track.material.name, Is.EqualTo(Lamp), "the group's copy of its lamp material");
            Stage.AssertRgb(track.color, MaterialColor.Encode(new Color(1f, 0.8f, 0.3f)), "the record's colour");
            Assert.That(track.strength, Is.EqualTo(1f));
            Assert.That(track.weight, Is.EqualTo(0f).Within(1e-4f), "at rest the material as delivered: dark");
            string path = "data_export/" + Lamp;
            AnimationCurve first = Curve(Clip("FadeOut"), path, typeof(EmissionTrack), nameof(EmissionTrack.weight));
            Assert.That(new[] { At(first, 0f), At(first, 23f) }, Is.EqualTo(new[] { 0f, 0f }).Within(1e-4f), "off through the first shot");
            AnimationCurve second = Curve(Clip("FadeIn"), path, typeof(EmissionTrack), nameof(EmissionTrack.weight));
            Assert.That(At(second, 3f), Is.EqualTo(0f).Within(1e-4f), "frame 28: still off");
            Assert.That(At(second, 9f), Is.EqualTo(1f).Within(1e-4f), "frame 34: on");
            Assert.That(At(second, 23f), Is.EqualTo(1f).Within(1e-4f), "and held");

            OverAnInstance(instance =>
            {
                var renderer = instance.transform.Find("Panel").GetComponent<Renderer>();
                Material[] slots = renderer.sharedMaterials;
                Assert.That(slots.Select(m => m.name), Is.EquivalentTo(new[] { "Panel_MAT", Lamp }), "a two-material object");
                int lamp = Array.FindIndex(slots, m => m.name == Lamp);
                var weight = instance.transform.Find(path).GetComponent<EmissionTrack>();
                weight.weight = 1f;
                weight.Apply();
                Material on = renderer.sharedMaterials[lamp];
                Assert.That(on.name, Is.EqualTo(Lamp + " (Emitting)"));
                Stage.AssertRgb(MaterialColor.Decode(on.GetColor(host.Lit.EmissionColor)), new Color(1f, 0.8f, 0.3f),
                                "the record's colour x strength 1 x weight 1");
                Assert.That(renderer.sharedMaterials[1 - lamp], Is.SameAs(slots[1 - lamp]), "the body keeps its material");
                weight.weight = 0f;
                weight.Apply();
                Assert.That(renderer.sharedMaterials, Is.EqualTo(slots), "the shared originals at rest");
            });
        }

        [Test]
        public void The_panels_blink_is_its_own_active_state_stepped()
        {
            Assert.That(Prefab().transform.Find("Panel").GetComponent<RenderEffect>(), Is.Null, "a visibility is the node's own state");
            AnimationCurve blink = Curve(Clip("FadeOut"), "Panel", typeof(GameObject), "m_IsActive");
            Assert.That(At(blink, 6.5f), Is.EqualTo(1f), "shown until frame 8");
            Assert.That(At(blink, 7.5f), Is.EqualTo(0f), "off from it, stepped, never ramped");
            Assert.That(At(blink, 10.5f), Is.EqualTo(0f));
            Assert.That(At(blink, 11.5f), Is.EqualTo(1f), "on again from frame 12");
            Assert.That(blink.keys.All(k => float.IsInfinity(k.outTangent)), Is.True, "every key a step");
            AnimationCurve after = Curve(Clip("FadeIn"), "Panel", typeof(GameObject), "m_IsActive");
            Assert.That(after.keys.Select(k => k.value).Distinct(), Is.EqualTo(new[] { 1f }), "shown through the second shot");
            foreach (string clip in new[] { "FadeOut", "FadeIn" })
                Assert.That(AnimationUtility.GetCurveBindings(Clip(clip)).Where(b => b.propertyName == "m_Enabled" && b.path.StartsWith("Panel")),
                            Is.Empty, $"{clip}: the node's active state owns its presence, not Unity's renderer curve");
        }

        [Test]
        public void The_twins_are_made_into_the_model_and_no_proxy_is_left()
        {
            Host();
            GameObject model = Prefab();
            var effects = model.GetComponentsInChildren<RenderEffect>(true);
            Assert.That(effects.Select(e => e.GetType().Name), Is.EquivalentTo(new[] { "Fade", "Highlight", "EmissionTrack" }));
            MaterialTwins table = effects[0].twins;
            Assert.That(table, Is.Not.Null);
            Assert.That(effects.Select(e => e.twins).Distinct(), Is.EqualTo(new[] { table }), "one table for the model's effects");
            Assert.That(AssetDatabase.GetAssetPath(table), Is.EqualTo(Model()), "the model's own, rebuilt with it");
            Assert.That(table.twins.Select(t => (t.original.name, t.state)), Is.EquivalentTo(new[]
            {
                ("Box_MAT", TwinState.Blended), ("Sphere_MAT", TwinState.Glowing), (Lamp, TwinState.Emitting),
            }), "per material, each state its effects can ask for; none for the panel's body, which no effect covers");
            foreach (var twin in table.twins)
                Assert.That(AssetDatabase.GetAssetPath(twin.template), Is.EqualTo(Model()), $"{twin.template.name}: a sub-asset");

            Assert.That(model.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("__")), Is.Empty, "no proxy reaches the prefab");
            foreach (string clip in new[] { "FadeOut", "FadeIn" })
                Assert.That(AnimationUtility.GetCurveBindings(Clip(clip)).Where(b => b.path.Contains("__")), Is.Empty, $"{clip}: no proxy curve left");
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model());
            Assert.That(importer.resampleCurves, Is.False, "asked of the carrier: the curves import as keyed");
        }

        [Test]
        public void The_demo_recipe_builds_its_scene_with_no_errors()
        {
            string folder = Folder();
            Type recipe = Type.GetType(Recipe);
            Assert.That(recipe, Is.Not.Null, "the sample's editor assembly is missing: it did not compile, or the sample is not imported");
            try
            {
                var scene = (Scene)recipe.GetMethod("Build").Invoke(null, new object[] { folder });
                Assert.That(scene.path, Is.EqualTo(folder + "/RenderEffectsDemo.unity"), "saved beside the model");
                GameObject model = scene.GetRootGameObjects().Single(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == Prefab());
                Assert.That(model.GetComponentsInChildren<RenderEffect>(true), Has.Length.EqualTo(3), "the model's own effects, placed");
                var animator = model.GetComponent<Animator>();
                Assert.That(animator.runtimeAnimatorController, Is.Not.Null, "an Animator Controller plays the clips");
                var machine = ((UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController).layers[0].stateMachine;
                Assert.That(machine.states.Select(s => s.state.motion), Is.EqualTo(new[] { Clip("FadeOut"), Clip("FadeIn") }),
                            "a state per clip, in shot order, each playing the model's own clip");
                Assert.That(machine.defaultState.motion, Is.EqualTo(Clip("FadeOut")));
                // The lamp face's normal, from its own submesh: the camera looks at it, the sun lights it.
                Transform panel = model.transform.Find("Panel");
                Material[] slots = panel.GetComponent<Renderer>().sharedMaterials;
                Mesh mesh = panel.GetComponent<MeshFilter>().sharedMesh;
                int[] lamp = mesh.GetTriangles(Array.FindIndex(slots, m => m.name == Lamp));
                Vector3 facing = panel.TransformDirection(mesh.normals[lamp[0]]);
                Assert.That(Vector3.Dot(facing, Camera.main.transform.position - panel.position), Is.GreaterThan(0f), "the camera faces the lamp");
                Light sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l => l.type == LightType.Directional);
                Assert.That(Vector3.Dot(facing, -sun.transform.forward), Is.GreaterThan(0f), "the sun lights the side the camera sees");
                // An error logged fails the test (the Test Framework's own rule).
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
