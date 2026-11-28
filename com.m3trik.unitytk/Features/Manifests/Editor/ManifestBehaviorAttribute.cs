using System;
using UnityEditor;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// Marks an <see cref="IManifestBehavior"/> as the builder of the plan behavior <see cref="Name"/>:
    /// the name the sheet's reader gives a row. A kit's own attribute derives from it to carry what
    /// its behaviors say about themselves (<see cref="ManifestBehaviors.Mark"/> hands it back), so a
    /// project's own behavior takes part as a kit's do.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ManifestBehaviorAttribute : Attribute
    {
        /// <param name="name">The behavior's name (snake_case, as the plan spells it).</param>
        public ManifestBehaviorAttribute(string name)
        {
            Name = name;
        }

        /// <summary>The behavior's name.</summary>
        public string Name { get; }

        /// <summary>What a row of it does, in a sentence: what the manifest says of it wherever it is listed. Empty: nothing said.</summary>
        public string About { get; set; } = "";

        /// <summary>
        /// The fields of its kit's settings (<see cref="IManifestKit.Settings"/>) that say how every row of
        /// it is built, by name: what the manifest opens as the behavior's own settings. Where its kit's settings have a
        /// member named as the behavior (a <c>[Serializable]</c> class grouping its settings: <c>indicate</c>), a row's
        /// settings resolve under it (<see cref="ManifestBehaviors.SettingName"/>): a name here with no dot is that member's
        /// field (<c>size</c>: <c>indicate.size</c>), and none at all (null) is each of its fields. Null with no such
        /// member: it has none.
        /// </summary>
        public string[] Settings { get; set; }

        /// <summary>
        /// The fields of <see cref="Settings"/> that are its row's as a whole, never one object's (a take's window and speed:
        /// the delivery's clip player keeps one entry per take, whatever objects the row names): the manifest offers them for
        /// the row alone, keeps them under it (an empty target, as every setting of a row that names no object) and refuses
        /// them for one of its objects (<see cref="ManifestsActions.SetTune"/>); its builder reads them as the row's
        /// (<c>scene.Settings(step, "", row)</c>). Named as <see cref="Settings"/> names them (a group's field by its own name).
        /// Null: none.
        /// </summary>
        public string[] RowSettings { get; set; }

        /// <summary>The settings the manifest offers one object of its rows as that object's own: its <see cref="Settings"/> but its <see cref="RowSettings"/>.</summary>
        public string[] ObjectSettings()
            => Settings == null ? Array.Empty<string>() : Array.FindAll(Settings, setting => !IsRowSetting(setting));

        /// <summary>Whether <paramref name="setting"/> is its rows' as a whole (<see cref="RowSettings"/>), never one object's.</summary>
        /// <param name="setting">A setting's name.</param>
        public bool IsRowSetting(string setting) => RowSettings != null && Array.IndexOf(RowSettings, setting) >= 0;

        /// <summary>
        /// The fields of its rows a person may set in the manifest, by the plan's names (<c>clip</c>, <c>with</c>,
        /// <c>to</c>, <c>label</c>...): what a row added here needs to be built, and what one object of a sheet's
        /// row may have of its own. Null: none.
        /// </summary>
        public string[] Fields { get; set; }

        /// <summary>
        /// The fields of its rows that name nodes of the delivery, by the plan's names (what the hand holds, where
        /// a placing row happens): an assessment finds each as it finds the row's targets, and a name no
        /// node answers to keeps the row from being built. Null: none (a field of another meaning is never
        /// taken for a node).
        /// </summary>
        public string[] NodeFields { get; set; }

        /// <summary>
        /// The fields of its rows that name takes of the delivery, by the plan's names: an assessment finds each
        /// among the delivery's takes, and a name none answers to keeps the row from being built. The contract's
        /// own take (<c>clip</c>) by default; empty: none.
        /// </summary>
        public string[] TakeFields { get; set; } = { "clip" };

        /// <summary>
        /// What the manifest calls its rows' fields where it lists them (a chip's tooltip, the window of one
        /// object's values), as <c>name=Label</c> pairs (<c>label=Choice</c>); a field named in none reads as
        /// <see cref="DefaultLabel"/> says. Null: none.
        /// </summary>
        public string[] FieldLabels { get; set; }

        /// <summary>
        /// What a row's chip says beside its behavior's name, telling two rows of it apart (a step's two choices):
        /// templates whose <c>{field}</c> holes take the row's values (<c>'{label}'</c>, <c>→ {to}</c>; a node
        /// field's value as its node's own name), joined by a space; one whose holes are all empty is left out.
        /// Null: the name alone.
        /// </summary>
        public string[] ChipFields { get; set; }

        /// <summary>
        /// The settings whose value a row's chip says beside its name where the row or its object sets it (one
        /// behavior read several ways: <c>Use · hold</c>, <c>Button · start</c>), named as <see cref="Settings"/> names
        /// them. Null: none.
        /// </summary>
        public string[] Suffix { get; set; }

        /// <summary>
        /// The behavior this one is a way of setting, when several are (a swing's <c>swing_open</c> and <c>swing_shut</c>
        /// are both <c>swing</c>): the manifest reads a family as one behavior, its name led by the family's and
        /// followed by <see cref="Variant"/> (<c>Swing · open</c>), and one switch turns every row of each off or on.
        /// Each keeps its own name in plans and deliveries. Null: none.
        /// </summary>
        public string Family { get; set; }

        /// <summary>How it sets its <see cref="Family"/>, as its suffix reads (<c>open</c>, <c>shut</c>).</summary>
        public string Variant { get; set; }

        /// <summary>
        /// Whether its rows name the objects they act on (a highlight, an arrow): what the manifest may give
        /// an object. False: a row of its step's own (go on, a note), never offered for an object.
        /// </summary>
        public bool NamesObjects { get; set; } = true;

        // Whether its settings are named by their paths in its kit's settings yet (NameSettings).
        internal bool Named { get; private set; }

        // Name its settings by their paths in settings (its kit's), once (ManifestBehaviors.Mark): where settings group its
        // behavior's (a member named as the behavior, ManifestValues.Group), a name with no dot is that group's field, and
        // no Settings at all is each of its fields. Left as written where settings has no group of it.
        internal void NameSettings(Type settings)
        {
            if (Named) return;
            Named = true;
            string[] group = ManifestValues.GroupSettings(settings, Name);
            if (group == null) return;
            Settings = Settings == null ? group : Under(Settings);
            RowSettings = Under(RowSettings);
            Suffix = Under(Suffix);
        }

        // names, each with no dot named under its behavior's group (size: indicate.size).
        string[] Under(string[] names)
            => names == null ? null : Array.ConvertAll(names, name => string.IsNullOrEmpty(name) || name.IndexOf('.') >= 0 ? name : $"{Name}.{name}");

        /// <summary>What the manifest calls the field <paramref name="field"/> of its rows: its <see cref="FieldLabels"/> entry, else <see cref="DefaultLabel"/>.</summary>
        /// <param name="field">A field's name, as the plan spells it.</param>
        public string FieldLabel(string field)
        {
            if (FieldLabels != null && !string.IsNullOrEmpty(field))
                foreach (string pair in FieldLabels)
                    if (pair != null && pair.Length > field.Length && pair[field.Length] == '=' && pair.StartsWith(field, StringComparison.Ordinal))
                        return pair.Substring(field.Length + 1);
            return DefaultLabel(field);
        }

        /// <summary>Whether the field <paramref name="field"/> of its rows names a node (<see cref="NodeFields"/>).</summary>
        /// <param name="field">A field's name.</param>
        public bool NamesNode(string field) => NodeFields != null && Array.IndexOf(NodeFields, field) >= 0;

        /// <summary>
        /// A field's label where no behavior says one: the contract's words for its own (<c>clip</c> the take,
        /// <c>to</c> the step a row goes to), else its name in words (<c>side_of</c>: Side of).
        /// </summary>
        /// <param name="field">A field's name, as the plan spells it.</param>
        public static string DefaultLabel(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field == nameof(SheetPlan.Shape.PlanAction.clip)) return "Take";
            if (field == nameof(SheetPlan.Shape.PlanAction.to)) return "Goes to";
            return ObjectNames.NicifyVariableName(field.Replace('_', ' '));
        }
    }
}
