using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.RenderEffects;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>set</c> ("The solenoid LED turns amber"): each target's indicator switches to its <see cref="SetSettings.state"/>
    /// (the sheet's <c>state</c>) as its step starts, and stays: an <see cref="EmissionStateSwitch"/> on the Emission Track
    /// that shows the target's material (<see cref="RenderEffectsActions.TrackOf"/>), which the step enables. The
    /// states are the delivery's (the authoring tool's Emissive Groups: off, green, blue, amber, red...). A target
    /// the delivery brought no track for, wearing a material someone gave an emission by hand, gets one from the
    /// build (<see cref="RenderEffectsActions.AddEmissionTrack(UnityEngine.GameObject, OpContext)"/>), with the
    /// states the authoring tool gives by default: the kit switches emissive states with no DCC's record.
    /// </summary>
    [TaskBehavior("set", Suffix = new[] { nameof(SetSettings.state) },
                  About = "The object's indicator light switches to its state (off, green, amber...) as its step starts, and stays.")]
    internal sealed class SetBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            foreach (string issue in Rows.NeedTargets(action)) yield return issue;
            string step = PlanOrder.StepOf(scene.Plan, action)?.id ?? "";
            foreach (string path in action.targets ?? new List<string>())
            {
                string state = scene.Settings<TaskSettings>(step, path, action).set.state;
                if (string.IsNullOrEmpty(state))
                {
                    yield return $"it names no state for '{path}' to switch to: set its state";
                    continue;
                }
                var node = scene.Node(path);
                if (node == null) continue;  // a node the delivery lacks is the scene's own issue
                EmissionTrack track = RenderEffectsActions.TrackOf(node.gameObject);
                UnityEngine.Material emits = track == null ? RenderEffectsActions.Emitting(node.gameObject) : null;
                if (track == null && emits == null)
                {
                    yield return $"'{path}' wears no material that emits, and no Emission Track shows it: it has no states " +
                                 "(give its material an emission, or the part an emissive group with states)";
                    continue;
                }
                // The states it has: its track's, else those the build gives the track it makes for its material.
                List<string> has = track != null ? track.states.ConvertAll(s => s.name)
                                                 : new List<EmissionState>(RenderEffectsActions.DefaultStates(emits)).ConvertAll(s => s.name);
                if (!has.Contains(state))
                    yield return $"'{path}' has no state '{state}'; it has " + (has.Count > 0 ? string.Join(", ", has) : "none");
            }
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            foreach (string path in action.targets)
            {
                string state = scene.Settings<TaskSettings>(step.id, path, action).set.state;
                UnityEngine.GameObject node = scene.Node(path).gameObject;
                // A dry run's: a track the build would make is planned, and what goes on it with it.
                PlannedObject planned = RenderEffectsActions.PlannedTrack(node);
                EmissionTrack track = RenderEffectsActions.AddEmissionTrack(node, ctx);
                if (track == null)
                {
                    if (RenderEffectsActions.AddStateSwitch(planned, state, ctx)) scene.EnableDuring<EmissionStateSwitch>(step.id, planned, ctx);
                    continue;
                }
                scene.Hold(track);
                // Null in a dry run: the step's enabling it is planned by its type.
                EmissionStateSwitch change = RenderEffectsActions.AddStateSwitch(track, state, ctx: ctx);
                scene.EnableDuring(step.id, track.gameObject, change, ctx);
            }
        }
    }
}
