using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using Entry = MaterialRecord.Shape.Entry;
using Payload = MaterialRecord.Shape.Payload;

namespace M3trik.Unitytk.Materials
{
    /// <summary>
    /// The Materials feature's actions, on any material in Unity's terms: what a
    /// person's menu item and an agent's code do to a project, with or without a
    /// delivery behind it. Each takes an optional <see cref="OpContext"/>: a dry run
    /// plans, a menu's run is one undo step for the materials it changes (the files it
    /// writes stay), and none just runs. Each changes a material only where the result
    /// differs from what it holds (<see cref="MaterialWriter.Equivalent"/>), so a second
    /// run plans nothing.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's material inspectors set one property at a time, by hand, with
    /// no glTF reading, no repacking and no dry run; glTFast and UnityGLTF read a glTF
    /// material only from a glTF file they import, onto shaders of their own. These
    /// write through the kernel's one writer (<see cref="MaterialWriter"/>), onto the
    /// pipeline's own Lit, as the import does.
    /// </remarks>
    public static class MaterialsActions
    {
        /// <summary>The issue a write raises for what the target shader cannot show.</summary>
        public const string LossCode = "materials.loss";
        /// <summary>The issue raised for an image a glTF material names that is not found.</summary>
        public const string MissingImageCode = "materials.missing_image";
        /// <summary>The issue raised where no render-pipeline host serves the pipeline, or its shader is missing.</summary>
        public const string NoWriterCode = "materials.no_writer";
        /// <summary>The issue raised for a texture two slots of one material read in different roles.</summary>
        public const string RoleConflictCode = "materials.role_conflict";
        /// <summary>The issue raised where a model carries no materials record to reapply.</summary>
        public const string NoRecordCode = "materials.no_record";

        /// <summary>
        /// Write a glTF 2.0 material onto <paramref name="material"/>, through the
        /// pipeline's own Lit (its Unlit for a <c>KHR_materials_unlit</c> one): base
        /// colour, metal and roughness (repacked into Unity's mask, written beside the
        /// material as a PNG), normal, occlusion, emission with its strength, alpha mode,
        /// double-sided and the maps' UV transform. How a person with no DCC gives a
        /// material the look a glTF file states.
        /// </summary>
        /// <param name="material">A material asset of the project's own (or one not saved): never one a model's import owns, which its next import rewrites.</param>
        /// <param name="gltf">A glTF document's JSON (a <c>.gltf</c> file's text, or a <c>materials</c> record): its
        /// <c>materials</c>, <c>textures</c> and <c>images</c> are read, each image by its file name.</param>
        /// <param name="name">The glTF material to write, by name; null: the one named like
        /// <paramref name="material"/>, else the document's only one. A glTF file's material
        /// with no <c>name</c> (glTF makes it optional) goes by <paramref name="material"/>'s.</param>
        /// <param name="imageFolder">The project folder the images are found in, by file name; null: the material's folder.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>Whether it changed the material or wrote its mask (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="material"/> or <paramref name="gltf"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="material"/> is a model's own (extract it first, or
        /// <see cref="Reapply"/> the model), <paramref name="gltf"/> is no glTF document (or a refused record), or
        /// it holds no material <paramref name="name"/> names.</exception>
        public static bool Apply(Material material, string gltf, string name = null, string imageFolder = null,
                                 OpContext ctx = null)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (gltf == null) throw new ArgumentNullException(nameof(gltf));
            RefuseImported(material);
            Payload document = Document(gltf, material.name);
            var entries = MaterialRecord.Entries(document);
            Entry entry = name != null ? MaterialRecord.Find(entries, name)
                : MaterialRecord.Find(entries, material.name) ?? (entries.Count == 1 ? Only(entries) : null);
            if (entry == null)
                throw new ArgumentException(
                    $"the glTF document holds no material named '{name ?? material.name}' (it holds: " +
                    $"{string.Join(", ", entries.Keys)}): name the one to write", nameof(name));
            string folder = imageFolder ?? FolderOf(material) ?? "Assets";
            return Write(material, document, entry, new[] { folder }, FolderOf(material) ?? folder, ctx);
        }

        /// <summary>
        /// Pack a glTF metallicRoughness image (<paramref name="metallicRoughness"/>:
        /// roughness G, metal B) and an occlusion image (<paramref name="occlusion"/>,
        /// read from R; it may be the same image) into the one map Unity's Lit reads
        /// (R metal, G occlusion, A smoothness), written beside the image as
        /// <c>&lt;image&gt;_MetallicSmoothness.png</c>, and bind it on
        /// <paramref name="material"/>'s metal / smoothness slot (and occlusion slot) at
        /// scale 1, leaving the rest of the material as it is. The maps are packed as they
        /// are: no factor is baked in.
        /// </summary>
        /// <param name="material">A material on the pipeline's own Lit, of the project's own.</param>
        /// <param name="metallicRoughness">A PNG or JPEG texture asset in glTF's layout, or null for occlusion alone.</param>
        /// <param name="occlusion">A PNG or JPEG texture asset whose R is the occlusion, or null.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>Whether it wrote the map or changed the material (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="material"/> is null, or both images are.</exception>
        /// <exception cref="ArgumentException">The material is a model's own or not on the pipeline's Lit, or an
        /// image is no texture file of the project.</exception>
        /// <exception cref="InvalidOperationException">No render-pipeline host serves the project's pipeline.</exception>
        public static bool PackMetallicSmoothness(Material material, Texture metallicRoughness, Texture occlusion = null,
                                                  OpContext ctx = null)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (metallicRoughness == null && occlusion == null)
                throw new ArgumentNullException(nameof(metallicRoughness), "name a metallicRoughness image, an occlusion image or both");
            RefuseImported(material);
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            if (host == null)
                throw new InvalidOperationException($"no unitytk render-pipeline host serves the {RenderPipelineHosts.NameOf(null)} pipeline");
            if (material.shader == null || material.shader.name != host.Lit.Lit)
                throw new ArgumentException(
                    $"'{material.name}' is on {(material.shader != null ? material.shader.name : "no shader")}, not the " +
                    $"{host.Name} pipeline's Lit ({host.Lit.Lit}): switch its shader, or Apply a glTF material", nameof(material));
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Named(Texture texture, string role)
            {
                if (texture == null) return null;
                string path = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsSubAsset(texture) || !File.Exists(Path.GetFullPath(FileUtil.GetPhysicalPath(path))))
                    throw new ArgumentException($"the {role} image '{texture.name}' is no texture file of the project", role);
                paths[Path.GetFileName(path)] = path;
                return Path.GetFileName(path);
            }
            var source = new MaskSource
            {
                metallicRoughness = Named(metallicRoughness, nameof(metallicRoughness)),
                occlusion = Named(occlusion, nameof(occlusion)),
            };
            string folder = FolderOfPath(paths[source.metallicRoughness ?? source.occlusion]);
            Texture mask = WriteMask(source, file => paths.TryGetValue(file, out string p) ? p : null, folder, ctx,
                                     out bool written, out string why);
            if (mask == null)
            {
                ctx.Raise(MissingImageCode, $"'{material.name}': the mask could not be packed: {why}.", location: Location(material));
                return false;
            }
            try
            {
                var wanted = new Material(material);
                MaterialWriter.BindMask(wanted, mask, source.occlusion != null, host);
                bool same = MaterialWriter.Equivalent(material, wanted);
                UnityEngine.Object.DestroyImmediate(wanted);
                if (same) return written;
                if (ctx == null || ctx.ShouldChange(material, $"bind '{source.Name}' as its metal and smoothness map"))
                {
                    MaterialWriter.BindMask(material, mask, source.occlusion != null, host);
                    EditorUtility.SetDirty(material);
                }
                return true;
            }
            finally
            {
                if (!EditorUtility.IsPersistent(mask)) UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        /// <summary>
        /// Import each texture <paramref name="material"/>'s slots bind in the role its
        /// slot reads it in, through the pipeline's table: a normal map as a normal map,
        /// the metal / smoothness, mask and occlusion maps linear (sRGB off), the base
        /// colour and emission maps as sRGB colour. Its import settings change and it
        /// reimports; nothing else does. A texture two slots read in different roles is
        /// said, and left.
        /// </summary>
        /// <param name="material">Any material, on its pipeline's Lit or Unlit.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them. Each change is a
        /// write of the texture's import settings (its <c>.meta</c>), which an undo does not take back.</param>
        /// <returns>How many textures it reimported (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="material"/> is null.</exception>
        public static int FixTextureRoles(Material material, OpContext ctx = null)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            if (host == null)
            {
                ctx.Raise(NoWriterCode, $"no unitytk render-pipeline host serves the {RenderPipelineHosts.NameOf(null)} " +
                                        "pipeline, so its slots' roles are not known", location: Location(material));
                return 0;
            }
            LitTable t = host.Lit;
            var slots = new (string property, TextureRole role)[]
            {
                (t.BaseMap, TextureRole.Colour), (t.UnlitMap, TextureRole.Colour), (t.MetaMap, TextureRole.Colour),
                (t.EmissionMap, TextureRole.Colour), (t.MaskMap, TextureRole.Data), (t.OcclusionMap, TextureRole.Data),
                (t.NormalMap, TextureRole.Normal),
            };
            var roles = new Dictionary<string, TextureRole>();
            var conflicts = new HashSet<string>();
            foreach (var (property, role) in slots)
            {
                if (property == null || !material.HasProperty(property)) continue;
                Texture texture = material.GetTexture(property);
                if (texture == null || AssetDatabase.IsSubAsset(texture)) continue;
                string path = AssetDatabase.GetAssetPath(texture);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter)) continue;
                if (roles.TryGetValue(path, out var held) && held != role) conflicts.Add(path);
                else roles[path] = role;
            }
            int fixedCount = 0;
            foreach (var pair in roles)
            {
                if (conflicts.Contains(pair.Key))
                {
                    ctx.Raise(RoleConflictCode, $"'{material.name}' reads '{pair.Key}' both as colour or a normal and as data: " +
                                                "it is left as it is imported.", location: pair.Key);
                    continue;
                }
                var importer = (TextureImporter)AssetImporter.GetAtPath(pair.Key);
                bool normal = pair.Value == TextureRole.Normal;
                bool srgb = pair.Value == TextureRole.Colour;
                bool typed = normal ? importer.textureType == TextureImporterType.NormalMap
                                    : importer.textureType != TextureImporterType.NormalMap;
                if (typed && (normal || importer.sRGBTexture == srgb)) continue;
                string role = normal ? "a normal map" : srgb ? "sRGB colour" : "linear data";
                fixedCount++;
                if (ctx != null && !ctx.ShouldWrite(pair.Key + ".meta", $"import it as {role}")) continue;
                if (normal) importer.textureType = TextureImporterType.NormalMap;
                else
                {
                    if (importer.textureType == TextureImporterType.NormalMap) importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = srgb;
                }
                importer.SaveAndReimport();
            }
            return fixedCount;
        }

        /// <summary>
        /// Write the model at <paramref name="modelPath"/>'s <c>materials</c> record onto
        /// the project materials its import is remapped to (the model importer's
        /// Materials tab, or extracted materials): how a person who extracted a
        /// delivery's materials puts them back to what it states. The model's own
        /// materials need none: its import writes them.
        /// </summary>
        /// <param name="modelPath">The model's asset path.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <param name="records">Where the record is read (default: the carrier).</param>
        /// <param name="media">Where its images are found (default: the carrier).</param>
        /// <returns>How many materials it changed or wrote a mask for (in a dry run, would).</returns>
        /// <exception cref="ArgumentException">No model is at <paramref name="modelPath"/>.</exception>
        public static int Reapply(string modelPath, OpContext ctx = null, IRecordSource records = null, IModelMedia media = null)
        {
            if (!(AssetImporter.GetAtPath(modelPath ?? "") is ModelImporter importer))
                throw new ArgumentException($"no model is at '{modelPath}': name a model's asset path", nameof(modelPath));
            if (records == null) records = Provided.One<IRecordSource>();
            if (media == null) media = Provided.One<IModelMedia>();
            string json = records.Record(modelPath, UnitytkRecords.Materials);
            Payload record = MaterialRecord.Read(json, modelPath, out string refusal);
            if (record == null)
            {
                ctx.Raise(NoRecordCode, refusal ?? $"'{modelPath}' carries no materials record: nothing to reapply.", location: modelPath);
                return 0;
            }
            var entries = MaterialRecord.Entries(record);
            string[] folders = media.MediaFolders(modelPath);
            int changed = 0;
            foreach (var pair in importer.GetExternalObjectMap())
            {
                if (pair.Key.type != typeof(Material) || !(pair.Value is Material material) || material == null) continue;
                Entry entry = MaterialRecord.Find(entries, pair.Key.name);
                if (entry == null) continue;
                if (Write(material, record, entry, folders, FolderOf(material) ?? FolderOfPath(modelPath), ctx)) changed++;
            }
            return changed;
        }

        // -- shared -------------------------------------------------------------------------

        static Entry Only(Dictionary<string, Entry> entries)
        {
            foreach (var entry in entries.Values) return entry;
            return null;
        }

        /// <summary>
        /// The glTF document <paramref name="gltf"/> holds, read as the record is: its version
        /// optional, and a material with no name named <paramref name="unnamed"/>.
        /// </summary>
        static Payload Document(string gltf, string unnamed)
        {
            object tree;
            try
            {
                tree = Json.Parse(gltf, false);
            }
            catch (FormatException e)
            {
                throw new ArgumentException($"the glTF document is not JSON ({e.Message})", nameof(gltf), e);
            }
            if (!(tree is IDictionary<string, object> fields))
                throw new ArgumentException("the glTF document is not a JSON object", nameof(gltf));
            if (fields.ContainsKey("version"))
            {
                // A materials record: read at its one version, as the import reads it.
                Payload record = MaterialRecord.Read(gltf, "the glTF document", out string refusal);
                if (record == null) throw new ArgumentException(refusal, nameof(gltf));
                return record;
            }
            // A glTF file's own JSON has no record version; its materials are the record's shape
            // once glTF's own defaults stand in for what a material leaves out.
            fields["version"] = (double)MaterialRecord.CurrentVersion;
            if (fields.TryGetValue("materials", out object materials) && materials is IList<object> list)
                foreach (object item in list)
                    if (item is IDictionary<string, object> m) Defaults(m, unnamed);
            try
            {
                return Payload.Read(fields) ?? throw new FormatException("not an object");
            }
            catch (FormatException e)
            {
                throw new ArgumentException($"the glTF document is not glTF 2.0's material shape ({e.Message})", nameof(gltf), e);
            }
        }

        // glTF 2.0's defaults for what a material may leave out (the record states them all), and
        // a name for one glTF leaves unnamed: the target material's, which the record requires.
        static void Defaults(IDictionary<string, object> material, string unnamed)
        {
            if (!material.TryGetValue("name", out object named) || !(named is string text) || text.Length == 0)
                material["name"] = unnamed;
            if (!material.TryGetValue("pbrMetallicRoughness", out object pbr) || !(pbr is IDictionary<string, object> fields))
                material["pbrMetallicRoughness"] = fields = new Dictionary<string, object>();
            if (!fields.ContainsKey("baseColorFactor")) fields["baseColorFactor"] = new List<object> { 1.0, 1.0, 1.0, 1.0 };
            if (!fields.ContainsKey("metallicFactor")) fields["metallicFactor"] = 1.0;
            if (!fields.ContainsKey("roughnessFactor")) fields["roughnessFactor"] = 1.0;
            if (!material.ContainsKey("emissiveFactor")) material["emissiveFactor"] = new List<object> { 0.0, 0.0, 0.0 };
            if (!material.ContainsKey("alphaMode")) material["alphaMode"] = "OPAQUE";
            if (!material.ContainsKey("doubleSided")) material["doubleSided"] = false;
        }

        // A model's own material: its next import rewrites it.
        static void RefuseImported(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter)
                throw new ArgumentException(
                    $"'{material.name}' is the import's own material of '{path}', which its next import rewrites: extract " +
                    "it (the model importer's Materials tab) and write that, or Reapply the model", nameof(material));
        }

        static string FolderOf(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) ? null : FolderOfPath(path);
        }

        static string FolderOfPath(string path) => (Path.GetDirectoryName(path) ?? "Assets").Replace('\\', '/');

        static string Location(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) ? material.name : path;
        }

        /// <summary>
        /// Write <paramref name="entry"/> of <paramref name="record"/> onto
        /// <paramref name="material"/> where it changes it, its images found in
        /// <paramref name="folders"/> and its mask written into <paramref name="maskFolder"/>.
        /// </summary>
        static bool Write(Material material, Payload record, Entry entry, string[] folders, string maskFolder, OpContext ctx)
        {
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            var missing = new List<string>();
            var losses = new List<string>();
            bool written = false;
            Texture made = null;
            string refused = null;
            var surface = GltfSurfaces.Surface(record, entry, file =>
            {
                string path = MediaFiles.Nearest(folders, file);
                return path != null ? AssetDatabase.LoadAssetAtPath<Texture>(path) : null;
            }, source =>
            {
                // A mask that cannot be packed is said by the surface's reading, as a loss, and why here.
                made = WriteMask(source, file => MediaFiles.Nearest(folders, file), maskFolder, ctx, out written,
                                 out string why);
                if (made == null) refused = why;
                return made;
            }, missing, losses);
            try
            {
                if (refused != null)
                    ctx.Raise(MissingImageCode, $"'{material.name}': its metal / smoothness mask is not written: {refused}.",
                              location: Location(material));
                foreach (string file in missing)
                    ctx.Raise(MissingImageCode, $"'{material.name}': the image '{file}' its glTF material names is not in " +
                                                $"{string.Join(" or ", folders)}: it reads as absent.", location: Location(material));
                var wanted = new Material(material);
                bool ok = MaterialWriter.Write(wanted, surface, host, losses);
                bool same = ok && MaterialWriter.Equivalent(material, wanted);
                UnityEngine.Object.DestroyImmediate(wanted);
                if (!ok)
                {
                    ctx.Raise(NoWriterCode, host == null
                        ? $"no unitytk render-pipeline host serves the {RenderPipelineHosts.NameOf(null)} pipeline: '{material.name}' is left as it is."
                        : $"the {host.Name} pipeline's shader is not in the project: '{material.name}' is left as it is.",
                        "error", Location(material));
                    return written;
                }
                foreach (string loss in losses)
                    ctx.Raise(LossCode, $"'{material.name}': {loss}.", location: Location(material));
                if (same) return written;
                if (ctx == null || ctx.ShouldChange(material, $"write glTF material '{entry.name}' on {host.Lit.Lit}"))
                {
                    MaterialWriter.Write(material, surface, host);
                    EditorUtility.SetDirty(material);
                }
                return true;
            }
            finally
            {
                if (made != null && !EditorUtility.IsPersistent(made)) UnityEngine.Object.DestroyImmediate(made);
            }
        }

        /// <summary>
        /// The mask <paramref name="source"/> states as a texture asset in
        /// <paramref name="folder"/> (<see cref="MaskSource.Name"/>.png, linear, stamped as
        /// this package's: <see cref="MaskTextures.Stamped"/>): the file there when it holds
        /// the same texels, else written (asked of <paramref name="ctx"/>; a dry run returns
        /// the packed texture unsaved, which the caller destroys); null, with why, where its
        /// images cannot be read or a file of its name is not a mask this package wrote
        /// (never overwritten).
        /// </summary>
        static Texture WriteMask(MaskSource source, Func<string, string> pathOf, string folder, OpContext ctx,
                                 out bool written, out string why)
        {
            written = false;
            string path = folder + "/" + source.Name + ".png";
            string full = Path.GetFullPath(FileUtil.GetPhysicalPath(path));
            byte[] held = File.Exists(full) ? File.ReadAllBytes(full) : null;
            if (held != null && MaskTextures.StampOf(held) == null)
            {
                why = $"'{path}' is a file unitytk did not write, and is not overwritten: rename or move it, or bind " +
                      "the mask from another folder";
                return null;
            }
            Texture2D packed = MaskTextures.Make(source, pathOf, out why);
            if (packed == null) return null;
            byte[] png = MaskTextures.Stamped(packed.EncodeToPNG(), source.Name);
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (asset != null && held != null && Same(held, png))
            {
                UnityEngine.Object.DestroyImmediate(packed);
                return asset;
            }
            written = true;
            if (ctx != null && !ctx.ShouldWrite(path, "write the packed metal / smoothness map")) return packed;
            UnityEngine.Object.DestroyImmediate(packed);
            File.WriteAllBytes(full, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
