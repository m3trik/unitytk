using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Indicators;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using M3trik.Unitytk.Viewpoints;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>teleport</c> ("The user is teleported facing the door", or "... to VIEW_FRONT, facing the
    /// door"): a viewpoint its step brings the user to as it starts, or, where the row comes after a row of the
    /// step that <see cref="TaskBehaviorAttribute.EndsStep"/> ("Selecting 'Next' ..."), as it ends; facing its forward, or, where the row
    /// names a viewpoint and also says what they face, a place beside it turned toward that
    /// (<see cref="ViewpointsActions.AddFacing(GameObject, Transform, OpContext)"/>), the user brought within the task's
    /// <see cref="TaskSettings.standOff"/> of it. The viewpoint is the row's node where it names one, else one the kit
    /// places for the step (<see cref="TaskKit.ViewpointName"/>, before any row is built:
    /// <see cref="TaskBehaviorAttribute.Teleports"/>): on the floor where what the row faces is seen, as the scene then
    /// stands (a door an earlier step opened stands open), within reach of what the rows there have the user act on
    /// (<see cref="Placement.Placer.DefaultReach"/>), facing it; the user stands on it exactly as it stands, so a person
    /// moves and turns it like any object, kept by its manifest (<see cref="Manifest.moved"/>).
    /// Who is brought is the scene's <see cref="Traveler"/> (the user's rig), found as the step runs, so
    /// the build asks for no rig. A viewpoint with a mesh of its own (a destination disc) gets a
    /// <see cref="Marker"/> showing it, off: the disc shows only where a showing <c>marker</c> row turns it on. Where a spot is
    /// searched for (<see cref="TaskSettings.standOff"/>, <see cref="TaskSettings.viewAngle"/>) is the task's, never a row's:
    /// a person moves the spot itself.
    /// </summary>
    [TaskBehavior("teleport", Fields = new[] { "facing", "side", "side_of" }, Places = true, Teleports = true,
                      About = "The user is brought to a viewpoint: one the row names, else a spot the build places where what they face is seen.")]
    internal sealed class TeleportBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            bool named = !Spots.Placed(action);
            if (!named && string.IsNullOrEmpty(action.Field(TaskKit.Fields.Facing)))
                yield return "it names neither a viewpoint nor what the user faces: say what they face (\", facing the door\")";
            else if (named && action.targets.Count > 1) yield return $"it names {action.targets.Count} viewpoints: name one";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            GameObject node = Spots.Viewpoint(scene, step, action, ctx);
            if (node == null)
            {
                Planned(action, step, scene, ctx);  // a dry run's: the viewpoint the kit places, and its spot, only planned
                return;
            }
            bool placed = Spots.Placed(action);
            if (placed || node.TryGetComponent(out MeshRenderer _)) IndicatorsActions.AddMarker(node, ownMesh: true, ctx: ctx);
            TravelTo travel;
            Transform at;  // the travel's object, where it is there (a dry run may plan the place facing the part)
            string place;
            string faced = action.Field(TaskKit.Fields.Facing);
            if (string.IsNullOrEmpty(faced) || placed)
            {
                // Stood on as it stands, facing its forward: one an artist made, and one the kit placed (facing what its
                // row faces, where the user stands), which a person then moves and turns like any object.
                travel = ViewpointsActions.AddTravelTo(node, ctx: ctx);
                at = node.transform;
                place = node.name;
                if (!placed)
                {
                    FacesAway(node.transform, action, step, scene, ctx);
                    Spots.Given(scene, node.transform, ctx);
                }
                else ViewpointsActions.RemoveFacing(node, scene.Node(faced), ctx);  // the place an earlier build turned toward the part
            }
            else
            {
                // One the delivery authored, with what the user faces from it: a place to stand toward the part from.
                Transform facing = scene.Node(faced);
                Spots.Given(scene, node.transform, ctx);
                travel = ViewpointsActions.AddTravelTo(node, facing, scene.Settings<TaskSettings>().standOff, ctx: ctx);
                place = ViewpointsActions.FacingName(node, facing);
                at = node.transform.Find(place);
            }
            bool atEnd = PlanOrder.AfterAdvance(step, action);
            if (at == null)  // a dry run's: the place facing the part only planned
            {
                var planned = new PlannedObject(place, node.transform);
                if (atEnd) scene.EnableAtEnd<TravelTo>(step.id, planned, ctx);
                else scene.EnableDuring<TravelTo>(step.id, planned, ctx);
            }
            else if (atEnd) scene.EnableAtEnd(step.id, at.gameObject, travel, ctx);
            else scene.EnableDuring(step.id, at.gameObject, travel, ctx);
        }

        // A viewpoint an artist made is stood on as it stands, facing its forward: where the row also says what the user
        // is brought to face (its Target) and the viewpoint looks the other way, that is said, never turned for them.
        static void FacesAway(Transform view, SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string faces = action.Field(TaskKit.Fields.Faces);
            Transform about = string.IsNullOrEmpty(faces) ? null : scene.Node(faces);
            if (about == null) return;
            // Its middle as its travel faces it (TravelTo.Stand): what it shows, a renderer switched off for now too.
            Vector3 centre = SceneBounds.Of(about, includeDisabled: true).center;
            Vector3 to = Vector3.ProjectOnPlane(centre - view.position, Vector3.up), forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            if (to.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-6f || Vector3.Dot(to.normalized, forward.normalized) >= 0f) return;
            ctx.Raise(TaskKit.FacesAwayCode, $"{step.id}: the viewpoint '{view.name}' faces away from '{about.name}', which its row says the user is " +
                                             "brought to face: turn it where it was made (the user faces its forward, its blue axis), or clear the row's Target");
        }

        // A dry run's, where the viewpoint the kit places is only planned (its spot with it, Spots.Viewpoint): what the
        // live run makes on it, by the features' own keys (its ring, the place facing the part, the travel).
        static void Planned(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string view = TaskKit.ViewpointName(step, action, scene.Settings<TaskSettings>()), holder = TaskKit.ViewpointsName(scene);
            PlannedObject viewpoint = ViewpointsActions.PlannedViewpoint(holder, view);
            IndicatorsActions.AddMarker(viewpoint, ctx);
            ViewpointsActions.AddTravelTo(viewpoint, ctx);
            if (PlanOrder.AfterAdvance(step, action)) scene.EnableAtEnd<TravelTo>(step.id, viewpoint, ctx);
            else scene.EnableDuring<TravelTo>(step.id, viewpoint, ctx);
        }
    }
}
