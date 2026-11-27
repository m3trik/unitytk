using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// The behaviors this editor can build: every <see cref="IManifestBehavior"/> marked
    /// <see cref="ManifestBehaviorAttribute"/> (or a kit's attribute deriving from it), by its name;
    /// and the kits (<see cref="IManifestKit"/>) that build around them.
    /// </summary>
    /// <remarks>
    /// Prior art: the kernel's provider registry (<c>Provided</c>) serves a contract from
    /// ONE provider, and counts a second as an error; a behavior is one of many, each keyed by
    /// name, so this registry reads Unity's own type index (<c>TypeCache.GetTypesDerivedFrom</c> of
    /// <see cref="IManifestBehavior"/>, keeping those marked with the attribute, a class's several marks
    /// each a name), keyed by the attribute's name.
    /// Instances live for the domain, rebuilt after a reload: the one kind of mutable static
    /// state the C# rules allow.
    /// </remarks>
    public static class ManifestBehaviors
    {
        static Dictionary<string, Type> _types;
        static Dictionary<string, ManifestBehaviorAttribute> _marks;  // each behavior's attribute, by its name
        static List<IManifestKit> _kits;
        static readonly Dictionary<string, IManifestBehavior> _instances =
            new Dictionary<string, IManifestBehavior>(StringComparer.Ordinal);

        /// <summary>The builder of <paramref name="behavior"/>; null when this editor has none.</summary>
        /// <param name="behavior">A plan action's behavior.</param>
        /// <exception cref="InvalidOperationException">Two classes claim the behavior: the message names both.</exception>
        public static IManifestBehavior Find(string behavior)
        {
            if (string.IsNullOrEmpty(behavior)) return null;
            if (_instances.TryGetValue(behavior, out IManifestBehavior known)) return known;
            if (!Types().TryGetValue(behavior, out Type type)) return null;
            var made = (IManifestBehavior)Activator.CreateInstance(type);
            _instances[behavior] = made;
            return made;
        }

        /// <summary>
        /// The attribute <paramref name="behavior"/>'s builder is marked with: a kit's own (deriving from
        /// <see cref="ManifestBehaviorAttribute"/>) carries what its behaviors say about themselves; null
        /// where this editor has no builder for it.
        /// </summary>
        /// <param name="behavior">A plan action's behavior.</param>
        public static ManifestBehaviorAttribute Mark(string behavior)
        {
            Types();
            return !string.IsNullOrEmpty(behavior) && _marks.TryGetValue(behavior, out ManifestBehaviorAttribute mark) ? mark : null;
        }

        /// <summary>Every behavior this editor can build, sorted.</summary>
        public static IReadOnlyList<string> Names()
        {
            var names = new List<string>(Types().Keys);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// The behavior <paramref name="behavior"/> is a way of setting (its mark's
        /// <see cref="ManifestBehaviorAttribute.Family"/>: <c>fade_in</c> is <c>fade</c>), else itself.
        /// </summary>
        /// <param name="behavior">A behavior's name.</param>
        public static string FamilyOf(string behavior)
        {
            ManifestBehaviorAttribute mark = Mark(behavior);
            return mark != null && !string.IsNullOrEmpty(mark.Family) && !string.IsNullOrEmpty(mark.Variant) ? mark.Family : behavior;
        }

        /// <summary>
        /// The behaviors of <paramref name="behavior"/>'s family, sorted (<c>fade_in</c>: <c>fade_in</c>,
        /// <c>fade_out</c>); itself alone where it has none: what one switch turns off or on.
        /// </summary>
        /// <param name="behavior">A behavior's name.</param>
        public static IReadOnlyList<string> Family(string behavior)
        {
            string family = FamilyOf(behavior);
            var members = new List<string>();
            foreach (string name in Names())
                if (name == behavior || (family != behavior && FamilyOf(name) == family)) members.Add(name);
            if (!members.Contains(behavior)) members.Add(behavior);
            return members;
        }

        /// <summary>
        /// The kits that take part in a build of <paramref name="plan"/>: each <see cref="IManifestKit"/>
        /// one of the plan's rows belongs to, by type name (a stable order). A row is a kit's when its
        /// behavior's builder is compiled in the kit's assembly, or is marked with the kit's own attribute
        /// (a project's behavior marked with a kit's own attribute is that kit's).
        /// </summary>
        /// <param name="plan">The plan.</param>
        public static IReadOnlyList<IManifestKit> Kits(SheetPlan.Shape.Payload plan)
        {
            var used = new HashSet<Assembly>();
            if (plan != null && plan.steps != null)
                foreach (var step in plan.steps)
                    if (step.actions != null)
                        foreach (var action in step.actions)
                            if (action != null && Types().TryGetValue(action.behavior ?? "", out Type type))
                            {
                                used.Add(type.Assembly);
                                used.Add(_marks[action.behavior].GetType().Assembly);  // a kit's own attribute: its behavior wherever it is compiled
                            }
            var taking = new List<IManifestKit>();
            foreach (IManifestKit kit in AllKits())
                if (used.Contains(kit.GetType().Assembly)) taking.Add(kit);
            return taking;
        }

        /// <summary>
        /// The kit <paramref name="behavior"/> is one of: the kit compiled beside its attribute (a kit's own attribute
        /// marks its behaviors wherever they are compiled), else the one compiled beside its builder; null where
        /// neither assembly holds a kit, or this editor has no builder for it.
        /// </summary>
        /// <param name="behavior">A plan action's behavior.</param>
        public static IManifestKit KitOf(string behavior)
        {
            if (string.IsNullOrEmpty(behavior) || !Types().TryGetValue(behavior, out Type type)) return null;
            return KitIn(_marks[behavior].GetType().Assembly) ?? KitIn(type.Assembly);
        }

        /// <summary>
        /// The settings type the fields <paramref name="behavior"/> names as its own settings
        /// (<see cref="ManifestBehaviorAttribute.Settings"/>) are of: its kit's (<see cref="KitOf"/>); null where it
        /// names none, belongs to no kit, or its kit keeps no settings.
        /// </summary>
        /// <param name="behavior">A plan action's behavior.</param>
        public static Type SettingsOf(string behavior)
        {
            string[] named = Mark(behavior)?.Settings;
            return named == null || named.Length == 0 ? null : KitOf(behavior)?.Settings;
        }

        // The kit compiled in assembly (the first by type name); null where it holds none.
        internal static IManifestKit KitIn(Assembly assembly)
        {
            foreach (IManifestKit kit in AllKits())
                if (kit.GetType().Assembly == assembly) return kit;
            return null;
        }

        /// <summary>Every kit this editor holds, by type name.</summary>
        public static IReadOnlyList<IManifestKit> AllKits()
        {
            if (_kits != null) return _kits;
            var found = new List<IManifestKit>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IManifestKit>())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException(
                        $"{type.FullName} is an IManifestKit with no public constructor without parameters: give it one");
                found.Add((IManifestKit)Activator.CreateInstance(type));
            }
            found.Sort((a, b) => string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName));
            return _kits = found;
        }

        static Dictionary<string, Type> Types()
        {
            if (_types != null) return _types;
            var found = new Dictionary<string, Type>(StringComparer.Ordinal);
            var marks = new Dictionary<string, ManifestBehaviorAttribute>(StringComparer.Ordinal);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IManifestBehavior>())
            {
                var own = (ManifestBehaviorAttribute[])type.GetCustomAttributes(typeof(ManifestBehaviorAttribute), false);
                if (own.Length == 0 || type.IsAbstract) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException(
                        $"{type.FullName} is marked as a manifest behavior but has no public constructor " +
                        "without parameters: give it one, or take the attribute off");
                foreach (ManifestBehaviorAttribute mark in own)
                {
                    if (found.TryGetValue(mark.Name, out Type other) && other != type)
                        throw new InvalidOperationException(
                            $"the manifest behavior '{mark.Name}' is claimed by {other.FullName} and {type.FullName}: " +
                            "one builder per behavior; give one of them another name");
                    found[mark.Name] = type;
                    marks[mark.Name] = mark;
                }
            }
            _marks = marks;
            return _types = found;
        }
    }
}
