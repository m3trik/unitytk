using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// What a sheet builds in this project, as one asset: its plan (the sheet as read), the
    /// delivery the plan is built into (the model and any extra files beside it), the scene that
    /// holds the result, the steps a person locked, and each kit's own settings. The Manifest
    /// window (Tools > unitytk > Manifest) opens on one; Create > unitytk > Manifest makes one, as
    /// <see cref="ManifestsActions.Create"/> does from a plan.
    /// </summary>
    [CreateAssetMenu(menuName = "unitytk/Manifest", fileName = "Manifest")]
    public sealed class Manifest : ScriptableObject
    {
        [Tooltip("The plan: the JSON the sheet's reader writes (the sheet's steps and rows, each read as a behavior).")]
        public TextAsset plan;

        [Tooltip("The delivery's model (an imported FBX), placed in the scene once.")]
        public GameObject delivery;

        [Tooltip("Files whose nodes join the delivery (anchors, viewpoints a delivery authors in files of their own), placed beside it.")]
        public List<GameObject> extras = new List<GameObject>();

        [Tooltip("Where the built scene is saved (a .unity path under Assets); empty: the open scene, unsaved.")]
        public string scene = "";

        [Tooltip("The asset folder the files the plan's steps name are found under, at their paths (a step's sound; empty: the plan's folder).")]
        [FormerlySerializedAs("soundFolder")]
        public string assetFolder = "";

        [Tooltip("The sheet's settings file (or the sheet), as a path: what the plan is read from again when the sheet changes. Empty: not known here.")]
        public string sheet = "";

        [Tooltip("The steps a build leaves as they are, by ID (locked in the Manifest window).")]
        public List<string> locked = new List<string>();

        [Tooltip("Each kit's own settings (what its behaviors build by); a kit with none here builds by its defaults.")]
        public List<ScriptableObject> settings = new List<ScriptableObject>();

        [Tooltip("The behaviors a build leaves out, by name: every row of one is off (turned off in the Manifest window).")]
        public List<string> off = new List<string>();

        [Tooltip("Where this project differs from the sheet, an object of a step at a time: a behavior the sheet gives it left out, " +
                 "or one the sheet does not give it added. Listed for the sheet's owner in the window's Export.")]
        public List<Edit> edits = new List<Edit>();

        /// <summary>What an edit or an object's own values are kept for: a behavior of one object in one step, one row of it or every row.</summary>
        [System.Serializable]
        public abstract class Entry
        {
            [Tooltip("The step's ID.")]
            public string step = "";

            [Tooltip("The object, as the sheet's rows name it (a node's path in the delivery); empty: the step's own row.")]
            public string target = "";

            [Tooltip("The behavior's name.")]
            public string behavior = "";

            [Tooltip("Which of the step's rows it is, by its place among them in the sheet (from 0); -1: every row of the behavior " +
                     "the object has, or a row added here. Two rows of one behavior on one object (a marker shown, then hidden) are told apart by it.")]
            public int row = -1;
        }

        /// <summary>One difference from the sheet: a behavior of one object in one step, left out or added.</summary>
        [System.Serializable]
        public sealed class Edit : Entry
        {
            [Tooltip("Added here (the sheet does not give the object this behavior in this step); off: left out (the sheet does).")]
            public bool add;
        }

        [Tooltip("An object's own values for a behavior in a step: its settings over its kit's (an arrow's colour, a button held to play), " +
                 "and its row's fields over the sheet's (the take it plays, the step it goes to: what a row this project added needs). " +
                 "Kept, marked and reverted as edits.")]
        public List<Tune> tunes = new List<Tune>();

        [Tooltip("What the last build gave the parts a behavior builds from its settings (an arrow's size), so the next tells a person's " +
                 "change in the scene from a change of the settings. Written by every build.")]
        public List<Value> built = new List<Value>();

        [Tooltip("What a person changed in the scene on a part a build made (where a page comes up, how long a press is held), by the " +
                 "part's place in the scene and its field. Kept over what every later build gives the part; listed and reverted with the edits.")]
        public List<Value> adjusted = new List<Value>();

        [Tooltip("The helpers a person moved from where a build placed them (a viewpoint, a picture's camera, an arrow), each by the key " +
                 "its kit placed it under, its place as that kit writes it. Every build puts them back, in this scene or one built anew; " +
                 "listed and reverted with the other adjustments.")]
        public List<Value> moved = new List<Value>();

        /// <summary>One object's own values for a behavior in a step: its settings over its kit's, and its row's fields over the sheet's.</summary>
        [System.Serializable]
        public sealed class Tune : Entry
        {
            [Tooltip("Its own settings, each by its name in its kit's settings, as text.")]
            public List<Value> values = new List<Value>();

            [Tooltip("Its row's own fields, each by the plan's name for it (the take it plays, the step it goes to), as text.")]
            public List<Value> fields = new List<Value>();

            /// <summary>Its own setting named <paramref name="name"/>; null where it keeps none.</summary>
            /// <param name="name">A setting's name.</param>
            public string Get(string name) => Value.Find(values, name);

            /// <summary>Keep <paramref name="value"/> as its own setting named <paramref name="name"/>; null: keep none.</summary>
            /// <param name="name">A setting's name.</param>
            /// <param name="value">Its value, as text; null drops it.</param>
            public void Set(string name, string value) => Value.Put(values, name, value);

            /// <summary>Its row's own field named <paramref name="name"/>; null where it keeps none.</summary>
            /// <param name="name">A row field's name.</param>
            public string Field(string name) => Value.Find(fields, name);

            /// <summary>Keep <paramref name="value"/> as its row's own field named <paramref name="name"/>; null: keep none.</summary>
            /// <param name="name">A row field's name.</param>
            /// <param name="value">Its value, as text; null drops it.</param>
            public void SetField(string name, string value) => Value.Put(fields, name, value);

            /// <summary>Whether it keeps nothing: neither a setting nor a field.</summary>
            public bool Empty => values.Count == 0 && fields.Count == 0;
        }

        /// <summary>A named value, as text.</summary>
        [System.Serializable]
        public sealed class Value
        {
            [Tooltip("Its name.")]
            public string name = "";

            [Tooltip("Its value, as text (a number in invariant form, a colour as its four numbers r,g,b,a, or #RRGGBBAA; a choice by its name).")]
            public string value = "";

            // The value named name in list; null where it has none.
            internal static string Find(List<Value> list, string name)
            {
                foreach (Value each in list)
                    if (each != null && each.name == name) return each.value;
                return null;
            }

            // Keep value under name in list (null: drop it).
            internal static void Put(List<Value> list, string name, string value)
            {
                list.RemoveAll(each => each == null || each.name == name);
                if (value != null) list.Add(new Value { name = name, value = value });
            }
        }

        /// <summary>The object's own values for <paramref name="behavior"/> in <paramref name="step"/>; null where it keeps none.</summary>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet names it; empty: the step's own row.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="row">Which of the step's rows (<see cref="Entry.row"/>): its own, else one kept for every row; -1: the one kept for every row.</param>
        public Tune TuneOf(string step, string target, string behavior, int row = -1) => Find(tunes, step, target, behavior, row);

        /// <summary>The edit of <paramref name="behavior"/> on <paramref name="target"/> in <paramref name="step"/>; null where it keeps none.</summary>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet names it; empty: the step's own row.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="row">Which of the step's rows (<see cref="Entry.row"/>): its own, else one kept for every row; -1: the one kept for every row.</param>
        public Edit EditOf(string step, string target, string behavior, int row = -1) => Find(edits, step, target, behavior, row);

        // The entry of list for one object's behavior in a step: row's own, else the one kept for every row.
        static T Find<T>(List<T> list, string step, string target, string behavior, int row) where T : Entry
        {
            T every = null;
            foreach (T each in list)
            {
                if (each == null || each.step != step || each.target != (target ?? "") || each.behavior != behavior) continue;
                if (each.row == row) return each;
                if (each.row == -1) every = each;
            }
            return every;
        }

        /// <summary>The settings of type <typeparamref name="T"/> it keeps; null where it keeps none (the kit's defaults apply).</summary>
        /// <typeparam name="T">A kit's settings type.</typeparam>
        public T Settings<T>() where T : ScriptableObject => Settings(typeof(T)) as T;

        /// <summary>The settings of <paramref name="type"/> it keeps; null where it keeps none.</summary>
        /// <param name="type">A kit's settings type.</param>
        public ScriptableObject Settings(System.Type type)
        {
            foreach (ScriptableObject each in settings)
                if (each != null && type.IsInstanceOfType(each)) return each;
            return null;
        }
    }
}
