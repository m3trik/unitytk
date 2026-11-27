using M3trik.Unitytk.Manifests;
using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;
using M3trik.Unitytk.Steps;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// What a step's page on the task's placard says and offers, as its rows fill it: its text (a row's quote,
    /// else the sheet's placard column; on a page made for its buttons alone, or every page where the task asks for
    /// one each, <see cref="TaskSettings.pages"/>, its voice line), a button per label a row offers
    /// (<see cref="TaskBehaviorAttribute.Offers"/>), what choosing a label says where it does not go on, and
    /// whether it shows the task's number keys. A row's behavior fills it through <see cref="ITaskPageContent"/>.
    /// </summary>
    public sealed class TaskPage
    {
        readonly List<string> _choices = new List<string>();
        readonly Dictionary<string, string> _replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal TaskPage(string text)
        {
            Text = text;
        }

        /// <summary>What the page says (its placard text: a row's quote, else the sheet's placard column).</summary>
        public string Text { get; set; }

        /// <summary>Whether the page shows the task's number keys (<see cref="TaskSettings.numberKeys"/>).</summary>
        public bool Keys { get; set; }

        /// <summary>
        /// Whether a row of the step puts something on the placard besides its page (a list, a tick, what a part
        /// selected without its tool says): the task gets its placard though no step has a page.
        /// </summary>
        public bool OnPlacard { get; set; }

        /// <summary>The labels the page offers, a button each, in the order offered.</summary>
        public IReadOnlyList<string> Choices => _choices;

        /// <summary>What choosing a label says, by label, where choosing it does not go on (a wrong answer).</summary>
        public IReadOnlyDictionary<string, string> Replies => _replies;

        /// <summary>Whether the page has something of its own to show: its text, a button, the key pad or a reply.</summary>
        public bool Shows => !string.IsNullOrEmpty(Text) || _choices.Count > 0 || Keys || _replies.Count > 0;

        /// <summary>Offer <paramref name="label"/> as a button; a label offered already (in any case) is offered once.</summary>
        /// <param name="label">The button's label.</param>
        public void Offer(string label)
        {
            if (string.IsNullOrEmpty(label)) return;
            if (!_choices.Exists(c => string.Equals(c, label, StringComparison.OrdinalIgnoreCase))) _choices.Add(label);
        }

        /// <summary>Have choosing <paramref name="label"/> say <paramref name="reply"/>, the page staying.</summary>
        /// <param name="label">The button's label.</param>
        /// <param name="reply">What it says.</param>
        public void Reply(string label, string reply)
        {
            if (!string.IsNullOrEmpty(label)) _replies[label] = reply;
        }

        // Whether the task gets a placard: a page for every step asked for, or a step with a page of its own or a
        // row putting something else on the placard.
        internal static bool PlacardNeeded(SheetPlan.Shape.Payload plan, ManifestScene scene)
        {
            if (plan == null || plan.steps == null || plan.steps.Count == 0) return false;
            if (scene.Settings<TaskSettings>().pages == PlacardPages.EveryStep) return true;
            foreach (var step in plan.steps)
            {
                TaskPage page = Of(step, scene);
                if (page.Shows || page.OnPlacard) return true;
            }
            return false;
        }

        // The task's placard (named placard): a page per step with something to show (every step, where the
        // task asks), filled by its rows, shown while it runs, its Next held back where the step asks the user to
        // act, and placed clear of what the step shows.
        internal static void BuildAll(SheetPlan.Shape.Payload plan, string placard, ManifestScene scene, OpContext ctx)
        {
            if (!PlacardNeeded(plan, scene)) return;
            TaskSettings settings = scene.Settings<TaskSettings>();
            bool every = settings.pages == PlacardPages.EveryStep;
            Placard shownOn = SceneRoots.Find<Placard>(placard);
            foreach (var step in plan.steps)
            {
                if (!scene.Builds(step.id)) continue;
                TaskPage filled = Of(step, scene);
                if (!every && !filled.Shows) continue;  // nothing of its own to show: no page
                using System.IDisposable own = scene.For(step.id);  // the page is its step's
                string title = string.IsNullOrEmpty(step.name) ? step.id : step.name;
                string text = string.IsNullOrEmpty(filled.Text) ? step.Field(TaskKit.Fields.Voice) : filled.Text;
                PlacardPage page = PanelsActions.AddPage(placard, step.id, title, text, filled._choices, ctx: ctx);
                if (filled.Keys) PanelsActions.SetKeys(placard, step.id, settings.numberKeys, ctx);
                HoldNextBack(step, title, placard, scene, ctx);
                foreach (Transform shown in Shown(step, scene)) PanelsActions.KeepClear(placard, step.id, shown, ctx);
                Spots.Panel(scene, step, placard, shownOn, Shown(step, scene), ctx);
                foreach (var reply in filled._replies) PanelsActions.AddReply(placard, step.id, reply.Key, reply.Value, ctx);
                if (page != null) scene.EnableDuring(step.id, page.gameObject, page, ctx);
                else scene.EnableDuring<PlacardPage>(step.id, PanelsActions.PlannedPage(placard, step.id), ctx);
            }
            PanelsActions.SetUpInput(ctx);
        }

        // Step's page as its rows fill it.
        static TaskPage Of(SheetPlan.Shape.PlanStep step, ManifestScene scene)
        {
            var page = new TaskPage(step.Field(TaskKit.Fields.Placard));
            if (step.actions == null) return page;
            foreach (var action in step.actions)
            {
                if (ManifestBehaviors.Find(action.behavior) is ITaskPageContent content) content.Fill(action, step, page, scene);
                if (TaskBehaviors.Traits(action.behavior).Offers) page.Offer(action.Field(TaskKit.Fields.Label));
            }
            return page;
        }

        // A step that asks the user to act (take a tool, select or use a part) and also holds on a gate's button
        // (IStepGate, as it finishes): its button waits for the step to complete, shown then, so the user never goes
        // on without the tool a later step needs. Only an acting row the run builds (assessed ready) makes the step
        // wait: one held back (a node the scene lacks) gives it nothing to wait for, as a dry run and its live run know alike.
        static void HoldNextBack(SheetPlan.Shape.PlanStep step, string title, string placard, ManifestScene scene, OpContext ctx)
        {
            if (step.actions == null) return;
            ManifestAssessment.Step assessed = scene.Assessment.steps.Find(s => s.id == step.id);
            bool acts = false;
            for (int i = 0; assessed != null && i < step.actions.Count && i < assessed.rows.Count; i++)
                acts |= step.actions[i] != null && assessed.rows[i].state == ManifestAssessment.State.Ready
                        && TaskBehaviors.Traits(step.actions[i].behavior).ActsOn;
            if (!acts) return;
            Steps.Step made = StepsActions.AddStep(scene.Sequence(), step.id, title, ctx);
            foreach (var action in step.actions)
            {
                string label = action == null ? null : action.Field(TaskKit.Fields.Label);
                IStepGate gate = action == null ? null : TaskBehaviors.Gate(action.behavior);
                if (gate == null || string.IsNullOrEmpty(label)) continue;
                if (gate.AtStart(action, step, scene)) continue;  // its button starts the step: offered at once
                StepsActions.WaitToOffer(scene.Sequence(), step.id, label, ctx);
                PanelsActions.HoldBack(placard, step.id, label, ctx);
                if (made != null) PanelsActions.RevealOn(placard, step.id, made.gameObject, made, made.onComplete, ctx);
                else PanelsActions.RevealOn(placard, step.id, StepsActions.PlannedStep(scene.Sequence(), step.id), ctx);
            }
        }

        // What step's rows show the user or ask them to act on (every node a row names but a place,
        // and the tools its rows use), each once: what its page keeps clear of.
        static IEnumerable<Transform> Shown(SheetPlan.Shape.PlanStep step, ManifestScene scene)
        {
            var seen = new HashSet<Transform>();
            if (step.actions == null) yield break;
            foreach (var action in step.actions)
            {
                if (TaskBehaviors.Traits(action.behavior).Places) continue;
                var paths = new List<string>();
                if (action.targets != null) paths.AddRange(action.targets);
                if (!string.IsNullOrEmpty(action.Field(TaskKit.Fields.Tool))) paths.Add(action.Field(TaskKit.Fields.Tool));
                foreach (string path in paths)
                {
                    Transform node = scene.Node(path);
                    if (node != null && seen.Add(node)) yield return node;
                }
            }
        }
    }
}
