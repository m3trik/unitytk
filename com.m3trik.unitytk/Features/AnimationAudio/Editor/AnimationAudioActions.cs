using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.AnimationAudio
{
    /// <summary>
    /// The Animation Audio feature's actions: what a person's menu item and an agent's
    /// code do to a project with no DCC behind it. Each takes an optional
    /// <see cref="OpContext"/>: a dry run plans, a menu's run is one undo step, and
    /// none just runs.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own authoring of the same events, the model importer's
    /// Events tab and the Animation window's event markers, which a person uses by
    /// hand with no dry run and no way for an agent to call; these write exactly what
    /// those write (a model's clip: the Events tab, Unity's half of an imported asset;
    /// a clip of the project's own: the clip), through the kernel's
    /// <see cref="OpContext"/>.
    /// </remarks>
    public static class AnimationAudioActions
    {
        /// <summary>
        /// Cue <paramref name="sound"/> in <paramref name="clip"/>: a <c>PlayAudio</c>
        /// event at <paramref name="time"/>, starting <paramref name="offset"/> seconds
        /// into the sound, and, with a <paramref name="stop"/>, a <c>StopAudio</c> event
        /// there. A model's clip gets them in the model importer's Events tab (the
        /// project's half of an imported asset, kept through every re-export) and is
        /// reimported; a clip of the project's own gets them in the clip. A clip that
        /// cues the sound at that time already is left alone, so a second run plans
        /// nothing. The object whose Animator plays the clip needs an
        /// <see cref="AnimationAudioPlayer"/> (<see cref="AddPlayer"/>; an imported
        /// model's root gets one at import).
        /// </summary>
        /// <param name="clip">The clip that plays the sound.</param>
        /// <param name="sound">The sound.</param>
        /// <param name="time">When it starts, in seconds from the clip's start.</param>
        /// <param name="stop">When it stops, in seconds from the clip's start; null: at its end, or when the clip stops playing.</param>
        /// <param name="offset">Where in the sound it starts, in seconds.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it added the cue (in a dry run, would add).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> or <paramref name="sound"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="time"/> is outside the clip,
        /// <paramref name="stop"/> is not after it or past the clip's end, or <paramref name="offset"/> is
        /// negative or past the sound's end.</exception>
        /// <exception cref="ArgumentException">The model's clip list cuts no clip of that name: reset
        /// the model's clip list in its Animation tab.</exception>
        public static bool AddCue(AnimationClip clip, AudioClip sound, float time, float? stop = null,
                                  float offset = 0f, OpContext ctx = null)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (sound == null) throw new ArgumentNullException(nameof(sound));
            const float slack = 1e-4f;
            if (time < 0f || time > clip.length + slack)
                throw new ArgumentOutOfRangeException(nameof(time), time,
                    $"a cue starts within its clip, 0 to {clip.length:F3} s of '{clip.name}'");
            if (stop.HasValue && (stop.Value <= time || stop.Value > clip.length + slack))
                throw new ArgumentOutOfRangeException(nameof(stop), stop,
                    $"a cue stops after it starts ({time:F3} s) and no later than the end of '{clip.name}' ({clip.length:F3} s)");
            if (offset < 0f || offset >= sound.length)
                throw new ArgumentOutOfRangeException(nameof(offset), offset,
                    $"the offset is a time within '{sound.name}', 0 to {sound.length:F3} s");
            if (Cues(clip, sound, time)) return false;

            string action = $"cue '{sound.name}' at {time:F3} s of clip '{clip.name}'";
            string path = AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter importer)
            {
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
                ModelImporterClipAnimation cut = clips.FirstOrDefault(c => c.name == clip.name);
                if (cut == null)
                    throw new ArgumentException(
                        $"'{path}' cuts no clip named '{clip.name}' in its Animation tab: reset its clip list there, " +
                        "then add the cue again.", nameof(clip));
                if (ctx != null && !ctx.ShouldWrite(path, action + " in the model's Events tab")) return true;
                // The Events tab states each event's time as a fraction of its clip.
                float length = clip.length > 0f ? clip.length : 1f;
                var events = new List<AnimationEvent>(cut.events ?? new AnimationEvent[0]);
                events.AddRange(Pair(sound, time / length, stop / length, offset));
                cut.events = events.OrderBy(e => e.time).ToArray();
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                return true;
            }

            if (ctx == null || ctx.ShouldChange(clip, action))
            {
                var events = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip));
                events.AddRange(Pair(sound, time, stop, offset));
                AnimationUtility.SetAnimationEvents(clip, events.OrderBy(e => e.time).ToArray());
                if (ctx == null) EditorUtility.SetDirty(clip);
            }
            return true;
        }

        /// <summary>
        /// Give <paramref name="target"/> an <see cref="AnimationAudioPlayer"/> (and the
        /// AudioSource whose settings its sounds play with, 3D, when it has none): the
        /// object whose Animator or Animation plays clips that cue sounds, since Unity
        /// sends a clip's events there. One that has a player already is left alone.
        /// </summary>
        /// <param name="target">The object whose Animator or Animation plays the clips.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it added the player (in a dry run, would add).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        public static bool AddPlayer(GameObject target, OpContext ctx = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.GetComponent<AnimationAudioPlayer>() != null) return false;
            if (ctx == null || ctx.ShouldCreate($"{target.name}/AnimationAudioPlayer", "add an Animation Audio Player"))
            {
                bool hadSource = target.GetComponent<AudioSource>() != null;
                ctx.AddComponent<AnimationAudioPlayer>(target);
                if (!hadSource) AnimationAudioPlayer.Defaults(target.GetComponent<AudioSource>());
            }
            return true;
        }

        // Whether the clip cues the sound at the time already (delivered, or authored).
        static bool Cues(AnimationClip clip, AudioClip sound, float time)
        {
            foreach (AnimationEvent e in AnimationUtility.GetAnimationEvents(clip))
                if (e.functionName == PersistedStrings.PlayAudioFunction && e.objectReferenceParameter == sound
                    && Mathf.Abs(e.time - time) < 1e-4f)
                    return true;
            return false;
        }

        // A cue's events: PlayAudio at its time (its offset the seconds into the sound),
        // and StopAudio at its stop when it has one -- the one spelling of a cue, the
        // importer's delivered cues and these actions' alike.
        internal static IEnumerable<AnimationEvent> Pair(AudioClip sound, float time, float? stop, float offset)
        {
            yield return new AnimationEvent
            {
                time = time,
                functionName = PersistedStrings.PlayAudioFunction,
                objectReferenceParameter = sound,
                floatParameter = offset,
            };
            if (stop.HasValue)
                yield return new AnimationEvent
                {
                    time = stop.Value,
                    functionName = PersistedStrings.StopAudioFunction,
                    objectReferenceParameter = sound,
                };
        }
    }
}
