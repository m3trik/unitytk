using System;
using System.Collections.Generic;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Kernel;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Steps
{
    /// <summary>
    /// The Steps feature's actions: a <see cref="Sequence"/> made in the active scene, its
    /// <see cref="Step"/>s added in order, each step's awaited signals, choices and the
    /// components it enables set, and another part's event wired to signal the sequence.
    /// Each takes plain names (the sequence's, a step's ID), so a run of several plans in a
    /// dry run what it would make before it is made, and an optional
    /// <see cref="OpContext"/>: a dry run plans, a menu's run is one undo step, and none just
    /// runs; a second run changes nothing.
    /// </summary>
    /// <remarks>
    /// Prior art: as <see cref="Sequence"/>'s. Another feature's event reaches a sequence by
    /// a persistent call (<c>UnityEventTools</c>), the action taking the event as Unity's own
    /// type, so neither feature names the other.
    /// </remarks>
    public static class StepsActions
    {
        /// <summary>An action names a sequence or step that is not there.</summary>
        public const string MissingCode = "unity.steps.missing";

        /// <summary>
        /// The sequence <paramref name="name"/> in the active scene, made at its top level where
        /// there is none.
        /// </summary>
        /// <param name="name">The sequence's object name.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The sequence; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        public static Sequence CreateSequence(string name, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the sequence", nameof(name));
            Sequence existing = FindSequence(name);
            string key = SequenceKey(name);
            if (existing != null || ctx.Planned("create", key)) return existing;
            if (ctx != null && !ctx.ShouldCreate(key, "make the steps' sequence")) return null;
            var node = new GameObject(name);
            ctx.Created(node);
            return ctx.AddComponent<Sequence>(node);
        }

        /// <summary>
        /// Add the step <paramref name="id"/> to the end of <paramref name="sequence"/> (a child
        /// object named for its ID); one it has gets its title brought up to date.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="title">The step's name.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The step; null in a dry run that would make one, or where the sequence is missing.</returns>
        public static Step AddStep(string sequence, string id, string title, OpContext ctx = null)
        {
            if (!Owner(sequence, ctx, out Sequence owner)) return null;
            Step step = owner == null ? null : owner.Find(id);
            if (step == null && !ctx.Planned("create", StepKey(sequence, id)))
            {
                if (ctx != null && !ctx.ShouldCreate(StepKey(sequence, id), $"add the step '{title}'"))
                {
                    ctx.ShouldChange(sequence, $"list the step {id}");
                    return null;
                }
                var node = new GameObject(id);
                node.transform.SetParent(owner.transform, false);
                ctx.Created(node);
                step = ctx.AddComponent<Step>(node);
                step.id = id;
                step.title = title ?? "";
                ctx.Edited(step);
                if (ctx == null || ctx.ShouldChange(owner, $"list the step {id}"))
                {
                    owner.steps.Add(step);
                    ctx.Edited(owner);
                }
                return step;
            }
            if (step == null || step.title == (title ?? "")) return step;
            if (ctx != null && !ctx.ShouldChange(step, $"name it '{title}'")) return step;
            step.title = title ?? "";
            ctx.Edited(step);
            return step;
        }

        /// <summary>
        /// Keep what step <paramref name="id"/> is built from (<see cref="Step.source"/>: a mark its
        /// builder gives it, the rows it wires). A step built from something else has its wiring emptied
        /// first (what it awaits, offers, enables and switches, how it goes on), so the build that
        /// follows wires it anew and nothing of a row it no longer has stays; one built from the same
        /// is left as it is, and one with no mark yet only takes it. What its wiring switched stays in
        /// the scene, off: a part no step enables does nothing.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="source">What it is built from now.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether its wiring was emptied (or would be, in a dry run).</returns>
        public static bool BuiltFrom(string sequence, string id, string source, OpContext ctx = null)
        {
            source = source ?? "";
            if (!Owner(sequence, ctx, out Sequence owner)) return false;
            Step step = owner == null ? null : owner.Find(id);
            const string mark = "keep what it is built from";
            if (step == null)
            {
                if (!ctx.Planned("create", StepKey(sequence, id)))
                {
                    ctx.Raise(MissingCode, $"no step '{id}' in '{sequence}': add it first (StepsActions.AddStep)");
                    return false;
                }
                string planned = PlannedStep(sequence, id).Path;  // a dry run's: the step is planned
                if (!ctx.Planned("change", planned, mark)) ctx.ShouldChange(planned, mark);
                return false;
            }
            if (step.source == source) return false;
            bool anew = step.source.Length > 0;
            if (ctx != null && !ctx.ShouldChange(step, anew ? RewireAction : mark)) return anew;
            if (anew)
            {
                step.awaits.Clear();
                step.choices.Clear();
                step.activates.Clear();
                step.ends.Clear();
                step.turnsOn.Clear();
                step.turnsOff.Clear();
                step.autoAdvance = false;
                step.dwell = 0f;
            }
            step.source = source;
            ctx.Edited(step);
            return anew;
        }

        // What BuiltFrom asks to empty a step's wiring by: a dry run that planned it reads the step as empty after.
        const string RewireAction = "wire it anew: what it is built from changed";

        /// <summary>
        /// The step <see cref="AddStep"/> makes, as a dry run that only planned it hands it on: the object
        /// <paramref name="id"/> under the sequence <paramref name="sequence"/>, there now or only planned.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <exception cref="ArgumentException"><paramref name="sequence"/> or <paramref name="id"/> is empty.</exception>
        public static PlannedObject PlannedStep(string sequence, string id)
        {
            if (string.IsNullOrEmpty(sequence)) throw new ArgumentException("name the sequence", nameof(sequence));
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("name the step", nameof(id));
            return new PlannedObject(id, new PlannedObject(sequence));
        }

        /// <summary>Have step <paramref name="id"/> await <paramref name="signal"/> (all its signals complete it).</summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="signal">The signal (a clip's name, a selection's).</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool Await(string sequence, string id, string signal, OpContext ctx = null)
            => Edit(sequence, id, ctx, s => s != null && s.awaits.Contains(signal), s => s.awaits.Add(signal), $"await '{signal}'");

        /// <summary>Have step <paramref name="id"/> go on by itself once complete (<paramref name="on"/>), or wait for a choice.</summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="on">Go on by itself.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetAutoAdvance(string sequence, string id, bool on, OpContext ctx = null)
            => Edit(sequence, id, ctx, s => (s != null && s.autoAdvance) == on, s => s.autoAdvance = on, on ? "go on by itself" : "wait for a choice");

        /// <summary>Have step <paramref name="id"/>, going on by itself, run at least <paramref name="seconds"/> (<see cref="Step.dwell"/>).</summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="seconds">The seconds it runs at least; 0: it goes on as soon as it is complete.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetDwell(string sequence, string id, float seconds, OpContext ctx = null)
            => Edit(sequence, id, ctx, s => Mathf.Approximately(s != null ? s.dwell : 0f, seconds), s => s.dwell = seconds, $"run at least {seconds:0.#} s");

        /// <summary>
        /// Have step <paramref name="id"/> offer a choice labelled <paramref name="label"/> that starts
        /// over (<see cref="Sequence.Restart"/>: the scene loaded again); one it offers already is made to.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="label">What the choice says.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool AddRestart(string sequence, string id, string label, OpContext ctx = null)
            => Edit(sequence, id, ctx,
                    s => s != null && s.Choice(label) is StepChoice c && c.Restarts && string.IsNullOrEmpty(c.to),
                    s =>
                    {
                        StepChoice choice = Offer(s, label);
                        choice.outcome = ChoiceOutcome.Restart;
                        choice.to = "";
                    },
                    $"offer '{label}', starting over");

        /// <summary>Offer the choice <paramref name="label"/> on step <paramref name="id"/>, going to <paramref name="to"/> (empty: the next step).</summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="label">What the choice says.</param>
        /// <param name="to">The step it goes to; null or empty: the next.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool AddChoice(string sequence, string id, string label, string to, OpContext ctx = null)
            => Edit(sequence, id, ctx,
                    s => s != null && s.Choice(label) is StepChoice c && c.to == (to ?? ""),
                    s => Offer(s, label).to = to ?? "",
                    $"offer '{label}'");

        /// <summary>
        /// Have step <paramref name="id"/>'s choice labelled <paramref name="label"/> start the step rather than
        /// leave it (<paramref name="starts"/>, <see cref="ChoiceOutcome.Start"/>: read first, its targets on once
        /// taken), or go on from it. One so already gets nothing; one the step does not offer yet is added.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="label">The choice's label.</param>
        /// <param name="starts">It starts the step.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetStarts(string sequence, string id, string label, bool starts, OpContext ctx = null)
            => Edit(sequence, id, ctx,
                    s => (s != null ? s.Choice(label) : null) is StepChoice c ? c.Starts == starts : !starts,
                    s =>
                    {
                        StepChoice choice = Offer(s, label);
                        if (starts) choice.outcome = ChoiceOutcome.Start;
                        else if (choice.Starts) choice.outcome = ChoiceOutcome.Next;
                    },
                    starts ? $"start the step on '{label}'" : $"go on from the step on '{label}'");

        /// <summary>
        /// Have step <paramref name="id"/>'s choice labelled <paramref name="label"/> wait for the
        /// step to complete (<see cref="StepChoice.waits"/>): a Next offered once the step's tool is
        /// taken and its part selected. One that waits already gets nothing; one the step does not
        /// offer yet is added, waiting.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="label">The choice's label.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool WaitToOffer(string sequence, string id, string label, OpContext ctx = null)
            => Edit(sequence, id, ctx,
                    s => s != null && s.Choice(label) is StepChoice c && c.waits,
                    s => Offer(s, label).waits = true,
                    $"offer '{label}' once the step is complete");

        /// <summary>
        /// Have step <paramref name="id"/> offer a choice labelled <paramref name="label"/> that
        /// finishes the sequence, wherever the step stands (an Exit); one it offers already
        /// is made to finish.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="label">What the choice says.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool AddExit(string sequence, string id, string label, OpContext ctx = null)
            => Edit(sequence, id, ctx,
                    s => s != null && s.Choice(label) is StepChoice c && c.Finishes,
                    s =>
                    {
                        StepChoice choice = Offer(s, label);
                        choice.to = "";
                        choice.outcome = ChoiceOutcome.Finish;
                    },
                    $"offer '{label}', which finishes it");

        /// <summary>
        /// Have step <paramref name="id"/> offer a choice taken for any label no other choice of it
        /// names (any other answer, <see cref="StepChoice.otherwise"/>), going to <paramref name="to"/>
        /// (empty: the next step); one it offers already is pointed there.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="to">The step it goes to; null or empty: the next.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool AddOtherwise(string sequence, string id, string to, OpContext ctx = null)
            => Edit(sequence, id, ctx,
                    s => s != null && Otherwise(s) is StepChoice c && c.to == (to ?? ""),
                    s =>
                    {
                        StepChoice choice = Otherwise(s);
                        if (choice == null) s.choices.Add(choice = new StepChoice { label = OtherwiseLabel, otherwise = true });
                        choice.to = to ?? "";
                    },
                    "offer a choice for any other answer");

        /// <summary>The label <see cref="AddOtherwise"/> gives its choice, as the inspector shows it.</summary>
        public const string OtherwiseLabel = "any other";

        /// <summary>
        /// Have step <paramref name="id"/> enable <paramref name="behaviour"/> (on <paramref name="node"/>) as it starts,
        /// and leave it on after (a marker showing where to go next); the sequence keeps it disabled until then.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="node">The object it sits on.</param>
        /// <param name="behaviour">The component; null only in a dry run where it is planned.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <typeparam name="T">The component's type: what the plan names when it is only planned.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool EnableFrom<T>(string sequence, string id, GameObject node, T behaviour, OpContext ctx = null) where T : Behaviour
            => Switch(sequence, id, NodePath(node), behaviour, ctx, s => s.turnsOn, FromFormat);

        /// <summary>
        /// <see cref="EnableFrom{T}(string, string, GameObject, T, OpContext)"/> on a <typeparamref name="T"/> of
        /// <paramref name="node"/>, an object a dry run only planned: the change its live run makes, planned once;
        /// nothing in a live run.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="node">The object it sits on, which a dry run only planned.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the change now.</returns>
        /// <typeparam name="T">The component's type, which the plan names.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool EnableFrom<T>(string sequence, string id, PlannedObject node, OpContext ctx = null) where T : Behaviour
            => SwitchPlanned<T>(sequence, id, node, ctx, s => s.turnsOn, FromFormat);

        /// <summary>
        /// Have step <paramref name="id"/> disable <paramref name="behaviour"/> (on <paramref name="node"/>) as it
        /// starts: what an earlier step turned on (<see cref="EnableFrom{T}(string, string, GameObject, T, OpContext)"/>).
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="node">The object it sits on.</param>
        /// <param name="behaviour">The component; null only in a dry run where it is planned.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <typeparam name="T">The component's type: what the plan names when it is only planned.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool DisableFrom<T>(string sequence, string id, GameObject node, T behaviour, OpContext ctx = null) where T : Behaviour
            => Switch(sequence, id, NodePath(node), behaviour, ctx, s => s.turnsOff, OffFormat);

        /// <summary>
        /// <see cref="DisableFrom{T}(string, string, GameObject, T, OpContext)"/> on a <typeparamref name="T"/> of
        /// <paramref name="node"/>, an object a dry run only planned: the change its live run makes, planned once;
        /// nothing in a live run.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="node">The object it sits on, which a dry run only planned.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the change now.</returns>
        /// <typeparam name="T">The component's type, which the plan names.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool DisableFrom<T>(string sequence, string id, PlannedObject node, OpContext ctx = null) where T : Behaviour
            => SwitchPlanned<T>(sequence, id, node, ctx, s => s.turnsOff, OffFormat);

        /// <summary>
        /// Have step <paramref name="id"/> enable <paramref name="behaviour"/> (on
        /// <paramref name="node"/>) while it runs; the sequence disables it otherwise.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="node">The object it sits on.</param>
        /// <param name="behaviour">The component; null only in a dry run where it is planned.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <typeparam name="T">The component's type: what the plan names when it is only planned.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool EnableDuring<T>(string sequence, string id, GameObject node, T behaviour, OpContext ctx = null) where T : Behaviour
            => Switch(sequence, id, NodePath(node), behaviour, ctx, s => s.activates, DuringFormat);

        /// <summary>
        /// <see cref="EnableDuring{T}(string, string, GameObject, T, OpContext)"/> on a <typeparamref name="T"/> of
        /// <paramref name="node"/>, an object a dry run only planned: the change its live run makes, planned once;
        /// nothing in a live run.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="node">The object it sits on, which a dry run only planned.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the change now.</returns>
        /// <typeparam name="T">The component's type, which the plan names.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool EnableDuring<T>(string sequence, string id, PlannedObject node, OpContext ctx = null) where T : Behaviour
            => SwitchPlanned<T>(sequence, id, node, ctx, s => s.activates, DuringFormat);

        /// <summary>
        /// Have step <paramref name="id"/> enable <paramref name="behaviour"/> (on
        /// <paramref name="node"/>) as it ends, however it ends; the sequence keeps it disabled
        /// until then.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="node">The object it sits on.</param>
        /// <param name="behaviour">The component; null only in a dry run where it is planned.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <typeparam name="T">The component's type: what the plan names when it is only planned.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool EnableAtEnd<T>(string sequence, string id, GameObject node, T behaviour, OpContext ctx = null) where T : Behaviour
            => Switch(sequence, id, NodePath(node), behaviour, ctx, s => s.ends, AtEndFormat);

        /// <summary>
        /// <see cref="EnableAtEnd{T}(string, string, GameObject, T, OpContext)"/> on a <typeparamref name="T"/> of
        /// <paramref name="node"/>, an object a dry run only planned: the change its live run makes, planned once;
        /// nothing in a live run.
        /// </summary>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="id">The step's ID.</param>
        /// <param name="node">The object it sits on, which a dry run only planned.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the change now.</returns>
        /// <typeparam name="T">The component's type, which the plan names.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool EnableAtEnd<T>(string sequence, string id, PlannedObject node, OpContext ctx = null) where T : Behaviour
            => SwitchPlanned<T>(sequence, id, node, ctx, s => s.ends, AtEndFormat);

        // What each switch says, of the object a part sits on (its path) and the part's type.
        const string FromFormat = "enable '{0}' ({1}) as it starts, left on after";
        const string OffFormat = "disable '{0}' ({1}) as it starts";
        const string DuringFormat = "enable '{0}' ({1}) while it runs";
        const string AtEndFormat = "enable '{0}' ({1}) as it ends";

        /// <summary>
        /// Wire <paramref name="when"/> (an event of <paramref name="owner"/>, on
        /// <paramref name="sender"/>) to signal <paramref name="signal"/> to
        /// <paramref name="sequence"/>; a wiring that is there gets nothing.
        /// </summary>
        /// <param name="sender">The scene object the event belongs to (its component's object, or the one the asset serves).</param>
        /// <param name="owner">The object holding the event (a component, an asset); null only in a dry run where it is planned.</param>
        /// <param name="when">The event (a target's selection); null as <paramref name="owner"/> may be.</param>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="signal">The signal it sends.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SignalOn(GameObject sender, UnityEngine.Object owner, UnityEventBase when, string sequence, string signal, OpContext ctx = null)
            => Wire(PathOf(sender), owner, when, sequence, $"signal '{signal}'", ctx, nameof(Sequence.Signal),
                    s => UnityEventTools.AddStringPersistentListener(when, s.Signal, signal), signal ?? "");

        /// <summary>
        /// Wire <paramref name="when"/>, an event carrying a name (a clip player's clip as it
        /// ends), to signal that name to <paramref name="sequence"/>; a wiring that is there gets
        /// nothing.
        /// </summary>
        /// <param name="sender">The scene object the event belongs to (its component's object, or the one the asset serves).</param>
        /// <param name="owner">The object holding the event (a component, an asset); null only in a dry run where it is planned.</param>
        /// <param name="when">The event; null as <paramref name="owner"/> may be.</param>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SignalOn(GameObject sender, UnityEngine.Object owner, UnityEvent<string> when, string sequence, OpContext ctx = null)
            => Wire(PathOf(sender), owner, when, sequence, "signal each name it carries", ctx, nameof(Sequence.Signal), s => UnityEventTools.AddPersistentListener(when, s.Signal));

        /// <summary>
        /// Wire <paramref name="when"/>, an event carrying a choice's label (a panel's button as
        /// it is pressed), to take that choice in <paramref name="sequence"/>'s current step
        /// (<see cref="Sequence.Pick"/>); a wiring that is there gets nothing.
        /// </summary>
        /// <param name="sender">The scene object the event belongs to (its component's object, or the one the asset serves).</param>
        /// <param name="owner">The object holding the event (a component, an asset); null only in a dry run where it is planned.</param>
        /// <param name="when">The event; null as <paramref name="owner"/> may be.</param>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool ChooseOn(GameObject sender, UnityEngine.Object owner, UnityEvent<string> when, string sequence, OpContext ctx = null)
            => Wire(PathOf(sender), owner, when, sequence, ChooseAction, ctx, nameof(Sequence.Pick), s => UnityEventTools.AddPersistentListener(when, s.Pick));

        /// <summary>
        /// <see cref="ChooseOn(GameObject, UnityEngine.Object, UnityEvent{string}, string, OpContext)"/> on an event of
        /// <paramref name="sender"/>, an object a dry run only planned (a placard the same build makes): the wiring its live
        /// run makes, planned once; nothing in a live run.
        /// </summary>
        /// <param name="sender">The planned object the event belongs to.</param>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the wiring now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sender"/> is null.</exception>
        public static bool ChooseOn(PlannedObject sender, string sequence, OpContext ctx = null)
        {
            if (sender == null) throw new ArgumentNullException(nameof(sender), "name the object whose event signals");
            return ctx != null && ctx.DryRun && Wire(sender.Path, null, null, sequence, ChooseAction, ctx, nameof(Sequence.Pick), null);
        }

        const string ChooseAction = "choose each label it carries";

        /// <summary>
        /// Put a <see cref="SequenceAutopilot"/> on <paramref name="node"/>, walking the sequence named
        /// <paramref name="sequence"/>, switched off unless <paramref name="startEnabled"/> (a debug panel's Autopilot
        /// switches it on); one there is pointed at the sequence where it walks another. How it acts on the steps' parts
        /// is an <see cref="IStepActor"/> beside it, which the part composing the scene's features adds.
        /// </summary>
        /// <param name="node">The object it goes on, a scene object.</param>
        /// <param name="sequence">The sequence's name; one a dry run only planned leaves it to the scene's first.</param>
        /// <param name="startEnabled">Make it switched on: it walks the sequence as the scene starts.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The autopilot; null in a dry run that would add it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static SequenceAutopilot AddAutopilot(GameObject node, string sequence, bool startEnabled = false, OpContext ctx = null)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name the object it goes on");
            Sequence walked = FindSequence(sequence);
            SequenceAutopilot pilot = node.GetComponent<SequenceAutopilot>();
            if (pilot == null)
            {
                string key = OpContextEdits.Key(node, typeof(SequenceAutopilot));
                if (ctx.Planned("create", key)) return null;
                if (ctx != null && !ctx.ShouldCreate(key, AutopilotAction)) return null;
                pilot = ctx.AddComponent<SequenceAutopilot>(node);
                pilot.sequence = walked;
                pilot.enabled = startEnabled;
                ctx.Edited(pilot);
                return pilot;
            }
            if (walked == null || pilot.sequence == walked) return pilot;
            if (ctx != null && !ctx.ShouldChange(pilot, $"walk '{sequence}'")) return pilot;
            pilot.sequence = walked;
            ctx.Edited(pilot);
            return pilot;
        }

        /// <summary>
        /// <see cref="AddAutopilot(GameObject, string, bool, OpContext)"/> on <paramref name="node"/>, an object a dry run
        /// only planned: the autopilot its live run adds, planned once; nothing in a live run.
        /// </summary>
        /// <param name="node">The planned object.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to put it on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static SequenceAutopilot AddAutopilot(PlannedObject node, OpContext ctx = null)
        {
            ctx.PlanCreate(OpContextEdits.Key(node, typeof(SequenceAutopilot)), AutopilotAction);
            return null;
        }

        const string AutopilotAction = "an autopilot that plays the sequence through by itself";

        /// <summary>
        /// Put a sequence's debug commands on <paramref name="node"/> (<see cref="SequenceDebugCommands"/>: its status,
        /// Skip, Back, Restart and Autopilot) for the sequence named <paramref name="sequence"/>, over an autopilot there,
        /// off (<see cref="AddAutopilot(GameObject, string, bool, OpContext)"/>). They go on whatever command surface stands
        /// beside them (<see cref="ICommandSurface"/>: a debug panel, through the part composing the scene's features). Ones
        /// there are left as they are.
        /// </summary>
        /// <param name="node">The object they go on, a scene object (a debug panel's).</param>
        /// <param name="sequence">The sequence's name.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The commands; null in a dry run that would add them.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static SequenceDebugCommands AddDebugCommands(GameObject node, string sequence, OpContext ctx = null)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name the object they go on");
            SequenceAutopilot pilot = AddAutopilot(node, sequence, false, ctx);
            if (node.TryGetComponent(out SequenceDebugCommands found)) return found;
            string key = OpContextEdits.Key(node, typeof(SequenceDebugCommands));
            if (ctx.Planned("create", key)) return null;
            if (ctx != null && !ctx.ShouldCreate(key, CommandsAction)) return null;
            SequenceDebugCommands made = ctx.AddComponent<SequenceDebugCommands>(node);
            made.sequence = FindSequence(sequence);
            made.autopilot = pilot;
            ctx.Edited(made);
            return made;
        }

        /// <summary>
        /// <see cref="AddDebugCommands(GameObject, string, OpContext)"/> on <paramref name="node"/>, an object a dry run
        /// only planned: the autopilot and the commands its live run adds, planned once each; nothing in a live run.
        /// </summary>
        /// <param name="node">The planned object.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to put them on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static SequenceDebugCommands AddDebugCommands(PlannedObject node, OpContext ctx = null)
        {
            AddAutopilot(node, ctx);
            ctx.PlanCreate(OpContextEdits.Key(node, typeof(SequenceDebugCommands)), CommandsAction);
            return null;
        }

        const string CommandsAction = "the sequence's debug commands: its status, Skip, Back, Restart and Autopilot";

        // -- helpers ---------------------------------------------------------------

        // The choice of step labelled label, added where it offers none.
        static StepChoice Offer(Step step, string label)
        {
            StepChoice choice = step.Choice(label);
            if (choice == null) step.choices.Add(choice = new StepChoice { label = label });
            return choice;
        }

        static StepChoice Otherwise(Step step)
        {
            foreach (StepChoice choice in step.choices)
                if (choice != null && choice.otherwise) return choice;
            return null;
        }

        // Add behaviour to the list of step id that list() names; a dry run's planned component counts as lacking it.
        static bool Switch<T>(string sequence, string id, string node, T behaviour, OpContext ctx,
                              Func<Step, System.Collections.Generic.List<Behaviour>> list, string format) where T : Behaviour
        {
            if (behaviour == null && (ctx == null || !ctx.DryRun))
            {
                ctx.Raise(MissingCode, $"'{node}' has no component for step {id} to {string.Format(format, node, typeof(T).Name)}: make it first");
                return false;
            }
            string what = string.Format(format, node, typeof(T).Name);
            return Edit(sequence, id, ctx, s => behaviour != null && s != null && list(s).Contains(behaviour), s => list(s).Add(behaviour), what);
        }

        // Switch for a T of node, an object a dry run only planned: nothing in a live run.
        static bool SwitchPlanned<T>(string sequence, string id, PlannedObject node, OpContext ctx,
                                     Func<Step, System.Collections.Generic.List<Behaviour>> list, string format) where T : Behaviour
        {
            string path = NodePath(node);
            return ctx != null && ctx.DryRun && Switch<T>(sequence, id, path, null, ctx, list, format);
        }

        // What a plan calls the object a part sits on: its path, made or only planned.
        static string NodePath(GameObject node)
            => node != null ? OpContextEdits.Label(node) : throw new ArgumentNullException(nameof(node), "name the object the part sits on");

        static string NodePath(PlannedObject node)
            => node != null ? node.Path : throw new ArgumentNullException(nameof(node), "name the object the part sits on");

        // What a plan calls the sequence named name, a top-level object.
        static string SequenceKey(string name) => OpContextEdits.Key(new PlannedObject(name), typeof(Sequence));

        // What a plan calls the step id of sequence.
        static string StepKey(string sequence, string id) => OpContextEdits.Key(PlannedStep(sequence, id), typeof(Step));

        static Sequence FindSequence(string name) => SceneRoots.Find<Sequence>(name);

        // The sequence named, or a dry run's planned one (null then); false, raised, when neither.
        static bool Owner(string sequence, OpContext ctx, out Sequence owner)
        {
            owner = FindSequence(sequence);
            if (owner != null || ctx.Planned("create", SequenceKey(sequence))) return true;
            ctx.Raise(MissingCode, $"no sequence '{sequence}' in the active scene: make it first (StepsActions.CreateSequence)");
            return false;
        }

        // Change step id where has() says it lacks the change. has(null) reads an empty step: one a dry run only
        // planned, or planned to wire anew (BuiltFrom), which its live run finds empty.
        static bool Edit(string sequence, string id, OpContext ctx, Func<Step, bool> has, Action<Step> make, string what)
        {
            if (!Owner(sequence, ctx, out Sequence owner)) return false;
            Step step = owner == null ? null : owner.Find(id);
            if (step == null && !ctx.Planned("create", StepKey(sequence, id)))
            {
                ctx.Raise(MissingCode, $"no step '{id}' in '{sequence}': add it first (StepsActions.AddStep)");
                return false;
            }
            object asked = step != null ? (object)step : PlannedStep(sequence, id).Path;
            if (has(ctx.Planned("change", asked, RewireAction) ? null : step)) return false;
            if (ctx.Planned("change", asked, what)) return false;
            if (ctx != null && !ctx.ShouldChange(asked, what)) return true;
            make(step);
            ctx.Edited(step);
            return true;
        }

        static string PathOf(GameObject sender)
            => sender != null ? OpContextEdits.Label(sender) : throw new ArgumentNullException(nameof(sender), "name the object whose event signals");

        // Wire when to call method on sequence (add), unless a call of it is there already (passing argument, where given).
        static bool Wire(string sender, UnityEngine.Object owner, UnityEventBase when, string sequence, string what, OpContext ctx,
                         string method, Action<Sequence> add, string argument = null)
        {
            if (string.IsNullOrEmpty(sender)) throw new ArgumentNullException(nameof(sender), "name the object whose event signals");
            if (!Owner(sequence, ctx, out Sequence target)) return false;
            if ((owner == null || when == null) && (ctx == null || !ctx.DryRun))
            {
                ctx.Raise(MissingCode, $"'{sender}' has no component holding the event: make it first");
                return false;
            }
            if (owner != null && target != null)
            {
                IReadOnlyList<PersistentCalls.Call> calls = PersistentCalls.Of(owner, when, ctx);
                if (calls == null) return false;  // its calls cannot be read: raised, and nothing wired twice
                foreach (PersistentCalls.Call call in calls)
                    if (call.Calls(target, method) && (argument == null || call.Argument as string == argument)) return false;
            }
            string action = $"{what} to '{sequence}'";
            object asked = owner != null ? (object)owner : sender;
            if (ctx.Planned("change", asked, action)) return false;
            if (ctx != null && !ctx.ShouldChange(asked, action)) return true;
            add(target);
            ctx.Edited(owner);
            return true;
        }
    }
}
