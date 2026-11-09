using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// The Render Effects feature's actions, in Unity's terms: key a fade or a pulse on a
    /// clip, and prepare the twins a hierarchy's effects instance. Each takes an optional
    /// <see cref="OpContext"/>: a dry run plans what the run makes, a menu's run is one undo
    /// step, and none just runs. A second run of each with the same arguments changes nothing.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's Animation window, which keys one value at a time onto a
    /// component's field (its binding path from the Animator above, its tangents set by
    /// <see cref="AnimationUtility"/>); these key a whole fade or pulse in one step onto the
    /// same field, as the window would, so the clip reads in the window as it keyed it.
    /// </remarks>
    public static class RenderEffectsActions
    {
        /// <summary>The issue raised for a material whose twin cannot show part of its effect.</summary>
        public const string LossCode = "render_effects.loss";
        /// <summary>The issue raised where no render-pipeline host serves the pipeline.</summary>
        public const string NoHostCode = "render_effects.no_host";

        /// <summary>
        /// Key a fade on <paramref name="target"/>'s <see cref="Fade"/> (added where it has
        /// none) in <paramref name="clip"/>: its alpha from 1 at <paramref name="start"/> to 0
        /// at <paramref name="end"/> (seconds from the clip's start), or the other way for
        /// <see cref="FadeDirection.In"/>, linear, any keys between the two replaced. The
        /// curve is bound from the Animator above <paramref name="target"/>, as the
        /// Animation window binds it.
        /// </summary>
        /// <param name="target">The subtree root to fade: every renderer under it, a scene object (in a scene,
        /// or a prefab open in Prefab Mode).</param>
        /// <param name="clip">A clip of the project's own (an imported clip is its model's: duplicate it first).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>Whether it changed the clip or added the Fade (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="clip"/> is null.</exception>
        /// <exception cref="ArgumentException">No Animator is above <paramref name="target"/>, the clip is a model's,
        /// <paramref name="target"/> is in a prefab asset, or <paramref name="end"/> is not after
        /// <paramref name="start"/>.</exception>
        public static bool KeyFade(GameObject target, AnimationClip clip, float start, float end,
                                   FadeDirection direction = FadeDirection.Out, OpContext ctx = null)
        {
            float from = direction == FadeDirection.Out ? 1f : 0f;
            return Key<Fade>(target, clip, nameof(Fade.alpha), start, end,
                             new[] { new Keyframe(start, from), new Keyframe(end, 1f - from) }, null, ctx);
        }

        /// <summary>
        /// Key a pulse on <paramref name="target"/>'s <see cref="Highlight"/> (added where it
        /// has none) in <paramref name="clip"/>: its intensity from 0 up to 1 and back every
        /// <paramref name="period"/> seconds from <paramref name="start"/> to
        /// <paramref name="end"/>, linear, any keys between the two replaced; its colour set
        /// to <paramref name="color"/> when given. For a pulse that runs on its own, unkeyed,
        /// turn on the Highlight's <see cref="Highlight.pulse"/> instead.
        /// </summary>
        /// <param name="color">The glow at full intensity, as the colour field stores it; null keeps the Highlight's.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>Whether it changed the clip or the Highlight (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="clip"/> is null.</exception>
        /// <exception cref="ArgumentException">No Animator is above <paramref name="target"/>, the clip is a model's,
        /// <paramref name="target"/> is in a prefab asset, <paramref name="end"/> is not after <paramref name="start"/>,
        /// or <paramref name="period"/> is not above 0.</exception>
        public static bool KeyPulse(GameObject target, AnimationClip clip, float start, float end, float period,
                                    Color? color = null, OpContext ctx = null)
        {
            if (!(period > 0f)) throw new ArgumentException("a pulse's period is a time above 0 seconds", nameof(period));
            var keys = new List<Keyframe>();
            float half = period / 2f;
            int n = 0;
            for (float t = start; t < end - 1e-5f; t = start + half * ++n)
                keys.Add(new Keyframe(t, n % 2 == 0 ? 0f : 1f));
            keys.Add(new Keyframe(end, 0f));
            return Key<Highlight>(target, clip, nameof(Highlight.intensity), start, end, keys.ToArray(),
                                  color.HasValue ? h => Set(h, target.name, color.Value, ctx) : (Func<Highlight, bool>)null, ctx);
        }

        // The Highlight's colour set to color; null: the one a dry run would add, at its default
        // colour, named as the run names the one it adds (its object's name).
        static bool Set(Highlight highlight, string name, Color color, OpContext ctx)
        {
            if ((highlight != null ? highlight.color : Highlight.DefaultColor) == color) return false;
            object asked = highlight != null ? (object)highlight : name;
            if ((ctx == null || ctx.ShouldChange(asked, $"set its highlight colour to {color}")) && highlight != null)
            {
                highlight.color = color;
                ctx.Edited(highlight);
            }
            return true;
        }

        static bool Key<T>(GameObject target, AnimationClip clip, string field, float start, float end, Keyframe[] keys,
                           Func<T, bool> also, OpContext ctx) where T : RenderEffect
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (!(end > start)) throw new ArgumentException($"the end ({end} s) is not after the start ({start} s)", nameof(end));
            string owner = AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrEmpty(owner) && AssetImporter.GetAtPath(owner) is ModelImporter)
                throw new ArgumentException($"'{clip.name}' is imported with '{owner}', whose next import rewrites it: duplicate " +
                                            "the clip into the project (Ctrl+D) and key that, or key the effect at the source",
                                            nameof(clip));
            // A prefab asset's object would take the component in memory, unsaved until some later save writes it
            // (measured: the asset holds it after the next AssetDatabase.SaveAssets, and no undo takes it back).
            if (EditorUtility.IsPersistent(target))
                throw new ArgumentException($"'{target.name}' is in the prefab asset '{AssetDatabase.GetAssetPath(target)}': open " +
                                            "the prefab (Prefab Mode) and key it there, or key an instance of it in a scene",
                                            nameof(target));
            Animator animator = target.GetComponentInParent<Animator>(true);
            if (animator == null)
                throw new ArgumentException($"no Animator is above '{target.name}' to play the clip: add one to the object " +
                                            "that plays it", nameof(target));
            bool changed = false;
            T effect = target.GetComponent<T>();
            if (effect == null)
            {
                changed = true;
                if (ctx == null || ctx.ShouldCreate($"{target.name}/{typeof(T).Name}", $"add a {typeof(T).Name}"))
                    effect = ctx.AddComponent<T>(target);
            }
            // The component's own change: on the one it adds, or in a dry run the one it would (none yet).
            if (also != null) changed |= also(effect);
            var binding = EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(target.transform, animator.transform),
                                                        typeof(T), field);
            AnimationCurve was = AnimationUtility.GetEditorCurve(clip, binding);
            AnimationCurve curve = Keyed(was, start, end, keys);
            if (was != null && Same(was, curve)) return changed;
            if (ctx == null || ctx.ShouldChange(clip, $"key {typeof(T).Name}.{field} from {start} s to {end} s"))
            {
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                EditorUtility.SetDirty(clip);
            }
            return true;
        }

        // The curve with every key from start to end replaced by keys, linear, the rest kept.
        static AnimationCurve Keyed(AnimationCurve was, float start, float end, Keyframe[] keys)
        {
            var curve = new AnimationCurve();
            if (was != null)
                foreach (Keyframe key in was.keys)
                    if (key.time < start - 1e-5f || key.time > end + 1e-5f) curve.AddKey(key);
            foreach (Keyframe key in keys) curve.AddKey(key);
            for (int i = 0; i < curve.length; i++)
            {
                float time = curve.keys[i].time;
                if (time < start - 1e-5f || time > end + 1e-5f) continue;
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        static bool Same(AnimationCurve a, AnimationCurve b)
        {
            if (a.length != b.length) return false;
            for (int i = 0; i < a.length; i++)
            {
                Keyframe x = a.keys[i], y = b.keys[i];
                if (!Near(x.time, y.time) || !Near(x.value, y.value) || !Near(x.inTangent, y.inTangent)
                    || !Near(x.outTangent, y.outTangent))
                    return false;
            }
            return true;
        }

        // Equal within float noise, a stepped key's infinite tangent equal to itself.
        static bool Near(float a, float b) => a == b || Mathf.Approximately(a, b);

        /// <summary>
        /// Make the twins the render effects at or under <paramref name="root"/> can ask for
        /// -- per material, each state its Fades, Highlights and emission tracks combine to
        /// -- in a <see cref="MaterialTwins"/> asset, and give it to every effect there that
        /// has none of its own, so a player build keeps the shader variants the twins draw
        /// with and the runtime only instances them. A twin already made and still the
        /// material's is left; one its material has changed since is made again.
        /// </summary>
        /// <param name="root">A scene object or a prefab of the project's own (a model's own effects are its import's).</param>
        /// <param name="assetPath">Where the table is written when no effect there has one; null: beside the scene or
        /// prefab, as <c>&lt;root&gt; Twins.asset</c>.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them. The table and its
        /// twins are files it writes (an undo does not take them back, and a menu's run names them); giving the
        /// effects the table is a scene change, undone with the run.</param>
        /// <returns>How many twins it made or remade, and effects it gave the table (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="root"/> is an imported model's own.</exception>
        public static int PrepareMaterials(GameObject root, string assetPath = null, OpContext ctx = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            string owner = AssetDatabase.GetAssetPath(root);
            if (!string.IsNullOrEmpty(owner) && AssetImporter.GetAtPath(owner) is ModelImporter)
                throw new ArgumentException($"'{root.name}' is '{owner}''s own: its import makes its twins (reimport it)", nameof(root));
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            if (host == null)
            {
                ctx.Raise(NoHostCode, $"no unitytk render-pipeline host serves the {RenderPipelineHosts.NameOf(null)} pipeline, " +
                                      "so no twins can be made: its fades, highlights and emission tracks show only as hidden at 0");
                return 0;
            }
            var effects = new List<RenderEffect>(root.GetComponentsInChildren<RenderEffect>(true));
            foreach (EmissionTrack track in TwinPlan.Tracks(root.transform))
                if (!effects.Contains(track)) effects.Add(track);
            if (effects.Count == 0) return 0;

            MaterialTwins table = null;
            foreach (RenderEffect effect in effects)
                if (effect.twins != null && Writable(effect.twins)) { table = effect.twins; break; }
            int count = 0;
            var wrote = new HashSet<string>(StringComparer.Ordinal);  // the asset files it changed, saved at the end
            string path = table != null ? AssetDatabase.GetAssetPath(table) : assetPath ?? TablePath(root);
            bool planned = table != null;  // the table exists, or its write is asked once
            var losses = new List<string>();
            foreach (var (material, state) in TwinPlan.Needed(root.transform))
            {
                if (Covered(effects, material, state, out Material held) && !Writable(held)) continue;
                Material wanted = MaterialWriter.Twin(material, state, host, losses);
                if (wanted == null) continue;
                try
                {
                    if (held != null && MaterialWriter.Equivalent(held, wanted)) continue;
                    count++;
                    if (!planned)
                    {
                        planned = true;
                        if (ctx == null || ctx.ShouldWrite(path, "write the twins' table"))
                        {
                            path = AssetDatabase.GenerateUniqueAssetPath(path);
                            table = ScriptableObject.CreateInstance<MaterialTwins>();
                            table.name = Path.GetFileNameWithoutExtension(path);
                            AssetDatabase.CreateAsset(table, path);
                            wrote.Add(path);
                        }
                    }
                    if (held != null)
                    {
                        if (ctx == null || ctx.ShouldWrite(AssetDatabase.GetAssetPath(held), $"make '{held.name}' again from '{material.name}'"))
                        {
                            string name = held.name;
                            EditorUtility.CopySerialized(wanted, held);
                            held.name = name;
                            EditorUtility.SetDirty(held);
                            wrote.Add(AssetDatabase.GetAssetPath(held));
                        }
                    }
                    else if ((ctx == null || ctx.ShouldWrite(path, $"add the twin '{wanted.name}'")) && table != null)
                    {
                        AssetDatabase.AddObjectToAsset(wanted, table);
                        table.twins.Add(new MaterialTwins.Twin { original = material, state = state, template = wanted });
                        EditorUtility.SetDirty(table);
                        wrote.Add(AssetDatabase.GetAssetPath(table));
                        wanted = null;  // the table's now
                    }
                }
                finally
                {
                    if (wanted != null && !EditorUtility.IsPersistent(wanted)) UnityEngine.Object.DestroyImmediate(wanted);
                }
            }
            // The effects with no table of their own get this one (in a dry run, the one it would write).
            if (planned)
                foreach (RenderEffect effect in effects)
                {
                    if (effect.twins != null) continue;
                    count++;
                    if ((ctx == null || ctx.ShouldChange(effect, $"give it the twins '{Path.GetFileNameWithoutExtension(path)}'"))
                        && table != null)
                    {
                        effect.twins = table;
                        ctx.Edited(effect);
                        if (EditorUtility.IsPersistent(effect)) wrote.Add(AssetDatabase.GetAssetPath(effect));  // a prefab of the project's own
                    }
                }
            foreach (string loss in new HashSet<string>(losses)) ctx.Raise(LossCode, loss, location: owner);
            // What it wrote, and no other asset: another's unsaved edit is the person's to save.
            foreach (string written in wrote) AssetDatabase.SaveAssetIfDirty(AssetDatabase.GUIDFromAssetPath(written));
            return count;
        }

        // Whether a table one of the effects holds already has a twin of material in state.
        static bool Covered(List<RenderEffect> effects, Material material, TwinState state, out Material held)
        {
            foreach (RenderEffect effect in effects)
            {
                held = effect.twins != null ? effect.twins.Template(material, state) : null;
                if (held != null) return true;
            }
            held = null;
            return false;
        }

        // A table or twin a person's action may write: the project's own, not a model's import.
        static bool Writable(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && !(AssetImporter.GetAtPath(path) is ModelImporter);
        }

        static string TablePath(GameObject root)
        {
            string beside = root.scene.IsValid() && !string.IsNullOrEmpty(root.scene.path)
                ? root.scene.path
                : AssetDatabase.GetAssetPath(root);
            string folder = string.IsNullOrEmpty(beside) ? "Assets" : Path.GetDirectoryName(beside).Replace('\\', '/');
            return $"{folder}/{root.name} Twins.asset";
        }
    }
}
