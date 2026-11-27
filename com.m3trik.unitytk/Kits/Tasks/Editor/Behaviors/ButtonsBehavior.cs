using M3trik.Unitytk.Manifests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>buttons</c> ("YES and NO buttons appear on the placard screen"): the buttons the row names (split by the
    /// row's <see cref="TaskSettings.labelSeparators"/>) are on the step's page, one per choice a row of the step
    /// offers (<see cref="TaskBehaviorAttribute.Offers"/>: <c>goto</c>, <c>exit</c>...); a button no row says what
    /// to do with is raised, never guessed.
    /// </summary>
    [TaskBehavior("buttons", Fields = new[] { "label" }, NamesObjects = false, Settings = new[] { nameof(TaskSettings.labelSeparators) },
                      About = "The buttons the row names are on the step's page.")]
    internal sealed class ButtonsBehavior : IManifestBehavior
    {
        /// <summary>The labels a row names: <c>YES and NO</c>, <c>A, B or C</c>.</summary>
        /// <param name="label">The row's label.</param>
        /// <param name="separators">What splits it: punctuation as written, a word where it stands alone.</param>
        internal static IEnumerable<string> Labels(string label, IEnumerable<string> separators)
        {
            var between = new List<string>();
            foreach (string each in separators ?? Enumerable.Empty<string>())
            {
                string word = (each ?? "").Trim();
                if (word.Length == 0) continue;
                string escaped = Regex.Escape(word);
                between.Add(char.IsLetterOrDigit(word[0]) ? $@"\b{escaped}\b" : escaped);
            }
            IEnumerable<string> parts = between.Count == 0
                ? new[] { label ?? "" }
                : Regex.Split(label ?? "", $@"\s*(?:{string.Join("|", between)})\s*", RegexOptions.IgnoreCase);
            foreach (string part in parts)
                if (part.Trim().Length > 0) yield return part.Trim().Trim('\'', '"');
        }

        // The labels action names, split by its own settings' separators.
        static IEnumerable<string> Labels(SheetPlan.Shape.PlanAction action, string step, ManifestScene scene)
            => Labels(action.Field(TaskKit.Fields.Label), scene.Settings<TaskSettings>(step ?? "", "", action).labelSeparators);

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (!Labels(action, PlanOrder.StepOf(scene.Plan, action)?.id, scene).Any()) yield return "it names no button";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            foreach (string label in Labels(action, step.id, scene))
            {
                bool offered = false;
                foreach (var other in step.actions)
                    if (TaskBehaviors.Traits(other.behavior).Offers
                        && string.Equals(other.Field(TaskKit.Fields.Label), label, StringComparison.OrdinalIgnoreCase)) offered = true;
                if (!offered)
                    ctx.Raise(ManifestsActions.UnresolvedCode, $"{step.id}: the placard's '{label}' button: no row of the step says what it does");
            }
        }
    }
}
