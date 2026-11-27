using System;
using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Contracts
{
    /// <summary>
    /// How something that plays a sequence of steps by itself (an autopilot) acts on a step's parts as a person would,
    /// and asks what still plays: the part that knows the scene's parts (a target taken, pressed or selected; a clip
    /// let play). A component beside the player that is one is read off it (<c>GetComponent&lt;IStepActor&gt;()</c>), or
    /// code sets one; with none, the player acts on no part.
    /// </summary>
    /// <remarks>
    /// Prior art: a recorded input replay acts as one person did, so it breaks as the scene changes; the feature that
    /// owns a part knows how a person acts on it, and this contract lets a player of steps ask it without naming it.
    /// </remarks>
    public interface IStepActor
    {
        /// <summary>Whether it acts on <paramref name="part"/>, a component the running step enables.</summary>
        /// <param name="part">One of the parts the step switches on, switched on.</param>
        bool Acts(Behaviour part);

        /// <summary>Act on <paramref name="part"/> as a person would (take what it needs into the hand, press or select it).</summary>
        /// <param name="part">A part it acts on (<see cref="Acts"/>).</param>
        /// <param name="held">Whether it holds the part pressed now: the player lets go of it on its next move (<see cref="Release"/>).</param>
        /// <returns>What it did, a line for the player's log.</returns>
        string Act(Behaviour part, out bool held);

        /// <summary>Let go of <paramref name="part"/>, held pressed since <see cref="Act"/>.</summary>
        /// <param name="part">The part held.</param>
        /// <returns>What it did, a line for the log.</returns>
        string Release(Behaviour part);

        /// <summary>
        /// Whether something the steps started still plays (a clip, a press held its time), which the walk waits for;
        /// with <paramref name="finish"/>, it plays it through at once instead, and that is the move.
        /// </summary>
        /// <param name="finish">Play what plays to its end now (a fast walk).</param>
        /// <param name="note">What it did, a line for the log; null where it did nothing to say.</param>
        /// <returns>Whether the walk spends this move on it (something plays, or was just played to its end).</returns>
        bool Busy(bool finish, out string note);
    }

    /// <summary>
    /// The step actor's stand-in: one a test sets up as the parts it acts on (<see cref="ActsOn"/>), the ones it holds
    /// pressed (<see cref="Holds"/>) and what still plays (<see cref="Playing"/>), which keeps every part it acted on and let
    /// go of. It acts on no part until told.
    /// </summary>
    public sealed class PureStepActor : IStepActor
    {
        /// <summary>Which parts it acts on; default: none.</summary>
        public Func<Behaviour, bool> ActsOn { get; set; } = part => false;

        /// <summary>The parts an act holds pressed (let go on the player's next move); the rest it selects.</summary>
        public HashSet<Behaviour> Holds { get; } = new HashSet<Behaviour>();

        /// <summary>How many things the steps started still play: each <see cref="Busy"/> asked to finish plays them all.</summary>
        public int Playing { get; set; }

        /// <summary>Every part it acted on, in order.</summary>
        public List<Behaviour> Acted { get; } = new List<Behaviour>();

        /// <summary>Every part it let go of, in order.</summary>
        public List<Behaviour> Released { get; } = new List<Behaviour>();

        /// <inheritdoc/>
        public bool Acts(Behaviour part) => part != null && ActsOn(part);

        /// <inheritdoc/>
        public string Act(Behaviour part, out bool held)
        {
            Acted.Add(part);
            held = Holds.Contains(part);
            return (held ? "pressed " : "selected ") + part.name;
        }

        /// <inheritdoc/>
        public string Release(Behaviour part)
        {
            Released.Add(part);
            return "let go of " + part.name;
        }

        /// <inheritdoc/>
        public bool Busy(bool finish, out string note)
        {
            note = null;
            if (Playing <= 0) return false;
            if (!finish) return true;
            Playing = 0;
            note = "played what plays to its end";
            return true;
        }
    }
}
