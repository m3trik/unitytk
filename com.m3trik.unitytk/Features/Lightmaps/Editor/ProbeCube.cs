using Unity.Collections;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace M3trik.Unitytk.Lightmaps
{
    /// <summary>
    /// The cubemap a delivery's reflection probe reads, made from its equirectangular
    /// panorama by Unity's own texture pipeline (<see cref="TextureGenerator"/>: the
    /// Latitude-Longitude layout, Specular convolution for glossy reflections, trilinear,
    /// linear, compressed for the platform as Unity compresses an HDR cube), as a
    /// sub-asset the model's import owns.
    /// </summary>
    /// <remarks>
    /// Unity reads a lat-long panorama in its own orientation: +Z at the image centre,
    /// +X at three quarters, the top row +Y (u = 0.5 + atan2(x, z) / 2pi, measured on
    /// 2022.3 and 6000.3). The record states u = 0.5 + atan2(z, x) / 2pi in the file's
    /// axes, and the FBX import mirrors X: together a quarter turn, no flip, so column c
    /// of Unity's panorama is column c + W/4 of the record's. The pixels are turned so
    /// before Unity makes the cube.
    /// </remarks>
    internal static class ProbeCube
    {
        /// <summary>
        /// The cube <paramref name="panorama"/> (a readable linear texture) makes, named
        /// <paramref name="name"/>, in a project of the colour space <paramref name="space"/>,
        /// and the light it gives a surface facing each way (<paramref name="ambient"/>,
        /// <see cref="Ambient"/>); null, with why in <paramref name="why"/>, where it cannot.
        /// </summary>
        internal static Cubemap Make(Texture2D panorama, string name, ColorSpace space, out float[] ambient, out string why)
        {
            why = null;
            ambient = System.Array.Empty<float>();
            if (!panorama.isReadable)
            {
                why = "it is not imported readable (Read/Write on, the first import a delivery gives it)";
                return null;
            }
            int w = panorama.width, h = panorama.height;
            Color[] turned = Turned(panorama.GetPixels(), w, h, space);
            ambient = Ambient(turned, w, h);

            var settings = new TextureGenerationSettings(TextureImporterType.Default);
            TextureImporterSettings texture = settings.textureImporterSettings;
            texture.textureShape = TextureImporterShape.TextureCube;
            texture.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
            texture.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
            texture.sRGBTexture = false;
            texture.mipmapEnabled = true;
            texture.filterMode = FilterMode.Trilinear;
            texture.readable = false;
            settings.textureImporterSettings = texture;
            settings.sourceTextureInformation = new SourceTextureInformation { width = w, height = h, hdr = true, containsAlpha = false };
            TextureImporterPlatformSettings platform = settings.platformSettings;
            platform.textureCompression = TextureImporterCompression.Compressed;
            settings.platformSettings = platform;
            settings.enablePostProcessor = false;
            TextureGenerationOutput output;
            using (var pixels = new NativeArray<Color>(turned, Allocator.Temp))
                output = TextureGenerator.GenerateTexture(settings, pixels);
            // A cube is the output (the 2D texture field is null for one).
            if (!(output.output is Cubemap cube))
            {
                if (output.output != null) Object.DestroyImmediate(output.output);
                why = "Unity's texture pipeline made no cubemap of it" +
                      (output.importWarnings != null && output.importWarnings.Length > 0 ? $" ({string.Join("; ", output.importWarnings)})" : "");
                return null;
            }
            cube.name = name;
            return cube;
        }

        /// <summary>
        /// The light a panorama in Unity's layout (<see cref="Turned"/>) gives a surface facing
        /// each way, as spherical harmonics (L2) in the order
        /// <see cref="UnityEngine.Rendering.SphericalHarmonicsL2"/> keeps them: 27 values, each
        /// colour channel's nine coefficients in turn. What a scene's ambient probe holds, so
        /// a part no lightmap lights is lit by the room the panorama shows.
        /// </summary>
        /// <remarks>
        /// The radiance is projected onto the harmonics exactly (averaged over at most 64 x 32
        /// blocks first, which keeps a small bright light's energy): Unity's
        /// <c>AddDirectionalLight</c> doubles its intensity and scales by 16/17 to hold a
        /// single light's peak to its colour, so each block goes in at 17/32 of its
        /// irradiance over pi. Measured against the cosine-weighted integral on a production
        /// room: within 5% facing the ceiling lights and the walls, 20% low facing the floor
        /// (what a second-order fit loses).
        /// </remarks>
        /// <param name="turned">The panorama's pixels, linear, in Unity's layout, rows bottom to top.</param>
        /// <param name="w">Its width.</param>
        /// <param name="h">Its height.</param>
        internal static float[] Ambient(Color[] turned, int w, int h)
        {
            int across = Mathf.Min(64, w), down = Mathf.Min(32, h);
            var light = new UnityEngine.Rendering.SphericalHarmonicsL2();
            for (int by = 0; by < down; by++)
            {
                int y0 = by * h / down, y1 = Mathf.Max(y0 + 1, (by + 1) * h / down);
                double polar = System.Math.PI * (1.0 - (by + 0.5) / down);  // from +Y: the top row is up
                double solid = (2.0 * System.Math.PI / across) * (System.Math.PI / down) * System.Math.Sin(polar);
                for (int bx = 0; bx < across; bx++)
                {
                    int x0 = bx * w / across, x1 = Mathf.Max(x0 + 1, (bx + 1) * w / across);
                    float r = 0f, g = 0f, b = 0f;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            Color c = turned[y * w + x];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                        }
                    float n = (y1 - y0) * (x1 - x0);
                    double around = ((bx + 0.5) / across - 0.5) * 2.0 * System.Math.PI;  // +Z at the centre, +X at three quarters
                    var from = new Vector3((float)(System.Math.Sin(around) * System.Math.Sin(polar)), (float)System.Math.Cos(polar),
                                           (float)(System.Math.Cos(around) * System.Math.Sin(polar)));
                    light.AddDirectionalLight(from, new Color(r / n, g / n, b / n), (float)(solid / System.Math.PI * 17.0 / 32.0));
                }
            }
            var values = new float[27];
            for (int channel = 0; channel < 3; channel++)
                for (int i = 0; i < 9; i++) values[channel * 9 + i] = light[channel, i];
            return values;
        }

        /// <summary>
        /// The record's panorama pixels <paramref name="source"/> (<paramref name="w"/> by
        /// <paramref name="h"/>, bottom row first) in Unity's layout: linear, and a quarter
        /// turn on, so column c holds the record's column c + w / 4 (blended where w is no
        /// multiple of 4). A Gamma-space project stores an EXR gamma-encoded on import
        /// (measured): <paramref name="space"/> Gamma takes the bake's linear values back.
        /// </summary>
        internal static Color[] Turned(Color[] source, int w, int h, ColorSpace space)
        {
            var linear = (Color[])source.Clone();
            if (space == ColorSpace.Gamma)
                for (int i = 0; i < linear.Length; i++) linear[i] = linear[i].linear;
            var turned = new Color[linear.Length];
            float shift = w * 0.25f;
            int whole = Mathf.FloorToInt(shift);
            float part = shift - whole;  // a width that is not a multiple of 4
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    Color a = linear[row + (x + whole) % w];
                    turned[row + x] = part > 0f ? Color.LerpUnclamped(a, linear[row + (x + whole + 1) % w], part) : a;
                }
            }
            return turned;
        }
    }
}
