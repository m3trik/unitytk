using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// A project's own verbs, each taking one of the kit's roles by its declaration alone, as a project
    /// registers them (<see cref="TaskBehaviorAttribute"/>): bringing the user somewhere
    /// (<see cref="TaskBehaviorAttribute.Travels"/>), saying its step goes on
    /// (<see cref="TaskBehaviorAttribute.GoesOn"/>), a tick a list lists (<see cref="TaskBehaviorAttribute.Partner"/>),
    /// sending a part home (<see cref="TaskBehaviorAttribute.Returns"/>). Only the tests register them, and they build
    /// nothing: what the kit does around them is the kit's.
    /// </summary>
    [TaskBehavior("test_walk", Travels = true, Places = true)]
    [TaskBehavior("test_go_on", NamesObjects = false, GoesOn = true)]
    [TaskBehavior("test_tick", NamesObjects = false, Partner = "list", Fields = new[] { "label" })]
    [TaskBehavior("test_send_home", Returns = true)]
    public sealed class TestRolesBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            yield break;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
        }
    }
}
