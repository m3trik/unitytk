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
                                  color.HasValue ? h => Set(h, target, color.Value, ctx) : (Func<Highlight, bool>)null, ctx);
        }

        // The Highlight's colour set to color; null: the one a dry run would add, at its default
        // colour, asked on its node, which a plan names as the one the run adds (by its object).
        static bool Set(Highlight highlight, GameObject node, Color color, OpContext ctx)
        {
            if ((highlight != null ? highlight.color : Highlight.DefaultColor) == color) return false;
            UnityEngine.Object asked = highlight != null ? (UnityEngine.Object)highlight : node;
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
                if (ctx == null || ctx.ShouldCreate(OpContextEdits.Key(target, typeof(T)), $"add a {typeof(T).Name}"))
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
        /// Put a <see cref="Highlight"/> on <paramref name="node"/> at full intensity (pulsing
        /// where <paramref name="pulse"/>), switched off unless <paramref name="startEnabled"/>: it
        /// glows while something enables it (a sequence's step, as it runs). One that has a
        /// Highlight gets nothing.
        /// </summary>
        /// <param name="node">The subtree root to light: every renderer under it, a scene object.</param>
        /// <param name="pulse">Swing the glow on its own; off: a steady glow (a light turned on).</param>
        /// <param name="startEnabled">Make it glowing from the start; false: off until something enables it.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The Highlight; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static Highlight AddHighlight(GameObject node, bool pulse = true, bool startEnabled = false, OpContext ctx = null)
        {
            InScene(node);
            Highlight existing = node.GetComponent<Highlight>();
            string key = OpContextEdits.Key(node, typeof(Highlight));
            if (existing != null || ctx.Planned("create", key)) return existing;
            if (ctx != null && !ctx.ShouldCreate(key, pulse ? "add a pulsing highlight" : "add a steady glow"))
                return null;
            Highlight highlight = ctx.AddComponent<Highlight>(node);
            highlight.intensity = 1f;
            highlight.pulse = pulse;
            highlight.enabled = startEnabled;
            ctx.Edited(highlight);
            return highlight;
        }

        /// <summary>
        /// Have <paramref name="glow"/> pulse as long as it is on (<paramref name="loop"/>), or swing once and hold
        /// steady (<see cref="Highlight.loop"/>). One so already gets nothing.
        /// </summary>
        /// <param name="glow">The highlight (null: planned, nothing to set).</param>
        /// <param name="loop">Pulse as long as it is on.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetLoop(Highlight glow, bool loop, OpContext ctx = null)
        {
            if (glow == null || glow.loop == loop) return false;
            if (ctx != null && !ctx.ShouldChange(glow, loop ? "pulse as long as it is on" : "pulse once, then glow steady")) return true;
            glow.loop = loop;
            ctx.Edited(glow);
            return true;
        }

        /// <summary>
        /// Have <paramref name="glow"/> swing on its own (<paramref name="pulse"/>) or glow steady
        /// (<see cref="Highlight.pulse"/>). One so already gets nothing.
        /// </summary>
        /// <param name="glow">The highlight (null: planned, nothing to set).</param>
        /// <param name="pulse">Swing the glow on its own; off: a steady glow.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetPulse(Highlight glow, bool pulse, OpContext ctx = null)
        {
            if (glow == null || glow.pulse == pulse) return false;
            if (ctx != null && !ctx.ShouldChange(glow, pulse ? "pulse" : "glow steady")) return true;
            glow.pulse = pulse;
            ctx.Edited(glow);
            return true;
        }

        /// <summary>
        /// Have <paramref name="glow"/> glow in <paramref name="color"/> (<see cref="Highlight.color"/>, its dim colour
        /// with it: <see cref="Highlight.DimOf"/>). One so already gets nothing.
        /// </summary>
        /// <param name="glow">The highlight (null: planned, nothing to set).</param>
        /// <param name="color">Its colour at intensity 1.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetColor(Highlight glow, Color color, OpContext ctx = null)
        {
            if (glow == null) return false;
            Color dim = Highlight.DimOf(color);
            if (glow.color == color && glow.colorDim == dim) return false;
            if (ctx != null && !ctx.ShouldChange(glow, "glow in its colour")) return true;
            glow.color = color;
            glow.colorDim = dim;
            ctx.Edited(glow);
            return true;
        }

        /// <summary>Have <paramref name="fade"/> take <paramref name="seconds"/> (<see cref="FadeTo.seconds"/>). One so already gets nothing.</summary>
        /// <param name="fade">The fade (null: planned, nothing to set).</param>
        /// <param name="seconds">How long it takes; under 0 is 0.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetSeconds(FadeTo fade, float seconds, OpContext ctx = null)
        {
            seconds = Mathf.Max(0f, seconds);
            if (fade == null || Mathf.Approximately(fade.seconds, seconds)) return false;
            if (ctx != null && !ctx.ShouldChange(fade, "fade in its time")) return true;
            fade.seconds = seconds;
            ctx.Edited(fade);
            return true;
        }

        /// <summary>
        /// Put a <see cref="FadeTo"/> on <paramref name="node"/>, switched off unless
        /// <paramref name="startEnabled"/>, that fades its <see cref="Fade"/> (added where it has
        /// none, starting at the other end: hidden for a fade in) to <paramref name="alpha"/> each
        /// time something enables it (a sequence's step, as it starts or ends). One fading to
        /// <paramref name="alpha"/> already gets nothing.
        /// </summary>
        /// <param name="node">The subtree root to fade, a scene object.</param>
        /// <param name="alpha">The opacity it fades to: 0 a fade out, 1 a fade in.</param>
        /// <param name="seconds">How long it takes (default: the effect recipe's fade, <see cref="FadeTo.DefaultSeconds"/>).</param>
        /// <param name="startEnabled">Make it fading as the scene starts; false: off until something enables it.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The FadeTo; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static FadeTo AddFadeTo(GameObject node, float alpha, float seconds = FadeTo.DefaultSeconds, bool startEnabled = false,
                                       OpContext ctx = null)
        {
            InScene(node);
            alpha = Mathf.Clamp01(alpha);
            string key = $"{OpContextEdits.Key(node, typeof(FadeTo))} {alpha.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            foreach (FadeTo fading in node.GetComponents<FadeTo>())
                if (Mathf.Approximately(fading.alpha, alpha)) return fading;
            if (ctx.Planned("create", key)) return null;
            string fadeKey = OpContextEdits.Key(node, typeof(Fade));
            if (node.GetComponent<Fade>() == null && !ctx.Planned("create", fadeKey))
            {
                if (ctx == null || ctx.ShouldCreate(fadeKey, "add a Fade"))
                {
                    Fade fade = ctx.AddComponent<Fade>(node);
                    fade.alpha = 1f - alpha;
                    ctx.Edited(fade);
                }
            }
            if (ctx != null && !ctx.ShouldCreate(key, alpha > 0f ? "fade it in when enabled" : "fade it out when enabled")) return null;
            FadeTo added = ctx.AddComponent<FadeTo>(node);
            added.alpha = alpha;
            added.seconds = Mathf.Max(0f, seconds);
            added.enabled = startEnabled;
            ctx.Edited(added);
            return added;
        }

        const string ExemptAction = "leave it out of the effects above it";

        /// <summary>
        /// Leave <paramref name="node"/>, and what is under it, out of the effects above it (an <see cref="EffectExempt"/>
        /// on it): what is put under a part to point it out shows while the part is hidden and keeps its own look while
        /// the part glows. One left out already gets nothing.
        /// </summary>
        /// <param name="node">A scene object: an arrow's or a ring's, under a part.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The exemption; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static EffectExempt Exempt(GameObject node, OpContext ctx = null)
        {
            InScene(node);
            if (node.TryGetComponent(out EffectExempt existing)) return existing;
            string key = OpContextEdits.Key(node, typeof(EffectExempt));
            if (ctx.Planned("create", key)) return null;
            if (ctx != null && !ctx.ShouldCreate(key, ExemptAction)) return null;
            return ctx.AddComponent<EffectExempt>(node);
        }

        /// <summary>
        /// <see cref="Exempt(GameObject, OpContext)"/> on <paramref name="node"/>, an object a dry run only planned: the
        /// exemption its live run makes, planned once; nothing in a live run.
        /// </summary>
        /// <param name="node">The planned object.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to make it on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static EffectExempt Exempt(PlannedObject node, OpContext ctx = null)
        {
            ctx.PlanCreate(OpContextEdits.Key(node, typeof(EffectExempt)), ExemptAction);
            return null;
        }

        /// <summary>
        /// Put an <see cref="EmissionStateSwitch"/> on <paramref name="track"/>'s object, switched
        /// off unless <paramref name="startEnabled"/>, that switches the track to
        /// <paramref name="state"/> each time something enables it (a sequence's step, as it
        /// starts). One switching to <paramref name="state"/> already gets nothing.
        /// </summary>
        /// <param name="track">The material's emission track (an import makes one per material with states), a scene object's.</param>
        /// <param name="state">One of the track's state names.</param>
        /// <param name="startEnabled">Switch as the scene starts; false: off until something enables it.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The switch; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
        /// <exception cref="ArgumentException">Its object is not in a scene, or the track has no state <paramref name="state"/> (its states named).</exception>
        public static EmissionStateSwitch AddStateSwitch(EmissionTrack track, string state, bool startEnabled = false,
                                                         OpContext ctx = null)
        {
            if (track == null) throw new ArgumentNullException(nameof(track), "name an Emission Track");
            InScene(track.gameObject);
            if (!track.states.Exists(s => string.Equals(s.name, state, StringComparison.Ordinal)))
                throw new ArgumentException($"'{track.name}' has no emission state '{state}'; it has " +
                                            (track.states.Count > 0 ? string.Join(", ", track.states.ConvertAll(s => s.name)) : "none"),
                                            nameof(state));
            foreach (EmissionStateSwitch existing in track.GetComponents<EmissionStateSwitch>())
                if (string.Equals(existing.state, state, StringComparison.Ordinal)) return existing;
            string key = $"{OpContextEdits.Key(track.gameObject, typeof(EmissionStateSwitch))} {state}";
            if (ctx.Planned("create", key)) return null;
            if (ctx != null && !ctx.ShouldCreate(key, $"switch it to '{state}' when enabled")) return null;
            EmissionStateSwitch added = ctx.AddComponent<EmissionStateSwitch>(track.gameObject);
            added.state = state;
            added.enabled = startEnabled;
            ctx.Edited(added);
            return added;
        }

        /// <summary>
        /// The Emission Track that shows <paramref name="node"/>'s emission: the track in its hierarchy whose
        /// material a renderer at or under it wears (a render effect's twin of it counts as it). What a step
        /// that switches an indicator's state switches (<see cref="AddStateSwitch"/>).
        /// </summary>
        /// <param name="node">A scene object: an indicator's lens, or a group holding it.</param>
        /// <returns>The track; null where no material it wears has one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static EmissionTrack TrackOf(GameObject node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name a scene object");
            var worn = new HashSet<Material>();
            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) worn.Add(EffectWriter.Authored(material));
            foreach (EmissionTrack track in node.transform.root.GetComponentsInChildren<EmissionTrack>(true))
                if (track.material != null && worn.Contains(track.material)) return track;
            return null;
        }

        /// <summary>The name of the object a track made for <paramref name="material"/> is on (<see cref="AddEmissionTrack(GameObject, OpContext)"/>).</summary>
        /// <param name="material">The material's name.</param>
        public static string TrackName(string material) => $"{material} (emission)";

        /// <summary>
        /// The material <paramref name="node"/> shows its emission with, where no <see cref="EmissionTrack"/> plays one
        /// yet: the first a renderer at or under it wears that emits (an emission colour above black, its map
        /// counted with it). What <see cref="AddEmissionTrack(GameObject, OpContext)"/> gives a track.
        /// </summary>
        /// <param name="node">A scene object.</param>
        /// <returns>The material; null where nothing it wears emits.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static Material Emitting(GameObject node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name a scene object");
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
                foreach (Material worn in renderer.sharedMaterials)
                {
                    Material material = worn == null ? null : EffectWriter.Authored(worn);
                    if (material != null && MaterialWriter.EmissionOf(material, host).maxColorComponent > 0f) return material;
                }
            return null;
        }

        /// <summary>
        /// Give <paramref name="node"/>'s emission a track where the delivery brought none: an
        /// <see cref="EmissionTrack"/> on an object of its own under the node's root (<see cref="TrackName"/>), playing
        /// the material the node emits with (<see cref="Emitting"/>) as it stands (its own colour, at full weight), with
        /// the states the authoring tool gives an indicator that has none of its own (<see cref="EmissionStateDefaults"/>):
        /// off, green, blue, amber, red where a state can colour it; off and on where it can only dim it (its emission
        /// is mapped). So a part someone gave an emissive material by hand switches by name like one an emissive group
        /// made. A track that shows the node already (<see cref="TrackOf"/>) is the one returned, unchanged.
        /// </summary>
        /// <param name="node">A scene object: an indicator's lens, or a group holding it.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The track; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException">Its object is not in a scene, or nothing it wears emits.</exception>
        public static EmissionTrack AddEmissionTrack(GameObject node, OpContext ctx = null)
        {
            InScene(node);
            EmissionTrack found = TrackOf(node);
            if (found != null) return found;
            Material material = Emitting(node);
            if (material == null)
                throw new ArgumentException($"'{node.name}' wears no material that emits: give one an emission colour or map", nameof(node));
            Transform root = node.transform.root;
            string name = TrackName(material.name);
            string key = new PlannedObject(name, root).Path;
            if (ctx.Planned("create", key)) return null;
            if (ctx != null && !ctx.ShouldCreate(key, TrackAction)) return null;
            var holder = new GameObject(name);
            holder.transform.SetParent(root, false);
            ctx.Created(holder);
            EmissionTrack track = ctx.AddComponent<EmissionTrack>(holder);
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            track.material = material;
            track.color = MaterialColor.Encode(MaterialWriter.EmissionOf(material, host));
            track.strength = 1f;
            track.tint = Color.white;
            track.weight = 1f;
            track.states.AddRange(DefaultStates(material));
            ctx.Edited(track);
            return track;
        }

        /// <summary>
        /// The object <see cref="AddEmissionTrack(GameObject, OpContext)"/> makes for <paramref name="node"/>, as a dry
        /// run plans it: for the actions that add to the track it only planned.
        /// </summary>
        /// <param name="node">A scene object that emits and has no track.</param>
        /// <returns>The planned object; null where a track shows the node already, or nothing it wears emits.</returns>
        public static PlannedObject PlannedTrack(GameObject node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name a scene object");
            if (TrackOf(node) != null) return null;
            Material material = Emitting(node);
            return material == null ? null : new PlannedObject(TrackName(material.name), node.transform.root);
        }

        /// <summary>The states a track made for <paramref name="material"/> has (<see cref="EmissionStateDefaults"/>): those that colour it, or those that only dim it where its emission is mapped.</summary>
        /// <param name="material">A material that emits.</param>
        public static EmissionState[] DefaultStates(Material material)
            => MaterialWriter.EmissionMapped(material, RenderPipelineHosts.Current) ? EmissionStateDefaults.Weighted : EmissionStateDefaults.Tinted;

        /// <summary>
        /// <see cref="AddStateSwitch(EmissionTrack, string, bool, OpContext)"/> on <paramref name="track"/>, a track a dry
        /// run only planned (<see cref="PlannedTrack"/>): the switch its live run adds, planned once; nothing in a live run.
        /// </summary>
        /// <param name="track">The planned track's object.</param>
        /// <param name="state">The state's name.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the switch now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
        public static bool AddStateSwitch(PlannedObject track, string state, OpContext ctx = null)
        {
            if (track == null) throw new ArgumentNullException(nameof(track), "name the planned track");
            return ctx.PlanCreate($"{track.Path}/{nameof(EmissionStateSwitch)} {state}", $"switch it to '{state}' when enabled");  // the key its live run asks by
        }

        const string TrackAction = "an emission track for the material it emits with, with the default states";

        static void InScene(GameObject node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name a scene object");
            if (!node.scene.IsValid()) throw new ArgumentException($"'{node.name}' is not in a scene (a prefab asset?): name its instance", nameof(node));
        }

        /// <summary>
        /// Make the twins the render effects at or under <paramref name="root"/> can ask for
        /// -- per material, each state its Fades, Highlights and emission tracks combine to
        /// -- in a <see cref="MaterialTwins"/> asset, and give it to every effect there that
        /// has none of its own, so a player build keeps the shader variants the twins draw
        /// with and the runtime only instances them. A twin already made and still the
        /// material's is left; one its material has changed since is made again. A table already
        /// at <paramref name="assetPath"/> is the one it adds to. A dry run counts the Fades and
        /// Highlights it planned (<see cref="AddHighlight"/>, <see cref="AddFadeTo"/>) as made, so it
        /// plans the twins and the hand-over its live run makes for them; nothing above an
        /// <see cref="EffectExempt"/> reaches past it.
        /// </summary>
        /// <param name="root">A scene object or a prefab of the project's own (a model's own effects are its import's).</param>
        /// <param name="assetPath">Where the table is written (or the one there added to) when no effect there has one; null:
        /// beside the scene or prefab, as <c>&lt;root&gt; Twins.asset</c>.</param>
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
            Dictionary<Transform, TwinState> unmade = Unmade(root.transform, ctx);
            if (effects.Count == 0 && unmade.Count == 0) return 0;
            TwinState On(Transform node) => TwinPlan.OwnStates(node) | (unmade.TryGetValue(node, out TwinState more) ? more : 0);

            MaterialTwins table = null;
            foreach (RenderEffect effect in effects)
                if (effect.twins != null && Writable(effect.twins)) { table = effect.twins; break; }
            int count = 0;
            var wrote = new HashSet<string>(StringComparer.Ordinal);  // the asset files it changed, saved at the end
            string path = table != null ? AssetDatabase.GetAssetPath(table) : assetPath ?? TablePath(root);
            if (table == null) table = AssetDatabase.LoadAssetAtPath<MaterialTwins>(path);  // an earlier run's: added to, never a second beside it
            const string WriteTable = "write the twins' table";
            bool planned = table != null || ctx.Planned("write", path, WriteTable);  // the table exists, or its write is asked once
            var losses = new List<string>();
            foreach (var (material, state) in TwinPlan.Needed(root.transform, On))
            {
                if (Covered(effects, table, material, state, out Material held) && !Writable(held)) continue;
                Material wanted = MaterialWriter.Twin(material, state, host, losses);
                if (wanted == null) continue;
                try
                {
                    if (held != null && MaterialWriter.Equivalent(held, wanted)) continue;
                    if (held == null && ctx.Planned("write", path, $"add the twin '{wanted.name}'")) continue;  // this dry run's already
                    count++;
                    if (!planned)
                    {
                        planned = true;
                        if (ctx == null || ctx.ShouldWrite(path, WriteTable))
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
            // The effects with no table of their own get this one (in a dry run, the one it would write), in one order
            // whether made or only planned: by their object's path, then their kind.
            if (planned)
            {
                string give = $"give it the twins '{Path.GetFileNameWithoutExtension(path)}'";
                var takers = new List<(string at, string kind, RenderEffect effect, GameObject node)>();
                foreach (RenderEffect effect in effects)
                    if (effect.twins == null) takers.Add((OpContextEdits.Label(effect.gameObject), effect.GetType().Name, effect, null));
                foreach (var pair in unmade)
                {
                    if ((pair.Value & TwinState.Blended) != 0) takers.Add((OpContextEdits.Label(pair.Key.gameObject), nameof(Fade), null, pair.Key.gameObject));
                    if ((pair.Value & TwinState.Glowing) != 0) takers.Add((OpContextEdits.Label(pair.Key.gameObject), nameof(Highlight), null, pair.Key.gameObject));
                }
                takers.Sort((a, b) =>
                {
                    int by = string.CompareOrdinal(a.at, b.at);
                    return by != 0 ? by : string.CompareOrdinal(a.kind, b.kind);
                });
                foreach (var (_, _, effect, node) in takers)
                {
                    count++;
                    if (effect == null)
                    {
                        ctx.ShouldChange(node, give);  // a dry run's: the effect is only planned, named as its live run names it
                        continue;
                    }
                    if ((ctx == null || ctx.ShouldChange(effect, give)) && table != null)
                    {
                        effect.twins = table;
                        ctx.Edited(effect);
                        if (EditorUtility.IsPersistent(effect)) wrote.Add(AssetDatabase.GetAssetPath(effect));  // a prefab of the project's own
                    }
                }
            }
            foreach (string loss in new HashSet<string>(losses)) ctx.Raise(LossCode, loss, location: owner);
            // What it wrote, and no other asset: another's unsaved edit is the person's to save.
            foreach (string written in wrote) AssetDatabase.SaveAssetIfDirty(AssetDatabase.GUIDFromAssetPath(written));
            return count;
        }

        // Whether a table one of the effects holds, or table, already has a twin of material in state.
        static bool Covered(List<RenderEffect> effects, MaterialTwins table, Material material, TwinState state, out Material held)
        {
            foreach (RenderEffect effect in effects)
            {
                held = effect.twins != null ? effect.twins.Template(material, state) : null;
                if (held != null) return true;
            }
            held = table != null ? table.Template(material, state) : null;
            return held != null;
        }

        // The Fades and Highlights a dry run planned at or under root and has not made (AddFadeTo, AddHighlight): the
        // states each node would give what is under it. None in a live run, which made them.
        static Dictionary<Transform, TwinState> Unmade(Transform root, OpContext ctx)
        {
            var unmade = new Dictionary<Transform, TwinState>();
            if (ctx == null || !ctx.DryRun) return unmade;
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                TwinState states = 0;
                if (node.GetComponent<Fade>() == null && ctx.Planned("create", OpContextEdits.Key(node.gameObject, typeof(Fade))))
                    states |= TwinState.Blended;
                if (node.GetComponent<Highlight>() == null && ctx.Planned("create", OpContextEdits.Key(node.gameObject, typeof(Highlight))))
                    states |= TwinState.Glowing;
                if (states != 0) unmade[node] = states;
            }
            return unmade;
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
