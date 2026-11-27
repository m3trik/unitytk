using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WindowRules = M3trik.Unitytk.Interaction.Interactions.WindowRules;

namespace M3trik.Unitytk.Interaction
{
    // The stretch of a clip worth playing (Interactions.Window): from its first change to its last, never the poses
    // a tool it lends pops into around its work.
    internal static class ClipWindow
    {
        internal static Vector2 Of(AnimationClip clip, IEnumerable<string> tools, WindowRules rules)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip), "name a clip");
            WindowRules read = rules ?? new WindowRules();
            var lent = new List<string>(tools ?? Array.Empty<string>());
            float first = float.MaxValue, last = float.MinValue;            // every change, of anything
            float moved = float.MaxValue, still = float.MinValue;           // a tool's own motion, its pops aside
            var pops = new List<Vector2>();                                 // a tool's pops: (starts, ends)
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                Keyframe[] keys = curve == null ? null : curve.keys;
                if (keys == null || keys.Length < 2) continue;
                bool tool = false;
                foreach (string path in lent)
                    if (binding.path == path || binding.path.StartsWith(path + "/", StringComparison.Ordinal)) tool = true;
                float usual = tool ? UsualRate(keys, read) : 0f;
                for (int i = 0; i + 1 < keys.Length; i++)
                {
                    float step = Mathf.Abs(keys[i + 1].value - keys[i].value);
                    if (step <= read.still) continue;
                    if (tool && Pop(keys, i, usual, read))
                    {
                        pops.Add(new Vector2(keys[i].time, keys[i + 1].time));
                        continue;
                    }
                    first = Mathf.Min(first, keys[i].time);
                    last = Mathf.Max(last, keys[i + 1].time);
                    if (!tool) continue;
                    moved = Mathf.Min(moved, keys[i].time);
                    still = Mathf.Max(still, keys[i + 1].time);
                }
            }
            if (first == float.MaxValue) return new Vector2(0f, clip.length);  // nothing but pops: the whole clip
            if (moved == float.MaxValue)
            {
                moved = first;  // a tool the clip only pops: in place over what else it moves
                still = last;
            }
            float from = first, until = last;
            foreach (Vector2 pop in pops)
            {
                if (pop.y <= moved + read.popSeconds) from = Mathf.Max(from, pop.y);                  // in place from here
                else if (pop.x >= still - read.popSeconds) until = Mathf.Min(until, pop.x - Margin);  // away from here
            }
            return until > from ? new Vector2(from, until) : new Vector2(0f, clip.length);
        }

        // How far short of a pop a clip stops, so the pose it holds is the one before it.
        const float Margin = 1e-3f;
        // The shortest stretch a rate is measured over: two keys at one instant are a frame apart at most.
        const float Frame = 1f / 60f;

        // How fast keys' curve usually changes (its value a second): the median over the stretches longer than a
        // pop where it has any (a curve whose keys the import reduced: its motion's), else over all its changes
        // where they are five at least (a curve keyed every frame); 0 where neither says.
        static float UsualRate(Keyframe[] keys, WindowRules read)
        {
            var slow = new List<float>();
            var all = new List<float>();
            for (int i = 0; i + 1 < keys.Length; i++)
            {
                float step = Mathf.Abs(keys[i + 1].value - keys[i].value);
                if (step <= read.still) continue;
                float span = keys[i + 1].time - keys[i].time;
                float rate = step / Mathf.Max(span, Frame);
                all.Add(rate);
                if (span > read.popSeconds) slow.Add(rate);
            }
            List<float> rates = slow.Count > 0 ? slow : all.Count >= 5 ? all : null;
            if (rates == null) return 0f;
            rates.Sort();
            return rates[rates.Count / 2];
        }

        // Whether the change from key at to the next is a pop: quick, and far faster than the curve usually
        // moves, or alone between two holds (it and the changes touching it lasting no longer than a pop).
        static bool Pop(Keyframe[] keys, int at, float usual, WindowRules read)
        {
            float span = keys[at + 1].time - keys[at].time;
            if (span > read.popSeconds) return false;
            float rate = Mathf.Abs(keys[at + 1].value - keys[at].value) / Mathf.Max(span, Frame);
            if (usual > 0f && rate >= read.popTimes * usual) return true;
            int first = at, last = at;
            while (first > 0 && Mathf.Abs(keys[first].value - keys[first - 1].value) > read.still) first--;
            while (last + 2 < keys.Length && Mathf.Abs(keys[last + 2].value - keys[last + 1].value) > read.still) last++;
            return keys[last + 1].time - keys[first].time <= read.popSeconds;
        }
    }
}
