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
            if (ctx == null || ctx.ShouldCreate(OpContextEdits.Key(target, typeof(AnimationAudioPlayer)), "add an Animation Audio Player"))
            {
                bool hadSource = target.GetComponent<AudioSource>() != null;
                ctx.AddComponent<AnimationAudioPlayer>(target);
                if (!hadSource) AnimationAudioPlayer.Defaults(target.GetComponent<AudioSource>());
            }
            return true;
        }

        /// <summary>
        /// Leave the cues of <paramref name="sounds"/> (by name) out of what the player on <paramref name="target"/>
        /// plays (<see cref="AnimationAudioPlayer.leave"/>): something else plays them (a task's voice line, as its
        /// step starts), so a delivered cue of the same sound never plays twice. The list becomes exactly these, sorted;
        /// an object with no player cues no sound, and is left alone.
        /// </summary>
        /// <param name="target">The object a delivery's player sits on (its model's root).</param>
        /// <param name="sounds">The sounds played elsewhere, by name.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed the list (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="sounds"/> is null.</exception>
        public static bool Leave(GameObject target, IEnumerable<string> sounds, OpContext ctx = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (sounds == null) throw new ArgumentNullException(nameof(sounds));
            var player = target.GetComponent<AnimationAudioPlayer>();
            if (player == null) return false;
            List<string> wanted = sounds.Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (player.leave.SequenceEqual(wanted)) return false;
            if (ctx != null && !ctx.ShouldChange(player, $"leave {wanted.Count} sound(s) to what plays them elsewhere")) return true;
            player.leave = wanted;
            ctx.Edited(player);
            return true;
        }

        /// <summary>
        /// Give the object <paramref name="cue"/> under the active scene's top-level object <paramref name="owner"/>
        /// (both made where missing) a <see cref="SoundCue"/> playing <paramref name="sound"/>, made switched off
        /// unless <paramref name="startEnabled"/>: whatever switches it on plays it, for as long as it stays on.
        /// One there is brought up to date where it plays another sound, or another way. A dry run plans what it
        /// would make, so a later action may name it.
        /// </summary>
        /// <param name="owner">The name of the top-level object that holds the scene's cues.</param>
        /// <param name="cue">The cue's object's name under it.</param>
        /// <param name="sound">The sound.</param>
        /// <param name="startEnabled">Make it playing from the start; false: off until something switches it on.</param>
        /// <param name="spatialBlend">How far the sound is placed at its object: 0 flat (a voice in the listener's ears), 1 fully 3D.</param>
        /// <param name="loop">Play it over and over while switched on; false: once.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The cue; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentException"><paramref name="owner"/> or <paramref name="cue"/> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="sound"/> is null.</exception>
        public static SoundCue AddSoundCue(string owner, string cue, AudioClip sound, bool startEnabled = false, float spatialBlend = 0f,
                                           bool loop = false, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(owner)) throw new ArgumentException("name the top-level object", nameof(owner));
            if (string.IsNullOrEmpty(cue)) throw new ArgumentException("name the cue", nameof(cue));
            if (sound == null) throw new ArgumentNullException(nameof(sound), "name the sound it plays");
            spatialBlend = Mathf.Clamp01(spatialBlend);
            GameObject root = SceneRoots.Find(owner);
            Transform child = root == null ? null : root.transform.Find(cue);
            SoundCue found = child == null ? null : child.GetComponent<SoundCue>();
            if (found != null)
            {
                if (found.clip == sound && found.setSource && found.spatialBlend == spatialBlend && found.loop == loop) return found;
                if (ctx != null && !ctx.ShouldChange(found, $"play '{sound.name}'")) return found;
                found.clip = sound;
                found.setSource = true;
                found.spatialBlend = spatialBlend;
                found.loop = loop;
                ctx.Edited(found);
                return found;
            }
            var holder = new PlannedObject(owner);
            string key = OpContextEdits.Key(new PlannedObject(cue, holder), typeof(SoundCue));
            if (ctx.Planned("create", key)) return null;
            if (root == null && !ctx.Planned("create", holder.Path)
                && (ctx == null || ctx.ShouldCreate(holder.Path, "hold the scene's sound cues")))  // a dry run goes on to plan the cue
            {
                root = new GameObject(owner);
                ctx.Created(root);
            }
            if (ctx != null && !ctx.ShouldCreate(key, $"play '{sound.name}' while it is switched on")) return null;
            if (child == null)
            {
                child = new GameObject(cue).transform;
                child.SetParent(root.transform, false);
                ctx.Created(child.gameObject);
            }
            SoundCue made = ctx.AddComponent<SoundCue>(child.gameObject);  // it sets up its source itself, as it plays
            made.clip = sound;
            made.spatialBlend = spatialBlend;
            made.loop = loop;
            made.enabled = startEnabled;
            ctx.Edited(made);
            return made;
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
