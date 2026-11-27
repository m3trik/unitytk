using System;
using UnityEngine;

namespace M3trik.Unitytk.Steps
{
    /// <summary>What taking a <see cref="StepChoice"/> does.</summary>
    public enum ChoiceOutcome
    {
        /// <summary>Goes to its step (<see cref="StepChoice.to"/>), or on to the next where it names none.</summary>
        Next = 0,
        /// <summary>Finishes the sequence, wherever its step stands (an Exit).</summary>
        Finish = 1,
        /// <summary>Starts over: the scene is loaded again, so all the steps changed is undone (<see cref="Sequence.Restart"/>).</summary>
        Restart = 2,
        /// <summary>Starts its step rather than leaving it: the step's targets come on only then (read first, then go).</summary>
        Start = 3,
    }

    /// <summary>
    /// A choice a step offers: its label, and what taking it does (<see cref="outcome"/>: on to its step or the
    /// next, the end of the sequence, the start over, or its own step's start); one taken for any label no other
    /// choice of the step names (<see cref="otherwise"/>: any other answer). One that <see cref="waits"/> is taken
    /// only once its step is complete.
    /// </summary>
    [Serializable]
    public sealed class StepChoice
    {
        [Tooltip("What the choice says (Next, YES, Try Again).")]
        public string label = "";

        [Tooltip("The step it goes to, by ID, where it goes on; empty: the next step.")]
        public string to = "";

        [Tooltip("What taking it does: go on (to its step, else the next), finish the sequence, start over, or start its own step.")]
        public ChoiceOutcome outcome = ChoiceOutcome.Next;

        [Tooltip("Taken for any label no other choice of the step names (any other answer).")]
        public bool otherwise;

        [Tooltip("Offered only once the step is complete (every signal it awaits arrived): a Next after a tool taken, a part selected.")]
        public bool waits;

        /// <summary>Whether taking it finishes the sequence (<see cref="ChoiceOutcome.Finish"/>).</summary>
        public bool Finishes => outcome == ChoiceOutcome.Finish;

        /// <summary>Whether taking it starts over (<see cref="ChoiceOutcome.Restart"/>).</summary>
        public bool Restarts => outcome == ChoiceOutcome.Restart;

        /// <summary>Whether taking it starts its own step (<see cref="ChoiceOutcome.Start"/>).</summary>
        public bool Starts => outcome == ChoiceOutcome.Start;
    }
}
