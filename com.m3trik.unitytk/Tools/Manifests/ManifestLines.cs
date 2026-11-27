using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Manifests;
using UnityEditor;

namespace M3trik.Unitytk.Tools.Manifests
{
    /// <summary>
    /// A manifest's plan and its assessment as the lines a table shows: a line per step, and under it
    /// a line per object the step's rows act on, each line with its behaviors (<see cref="Token"/>:
    /// one a row of the sheet, or of this project's edits), its status in a few words and a tooltip
    /// saying every issue. The step's own line holds the behaviors that name no object (it goes on by
    /// itself, offers Next). Host-free of the window: the same lines serve a table, an export, a test.
    /// </summary>
    /// <remarks>
    /// Prior art: the producer's own manifest table, the other manifest of the same sheet, lists a
    /// step's objects under it with each object's behaviors in one cell, a glyph before a behavior
    /// that has an issue and one struck through where it is off; these are the same lines for a
    /// Unity scene, so one sheet reads alike in both tools.
    /// </remarks>
    public static class ManifestLines
    {
        /// <summary>One behavior of a line: a row of the sheet (or one this project added) as it stands for the line's object.</summary>
        public sealed class Token
        {
            /// <summary>The behavior's name, as the plan spells it.</summary>
            public string behavior = "";
            /// <summary>The behavior's name as a label (<see cref="BehaviorLabel"/>).</summary>
            public string label = "";
            /// <summary>Its status (<see cref="ManifestStatus"/>); empty before an assessment.</summary>
            public string status = "";
            /// <summary>The row's words in the sheet.</summary>
            public string text = "";
            /// <summary>What it says on a hover: its status, its issues, the sheet's words, what the behavior does.</summary>
            public string tooltip = "";
            /// <summary>Its row among its step's built rows (<see cref="ManifestAssessment.Step.rows"/>); -1 for one that is off.</summary>
            public int row = -1;
            /// <summary>Which of its step's rows in the sheet it is; -1 for one this project added.</summary>
            public int source = -1;
            /// <summary>Turned off in this project: for every row of its behavior, or for this object alone.</summary>
            public bool off;
            /// <summary>Added in this project: the sheet does not give the object this behavior.</summary>
            public bool added;
            /// <summary>Off because its behavior is off for the whole manifest (not this object's own edit).</summary>
            public bool offEverywhere;
            /// <summary>Whether the object has values of its own for it in the manifest (<see cref="Manifest.tunes"/>): its settings, its row's fields.</summary>
            public bool tuned;
            /// <summary>What tells its row from its behavior's others, shown beside its label (<see cref="ManifestBehaviorAttribute.ChipFields"/>: <c>'Yes' → B03_5</c>); empty for none.</summary>
            public string detail = "";
            /// <summary>How its behavior is set where its row or object says so (<see cref="ManifestBehaviorAttribute.Suffix"/>): <c>hide</c> for a marker taken away; empty: the usual way.</summary>
            public string variant = "";
            /// <summary>What the sheet's row gives it, a <c>Label: value</c> line a field (<see cref="ManifestBehaviorAttribute.FieldLabel"/>); empty for none.</summary>
            public string fields = "";

            /// <summary>Its severity (<see cref="ManifestStatus.Levels"/>): off and added before whatever its row says.</summary>
            public string Level => off ? "off" : string.IsNullOrEmpty(status) ? "" : ManifestStatus.Level(status);
        }

        /// <summary>One line of the table: a step, or one object of a step.</summary>
        public sealed class Line
        {
            /// <summary>The step's ID.</summary>
            public string step = "";
            /// <summary>The object's place among its step's objects, from 0; -1 for the step's own line.</summary>
            public int row = -1;
            /// <summary>The object, as the sheet's rows name it (a node's path in the delivery); empty for a step's own line.</summary>
            public string target = "";
            /// <summary>Its status (<see cref="ManifestStatus"/>): the worst of its behaviors'; empty before an assessment.</summary>
            public string status = "";
            /// <summary>Its status in a few words; empty when there is nothing to say.</summary>
            public string label = "";
            /// <summary>A step's section.</summary>
            public string section = "";
            /// <summary>A step's name; an object's own name (its path's last part).</summary>
            public string description = "";
            /// <summary>Its behaviors as plain words, comma separated (what a search and an export read).</summary>
            public string behaviors = "";
            /// <summary>A step: how many objects it acts on; an object: empty.</summary>
            public string acts = "";
            /// <summary>Every issue, a line each; empty when there is none.</summary>
            public string tooltip = "";
            /// <summary>Its behaviors: an object's, or (a step's own line) those that name no object.</summary>
            public List<Token> tokens = new List<Token>();
            /// <summary>A step's objects; empty for an object's line.</summary>
            public List<Line> rows = new List<Line>();

            /// <summary>Whether it is a step's own line.</summary>
            public bool IsStep => row < 0;

            /// <summary>Whether it has nothing to say: no words in its status, no issue, no edit of this project's, nor any of its objects'.</summary>
            public bool Quiet
            {
                get
                {
                    if (label.Length > 0 || tooltip.Length > 0) return false;
                    foreach (Token token in tokens)
                        if (token.off || token.added) return false;
                    foreach (Line each in rows)
                        if (!each.Quiet) return false;
                    return true;
                }
            }

            /// <summary>Whether it, or one of its objects, mentions <paramref name="words"/> (in any column, any case).</summary>
            /// <param name="words">What is searched for; empty matches every line.</param>
            public bool Matches(string words)
            {
                if (string.IsNullOrEmpty(words)) return true;
                var all = new StringBuilder(string.Join(" ", step, label, section, description, behaviors, acts, target));
                foreach (Token token in tokens) all.Append(' ').Append(token.text);
                if (all.ToString().IndexOf(words, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                foreach (Line each in rows)
                    if (each.Matches(words)) return true;
                return false;
            }
        }

        /// <summary>The glyph of a severity: its shape says what its colour does. Empty for none.</summary>
        /// <param name="level">A severity (<see cref="ManifestStatus.Levels"/>).</param>
        public static string Glyph(string level)
        {
            switch (level)
            {
                case "error": return "✖";
                case "warn": return "▲";
                case "info": return "●";
                case "locked": return "■";
                case "edit": return "✎";
                case "off": return "–";
                case "valid": return "✓";
                default: return "";
            }
        }

        /// <summary>
        /// A behavior's name as a label: <c>press_hold</c> as <c>Press Hold</c>; one of a family
        /// (<see cref="ManifestBehaviors.FamilyOf"/>) its family's, then how it sets it: <c>fade_in</c> as
        /// <c>Fade · in</c>, as the Shot Manifest reads it.
        /// </summary>
        /// <param name="behavior">A behavior's name.</param>
        public static string BehaviorLabel(string behavior)
        {
            if (string.IsNullOrEmpty(behavior)) return "";
            ManifestBehaviorAttribute mark = ManifestBehaviors.Mark(behavior);
            if (mark != null && !string.IsNullOrEmpty(mark.Family) && !string.IsNullOrEmpty(mark.Variant))
                return BehaviorLabel(mark.Family) + " · " + mark.Variant;
            var words = behavior.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
                words[i] = char.ToUpper(words[i][0], CultureInfo.InvariantCulture) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        /// <summary>The lines of <paramref name="plan"/>, marked by <paramref name="assessed"/> where there is one.</summary>
        /// <param name="plan">The sheet's plan; null: no lines.</param>
        /// <param name="assessed">Its assessment (<see cref="ManifestsActions.Assess(Manifest, Kernel.OpContext)"/>); null: unmarked.</param>
        /// <param name="manifest">The manifest, whose edits and switched-off behaviors show before an assessment too; null: none.</param>
        public static List<Line> Of(SheetPlan.Shape.Payload plan, ManifestAssessment assessed, Manifest manifest = null)
        {
            var lines = new List<Line>();
            if (plan == null || plan.steps == null) return lines;
            for (int s = 0; s < plan.steps.Count; s++)
            {
                var step = plan.steps[s];
                ManifestAssessment.Step entry = assessed != null && s < assessed.steps.Count ? assessed.steps[s] : null;
                var head = new Line
                {
                    step = step.id,
                    section = string.IsNullOrEmpty(step.section_title) ? step.section ?? "" : $"{step.section}: {step.section_title}",
                    description = string.IsNullOrEmpty(step.name) ? step.id : step.name,
                };
                var byTarget = new Dictionary<string, Line>(StringComparer.Ordinal);
                foreach (var (token, targets) in Tokens(step, entry, manifest))
                {
                    if (targets.Count == 0) head.tokens.Add(token);
                    foreach (string target in targets)
                    {
                        if (!byTarget.TryGetValue(target, out Line line))
                        {
                            line = new Line { step = step.id, row = head.rows.Count, target = target, description = Leaf(target) };
                            byTarget[target] = line;
                            head.rows.Add(line);
                        }
                        line.tokens.Add(Copy(token, target, manifest, step.id));
                    }
                }
                // Each behavior with values of the object's own (its settings, its row's fields), said on its chip, and those its
                // row keeps as a whole (a take's speed), which every object of it builds by, said on each of theirs.
                if (manifest != null && manifest.tunes.Count > 0)
                {
                    foreach (Token token in head.tokens) Own(token, manifest.TuneOf(step.id, "", token.behavior, token.source));
                    foreach (Line line in head.rows)
                        foreach (Token token in line.tokens)
                        {
                            Own(token, manifest.TuneOf(step.id, line.target, token.behavior, token.source));
                            if (ManifestsActions.Offered(manifest, step.id, line.target, token.behavior, token.source, wholeRow: true).Length > 0)
                                Own(token, manifest.TuneOf(step.id, "", token.behavior, token.source), "Its row's here");
                        }
                }
                foreach (Line line in head.rows) Mark(line);
                Mark(head);
                head.acts = head.rows.Count == 0 ? "" : head.rows.Count == 1 ? "1 object" : $"{head.rows.Count} objects";
                if (entry != null)
                {
                    // The step's own state leads its line: what its objects say is theirs, counted in its tooltip.
                    var issues = StepIssues(entry);
                    head.status = entry.Status;
                    head.label = ManifestStatus.Label(head.status);
                    head.tooltip = Text(issues);
                }
                lines.Add(head);
            }
            return lines;
        }

        // Each behavior of a step with the objects it names: the rows a build builds (the sheet's, then this
        // project's own), and the sheet's rows this project turned off. Before an assessment: the sheet's rows.
        static IEnumerable<(Token token, List<string> targets)> Tokens(SheetPlan.Shape.PlanStep step, ManifestAssessment.Step entry, Manifest manifest)
        {
            if (entry == null)
            {
                for (int i = 0; step.actions != null && i < step.actions.Count; i++)
                {
                    var action = step.actions[i];
                    bool off = manifest != null && manifest.off.Contains(action.behavior);
                    var token = new Token
                    {
                        behavior = action.behavior, label = BehaviorLabel(action.behavior), text = action.text ?? "", source = i,
                        off = off, offEverywhere = off,
                    };
                    (token.detail, token.fields) = Fields(action);
                    token.variant = Variant(action.behavior, action.settings, null);
                    token.tooltip = Tooltip(token, new List<(string, string)>());  // a step's own chip says as much as an object's
                    yield return (token, action.targets ?? new List<string>());
                }
                for (int i = 0; manifest != null && i < manifest.edits.Count; i++)
                {
                    Manifest.Edit edit = manifest.edits[i];
                    if (edit == null || !edit.add || edit.step != step.id) continue;
                    bool off = manifest.off.Contains(edit.behavior);
                    var added = new Token
                    {
                        behavior = edit.behavior, label = BehaviorLabel(edit.behavior), added = true, off = off, offEverywhere = off,
                    };
                    added.tooltip = Tooltip(added, new List<(string, string)>());
                    yield return (added, string.IsNullOrEmpty(edit.target) ? new List<string>() : new List<string> { edit.target });
                }
                yield break;
            }
            // In the sheet's order, the rows left out among those built.
            var all = new List<(int order, ManifestAssessment.Row row, int at)>();
            for (int i = 0; i < entry.rows.Count; i++) all.Add((entry.rows[i].source < 0 ? int.MaxValue : entry.rows[i].source, entry.rows[i], i));
            foreach (ManifestAssessment.Row row in entry.off) all.Add((row.source, row, -1));
            all.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.at.CompareTo(b.at));
            foreach (var (_, row, at) in all)
            {
                var token = new Token
                {
                    behavior = row.behavior, label = BehaviorLabel(row.behavior), text = row.text ?? "", row = at, source = row.source,
                    off = row.off, added = row.added, status = row.Status,
                    offEverywhere = row.off && manifest != null && manifest.off.Contains(row.behavior),
                };
                if (row.source >= 0 && step.actions != null && row.source < step.actions.Count)
                {
                    (token.detail, token.fields) = Fields(step.actions[row.source]);
                    token.variant = Variant(row.behavior, step.actions[row.source].settings, null);
                }
                token.tooltip = Tooltip(token, RowIssues(row));
                yield return (token, row.targets);
                // The objects the delivery's own build made it on, where it builds the rest: they stand on their lines,
                // as the delivery's.
                if (row.state != ManifestAssessment.State.Upstream && row.upstream.Count > 0)
                {
                    var delivered = new Token
                    {
                        behavior = row.behavior, label = token.label, text = token.text, row = at, source = row.source, status = ManifestStatus.Upstream,
                        detail = token.detail, fields = token.fields, variant = token.variant,
                    };
                    delivered.tooltip = Tooltip(delivered, new List<(string, string)>());
                    yield return (delivered, row.upstream);
                }
                // The objects an edit left this row out for: it stands on their lines, off.
                if (row.off || row.dropped.Count == 0) continue;
                var dropped = new Token
                {
                    behavior = row.behavior, label = token.label, text = token.text, row = -1, source = row.source, off = true, status = ManifestStatus.Off,
                    detail = token.detail, fields = token.fields, variant = token.variant,
                };
                dropped.tooltip = Tooltip(dropped, new List<(string, string)>());
                yield return (dropped, row.dropped);
            }
        }

        // A token as it stands on one object's line: off there where the manifest takes it from that object
        // (an assessment lists those objects apart; before one, the sheet's row still names them).
        static Token Copy(Token token, string target, Manifest manifest, string step)
        {
            var own = new Token
            {
                behavior = token.behavior, label = token.label, status = token.status, text = token.text, tooltip = token.tooltip, row = token.row,
                source = token.source, off = token.off, added = token.added, offEverywhere = token.offEverywhere,
                detail = token.detail, fields = token.fields, tuned = token.tuned, variant = token.variant,
            };
            Manifest.Edit edit = manifest != null && !own.off && !own.added ? manifest.EditOf(step, target, own.behavior, own.source) : null;
            if (edit != null && !edit.add)
            {
                own.off = true;
                own.tooltip = "";
            }
            if (own.tooltip.Length == 0) own.tooltip = Tooltip(own, new List<(string, string)>());
            return own;
        }

        // What a behavior says on a hover: its state, its issues, the sheet's words and what it does.
        static string Tooltip(Token token, List<(string status, string text)> issues)
        {
            var text = new StringBuilder();
            string level = token.Level;
            string glyph = Glyph(level);
            text.Append(glyph.Length > 0 ? glyph + " " : "").Append(token.label);
            if (token.detail.Length > 0) text.Append(' ').Append(token.detail);
            if (token.variant.Length > 0) text.Append(" · ").Append(token.variant);
            if (token.off) text.Append(token.offEverywhere ? "  (off for every row: the window's menu)" : "  (off for this object)");
            else if (token.added) text.Append("  (added in this project)");
            else if (token.status.Length > 0 && ManifestStatus.Label(token.status).Length > 0) text.Append("  (").Append(ManifestStatus.Label(token.status)).Append(')');
            foreach (var (code, said) in issues)
            {
                string mark = Glyph(ManifestStatus.Level(code));
                text.Append('\n').Append(mark.Length > 0 ? mark + " " : "").Append(said);
            }
            if (!string.IsNullOrEmpty(token.text) && !token.added) text.Append("\nSheet: ").Append(token.text);
            if (token.fields.Length > 0) text.Append('\n').Append(token.fields);
            string about = ManifestBehaviors.Mark(token.behavior)?.About;
            if (!string.IsNullOrEmpty(about)) text.Append('\n').Append(about);
            return text.ToString();
        }

        // How a behavior is set, in words, for its chip: each of its suffix settings (ManifestBehaviorAttribute.Suffix)
        // that the object's own values (tune) or else its row (row) set ("HoldToPlay": "hold to play"); was: the row's.
        static string Variant(string behavior, IDictionary<string, object> row, Manifest.Tune tune, string was = "")
        {
            string[] names = ManifestBehaviors.Mark(behavior)?.Suffix;
            if (names == null) return was;
            var said = new List<string>();
            foreach (string name in names)
            {
                string value = tune?.Get(name);
                if (value == null && row != null && row.TryGetValue(name, out object set)) value = Convert.ToString(set, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(value)) said.Add(Words(value));
            }
            return said.Count > 0 ? string.Join(", ", said) : tune != null ? was : "";
        }

        // A token the object (or its row: whose) has values of its own for: so marked, and its tooltip says them.
        static void Own(Token token, Manifest.Tune tune, string whose = "Its own here")
        {
            if (tune == null || tune.Empty || token.off) return;
            token.tuned = true;
            token.variant = Variant(token.behavior, null, tune, token.variant);
            var said = new List<string>();
            foreach (Manifest.Value value in tune.values)
                if (value != null) said.Add($"{Words(value.name)} {value.value}");
            ManifestBehaviorAttribute mark = ManifestBehaviors.Mark(token.behavior);
            foreach (Manifest.Value value in tune.fields)
                if (value != null) said.Add($"{(mark != null ? mark.FieldLabel(value.name) : ManifestBehaviorAttribute.DefaultLabel(value.name)).ToLowerInvariant()} {value.value}");
            token.tooltip += $"\n{whose}: " + string.Join("; ", said);
        }

        // A setting's name or a choice ("HoldToPlay") in plain lower-case words ("hold to play").
        static string Words(string name) => ObjectNames.NicifyVariableName(name).ToLowerInvariant();

        // What the sheet's row gives its behavior, a "Label: value" line a field (the contract's take and jump, then the row's
        // own fields, each labelled as its behavior says: ManifestBehaviorAttribute.FieldLabel), and what its chip says beside
        // its name to tell it from its behavior's other rows (ManifestBehaviorAttribute.ChipFields).
        static (string detail, string fields) Fields(SheetPlan.Shape.PlanAction action)
        {
            if (action == null) return ("", "");
            ManifestBehaviorAttribute mark = ManifestBehaviors.Mark(action.behavior);
            var names = new List<string>(SheetPlan.DeclaredFields);
            if (action.fields != null) names.AddRange(action.fields.Keys);
            var lines = new List<string>();
            foreach (string name in names)
            {
                string value = Shown(action, name, mark);
                if (value.Length > 0) lines.Add((mark != null ? mark.FieldLabel(name) : ManifestBehaviorAttribute.DefaultLabel(name)) + ": " + value);
            }
            var chip = new List<string>();
            foreach (string template in mark?.ChipFields ?? Array.Empty<string>())
            {
                bool any = false;
                string said = Regex.Replace(template ?? "", @"\{(\w+)\}", hole =>
                {
                    string value = Shown(action, hole.Groups[1].Value, mark);
                    any |= value.Length > 0;
                    return value;
                });
                if (any) chip.Add(said);
            }
            return (string.Join(" ", chip), string.Join("\n", lines));
        }

        // A row's field as the manifest shows it: its values joined, a node field's each by its node's own name; empty for none.
        static string Shown(SheetPlan.Shape.PlanAction action, string name, ManifestBehaviorAttribute mark)
        {
            List<string> values = action.FieldValues(name);
            if (values == null) return "";
            if (mark != null && mark.NamesNode(name)) values = values.ConvertAll(Leaf);
            values.RemoveAll(string.IsNullOrEmpty);
            return string.Join(", ", values);
        }

        // A line's own words from its behaviors: their names, the worst status among them, and what each says.
        static void Mark(Line line)
        {
            var names = new List<string>();
            var statuses = new List<string>();
            var said = new StringBuilder();
            foreach (Token token in line.tokens)
            {
                names.Add(token.detail.Length > 0 ? token.label + " " + token.detail : token.label);
                if (!token.off && token.status.Length > 0) statuses.Add(token.status);
                bool speaks = token.off || token.added || (token.status.Length > 0 && token.status != ManifestStatus.Valid);
                if (!speaks || token.tooltip.Length == 0) continue;
                if (said.Length > 0) said.Append('\n');
                said.Append(token.tooltip);
            }
            line.behaviors = string.Join(", ", names);
            if (line.IsStep) return;  // a step's own line is marked by its step's state
            // Every behavior it has turned off: the object's line says so, where one still on speaks for it otherwise.
            bool allOff = line.tokens.Count > 0 && line.tokens.TrueForAll(k => k.off);
            line.status = statuses.Count > 0 ? ManifestStatus.Worst(statuses) : allOff ? ManifestStatus.Off : "";
            line.label = ManifestStatus.Label(line.status);
            line.tooltip = said.ToString();
        }

        static string Text(List<(string status, string text)> issues)
        {
            var text = new StringBuilder();
            foreach (var (code, said) in issues)
            {
                if (text.Length > 0) text.Append('\n');
                string glyph = Glyph(ManifestStatus.Level(code));
                text.Append(glyph.Length > 0 ? glyph + " " : "").Append(said);
            }
            return text.ToString();
        }

        /// <summary>A row's issues, each with its status: what its tooltip lists and its detail shows.</summary>
        /// <param name="row">An assessed row.</param>
        public static List<(string status, string text)> RowIssues(ManifestAssessment.Row row)
        {
            var issues = new List<(string, string)>();
            string status = row.Status;
            if (row.off) return issues;
            if (row.state == ManifestAssessment.State.Unsupported)
                issues.Add((status, $"'{row.behavior}': {ManifestStatus.Help(status)}"));
            foreach (string problem in row.problems) issues.Add((status, problem));
            foreach (string warning in row.warnings) issues.Add((ManifestStatus.Warned, warning));
            foreach (string note in row.notes) issues.Add((ManifestStatus.Warned, note));
            foreach (ManifestAdjustment each in row.adjusted) issues.Add((ManifestStatus.Adjusted, $"Adjusted in the scene: {each.Text()}"));
            if (status == ManifestStatus.Missing || status == ManifestStatus.Update || status == ManifestStatus.Locked)
                issues.Add((status, ManifestStatus.Help(status)));
            return issues;
        }

        /// <summary>A step's issues: its own (locked, its own parts to build), then each of its rows that has something to say.</summary>
        /// <param name="step">An assessed step.</param>
        public static List<(string status, string text)> StepIssues(ManifestAssessment.Step step)
        {
            var issues = new List<(string, string)>();
            if (step.locked) issues.Add((ManifestStatus.Locked, ManifestStatus.Help(ManifestStatus.Locked)));
            else if (!step.made && step.changes.Count > 0)
                issues.Add((ManifestStatus.Missing, $"Build makes the step's own parts ({step.changes.Count} change(s))."));
            foreach (string note in step.notes) issues.Add((ManifestStatus.Warned, note));
            foreach (ManifestAdjustment each in step.adjusted) issues.Add((ManifestStatus.Adjusted, $"Adjusted in the scene: {each.Text()}"));
            foreach (ManifestAssessment.Row row in step.rows)
            {
                string status = row.Status;
                if (status == ManifestStatus.Valid || status == ManifestStatus.Locked) continue;
                string label = ManifestStatus.Label(status);
                issues.Add((status, $"{Short(row.text)}: {label}"));
            }
            return issues;
        }

        /// <summary>
        /// What waits on the sheet's or the delivery's owner, as a note to send them (Markdown): each row of
        /// the sheet that cannot be built with why, each warning, the sheet's own issues, and where this
        /// project builds otherwise than the sheet says (a row it added among them). Empty when there is nothing to send.
        /// </summary>
        /// <param name="plan">The sheet's plan.</param>
        /// <param name="assessed">Its assessment.</param>
        /// <param name="manifest">The manifest, whose edits are listed; null: none.</param>
        public static string IssuesReport(SheetPlan.Shape.Payload plan, ManifestAssessment assessed, Manifest manifest = null)
        {
            if (plan == null || assessed == null) return "";
            var text = new StringBuilder();
            foreach (string issue in assessed.planIssues) text.Append("- ").Append(issue).Append('\n');
            foreach (ManifestAssessment.Step step in assessed.steps)
                foreach (ManifestAssessment.Row row in step.rows)
                {
                    if (row.added) continue;  // the project's own row: said under what it builds otherwise
                    string status = row.Status;
                    bool waits = ManifestStatus.NeedsYou(status);
                    if (!waits && row.warnings.Count + row.notes.Count == 0) continue;
                    text.Append("- **").Append(step.id).Append("** row ").Append(row.source + 1).Append(" (").Append(BehaviorLabel(row.behavior))
                        .Append("): \"").Append(row.text).Append("\"\n");
                    foreach (var (_, said) in RowIssues(row))
                        if (said != ManifestStatus.Help(status) || waits) text.Append("  - ").Append(said).Append('\n');
                }
            if (manifest != null && (manifest.off.Count > 0 || manifest.edits.Count > 0))
            {
                text.Append("\n## Built otherwise than the sheet says\n\n");
                foreach (string behavior in manifest.off) text.Append("- ").Append(BehaviorLabel(behavior)).Append(": off for every row\n");
                foreach (Manifest.Edit edit in manifest.edits)
                    if (edit != null)
                        text.Append("- **").Append(edit.step).Append("** ").Append(Leaf(edit.target)).Append(": ").Append(BehaviorLabel(edit.behavior))
                            .Append(edit.add ? " added\n" : " left out\n");
            }
            if (text.Length == 0) return "";
            string sheet = plan.source != null ? plan.source.sheet : "";
            return $"# Manifest issues{(string.IsNullOrEmpty(sheet) ? "" : $": {sheet}")}\n\n{text}";
        }

        /// <summary>A node's own name: its path's last part.</summary>
        /// <param name="path">A node's path in the delivery, as the sheet's rows name it.</param>
        public static string Leaf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int cut = path.LastIndexOf('|');
            return cut < 0 ? path : path.Substring(cut + 1);
        }

        static string Short(string text)
            => string.IsNullOrEmpty(text) ? "" : text.Length <= 60 ? text : text.Substring(0, 57) + "...";
    }
}
