using UnityEngine;

namespace M3trik.Unitytk.Looks
{
    /// <summary>
    /// The display transforms a delivery's look names (its handoff record's <c>renderer.toneMapping</c>,
    /// three.js's tone mappings by name): how a linear scene colour, at an exposure, shows on a display, and
    /// the same as a lookup table a render pipeline shows its cameras through
    /// (<see cref="Kernel.IRenderPipelineHost.Display"/>). A look beyond these is a table of its own
    /// (<see cref="SceneLook.table"/>), in the same layout.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own tone mappers are not three.js's. URP's ACES is the reference transform,
    /// darker in the shadows than three.js r169's (Stephen Hill's fit, its input scaled by exposure / 0.6)
    /// that a hand-off is approved under: grey 0.18 shows at 0.37 against 0.50 (sRGB), 1.0 at 0.95 against
    /// 0.89; HDRP's custom curve is HDRP's alone. A table of the named curve, shown through the pipeline's
    /// own colour lookup, is the approved curve exactly, in any pipeline that has one.
    /// </remarks>
    public static class LookCurve
    {
        /// <summary>three.js's ACESFilmicToneMapping (r169): Stephen Hill's fit, its input scaled by exposure / 0.6.</summary>
        public const string AcesFilmic = "ACESFilmic";

        /// <summary>three.js's LinearToneMapping: the colour times the exposure, clamped.</summary>
        public const string Linear = "Linear";

        /// <summary>three.js's NoToneMapping: the colour as it is, clamped; the exposure plays no part.</summary>
        public const string None = "None";

        /// <summary>The tone mappings it shows, as a sentence names them.</summary>
        public static string Names => $"{AcesFilmic}, {Linear}, {None}";

        /// <summary>The brightest scene light a table spans by default: past it every curve here has flattened.</summary>
        public const float Brightest = 16f;

        /// <summary>Whether <paramref name="toneMapping"/> is one it shows.</summary>
        /// <param name="toneMapping">A tone mapping's name.</param>
        public static bool Knows(string toneMapping) => toneMapping == AcesFilmic || toneMapping == Linear || toneMapping == None;

        /// <summary>How the linear scene colour <paramref name="linear"/> shows, display-linear in [0, 1].</summary>
        /// <param name="linear">The scene's colour, linear light.</param>
        /// <param name="toneMapping">The tone mapping (<see cref="Knows"/>); one it does not know shows as <see cref="None"/>.</param>
        /// <param name="exposure">The exposure the scene is shown at (three.js's toneMappingExposure).</param>
        public static Color Show(Color linear, string toneMapping, float exposure)
        {
            float e = Mathf.Max(0f, exposure);
            Vector3 c = new Vector3(linear.r, linear.g, linear.b);
            if (toneMapping == AcesFilmic) c = Aces(c * (e / 0.6f));
            else if (toneMapping == Linear) c *= e;
            return new Color(Mathf.Clamp01(c.x), Mathf.Clamp01(c.y), Mathf.Clamp01(c.z), linear.a);
        }

        /// <summary>
        /// <see cref="Show"/> as a strip lookup table: <paramref name="size"/> x <paramref name="size"/> wide and
        /// <paramref name="size"/> high, blue across the strips, an sRGB input (the scene's light over
        /// <paramref name="span"/>) to the sRGB colour it shows as. Never saved; its maker destroys it.
        /// </summary>
        /// <param name="toneMapping">The tone mapping.</param>
        /// <param name="exposure">The exposure.</param>
        /// <param name="size">The table's size per channel: the one the pipeline reads (<see cref="Kernel.IRenderPipelineHost.DisplaySize"/>).</param>
        /// <param name="span">The brightest scene light it spans (at least 1): a shown look scales the scene's light by its inverse.</param>
        public static Texture2D Table(string toneMapping, float exposure, int size, float span = Brightest)
        {
            span = Mathf.Max(1f, span);
            var table = new Texture2D(size * size, size, TextureFormat.RGBAHalf, false, true)
            {
                name = $"{toneMapping} at {exposure}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color[size * size * size];
            float step = 1f / (size - 1);
            for (int b = 0; b < size; b++)
                for (int g = 0; g < size; g++)
                    for (int r = 0; r < size; r++)
                    {
                        var scene = new Color(Mathf.GammaToLinearSpace(r * step) * span, Mathf.GammaToLinearSpace(g * step) * span,
                                              Mathf.GammaToLinearSpace(b * step) * span, 1f);
                        Color shown = Show(scene, toneMapping, exposure);
                        pixels[g * size * size + b * size + r] = new Color(Mathf.LinearToGammaSpace(shown.r), Mathf.LinearToGammaSpace(shown.g),
                                                                           Mathf.LinearToGammaSpace(shown.b), 1f);
                    }
            table.SetPixels(pixels);
            table.Apply(false, false);
            return table;
        }

        // three.js r169's ACESFilmicToneMapping past its exposure: sRGB to AP1 (with the RRT's
        // saturation), Hill's fit of the RRT and ODT, back to sRGB.
        static Vector3 Aces(Vector3 c)
        {
            c = new Vector3(0.59719f * c.x + 0.35458f * c.y + 0.04823f * c.z,
                            0.07600f * c.x + 0.90834f * c.y + 0.01566f * c.z,
                            0.02840f * c.x + 0.13383f * c.y + 0.83777f * c.z);
            c = new Vector3(Fit(c.x), Fit(c.y), Fit(c.z));
            return new Vector3(1.60475f * c.x - 0.53108f * c.y - 0.07367f * c.z,
                               -0.10208f * c.x + 1.10813f * c.y - 0.00605f * c.z,
                               -0.00327f * c.x - 0.07276f * c.y + 1.07602f * c.z);
        }

        static float Fit(float v) => (v * (v + 0.0245786f) - 0.000090537f) / (v * (0.983729f * v + 0.4329510f) + 0.238081f);
    }
}
