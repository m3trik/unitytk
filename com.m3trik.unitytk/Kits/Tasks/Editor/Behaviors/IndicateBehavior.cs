using M3trik.Unitytk.Manifests;
using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using M3trik.Unitytk.Indicators;
using M3trik.Unitytk.Placement;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>indicate</c> ("Arrow points at the key"): an arrow at each target while its step runs: at the object the
    /// row names as where it is (its <c>where</c>, one entry a target: an arrow an artist modelled shown where it
    /// stands, bobbing in place; an object that draws nothing the kit's arrow, its tip on it and pointing along it,
    /// <see cref="IndicatorsActions.AddIndicatorAt"/>), else one the kit places for the
    /// step (<see cref="IndicatorsActions.AddIndicator(GameObject, string, bool, bool, OpContext)"/>, styled by
    /// the object's settings: <see cref="IndicateSettings.size"/>, <see cref="IndicateSettings.color"/>,
    /// <see cref="IndicateSettings.style"/>, flat or solid): beside the target in free space, pointing at it from where
    /// the user then stands and sees it (over it where that works), riding it as it moves, and kept where a person moved
    /// it by its manifest (<see cref="Manifest.moved"/>). A target that is several things apart ("both latches": two
    /// latches under one group) gets an arrow at each (<see cref="Placer.Items"/>), but things closer together than
    /// <see cref="IndicateSettings.merge"/> share one (<see cref="Placer.Cluster"/>); an arrow a build made for the step
    /// that it no longer makes goes (<see cref="IndicatorsActions.RemoveIndicator"/>).
    /// </summary>
    [TaskBehavior("indicate",
                  About = "An arrow points at the object while its step runs; things close together share one. Moved or recoloured in the scene, it keeps it.")]
    internal sealed class IndicateBehavior : IManifestBehavior, IManifestBound, IPointsOut
    {
        /// <summary>
        /// The name of the arrow the kit makes at a target for <paramref name="step"/>: <c>Indicator &lt;step&gt;</c>,
        /// or, one of several (a target that is several things apart), <c>Indicator &lt;step&gt; &lt;thing&gt;</c>.
        /// </summary>
        /// <param name="step">The step's ID.</param>
        /// <param name="thing">The thing it points at, one of several; null: the target's one arrow.</param>
        internal static string ArrowName(string step, string thing = null)
            => string.IsNullOrEmpty(thing) ? $"{IndicatorsActions.ChildName} {step}" : $"{IndicatorsActions.ChildName} {step} {thing}";

        /// <summary>
        /// Whether <paramref name="child"/> is an arrow the kit made at a target for <paramref name="step"/>: placed under its
        /// arrow key (<c>indicate/&lt;step&gt;/</c>), whatever it is named, or named as a build named one before
        /// (<see cref="ArrowName"/>).
        /// </summary>
        /// <param name="child">A child of the target.</param>
        /// <param name="step">The step's ID.</param>
        internal static bool IsArrow(Transform child, string step)
        {
            if (child.TryGetComponent(out PlacedSpot spot) && spot.key != null && spot.key.StartsWith($"indicate/{step}/", StringComparison.Ordinal))
                return true;
            string one = ArrowName(step);
            return child.name == one || child.name.StartsWith(one + " ", StringComparison.Ordinal);
        }

        // The name of the arrow at a target's things (thing: one of several, else null): the row's own helper's name as every
        // tool names it (the plan's helpers; one of several things adds its name), else, for a plan from the old sentence
        // format, ArrowName.
        static string Named(SheetPlan.Shape.PlanAction action, int index, string step, string thing)
        {
            string own = TaskKit.Helper(action, index);
            if (own == null) return ArrowName(step, thing);
            return thing == null ? own : $"{own}_{thing}";
        }

        // Its settings a person's change to an arrow in the scene is kept as (IManifestBound), by their paths.
        const string Size = nameof(TaskSettings.indicate) + "." + nameof(IndicateSettings.size),
                     Colour = nameof(TaskSettings.indicate) + "." + nameof(IndicateSettings.color);

        /// <summary>The arrows it made for step at the target: their size and colour are the object's settings (a person's change kept).</summary>
        public IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                            ManifestScene scene, string target)
        {
            Transform node = string.IsNullOrEmpty(target) ? null : scene.Node(target);
            if (node == null) yield break;
            // The arrow at the object the row names as where it is, where the kit draws it there.
            string named = action.targets == null ? null : TaskKit.Where(action, action.targets.IndexOf(target));
            Transform where = named == null ? null : scene.Node(named);
            Transform own = where == null ? null : where.Find(IndicatorsActions.ChildName);
            if (own != null && own.TryGetComponent(out Indicator placed) && placed.arrow == null)
            {
                yield return (Size, placed, nameof(Indicator.size));
                yield return (Colour, placed, nameof(Indicator.color));
            }
            foreach (Transform child in node)
            {
                if (!IsArrow(child, step.id) || !child.TryGetComponent(out Indicator arrow)) continue;
                yield return (Size, arrow, nameof(Indicator.size));
                yield return (Colour, arrow, nameof(Indicator.color));
            }
        }

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
            => Rows.NeedTargets(action, "point at");

        // An arrow at a part the user takes (a tool) has done its job once it is taken: the part's taking
        // switches it off (it would ride into the hand otherwise); its step switches it on again. A dry run's
        // arrow, only planned, is handed over as the object its live run makes (name, under node).
        static void GoneOnTake(GameObject node, Indicator arrow, string name, OpContext ctx)
        {
            if (arrow != null) Interaction.Interactions.DisableOnTake(node, arrow, ctx);
            else Interaction.Interactions.DisableOnTake(node, new PlannedObject(name, node.transform), ctx);
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            for (int i = 0; i < action.targets.Count; i++)
            {
                var node = scene.Node(action.targets[i]).gameObject;
                string authored = TaskKit.Where(action, i);
                if (authored != null)
                {
                    At(scene.Node(authored), node, action.targets[i], action, step, scene, ctx);
                    Unmade(node, step.id, new HashSet<string>(), ctx);  // the arrow a build searched a spot for, before the row named its own
                    continue;
                }
                // One arrow at each thing the target is, those close together sharing one (one, for a target that
                // is one thing: its name unchanged).
                var items = Placer.Items(node.transform, t => t.GetComponent<Indicator>() != null);
                if (items.Count == 0) items.Add((new List<Transform> { node.transform }, default));
                IndicateSettings own = scene.Settings<TaskSettings>(step.id, action.targets[i], action).indicate;
                var groups = Placer.Cluster(items, own.merge);
                var made = new HashSet<string>();
                foreach (var (nodes, _) in groups)
                {
                    string name = Named(action, i, step.id, groups.Count == 1 ? null : nodes[0].name);
                    made.Add(name);
                    Indicator arrow = IndicatorsActions.AddIndicator(node, name, ctx: ctx);
                    Apart(arrow, new PlannedObject(name, node.transform), ctx);
                    // Styled first: its size is the room it is placed with. The object's colour and size: the task's, or
                    // the object's own (set before a build, or kept from a change in the scene).
                    Style(arrow, node, name, own, ctx);
                    Spots.Point(scene, step, node.transform, nodes, name, arrow, ctx);
                    if (arrow != null) Cues.During(scene, step, arrow.gameObject, arrow, nodes, own.cue, ctx);
                    else Cues.During<Indicator>(scene, step, new PlannedObject(name, node.transform), nodes, ctx);
                    GoneOnTake(node, arrow, name, ctx);
                }
                Unmade(node, step.id, made, ctx);
            }
        }

        // The arrow at the object a row names as where it is (its `where`), shown while step runs: the object's own mesh
        // where it draws one (an arrow an artist modelled, bobbing where it stands), else the kit's, its tip on the object
        // and pointing along it, styled by the object's settings. Nothing is searched for: the object is where it is.
        static void At(Transform where, GameObject node, string target, SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                       ManifestScene scene, OpContext ctx)
        {
            IndicateSettings own = scene.Settings<TaskSettings>(step.id, target, action).indicate;
            var aims = new[] { node.transform };
            Spots.Given(scene, where, ctx);
            if (Drawn(where))
            {
                Indicator modelled = IndicatorsActions.AddIndicator(node, where, ctx: ctx);
                Cues.During(scene, step, node, modelled, aims, own.cue, ctx);
                GoneOnTake(node, modelled, IndicatorsActions.ChildName, ctx);
                return;
            }
            Indicator arrow = IndicatorsActions.AddIndicatorAt(node, where, ctx: ctx);
            var planned = new PlannedObject(IndicatorsActions.ChildName, where);  // a dry run's: the object its live run makes
            Apart(arrow, planned, ctx);
            Style(arrow, where.gameObject, IndicatorsActions.ChildName, own, ctx);
            if (arrow != null)
            {
                Cues.During(scene, step, arrow.gameObject, arrow, aims, own.cue, ctx);
                Interaction.Interactions.DisableOnTake(node, arrow, ctx);
                return;
            }
            Cues.During<Indicator>(scene, step, planned, aims, ctx);
            Interaction.Interactions.DisableOnTake(node, planned, ctx);
        }

        // The look own gives the arrow the kit makes (arrow, on target's child name; null: a dry run only planned it): its colour,
        // its size and whether it is flat.
        static void Style(Indicator arrow, GameObject target, string name, IndicateSettings own, OpContext ctx)
        {
            bool flat = own.style == IndicateStyle.Flat;
            if (arrow != null)
            {
                IndicatorsActions.SetStyle(arrow, own.color, own.size, ctx);
                IndicatorsActions.SetFlat(arrow, flat, ctx);
                return;
            }
            IndicatorsActions.SetStyle(target, name, own.color, own.size, ctx);
            IndicatorsActions.SetFlat(target, name, flat, ctx);
        }

        // An arrow the kit makes is no part of the look of what it sits under: a part that fades or glows neither hides
        // nor lights its arrow (planned: the object a dry run only planned it on).
        static void Apart(Indicator arrow, PlannedObject planned, OpContext ctx)
        {
            if (arrow != null) RenderEffects.RenderEffectsActions.Exempt(arrow.gameObject, ctx);
            else RenderEffects.RenderEffectsActions.Exempt(planned, ctx);
        }

        // Whether where draws something of its own (an arrow an artist modelled); the arrow the kit shows at one that
        // draws nothing is no part of it.
        static bool Drawn(Transform where)
        {
            foreach (Renderer drawn in where.GetComponentsInChildren<Renderer>(true))
                if (drawn.GetComponentInParent<Indicator>(true) == null) return true;
            return false;
        }

        // The arrows a build made at node for the step that this one does not (the things merged since): they go.
        static void Unmade(GameObject node, string step, HashSet<string> made, OpContext ctx)
        {
            var gone = new List<string>();
            foreach (Transform child in node.transform)
                if (IsArrow(child, step) && !made.Contains(child.name)) gone.Add(child.name);
            foreach (string name in gone) IndicatorsActions.RemoveIndicator(node, name, ctx);
        }
    }
}
