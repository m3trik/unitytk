using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Indicators;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Viewpoints;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>travel</c> ("The user is brought to face the door", or "... to VIEW_FRONT"): its step brings the user to a viewpoint
    /// as it starts, or, by its <see cref="TravelSettings.when"/> (End), as it ends, once its choice is made. The viewpoint is
    /// the row's <c>where</c> where it names one, an artist's, stood on exactly as it stands, facing its forward (one that faces
    /// away from the row's Target, what the user is brought to face, is said, never turned for them:
    /// <see cref="TaskKit.FacesAwayCode"/>); else one the kit places for the step (<see cref="TaskKit.ViewpointName(SheetPlan.Shape.PlanStep, SheetPlan.Shape.PlanAction, TaskSettings)"/>,
    /// before any row is built: <see cref="TaskBehaviorAttribute.Travels"/>): on the floor where what the row's Target names is
    /// seen, as the scene then stands (a door an earlier step opened stands open), within reach of what the rows there have
    /// the user act on (<see cref="Placement.Placer.DefaultReach"/>), facing it; the user stands on it exactly as it stands, so
    /// a person moves and turns it like any object, kept by its manifest (<see cref="Manifest.moved"/>). Who is brought is the
    /// scene's <see cref="Traveler"/> (the user's rig), found as the step runs, so the build asks for no rig. A viewpoint the
    /// kit places, or one with a mesh of its own (a destination disc), gets a <see cref="Marker"/>, the ring on its floor,
    /// shown for the row's <see cref="TravelSettings.ring"/> (a span: <c>step</c>, or until a named step starts; none: off).
    /// Where a spot is searched for (<see cref="TaskSettings.standOff"/>, <see cref="TaskSettings.viewAngle"/>) is the task's,
    /// never a row's: a person moves the spot itself.
    /// </summary>
    [TaskBehavior("travel", Fields = new[] { TaskKit.Fields.Side, TaskKit.Fields.SideOf }, Places = true, Travels = true,
                  RowSettings = new[] { nameof(TravelSettings.when), nameof(TravelSettings.ring) }, Suffix = new[] { nameof(TravelSettings.when) },
                  About = "The user is brought to a viewpoint: the one its where names, else a spot the build places where what they face is seen; its ring shows for its span.")]
    internal sealed class TravelBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            List<string> where = action.FieldList(TaskKit.Fields.Where);
            int named = where == null ? 0 : where.FindAll(each => !string.IsNullOrEmpty(each)).Count;
            int faced = action.targets == null ? 0 : action.targets.Count;
            if (named == 0 && faced == 0) yield return "it names neither a viewpoint nor what the user faces: set its Target, or its where";
            if (named > 1) yield return $"it names {named} viewpoints: name one";
            if (faced > 1) yield return $"it names {faced} things to face: name one (a group holding them)";
            SheetPlan.Shape.PlanStep step = PlanOrder.StepOf(scene.Plan, action);
            string ring = Spans.Problem(scene.Plan, step, scene.Settings<TaskSettings>(step?.id ?? "", "", action).travel.ring, "ring");
            if (ring != null) yield return ring;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            TravelSettings own = scene.Settings<TaskSettings>(step.id, "", action).travel;
            bool atEnd = own.when == Moment.End;
            GameObject node = Spots.Viewpoint(scene, step, action, ctx);
            if (node == null)
            {
                Planned(action, step, scene, own, ctx);  // a dry run's: the viewpoint the kit places, and its spot, only planned
                return;
            }
            bool placed = Spots.Placed(action), ringed = !string.IsNullOrEmpty(own.ring);
            // Its ring: off but for its span (one the kit placed, and a disc the delivery's has, always carry one).
            Marker ring = placed || ringed || node.TryGetComponent(out MeshRenderer _) ? IndicatorsActions.AddMarker(node, ownMesh: true, ctx: ctx) : null;
            if (ringed) Spans.Show(scene, step, own.ring, node, ring, ctx);
            // Stood on as it stands, facing its forward: one an artist made, and one the kit placed (facing what its row
            // faces, where the user stands), which a person then moves and turns like any object.
            TravelTo travel = ViewpointsActions.AddTravelTo(node, ctx: ctx);
            if (!placed)
            {
                FacesAway(node.transform, action, step, scene, ctx);
                Spots.Given(scene, node.transform, ctx);
            }
            if (atEnd) scene.EnableAtEnd(step.id, node, travel, ctx);
            else scene.EnableDuring(step.id, node, travel, ctx);
        }

        // A viewpoint an artist made is stood on as it stands, facing its forward: where the row also says what the user
        // is brought to face (its Target) and the viewpoint looks the other way, that is said, never turned for them.
        static void FacesAway(Transform view, SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            Transform about = Spots.Faced(scene, action);
            if (about == null) return;
            // Its middle as its travel faces it (TravelTo.Stand): what it shows, a renderer switched off for now too.
            Vector3 centre = SceneBounds.Of(about, includeDisabled: true).center;
            Vector3 to = Vector3.ProjectOnPlane(centre - view.position, Vector3.up), forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (to.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-6f || Vector3.Dot(to.normalized, forward.normalized) >= 0f) return;
            ctx.Raise(TaskKit.FacesAwayCode, $"{step.id}: the viewpoint '{view.name}' faces away from '{about.name}', which its row says the user is " +
                                             "brought to face: turn it where it was made (the user faces its forward, its blue axis), or clear the row's Target");
        }

        // A dry run's, where the viewpoint the kit places is only planned (its spot with it, Spots.Viewpoint): what the
        // live run makes on it, by the features' own keys (its ring, the travel).
        static void Planned(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, TravelSettings own, OpContext ctx)
        {
            string view = TaskKit.ViewpointName(step, action, scene.Settings<TaskSettings>()), holder = TaskKit.ViewpointsName(scene);
            PlannedObject viewpoint = ViewpointsActions.PlannedViewpoint(holder, view);
            IndicatorsActions.AddMarker(viewpoint, ctx);
            if (!string.IsNullOrEmpty(own.ring)) Spans.Show<Marker>(scene, step, own.ring, viewpoint, ctx);
            ViewpointsActions.AddTravelTo(viewpoint, ctx);
            if (own.when == Moment.End) scene.EnableAtEnd<TravelTo>(step.id, viewpoint, ctx);
            else scene.EnableDuring<TravelTo>(step.id, viewpoint, ctx);
        }
    }
}
