using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Indicators;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>marker</c>: a ring on the floor (<see cref="Marker"/>) where the user is to go, put up or taken away as the
    /// row says (<see cref="TaskSettings.markerShow"/>). Shown ("Destination circle fades in"): at the row's target,
    /// or, where it names none, at the viewpoint the task's next row that <see cref="TaskBehaviorAttribute.Teleports"/>
    /// brings the user to, on as its step starts and left on until a hiding row's step; a hiding row later in its own step
    /// makes it shown while the step runs. Hidden ("Destination circle fades away"): every ring an earlier row showed goes as
    /// its step starts.
    /// </summary>
    [TaskBehavior("marker", Places = true, Settings = new[] { nameof(TaskSettings.markerShow) }, Suffix = new[] { nameof(TaskSettings.markerShow) },
                      About = "A ring on the floor where the user is to go: put up from its step on, or taken away (by its setting).")]
    internal sealed class MarkerBehavior : IManifestBehavior
    {
        /// <summary>Whether <paramref name="action"/>, a marker row of <paramref name="step"/>, takes the ring away.</summary>
        internal static bool Hides(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene)
            => scene.Settings<TaskSettings>(step.id, action.targets != null && action.targets.Count > 0 ? action.targets[0] : "", action).markerShow == Showing.Hide;

        /// <summary>The row whose viewpoint <paramref name="action"/> (a showing row naming no place) marks: the task's next that teleports.</summary>
        internal static (SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action) Teleport(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            foreach (var (step, later) in PlanOrder.From(scene.Plan, action))
                if (TaskBehaviors.Traits(later.behavior).Teleports) return (step, later);
            return default;
        }

        /// <summary>
        /// Where <paramref name="action"/> (a showing row) puts its ring: its target, else the next teleport's viewpoint (placed
        /// where this run builds that teleport, else as it stands); null when neither, or in a dry run that plans the viewpoint.
        /// </summary>
        internal static Transform Spot(SheetPlan.Shape.PlanAction action, ManifestScene scene, OpContext ctx, bool make = true)
        {
            if (action.targets != null && action.targets.Count > 0) return scene.Node(action.targets[0]);
            var (step, teleport) = Teleport(action, scene);
            if (teleport == null) return null;
            if (!make || !Builds(step, teleport, scene)) return Spots.Existing(scene, step, teleport);
            GameObject node = Spots.Viewpoint(scene, step, teleport, ctx);
            return node == null ? null : node.transform;
        }

        // Whether the run under way builds teleport, a row of step: its step is built, and the row is ready (the guard
        // TaskKit.PlaceViewpoints places by). A teleport held back, or in a step left as it stands, places nothing for a ring.
        static bool Builds(SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction teleport, ManifestScene scene)
        {
            if (!scene.Builds(step.id)) return false;
            if (scene.Assessment == null) return true;
            foreach (var (_, each, row) in PlanOrder.Assessed(scene.Plan, scene.Assessment))
                if (each == teleport) return row.state == ManifestAssessment.State.Ready;
            return false;
        }

        // Whether a hiding row of action's behavior comes after action in step.
        static bool HiddenLaterInStep(SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            bool after = false;
            foreach (var each in step.actions ?? new List<SheetPlan.Shape.PlanAction>())
            {
                if (after && each.behavior == action.behavior && Hides(each, step, scene)) return true;
                after |= each == action;
            }
            return false;
        }

        // The showing rows of action's behavior before action (in other steps only, where otherSteps) whose ring has a place.
        static IEnumerable<SheetPlan.Shape.PlanAction> Shows(SheetPlan.Shape.PlanAction action, ManifestScene scene, bool otherSteps = false)
            => PlanOrder.Before(scene.Plan, action)
                        .Where(each => each.action.behavior == action.behavior && !Hides(each.action, each.step, scene))
                        .Where(each => !otherSteps || !(each.step.actions?.Contains(action) ?? false))
                        .Where(each => (each.action.targets?.Count ?? 0) > 0 || Teleport(each.action, scene).action != null)
                        .Select(each => each.action);

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            SheetPlan.Shape.PlanStep step = PlanOrder.StepOf(scene.Plan, action);
            if (step != null && Hides(action, step, scene))
            {
                if (!Shows(action, scene).Any()) yield return "no row before it shows a marker";
                yield break;
            }
            if (action.targets != null && action.targets.Count > 0) yield break;
            var (at, teleport) = Teleport(action, scene);
            if (teleport == null)
            {
                yield return "it names no place, and no teleport follows it: name where the marker goes";
                yield break;
            }
            // The ring marks that teleport's viewpoint: a teleport that cannot be built places none, so neither is this row.
            foreach (string why in ManifestsActions.Problems(teleport, scene))
            {
                yield return $"the teleport it marks ({at.id}: '{teleport.text}') cannot be built: {why}";
                yield break;
            }
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            if (Hides(action, step, scene))
            {
                foreach (Transform shown in Shows(action, scene, otherSteps: true).Select(each => Spot(each, scene, null, make: false))
                                                                                .Where(spot => spot != null).Distinct())
                    scene.DisableFrom(step.id, shown.gameObject, IndicatorsActions.AddMarker(shown.gameObject, ownMesh: true, ctx: ctx), ctx);
                return;
            }
            Transform spot = Spot(action, scene, ctx);
            bool during = HiddenLaterInStep(step, action, scene);
            if (spot == null)
            {
                var (at, teleport) = Teleport(action, scene);
                if (teleport == null) return;  // it names its place, which its check found: never here
                if (!Builds(at, teleport, scene))
                {
                    // Its viewpoint is a step's this run leaves as it stands, and stands nowhere yet: nothing to put a ring on.
                    ctx.Raise(TaskKit.SpotCode, $"{step.id}: the viewpoint its ring marks ({at.id}'s) stands nowhere yet: build {at.id} too", "warning");
                    return;
                }
                // A dry run's, the viewpoint the kit places only planned: its ring and its switching, planned on it.
                PlannedObject view = Viewpoints.ViewpointsActions.PlannedViewpoint(TaskKit.ViewpointsName(scene),
                                                                                   TaskKit.ViewpointName(at, teleport, scene.Settings<TaskSettings>()));
                IndicatorsActions.AddMarker(view, ctx);
                if (during) scene.EnableDuring<Marker>(step.id, view, ctx);
                else scene.EnableFrom<Marker>(step.id, view, ctx);
                return;
            }
            GameObject node = spot.gameObject;
            Marker marker = IndicatorsActions.AddMarker(node, ownMesh: true, ctx: ctx);
            if (during) scene.EnableDuring(step.id, node, marker, ctx);
            else scene.EnableFrom(step.id, node, marker, ctx);
        }
    }
}
