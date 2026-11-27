using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Lightmaps.URP.Tests")]

namespace M3trik.Unitytk.Lightmaps.URP
{
    /// <summary>
    /// Baked Lit (<see cref="ShaderName"/>), made on import from the project's own URP Lit
    /// (<see cref="Lit"/>): the door's stub asset (<see cref="Stub"/>) imports as Lit renamed, its forward pass
    /// given the reflection rule's material keyword (<see cref="LightmapsImporter.ReflectByBakeKeyword"/>) and the
    /// door's reroute of its one call to <c>UniversalFragmentPBR</c> (<c>URP/BakedLit.hlsl</c>), before
    /// <c>LitForwardPass.hlsl</c>. Every other line is Lit's as installed, so every property, keyword, pass and its
    /// inspector are Lit's, a material keeps its values moving onto it, and the SRP Batcher draws it as it draws Lit
    /// (the reroute adds no material property). It is made again whenever Lit or the reroute changes; a Lit laid
    /// out otherwise (no anchor it inserts at) fails the import, naming the file and URP's version.
    /// </summary>
    /// <remarks>
    /// Prior art: Shader Graph's importer, a <c>ScriptedImporter</c> that writes a shader's source and makes it with
    /// <c>ShaderUtil.CreateShaderAsset(context, source, false)</c>, the call that records the includes it reads as the
    /// import's dependencies. A copy of Lit shipped in the package was the earlier form: URP's code in this package,
    /// pinned to one URP version by drift tests. A Shader Graph over Lit's target was weighed and not taken: a graph
    /// cannot reroute the forward pass's lighting call, and its Lit is not URP's Lit material for material.
    /// </remarks>
    [ScriptedImporter(Version, Extension, ImportQueueOffset)]
    internal sealed class BakedLitImporter : ScriptedImporter
    {
        /// <summary>Bump when what the import makes of Lit changes.</summary>
        const int Version = 2;

        /// <summary>
        /// Where its import stands in the queue: ahead of the models whose import puts their materials on it, which a
        /// scripted importer otherwise follows (measured: a delivery imported first, found no Baked Lit, and was
        /// imported again once it was made).
        /// </summary>
        const int ImportQueueOffset = -1000;

        /// <summary>The stub's extension: the asset this importer makes Baked Lit of.</summary>
        internal const string Extension = "bakedlit";

        /// <summary>The door's stub asset, which imports as Baked Lit (its main object the shader).</summary>
        internal const string Stub = "Packages/com.m3trik.unitytk/Features/Lightmaps/URP/BakedLit.bakedlit";

        /// <summary>URP's Lit as the project installs it: what Baked Lit is made of.</summary>
        internal const string Lit = "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader";

        /// <summary>The shader's name.</summary>
        internal const string ShaderName = "unitytk/Baked Lit";

        /// <summary>The door's reroute, included in the forward pass.</summary>
        internal const string Reroute = "Packages/com.m3trik.unitytk/Features/Lightmaps/URP/BakedLit.hlsl";

        // Where it inserts: Lit's name, its forward pass and that pass's include of LitForwardPass.hlsl.
        const string LitName = "Universal Render Pipeline/Lit";
        const string ForwardPass = "Name \"ForwardLit\"";
        const string ForwardInclude = "#include \"Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl\"";

        /// <inheritdoc/>
        public override void OnImportAsset(AssetImportContext ctx)
        {
            ctx.DependsOnSourceAsset(Lit);
            string made = Derive(File.ReadAllText(Path.GetFullPath(Lit)), out string missing);
            if (made == null)
            {
                ctx.LogImportError(Refusal(missing, UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Lit)?.version));
                return;
            }
            Shader shader = ShaderUtil.CreateShaderAsset(ctx, made, false);
            ctx.AddObjectToAsset("shader", shader);
            ctx.SetMainObject(shader);
        }

        /// <summary>
        /// Baked Lit's source, made of <paramref name="lit"/> (Lit.shader's text): renamed, and its forward pass given
        /// the rule's keyword and the reroute before its include of <c>LitForwardPass.hlsl</c>; null where an anchor is
        /// not found, <paramref name="missing"/> naming it.
        /// </summary>
        internal static string Derive(string lit, out string missing)
        {
            missing = null;
            var name = new Regex("^(\\s*Shader\\s+)\"" + Regex.Escape(LitName) + "\"", RegexOptions.Multiline);
            if (!name.IsMatch(lit))
            {
                missing = $"its name (Shader \"{LitName}\")";
                return null;
            }
            string made = name.Replace(lit, "$1\"" + ShaderName + "\"", 1);
            int pass = made.IndexOf(ForwardPass, System.StringComparison.Ordinal);
            int end = pass < 0 ? -1 : made.IndexOf("ENDHLSL", pass, System.StringComparison.Ordinal);
            int include = pass < 0 ? -1 : made.IndexOf(ForwardInclude, pass, System.StringComparison.Ordinal);
            if (pass < 0 || include < 0 || include > end)
            {
                missing = pass < 0 ? $"its forward pass ({ForwardPass})" : $"its forward pass's {ForwardInclude}";
                return null;
            }
            int line = made.LastIndexOf('\n', include) + 1;
            string indent = made.Substring(line, include - line);
            string newline = made.Contains("\r\n") ? "\r\n" : "\n";
            string inserted = $"#pragma shader_feature_local_fragment {LightmapsImporter.ReflectByBakeKeyword}{newline}" +
                              $"{indent}#include \"{Reroute}\"{newline}{indent}";
            return made.Insert(include, inserted);
        }

        /// <summary>The import's error where <paramref name="missing"/> is not in Lit (URP <paramref name="version"/>).</summary>
        internal static string Refusal(string missing, string version)
            => $"Baked Lit is made of URP's Lit ({Lit}, URP {version ?? "unknown"}), and {missing} is not in it: this URP lays " +
               "Lit out otherwise, so no Baked Lit is made and a lightmapped material keeps URP's Lit. Update " +
               "BakedLitImporter for it (the Lightmaps feature's URP door).";
    }
}
