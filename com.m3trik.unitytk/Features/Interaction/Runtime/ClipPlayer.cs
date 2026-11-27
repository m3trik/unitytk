using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// Plays a delivery's clips on demand, each over the stretch in which it moves anything
    /// (<see cref="Entry.from"/> to <see cref="Entry.until"/>), and keeps the pose each ends on:
    /// the scene stands where the last clip to move a thing left it, as the clips were authored
    /// one after another. A holdable a clip moves is lent to it while it plays
    /// (<see cref="Holdable.Lend"/>): the clip waits at its first pose until the holdable has
    /// come from the hand into it (<see cref="Entry.waits"/>), then plays, and the holdable goes
    /// back to the hand or its rest after. A clip played while something is held
    /// (<see cref="Hold"/>) stops where it stands as it is let go (<see cref="Pause"/>), and goes
    /// on from there at the next hold.
    /// </summary>
    /// <remarks>
    /// Prior art: an Animator controller's states (one state per clip, a transition per
    /// step) would be a second copy of the steps' flow, and a state that is left lets
    /// go of its pose; Timeline plays one authored sequence, not clips picked at run time;
    /// <c>AnimationPlayableUtilities.PlayClip</c> plays one clip, and the next replaces it,
    /// so the pose the first left is lost. So one Playables graph holds every clip played,
    /// each a layer over the ones before (<c>AnimationLayerMixerPlayable</c>, override): a
    /// later clip moves what it animates, and everything else keeps the pose an earlier one
    /// left. A clip played again takes the place of its own finished layer (it animates the
    /// same properties, so the earlier one shows nothing more), so the graph holds a layer for
    /// each clip at most, besides those still playing. Its time is driven here
    /// (<see cref="Advance"/>), each frame in a player, or by a caller (a test, an editor preview).
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Clip Player")]
    [DisallowMultipleComponent]
    public sealed class ClipPlayer : MonoBehaviour
    {
        /// <summary>A clip the player plays, and the holdables it moves.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("The clip (one of the delivery's takes).")]
            public AnimationClip clip;

            [Tooltip("The holdables the clip moves: lent to it while it plays.")]
            public List<Holdable> lends = new List<Holdable>();

            [Tooltip("Of those, the ones whose own place the clip does not key (it moves their parts): they go back " +
                     "where they rest while it plays, the place its motion of their parts was made from.")]
            public List<Holdable> rests = new List<Holdable>();

            [Tooltip("The holdables the clip puts in place for good (a part fitted into its slot): as it ends, one it moved stays " +
                     "where it left it, out of the hand, that place its rest from then on; one that worked for it (it rode what the " +
                     "clip moved) stays at its work, riding it. One it is stopped short of ending goes back to the hand.")]
            public List<Holdable> leaves = new List<Holdable>();

            [Tooltip("Seconds into the clip it plays from. A take made for one long animation often pops its parts into place " +
                     "on its first frames and stands still a while: what plays starts where its motion does.")]
            [Min(0f)]
            public float from;

            [Tooltip("Seconds into the clip it plays to, and holds (0: the clip's end). A take often ends on the next take's " +
                     "first pose: what plays ends where its own motion does.")]
            [Min(0f)]
            public float until;

            [Tooltip("From and Until are found from the clip's motion whenever the build adds the clip. Off: they stay as set here.")]
            public bool auto = true;

            [Tooltip("How fast it plays: 1 as authored, 2 twice as fast.")]
            [Min(0.01f)]
            public float speed = 1f;

            [Tooltip("Wait at its first pose until every holdable it lends, and the tool that works for it, has arrived: the " +
                     "tool goes there, then the clip plays. Off: it plays at once, the tool catching up.")]
            public bool waits = true;

            /// <summary>Where it plays to, in seconds into the clip: <see cref="until"/>, or the clip's end where that is 0 or past it.</summary>
            public float End => clip == null ? 0f : until > 0f && until < clip.length ? until : clip.length;

            /// <summary>Where it plays from, in seconds into the clip: <see cref="from"/>, never past <see cref="End"/>.</summary>
            public float Start => Mathf.Clamp(from, 0f, End);

            [Tooltip("The clip that plays when this one ends (empty: none).")]
            public string then = "";
        }

        [Tooltip("The animator the clips play on (this object's when empty).")]
        public Animator animator;

        [Tooltip("The clips this player plays, by name.")]
        public List<Entry> clips = new List<Entry>();

        [Tooltip("A clip started (its name).")]
        public ClipEvent onStarted = new ClipEvent();

        [Tooltip("A clip reached its end (its name).")]
        public ClipEvent onFinished = new ClipEvent();

        [Tooltip("Play in real time, unaffected by the time scale (a paused or slowed scene).")]
        public bool unscaledTime;

        sealed class Layer
        {
            public Entry entry;
            public AnimationClipPlayable playable;
            public bool done;
            public bool ended;
            public bool paused;
            public bool rewinding;  // let go: playing back toward its start, where it pauses (Rewind)
            public bool waiting;  // at its first pose, until what it lends and its worker have arrived
            public Holdable worker;
        }

        readonly List<Layer> _layers = new List<Layer>();
        PlayableGraph _graph;
        AnimationLayerMixerPlayable _mixer;

        /// <summary>The animator the clips play on.</summary>
        public Animator Animator => animator != null ? animator : GetComponent<Animator>();

        /// <summary>The entry of the clip named <paramref name="clip"/>; null when the player has none.</summary>
        /// <param name="clip">A clip's name.</param>
        public Entry Find(string clip)
        {
            foreach (Entry entry in clips)
                if (entry != null && entry.clip != null && entry.clip.name == clip) return entry;
            return null;
        }

        /// <summary>Whether the clip named <paramref name="clip"/> is playing (started, not at its end).</summary>
        /// <param name="clip">A clip's name.</param>
        public bool IsPlaying(string clip)
        {
            foreach (Layer layer in _layers)
                if (!layer.done && layer.entry.clip.name == clip) return true;
            return false;
        }

        /// <summary>
        /// Whether <paramref name="entry"/>'s clip is playing on: started, not at its end, nor paused part way (a hold let
        /// go). Asked by what watches a take each frame: it allocates nothing.
        /// </summary>
        /// <param name="entry">One of this player's entries (<see cref="Find"/>).</param>
        public bool PlayingOn(Entry entry)
        {
            foreach (Layer layer in _layers)
                if (!layer.done && !layer.paused && layer.entry == entry) return true;
            return false;
        }

        /// <summary>Whether the clip named <paramref name="clip"/> waits at its first pose for a tool to arrive (<see cref="Entry.waits"/>).</summary>
        /// <param name="clip">A clip's name.</param>
        public bool IsWaiting(string clip) => Playing(clip) is Layer layer && layer.waiting;

        /// <summary>Whether the clip named <paramref name="clip"/> stands paused part way (<see cref="Pause"/>).</summary>
        /// <param name="clip">A clip's name.</param>
        public bool IsPaused(string clip) => Playing(clip) is Layer layer && layer.paused;

        /// <summary>Whether any clip is playing on (one paused part way waits on a hold, so is not).</summary>
        public bool Busy
        {
            get
            {
                foreach (Layer layer in _layers)
                    if (!layer.done && !layer.paused) return true;
                return false;
            }
        }

        /// <summary>
        /// Play the clip named <paramref name="clip"/> from its start (<see cref="Entry.from"/>),
        /// over every clip played before (what it does not animate keeps their pose). A name the
        /// player does not know is refused with a warning and nothing plays.
        /// </summary>
        /// <param name="clip">A clip's name (its take's).</param>
        public void Play(string clip) => Begin(clip, null, null);

        /// <summary>
        /// <see cref="Play(string)"/>, with <paramref name="worker"/> (a tool in hand the clip
        /// does not move) working at <paramref name="at"/> while it plays
        /// (<see cref="Holdable.WorkAt"/>) and going back as it ends: a screwdriver on the
        /// screw the clip turns out. A worker the clip lends (it moves it) is the clip's alone.
        /// </summary>
        /// <param name="clip">A clip's name (its take's).</param>
        /// <param name="worker">The tool that works (null: none).</param>
        /// <param name="at">Where it works (null: it does not).</param>
        public void Play(string clip, Holdable worker, Transform at) => Begin(clip, worker, at);

        void Begin(string clip, Holdable worker, Transform at)
        {
            Entry entry = Find(clip);
            if (entry == null || Animator == null)
            {
                Debug.LogWarning(entry == null
                    ? $"{name}: no clip '{clip}' in its Clip Player's list, so nothing plays: add it to the list"
                    : $"{name}: its Clip Player has no Animator to play '{clip}' on: add one or name it", this);
                return;
            }
            EnsureGraph();
            DropFinished(entry.clip);
            var playable = AnimationClipPlayable.Create(_graph, entry.clip);
            playable.SetApplyFootIK(false);
            playable.SetTime(entry.Start);
            int index = _mixer.GetInputCount();
            _mixer.SetInputCount(index + 1);
            _graph.Connect(playable, 0, _mixer, index);
            _mixer.SetInputWeight(index, 1f);
            var layer = new Layer { entry = entry, playable = playable };
            _layers.Add(layer);
            foreach (Holdable lent in entry.lends)
                if (lent != null) lent.Lend(entry.rests.Contains(lent));
            if (worker != null && at != null && !entry.lends.Contains(worker))
            {
                layer.worker = worker;
                worker.WorkAt(at);
            }
            layer.waiting = entry.waits && Arriving(layer);
            _graph.Evaluate(0f);
            // The clip's first pose stands: what it lends moves into it from where it was, never shown there at once.
            foreach (Holdable lent in entry.lends)
                if (lent != null) lent.Settle();
            onStarted.Invoke(clip);
        }

        /// <summary>
        /// Play the clip named <paramref name="clip"/> while something is held: on from where it
        /// was paused (<see cref="Pause"/>), else from its start as <see cref="Play(string)"/> does; one
        /// playing already goes on.
        /// </summary>
        /// <param name="clip">A clip's name (its take's).</param>
        public void Hold(string clip) => Hold(clip, null, null);

        /// <summary>
        /// <see cref="Hold(string)"/>, with <paramref name="worker"/> working at <paramref name="at"/> as
        /// <see cref="Play(string, Holdable, Transform)"/> has it, where the clip starts anew.
        /// </summary>
        /// <param name="clip">A clip's name (its take's).</param>
        /// <param name="worker">The tool that works (null: none).</param>
        /// <param name="at">Where it works (null: it does not).</param>
        public void Hold(string clip, Holdable worker, Transform at)
        {
            Layer layer = Playing(clip);
            if (layer == null)
            {
                Begin(clip, worker, at);
                return;
            }
            layer.paused = false;
            layer.rewinding = false;
        }

        /// <summary>
        /// Play the clip named <paramref name="clip"/> back toward its start (<see cref="Entry.from"/>), where it
        /// pauses, until <see cref="Hold(string)"/> goes on with it: a press let go that undoes what it did (a
        /// button whose effect lasts only while it is held). A clip at its end, or none playing, is left.
        /// </summary>
        /// <param name="clip">A clip's name.</param>
        public void Rewind(string clip)
        {
            Layer layer = Playing(clip);
            if (layer == null) return;
            layer.paused = false;
            layer.rewinding = true;
        }

        /// <summary>Whether the clip named <paramref name="clip"/> plays back toward its start (<see cref="Rewind"/>).</summary>
        /// <param name="clip">A clip's name.</param>
        public bool IsRewinding(string clip) => Playing(clip) is Layer layer && layer.rewinding;

        /// <summary>Stop the clip named <paramref name="clip"/> where it stands, until <see cref="Hold"/> goes on with it.</summary>
        /// <param name="clip">A clip's name.</param>
        public void Pause(string clip)
        {
            Layer layer = Playing(clip);
            if (layer == null) return;
            layer.paused = true;
            layer.rewinding = false;
        }

        // Take away the finished layers of clip: one about to play it again covers every property they hold.
        void DropFinished(AnimationClip clip)
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                if (!_layers[i].done || _layers[i].entry.clip != clip) continue;
                _graph.DestroyPlayable(_layers[i].playable);
                int count = _mixer.GetInputCount();
                for (int j = i + 1; j < count; j++)  // the layers over it move down one
                {
                    Playable above = _mixer.GetInput(j);
                    float weight = _mixer.GetInputWeight(j);
                    _graph.Disconnect(_mixer, j);
                    _graph.Connect(above, 0, _mixer, j - 1);
                    _mixer.SetInputWeight(j - 1, weight);
                }
                _mixer.SetInputCount(count - 1);
                _layers.RemoveAt(i);
            }
        }

        // The latest layer of clip still short of its end; null when none is.
        Layer Playing(string clip)
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
                if (!_layers[i].done && _layers[i].entry.clip.name == clip) return _layers[i];
            return null;
        }

        /// <summary>
        /// Move every playing clip <paramref name="seconds"/> on, writing the pose; a clip
        /// that reaches its end stays there, gives back what it borrowed and raises
        /// <see cref="onFinished"/>. A player calls it each frame; a test or a preview may
        /// call it itself.
        /// </summary>
        /// <param name="seconds">How far to move (not below 0).</param>
        public void Advance(float seconds)
        {
            if (!_graph.IsValid()) return;
            for (int i = 0; i < _layers.Count; i++)
            {
                Layer layer = _layers[i];
                if (layer.done || layer.paused) continue;
                if (layer.waiting)
                {
                    if (Arriving(layer)) continue;  // its tool is on its way: the clip stands at its first pose
                    layer.waiting = false;
                }
                double step = Math.Max(0f, seconds) * Math.Max(0.01f, layer.entry.speed);
                if (layer.rewinding)
                {
                    double start = layer.entry.Start;
                    double back = Math.Max(layer.playable.GetTime() - step, start);
                    layer.playable.SetTime(back);
                    if (back <= start)
                    {
                        // Back at its start: it waits on a hold.
                        layer.rewinding = false;
                        layer.paused = true;
                    }
                    continue;
                }
                double end = layer.entry.End;
                double time = Math.Min(layer.playable.GetTime() + step, end);
                layer.playable.SetTime(time);
                if (time >= end) layer.done = layer.ended = true;
            }
            _graph.Evaluate(0f);
            // The layers that ended, taken first: a clip that follows one (Play) drops its own finished layers, one that
            // ended now among them, which would then never raise its end.
            List<Layer> ended = null;
            foreach (Layer layer in _layers)
                if (layer.ended)
                {
                    layer.ended = false;
                    (ended ??= new List<Layer>()).Add(layer);
                }
            if (ended == null) return;
            foreach (Layer layer in ended)
            {
                foreach (Holdable lent in layer.entry.lends)
                {
                    if (lent == null) continue;
                    if (layer.entry.leaves.Contains(lent)) lent.Leave();
                    else lent.Reclaim();
                }
                if (layer.worker != null)
                {
                    if (layer.entry.leaves.Contains(layer.worker)) layer.worker.StayAtWork();  // put in place: it stays there
                    else layer.worker.StopWork();
                }
                onFinished.Invoke(layer.entry.clip.name);
                if (!string.IsNullOrEmpty(layer.entry.then)) Play(layer.entry.then);
            }
        }

        /// <summary>
        /// Play every clip that plays on to its end at once, and each that follows it, the tools a
        /// clip waits for brought there first (<see cref="Holdable.Arrive"/>): what skips the watching
        /// does (a fast-forward, a test of what the clips' ends set off). A clip paused part way
        /// (<see cref="Pause"/>) stays paused.
        /// </summary>
        public void Finish()
        {
            for (int pass = 0; pass < 64 && Busy; pass++)
            {
                foreach (Layer layer in _layers)
                {
                    if (layer.done || !layer.waiting) continue;
                    foreach (Holdable lent in layer.entry.lends)
                        if (lent != null) lent.Arrive();
                    if (layer.worker != null) layer.worker.Arrive();
                }
                Advance(3600f);
            }
        }

        // Whether a holdable the layer's clip lends, or the tool that works for it, is still on its way. One switched
        // off (its object or itself) is moved on by no frame of its own, so it is brought there at once: the clip never
        // waits on what cannot come.
        static bool Arriving(Layer layer)
        {
            bool arriving = OnItsWay(layer.worker);
            foreach (Holdable lent in layer.entry.lends)
                arriving |= OnItsWay(lent);
            return arriving;
        }

        static bool OnItsWay(Holdable holdable)
        {
            if (holdable == null || !holdable.Travelling) return false;
            if (holdable.isActiveAndEnabled) return true;
            holdable.Arrive();
            return false;
        }

        /// <summary>Forget every clip played: the next one plays over the delivery's own pose.</summary>
        public void Clear()
        {
            foreach (Layer layer in _layers)
                if (!layer.done)
                {
                    foreach (Holdable lent in layer.entry.lends)
                        if (lent != null) lent.Reclaim();
                    if (layer.worker != null) layer.worker.StopWork();
                }
            _layers.Clear();
            if (_graph.IsValid()) _graph.Destroy();
        }

        void Update() => Advance(unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        void OnDisable() => Clear();

        void EnsureGraph()
        {
            if (_graph.IsValid()) return;
            _graph = PlayableGraph.Create($"{name} clips");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationLayerMixerPlayable.Create(_graph, 0);
            var output = AnimationPlayableOutput.Create(_graph, "clips", Animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }
    }
}
