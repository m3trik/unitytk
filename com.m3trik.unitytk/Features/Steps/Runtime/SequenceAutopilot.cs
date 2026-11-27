using System.Collections.Generic;
using M3trik.Unitytk.Contracts;
using UnityEngine;

namespace M3trik.Unitytk.Steps
{
    /// <summary>
    /// Plays a sequence through as a person would, move by move (<see cref="Tick"/>): each step's parts acted on in
    /// the order the step lists them (by its <see cref="Actor"/>: what a part requires taken into the hand first, a
    /// part pressed while it plays and let go once that ends, any other selected), what plays let play (or played to
    /// its end at once, <see cref="fastForward"/>), and a choice taken once the step is complete: one going forward it
    /// has not taken before, else one finishing the sequence, else any. A step nothing it can do completes within
    /// <see cref="patience"/> is noted <see cref="Stuck"/> and left for the next. It stops at the end, or as
    /// <see cref="stopAt"/> starts: a walk to a step, the scene standing as the steps before it left it.
    /// </summary>
    /// <remarks>
    /// Prior art: a recorded input replay (the Input System's <c>InputEventTrace</c>) replays what a person did, so it
    /// breaks as the scene changes, and needs a person to record it first; a sequence's own steps already say what
    /// completes each, so the walk reads them. Timeline or an animation resample poses a scene at a time, but a
    /// sequence's state is what its steps did (what the hand holds, which clips played), so starting at a step is a
    /// walk to it. How a part is acted on is the actor's (<see cref="IStepActor"/>), which a part that composes the
    /// scene's features provides; with none, the walk only takes choices.
    /// </remarks>
    [AddComponentMenu("unitytk/Steps/Sequence Autopilot")]
    [DisallowMultipleComponent]
    public sealed class SequenceAutopilot : MonoBehaviour
    {
        [Tooltip("The sequence it walks (the scene's first when empty).")]
        public Sequence sequence;

        [Tooltip("Play what plays to its end at once instead of letting it play (a walk to a step, a test).")]
        public bool fastForward;

        [Tooltip("The step it stops at as it starts (empty: it walks to the end).")]
        public string stopAt = "";

        [Tooltip("Seconds a step may go without a move before it is noted stuck and left for the next.")]
        [Min(0f)]
        public float patience = 30f;

        [Tooltip("Count its moves' time unscaled (a paused game's time scale of 0 still walks it); off: the game's time.")]
        public bool unscaledTime;

        readonly List<string> _log = new List<string>();
        readonly List<string> _stuck = new List<string>();
        readonly HashSet<Behaviour> _acted = new HashSet<Behaviour>();
        readonly HashSet<string> _taken = new HashSet<string>();
        IStepActor _actor;
        readonly SceneLookup<Sequence> _found = new SceneLookup<Sequence>();  // the scene's, while none is named
        Step _step;
        Behaviour _pressed;
        float _idle;
        float _seconds;

        /// <summary>What it did, a line a move (<c>A01: selected DOOR</c>).</summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary>The steps it found nothing to complete, by ID, in order.</summary>
        public IReadOnlyList<string> Stuck => _stuck;

        /// <summary>
        /// The sequence it walks: the one named, else the scene's first (looked up once, and again once it is gone; where
        /// there is none, again once it is switched on); null when there is none.
        /// </summary>
        public Sequence Sequence => sequence != null ? sequence : _found.Get();

        /// <summary>
        /// What acts on the steps' parts: the one set, else a component on its object that is one (looked up once, and
        /// again once it is gone); null where there is neither (the walk then only takes choices, and a step awaiting a
        /// part is left as stuck).
        /// </summary>
        public IStepActor Actor
        {
            get
            {
                if (_actor is Object unityActor ? unityActor == null : _actor == null) _actor = GetComponent<IStepActor>();
                return _actor;
            }
            set => _actor = value;
        }

        /// <summary>Whether the walk is over: the sequence finished, or <see cref="stopAt"/> reached.</summary>
        public bool Done
        {
            get
            {
                Sequence walked = Sequence;
                return walked == null || walked.Finished
                       || (stopAt.Length > 0 && walked.Current != null && walked.Current.id == stopAt);
            }
        }

        /// <summary>
        /// Make one move (<paramref name="seconds"/> after the last): begin the sequence where it has not
        /// begun, else act on the current step as the summary says.
        /// </summary>
        /// <param name="seconds">The time since the last move (what counts toward <see cref="patience"/>).</param>
        /// <returns>Whether the walk goes on.</returns>
        public bool Tick(float seconds)
        {
            _seconds = seconds;
            Sequence walked = Sequence;
            if (walked == null) return false;
            if (walked.Current == null && !walked.Finished)
            {
                walked.Begin();
                return !Done;
            }
            if (Done) return false;
            Step step = walked.Current;
            if (step != _step)
            {
                _step = step;
                _acted.Clear();
                _idle = 0f;
            }
            IStepActor actor = Actor;
            if (actor != null && actor.Busy(fastForward, out string played))
            {
                if (!string.IsNullOrEmpty(played)) Note(played);
                return !Done;
            }
            if (walked.Current != step) return !Done;
            if (!step.Started)
            {
                // Held at its start: read, then go on to its actions.
                foreach (StepChoice choice in step.choices)
                    if (choice != null && choice.Starts && walked.Choose(choice.label))
                    {
                        Note($"started it ({choice.label})");
                        return !Done;
                    }
            }
            if (_pressed != null)
            {
                Behaviour pressed = _pressed;
                _pressed = null;
                if (actor != null) Note(actor.Release(pressed));
                return !Done;
            }
            if (actor != null)
                foreach (Behaviour part in step.activates)
                    if (part != null && part.isActiveAndEnabled && actor.Acts(part) && _acted.Add(part))
                    {
                        Note(actor.Act(part, out bool held));
                        if (held) _pressed = part;
                        _idle = 0f;
                        return !Done;
                    }
            if (step.IsComplete)
            {
                Choose(walked, step);
                return !Done;
            }
            _idle += seconds;
            if (_idle >= patience || fastForward)
            {
                _stuck.Add(step.id);
                Note($"stuck: nothing completes it (it awaits {string.Join(", ", step.awaits)})");
                walked.Next();
            }
            return !Done;
        }

        /// <summary>Walk until it is done, or <paramref name="moves"/> moves are made (a loop's guard).</summary>
        /// <param name="moves">The most moves it makes.</param>
        /// <param name="seconds">The time each move counts.</param>
        /// <returns>Whether it is done.</returns>
        public bool Run(int moves = 10000, float seconds = 0.1f)
        {
            for (int i = 0; i < moves && Tick(seconds); i++) { }
            return Done;
        }

        void OnEnable() => _found.Forget();

        // At the step it was to stop at, it stops, so the person goes on from there.
        void Update()
        {
            if (!Tick(unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) && stopAt.Length > 0 && Done) enabled = false;
        }

        void Choose(Sequence walked, Step step)
        {
            StepChoice pick = null;
            int here = walked.steps.IndexOf(step);
            foreach (StepChoice choice in step.choices)
                if (choice != null && !choice.Finishes && !_taken.Contains(Key(step, choice)) && Forward(walked, here, choice))
                {
                    pick = choice;
                    break;
                }
            if (pick == null)
                foreach (StepChoice choice in step.choices)
                    if (choice != null && choice.Finishes)
                    {
                        pick = choice;
                        break;
                    }
            if (pick == null)
                foreach (StepChoice choice in step.choices)
                    if (choice != null && !_taken.Contains(Key(step, choice)))
                    {
                        pick = choice;
                        break;
                    }
            if (pick == null)
            {
                if (step.autoAdvance)
                {
                    // It goes on once it has run its dwell: at once, fast forward; else as time passes
                    // (a player's sequence counts it itself; a walk in the editor counts it here).
                    if (fastForward) walked.Tick(Mathf.Max(step.dwell - step.Elapsed, 0f));
                    else if (!Application.isPlaying) walked.Tick(_seconds);
                    return;
                }
                if (here + 1 < walked.steps.Count)
                {
                    _stuck.Add(step.id);
                    Note("stuck: complete, but it offers no choice and does not go on by itself");
                }
                walked.Next();
                return;
            }
            _taken.Add(Key(step, pick));
            Note($"chose '{pick.label}'");
            walked.Choose(pick.label);
        }

        static string Key(Step step, StepChoice choice) => step.id + "\n" + choice.label;

        static bool Forward(Sequence walked, int here, StepChoice choice)
        {
            if (choice.Restarts) return false;  // back to the start: taken only when nothing else is offered
            if (string.IsNullOrEmpty(choice.to)) return true;
            Step to = walked.Find(choice.to);
            return to != null && walked.steps.IndexOf(to) > here;
        }

        void Note(string what) => _log.Add($"{(_step != null ? _step.id : "-")}: {what}");
    }
}
