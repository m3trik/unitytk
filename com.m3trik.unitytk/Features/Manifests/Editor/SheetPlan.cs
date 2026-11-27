using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using M3trik.Unitytk.Core;

namespace M3trik.Unitytk.Manifests
{
    // The hand-written half of the generated reader (SheetPlan.g.cs): how a
    // plan's file is read, what a refusal says to do, and the typed reads of the
    // values the contract leaves to the kits (a row's and a step's fields, a kit's
    // extension).
    public static partial class SheetPlan
    {
        // The text fields the contract declares on every row that a person may set by name: every public
        // string field but the row's behavior and words.
        static readonly Dictionary<string, FieldInfo> _declared = typeof(Shape.PlanAction)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(string) && f.Name != nameof(Shape.PlanAction.behavior) && f.Name != nameof(Shape.PlanAction.text))
            .ToDictionary(f => f.Name, StringComparer.Ordinal);

        /// <summary>
        /// The text fields the contract declares on every row that a person may set by name (the take it plays,
        /// the step it goes to); every other value of a row is in its <c>fields</c>.
        /// </summary>
        public static IReadOnlyCollection<string> DeclaredFields => _declared.Keys;

        /// <summary>
        /// The row's value named <paramref name="name"/>, as text: a declared text field (<see cref="DeclaredFields"/>)
        /// by its name, else its <c>fields</c> entry (each group of the clause template that read the row: a phrase,
        /// a label, a node's path). Null where it has none, or where the entry is a list (<see cref="FieldList"/>).
        /// </summary>
        /// <param name="action">The plan's row.</param>
        /// <param name="name">The value's name (<c>clip</c>, <c>tool</c>, <c>label</c> ...).</param>
        public static string Field(this Shape.PlanAction action, string name)
        {
            if (action == null || string.IsNullOrEmpty(name)) return null;
            if (_declared.TryGetValue(name, out FieldInfo declared)) return (string)declared.GetValue(action);
            return action.fields != null && action.fields.TryGetValue(name, out object value) ? value as string : null;
        }

        /// <summary>
        /// The row's <c>fields</c> entry named <paramref name="name"/> as a list of text (the authored arrows of a
        /// pointing row): a list entry's text items, a text entry as a list of one; null where it has none.
        /// </summary>
        /// <param name="action">The plan's row.</param>
        /// <param name="name">The entry's name.</param>
        public static List<string> FieldList(this Shape.PlanAction action, string name)
        {
            if (action == null || action.fields == null || string.IsNullOrEmpty(name)
                || !action.fields.TryGetValue(name, out object value) || value == null) return null;
            if (value is string one) return new List<string> { one };
            if (!(value is IList<object> many)) return null;
            var items = new List<string>(many.Count);
            foreach (object item in many)
                items.Add(item as string ?? "");
            return items;
        }

        /// <summary>
        /// The row's values named <paramref name="name"/>, as a list of text: a declared text field's
        /// (<see cref="DeclaredFields"/>) as a list of one, else its <c>fields</c> entry's (<see cref="FieldList"/>); null where it has none.
        /// </summary>
        /// <param name="action">The plan's row.</param>
        /// <param name="name">The value's name (<c>clip</c>, <c>arrows</c> ...).</param>
        public static List<string> FieldValues(this Shape.PlanAction action, string name)
        {
            if (action == null || string.IsNullOrEmpty(name)) return null;
            if (!_declared.ContainsKey(name)) return action.FieldList(name);
            string one = action.Field(name);
            return one == null ? null : new List<string> { one };
        }

        /// <summary>
        /// Set the row's value named <paramref name="name"/>: a declared text field (<see cref="DeclaredFields"/>), else its
        /// <c>fields</c> entry; null drops the entry (a declared field reads as absent). The row's own <c>fields</c> are
        /// changed: copy them first to change a copy of a row alone.
        /// </summary>
        /// <param name="action">The plan's row.</param>
        /// <param name="name">The value's name.</param>
        /// <param name="value">Its text; null: none.</param>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        public static void SetField(this Shape.PlanAction action, string name, string value)
        {
            if (action == null) throw new ArgumentNullException(nameof(action), "name the row");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the value to set", nameof(name));
            if (_declared.TryGetValue(name, out FieldInfo declared))
            {
                declared.SetValue(action, value);
                return;
            }
            if (value == null)
            {
                if (action.fields != null) action.fields.Remove(name);
                return;
            }
            if (action.fields == null) action.fields = new Dictionary<string, object>(StringComparer.Ordinal);
            action.fields[name] = value;
        }

        /// <summary>The step's value named <paramref name="name"/> (its <c>fields</c>: named by the sheet's reading); null where it has none.</summary>
        /// <param name="step">The plan's step.</param>
        /// <param name="name">The value's name (<c>voice</c>, <c>sound</c> ...).</param>
        public static string Field(this Shape.PlanStep step, string name)
            => step != null && step.fields != null && !string.IsNullOrEmpty(name) && step.fields.TryGetValue(name, out object value)
                ? value as string : null;

        /// <summary>The plan's row of step <paramref name="step"/> at <paramref name="place"/> (its place among the step's rows); null where there is none.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="place">The row's place in its step; below 0: none (a row added in a project, which the plan lacks).</param>
        public static Shape.PlanAction Row(this Shape.Payload plan, string step, int place)
        {
            if (plan == null || plan.steps == null || place < 0) return null;
            foreach (Shape.PlanStep each in plan.steps)
                if (each.id == step) return each.actions != null && place < each.actions.Count ? each.actions[place] : null;
            return null;
        }

        /// <summary>
        /// The kit <paramref name="kit"/>'s own data for the whole plan (its entry of <c>extensions</c>, as the project's
        /// settings file gives it, which that kit alone reads); null where the plan carries none.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="kit">The kit's name (its settings file key).</param>
        public static IDictionary<string, object> Extension(this Shape.Payload plan, string kit)
            => plan != null && plan.extensions != null && !string.IsNullOrEmpty(kit) && plan.extensions.TryGetValue(kit, out object data)
                ? data as IDictionary<string, object> : null;

        /// <summary>
        /// The plan <paramref name="json"/> holds, read through its declared shape, when it
        /// is of <see cref="CurrentVersion"/>; else null, and <paramref name="refusal"/> says
        /// why and what to do (text that is not JSON, another version, a plan that fails its
        /// declaration: nothing of it is read, never half).
        /// </summary>
        /// <param name="json">The plan file's text.</param>
        /// <param name="where">The file, for the refusal.</param>
        /// <param name="refusal">Why the plan was not read; null when it was.</param>
        public static Shape.Payload Read(string json, string where, out string refusal)
        {
            refusal = null;
            object tree;
            try
            {
                tree = Json.Parse(json ?? "", false);
            }
            catch (FormatException e)
            {
                refusal = $"'{where}' is not JSON ({e.Message}): {Again}";
                return null;
            }
            Shape.Payload plan;
            try
            {
                plan = Shape.Payload.Read(tree);
            }
            catch (FormatException e)
            {
                refusal = $"'{where}' is not a sheet plan ({e.Message}): {Again}";
                return null;
            }
            if (plan == null)
            {
                refusal = $"'{where}' is not a sheet plan (no JSON object): {Again}";
                return null;
            }
            if (plan.version != CurrentVersion)
            {
                refusal = $"'{where}' is a version {plan.version} plan; this editor reads " +
                          $"{CurrentVersion} only: {Again}";
                return null;
            }
            return plan;
        }

        /// <summary>The plan saved at <paramref name="path"/>; null with a refusal as <see cref="Read"/> says.</summary>
        /// <param name="path">The plan file.</param>
        /// <param name="refusal">Why the plan was not read; null when it was.</param>
        public static Shape.Payload Load(string path, out string refusal)
        {
            if (!File.Exists(path))
            {
                refusal = $"no plan at '{path}': {Again}";
                return null;
            }
            return Read(File.ReadAllText(path), path, out refusal);
        }

        // What every refusal says to do: stage the plan again from its sheet, as the sheet's reader's sync does.
        static string Again => "stage it again from its sheet: " + ManifestsActions.SyncCommand("<its settings file>", "<this project>");
    }
}
