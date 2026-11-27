using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Steps
{
    /// <summary>
    /// One step of a <see cref="Sequence"/>: complete once every signal it awaits has arrived
    /// (a clip's name when it ends, a selection's), it goes on to the next step by itself
    /// (<see cref="autoAdvance"/>) or waits for a choice (Next, an answer). While it runs, the
    /// components it lists are enabled (its targets), and disabled again after; the ones it
    /// lists to end with are enabled as it ends (a tool sent back, a fade); the ones it turns on
    /// stay on after it (a marker showing where to go next), until a later step turns them off.
    /// </summary>
    [AddComponentMenu("unitytk/Steps/Step")]
    [DisallowMultipleComponent]
    public sealed class Step : MonoBehaviour
    {
        [Tooltip("The step's ID: what a choice goes to.")]
        public string id = "";

        [Tooltip("The step's name.")]
        public string title = "";

        [Tooltip("What it was built from: a mark its builder keeps (the rows it wired). A build that finds another wires the step anew.")]
        public string source = "";

        [Tooltip("The signals that complete it, all of them; none: complete as it starts.")]
        public List<string> awaits = new List<string>();

        [Tooltip("Go on to the next step once complete; off: wait for a choice.")]
        public bool autoAdvance;

        [Tooltip("Going on by itself, the seconds it runs at least (time to read or hear what it says).")]
        public float dwell;

        [Tooltip("The choices it offers (Next, YES, NO...), each going to a step, or on to the next where none is named.")]
        public List<StepChoice> choices = new List<StepChoice>();

        [Tooltip("Enabled while the step runs, disabled otherwise (the things it lets a user act on).")]
        public List<Behaviour> activates = new List<Behaviour>();

        [Tooltip("Enabled as the step ends, however it ends (a tool sent back to its rest, a fade): disabled until then.")]
        public List<Behaviour> ends = new List<Behaviour>();

        [Tooltip("Enabled as the step starts and left on after it (a marker showing where to go next): disabled until then.")]
        public List<Behaviour> turnsOn = new List<Behaviour>();

        [Tooltip("Disabled as the step starts (what an earlier step turned on).")]
        public List<Behaviour> turnsOff = new List<Behaviour>();

        [Tooltip("The step started.")]
        public UnityEvent onEnter = new UnityEvent();

        [Tooltip("The step ended.")]
        public UnityEvent onExit = new UnityEvent();

        [Tooltip("Every awaited signal arrived: once each time it runs, as it starts where it awaits none.")]
        public UnityEvent onComplete = new UnityEvent();

        readonly HashSet<string> _received = new HashSet<string>();
        bool _completed;  // onComplete raised since it started

        /// <summary>Whether every awaited signal has arrived since the step started.</summary>
        public bool IsComplete
        {
            get
            {
                foreach (string signal in awaits)
                    if (!_received.Contains(signal)) return false;
                return true;
            }
        }

        /// <summary>The choice labelled <paramref name="label"/> (ignoring case); null when it offers none.</summary>
        /// <param name="label">A choice's label.</param>
        public StepChoice Choice(string label)
        {
            foreach (StepChoice choice in choices)
                if (choice != null && string.Equals(choice.label, label, System.StringComparison.OrdinalIgnoreCase)) return choice;
            return null;
        }

        /// <summary>
        /// The choice <paramref name="label"/> takes: the one labelled so, else the one taken for any
        /// other label (<see cref="StepChoice.otherwise"/>); null when neither is offered.
        /// </summary>
        /// <param name="label">What was chosen (a button's label, an entered value).</param>
        public StepChoice ChoiceFor(string label)
        {
            StepChoice named = Choice(label);
            if (named != null) return named;
            foreach (StepChoice choice in choices)
                if (choice != null && choice.otherwise) return choice;
            return null;
        }

        internal void Enter()
        {
            _received.Clear();
            _completed = false;
            Elapsed = 0f;
            // Disabled until it ends, each time it runs: a step run again (a choice going back,
            // a viewpoint a later step reuses) ends anew.
            foreach (Behaviour behaviour in ends)
                if (behaviour != null) behaviour.enabled = false;
            foreach (Behaviour behaviour in turnsOff)
                if (behaviour != null) behaviour.enabled = false;
            foreach (Behaviour behaviour in turnsOn)
                if (behaviour != null) behaviour.enabled = true;
            // Held at its start where a choice starts it (read first): its targets come on as that is taken.
            Started = true;
            foreach (StepChoice choice in choices)
                if (choice != null && choice.Starts) Started = false;
            Activate(Started);
            onEnter.Invoke();
        }

        /// <summary>Whether its targets are on: from its start, unless a choice starts it (<see cref="ChoiceOutcome.Start"/>), then once that is taken.</summary>
        public bool Started { get; private set; }

        // Begin the actions of a step held at its start; whether it was held. (Not "Start": Unity calls a method of
        // that name on every component as the scene starts, which switched on every step whose call came after the
        // sequence's own start.)
        internal bool BeginActions()
        {
            if (Started) return false;
            Started = true;
            Activate(true);
            return true;
        }

        internal void Exit()
        {
            Activate(false);
            foreach (Behaviour behaviour in ends)
                if (behaviour != null) behaviour.enabled = true;
            onExit.Invoke();
        }

        // Whether signal completed the step just now.
        internal bool Receive(string signal)
        {
            if (IsComplete || !awaits.Contains(signal) || !_received.Add(signal)) return false;
            if (!IsComplete) return false;
            Complete();
            return true;
        }

        // Raise onComplete, once a run: as its last awaited signal arrives, or, awaiting none, as it starts.
        internal void Complete()
        {
            if (_completed) return;
            _completed = true;
            onComplete.Invoke();
        }

        /// <summary>Seconds since the step started (the sequence's clock: <see cref="Sequence.Tick"/>).</summary>
        public float Elapsed { get; internal set; }

        /// <summary>Whether it goes on by itself now: complete, going on by itself, and run its <see cref="dwell"/>.</summary>
        public bool GoesOn => Started && autoAdvance && IsComplete && Elapsed >= dwell;

        internal void Activate(bool on)
        {
            foreach (Behaviour behaviour in activates)
                if (behaviour != null) behaviour.enabled = on;
        }

        // Before the sequence begins: nothing it turns on or enables as it ends has happened yet.
        internal void Rewind()
        {
            Activate(false);
            foreach (Behaviour behaviour in ends)
                if (behaviour != null) behaviour.enabled = false;
            foreach (Behaviour behaviour in turnsOn)
                if (behaviour != null) behaviour.enabled = false;
        }
    }
}
