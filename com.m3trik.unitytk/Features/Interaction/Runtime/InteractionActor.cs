using System.Collections.Generic;
using M3trik.Unitytk.Contracts;
using UnityEngine;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// How a player of steps acts on this feature's parts (<see cref="IStepActor"/>): a <see cref="Target"/> the running
    /// step enables gets what it requires taken into the hand first, then is pressed where a trigger switched on plays on
    /// a press (<see cref="ClipTrigger.PlaysOnPress"/>; let go on the next move), else selected; a timed press is held
    /// its time first (<see cref="ClipTrigger.Timed"/>), and a <see cref="ClipPlayer"/> still playing is waited for, or,
    /// on a fast walk, each is played through at once.
    /// </summary>
    /// <remarks>
    /// Prior art: a recorded input replay acts as one person did, so it breaks as the scene changes; the feature that
    /// owns the parts knows how a person acts on them, and the contract lets a player of steps ask without naming it.
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Interaction Actor")]
    [DisallowMultipleComponent]
    public sealed class InteractionActor : MonoBehaviour, IStepActor
    {
        [Tooltip("The hand it acts with (the scene's switched-on hand when empty).")]
        public Hand hand;

        // The scene's clip players, found as it first asks after it is switched on: what it waits for, never
        // looked for again each frame.
        readonly List<ClipPlayer> _players = new List<ClipPlayer>();
        bool _found;
        readonly List<ClipTrigger> _timing = new List<ClipTrigger>();  // the timed presses it holds, short of their time


        /// <summary>The hand it acts with: the one named, else the scene's switched-on one; null when none.</summary>
        public Hand Hand => hand != null ? hand : Hand.InScene();

        void OnEnable()
        {
            _found = false;
            _timing.Clear();
        }

        /// <inheritdoc/>
        public bool Acts(Behaviour part) => part is Target;

        /// <inheritdoc/>
        public string Act(Behaviour part, out bool held)
        {
            held = false;
            if (!(part is Target target)) return $"{part.name}: no target to act on";
            Hand by = Hand;
            string took = "";
            if (target.requires != null && by != null && !by.Holds(target.requires))
            {
                by.Take(target.requires);
                took = $"took {target.requires.name} in hand; ";
            }
            if (PlaysOnPress(target) && target.Press(by))
            {
                held = true;
                foreach (ClipTrigger trigger in target.GetComponents<ClipTrigger>())
                    if (trigger.isActiveAndEnabled && trigger.Counting) _timing.Add(trigger);
                return $"{took}pressed {target.name}";
            }
            return target.Select(by) ? $"{took}selected {target.name}" : $"{took}selected {target.name}: refused";
        }

        /// <inheritdoc/>
        public string Release(Behaviour part)
        {
            if (part is Target target) target.Release();
            return $"let go of {part.name}";
        }

        /// <inheritdoc/>
        public bool Busy(bool finish, out string note)
        {
            note = null;
            // A timed press it holds plays once held its time: the walk waits while a player's frames count it on, or, on
            // a fast walk, it is held its time at once.
            for (int i = _timing.Count - 1; i >= 0; i--)
            {
                ClipTrigger trigger = _timing[i];
                if (trigger == null || !trigger.Counting)
                {
                    _timing.RemoveAt(i);
                    continue;
                }
                if (!finish) return true;
                _timing.RemoveAt(i);
                trigger.Tick(trigger.pressSeconds);
                note = $"held {trigger.name} pressed its {trigger.pressSeconds:0.##} s";
                return true;
            }
            if (!_found)
            {
                _players.Clear();
                _players.AddRange(FindObjectsByType<ClipPlayer>(FindObjectsSortMode.None));
                _found = true;
            }
            bool busy = false;
            foreach (ClipPlayer player in _players)
            {
                if (player == null || !player.Busy) continue;
                if (!finish)
                {
                    busy = true;
                    continue;
                }
                player.Finish();
                note = "played its clips to their end";
                return true;
            }
            return busy;
        }

        // Whether a press of the target plays what it plays now (held, or timed): a trigger switched on says so.
        static bool PlaysOnPress(Target target)
        {
            foreach (ClipTrigger trigger in target.GetComponents<ClipTrigger>())
                if (trigger.isActiveAndEnabled && trigger.PlaysOnPress) return true;
            return false;
        }
    }
}
