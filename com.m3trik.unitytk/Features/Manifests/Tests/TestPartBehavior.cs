using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// A test-only behavior, <c>test_part</c>: a box collider on each target, a trigger where the
    /// row's value says <c>trigger</c>, which the row holds. What a feature's own action does for a
    /// kit's builder, without a feature: made once, brought in line where it differs, found made after.
    /// Its rows' <c>anchor</c> names a node (an assessment finds it); no other field of theirs does.
    /// Where the row's value says <c>note</c>, it says a note on each target as it builds, by a code of
    /// its own, as a kit's builder says a spot it could not find (<see cref="NoteCode"/>).
    /// </summary>
    [ManifestBehavior("test_part", Settings = new[] { nameof(TestSettings.box) }, Fields = new[] { "value" }, NodeFields = new[] { "anchor" })]
    public sealed class TestPartBehavior : IManifestBehavior, IManifestBound
    {
        /// <summary>The code of the note it says where its row's value says <c>note</c>.</summary>
        public const string NoteCode = "test.part.note";

        /// <summary>The words that note holds.</summary>
        public const string NoteWords = "built, and noted";

        /// <inheritdoc/>
        public IEnumerable<(string setting, Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                ManifestScene scene, string target)
        {
            Transform node = scene.Node(target);
            if (node != null && node.TryGetComponent(out BoxCollider part)) yield return (nameof(TestSettings.box), part, "m_Size");
        }

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (action.targets == null || action.targets.Count == 0) yield return "it names no target";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            bool trigger = action.Field("value") == "trigger";
            foreach (string path in action.targets)
            {
                Transform node = scene.Node(path);
                if (action.Field("value") == "note") ctx.Raise(NoteCode, $"{step.id}: '{node.name}' is {NoteWords}");
                if (!node.TryGetComponent(out BoxCollider part))
                {
                    if (ctx != null && !ctx.ShouldCreate(OpContextEdits.Key(node.gameObject, typeof(BoxCollider)), "add a collider")) continue;
                    part = ctx.AddComponent<BoxCollider>(node.gameObject);
                    part.isTrigger = trigger;
                }
                scene.Hold(part);
                Vector3 box = scene.Settings<TestSettings>(step.id, path, action).box;
                if (part.size != box && (ctx == null || ctx.ShouldChange(part, "size it")))
                {
                    part.size = box;
                    ctx.Edited(part);
                }
                if (part.isTrigger == trigger) continue;
                if (ctx != null && !ctx.ShouldChange(part, trigger ? "make it a trigger" : "make it solid")) continue;
                part.isTrigger = trigger;
                ctx.Edited(part);
            }
        }
    }
}
