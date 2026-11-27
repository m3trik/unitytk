using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// A builder only the tests register: it gives each target a BoxCollider, through the
    /// kernel's edits, as a real builder would through a feature's action.
    /// </summary>
    [TaskBehavior("test_collider")]
    public sealed class TestColliderBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (action.targets == null || action.targets.Count == 0) yield return "it names no target";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            foreach (string path in action.targets)
            {
                Transform node = scene.Node(path);
                if (node.GetComponent<BoxCollider>() != null) continue;
                if (ctx != null && !ctx.ShouldCreate($"{node.name}/{nameof(BoxCollider)}", "add a collider")) continue;
                ctx.AddComponent<BoxCollider>(node.gameObject);
            }
        }
    }
}
