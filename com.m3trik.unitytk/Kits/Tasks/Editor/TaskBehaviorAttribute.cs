using System;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// Marks an <see cref="IManifestBehavior"/> as the builder of a task's plan behavior (the
    /// manifest's <see cref="ManifestBehaviorAttribute"/>, with a task's traits): the name a
    /// clause template reads a row as, and what kind of row it is, which the rest of the task builds
    /// by (<see cref="TaskBehaviors.Traits"/>), so a project's own behavior takes part as the
    /// built-in ones do. What a row does that needs its own code to say is an interface its builder
    /// implements: <see cref="ITaskPageContent"/> (what it puts on its step's page),
    /// <see cref="IStepGate"/> (a button its step waits on).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class TaskBehaviorAttribute : ManifestBehaviorAttribute
    {
        /// <param name="name">The behavior's name (snake_case, a clause template's <c>behavior</c>).</param>
        public TaskBehaviorAttribute(string name) : base(name)
        {
            // A task's reading puts these on its rows (a behavior may say otherwise): the nodes in hand, faced, stood
            // beside and pointed at (every node group of the built-in clause templates, and the reading's hand and arrows:
            // test_sample_task.py holds them to it), how the manifest labels its fields, and a choice and its jump as what
            // tells two rows apart. Declared here, not read off the plan: a row added in the manifest, and a field a person
            // sets there, no clause template ever read.
            NodeFields = new[] { TaskKit.Fields.Tool, TaskKit.Fields.Facing, TaskKit.Fields.SideOf, TaskKit.Fields.Arrows, TaskKit.Fields.From, TaskKit.Fields.Faces };
            FieldLabels = new[] { TaskKit.Fields.Label + "=Choice", TaskKit.Fields.Tool + "=In hand", TaskKit.Fields.From + "=Taken from", "phrase=Says" };
            ChipFields = new[] { "'{" + TaskKit.Fields.Label + "}'", "→ {to}" };
        }

        /// <summary>
        /// Whether its rows have the user act on their targets (select, use, take, place, a
        /// project's own <c>inspect</c>): its targets pulse when highlighted, a Next in its step waits for
        /// it, and the spot a teleport brings the user to keeps them in sight.
        /// </summary>
        public bool ActsOn { get; set; }

        /// <summary>Whether its rows offer their label as a button on the step's page (pause, goto, exit, quiz, option).</summary>
        public bool Offers { get; set; }

        /// <summary>
        /// Whether choosing its row's label ends the step (pause, goto, exit, quiz): a row written after it
        /// in its step happens as the step ends, and an option sharing its label does what it says.
        /// </summary>
        public bool EndsStep { get; set; }

        /// <summary>
        /// Whether its rows have the user choose what follows (pause, goto, exit, quiz, option, a key pad and its
        /// answers): its step waits on that choice. Every other step goes on by itself once done
        /// (<see cref="TaskBuild"/>), a row that <see cref="GoesOn"/> only saying so.
        /// </summary>
        public bool Chooses { get; set; }

        /// <summary>
        /// Whether its row says its step goes on by itself once done and heard or read (<c>advance</c>), whatever
        /// else the step offers; turned off in the manifest, its step waits (and, off for every row, every step).
        /// </summary>
        public bool GoesOn { get; set; }

        /// <summary>Whether the hand holds its rows' targets (take, hold), as it holds any row's tool: they are made holdable.</summary>
        public bool Holds { get; set; }

        /// <summary>Whether its rows name places, not what the user looks at (teleport, marker): a page never keeps clear of them.</summary>
        public bool Places { get; set; }

        /// <summary>
        /// Whether its rows bring the user to a viewpoint (<c>teleport</c>): the row's first target, else the one the
        /// kit places for its step before any row is built, where what the row's <c>facing</c> names is seen. Every other
        /// spot (an arrow, a picture, a page) is placed from where the last of them left the user, a rig the build made
        /// starts before the first, and a marker naming no place marks the next.
        /// </summary>
        public bool Teleports { get; set; }

        /// <summary>Whether its rows send their targets back where they rest as the step ends (<c>return</c>): a fade of the step leaves them shown.</summary>
        public bool Returns { get; set; }

        /// <summary>
        /// The behavior its rows pair with, by name: a <c>check</c> ticks an item of the latest row of its partner
        /// (<c>checklist</c>), a <c>quiz</c> goes to the first later step holding one (<c>option</c>). The pair goes both
        /// ways (<see cref="TaskBehaviors.Pairs"/>): a project's own verb naming a kit's behavior as its partner pairs
        /// with it (a tick of its own a checklist lists). Empty: none.
        /// </summary>
        public string Partner { get; set; } = "";
    }
}
