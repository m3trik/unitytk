using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace M3trik.Unitytk.Lightmaps.URP.Tests
{
    /// <summary>
    /// What a URP project's renderers do to a lightmap set, named where every project meets it: a Forward+ renderer
    /// that reads probes through its atlas under a set's box-projected probe, and screen-space ambient occlusion over
    /// its maps; each with its fix, an action that asks first and then sets the renderer as its inspector would.
    /// </summary>
    public class UniversalLightmapRenderersTests
    {
        readonly List<Object> _made = new List<Object>();
        UniversalRenderPipelineAsset _pipeline;
        UniversalRendererData _renderer;
        ScreenSpaceAmbientOcclusion _occlusion;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // URP's template, as a person's project starts: Forward+, probes blended through the atlas, occlusion on.
            _renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            _renderer.name = "PC_Renderer";
            _renderer.renderingMode = RenderingMode.ForwardPlus;
            _occlusion = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            _occlusion.name = "ScreenSpaceAmbientOcclusion";
            _renderer.rendererFeatures.Add(_occlusion);
            _pipeline = UniversalRenderPipelineAsset.Create(_renderer);
            var settings = new SerializedObject(_pipeline);
            settings.FindProperty("m_ReflectionProbeBlending").boolValue = true;
            settings.FindProperty("m_ReflectionProbeAtlas").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            _made.AddRange(new Object[] { _renderer, _occlusion, _pipeline });
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (Object o in _made) Object.DestroyImmediate(o);
            _made.Clear();
        }

        // A set in the open scene, its renderer mapped, its probe box-projected or not.
        static void Set(bool boxed)
        {
            var room = new GameObject("Room");
            room.SetActive(false);  // validated, never bound
            var set = room.AddComponent<LightmapSet>();
            set.receivers.Add(new LightmapSet.Receiver { renderer = room.AddComponent<MeshRenderer>() });
            set.probe = new GameObject("Probe").AddComponent<ReflectionProbe>();
            set.probe.boxProjection = boxed;
        }

        static List<string> Codes(OpContext ctx) => ctx.Issues.Select(i => i.code).ToList();

        [Test]
        public void Validate_names_each_with_the_action_that_fixes_it()
        {
            Set(boxed: true);
            var ctx = new OpContext();
            LightmapsActions.Validate(_pipeline, ctx);
            Assert.That(Codes(ctx), Is.EquivalentTo(new[] { LightmapsActions.ProbeAtlasCode, LightmapsActions.OcclusionCode }));
            foreach (var (code, action) in new[] { (LightmapsActions.ProbeAtlasCode, nameof(LightmapsActions.ReadProbesWhole)),
                                                   (LightmapsActions.OcclusionCode, nameof(LightmapsActions.SwitchOcclusionOff)) })
            {
                var issue = ctx.Issues.Single(i => i.code == code);
                Assert.That(issue.fix.op, Is.EqualTo("unity.csharp.exec"));
                Assert.That((string)issue.fix.args["code"], Does.Contain("LightmapsActions." + action + "()"), "its fix runs the action");
            }

            var none = new OpContext();
            var plain = ScriptableObject.CreateInstance<UniversalRendererData>();
            UniversalRenderPipelineAsset forward = UniversalRenderPipelineAsset.Create(plain);
            _made.Add(plain);
            _made.Add(forward);
            LightmapsActions.Validate(forward, none);
            Assert.That(Codes(none), Is.Empty, "the Forward path, its probes whole and no occlusion: nothing to say");
        }

        [Test]
        public void An_atlas_is_named_only_under_a_box_projected_probe()
        {
            Set(boxed: false);
            var ctx = new OpContext();
            LightmapsActions.Validate(_pipeline, ctx);
            Assert.That(Codes(ctx), Is.EqualTo(new[] { LightmapsActions.OcclusionCode }), "an unboxed probe reads whole from the atlas");
        }

        [Test]
        public void Each_fix_is_planned_asked_and_made_once()
        {
            Set(boxed: true);
            var plan = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.ReadProbesWhole(_pipeline, plan), Is.EqualTo(1));
            Assert.That(LightmapsActions.SwitchOcclusionOff(_pipeline, plan), Is.EqualTo(1));
            Assert.That(plan.Changes.Select(c => (c.kind, c.target)),
                        Is.EqualTo(new[] { ("change", "PC_Renderer"), ("change", "ScreenSpaceAmbientOcclusion") }));
            Assert.That((_renderer.renderingMode, _occlusion.isActive), Is.EqualTo((RenderingMode.ForwardPlus, true)), "a plan changes nothing");

            var live = new OpContext();
            Assert.That(LightmapsActions.ReadProbesWhole(_pipeline, live), Is.EqualTo(1));
            Assert.That(LightmapsActions.SwitchOcclusionOff(_pipeline, live), Is.EqualTo(1));
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))),
                        "the plan names every change the run makes");
            Assert.That((_renderer.renderingMode, _occlusion.isActive), Is.EqualTo((RenderingMode.Forward, false)));

            var again = new OpContext();
            LightmapsActions.Validate(_pipeline, again);
            Assert.That(Codes(again), Is.Empty, "fixed: nothing to say");
            Assert.That(LightmapsActions.ReadProbesWhole(_pipeline, again) + LightmapsActions.SwitchOcclusionOff(_pipeline, again), Is.Zero);
        }

        [Test]
        public void Each_fix_is_one_undo_step_and_a_null_pipeline_is_none()
        {
            Set(boxed: true);
            Assert.That(LightmapsActions.ReadProbesWhole((RenderPipelineAsset)null, new OpContext(dryRun: true)), Is.Zero,
                        "null: no pipeline, as Validate reads it, so nothing to fix");
            Assert.That(LightmapsActions.SwitchOcclusionOff((RenderPipelineAsset)null, new OpContext(dryRun: true)), Is.Zero);
            OpContext.RunMenu("Read Probes Whole", ctx => LightmapsActions.ReadProbesWhole(_pipeline, ctx));
            OpContext.RunMenu("Switch Occlusion Off", ctx => LightmapsActions.SwitchOcclusionOff(_pipeline, ctx));
            Assert.That((_renderer.renderingMode, _occlusion.isActive), Is.EqualTo((RenderingMode.Forward, false)));
            Undo.PerformUndo();
            Assert.That(_occlusion.isActive, Is.True, "one Ctrl+Z switches its occlusion back on");
            Undo.PerformUndo();
            Assert.That(_renderer.renderingMode, Is.EqualTo(RenderingMode.ForwardPlus), "...and the one before gives the renderer its path back");
        }
    }
}
