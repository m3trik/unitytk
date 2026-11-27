using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.Panels;
using M3trik.Unitytk.Placement;
using M3trik.Unitytk.Steps;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// The Tasks kit's part of every build of a plan that holds a task's rows
    /// (<see cref="IManifestKit"/>): before any row, the sequence the steps run in and a step per
    /// plan step (<c>Task (&lt;task&gt;)</c>), the task's placard where a step shows a page on it
    /// (<see cref="TaskSettings.pages"/>), what the hand holds made holdable, the delivery's authoring
    /// anchors hidden, the viewpoints the kit places and, where the scene has no rig of its own, the user's
    /// (<see cref="TaskKit.MakeRig"/>); after the rows, the placard's page per step and each step's
    /// sound, a rig just made stood before the first viewpoint, the debug tools where the settings ask
    /// for them (<see cref="TaskSettings.rig"/>, <see cref="TaskSettings.debugTools"/>: a manifest's, or the
    /// ones a script hands the scene), and, for a manifest's build, the twins its fades and highlights draw with
    /// (<see cref="TaskScene.TwinsPath"/>), so a player keeps their shader variants and nothing is made as it plays.
    /// A person's adjustments to the spots it places are kept in its manifest (<see cref="Manifest.moved"/>), with its other adjustments.
    /// </summary>
    public sealed class TaskBuild : IManifestKit
    {
        /// <inheritdoc/>
        public Type Settings => typeof(TaskSettings);

        /// <inheritdoc/>
        public void Begin(ManifestScene scene, OpContext ctx)
        {
            SheetPlan.Shape.Payload plan = scene.Plan;
            TaskState state = scene.State<TaskState>();
            state.Sequence = TaskKit.SequenceName(scene);
            TaskKit.SayUnread(plan, ctx);  // once a run: what a settings file gives the kit, which it does not read
            state.Worked.Clear();  // a run places each working spot once: none is placed yet
            scene.Hold(StepsActions.CreateSequence(state.Sequence, ctx));
            foreach (var step in plan.steps)
            {
                if (!scene.Builds(step.id)) continue;
                using (scene.For(step.id))
                {
                    scene.Hold(StepsActions.AddStep(state.Sequence, step.id, string.IsNullOrEmpty(step.name) ? step.id : step.name, ctx));
                    // A step whose rows changed since it was built (one turned off, one the sheet dropped) is wired anew.
                    StepsActions.BuiltFrom(state.Sequence, step.id, TaskKit.BuiltFrom(scene.Source(step.id)), ctx);
                }
            }
            if (TaskPage.PlacardNeeded(plan, scene))
            {
                string placard = TaskKit.PlacardName(scene);
                Placard made = PanelsActions.MakePlacard(placard, ctx);
                scene.Hold(made);
                if (made != null) StepsActions.ChooseOn(made.gameObject, made, made.onChosen, state.Sequence, ctx);
                else StepsActions.ChooseOn(new PlannedObject(placard), state.Sequence, ctx);
            }
            TaskKit.MakeHoldables(plan, scene, scene.Assessment, ctx);
            TaskKit.HideAnchors(scene, ctx);
            TaskKit.PlaceViewpoints(plan, scene, scene.Assessment, ctx);
            TaskKit.MakeRig(scene, ctx);  // as its settings ask (TaskSettings.rig)
        }

        // The task's picture leaves out its arrows: a close-up of a part with the arrow at it drawn over it (the dials'
        // picture was a third arrow). Every arrow the task shows, the kit's and the delivery's own it was given.
        static void HideCues(ManifestScene scene, OpContext ctx)
        {
            GameObject made = SceneRoots.Find(TaskKit.PipName(scene));
            PipView view = made == null ? null : made.GetComponent<PipView>();
            if (view == null)
            {
                // A dry run's first build: the picture is planned with its rows, and so is what it leaves out, where a row
                // of the plan points something out.
                PlannedObject planned = PanelsActions.PlannedPip(TaskKit.PipName(scene), ctx);
                if (planned != null && PointsOut(scene)) PanelsActions.SetHidden(planned, ctx);
                return;
            }
            if (Indicators.IndicatorsActions.PlansArrows(ctx))
            {
                // A dry run that makes or takes away arrows: what the picture leaves out changes with them, as the live run's.
                PanelsActions.SetHidden(new PlannedObject(made.name), ctx);
                return;
            }
            var arrows = new List<GameObject>();
            foreach (GameObject root in scene.Roots)
                if (root != null)
                    foreach (Indicators.Indicator arrow in root.GetComponentsInChildren<Indicators.Indicator>(true))
                        arrows.Add(arrow.gameObject);
            PanelsActions.SetHidden(view, arrows, ctx);
        }

        // Whether a row the run builds points something out with an object of its own (IPointsOut).
        static bool PointsOut(ManifestScene scene)
        {
            foreach (var step in scene.Plan.steps)
                if (step.actions != null && scene.Builds(step.id))
                    foreach (var action in step.actions)
                        if (ManifestBehaviors.Find(action.behavior) is IPointsOut) return true;
            return false;
        }

        /// <inheritdoc/>
        public void End(ManifestScene scene, OpContext ctx)
        {
            SheetPlan.Shape.Payload plan = scene.Plan;
            GoOn(plan, scene, ctx);
            TaskPage.BuildAll(plan, TaskKit.PlacardName(scene), scene, ctx);
            TaskKit.BuildVoices(plan, scene, ctx);
            TaskKit.StartRig(plan, scene, ctx);
            HideCues(scene, ctx);
            TaskSettings settings = scene.Settings<TaskSettings>();
            if (settings.debugTools) scene.Hold(TaskKit.AddDebugTools(plan, settings, ctx));
            else PanelsActions.RemoveDebugPanel(TaskKit.DebugName(scene), ctx);  // its settings ask for none
            string twins = TaskScene.TwinsPath(scene.Manifest);
            if (twins.Length > 0)
                foreach (GameObject root in scene.Roots)
                    if (root != null) RenderEffects.RenderEffectsActions.PrepareMaterials(root, twins, ctx);
        }

        // Each step that leaves the user no choice (no row Chooses), or that a row which GoesOn says goes on, goes on
        // by itself once what it awaits is done and it has been heard or read: going on is the default, so a sheet
        // need not say "Auto Advance" on every step. A step whose going-on row this manifest turned off waits, and
        // every step does where the manifest turns off a behavior that says so (advance, everywhere).
        static void GoOn(SheetPlan.Shape.Payload plan, ManifestScene scene, OpContext ctx)
        {
            Manifest manifest = scene.Manifest;
            List<string> goesOn = TaskBehaviors.With(t => t.GoesOn).ToList();
            if (manifest != null && goesOn.Any(manifest.off.Contains)) return;
            foreach (var step in plan.steps)
            {
                if (!scene.Builds(step.id) || !GoesOn(step, scene)) continue;
                if (manifest != null && goesOn.Any(b => manifest.EditOf(step.id, "", b) is Manifest.Edit edit && !edit.add)) continue;
                using (scene.For(step.id))
                {
                    StepsActions.SetAutoAdvance(scene.Sequence(), step.id, true, ctx);
                    StepsActions.SetDwell(scene.Sequence(), step.id, TaskKit.DwellOf(step, scene), ctx);
                }
            }
        }

        // Whether step goes on by itself: a row says so, or no row has the user choose (a gate at its start
        // only starts it: once started, it goes on by itself).
        static bool GoesOn(SheetPlan.Shape.PlanStep step, ManifestScene scene)
        {
            if (step.actions == null) return true;
            bool chooses = false;
            foreach (var action in step.actions)
            {
                TaskBehaviorAttribute traits = TaskBehaviors.Traits(action.behavior);
                if (traits.GoesOn) return true;
                IStepGate gate = TaskBehaviors.Gate(action.behavior);
                if (gate != null && gate.AtStart(action, step, scene)) continue;
                chooses |= traits.Chooses;
            }
            return !chooses;
        }

        /// <inheritdoc/>
        public void Keep(Manifest manifest, ManifestScene scene, OpContext ctx)
        {
            // Only this task's spots: another plan built into the scene keeps its own in its own manifest.
            Placements.Tuning kept = TaskScene.Kept(manifest, out List<Manifest.Value> unread);
            foreach (Manifest.Value value in unread)
                ctx.Raise(TaskKit.KeptCode, $"the place kept for the moved helper '{value.name}' reads as none ({value.value}): it is kept as it is " +
                                            "and puts nothing back; correct it in the manifest, or Revert to drop it", "warning");
            if (Placements.Capture(kept, Roots(manifest, scene)))
            {
                int count = kept.spots.Count;
                if (ctx == null || ctx.ShouldChange(manifest, $"keep {count} moved helper{(count == 1 ? "" : "s")}"))
                {
                    manifest.moved.Clear();
                    foreach (Placements.Tuned tuned in kept.spots)
                        manifest.moved.Add(new Manifest.Value { name = tuned.key, value = Placements.Write(tuned) });
                    manifest.moved.AddRange(unread);
                    ctx.Edited(manifest);
                }
            }
            if (scene != null) scene.State<TaskState>().Tuning = kept;  // a dry run writes nothing: its build still plans by what would be kept
        }

        // What the manifest's build made its spots under, in the open scene: its delivery (and the extras built with it),
        // and the task's own top-level objects (the viewpoints it places, its picture's, its placard and debug panel).
        static IEnumerable<GameObject> Roots(Manifest manifest, ManifestScene scene)
        {
            var roots = new List<GameObject>();
            if (scene != null) roots.AddRange(scene.Roots);
            else if (manifest.delivery != null) roots.Add(ManifestsActions.Placed(manifest.delivery));
            SheetPlan.Shape.Payload plan = scene != null ? scene.Plan : null;
            if (plan == null) plan = ManifestsActions.Plan(manifest, out _);
            TaskSettings settings = manifest.Settings<TaskSettings>();
            foreach (string name in new[]
                     {
                         TaskKit.ViewpointsName(plan, settings), TaskKit.PipName(plan, settings),
                         TaskKit.PlacardName(plan, settings), TaskKit.DebugName(plan, settings),
                     })
                roots.Add(SceneRoots.Find(name));
            roots.RemoveAll(root => root == null);
            return roots;
        }

        /// <inheritdoc/>
        public void Forget(Manifest manifest, OpContext ctx)
        {
            if (manifest.moved.Count == 0) return;
            if (ctx != null && !ctx.ShouldChange(manifest, "drop the places of the helpers a person moved")) return;
            manifest.moved.Clear();
            ctx.Edited(manifest);
        }

        /// <inheritdoc/>
        public IEnumerable<ManifestAdjustment> Kept(Manifest manifest, ManifestScene scene)
        {
            var kept = new List<ManifestAdjustment>();
            foreach (PlacedSpot spot in Moved(manifest, scene))
                kept.Add(ManifestAdjustment.Of(spot, "place", Said(spot.Relative), Said(new Pose(spot.placedPosition, spot.placedRotation))));
            return kept;
        }

        /// <inheritdoc/>
        public int Revert(Manifest manifest, ManifestScene scene, Component part, OpContext ctx)
        {
            int gone = 0;
            foreach (PlacedSpot spot in Moved(manifest, scene))
                if ((part == null || part == spot) && Placements.Restore(spot, ctx)) gone++;
            if (gone > 0) Keep(manifest, scene, ctx);  // its manifest drops them at once: the scene stands as built
            return gone;
        }

        /// <inheritdoc/>
        public int PlaceAgain(Manifest manifest, ManifestScene scene, Component part, OpContext ctx)
        {
            if (scene == null) return 0;
            // The part's node, read once: placing a spot anew takes it off, and a part taken off reads as none (every part).
            GameObject only = part == null ? null : part.gameObject;
            var keys = new List<string>();
            foreach (GameObject root in Roots(manifest, scene))
                foreach (PlacedSpot spot in root.GetComponentsInChildren<PlacedSpot>(true))
                {
                    // A helper someone made and a row names is no search's: it stays.
                    if (string.IsNullOrEmpty(spot.key) || spot.key.StartsWith(Spots.GivenKey, StringComparison.Ordinal)) continue;
                    if (only != null && spot.gameObject != only) continue;
                    if (keys.Contains(spot.key)) continue;
                    keys.Add(spot.key);
                    Placements.PlaceAgain(spot, ctx);
                }
            // A move a person made of one goes too: the search wins.
            if (manifest.moved.Exists(v => keys.Contains(v.name)) && (ctx == null || ctx.ShouldChange(manifest, "drop the moves made of what is placed anew")))
            {
                manifest.moved.RemoveAll(v => keys.Contains(v.name));
                ctx.Edited(manifest);
                if (scene.State<TaskState>().Tuning != null) scene.State<TaskState>().Tuning.spots.RemoveAll(t => keys.Contains(t.key));
            }
            return keys.Count;
        }

        // The task's spots a person moved from where the build placed them (an arrow, a viewpoint, a picture's camera).
        static List<PlacedSpot> Moved(Manifest manifest, ManifestScene scene)
        {
            var moved = new List<PlacedSpot>();
            if (scene == null) return moved;
            foreach (GameObject root in Roots(manifest, scene))
                foreach (PlacedSpot spot in root.GetComponentsInChildren<PlacedSpot>(true))
                    if (spot.Adjusted && !moved.Contains(spot)) moved.Add(spot);
            return moved;
        }

        // A spot's place in words, relative to the part it is placed by.
        static string Said(Pose pose)
        {
            Vector3 at = pose.position, turned = pose.rotation.eulerAngles;
            return FormattableString.Invariant($"at {at.x:0.###},{at.y:0.###},{at.z:0.###} turned {turned.x:0.#},{turned.y:0.#},{turned.z:0.#}");
        }
    }
}
