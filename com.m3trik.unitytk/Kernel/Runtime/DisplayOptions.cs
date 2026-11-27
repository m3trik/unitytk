using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// How a display transform is shown (<see cref="IRenderPipelineHost.Display"/>): its weight over a project's
    /// own look, the effects it quiets while shown, and the cameras it turns post-processing on for. Its
    /// defaults are a delivery's approved look shown whole: over everything, bloom and vignette off, on every
    /// game camera.
    /// </summary>
    public sealed class DisplayOptions
    {
        /// <summary>
        /// Its weight over the project's own looks (URP: its volume's priority): the highest shows. Two shown at
        /// one weight blend as the pipeline orders them: give one a higher weight.
        /// </summary>
        public float priority = float.MaxValue;

        /// <summary>Switch bloom off while it is shown (a project's own bloom stays where false).</summary>
        public bool bloomOff = true;

        /// <summary>Switch the vignette off while it is shown (a project's own vignette stays where false).</summary>
        public bool vignetteOff = true;

        /// <summary>
        /// The cameras it turns post-processing on for, each given back as it is put away; null or empty: every
        /// game camera, those there now and each one later. A camera already post-processing shows it either way.
        /// </summary>
        public Camera[] cameras;
    }
}
