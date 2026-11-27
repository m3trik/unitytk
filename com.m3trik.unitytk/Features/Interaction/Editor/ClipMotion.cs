using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WindowRules = M3trik.Unitytk.Interaction.Interactions.WindowRules;

namespace M3trik.Unitytk.Interaction
{
    // What a clip moves under its root (Interactions.Moves), the holdables it lends and rests, and the scene posed
    // as clips leave it (Interactions.PosedAfter).
    internal static class ClipMotion
    {
        // The paths (from the animator's root) clip moves: a curve whose keys change. A take that keys
        // every node, most at the pose they hold, moves only those whose values change.
        internal static HashSet<string> Moving(AnimationClip clip)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (paths.Contains(binding.path)) continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                Keyframe[] keys = curve.keys;
                for (int i = 1; i < keys.Length; i++)
                    if (Mathf.Abs(keys[i].value - keys[0].value) > MovedBy)
                    {
                        paths.Add(binding.path);
                        break;
                    }
            }
            return paths;
        }

        // How far a key's value must stand from the first for the curve to move anything.
        const float MovedBy = 1e-4f;

        internal static IDisposable PosedAfter(GameObject root, IEnumerable<AnimationClip> clips, WindowRules rules)
        {
            if (root == null) throw new ArgumentNullException(nameof(root), "name the delivery's root");
            var posed = new Posed(root);
            if (clips == null) return posed;
            foreach (AnimationClip clip in clips)
            {
                if (clip == null) continue;
                HashSet<string> moving = Moving(clip);
                List<string> tools = Paths(root, Lends(root, clip));
                foreach (string own in tools)
                    moving.RemoveWhere(path => path == own || path.StartsWith(own + "/", StringComparison.Ordinal));
                var before = new Posed(root);
                clip.SampleAnimation(root, ClipWindow.Of(clip, tools, rules).y);
                before.RestoreExcept(root.transform, moving);
            }
            return posed;
        }

        internal static List<Transform> Moves(GameObject root, AnimationClip clip)
        {
            if (root == null) throw new ArgumentNullException(nameof(root), "name the delivery's root");
            if (clip == null) throw new ArgumentNullException(nameof(clip), "name a clip");
            var paths = new List<string>(Moving(clip));
            List<string> tools = Paths(root, Lends(root, clip));
            paths.RemoveAll(path => tools.Exists(own => path == own || path.StartsWith(own + "/", StringComparison.Ordinal)));
            paths.Sort((a, b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b));
            var kept = new List<string>();
            var moved = new List<Transform>();
            foreach (string path in paths)
            {
                if (kept.Exists(over => over.Length == 0 || path.StartsWith(over + "/", StringComparison.Ordinal))) continue;
                Transform part = path.Length == 0 ? root.transform : root.transform.Find(path);
                if (part == null) continue;
                kept.Add(path);
                moved.Add(part);
            }
            return moved;
        }

        // Every part's pose and switch under a root, put back on Dispose (or all but what clips moved).
        sealed class Posed : IDisposable
        {
            readonly List<(Transform part, Vector3 position, Quaternion rotation, Vector3 scale, bool active)> _parts =
                new List<(Transform, Vector3, Quaternion, Vector3, bool)>();

            internal Posed(GameObject root)
            {
                foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                    _parts.Add((part, part.localPosition, part.localRotation, part.localScale, part.gameObject.activeSelf));
            }

            // Put back each part whose path from root a clip does not move.
            internal void RestoreExcept(Transform root, HashSet<string> moved)
            {
                foreach (var kept in _parts)
                    if (kept.part != null && !moved.Contains(AnimationUtility.CalculateTransformPath(kept.part, root))) Put(kept);
            }

            public void Dispose()
            {
                foreach (var kept in _parts)
                    if (kept.part != null) Put(kept);
            }

            static void Put((Transform part, Vector3 position, Quaternion rotation, Vector3 scale, bool active) kept)
            {
                kept.part.localPosition = kept.position;
                kept.part.localRotation = kept.rotation;
                kept.part.localScale = kept.scale;
                if (kept.part.gameObject.activeSelf != kept.active) kept.part.gameObject.SetActive(kept.active);
            }
        }

        // The highest part at or under target that clip moves (root: the animator's); null when it moves none.
        internal static Transform MovedUnder(GameObject root, AnimationClip clip, Transform target)
        {
            string own = AnimationUtility.CalculateTransformPath(target, root.transform);
            string best = null;
            foreach (string path in Moving(clip))
            {
                if (path != own && !(own.Length == 0 || path.StartsWith(own + "/", StringComparison.Ordinal))) continue;
                if (best == null || Depth(path) < Depth(best) || Depth(path) == Depth(best) && string.CompareOrdinal(path, best) < 0) best = path;
            }
            return best == null ? null : best.Length == 0 ? root.transform : root.transform.Find(best);
        }

        static int Depth(string path)
        {
            int depth = path.Length == 0 ? 0 : 1;
            foreach (char c in path) if (c == '/') depth++;
            return depth;
        }

        // Of lends, those clip keys no curve on (it moves their parts): their parts move from where they rest.
        internal static List<Holdable> Rests(GameObject root, AnimationClip clip, List<Holdable> lends)
        {
            var keyed = new HashSet<string>(StringComparer.Ordinal);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip)) keyed.Add(binding.path);
            var rests = new List<Holdable>();
            foreach (Holdable holdable in lends)
                if (!keyed.Contains(AnimationUtility.CalculateTransformPath(holdable.transform, root.transform))) rests.Add(holdable);
            return rests;
        }

        // The holdables under root that clip moves: a curve on it, or under it, whose keys change.
        internal static List<Holdable> Lends(GameObject root, AnimationClip clip)
        {
            HashSet<string> paths = Moving(clip);
            var lends = new List<Holdable>();
            foreach (Holdable holdable in root.GetComponentsInChildren<Holdable>(true))
            {
                string own = AnimationUtility.CalculateTransformPath(holdable.transform, root.transform);
                foreach (string path in paths)
                    if (path == own || path.StartsWith(own + "/", StringComparison.Ordinal))
                    {
                        lends.Add(holdable);
                        break;
                    }
            }
            return lends;
        }

        // Each holdable's path from root (the clip's root), as a clip's curves name it.
        internal static List<string> Paths(GameObject root, List<Holdable> holdables)
        {
            var paths = new List<string>();
            foreach (Holdable holdable in holdables)
                if (holdable != null) paths.Add(AnimationUtility.CalculateTransformPath(holdable.transform, root.transform));
            return paths;
        }
    }
}
