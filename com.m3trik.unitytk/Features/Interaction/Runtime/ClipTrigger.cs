using UnityEngine;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// What a selection of its <see cref="Target"/> plays, while it is enabled: its
    /// <see cref="clip"/> on its <see cref="player"/> (a press held down plays it while held,
    /// <see cref="whileHeld"/>). One target used in several steps has a trigger for each, each
    /// enabled by its own step, so a door selected to open never plays the clip that closes it
    /// later. A <see cref="tool"/> in the selecting hand that the clip does not move works at
    /// <see cref="workAt"/> while it plays (<see cref="ClipPlayer.Play(string, Holdable, Transform)"/>).
    /// </summary>
    /// <remarks>
    /// The target's events call it (persistent calls, shown in the inspector); a disabled
    /// trigger ignores them, so the step that enables it scopes what a selection does. Prior
    /// art: the target's own events could call the clip player straight (they did): one target
    /// then played every clip any step gave it at once, the last over the others. An Animator
    /// controller's state per step would be a second copy of the steps; XRI's interactable
    /// events have the same one-list shape as the target's.
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Clip Trigger")]
    [RequireComponent(typeof(Target))]
    public sealed class ClipTrigger : MonoBehaviour
    {
        [Tooltip("The clip player the clip plays on (the delivery's root's).")]
        public ClipPlayer player;

        [Tooltip("The clip a selection plays (its take's name).")]
        public string clip = "";

        [Tooltip("Play it while a press is held, pausing as it is let go, rather than on a click.")]
        public bool whileHeld;

        [Tooltip("With While Held: let go, the clip plays back to its start (a button whose effect lasts only while it is held) " +
                 "rather than stopping where it stands.")]
        public bool reverseOnRelease;

        [Tooltip("Play it once a press has been held this long, in seconds, as a click plays it (a button held to confirm); " +
                 "let go sooner, nothing plays. 0: a click.")]
        [Min(0f)]
        public float pressSeconds;

        [Tooltip("Count a timed press in real time, unaffected by the time scale (a paused or slowed scene).")]
        public bool unscaledTime;

        Hand _pressedBy;
        float _pressed = -1f;  // seconds a timed press has been held; below 0: none

        /// <summary>How far a timed press is to playing, 0 to 1 (<see cref="pressSeconds"/>); 0 when none is held.</summary>
        public float PressProgress => _pressed < 0f || pressSeconds <= 0f ? 0f : Mathf.Clamp01(_pressed / pressSeconds);

        /// <summary>Whether its press is timed: it plays once held <see cref="pressSeconds"/>, as a click plays it (not <see cref="whileHeld"/>).</summary>
        public bool Timed => !whileHeld && pressSeconds > 0f;

        /// <summary>Whether a press of its target plays it (<see cref="whileHeld"/>, or <see cref="Timed"/>) rather than a click: what its target's press events call.</summary>
        public bool PlaysOnPress => whileHeld || Timed;

        /// <summary>Whether a timed press is held, short of its time (<see cref="Tick"/> counts it on).</summary>
        public bool Counting => _pressed >= 0f;

        [Tooltip("A tool in the selecting hand that goes to Work At and works there while the clip plays " +
                 "(none: what is in hand stays there). Set for a tool the clip does not move.")]
        public Holdable tool;

        [Tooltip("Where the tool works: the part the clip moves (a screw it turns out), the target where empty.")]
        public Transform workAt;

        /// <summary>Where the tool works: <see cref="workAt"/>, else the target.</summary>
        public Transform WorkPoint => workAt != null ? workAt : transform;

        /// <summary>
        /// A selection of the target (its <see cref="Target.onSelected"/>): plays the clip, with
        /// the <see cref="tool"/> working where the hand holds it. Ignored while disabled.
        /// </summary>
        /// <param name="by">The selecting hand (null: none).</param>
        public void Play(Hand by)
        {
            if (!isActiveAndEnabled || player == null) return;
            if (tool != null && by != null && by.Holds(tool)) player.Play(clip, tool, WorkPoint);
            else player.Play(clip);
        }

        /// <summary>Count a timed press on; at <see cref="pressSeconds"/>, play the clip as a click does. A player calls it each frame.</summary>
        /// <param name="seconds">The time since the last frame.</param>
        public void Tick(float seconds)
        {
            if (_pressed < 0f) return;
            _pressed += Mathf.Max(0f, seconds);
            if (_pressed < pressSeconds) return;
            _pressed = -1f;
            Play(_pressedBy);
        }

        void Update() => Tick(unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        void OnDisable() => _pressed = -1f;

        /// <summary>A press of the target (its <see cref="Target.onPressed"/>): plays the clip on, held; or, a timed press (<see cref="pressSeconds"/>), starts counting. Ignored while disabled.</summary>
        /// <param name="by">The pressing hand (null: none).</param>
        public void Hold(Hand by)
        {
            if (!isActiveAndEnabled || player == null) return;
            if (Timed)
            {
                _pressedBy = by;
                _pressed = 0f;  // counted on in Update; it plays when held long enough
                return;
            }
            if (tool != null && by != null && by.Holds(tool)) player.Hold(clip, tool, WorkPoint);
            else player.Hold(clip);
        }

        /// <summary>
        /// The press let go (its <see cref="Target.onReleased"/>): a clip played while held (<see cref="whileHeld"/>) stops
        /// where it stands, or plays back to its start (<see cref="reverseOnRelease"/>), even after the step; a timed press
        /// let go before its time plays nothing, and one that played its clip lets it play on, as a click's does.
        /// </summary>
        /// <param name="by">The hand that pressed (null: none).</param>
        public void Release(Hand by)
        {
            if (!whileHeld)
            {
                _pressed = -1f;  // a timed press let go: before its time nothing plays; after, its clip plays on
                return;
            }
            if (player == null) return;
            if (reverseOnRelease) player.Rewind(clip);
            else player.Pause(clip);
        }
    }
}
