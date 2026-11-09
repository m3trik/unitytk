// ----------------------------------------------------------------------------
// The ShadowPlane feature's editor part (M3trik.Unitytk.ShadowPlane.Editor): the
// import half of a delivery's ``shadows`` record (shadows v3), published by
// the producers' ShadowRig onto the shared ``data_export`` carrier. The record
// is read through its generated shape (ShadowRecord.g.cs, beside this file) at
// the version pythontk declares alone: another version, or a record that fails
// that shape, is refused and the file re-exported. Its optional ``silhouette``
// extension is the GLB's alone (the generated shape omits it): Unity plays the
// horizon map.
//
// On import:
//   * OnPostprocessGameObjectWithUserProperties collects the channel off the
//     ``data_export`` node (IRecordSource); OnPostprocessModel resolves each
//     plane record into a ShadowPlane component BY REFERENCE: its plane, its
//     source and its contact found by name (RecordNames: exact, else the
//     namespace-stripped leaf; a tie stays unbound and is reported), a source
//     node holding a Light taken as that Light and a bare one given a switched-
//     off stand-in Light, the record's lengths in metres (times unit_scale), its
//     angles in degrees and its frame vectors in Unity's axes. The delivered
//     horizon map -- a file the FBX embeds, which the carrier (IModelMedia)
//     extracts beside the model and reimports once -- is bound from the model's
//     media folders, and each one bound is a source dependency, so the model
//     reimports when it changes. Where the render pipeline's host draws the
//     package shader (Built-in, and URP through its SRPDefaultUnlit pass), every
//     plane of the model shares ONE instanced material on ``m3trik/ShadowPlane``
//     (the first plane's imported material, mutated in place); else each falls
//     back to the host's unlit-transparent shader, and a pipeline no host serves
//     (HDRP) keeps Unity's material, said once. The plane stops casting and
//     receiving shadows and sampling probes (it IS a shadow).
//     NOTE: the material rewire lives in OnPostprocessModel -- not the
//     material hooks -- because Unity's ImportViaMaterialDescription pipeline
//     never calls the legacy OnAssignMaterialModel, and it applies its
//     description mapping AFTER OnPostprocessMaterial, overwriting anything set
//     there. At OnPostprocessModel the embedded materials are final sub-assets,
//     so in-place mutation sticks.
//   * OnPreprocessTexture pins `*_horizon` maps as DATA (sRGB off, alpha kept,
//     no mips, clamp, point, NPOT kept, RGBA32 on Standalone and Android) --
//     first import only, the lightmap carrier's precedent.
//
// The runtime never reads the record: the component holds what it resolved.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;
using Shape = ShadowRecord.Shape;

[assembly: ReadsRecord(UnitytkRecords.Shadows, typeof(ShadowRecord))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.ShadowPlane.Tests")]

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// AssetPostprocessor that turns a delivery's shadow planes into
    /// <see cref="ShadowPlane"/> components on import, from the <c>shadows</c> record
    /// on <c>data_export</c>: each plane's nodes bound by reference, its delivered
    /// horizon map bound, one shared instanced material on the package shader (the
    /// pipeline's own unlit-transparent as the fallback), shadow casting and receiving
    /// and probes off.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's FBX import hands the record's string to every postprocessor
    /// as a user property and knows nothing of it; this reads it through the carrier
    /// (<see cref="IRecordSource"/>) as every unitytk importer does, asks the carrier
    /// (<see cref="IModelMedia"/>) to extract the maps the FBX embeds, since Unity holds
    /// embedded media only once extracted, and asks the render pipeline's host
    /// (<see cref="RenderPipelineHosts.Current"/>) whether it draws the package shader.
    /// Unity's material remapping (<c>ModelImporter.SearchAndRemapMaterials</c>) binds a
    /// project material by name, not a record's maps and per-plane values.
    /// </remarks>
    public class ShadowPlaneImporter : AssetPostprocessor
    {
        /// <summary>The package shader every plane of a model shares (<c>ShadowPlane.shader</c>, the runtime part's).</summary>
        public const string ShaderName = "m3trik/ShadowPlane";

        /// <summary>The name of the switched-off Light the importer hangs under a bare source node.</summary>
        public const string StandInName = "ShadowSource";

        /// <summary>The file-name stem a horizon map ends with: its first import is pinned as data.</summary>
        public const string HorizonStemMarker = "_horizon";

        const string Tag = "ShadowPlane";

        /// <summary>Bump to reimport every model when what the import makes changes.</summary>
        const uint VERSION = 2;

        /// <inheritdoc/>
        public override uint GetVersion() => VERSION;

        // Per-assetPath raw JSON, collected during
        // OnPostprocessGameObjectWithUserProperties (hierarchy not yet parented)
        // and consumed in OnPostprocessModel (root is final).
        static readonly Dictionary<string, string> _pending = new Dictionary<string, string>();

        static IRecordSource Records => Provided.One<IRecordSource>();

        bool ShouldProcess() => Imports.IsDelivery(assetPath);

        void OnPreprocessModel()
        {
            // Fresh-import reset: JSON stranded by an import that aborted between
            // the callbacks must not resurrect on a re-export without the carrier.
            _pending.Remove(assetPath);
        }

        void OnPreprocessTexture()
        {
            // First import only (no .meta yet): sensible DEFAULTS for a new map, not
            // an enforced policy -- re-pinning on every reimport would revert what the
            // user later set in the Inspector.
            if (!assetImporter.importSettingsMissing) return;
            if (Path.GetFileNameWithoutExtension(assetPath).EndsWith(HorizonStemMarker, StringComparison.OrdinalIgnoreCase))
                PinHorizon((TextureImporter)assetImporter, true);
        }

        /// <summary>
        /// The horizon map is DATA (elevation intervals in R/G, a 16-bit sub-bin
        /// occupancy mask in B/A): linear, alpha kept as a channel, no mips (a tile
        /// must never blend into its neighbour), clamped, point (the shader loads
        /// texels itself and bit-tests them), its exact size (never downsized),
        /// uncompressed, and RGBA32 per platform. Returns whether <paramref name="ti"/>
        /// differs from that; <paramref name="apply"/> sets it so.
        /// </summary>
        internal static bool PinHorizon(TextureImporter ti, bool apply)
        {
            bool differs = ti.textureType != TextureImporterType.Default || ti.sRGBTexture
                || ti.alphaSource != TextureImporterAlphaSource.FromInput || ti.alphaIsTransparency
                || ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Clamp || ti.filterMode != FilterMode.Point
                || ti.npotScale != TextureImporterNPOTScale.None
                || ti.textureCompression != TextureImporterCompression.Uncompressed || ti.maxTextureSize != 8192
                || PinPlatformFormat(ti, "Standalone", false) || PinPlatformFormat(ti, "Android", false);
            if (!apply || !differs) return differs;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Point;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 8192;
            PinPlatformFormat(ti, "Standalone", true);
            PinPlatformFormat(ti, "Android", true);
            return true;
        }

        static bool PinPlatformFormat(TextureImporter ti, string platform, bool apply)
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            if (ps.overridden && ps.format == TextureImporterFormat.RGBA32 && ps.maxTextureSize == 8192) return false;
            if (!apply) return true;
            ps.overridden = true;
            ps.format = TextureImporterFormat.RGBA32;
            ps.maxTextureSize = 8192;
            ti.SetPlatformTextureSettings(ps);
            return true;
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] propNames, object[] values)
        {
            if (!ShouldProcess()) return;
            IRecordSource records = Records;
            if (records.TryGetRecord(propNames, values, UnitytkRecords.Shadows, out string json))
                records.Collect(_pending, assetPath, go, json);
        }

        void OnPostprocessModel(GameObject root)
        {
            _pending.TryGetValue(assetPath, out string json);
            _pending.Remove(assetPath);
            Import(root, json, assetPath, dependsOn: path => context.DependsOnSourceAsset(path),
                   frame: Imports.Frame(assetImporter as ModelImporter));
        }

        /// <summary>
        /// The import's work on the model <paramref name="root"/> at
        /// <paramref name="assetPath"/>, where it is a delivery
        /// (<see cref="Imports.IsDelivery"/>): <see cref="Wire"/> through the providers
        /// (or the stand-ins given) and the render pipeline drawing now. Nothing for any
        /// other asset. Returns the planes wired.
        /// </summary>
        internal static int Import(GameObject root, string json, string assetPath,
                                   IRecordSource records = null, IModelMedia media = null, Action<string> dependsOn = null,
                                   GltfFrame? frame = null)
        {
            if (!Imports.IsDelivery(assetPath)) return 0;
            if (records == null) records = Records;
            if (media == null) media = Provided.One<IModelMedia>();
            return Wire(root, json, assetPath, records, media, RenderPipelineHosts.Current, Shader.Find(ShaderName), dependsOn,
                        frame);
        }

        /// <summary>
        /// Fill the planes of the model <paramref name="root"/> from the shadows record
        /// <paramref name="json"/> read through <paramref name="records"/>, the maps it
        /// names asked of <paramref name="media"/>, on <paramref name="shader"/> where
        /// <paramref name="host"/> draws it (else the host's unlit shader; with no host,
        /// Unity's material stays). With no current record, every stale plane goes.
        /// Returns the planes wired.
        /// </summary>
        /// <param name="assetPath">The model's file, for the import log and a refusal.</param>
        /// <param name="host">The render pipeline's host, or null for one no door serves (HDRP).</param>
        /// <param name="shader">The package shader, or null when it did not import.</param>
        /// <param name="dependsOn">Handed each map file the planes bind (the import's source
        /// dependency, so the model reimports when one changes or goes); null for none.</param>
        /// <param name="frame">The frame the import lands the record in (<see cref="Imports.Frame"/>: the
        /// model importer's scale); the default frame when null.</param>
        internal static int Wire(GameObject root, string json, string assetPath, IRecordSource records, IModelMedia media,
                                 IRenderPipelineHost host, Shader shader, Action<string> dependsOn = null,
                                 GltfFrame? frame = null)
        {
            Shape.Payload payload = records.Read(
                UnitytkRecords.Shadows, json, ShadowRecord.CurrentVersion, Shape.Payload.Read, Tag, assetPath);
            if (payload != null && !(payload.unit_scale.Value > 0 && payload.unit_scale.Value < double.PositiveInfinity))
            {
                ImportLog.Warning(assetPath, $"the {UnitytkRecords.Shadows} record states a unit scale of {payload.unit_scale.Value}, " +
                                             "so its shadow planes cannot be sized -- re-export the file from a current DCC exporter.");
                payload = null;
            }
            if (payload == null || payload.planes.Count == 0)
            {
                // Channel absent, emptied or refused -- drop stale planes so the prefab
                // matches the re-exported source.
                foreach (var stale in root.GetComponentsInChildren<ShadowPlane>(true))
                    UnityEngine.Object.DestroyImmediate(stale, true);
                return 0;
            }

            bool useShared = shader != null && host != null && host.Draws(shader);
            if (host == null)
                ImportLog.Warning(assetPath, $"the {RenderPipelineHosts.NameOf(null)} render pipeline has no unitytk host (no door " +
                                             "serves it), so its shadow planes keep the material Unity imported, which may draw " +
                                             "opaque: the horizon march does not apply.");
            else if (shader == null)
                ImportLog.Warning(assetPath, $"shader '{ShaderName}' not found -- did ShadowPlane.shader import with the unitytk " +
                                             $"package? Each shadow plane falls back to the {host.Name} pipeline's unlit-transparent " +
                                             "shader (no horizon march).");
            else if (!useShared)
                ImportLog.Warning(assetPath, $"the {host.Name} render pipeline does not draw '{ShaderName}': each shadow plane " +
                                             "falls back to the pipeline's unlit-transparent shader (no horizon march).");

            // Every map rides inside the FBX, and Unity holds embedded media only once
            // extracted into the model's media folder, where the lookups below look, on
            // the one reimport the extraction brings.
            var wanted = new List<string>();
            foreach (var rec in payload.planes)
                if (!wanted.Contains(rec.horizon.texture)) wanted.Add(rec.horizon.texture);
            bool asked = media.RequestEmbeddedFiles(assetPath, wanted);
            string[] folders = media.MediaFolders(assetPath);

            var renderers = new List<Renderer>(root.GetComponentsInChildren<Renderer>(true));
            var nodes = new List<Transform>(root.GetComponentsInChildren<Transform>(true));
            RecordNames.Match<Transform>[] matches = RecordNames.Resolve(
                renderers.ConvertAll(r => r.transform), payload.planes.ConvertAll(p => new RecordNames.Entry(p.name)),
                t => t.name, t => t.parent, root.transform);
            var planeRenderers = new HashSet<Renderer>();
            foreach (var match in matches)
                if (match.Outcome == RecordNames.Outcome.Bound) planeRenderers.Add(renderers[match.Index]);
            var standIns = new Dictionary<Transform, Light>();
            var wiredPlanes = new List<ShadowPlane>();
            var missing = new List<string>();
            // One record length (the file's own unit, unit_scale metres) as the import's locals.
            double u = (frame ?? GltfFrame.Default).Units(1.0, payload.unit_scale.Value);
            Material shared = null;

            for (int i = 0; i < payload.planes.Count; i++)
            {
                Shape.Plane rec = payload.planes[i];
                RecordNames.Match<Transform> match = matches[i];
                if (match.Outcome != RecordNames.Outcome.Bound)
                {
                    if (!asked) Unbound(assetPath, "plane", rec.name, match.Outcome, match.Candidates, rec.name);
                    continue;
                }
                Renderer renderer = renderers[match.Index];
                Transform contact = Node(nodes, rec.contact, "contact", rec.name, root.transform, assetPath, asked);
                Transform sourceNode = Node(nodes, rec.source, "source", rec.name, root.transform, assetPath, asked);
                Light light = sourceNode != null ? SourceLight(sourceNode, rec.source_type, standIns, assetPath, asked) : null;

                Shape.PlaneHorizon hz = rec.horizon;
                string mapPath = MediaFiles.Nearest<Texture2D>(folders, hz.texture);
                Texture2D map = mapPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath) : null;
                if (map != null && dependsOn != null && !mapPath.Contains(".fbm/")) dependsOn(mapPath);
                else if (map == null && !missing.Contains(hz.texture)) missing.Add(hz.texture);

                // ---- material: one shared on the package shader ----
                var mat = renderer.sharedMaterial;
                if (mat == null)
                {
                    if (!asked) ImportLog.Warning(assetPath, $"shadow plane '{rec.name}' has no imported material to rewire -- " +
                                                             "enable Material Creation Mode on the model importer.");
                }
                else if (useShared)
                {
                    if (shared == null)
                    {
                        shared = mat;
                        ConfigureShadowPlane(shared, shader, map);
                    }
                    else if (mat != shared)
                    {
                        renderer.sharedMaterial = shared;
                    }
                    // The cache marches the plane's own UV square: the full unit square,
                    // whatever tile a producer's silhouette atlas remapped it into.
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null) NormalizeUv(filter.sharedMesh);
                }
                else if (host != null)
                {
                    ConfigureUnlitTransparent(mat, host, assetPath, Mathf.Clamp01((float)rec.intensity.Value));
                }

                // A shadow must not cast or receive engine shadows or probe light.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                var plane = renderer.GetComponent<ShadowPlane>();
                if (plane == null) plane = renderer.gameObject.AddComponent<ShadowPlane>();
                Fill(plane, rec, u, contact, light, map, assetPath);
                plane.zUpFrames = ZUpFrame(contact, root.transform);
                plane.target = TargetOf(contact, root.transform, planeRenderers);
                // The model's frame: the root stands at rest here, so a placed instance's
                // move, turn and scale are what the plane follows.
                plane.BindSpace(root.transform);
                wiredPlanes.Add(plane);
            }

            // The delivered map is current for the delivered geometry: its hash, so a
            // later edit of a target is what Rebake Stale finds.
            foreach (ShadowPlane plane in wiredPlanes)
                if (plane.target != null && plane.horizonMap != null)
                    plane.horizonHash = ShadowPlaneActions.GeometryHash(plane) ?? "";

            if (asked)
                ImportLog.Info(assetPath, "shadows: its embedded horizon maps are asked of the extraction; the reimport that " +
                                          "follows binds them.");
            else
            {
                if (missing.Count > 0)
                    ImportLog.Warning(assetPath, $"shadows: {missing.Count} horizon map(s) the record names are not in the model's " +
                                                 $"media: {string.Join(", ", missing)}; their planes draw nothing. Re-export the file " +
                                                 "with its maps embedded.");
                ImportLog.Info(assetPath, $"shadows: {wiredPlanes.Count} of {payload.planes.Count} shadow plane(s) bound.");
            }
            return wiredPlanes.Count;
        }

        /// <summary>
        /// Copy the record into the plane: each length times <paramref name="u"/> (one record
        /// length in the import's locals), the angle in degrees, the frame and the canvas in Unity's axes.
        /// </summary>
        static void Fill(ShadowPlane plane, Shape.Plane rec, double u, Transform contact, Light light, Texture2D map,
                         string assetPath)
        {
            plane.contact = contact;
            plane.source = light;
            plane.radius = (float)(rec.radius.Value * u);
            plane.height = (float)(rec.height.Value * u);
            plane.ground = (float)(rec.ground.Value * u);
            plane.maxStretch = (float)rec.max_stretch.Value;
            plane.canvas = UnityCanvas(rec.canvas, plane.LiesInXY);
            plane.intensity = Mathf.Clamp01((float)rec.intensity.Value);
            plane.falloffPower = (float)rec.falloff_power.Value;
            plane.fadeHeight = (float)(rec.fade_height.Value * u);
            plane.unit = (float)u;
            double ramp = rec.source_ramp.Value;
            if (!(ramp > 0 && ramp < double.PositiveInfinity))
            {
                ImportLog.Warning(assetPath, $"shadow plane '{rec.name}' states a source ramp of {ramp} (it must be over 0), so " +
                                             "it fades in over one unit of the record -- re-export the file from a current DCC exporter.");
                ramp = 1.0;
            }
            plane.sourceRamp = (float)(ramp * u);
            plane.sourceSize = (float)(rec.source_size.Value * u);
            plane.sourceAngle = (float)(rec.source_angle.Value * 180.0 / Math.PI);
            plane.followSource = rec.follow_source.Value;

            Shape.PlaneHorizon hz = rec.horizon;
            plane.horizonMap = map;
            plane.horizonSize = hz.size.Value;
            plane.horizonSpans = hz.spans.Value;
            plane.horizonBounds = new Vector4((float)(hz.bounds[0] * u), (float)(hz.bounds[1] * u),
                                              (float)(hz.bounds[2] * u), (float)(hz.bounds[3] * u));
            plane.horizonHeightScale = (float)(hz.height_scale.Value * u);
            plane.horizonFrameA = FbxToUnity(hz.frame_a, Vector3.right);
            plane.horizonFrameB = FbxToUnity(hz.frame_b, Vector3.forward);
            plane.horizonRect = new Vector4((float)hz.rect[0], (float)hz.rect[1], (float)hz.rect[2], (float)hz.rect[3]);
            plane.horizonHash = "";
        }

        /// <summary>
        /// The node a plane's <paramref name="role"/> names, or null: none named, or a
        /// name that is absent or tied (said, on the import that binds).
        /// </summary>
        static Transform Node(IList<Transform> nodes, string name, string role, string plane, Transform root,
                              string assetPath, bool asked)
        {
            if (string.IsNullOrEmpty(name)) return null;
            RecordNames.Match<Transform> match = RecordNames.Find(nodes, name, t => t.name, t => t.parent, root);
            if (match.Outcome == RecordNames.Outcome.Bound) return match.Item;
            if (!asked) Unbound(assetPath, role, name, match.Outcome, match.Candidates, plane);
            return null;
        }

        static void Unbound(string assetPath, string role, string name, RecordNames.Outcome outcome, string[] candidates,
                            string plane)
        {
            string whose = role == "plane" ? $"shadow plane '{name}'" : $"shadow plane '{plane}''s {role} '{name}'";
            string fallback = role == "plane" ? "it is left out" : role == "source"
                ? "it is left unbound, and the plane follows the scene's sun"
                : "it is left unbound, and the plane draws at its intensity without moving";
            RecordNamesLog.Unbound(assetPath, whose, outcome, candidates, fallback);
        }

        /// <summary>
        /// The Light <paramref name="node"/> casts with: its own, else a switched-off
        /// stand-in Light of the record's type hung under it (one per node, named
        /// <see cref="StandInName"/>), turned to shine along the node's local -Z, the
        /// way a producer's bare source node shines. Enable it to light the scene too.
        /// </summary>
        static Light SourceLight(Transform node, string sourceType, Dictionary<Transform, Light> standIns, string assetPath,
                                 bool asked)
        {
            bool directional = sourceType == "directional";
            Light own = node.GetComponent<Light>();
            if (own != null)
            {
                if (!asked && (own.type == LightType.Directional) != directional)
                    ImportLog.Warning(assetPath, $"shadow source '{node.name}' is a {own.type} light, and the record states a " +
                                                 $"{sourceType} source: the plane casts as the Light does.");
                return own;
            }
            if (standIns.TryGetValue(node, out Light made)) return made;
            Transform existing = node.Find(StandInName);
            Light light = existing != null ? existing.GetComponent<Light>() : null;
            if (light == null)
            {
                var go = new GameObject(StandInName);
                go.transform.SetParent(node, false);
                light = go.AddComponent<Light>();
            }
            light.transform.localPosition = Vector3.zero;
            light.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);  // its +Z along the node's -Z
            light.type = directional ? LightType.Directional : LightType.Point;
            light.enabled = false;
            standIns[node] = light;
            return light;
        }

        /// <summary>
        /// The occluder a contact stands for: the node it hangs on (the producers hang it
        /// on what moves every target with it), when that is neither the model's root nor
        /// a node with no mesh but the shadow planes'; else null (Bake Horizon then asks
        /// for one).
        /// </summary>
        static Transform TargetOf(Transform contact, Transform root, HashSet<Renderer> planes)
        {
            Transform parent = contact != null ? contact.parent : null;
            if (parent == null || parent == root) return null;
            foreach (MeshFilter filter in parent.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer r = filter.GetComponent<Renderer>();
                if (filter.sharedMesh != null && (r == null || !planes.Contains(r))) return parent;
            }
            foreach (SkinnedMeshRenderer skinned in parent.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skinned.sharedMesh != null) return parent;
            return null;
        }

        /// <summary>
        /// Whether <paramref name="contact"/>'s frame is a Z-up producer's: its +Z, not its
        /// +Y, along the model's up. A Z-up exporter turns only the root nodes upright, so a
        /// contact below them keeps its Z-up local frame (one that is itself a root is
        /// turned, and reads as Y-up, as it then is). Read off the contact's own frame, never
        /// the plane's mesh: how a quad is laid says nothing of the contact.
        /// </summary>
        static bool ZUpFrame(Transform contact, Transform root)
        {
            if (contact == null) return false;
            Vector3 up = root.up;
            return Vector3.Dot(contact.forward, up) > Vector3.Dot(contact.up, up);
        }

        /// <summary>
        /// A record's canvas <c>(u0, u1, w0, w1)</c> in the component's terms, its sides along an XZ
        /// quad's local +X as Unity places it. A producer writes the sides along its own quad's local
        /// +X, in its ground frame (pythontk <c>ShadowModel.across</c>, one formula in both DCCs'
        /// frames). A Y-up producer's frame reaches Unity through the import's X mirror alone, which
        /// swaps the sides: <c>(u0, u1, -w1, -w0)</c>. A Z-up producer's quad lies in its local XY
        /// (<paramref name="quadInXY"/>), and its frame reaches Unity through the root's upright turn
        /// and that mirror together, which keep them. The far and near edges ride the bearing either way.
        /// </summary>
        internal static Vector4 UnityCanvas(double[] c, bool quadInXY)
            => quadInXY ? new Vector4((float)c[0], (float)c[1], (float)c[2], (float)c[3])
                        : new Vector4((float)c[0], (float)c[1], (float)-c[3], (float)-c[2]);

        /// <summary>
        /// A direction written in glTF's axes (right-handed Y-up), in Unity's
        /// (<see cref="GltfFrame.Direction"/>), as a unit vector; <paramref name="fallback"/> for none.
        /// </summary>
        static Vector3 FbxToUnity(double[] v, Vector3 fallback)
        {
            double[] d = GltfFrame.Direction(v);
            var u = new Vector3((float)d[0], (float)d[1], (float)d[2]);
            return u.sqrMagnitude > 1e-12f ? u.normalized : fallback;
        }

        /// <summary>Stretch a quad's UVs to 0..1 (undoing a producer's silhouette-atlas remap).</summary>
        static void NormalizeUv(Mesh mesh)
        {
            var uv = mesh.uv;
            if (uv == null || uv.Length == 0) return;
            Vector2 min = uv[0], max = uv[0];
            foreach (var p in uv)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            Vector2 size = Vector2.Max(max - min, new Vector2(1e-6f, 1e-6f));
            if (Mathf.Abs(size.x - 1f) < 1e-4f && Mathf.Abs(size.y - 1f) < 1e-4f && min.sqrMagnitude < 1e-8f)
                return;  // already the unit square
            for (int i = 0; i < uv.Length; i++)
                uv[i] = new Vector2((uv[i].x - min.x) / size.x, (uv[i].y - min.y) / size.y);
            mesh.uv = uv;
        }

        /// <summary>
        /// Rewire an imported material (in place -- a sub-asset of the model) into the
        /// shared, instanced shadow material on the package shader, the first plane's
        /// map bound (a plane with another map binds its own through its block).
        /// </summary>
        internal static void ConfigureShadowPlane(Material mat, Shader shader, Texture2D map)
        {
            mat.shader = shader;
            mat.enableInstancing = true;
            // Nothing until a plane writes its own fade (its block): an unplaced plane would
            // otherwise march the material's map -- the first plane's -- in a frame at the origin.
            mat.SetFloat("_Fade", 0f);
            if (map != null) mat.SetTexture("_HorizonMap", map);
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>
        /// Rewire an imported material into the pipeline's own unlit, blended shader (in
        /// place), as its host names and switches it: the fallback where the package
        /// shader is absent or the pipeline does not draw it. It draws the texture the
        /// material already carries; it marches no horizon, and no per-frame fade reaches
        /// it (the fade is the component's, on the package shader): it draws at the
        /// plane's <paramref name="intensity"/>, a constant alpha, where the shader has a
        /// base colour (URP's Unlit; Built-in's <c>Unlit/Transparent</c> has none).
        /// </summary>
        static void ConfigureUnlitTransparent(Material mat, IRenderPipelineHost host, string assetPath, float intensity)
        {
            Texture tex = mat.mainTexture;
            string name = host.UnlitShader(true);
            Shader unlit = Shader.Find(name);
            if (unlit == null)
            {
                ImportLog.Warning(assetPath, $"the {host.Name} pipeline's unlit shader '{name}' is not in the project, so " +
                                             $"shadow material '{mat.name}' keeps its imported shader.");
                return;
            }
            mat.shader = unlit;
            host.SetBlended(mat, true);
            if (tex != null && mat.HasProperty(host.BaseMapProperty)) mat.SetTexture(host.BaseMapProperty, tex);
            if (mat.HasProperty(host.BaseColorProperty)) mat.SetColor(host.BaseColorProperty, new Color(1f, 1f, 1f, intensity));
        }

        /// <summary>
        /// A map written over in place (a rebake, or a person's edit of the file) reimports
        /// into the same <c>Texture2D</c>, so the cache's key, which knows a map by that
        /// object, cannot see it: each open plane bound to a reimported texture forgets its
        /// cached march. The editor's alone -- a player never reimports.
        /// </summary>
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            HashSet<string> textures = null;
            foreach (string path in imported)
                if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(Texture2D))
                    (textures ?? (textures = new HashSet<string>(StringComparer.Ordinal))).Add(path);
            if (textures == null) return;
            foreach (ShadowPlane plane in UnityEngine.Object.FindObjectsByType<ShadowPlane>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (plane.horizonMap != null && textures.Contains(AssetDatabase.GetAssetPath(plane.horizonMap)))
                    plane.ForgetMap();
        }
    }
}
