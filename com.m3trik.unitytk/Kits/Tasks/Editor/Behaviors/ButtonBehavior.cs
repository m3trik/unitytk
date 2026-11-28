using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>button</c> ("Selecting 'Next' goes on"): a button on the step's page labelled as the row's Label, going where its To
    /// says (<see cref="Choices"/>): <c>next</c> on, a step's ID there, <c>exit</c> to the task's end, <c>restart</c> to its
    /// start again (the scene loaded again), <c>retry</c> nowhere (the step stays, saying the row's
    /// <see cref="ButtonSettings.reply"/>). A step with a <c>retry</c> button is a question: its buttons are its answers, the
    /// right one going on (<c>next</c>), each wrong one staying; neither ends its step (<see cref="ITaskRowTraits"/>). A
    /// button that goes on and is no answer holds its step (<see cref="IStepGate"/>): as it finishes, offered once the
    /// step's actions are done (<see cref="ButtonSettings.when"/> End, the usual), or at its start (Start), the button
    /// starting them (read first, then go). Every other step goes on by itself (<see cref="TaskBuild"/>).
    /// </summary>
    [TaskBehavior("button", Fields = new[] { TaskKit.Fields.Label, "to" }, NamesObjects = false, Chooses = true, Offers = true, EndsStep = true,
                  Suffix = new[] { nameof(ButtonSettings.when) },
                  About = "A button on the step's page, going where its To says: on, to a step, to the task's end or its start; a wrong answer (retry) stays and says its reply.")]
    internal sealed class ButtonBehavior : IManifestBehavior, IStepGate, ITaskRowTraits, ITaskPageContent
    {
        // Whether step asks a question: a button of it stays (To retry), so its buttons are answers.
        static bool Asks(SheetPlan.Shape.PlanStep step)
        {
            if (step == null || step.actions == null) return false;
            foreach (var other in step.actions)
                if (other != null && other.to == TaskKit.To.Retry && TaskBehaviors.Traits(other.behavior).Offers) return true;
            return false;
        }

        // Whether action, a button of step, is an answer of a question: a wrong one (retry), or the right one (next) beside one.
        static bool Answers(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step)
            => action.to == TaskKit.To.Retry || action.to == TaskKit.To.Next && Asks(step);

        /// <inheritdoc/>
        public void Refine(TaskBehaviorAttribute traits, SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step)
            => traits.EndsStep = !Answers(action, step);

        /// <inheritdoc/>
        public bool Gates(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step)
            => action.to == TaskKit.To.Next && !Asks(step);

        /// <inheritdoc/>
        public bool AtStart(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene)
            => Gates(action, step) && scene.Settings<TaskSettings>(step.id, "", action).button.when == Moment.Start;

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Label))) yield return "it names no button: set its Label";
            string where = Choices.Problem(scene.Plan, action.to);
            if (where != null) yield return where;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string label = action.Field(TaskKit.Fields.Label);
            Choices.Add(scene, step, label, action.to, ctx);
            if (Gates(action, step))
            {
                StepsActions.SetStarts(scene.Sequence(), step.id, label, AtStart(action, step, scene), ctx);
                return;
            }
            if (scene.Settings<TaskSettings>(step.id, "", action).button.when == Moment.Start)
                ctx.Raise(ManifestsActions.NoteCode, $"{step.id}: its '{label}' button holds no step at its start: only a button that goes on " +
                                                     $"({TaskKit.To.Next}) and answers no question does; its when is unused");
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
        {
            if (action.to == TaskKit.To.Retry) page.Reply(action.Field(TaskKit.Fields.Label), scene.Settings<TaskSettings>(step.id, "", action).button.reply);
        }
    }
}
