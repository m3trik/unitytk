using System;
using System.Linq;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Looks
{
    /// <summary>
    /// The Looks feature's actions: what a person's menu item and an agent's code
    /// do to a project. Each takes an optional <see cref="OpContext"/>: a dry run
    /// plans, a menu's run is one undo step, and none just runs.
    /// </summary>
    /// <remarks>
    /// Prior art: a URP project's look is a Volume with a profile asset a person builds by hand
    /// (Tonemapping, Color Adjustments), and the profile keeps a component only once it is saved
    /// inside the asset; Unity's tone mappers are not the approved hand-off's (<see cref="LookCurve"/>).
    /// A delivery's look is data its handoff record states, so it rides the model as a component
    /// (<see cref="SceneLook"/>) that shows it through the pipeline's own seam
    /// (<see cref="IRenderPipelineHost.Display"/>), made fresh each time, nothing saved to lose.
    /// </remarks>
    public static class LooksActions
    {
        /// <summary>
        /// Give <paramref name="root"/> the look <paramref name="toneMapping"/> at <paramref name="exposure"/>
        /// (<see cref="SceneLook"/>), or through a lookup table of its own: the game cameras
        /// <paramref name="options"/> names show the scene through it while it is enabled. A
        /// <paramref name="table"/>, where given, wins: it takes the tone mapping's place, a tone mapping given with
        /// it kept behind it (shown once the table is taken off; none given: the look's own, a new one's default), and
        /// the exposure scales the scene into whichever shows. One it has is set to these values; one already showing
        /// them is left as it is.
        /// </summary>
        /// <param name="root">The object it goes on: a delivery's model, or anything in the scene.</param>
        /// <param name="toneMapping">The tone mapping, by three.js's name (<see cref="LookCurve.Names"/>); null with a
        /// <paramref name="table"/>: the look's own.</param>
        /// <param name="exposure">The exposure the scene shows at, before tone mapping (or before its table).</param>
        /// <param name="table">A look of its own, in the tone mapping's place: a strip lookup table as the pipeline's
        /// colour lookup reads it (<see cref="SceneLook.table"/>); null: the tone mapping.</param>
        /// <param name="span">The brightest scene light the look spans (at least 1).</param>
        /// <param name="options">Its cameras, its weight over other looks and the effects it quiets
        /// (<see cref="DisplayOptions"/>); null: its defaults, over everything on every game camera, bloom and
        /// vignette off.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The look; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="root"/> is not in a scene, or the tone mapping is not one it
        /// shows (none given and no <paramref name="table"/>).</exception>
        public static SceneLook SetLook(GameObject root, string toneMapping, float exposure, Texture2D table = null,
                                        float span = LookCurve.Brightest, DisplayOptions options = null, OpContext ctx = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root), "name what the look goes on");
            if (!root.scene.IsValid()) throw new ArgumentException($"'{root.name}' is not in a scene (a prefab asset?): name its instance", nameof(root));
            if (!(toneMapping == null && table != null) && !LookCurve.Knows(toneMapping))
                throw new ArgumentException($"'{toneMapping}' is not a tone mapping it shows: name one of {LookCurve.Names}" +
                                            (table == null ? ", or give a table of its own" : ""), nameof(toneMapping));
            exposure = Mathf.Max(0f, exposure);
            span = Mathf.Max(1f, span);
            options = options ?? new DisplayOptions();  // its defaults
            SceneLook look = root.GetComponent<SceneLook>();
            string kept = toneMapping ?? (look != null ? look.toneMapping : LookCurve.AcesFilmic);  // a table's: the look's own behind it
            string through = table == null ? toneMapping : toneMapping != null ? $"its table '{table.name}' (in place of {toneMapping})"
                                                                               : $"its table '{table.name}'";
            string what = $"show the scene through {through} at exposure {exposure}";
            bool fresh = look == null;  // made now: set up as made, not asked again
            if (fresh)
            {
                string key = OpContextEdits.Key(root, typeof(SceneLook));
                if (ctx.Planned("create", key)) return null;
                if (ctx != null && !ctx.ShouldCreate(key, what)) return null;
                look = ctx.AddComponent<SceneLook>(root);
            }
            else if (Shows(look, kept, exposure, table, span, options)) return look;
            else if (ctx != null && !ctx.ShouldChange(look, what)) return look;
            Fill(look, kept, exposure, table, span, options);
            ctx.Edited(look);
            return look;
        }

        // Whether look holds these values already.
        static bool Shows(SceneLook look, string toneMapping, float exposure, Texture2D table, float span, DisplayOptions options)
            => look.toneMapping == toneMapping && look.exposure == exposure && look.table == table && look.span == span
               && look.priority == options.priority && look.bloomOff == options.bloomOff && look.vignetteOff == options.vignetteOff
               && look.cameras.SequenceEqual(options.cameras ?? Array.Empty<Camera>());

        // Set look's values (those left out: a look's defaults) and show it again with them.
        internal static void Fill(SceneLook look, string toneMapping, float exposure, Texture2D table = null,
                                  float span = LookCurve.Brightest, DisplayOptions options = null)
        {
            options = options ?? new DisplayOptions();
            look.toneMapping = toneMapping;
            look.exposure = Mathf.Max(0f, exposure);
            look.table = table;
            look.span = Mathf.Max(1f, span);
            look.priority = options.priority;
            look.bloomOff = options.bloomOff;
            look.vignetteOff = options.vignetteOff;
            look.cameras = (Camera[])(options.cameras ?? Array.Empty<Camera>()).Clone();
            look.Refresh();
        }
    }
}
