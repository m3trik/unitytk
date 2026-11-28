using System;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// Marks an <see cref="IManifestBehavior"/> as the builder of a task's plan behavior (the
    /// manifest's <see cref="ManifestBehaviorAttribute"/>, with a task's traits): the word a row
    /// reads as, and what kind of row it is, which the rest of the task builds by
    /// (<see cref="TaskBehaviors.Traits(SheetPlan.Shape.PlanAction, SheetPlan.Shape.PlanStep)"/>), so a project's own
    /// behavior takes part as the built-in ones do. Its settings are the group of <see cref="TaskSettings"/> named as the
    /// word, where there is one. What a row does that needs its own code to say is an interface its builder
    /// implements: <see cref="ITaskRowTraits"/> (its traits refined per row), <see cref="ITaskPageContent"/> (what it puts
    /// on its step's page), <see cref="IStepGate"/> (a button its step waits on).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class TaskBehaviorAttribute : ManifestBehaviorAttribute
    {
        /// <param name="name">The behavior's name (the sheet's word, snake_case).</param>
        public TaskBehaviorAttribute(string name) : base(name)
        {
            // A task's reading puts these on its rows (a behavior may say otherwise): what the hand holds, where a placing row
            // happens (one entry a Target), the side a travel stands on; how the manifest labels its fields; and a choice and
            // its jump as what tells two rows apart. Declared here, not read off the plan: a row added in the manifest, and a
            // field a person sets there, no reading ever read.
            NodeFields = new[] { TaskKit.Fields.With, TaskKit.Fields.Where, TaskKit.Fields.SideOf };
            FieldLabels = new[] { TaskKit.Fields.With + "=In hand", TaskKit.Fields.Phrase + "=Says" };
            ChipFields = new[] { "'{" + TaskKit.Fields.Label + "}'", "→ {to}" };
        }

        /// <summary>
        /// Whether its rows have the user act on their targets (select, use, take, a project's own
        /// <c>inspect</c>): its targets pulse when highlighted, a button in its step that goes on waits for
        /// it, and the spot a travel brings the user to keeps them in sight.
        /// </summary>
        public bool ActsOn { get; set; }

        /// <summary>Whether its rows offer their label as a button on the step's page (button).</summary>
        public bool Offers { get; set; }

        /// <summary>
        /// Whether choosing its row's label ends the step (a button that goes somewhere; not an answer of a question,
        /// which may stay: <see cref="ITaskRowTraits"/>).
        /// </summary>
        public bool EndsStep { get; set; }

        /// <summary>
        /// Whether its rows have the user choose what follows (button, answer): its step waits on that choice. Every other
        /// step goes on by itself once done (<see cref="TaskBuild"/>), a row that <see cref="GoesOn"/> only saying so.
        /// </summary>
        public bool Chooses { get; set; }

        /// <summary>
        /// Whether its row says its step goes on by itself once done and heard or read (<c>advance</c>), whatever
        /// else the step offers; turned off in the manifest, its step waits (and, off for every row, every step).
        /// </summary>
        public bool GoesOn { get; set; }

        /// <summary>Whether the hand holds its rows' targets (take), as it holds any row's With: they are made holdable.</summary>
        public bool Holds { get; set; }

        /// <summary>Whether its rows name places, not what the user looks at (travel): a page never keeps clear of them.</summary>
        public bool Places { get; set; }

        /// <summary>
        /// Whether its rows bring the user to a viewpoint (<c>travel</c>): the row's <c>where</c>, else the one the kit places
        /// for its step before any row is built, where what the row's Target names is seen. Every other spot (an arrow, a
        /// picture, a page) is placed from where the last of them left the user, and a rig the build made starts before the first.
        /// </summary>
        public bool Travels { get; set; }

        /// <summary>Whether its rows send their targets back where they rest as the step ends (<c>return</c>): a hide of the step leaves them shown.</summary>
        public bool Returns { get; set; }

        /// <summary>
        /// The behavior its rows pair with, by name: a <c>tick</c> ticks an item of the latest row of its partner
        /// (<c>list</c>) that lists it. The pair goes both ways (<see cref="TaskBehaviors.Pairs"/>): a project's own verb naming
        /// a kit's behavior as its partner pairs with it (a tick of its own a list lists). Empty: none.
        /// </summary>
        public string Partner { get; set; } = "";

        // A copy of these traits, for one row to refine (TaskBehaviors.Traits).
        internal TaskBehaviorAttribute Copy() => (TaskBehaviorAttribute)MemberwiseClone();
    }
}
