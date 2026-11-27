using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// What a person changes on a part a build made stays, whatever the part: every build records what it gave each
    /// part it holds (the plain fields of its components), so the next build tells a person's change from its own work,
    /// keeps the change in the manifest (<see cref="Manifest.adjusted"/>), builds over the parts as it last left them
    /// and puts the change back. A revert gives the part what the build gave it.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's prefab overrides (a variant's changes kept over its base, listed and revertable, but only
    /// for prefab instances: what a build adds in a scene has no base to override) and the bound settings of
    /// <see cref="IManifestBound"/> (one behavior's listed properties, kept as its object's settings). This is the
    /// same record-and-compare, for every held part without a list per behavior.
    /// </remarks>
    internal static class ManifestAdjustments
    {
        // Leads a part's record among Manifest.built's values, apart from a bound setting's.
        const string Mark = "@";
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        // The plain fields of each component type, found once (a cache of reflection, rebuilt after a domain reload).
        static readonly Dictionary<Type, FieldInfo[]> FieldsOf = new Dictionary<Type, FieldInfo[]>();

        // -- where a part is ------------------------------------------------------------------

        // The part's place in its scene: its object's path from its root, its type, and which of that type on the object.
        internal static string Where(Component part)
        {
            if (part == null || !part.gameObject.scene.IsValid()) return null;
            var names = new List<string>();
            for (Transform at = part.transform; at != null; at = at.parent) names.Add(at.name);
            names.Reverse();
            int index = 0;
            foreach (Component each in part.GetComponents<MonoBehaviour>())
            {
                if (each == part) break;
                if (each != null && each.GetType() == part.GetType()) index++;
            }
            return string.Join("/", names) + "#" + part.GetType().Name + (index > 0 ? $"[{index.ToString(Invariant)}]" : "");
        }

        // Rename the scene root from to to in manifest's records (what the build gave its parts, what a person adjusted):
        // a placed model moved onto another (a re-export under a new file name) is renamed for it, and its parts with it.
        // How many records it renamed.
        internal static int Rename(Manifest manifest, string from, string to)
        {
            if (manifest == null || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to) return 0;
            int renamed = 0;
            foreach (List<Manifest.Value> list in new[] { manifest.built, manifest.adjusted })
                foreach (Manifest.Value each in list)
                {
                    if (each == null || each.name == null) continue;
                    string lead = each.name.StartsWith(Mark, StringComparison.Ordinal) ? Mark : "";
                    string rest = each.name.Substring(lead.Length);
                    if (!rest.StartsWith(from, StringComparison.Ordinal) || rest.Length == from.Length) continue;
                    char next = rest[from.Length];
                    if (next != '/' && next != '#') continue;  // another root whose name only starts the same
                    each.name = lead + to + rest.Substring(from.Length);
                    renamed++;
                }
            return renamed;
        }

        // The part at a place of scene; null where it holds none.
        internal static Component Find(string where, Scene scene)
        {
            int hash = where == null ? -1 : where.LastIndexOf('#');
            if (hash < 0 || !scene.IsValid()) return null;
            string type = where.Substring(hash + 1);
            int index = 0, bracket = type.IndexOf('[');
            if (bracket >= 0)
            {
                int.TryParse(type.Substring(bracket + 1).TrimEnd(']'), NumberStyles.Integer, Invariant, out index);
                type = type.Substring(0, bracket);
            }
            string[] names = where.Substring(0, hash).Split('/');
            Transform at = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == names[0])
                {
                    at = root.transform;
                    break;
                }
            for (int i = 1; at != null && i < names.Length; i++)
            {
                Transform next = null;
                foreach (Transform child in at)
                    if (child.name == names[i])
                    {
                        next = child;
                        break;
                    }
                at = next;
            }
            if (at == null) return null;
            foreach (MonoBehaviour each in at.GetComponents<MonoBehaviour>())
                if (each != null && each.GetType().Name == type && index-- == 0) return each;
            return null;
        }

        // -- a part's plain fields ------------------------------------------------------------

        // The fields of a component a person sets in its inspector as plain values (a number, a flag, a text, a choice,
        // a colour, a vector): what a build's record holds. References, lists and events are the builders' wiring.
        static FieldInfo[] Fields(Type type)
        {
            if (FieldsOf.TryGetValue(type, out FieldInfo[] found)) return found;
            var fields = new List<FieldInfo>();
            for (Type at = type; at != null && at != typeof(MonoBehaviour) && at != typeof(object); at = at.BaseType)
                foreach (FieldInfo field in at.GetFields(Declared))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized) continue;
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) continue;
                    if (field.GetCustomAttribute<HideInInspector>() != null || !Plain(field.FieldType)) continue;
                    fields.Add(field);
                }
            fields.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return FieldsOf[type] = fields.ToArray();
        }

        static bool Plain(Type type)
            => type == typeof(float) || type == typeof(int) || type == typeof(bool) || type == typeof(string) || type.IsEnum
               || type == typeof(Color) || type == typeof(Vector2) || type == typeof(Vector3);

        // A field's value as the text the manifest keeps, and back: the one codec its values are kept by (ManifestValues).
        static string Text(object value) => ManifestValues.Text(value);

        static bool Parse(Type type, string text, out object value) => ManifestValues.Parse(type, text, out value);

        // A part's plain fields as one text, a line each (name=value, a line break in a value written \n).
        static string Pack(Component part)
        {
            var text = new StringBuilder();
            foreach (FieldInfo field in Fields(part.GetType()))
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(field.Name).Append('=').Append(Text(field.GetValue(part)).Replace("\\", "\\\\").Replace("\n", "\\n"));
            }
            return text.ToString();
        }

        static Dictionary<string, string> Unpack(string packed)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(packed)) return values;
            foreach (string line in packed.Split('\n'))
            {
                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                var value = new StringBuilder();
                for (int i = equals + 1; i < line.Length; i++)
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        value.Append(line[++i] == 'n' ? '\n' : line[i]);
                        continue;
                    }
                    value.Append(line[i]);
                }
                values[line.Substring(0, equals)] = value.ToString();
            }
            return values;
        }

        static FieldInfo Field(Component part, string name)
        {
            foreach (FieldInfo field in Fields(part.GetType()))
                if (field.Name == name) return field;
            return null;
        }

        // -- the record of a build ------------------------------------------------------------

        // The components an assessment's rows, steps and shared part hold in a scene, each once.
        static IEnumerable<Component> Held(ManifestAssessment built)
        {
            var seen = new HashSet<Component>();
            IEnumerable<UnityEngine.Object> All()
            {
                foreach (UnityEngine.Object part in built.shared.parts) yield return part;
                foreach (ManifestAssessment.Step step in built.steps)
                {
                    foreach (UnityEngine.Object part in step.parts) yield return part;
                    foreach (ManifestAssessment.Row row in step.rows)
                        foreach (UnityEngine.Object part in row.parts) yield return part;
                }
            }
            foreach (UnityEngine.Object part in All())
                if (part is MonoBehaviour component && component != null && component.gameObject.scene.IsValid() && seen.Add(component))
                    yield return component;
        }

        // What a build gave each part it holds, as Manifest.built keeps it: read while the parts stand as the
        // build left them (Lift), in the order the build holds them.
        internal static List<Manifest.Value> Record(ManifestAssessment built)
        {
            var made = new List<Manifest.Value>();
            if (built == null) return made;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Component part in Held(built))
            {
                string where = Where(part);
                if (where != null && Fields(part.GetType()).Length > 0 && seen.Add(where))
                    made.Add(new Manifest.Value { name = Mark + where, value = Pack(part) });
            }
            return made;
        }

        // Whether a value of Manifest.built is a part's record (else a bound setting's).
        internal static bool IsRecord(Manifest.Value value) => value != null && value.name.StartsWith(Mark, StringComparison.Ordinal);

        // -- what a person changed ------------------------------------------------------------

        static string Key(string where, string field) => where + "." + field;

        // Every adjustment in force: what the manifest keeps, with what the scene shows changed since the last build's
        // record (kept by the next build), less what a person set back to what the build gave. skip: the properties a
        // behavior keeps as its object's settings (IManifestBound), which are no one's twice.
        internal static List<ManifestAdjustment> Read(Manifest manifest, Scene scene, HashSet<(UnityEngine.Object, string)> skip = null)
        {
            var found = new List<ManifestAdjustment>();
            if (manifest == null) return found;
            var kept = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Manifest.Value value in manifest.adjusted)
                if (value != null) kept[value.name] = value.value;
            foreach (Manifest.Value record in manifest.built)
            {
                if (!IsRecord(record)) continue;
                string where = record.name.Substring(Mark.Length);
                Component part = Find(where, scene);
                Dictionary<string, string> gave = Unpack(record.value);
                foreach (KeyValuePair<string, string> pair in gave)
                {
                    string key = Key(where, pair.Key);
                    bool recorded = kept.TryGetValue(key, out string held);
                    FieldInfo field = part == null ? null : Field(part, pair.Key);
                    if (field != null && skip != null && skip.Contains((part, pair.Key))) continue;
                    string now = field == null ? null : Text(field.GetValue(part));
                    // In the scene: what it shows, where that differs from the build's. Not in the scene (another is
                    // open): what the manifest keeps.
                    string value = now != null ? (now != pair.Value ? now : null) : (recorded ? held : null);
                    if (value == null) continue;
                    found.Add(new ManifestAdjustment { part = part, where = where, field = pair.Key, value = value, built = pair.Value,
                                                       kept = recorded && held == value });
                }
            }
            return found;
        }

        // Keep what Read finds in the manifest, asked of ctx: a dry run lists it and keeps none.
        internal static List<ManifestAdjustment> Keep(Manifest manifest, Scene scene, OpContext ctx, HashSet<(UnityEngine.Object, string)> skip = null)
        {
            List<ManifestAdjustment> now = Read(manifest, scene, skip);
            var wanted = new List<Manifest.Value>();
            foreach (ManifestAdjustment each in now) wanted.Add(new Manifest.Value { name = Key(each.where, each.field), value = each.value });
            bool same = wanted.Count == manifest.adjusted.Count;
            for (int i = 0; same && i < wanted.Count; i++)
                same = manifest.adjusted[i] != null && manifest.adjusted[i].name == wanted[i].name && manifest.adjusted[i].value == wanted[i].value;
            if (same) return now;
            int fresh = 0;
            ManifestAdjustment first = null;
            foreach (ManifestAdjustment each in now)
                if (!each.kept)
                {
                    fresh++;
                    first ??= each;
                }
            string what = fresh > 0 ? $"keep what was adjusted in the scene: {first.Text()}" + (fresh > 1 ? $" and {fresh - 1} more" : "")
                                    : "drop the adjustments set back to what the build gave";
            if (ctx != null && !ctx.ShouldChange(manifest, what)) return now;
            manifest.adjusted = wanted;
            foreach (ManifestAdjustment each in now) each.kept = true;
            ctx.Edited(manifest);
            return now;
        }

        // -- building over them ---------------------------------------------------------------

        // Give each adjusted field what the last build gave it, so the builders work over their own last result and
        // change only what the sheet or a setting changed; the returned fields go back with Lower. A live run records
        // each part for undo first (its state before the build is the person's), without listing a change of the build.
        internal static List<(Component part, FieldInfo field, object value)> Lift(List<ManifestAdjustment> adjusted, OpContext ctx)
        {
            var lifted = new List<(Component, FieldInfo, object)>();
            var held = new HashSet<Component>();
            foreach (ManifestAdjustment each in adjusted)
            {
                if (each.part == null) continue;
                FieldInfo field = Field(each.part, each.field);
                if (field == null || !Parse(field.FieldType, each.built, out object built)) continue;
                if (ctx != null && !ctx.DryRun && held.Add(each.part))
                {
                    int asked = ctx.Changes.Count;
                    ctx.ShouldChange(each.part, "build over what the last build gave it");
                    ctx.Changes.RemoveRange(asked, ctx.Changes.Count - asked);  // the build's bookkeeping, no change of its own
                }
                lifted.Add((each.part, field, field.GetValue(each.part)));
                field.SetValue(each.part, built);
            }
            return lifted;
        }

        // Put back what Lift took: each adjusted field has the person's value again.
        internal static void Lower(List<(Component part, FieldInfo field, object value)> lifted)
        {
            if (lifted == null) return;
            foreach ((Component part, FieldInfo field, object value) in lifted)
                if (part != null) field.SetValue(part, value);
        }

        // Give the parts of a scene built anew what the manifest keeps as adjusted (a new scene's parts stand as built).
        internal static void Apply(Manifest manifest, Scene scene, OpContext ctx)
        {
            if (ctx != null && ctx.DryRun) return;
            foreach (Manifest.Value kept in manifest.adjusted)
            {
                int dot = kept == null ? -1 : kept.name.LastIndexOf('.');
                if (dot < 0) continue;
                Component part = Find(kept.name.Substring(0, dot), scene);
                FieldInfo field = part == null ? null : Field(part, kept.name.Substring(dot + 1));
                if (field == null || !Parse(field.FieldType, kept.value, out object value)) continue;
                field.SetValue(part, value);
                ctx.Edited(part);
            }
        }

        // Give the adjusted fields of part (null: of every part) what the build gave them, and drop them from the
        // manifest: how many went (or would, in a dry run).
        internal static int Revert(Manifest manifest, Scene scene, Component part, OpContext ctx)
        {
            List<ManifestAdjustment> gone = Read(manifest, scene).FindAll(each => part == null || each.part == part);
            if (gone.Count == 0) return 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ManifestAdjustment each in gone)
            {
                names.Add(Key(each.where, each.field));
                if (each.part == null) continue;
                FieldInfo field = Field(each.part, each.field);
                if (field == null || !Parse(field.FieldType, each.built, out object built)) continue;
                if (ctx != null && !ctx.ShouldChange(each.part, $"give {each.field} what the build gave it again: {each.built}")) continue;
                field.SetValue(each.part, built);
                ctx.Edited(each.part);
            }
            if (manifest.adjusted.Exists(kept => kept != null && names.Contains(kept.name))
                && (ctx == null || ctx.ShouldChange(manifest, $"drop {gone.Count} adjustment(s) made in the scene")))
            {
                manifest.adjusted.RemoveAll(kept => kept == null || names.Contains(kept.name));
                ctx.Edited(manifest);
            }
            return gone.Count;
        }
    }
}
