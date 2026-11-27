using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// Shows a cue (an arrow, a glow: any behaviour) only while it has something to point out: while one of its
    /// <see cref="targets"/> can still be acted on. A target counts while it is switched on, what it requires is in
    /// a hand (<see cref="waitsForHand"/>) and it has not been selected since the cue came on
    /// (<see cref="goesOnceActed"/>); and while a take one of them set off plays, nothing is pointed out (a cue
    /// several targets share comes back for the rest once the take is done). So of a step's cues, the one at the keys
    /// shows first and goes as they are taken, the ones at the locks come once the keys are in hand, and each goes as
    /// its lock is turned: never all at once, never on what the hand already holds, never over a tool at work or a
    /// take that plays. What switches it on (a step) switches the cue with it; off, its cue is off.
    /// </summary>
    /// <remarks>
    /// It names objects, and reads the <see cref="Target"/> on each as it comes on: an object that is no target has
    /// nothing to wait for, so its cue shows for as long as this is on.
    /// It writes the cue's <c>enabled</c> only as what it wants changes, so whatever else switches the cue (a
    /// holdable taken) keeps its say in between.
    /// <para>Prior art: the XR Interaction Toolkit's affordance system shows an interactable's own state (hovered,
    /// selected) on that interactable; it has no notion of a cue elsewhere that waits on what the hand holds, nor of
    /// one that goes once its target was used. Unity's UnityEvents alone (a holdable's taken switching a cue on)
    /// cannot say "while the step runs and the tool is held and the part is not done", which is one condition.</para>
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Target Cue")]
    public sealed class TargetCue : MonoBehaviour
    {
        [Tooltip("What it shows and hides: an arrow, a glow, any behaviour.")]
        public Behaviour cue;

        [Tooltip("What the cue points out: it shows while one of these can still be acted on (its Target switched on, what it requires " +
                 "in hand, not yet selected). One that is no Target always can.")]
        public List<GameObject> targets = new List<GameObject>();

        [Tooltip("Hidden until what a target requires is in a hand: the cue at the lock waits for the keys. Off: it shows before.")]
        public bool waitsForHand = true;

        [Tooltip("Gone once a target has been selected, and hidden while a take one of its targets set off plays: the cue at the lock " +
                 "goes as the key turns. Off: it stays for as long as it is switched on.")]
        public bool goesOnceActed = true;

        // What it points out while on: each object, its target (null: it is none) and what it hears that target by.
        readonly List<(GameObject at, Target target, UnityAction<Hand> heard)> _watched = new List<(GameObject, Target, UnityAction<Hand>)>();
        readonly List<Target> _acted = new List<Target>();  // the targets selected since it came on
        // The takes its targets set off (each target's clip triggers, read as it comes on).
        readonly List<(ClipPlayer player, ClipPlayer.Entry entry)> _takes = new List<(ClipPlayer, ClipPlayer.Entry)>();
        readonly List<ClipTrigger> _triggers = new List<ClipTrigger>();  // a buffer for reading them, its own
        bool _shown;

        /// <summary>Whether the cue has something to point out now (while it is on).</summary>
        public bool Wanted
        {
            get
            {
                if (goesOnceActed)
                    for (int i = 0; i < _takes.Count; i++)
                        if (_takes[i].player != null && _takes[i].player.PlayingOn(_takes[i].entry)) return false;  // over a take that plays
                for (int i = 0; i < _watched.Count; i++)
                {
                    (GameObject at, Target target, _) = _watched[i];
                    if (at == null) continue;
                    if (target == null) return true;  // no target: nothing to wait for
                    if (!target.isActiveAndEnabled) continue;
                    if (goesOnceActed && (target.IsPressed || _acted.Contains(target))) continue;
                    if (waitsForHand && target.requires != null && target.requires.Holder == null) continue;
                    return true;
                }
                return false;
            }
        }

        /// <summary>Begin to watch its targets and show the cue as it is wanted (what enabling it does).</summary>
        public void Watch()
        {
            _acted.Clear();
            Unwatch();
            foreach (GameObject at in targets)
            {
                if (at == null || Watched(at)) continue;
                Target target = at.GetComponent<Target>();
                UnityAction<Hand> heard = null;
                if (target != null)
                {
                    heard = by =>
                    {
                        if (!_acted.Contains(target)) _acted.Add(target);
                        Refresh();
                    };
                    target.onSelected.AddListener(heard);
                }
                _watched.Add((at, target, heard));
                at.GetComponents(_triggers);
                foreach (ClipTrigger trigger in _triggers)
                {
                    ClipPlayer.Entry entry = trigger.player != null ? trigger.player.Find(trigger.clip) : null;
                    if (entry != null) _takes.Add((trigger.player, entry));
                }
                _triggers.Clear();
            }
            _shown = Wanted;
            if (cue != null) cue.enabled = _shown;
        }

        /// <summary>Stop watching, its cue off (what disabling it does).</summary>
        public void Rest()
        {
            Unwatch();
            _acted.Clear();
            _shown = false;
            if (cue != null) cue.enabled = false;
        }

        void OnEnable() => Watch();

        void OnDisable() => Rest();

        bool Watched(GameObject at)
        {
            for (int i = 0; i < _watched.Count; i++)
                if (_watched[i].at == at) return true;
            return false;
        }

        void Unwatch()
        {
            foreach (var (_, target, heard) in _watched)
                if (target != null && heard != null) target.onSelected.RemoveListener(heard);
            _watched.Clear();
            _takes.Clear();
        }

        void Update() => Refresh();

        /// <summary>Switch the cue as it is wanted now (a player does each frame).</summary>
        public void Refresh()
        {
            bool wanted = Wanted;
            if (wanted == _shown) return;
            _shown = wanted;
            if (cue != null) cue.enabled = wanted;
        }
    }
}
