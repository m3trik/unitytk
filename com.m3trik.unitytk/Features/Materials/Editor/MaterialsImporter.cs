// ----------------------------------------------------------------------------
// The Materials feature's editor part (M3trik.Unitytk.Materials.Editor), all of
// it editor code: nothing reaches a player build.
//
// A delivery states its materials in its materials record (data_export.materials,
// MaterialRecord): literal glTF 2.0, each material named for the FBX material it
// describes, each image by the file the delivery carries. On import, each
// material the record names is written from its glTF material through the
// kernel's one writer (MaterialWriter), onto the render pipeline's own Lit (its
// Unlit for an unlit one); a material it does not name, and a file without a
// record, keep Unity's own import. The record is read off the file in
// OnPreprocessModel (IRecordSource.Record): the user properties that carry it
// reach the importers only after every material description (measured
// 2026-10-31, 6000.3.10f1).
//
// Unity's description preprocessors run at GetPostprocessOrder -990 (Built-in)
// and -980 / -970 / -960 (URP); this importer runs after all of them
// (ImportOrder.AfterMaterialDescriptions), on the material they made, and the
// result is final: it survives a forced reimport.
//
// Maps: Unity binds an EMBEDDED image only once it is extracted, so the images
// the record names are requested through the carrier (IModelMedia: one
// extraction and reimport per version of the model) and found by file name in
// the model's media folders. Each gets its role on its first import
// (MaterialsTextureRoles): a normal map as a normal map, data maps linear. The
// metallicRoughness and occlusion images, which no pipeline reads in glTF's
// layout, are repacked into Unity's mask (MaskMap) as a texture the import owns
// (a sub-asset of the model, rebuilt with it, as Unity's own materials are),
// depending on its source images, so an edited image repacks. An image the
// record names that the model's media do not hold, once extracted, is warned
// and reads as absent.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Entry = MaterialRecord.Shape.Entry;
using Payload = MaterialRecord.Shape.Payload;

[assembly: ReadsRecord(UnitytkRecords.Materials, typeof(MaterialRecord))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Materials.Tests")]

namespace M3trik.Unitytk.Materials
{
    /// <summary>
    /// The importer: writes each material a delivery's <c>materials</c> record names
    /// from its glTF material, through the kernel's one writer
    /// (<see cref="MaterialWriter"/>) onto the render pipeline's own Lit; asks the
    /// carrier to extract the images the record names, repacks metal, roughness and
    /// occlusion into a mask the import owns, and records each image's role for its
    /// first import (<see cref="MaterialsTextureRoles"/>). It reads FBX deliveries
    /// alone (<see cref="Imports.IsDelivery"/>), and leaves a file without a record,
    /// and a material it does not name, as Unity imports them.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own material description preprocessors map an FBX material
    /// by its shading model, each in its own way and with what the FBX can say (no
    /// packed maps, no alpha mode), so this rewrites the material they made, last
    /// (<see cref="ImportOrder.AfterMaterialDescriptions"/>), from the record, which
    /// states it in glTF's open terms. Unity's material remapping
    /// (<c>ModelImporter.SearchAndRemapMaterials</c>) binds a project material by name,
    /// never the file's values. glTFast's and UnityGLTF's importers read glTF materials
    /// too, but from a glTF file, onto shader graphs of their own
    /// (<see cref="MaterialWriter"/>'s remarks); the record arrives in an FBX.
    /// </remarks>
    public class MaterialsImporter : AssetPostprocessor
    {
        const string Tag = "Materials";

        /// <summary>Bump to reimport every model when the mapping changes.</summary>
        const uint VERSION = 4;

        /// <summary>After every Unity description preprocessor, so this one writes last.</summary>
        public override int GetPostprocessOrder() => ImportOrder.AfterMaterialDescriptions;

        /// <inheritdoc/>
        public override uint GetVersion() => VERSION;

        /// <summary>What one model's import reads and does, from its first callback to its last.</summary>
        internal sealed class Delivery
        {
            /// <summary>The record, read through its shape.</summary>
            public Payload record;
            /// <summary>Its materials by name.</summary>
            public Dictionary<string, Entry> entries;
            /// <summary>The folders its images are looked for in, nearest first.</summary>
            public string[] folders;
            /// <summary>The record's materials the model has, by entry name.</summary>
            public readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            /// <summary>What each written material became, for one log line.</summary>
            public readonly List<string> shaded = new List<string>();
            /// <summary>Every image its materials name, for the extraction request.</summary>
            public readonly HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>The images not found in its media folders.</summary>
            public readonly List<string> missing = new List<string>();
        }

        // This model's import, OnPreprocessModel to OnPostprocessModel: Unity makes one
        // postprocessor per import and calls each callback of that import on it, so what an
        // import reads and makes -- the masks, Unity objects -- is held by the import alone,
        // never in a static, and goes with it when the import fails.
        Delivery _delivery;
        readonly Dictionary<MaskSource, Texture2D> _masks = new Dictionary<MaskSource, Texture2D>();

        // An image's file name -> per model, what it holds, read on the texture's FIRST
        // import (no .meta yet) only, in the model's folder of its own
        // (IModelMedia.InOwnMediaFolder), where every extraction imports its files first.
        static readonly Dictionary<string, Dictionary<string, TextureRole>> _roles =
            new Dictionary<string, Dictionary<string, TextureRole>>(StringComparer.OrdinalIgnoreCase);

        // The pipeline no host serves, once said this session: by its name, so a session
        // that switches to another such pipeline is told of that one too.
        static string _warnedPipeline;

        static IRecordSource Records => Provided.One<IRecordSource>();
        static IModelMedia Media => Provided.One<IModelMedia>();

        void OnPreprocessModel()
        {
            _masks.Clear();
            _delivery = Read(assetPath);
        }

        /// <summary>
        /// What the model at <paramref name="assetPath"/> carries for its materials: its
        /// record read off its file through <paramref name="records"/> (default: the
        /// carrier) at <see cref="MaterialRecord.CurrentVersion"/> alone, and the folders
        /// its images are looked for in (<paramref name="media"/>, default: the carrier);
        /// null where the model is no delivery (<see cref="Imports.IsDelivery"/>), carries
        /// no record, or the record is refused (the records provider says why -- a v1
        /// record: re-export it -- and this says what follows: Unity's import stands).
        /// </summary>
        internal static Delivery Read(string assetPath, IRecordSource records = null, IModelMedia media = null)
        {
            if (!Imports.IsDelivery(assetPath)) return null;
            if (records == null) records = Records;
            string json = records.Record(assetPath, UnitytkRecords.Materials);
            var record = records.Read(UnitytkRecords.Materials, json, MaterialRecord.CurrentVersion,
                                      Payload.Read, Tag, assetPath);
            if (record == null)
            {
                if (!string.IsNullOrEmpty(json))
                    ImportLog.Info(assetPath, $"its {UnitytkRecords.Materials} record is not read, so its materials " +
                                              "keep Unity's own import.");
                return null;
            }
            return new Delivery
            {
                record = record,
                entries = MaterialRecord.Entries(record),
                folders = (media ?? Media).MediaFolders(assetPath),
            };
        }

        void OnPreprocessMaterialDescription(
            UnityEditor.AssetImporters.MaterialDescription description, Material material, AnimationClip[] clips)
        {
            var delivery = _delivery;
            if (delivery == null) return;
            var entry = MaterialRecord.Find(delivery.entries, description.materialName)
                        ?? MaterialRecord.Find(delivery.entries, material.name);
            if (entry == null) return;
            string label = Shade(material, entry, delivery, RenderPipelineHosts.Current, assetPath, MakeMask);
            if (label != null) delivery.shaded.Add($"{entry.name} ({label})");
        }

        // The mask this import owns: a sub-asset of the model, depending on its source images.
        Texture MakeMask(Delivery delivery, MaskSource source)
        {
            if (_masks.TryGetValue(source, out var made)) return made;
            Texture2D mask = MaskTextures.Make(source, file => MediaFiles.Nearest(delivery.folders, file), out string why);
            if (mask == null)
            {
                ImportLog.Warning(assetPath, $"the mask '{source.Name}' could not be packed: {why}.");
                return null;
            }
            try
            {
                // Compressed for the build target as its source image is: reading the target makes
                // the import depend on it, so a switch of platform reimports the model.
                BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(context.selectedBuildTarget);
                string platform = NamedBuildTarget.FromBuildTargetGroup(group).TargetName;
                string like = MediaFiles.Nearest(delivery.folders, source.metallicRoughness ?? source.occlusion);
                MaskTextures.Compress(mask, platform, like == null ? null : AssetImporter.GetAtPath(like) as TextureImporter);
                context.AddObjectToAsset("unitytk/" + source.Name, mask);
            }
            catch
            {
                // Not the import's yet: released here, as nothing else holds it.
                UnityEngine.Object.DestroyImmediate(mask);
                throw;
            }
            foreach (string file in new[] { source.metallicRoughness, source.occlusion })
            {
                string path = MediaFiles.Nearest(delivery.folders, file);
                // Not a file in the model's .fbm: Unity's External-materials import rewrites it every import.
                if (path != null && !path.Contains(".fbm/")) context.DependsOnSourceAsset(path);
            }
            _masks[source] = mask;
            return mask;
        }

        /// <summary>
        /// Write <paramref name="material"/> from <paramref name="entry"/>, one of
        /// <paramref name="delivery"/>'s, through <paramref name="host"/>'s table, its
        /// images found in the delivery's folders and its mask made by
        /// <paramref name="mask"/>; each loss said in the import log under
        /// <paramref name="assetPath"/>. Returns what it was written as, or null where it
        /// keeps Unity's import: no host serves the pipeline (said once per session) or
        /// the project lacks the shader.
        /// </summary>
        /// <param name="host">The render pipeline's host, or null for one no door serves (HDRP).</param>
        internal static string Shade(Material material, Entry entry, Delivery delivery, IRenderPipelineHost host,
                                     string assetPath, Func<Delivery, MaskSource, Texture> mask)
        {
            delivery.seen.Add(entry.name);
            var losses = new List<string>();
            var surface = GltfSurfaces.Surface(delivery.record, entry, file => Image(delivery, file, assetPath),
                                               source => mask(delivery, source), delivery.missing, losses);
            NoteRoles(delivery.record, entry, assetPath);
            if (!MaterialWriter.Write(material, surface, host, losses))
            {
                string pipeline = RenderPipelineHosts.NameOf(host);
                if (host == null)
                {
                    if (_warnedPipeline == pipeline) return null;
                    // It speaks for every model, so it names none.
                    _warnedPipeline = pipeline;
                    ImportLog.Warning(null, "materials are written from the materials record where a unitytk render-" +
                                            $"pipeline host serves the pipeline; the {pipeline} pipeline has none, so " +
                                            "Unity's own FBX material import stands.");
                }
                else
                {
                    ImportLog.Warning(assetPath, $"the {host.Name} pipeline's shader for '{entry.name}' is not in the " +
                                                 "project -- it keeps Unity's import.");
                }
                return null;
            }
            foreach (string loss in losses)
                ImportLog.Warning(assetPath, $"'{entry.name}': {loss}.");
            return material.shader.name;
        }

        // An image of the delivery by file name, the model's own copy (its media folder, then
        // its folder), and wanted for the extraction whether found or not.
        static Texture Image(Delivery delivery, string file, string assetPath)
        {
            delivery.wanted.Add(file);
            string path = MediaFiles.Nearest(delivery.folders, file);
            return path != null ? AssetDatabase.LoadAssetAtPath<Texture>(path) : null;
        }

        // Each image's role, for its first import: the normal map a normal map, the
        // packed and occlusion images data; a file a colour slot also samples keeps colour.
        static void NoteRoles(Payload record, Entry e, string assetPath)
        {
            var p = e.pbrMetallicRoughness;
            Note(MaterialRecord.ImageOf(record, p?.baseColorTexture?.index), TextureRole.Colour, assetPath);
            Note(MaterialRecord.ImageOf(record, e.emissiveTexture?.index), TextureRole.Colour, assetPath);
            Note(MaterialRecord.ImageOf(record, p?.metallicRoughnessTexture?.index), TextureRole.Data, assetPath);
            Note(MaterialRecord.ImageOf(record, e.occlusionTexture?.index), TextureRole.Data, assetPath);
            Note(MaterialRecord.ImageOf(record, e.normalTexture?.index), TextureRole.Normal, assetPath);
        }

        /// <summary>Record that the model at <paramref name="assetPath"/>'s materials read <paramref name="file"/> as <paramref name="role"/>.</summary>
        internal static void Note(string file, TextureRole role, string assetPath)
        {
            if (string.IsNullOrEmpty(file)) return;
            if (!_roles.TryGetValue(file, out var byModel))
                _roles[file] = byModel = new Dictionary<string, TextureRole>(StringComparer.OrdinalIgnoreCase);
            if (role == TextureRole.Colour || !byModel.TryGetValue(assetPath, out var known) || known != TextureRole.Colour)
                byModel[assetPath] = role;
        }

        /// <summary>
        /// Whether a material written on import named the file at
        /// <paramref name="textureAssetPath"/>, by its name: what a texture's first import asks first.
        /// </summary>
        internal static bool Named(string textureAssetPath) => _roles.ContainsKey(Path.GetFileName(textureAssetPath));

        /// <summary>
        /// The role a map takes on its first import at <paramref name="textureAssetPath"/>:
        /// the one a model whose materials name its file recorded, where the file lies in
        /// that model's folder of its own (<see cref="IModelMedia.InOwnMediaFolder"/>); else null.
        /// </summary>
        internal static TextureRole? RoleOf(string textureAssetPath, IModelMedia media)
        {
            if (!_roles.TryGetValue(Path.GetFileName(textureAssetPath), out var byModel)) return null;
            foreach (var pair in byModel)
                if (media.InOwnMediaFolder(pair.Key, textureAssetPath)) return pair.Value;
            return null;
        }

        /// <summary>Forget the pipeline the once-per-session warning named, so a test hears it again.</summary>
        internal static void ForgetWarnedPipeline() => _warnedPipeline = null;

        void OnPostprocessModel(GameObject root)
        {
            var delivery = _delivery;
            _delivery = null;
            _masks.Clear();  // the import owns them now (AddObjectToAsset)
            if (delivery == null) return;
            Finish(delivery, assetPath, assetImporter as ModelImporter, Media);
        }

        /// <summary>
        /// The end of the model's import: one line for what was written; a warning for
        /// each material the record names that the model has none of (one remapped to a
        /// project material is the person's: <see cref="MaterialsActions.Reapply"/> writes
        /// it); the images its materials name asked of <paramref name="media"/>'s
        /// extraction, and those still not found once nothing is left to extract warned.
        /// </summary>
        internal static void Finish(Delivery delivery, string assetPath, ModelImporter importer, IModelMedia media)
        {
            if (delivery.shaded.Count > 0)
                ImportLog.Info(assetPath, $"wrote {delivery.shaded.Count} material(s) from its materials record: " +
                                          string.Join(", ", delivery.shaded) + ".");
            var remapped = new HashSet<string>(StringComparer.Ordinal);
            if (importer != null)
                foreach (var pair in importer.GetExternalObjectMap())
                    if (pair.Key.type == typeof(Material)) remapped.Add(pair.Key.name);
            foreach (var name in delivery.entries.Keys)
            {
                if (delivery.seen.Contains(name)) continue;
                string leaf = RecordNames.Leaf(name);
                if (remapped.Contains(name) || remapped.Contains(leaf)) continue;
                ImportLog.Warning(assetPath, $"its materials record names '{name}', which the model has no material " +
                                             "of: nothing is written for it (re-export the file, or rename the material).");
            }
            bool asked = delivery.wanted.Count > 0 && media.RequestEmbeddedFiles(assetPath, delivery.wanted);
            if (!asked && delivery.missing.Count > 0)
                ImportLog.Warning(assetPath, $"its materials record names {delivery.missing.Count} image(s) its media do not " +
                                             $"hold: {string.Join(", ", delivery.missing)}. Each reads as absent (a colour or " +
                                             "emission its factor alone, a flat normal, no occlusion, metal and roughness " +
                                             "dielectric and matte); re-export the file with its images embedded.");
        }
    }
}
