// ----------------------------------------------------------------------------
// The Lightmaps feature's editor part (M3trik.Unitytk.Lightmaps.Editor): the import
// of a delivery's lightmaps record into a LightmapSet on the model's root, built of
// references (the renderers, the map textures, the probe), so the runtime never
// reads the record. The record is collected off the carrier's user properties; the
// maps and the probe's panorama ride inside the FBX and are asked of the carrier's
// extraction (IModelMedia), each given its import settings on its first import
// (LightmapsTextureRoles); nothing here writes the model's import settings.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Entry = LightmapRecord.Shape.Entry;
using Payload = LightmapRecord.Shape.Payload;

[assembly: ReadsRecord(UnitytkRecords.Lightmaps, typeof(LightmapRecord))]
[assembly: ReadsRecord(UnitytkRecords.Handoff, typeof(HandoffBlock))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Lightmaps.Tests")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Lightmaps.URP.Tests")]

namespace M3trik.Unitytk.Lightmaps
{
    /// <summary>
    /// The importer of a delivery's <c>lightmaps</c> record (<see cref="UnitytkRecords.Lightmaps"/>,
    /// read through <see cref="LightmapRecord"/> at its one version): it gives the
    /// model's root a <see cref="LightmapSet"/> holding each object's renderer -- found
    /// by name and hierarchy, namespace-tolerant (<see cref="RecordNames"/>), a tie left
    /// unbound and said -- with its map and rect, every map the record names (a texture
    /// imported as a lightmap, read through UV set 1, Unity's uv2, applied as stored:
    /// linear, any factor already folded in by the producer) and the bake's reflection
    /// probe: a native <see cref="ReflectionProbe"/>, custom, on a child of the root at
    /// the capture point, projected onto the record's box, in the record's units times
    /// its <c>unit_scale</c>, reading a cubemap the import makes of the record's
    /// panorama. The probe's panorama already holds the bake's highlights as the capture
    /// point sees them, so this reads neither the highlight copies nor the lights (the
    /// record's Unity types leave them out): Unity picks a renderer's probe by volume,
    /// never per renderer, and has no light that adds its specular alone. A file with no
    /// record, or one the records provider refuses (a v2 record: re-export it), gets no set.
    /// <para>The file's <c>handoff</c> record, where it carries one, sets how the set holds the scene
    /// (<see cref="Handoff"/>: its environment rule and level, its lights rule; a file with none leaves
    /// each at the set's own default), and, where it states its lightmapped materials, how each surface
    /// its maps light reflects (<see cref="Reflection"/>, <see cref="Reflect"/>): the reflection's scale is
    /// the delivery's own probe's intensity, which Unity hands each renderer it reaches per draw, and each
    /// material its maps light is put on the pipeline's shader for a baked surface (its door's: URP's Baked
    /// Lit, made on import from its Lit), reflecting by its bake where the rule says (a material keyword,
    /// <see cref="ReflectByBakeKeyword"/>). So two deliveries in one scene each draw their own. A file that
    /// states none keeps its materials on the pipeline's Lit, as does a pipeline with no such shader (Built-in).</para>
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own lightmapper bakes and binds its own lightmaps only, and
    /// keeps none it did not bake across a scene load (the set binds them again whenever
    /// it is enabled). Unity's FBX import extracts embedded media only on request, and
    /// imports a lightmap it did not bake as a colour texture; the carrier's extraction
    /// and the texture roles answer both. The cube is made by Unity's own texture pipeline
    /// (<c>TextureGenerator</c>), which convolves it as a cube texture's import does.
    /// </remarks>
    public class LightmapsImporter : AssetPostprocessor
    {
        const string Tag = "Lightmaps";

        /// <summary>Bump to reimport every model when what the import makes changes.</summary>
        const uint VERSION = 8;

        /// <inheritdoc/>
        public override uint GetVersion() => VERSION;

        /// <summary>The name of the child of the model's root holding the bake's reflection probe.</summary>
        public const string ProbeName = "Reflection Probe";

        /// <summary>
        /// The material keyword of a surface that reflects by its bake (the record's <c>reflectionNormalization</c>
        /// lightmapLuma), on the shader a pipeline's door draws a set's surfaces with (URP's Baked Lit): its
        /// reflection scaled per texel by the bake over the light its probe gives it about its normal.
        /// </summary>
        internal const string ReflectByBakeKeyword = "_UNITYTK_REFLECT_BY_BAKE";

        static IRecordSource Records => Provided.One<IRecordSource>();
        static IModelMedia Media => Provided.One<IModelMedia>();

        // This model's import, OnPreprocessModel to OnPostprocessModel (one postprocessor
        // per import): the records collected off its carrier.
        readonly Dictionary<string, string> _collected = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _handoff = new Dictionary<string, string>(StringComparer.Ordinal);

        void OnPreprocessModel()
        {
            _collected.Clear();
            _handoff.Clear();
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!Imports.IsDelivery(assetPath)) return;
            if (Records.TryGetRecord(names, values, UnitytkRecords.Lightmaps, out string json))
                Records.Collect(_collected, assetPath, go, json);
            if (Records.TryGetRecord(names, values, UnitytkRecords.Handoff, out string handoff))
                Records.Collect(_handoff, assetPath, go, handoff);
        }

        void OnPostprocessModel(GameObject root)
        {
            _collected.TryGetValue(assetPath, out string json);
            _handoff.TryGetValue(assetPath, out string handoff);
            _collected.Clear();
            _handoff.Clear();
            if (json == null || !Imports.IsDelivery(assetPath)) return;
            LightmapSet set = Assemble(root, json, assetPath, Records, Media,
                                       (id, made) => context.AddObjectToAsset(id, made),
                                       path => context.DependsOnSourceAsset(path), PlayerSettings.colorSpace,
                                       Imports.Frame(assetImporter as ModelImporter));
            HandoffBlock.Shape.Payload record = set != null && handoff != null
                ? Records.Read(UnitytkRecords.Handoff, handoff, HandoffBlock.CurrentVersion, HandoffBlock.Shape.Payload.Read, Tag, assetPath)
                : null;
            Handoff(set, record);
            if (set != null && Reflection(record, assetPath, out bool byBake, out float scale))
            {
                RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;  // one pipeline: its host and its door's shader
                Reflect(set, byBake, scale, assetPath, RenderPipelineHosts.For(pipeline), BakedLit(pipeline),
                        path => context.DependsOnArtifact(path));
            }
            // The DCC supplies the lightmap UVs on the mesh's second set; Unity's own
            // generation would overwrite them: said, never overridden.
            if (set != null && assetImporter is ModelImporter mi && mi.generateSecondaryUV)
                ImportLog.Warning(assetPath, "'Generate Lightmap UVs' is enabled: Unity overwrites the DCC's lightmap UVs " +
                                             "(the second UV set) with its own. Turn it off on this model.");
        }

        /// <summary>
        /// The shader the render pipeline <paramref name="pipeline"/> (null: none) draws a set's lightmapped surfaces
        /// with, by the asset path its door names (<see cref="ILightmapRenderers.BakedLit"/>: URP's Baked Lit); null where
        /// no door names one (Built-in): their materials stay on its Lit.
        /// </summary>
        internal static string BakedLit(RenderPipelineAsset pipeline)
        {
            foreach (ILightmapRenderers door in Provided.All<ILightmapRenderers>())
                if (door.BakedLit(pipeline) is string path) return path;
            return null;
        }

        /// <summary>
        /// Give <paramref name="set"/>'s surfaces the reflection rule its record states (<see cref="Reflection"/>): its
        /// scale <paramref name="scale"/> the set's probe's intensity, and, where the pipeline has a shader for a baked
        /// surface (at <paramref name="bakedLit"/>, its door's), each material its maps light on it, reflecting by its
        /// bake as <paramref name="byBake"/> says (<see cref="Baked"/>). The import depends on that shader
        /// (<paramref name="dependsOn"/>), so it runs again once the shader is made or remade. What it leaves undrawn is
        /// said: a scale with no probe to carry it, a shader not made, a project material, no material on the
        /// pipeline's Lit.
        /// </summary>
        /// <param name="set">The model's set.</param>
        /// <param name="byBake">Reflect by the bake (the record's <c>reflectionNormalization</c> lightmapLuma).</param>
        /// <param name="scale">The reflection's scale (its <c>envMapIntensity</c>).</param>
        /// <param name="assetPath">The model, as the import log names it.</param>
        /// <param name="host">The render pipeline's host.</param>
        /// <param name="bakedLit">The asset path of the pipeline's shader for a baked surface (<see cref="BakedLit(RenderPipelineAsset)"/>); null: none.</param>
        /// <param name="dependsOn">The import's dependency on an asset's import result, given that shader's path.</param>
        /// <returns>How many materials draw on that shader now.</returns>
        internal static int Reflect(LightmapSet set, bool byBake, float scale, string assetPath, IRenderPipelineHost host,
                                    string bakedLit, Action<string> dependsOn)
        {
            if (set.probe != null) set.probe.intensity = scale;
            else if (!Mathf.Approximately(scale, 1f))
                ImportLog.Warning(assetPath, $"lightmaps: the handoff scales its lightmapped materials' reflection by {scale}, and the " +
                                             "file has no reflection probe to carry it: they reflect the scene's environment at its own " +
                                             "level. Re-export it with its probe.");
            if (bakedLit == null || host == null) return 0;
            dependsOn(bakedLit);
            var baked = AssetDatabase.LoadAssetAtPath<Shader>(bakedLit);
            if (baked == null)
            {
                ImportLog.Warning(assetPath, $"lightmaps: the {host.Name} pipeline's shader for a baked surface ({bakedLit}) is not made, " +
                                             $"so the materials its maps light stay on {host.LitShader} and draw none of the handoff's " +
                                             "reflection rule: that shader's import says why. This model imports again once it is made.");
                return 0;
            }
            var projects = new List<string>();
            int count = Baked(set, host, baked, byBake, projects);
            if (projects.Count > 0)
                ImportLog.Warning(assetPath, $"lightmaps: {projects.Count} material(s) its maps light are project materials " +
                                             $"({string.Join(", ", projects)}), which a model's import does not write: put each on " +
                                             $"{baked.name}" + (byBake ? $" with its keyword {ReflectByBakeKeyword}" : "") +
                                             ", or remove the remap.");
            if (count > 0)
                ImportLog.Info(assetPath, $"lightmaps: {count} material(s) its maps light draw on {baked.name}, reflecting their " +
                                          $"probe at {scale}" + (byBake ? ", scaled by the bake" : ""));
            else if (projects.Count == 0 && Lit(set))
                ImportLog.Warning(assetPath, $"lightmaps: no material its maps light is on {host.LitShader}, so none draws the " +
                                             $"handoff's reflection rule ({baked.name}): put them on {host.LitShader} and reimport.");
            return count;
        }

        // Whether a receiver of set draws with any material.
        static bool Lit(LightmapSet set)
        {
            foreach (LightmapSet.Receiver receiver in set.receivers)
                if (receiver != null && receiver.renderer != null)
                    foreach (Material material in receiver.renderer.sharedMaterials)
                        if (material != null) return true;
            return false;
        }

        /// <summary>
        /// Put each material a receiver of <paramref name="set"/> draws with on <paramref name="baked"/>, the pipeline's
        /// shader for a baked surface, where it is on <paramref name="host"/>'s Lit and the host draws
        /// <paramref name="baked"/> (<see cref="RenderPipelineHosts.SwapLit"/>: its values and queue kept), reflecting by
        /// its bake as <paramref name="byBake"/> says (<see cref="ReflectByBakeKeyword"/>), each once. A material a renderer
        /// no map lights shares draws as before on that renderer (the shader changes only a lightmapped draw). A project
        /// material (one the model's import is remapped to, with an asset path of its own) is not the import's to write:
        /// it is left, its path added to <paramref name="projects"/>.
        /// </summary>
        /// <param name="set">The model's set; null: none.</param>
        /// <param name="host">The render pipeline's host.</param>
        /// <param name="baked">The shader for a baked surface; null: none.</param>
        /// <param name="byBake">Reflect by the bake (the keyword on), or not (off).</param>
        /// <param name="projects">Where each project material left is named; null: nowhere.</param>
        /// <returns>How many materials draw with it now.</returns>
        internal static int Baked(LightmapSet set, IRenderPipelineHost host, Shader baked, bool byBake, ICollection<string> projects = null)
        {
            if (set == null || host == null || baked == null) return 0;
            var done = new HashSet<Material>();
            foreach (LightmapSet.Receiver receiver in set.receivers)
            {
                if (receiver == null || receiver.renderer == null) continue;
                foreach (Material material in receiver.renderer.sharedMaterials)
                {
                    if (material == null || done.Contains(material)) continue;
                    string path = AssetDatabase.GetAssetPath(material);
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (projects != null && !projects.Contains(path)) projects.Add(path);
                        continue;
                    }
                    if (!RenderPipelineHosts.SwapLit(host, material, baked)) continue;
                    done.Add(material);
                    if (byBake) material.EnableKeyword(ReflectByBakeKeyword);
                    else material.DisableKeyword(ReflectByBakeKeyword);
                }
            }
            return done.Count;
        }

        /// <summary>
        /// Set <paramref name="set"/> as the file's <c>handoff</c> record <paramref name="record"/> states how it holds
        /// the scene (<see cref="HandoffRendering"/>; a setting it leaves out takes the policy's value): its environment
        /// rule (<c>environment.source</c> the probe) and level (<c>probeIntensity</c>), and its lights rule
        /// (<c>keyLight.disabledWhen</c> lightmapped). Null (no set, no record, or one refused): the set keeps its own
        /// defaults.
        /// </summary>
        internal static void Handoff(LightmapSet set, HandoffBlock.Shape.Payload record)
        {
            if (set == null || record == null) return;
            const string env = HandoffRendering.Environment.Section, key = HandoffRendering.KeyLight.Section;
            set.environment = HandoffBlock.States(record, env)
                              && HandoffBlock.Text(record, env, HandoffRendering.Environment.Source, HandoffRendering.Environment.SourceDefault) == "probe";
            set.environmentIntensity = Mathf.Max(0f, HandoffBlock.Number(record, env, HandoffRendering.Environment.ProbeIntensity,
                                                                         HandoffRendering.Environment.ProbeIntensityDefault));
            set.lightsOff = HandoffBlock.States(record, key)
                            && HandoffBlock.Text(record, key, HandoffRendering.KeyLight.DisabledWhen, HandoffRendering.KeyLight.DisabledWhenDefault) == "lightmapped";
        }

        /// <summary>
        /// The reflection rule the file's <c>handoff</c> record <paramref name="record"/> states for the surfaces its
        /// maps light (its <c>lightmappedMaterials</c>; a setting it leaves out takes the policy's value): whether they
        /// reflect by their bake (<paramref name="byBake"/>: <c>reflectionNormalization</c> lightmapLuma) and its scale
        /// (<paramref name="scale"/>: <c>envMapIntensity</c>). False where the record states no lightmapped materials (or
        /// there is none): their materials stay as they are. What the package does not draw is said (a warning for a
        /// value other than the ones it draws).
        /// </summary>
        internal static bool Reflection(HandoffBlock.Shape.Payload record, string assetPath, out bool byBake, out float scale)
        {
            byBake = false;
            scale = 1f;
            const string mats = HandoffRendering.LightmappedMaterials.Section;
            if (!HandoffBlock.States(record, mats)) return false;
            scale = Mathf.Max(0f, HandoffBlock.Number(record, mats, HandoffRendering.LightmappedMaterials.EnvMapIntensity,
                                                      HandoffRendering.LightmappedMaterials.EnvMapIntensityDefault));
            string normalization = HandoffBlock.Text(record, mats, HandoffRendering.LightmappedMaterials.ReflectionNormalization,
                                                     HandoffRendering.LightmappedMaterials.ReflectionNormalizationDefault);
            byBake = normalization == "lightmapLuma";
            var undrawn = new List<string>();
            if (normalization != "lightmapLuma" && normalization != "none") undrawn.Add($"reflectionNormalization '{normalization}'");
            string terms = HandoffBlock.Text(record, mats, HandoffRendering.LightmappedMaterials.EnvMapTerms,
                                             HandoffRendering.LightmappedMaterials.EnvMapTermsDefault);
            if (terms != "specular") undrawn.Add($"envMapTerms '{terms}' (a lightmapped surface reflects its environment's specular alone)");
            if (undrawn.Count > 0)
                ImportLog.Warning(assetPath, $"lightmaps: the handoff's lightmapped materials state what unitytk does not draw: " +
                                             $"{string.Join("; ", undrawn)}. They draw as its policy says.");
            return true;
        }

        /// <summary>
        /// The model under <paramref name="root"/> given the set the lightmaps record
        /// <paramref name="json"/> states (read through <paramref name="records"/>), its
        /// files found in <paramref name="media"/>'s folders and asked of its extraction,
        /// the probe's cubemap handed to <paramref name="add"/> (the import's sub-asset)
        /// and each file it reads to <paramref name="dependsOn"/>, in a project of the
        /// colour space <paramref name="space"/>, its lengths landed in <paramref name="frame"/>
        /// (<see cref="Imports.Frame"/>: the model importer's scale; the default frame when null).
        /// Returns the set, or null for no record, an empty one or one refused.
        /// </summary>
        internal static LightmapSet Assemble(GameObject root, string json, string assetPath, IRecordSource records,
                                             IModelMedia media, Action<string, UnityEngine.Object> add, Action<string> dependsOn,
                                             ColorSpace space, GltfFrame? frame = null)
        {
            Payload record = records.Read(UnitytkRecords.Lightmaps, json, LightmapRecord.CurrentVersion, Payload.Read, Tag, assetPath);
            if (record == null || record.objects == null || record.objects.Count == 0) return null;

            // The files ride inside the FBX: each noted for its first import, and asked of
            // the extraction in the one request a model version gets.
            var wanted = new List<string>();
            foreach (Entry entry in record.objects)
            {
                LightmapsTextureRoles.Note(entry.map, LightmapsTextureRoles.Role.Lightmap, assetPath);
                if (!wanted.Contains(entry.map)) wanted.Add(entry.map);
            }
            if (record.probe != null)
            {
                LightmapsTextureRoles.Note(record.probe.map, LightmapsTextureRoles.Role.Panorama, assetPath);
                wanted.Add(record.probe.map);
            }
            bool asked = media.RequestEmbeddedFiles(assetPath, wanted);
            string[] folders = media.MediaFolders(assetPath);

            var set = root.AddComponent<LightmapSet>();
            set.mode = LightmapsMode.NonDirectional;  // the maps are direction-free irradiance
            RecordNames.Match<Transform>[] matches = Receivers(
                root.transform, record.objects.ConvertAll(e => new RecordNames.Entry(e.name, e.hierarchy)));
            var missing = new List<string>();
            var unencoded = new List<string>();
            var noUv2 = new List<string>();
            int absent = 0;
            for (int i = 0; i < matches.Length; i++)
            {
                Entry entry = record.objects[i];
                RecordNames.Match<Transform> match = matches[i];
                if (match.Outcome == RecordNames.Outcome.Ambiguous)
                {
                    // Said once, on the import that binds (not the one that asks for the maps).
                    if (!asked) RecordNamesLog.Unbound(assetPath, $"lightmaps: '{entry.name}'", match.Outcome, match.Candidates,
                                                       "it is left unbound");
                    continue;
                }
                if (match.Outcome == RecordNames.Outcome.Absent)
                {
                    absent++;
                    continue;
                }
                Renderer receiver = match.Item.GetComponent<Renderer>();
                if (!HasLightmapUvs(receiver))
                {
                    noUv2.Add(entry.name);
                    continue;
                }
                string path = MediaFiles.Nearest(folders, entry.map);
                Texture2D map = path != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
                if (map == null)
                {
                    if (!missing.Contains(entry.map)) missing.Add(entry.map);
                    continue;
                }
                dependsOn(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.textureType != TextureImporterType.Lightmap
                    && !unencoded.Contains(path))
                    unencoded.Add(path);
                int index = set.maps.FindIndex(m => m.color == map);
                if (index < 0)
                {
                    set.maps.Add(new LightmapSet.Map { color = map });
                    index = set.maps.Count - 1;
                }
                set.receivers.Add(new LightmapSet.Receiver { renderer = receiver, map = index, scaleOffset = Rect(entry.scaleOffset) });
            }
            set.probe = record.probe != null
                ? Probe(root.transform, set, record, folders, assetPath, asked, add, dependsOn, space, frame ?? GltfFrame.Default)
                : null;

            if (!asked && noUv2.Count > 0)
                ImportLog.Warning(assetPath, $"lightmaps: {noUv2.Count} object(s) have no second UV set (uv2), which a " +
                                             $"lightmap is read through, and are left unbound: {string.Join(", ", noUv2)}. " +
                                             "Export with the lightmap UVs as the second set.");
            if (!asked && missing.Count > 0)
                ImportLog.Warning(assetPath, $"lightmaps: {missing.Count} map(s) the record names are not in the model's " +
                                             $"media: {string.Join(", ", missing)}; their objects are left unbound. Re-export " +
                                             "the file with its maps embedded.");
            if (!asked && unencoded.Count > 0)
                ImportLog.Warning(assetPath, $"lightmaps: {unencoded.Count} map(s) are not imported as lightmaps, so Unity " +
                                             $"does not encode them for the project's lightmap encoding: {string.Join(", ", unencoded)}. " +
                                             "Set each one's Texture Type to Lightmap in its import settings.");
            if (!asked && space != ColorSpace.Linear)
                ImportLog.Warning(assetPath, "lightmaps: the project's colour space is Gamma, and the maps are linear: they " +
                                             "read wrong. Switch to Linear (Project Settings > Player > Other Settings).");
            if (asked)
                ImportLog.Info(assetPath, "lightmaps: its embedded maps are asked of the extraction; the reimport that " +
                                          "follows binds them.");
            else
                ImportLog.Info(assetPath, $"lightmaps: a Lightmap Set on '{root.name}' binds {set.receivers.Count} of " +
                                          $"{record.objects.Count} object(s) to {set.maps.Count} map(s)" +
                                          (absent > 0 ? $" ({absent} not in this file)" : "") +
                                          (set.probe != null ? $", with the reflection probe '{record.probe.map}'." : "."));
            return set;
        }

        /// <summary>
        /// The probe the record states, on a child of <paramref name="root"/>
        /// (<see cref="ProbeName"/>): custom, its cubemap made of the record's panorama,
        /// at the capture point and projected onto the record's box (an open scene's,
        /// distant, reaching the model's bounds), lengths in the record's unit times its
        /// <c>unit_scale</c> landed as <paramref name="frame"/>'s import scales the meshes, X
        /// mirrored as Unity's import mirrors it (<see cref="GltfFrame"/>); its box
        /// and its panorama kept on <paramref name="set"/> (the bind sizes the box by the
        /// instance's scale, and Apply repairs the panorama's import). Null, said, where
        /// the record cannot place it or the panorama cannot be read.
        /// </summary>
        static ReflectionProbe Probe(Transform root, LightmapSet set, Payload record, string[] folders, string assetPath,
                                     bool asked, Action<string, UnityEngine.Object> add, Action<string> dependsOn, ColorSpace space,
                                     GltfFrame frame)
        {
            var probe = record.probe;
            double? unit = record.unit_scale;
            if (!(unit > 0) || double.IsInfinity(unit.Value) || !Point(probe.position, out Vector3 at))
            {
                ImportLog.Warning(assetPath, "lightmaps: the record's probe has no finite position or no positive unit_scale, " +
                                             "so no reflection probe is made. Re-export the file.");
                return null;
            }
            float scale = (float)frame.Units(1.0, unit.Value);
            Vector3 position = at * scale;
            Bounds box;
            bool boxed = probe.box != null;
            if (boxed)
            {
                if (!Point(probe.box.min, out Vector3 a) || !Point(probe.box.max, out Vector3 b))
                {
                    ImportLog.Warning(assetPath, "lightmaps: the probe's box has a corner that is no finite point, so no " +
                                                 "reflection probe is made. Re-export the file.");
                    return null;
                }
                box = new Bounds(a * scale, Vector3.zero);
                box.Encapsulate(b * scale);
            }
            else if (!ModelBounds(root, out box))
                box = new Bounds(position, Vector3.one * 10f);  // nothing to light: Unity's default reach

            // The set names the file whether or not it loads now: an import its own settings'
            // change brings can run before the file's (measured), and Apply repairs those.
            string source = MediaFiles.OnDisk(folders, probe.map);
            if (source != null) set.probeSource = AssetDatabase.AssetPathToGUID(source);
            string path = MediaFiles.Nearest(folders, probe.map);
            Texture2D panorama = path != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
            if (panorama == null)
            {
                if (!asked)
                    ImportLog.Warning(assetPath, $"lightmaps: the probe's panorama '{probe.map}' is not in the model's media, " +
                                                 "so no reflection probe is made. Re-export the file with it embedded.");
                return null;
            }
            dependsOn(path);
            set.probeSource = AssetDatabase.AssetPathToGUID(path);
            Cubemap cube = ProbeCube.Make(panorama, Path.GetFileNameWithoutExtension(probe.map), space, out float[] ambient, out string why);
            if (cube == null)
            {
                ImportLog.Warning(assetPath, $"lightmaps: the probe's panorama '{path}' makes no cubemap: {why}. No reflection " +
                                             "probe is made: turn its Read/Write on and its sRGB off (its import settings).");
                return null;
            }
            add("unitytk/" + cube.name, cube);
            set.ambient = ambient;  // the room's light on what no map lights (the set's environment)

            var go = new GameObject(ProbeName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            var rp = go.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Custom;
            rp.customBakedTexture = cube;
            rp.hdr = true;
            rp.intensity = 1f;
            rp.importance = 1;
            rp.blendDistance = 0f;
            // Projected onto the room it was captured in; an open scene's read as distant.
            rp.boxProjection = boxed;
            rp.center = set.probeCenter = box.center - position;
            rp.size = set.probeSize = box.size;
            return rp;
        }

        /// <summary>
        /// Which renderer under <paramref name="root"/> (inactive ones included) each of
        /// <paramref name="entries"/> lights, as ONE assignment by the one rule every reader
        /// of a record follows (<see cref="RecordNames"/>: exact name, then leaf; the entry's
        /// hierarchy against where each renderer sits; a tie never guessed): each bound
        /// match's <c>Item</c> is the renderer's transform. One per entry, in order.
        /// </summary>
        internal static RecordNames.Match<Transform>[] Receivers(Transform root, IList<RecordNames.Entry> entries)
        {
            var receivers = new List<Transform>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) receivers.Add(r.transform);
            return RecordNames.Resolve(receivers, entries, t => t.name, t => t.parent, root);
        }

        /// <summary>Whether <paramref name="r"/>'s mesh carries a second UV set (or it has no mesh to ask).</summary>
        internal static bool HasLightmapUvs(Renderer r)
        {
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            else if (r.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
            return mesh == null || mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
        }

        // The shape requires four numbers (LightmapRecord: Wire.Nums(..., 4)).
        static Vector4 Rect(double[] so) => new Vector4((float)so[0], (float)so[1], (float)so[2], (float)so[3]);

        // A record point (glTF's axes) in Unity's (GltfFrame.Point: X mirrored); false for no finite point.
        static bool Point(double[] xyz, out Vector3 point)
        {
            point = default;
            if (xyz == null || xyz.Length != 3) return false;
            double[] p = GltfFrame.Point(xyz);
            for (int i = 0; i < 3; i++)
            {
                if (double.IsNaN(p[i]) || double.IsInfinity(p[i])) return false;
                point[i] = (float)p[i];
            }
            return true;
        }

        /// <summary>The bounds, in <paramref name="root"/>'s space, of every mesh under it; false when it has none.</summary>
        static bool ModelBounds(Transform root, out Bounds bounds) => SceneBounds.TryOfMeshes(root, out bounds, root);
    }
}
