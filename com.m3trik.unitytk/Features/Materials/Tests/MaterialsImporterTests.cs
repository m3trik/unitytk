using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Materials.Tests
{
    /// <summary>
    /// The importer, over real imports of a binary FBX delivery whose carrier holds a
    /// materials record (v2, literal glTF), its images beside it: each material the
    /// record names is written through the pipeline's own table (whichever pipeline
    /// the project draws with: these run in the URP and the Built-in project alike) --
    /// metal, roughness and occlusion repacked into a mask the import owns, emission
    /// above 1, unlit, the alpha modes, double-sided, the UV transform; a missing image
    /// reads as absent and is warned; a material the record names that the model lacks
    /// is warned; a v1 record is refused with its fix, and it and no record leave Unity's
    /// import as it is (no fallback reading); a reimport writes the same; and the
    /// writer's once-per-session word for a pipeline no host serves.
    /// </summary>
    public class MaterialsImporterTests
    {
        const string Folder = "Assets/UnitytkMaterialsTests";
        const string Model = Folder + "/delivery.fbx";
        static readonly string[] Names = { "Ball_mat", "Glow_mat", "Sign_mat", "Leaf_mat", "Cast_mat" };

        const string Record = @"{""version"": 2, ""asset"": {""version"": ""2.0""},
            ""extensionsUsed"": [""KHR_materials_emissive_strength"", ""KHR_materials_unlit"", ""KHR_texture_transform""],
            ""materials"": [
              {""name"": ""Ball_mat"", ""pbrMetallicRoughness"": {""baseColorFactor"": [1, 1, 1, 1], ""baseColorTexture"": {""index"": 0},
                ""metallicFactor"": 1, ""roughnessFactor"": 1, ""metallicRoughnessTexture"": {""index"": 1}},
               ""normalTexture"": {""index"": 2, ""scale"": 0.75}, ""occlusionTexture"": {""index"": 1},
               ""emissiveFactor"": [0, 0, 0], ""alphaMode"": ""OPAQUE"", ""doubleSided"": false},
              {""name"": ""Glow_mat"", ""pbrMetallicRoughness"": {""baseColorFactor"": [0, 0, 0, 1], ""metallicFactor"": 0, ""roughnessFactor"": 1},
               ""emissiveTexture"": {""index"": 3}, ""emissiveFactor"": [1, 0.5, 0.25],
               ""extensions"": {""KHR_materials_emissive_strength"": {""emissiveStrength"": 4}},
               ""alphaMode"": ""OPAQUE"", ""doubleSided"": true},
              {""name"": ""Sign_mat"", ""pbrMetallicRoughness"": {""baseColorFactor"": [1, 1, 1, 0.5],
                ""baseColorTexture"": {""index"": 4, ""extensions"": {""KHR_texture_transform"": {""offset"": [0.25, 0.5], ""scale"": [2, 3]}}},
                ""metallicFactor"": 0, ""roughnessFactor"": 1},
               ""emissiveFactor"": [0, 0, 0], ""alphaMode"": ""BLEND"", ""doubleSided"": false,
               ""extensions"": {""KHR_materials_unlit"": {}}},
              {""name"": ""Leaf_mat"", ""pbrMetallicRoughness"": {""baseColorFactor"": [0.2, 0.6, 0.2, 1], ""metallicFactor"": 1,
                ""roughnessFactor"": 0.5, ""metallicRoughnessTexture"": {""index"": 5}},
               ""emissiveFactor"": [0, 0, 0], ""alphaMode"": ""MASK"", ""alphaCutoff"": 0.3, ""doubleSided"": true},
              {""name"": ""Cast_mat"", ""pbrMetallicRoughness"": {""baseColorFactor"": [0.5, 0.5, 0.5, 1], ""metallicFactor"": 0.3,
                ""roughnessFactor"": 0.7, ""metallicRoughnessTexture"": {""index"": 5}},
               ""emissiveTexture"": {""index"": 5}, ""emissiveFactor"": [1, 1, 1],
               ""extensions"": {""KHR_materials_emissive_strength"": {""emissiveStrength"": 2}},
               ""alphaMode"": ""OPAQUE"", ""doubleSided"": false},
              {""name"": ""Ghost_mat"", ""pbrMetallicRoughness"": {""baseColorFactor"": [1, 1, 1, 1], ""metallicFactor"": 0, ""roughnessFactor"": 1},
               ""emissiveFactor"": [0, 0, 0], ""alphaMode"": ""OPAQUE"", ""doubleSided"": false}],
            ""textures"": [{""source"": 0}, {""source"": 1}, {""source"": 2}, {""source"": 3}, {""source"": 4}, {""source"": 5}],
            ""images"": [{""uri"": ""Ball_BaseColor.png""}, {""uri"": ""Ball_ORM.png""}, {""uri"": ""Ball_Normal.png""},
                         {""uri"": ""Glow_Emissive.png""}, {""uri"": ""Sign_BaseColor.png""}, {""uri"": ""Leaf_Gone_ORM.png""}]}";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        static void Image(string name, Color32 texel)
        {
            var image = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            image.SetPixels32(Enumerable.Repeat(texel, 16).ToArray());
            File.WriteAllBytes(Folder + "/" + name, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(Folder + "/" + name, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// The delivery's images beside it (the one image Leaf's record names left out), then
        /// the model, its materials named <paramref name="names"/> (default <see cref="Names"/>).
        /// </summary>
        static void Import(string record, string[] names = null)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            Image("Ball_BaseColor.png", new Color32(255, 255, 255, 255));
            Image("Ball_ORM.png", new Color32(128, 102, 255, 255));  // occlusion 0.5, roughness 0.4, metal 1
            Image("Ball_Normal.png", new Color32(128, 128, 255, 255));
            Image("Glow_Emissive.png", new Color32(255, 255, 255, 255));
            Image("Sign_BaseColor.png", new Color32(255, 0, 0, 128));
            DeliveryFbx.Write(Model, record, names ?? Names);
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        static Material Material(string name)
            => AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Material>().Single(m => m.name == name);

        static LitTable Table()
        {
            var host = RenderPipelineHosts.Current;
            Assume.That(host != null, "a pipeline a unitytk host serves");
            return host.Lit;
        }

        static void AssertColour(Color stored, Color linear, string why)
        {
            var encoded = MaterialColor.Encode(linear);
            Assert.That(new[] { stored.r, stored.g, stored.b, stored.a },
                        Is.EqualTo(new[] { encoded.r, encoded.g, encoded.b, encoded.a }).Within(1e-3f), why);
        }

        [Test]
        public void Each_named_material_is_written_on_the_pipelines_lit_its_mask_owned_by_the_import()
        {
            var table = Table();
            Import(Record);
            var ball = Material("Ball_mat");
            Assert.That(ball.shader.name, Is.EqualTo(table.Lit));
            Assert.That(ball.GetTexture(table.BaseMap).name, Is.EqualTo("Ball_BaseColor"));
            var mask = ball.GetTexture(table.MaskMap) as Texture2D;
            Assert.That(mask, Is.Not.Null, "metal / roughness / occlusion repacked into Unity's mask");
            Assert.That(mask.name, Is.EqualTo("Ball_ORM_MetallicSmoothness"));
            Assert.That(AssetDatabase.GetAssetPath(mask), Is.EqualTo(Model), "a texture the import owns: a sub-asset of the model");
            Assert.That(ball.IsKeywordEnabled(table.MaskKeyword), Is.True);
            if (table.OcclusionMap != null)
                Assert.That(ball.GetTexture(table.OcclusionMap), Is.SameAs(mask), "occlusion read from the mask's G");
            Color texel = mask.GetPixel(1, 1);
            Assert.That(texel.r, Is.EqualTo(1f).Within(0.05f), "R metal: B 1 x factor 1");
            Assert.That(texel.g, Is.EqualTo(0.5f).Within(0.05f), "G occlusion: the ORM's R");
            Assert.That(texel.a, Is.EqualTo(0.6f).Within(0.05f), "A smoothness: 1 - roughness G 0.4");
            Assert.That(ball.GetTexture(table.NormalMap).name, Is.EqualTo("Ball_Normal"));
            Assert.That(ball.GetFloat(table.NormalScale), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(ball.renderQueue, Is.EqualTo((int)RenderQueue.Geometry), "OPAQUE");
        }

        [Test]
        public void Emission_above_one_is_its_factor_times_its_strength()
        {
            var table = Table();
            Import(Record);
            var glow = Material("Glow_mat");
            AssertColour(glow.GetColor(table.EmissionColor), new Color(4f, 2f, 1f, 1f), "factor (1, 0.5, 0.25) x strength 4");
            Assert.That(glow.GetTexture(table.EmissionMap).name, Is.EqualTo("Glow_Emissive"));
            Assert.That(glow.IsKeywordEnabled(table.EmissionKeyword), Is.True);
            Assert.That(glow.doubleSidedGI, Is.True);
            if (table.Cull != null) Assert.That(glow.GetFloat(table.Cull), Is.EqualTo((float)CullMode.Off), "double-sided");
        }

        [Test]
        public void A_namespaced_material_is_written_from_its_entry_whatever_unitys_import_renames_it()
        {
            // A referenced asset's material: the file and the record name it 'ns:Glow_mat'; Unity's
            // import names it 'ns_Glow_mat', in its description too (measured, 6000.3).
            var table = Table();
            Import(Record.Replace("\"Glow_mat\"", "\"ns:Glow_mat\""), Names.Select(n => n == "Glow_mat" ? "ns:Glow_mat" : n).ToArray());
            var glow = Material("ns_Glow_mat");
            AssertColour(glow.GetColor(table.EmissionColor), new Color(4f, 2f, 1f, 1f), "its entry's emission: factor x strength 4");
            Assert.That(glow.GetTexture(table.EmissionMap).name, Is.EqualTo("Glow_Emissive"));
        }

        [Test]
        public void An_unlit_material_is_the_pipelines_unlit_blended_and_its_uv_transform_turned_into_unitys()
        {
            var table = Table();
            Import(Record);
            var sign = Material("Sign_mat");
            Assert.That(sign.shader.name, Is.EqualTo(table.UnlitBlended ?? table.Unlit), "KHR_materials_unlit, BLEND");
            string map = table.UnlitMap ?? table.BaseMap;
            Assert.That(sign.GetTexture(map).name, Is.EqualTo("Sign_BaseColor"));
            Assert.That(sign.GetTextureScale(map), Is.EqualTo(new Vector2(2f, 3f)));
            Assert.That(sign.GetTextureOffset(map), Is.EqualTo(new Vector2(0.25f, 1f - 3f - 0.5f)),
                        "glTF's offset (u, v), origin top left: Unity's (u, 1 - scale v - v)");
            Assert.That(sign.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
        }

        [Test]
        public void A_mask_material_cuts_out_and_its_missing_image_reads_as_absent_and_is_warned()
        {
            var table = Table();
            Import(Record);
            var leaf = Material("Leaf_mat");
            Assert.That(leaf.shader.name, Is.EqualTo(table.Lit));
            Assert.That(leaf.IsKeywordEnabled(table.AlphaTestKeyword), Is.True);
            Assert.That(leaf.GetFloat(table.Cutoff), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(leaf.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
            Assert.That(leaf.GetTexture(table.MaskMap), Is.Null, "its metallicRoughness image is not there");
            Assert.That(leaf.GetFloat(table.Smoothness), Is.EqualTo(1f - 0.5f).Within(1e-5f),
                        "the missing map's roughness constant 0.5 is kept (GltfSurfaces.MetallicWithoutMap's rule)");
            Assert.That(leaf.GetFloat(table.Metallic), Is.EqualTo(MapAbsence.Metallic).Within(1e-5f),
                        "its metal factor 1 stood in for the map: dielectric");

            // The rule's other halves: a constant metal is the material's own, and no glow over a missing map.
            var cast = Material("Cast_mat");
            float metal = cast.GetFloat(table.Metallic);
            int slot = cast.shader.FindPropertyIndex(table.Metallic);
            if ((cast.shader.GetPropertyFlags(slot) & ShaderPropertyFlags.Gamma) != 0 && QualitySettings.activeColorSpace == ColorSpace.Linear)
                metal = Mathf.GammaToLinearSpace(metal);  // Standard's [Gamma] _Metallic is written encoded
            Assert.That(metal, Is.EqualTo(0.3f).Within(1e-3f), "a constant metal kept");
            Assert.That(cast.GetFloat(table.Smoothness), Is.EqualTo(1f - 0.7f).Within(1e-5f), "its roughness kept");
            AssertColour(cast.GetColor(table.EmissionColor), Color.black, "a missing emission map: no emission");

            // Once its media are extracted (the first import asked), the import says so.
            LogAssert.Expect(LogType.Warning, new Regex(@"names 1 image\(s\) its media do not hold: Leaf_Gone_ORM\.png"));
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        [Test]
        public void A_material_the_record_names_that_the_model_lacks_is_warned()
        {
            Table();
            LogAssert.Expect(LogType.Warning, new Regex(@"names 'Ghost_mat', which the model has no material of"));
            Import(Record);
        }

        [Test]
        public void A_reimport_writes_the_same_materials_and_keeps_the_mask_it_owns()
        {
            var table = Table();
            Import(Record);
            var before = Names.ToDictionary(n => n, n => EditorJsonUtility.ToJson(Material(n)));
            var mask = Material("Ball_mat").GetTexture(table.MaskMap);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mask, out string guid, out long id);
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var again = Material("Ball_mat").GetTexture(table.MaskMap);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(again, out string guid2, out long id2);
            Assert.That((guid2, id2), Is.EqualTo((guid, id)), "the same mask, by its identity");
            foreach (var name in Names)
                Assert.That(EditorJsonUtility.ToJson(Material(name)), Is.EqualTo(before[name]), name);
        }

        [Test]
        public void A_v1_record_is_refused_with_its_fix_and_it_and_no_record_keep_unitys_import()
        {
            Table();
            Import(null);
            var unitys = Names.ToDictionary(n => n, n => EditorJsonUtility.ToJson(Material(n)));
            string v1 = @"{""version"": 1, ""materials"": [{""name"": ""Ball_mat"", ""baseColorFactor"": [1, 0, 0, 1],
                ""metallicFactor"": 1, ""roughnessFactor"": 0.2, ""emissiveFactor"": [0, 0, 0], ""alphaMode"": ""OPAQUE""}]}";
            LogAssert.Expect(LogType.Warning, new Regex(@"carries the materials record v1; unitytk reads v2 only.*re-export"));
            Import(v1);
            foreach (var name in Names)
                Assert.That(EditorJsonUtility.ToJson(Material(name)), Is.EqualTo(unitys[name]),
                            $"{name}: Unity's own import, as with no record (no fallback reading of the file's materials)");
            Import(Record);
            Assert.That(EditorJsonUtility.ToJson(Material("Ball_mat")), Is.Not.EqualTo(unitys["Ball_mat"]), "the v2 record is written");
        }

        [Test]
        public void A_pipeline_no_host_serves_keeps_unitys_material_said_once_naming_no_model()
        {
            var records = new PureRecordSource().With(Model, UnitytkRecords.Materials, Record);
            var delivery = MaterialsImporter.Read(Model, records, new PureModelMedia());
            Assert.That(delivery, Is.Not.Null);
            var entry = delivery.entries["Glow_mat"];
            var material = new Material(Shader.Find("Unlit/Color"));
            try
            {
                MaterialsImporter.ForgetWarnedPipeline();
                LogAssert.Expect(LogType.Warning, new Regex(@"^\[unitytk\] materials are written from the materials record"));
                Assert.That(MaterialsImporter.Shade(material, entry, delivery, null, Model, (d, s) => null), Is.Null);
                Assert.That(MaterialsImporter.Shade(material, entry, delivery, null, "Assets/Other.fbx", (d, s) => null), Is.Null,
                            "the second model is not told again");
                LogAssert.NoUnexpectedReceived();
                Assert.That(material.shader.name, Is.EqualTo("Unlit/Color"), "untouched");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void An_asset_that_is_no_delivery_or_carries_another_version_is_not_read()
        {
            string other = Path.ChangeExtension(Model, ".obj");
            var records = new PureRecordSource().With(other, UnitytkRecords.Materials, Record);
            Assert.That(MaterialsImporter.Read(other, records, new PureModelMedia()), Is.Null);
            Assert.That(records.Refusals, Is.Empty, "the record is not even read");

            var v1 = new PureRecordSource().With(Model, UnitytkRecords.Materials, Record.Replace(@"""version"": 2,", @"""version"": 1,"));
            LogAssert.Expect(LogType.Log, new Regex("its materials record is not read, so its materials keep Unity's own import"));
            Assert.That(MaterialsImporter.Read(Model, v1, new PureModelMedia()), Is.Null);
            Assert.That(v1.Refusals.Single(), Does.Contain("materials record v1").And.Contain("re-export"));
        }

        [Test]
        public void It_writes_after_unitys_description_preprocessors_and_the_record_it_reads_is_declared()
        {
            Assert.That(new MaterialsImporter().GetPostprocessOrder(), Is.EqualTo(ImportOrder.AfterMaterialDescriptions));
            Assert.That(ImportOrder.AfterMaterialDescriptions, Is.GreaterThan(ImportOrder.MaxRank * 2 + 1));
            var read = ReadsRecordAttribute.Read(new[] { typeof(MaterialsImporter).Assembly });
            Assert.That(read[UnitytkRecords.Materials], Is.EqualTo(MaterialRecord.CurrentVersion));
        }
    }
}
