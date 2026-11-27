using System.Collections.Generic;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Kernel;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Indicators.Tests")]

namespace M3trik.Unitytk.Indicators
{
    // The material a part this feature draws itself (an arrow, a ring) wears: a new one on the render pipeline's own Lit,
    // in a colour and glowing in it. Its maker destroys it.
    internal static class PlainMaterial
    {
        // A new material named name on host's Lit (null host: the current pipeline's), in color, glowing glow times it
        // (Paint); null, said once, where no host serves the pipeline, or the project (a player: its build) holds no such shader. The
        // shader by its host's name, as the material writer finds it, holds in a player too, where a primitive's material
        // is not the pipeline's.
        internal static Material Of(string name, Color color, float glow = 0f, IRenderPipelineHost host = null)
        {
            if (host == null) host = RenderPipelineHosts.Current;
            Shader lit = host != null ? Shader.Find(host.LitShader) : null;
            if (lit == null)
            {
                Warn(host == null
                    ? $"no render-pipeline host serves the {RenderPipelineHosts.NameOf(null)} pipeline, so '{name}' has no material and is not drawn as it should be"
                    : $"the {host.Name} pipeline's Lit ('{host.LitShader}') is not in this project or build, so '{name}' has no material " +
                      "and is not drawn as it should be: include the shader (a material of the project's on it, or Always Included Shaders)");
                return null;
            }
            var made = new Material(lit) { name = name };
            Paint(made, color, glow, host);
            return made;
        }

        // Colour material (its base colour, by the names of host's Lit and Unlit; null host: the current one) and, where
        // glow is above 0, have it glow in that colour times glow, its emission switched on as the pipeline's Lit reads it
        // (MaterialWriter.SwitchEmission) and none of it baked into a lightmap. A slot its shader lacks is left out.
        internal static void Paint(Material material, Color color, float glow = 0f, IRenderPipelineHost host = null)
        {
            if (material == null) return;
            LitTable table = (host ?? RenderPipelineHosts.Current)?.Lit;
            if (table == null) return;
            foreach (string property in new[] { table.BaseColor, table.MetaColor, table.UnlitColor })
                if (Has(material, property)) material.SetColor(property, color);
            if (glow <= 0f || !Has(material, table.EmissionColor)) return;
            MaterialWriter.SwitchEmission(material, table, Has(material, table.EmissionMap) && material.GetTexture(table.EmissionMap) != null);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.SetColor(table.EmissionColor, color * glow);
        }

        static bool Has(Material material, string property) => property != null && material.HasProperty(property);

        // Each refusal said once a session (a part is made again each time it shows).
        static readonly HashSet<string> _warned = new HashSet<string>();

        // Unity's: with domain reload off as play mode starts, a static outlives edit mode: each is said again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _warned.Clear();

        static void Warn(string message)
        {
            if (_warned.Add(message)) Debug.LogWarning("Indicators: " + message + ".");
        }
    }
}
