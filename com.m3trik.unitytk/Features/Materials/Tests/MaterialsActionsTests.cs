using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Materials.Tests
{
    /// <summary>
    /// The actions on any material, each held to the kernel's three checks -- one
    /// Ctrl+Z after a menu run restores the material, the dry run's plan names every
    /// change the run makes, a second run plans nothing -- through whichever pipeline
    /// the project draws with: Apply from a glTF document (a plain glTF file's JSON, its
    /// defaults filled), PackMetallicSmoothness, FixTextureRoles, and Reapply onto a
    /// model's remapped materials; and what each refuses.
    /// </summary>
    public class MaterialsActionsTests
    {
        const string Folder = "Assets/UnitytkMaterialsActionsTests";
        const string MaterialPath = Folder + "/Crate.mat";

        // A glTF file's own JSON: no record version, and what glTF lets a material leave out left out.
        const string Gltf = @"{""asset"": {""version"": ""2.0""}, ""materials"": [{""name"": ""Crate"",
            ""pbrMetallicRoughness"": {""baseColorFactor"": [0.5, 0.25, 0.125, 1], ""baseColorTexture"": {""index"": 0},
              ""roughnessFactor"": 0.5, ""metallicRoughnessTexture"": {""index"": 1}},
            ""normalTexture"": {""index"": 2}}],
            ""textures"": [{""source"": 0}, {""source"": 1}, {""source"": 2}],
            ""images"": [{""uri"": ""Crate_BaseColor.png""}, {""uri"": ""Crate_ORM.png""}, {""uri"": ""Crate_Normal.png""}]}";

        LitTable _table;
        Material _material;

        [SetUp]
        public void SetUp()
        {
            var host = RenderPipelineHosts.Current;
            Assume.That(host != null, "a pipeline a unitytk host serves");
            _table = host.Lit;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            Image("Crate_BaseColor.png", new Color32(200, 150, 100, 255));
            Image("Crate_ORM.png", new Color32(255, 128, 0, 255));
            Image("Crate_Normal.png", new Color32(128, 128, 255, 255));
            AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Color")) { color = Color.magenta }, MaterialPath);
            _material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

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

        static Texture Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture>(Folder + "/" + name);

        static void AssertSamePlan(OpContext plan, OpContext live)
            => Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)),
                           Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))));

        // -- Apply --------------------------------------------------------------------------

        [Test]
        public void Apply_writes_a_gltf_material_through_the_pipelines_lit_and_one_ctrl_z_undoes_it()
        {
            bool changed = OpContext.RunMenu("Apply glTF Material", ctx => MaterialsActions.Apply(_material, Gltf, ctx: ctx));
            Assert.That(changed, Is.True);
            Assert.That(_material.shader.name, Is.EqualTo(_table.Lit));
            Assert.That(_material.GetTexture(_table.BaseMap), Is.SameAs(Texture("Crate_BaseColor.png")));
            var mask = _material.GetTexture(_table.MaskMap);
            Assert.That(AssetDatabase.GetAssetPath(mask), Is.EqualTo(Folder + "/Crate_ORM_MetallicSmoothness_m1_r0.5.png"),
                        "the mask beside the material, its factors in its name");
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mask))).sRGBTexture, Is.False);
            Assert.That(_material.GetTexture(_table.NormalMap), Is.SameAs(Texture("Crate_Normal.png")));

            Undo.PerformUndo();
            Assert.That(_material.shader.name, Is.EqualTo("Unlit/Color"), "one Ctrl+Z put the material back");
            Assert.That(_material.color, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void Apply_plans_what_it_then_does_and_a_second_run_plans_nothing()
        {
            var plan = new OpContext(dryRun: true);
            Assert.That(MaterialsActions.Apply(_material, Gltf, ctx: plan), Is.True);
            Assert.That(_material.shader.name, Is.EqualTo("Unlit/Color"), "a dry run changes nothing");
            Assert.That(File.Exists(Folder + "/Crate_ORM_MetallicSmoothness_m1_r0.5.png"), Is.False, "nor writes a file");
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "write", "change" }));

            var live = new OpContext();
            MaterialsActions.Apply(_material, Gltf, ctx: live);
            AssertSamePlan(plan, live);

            var again = new OpContext(dryRun: true);
            Assert.That(MaterialsActions.Apply(_material, Gltf, ctx: again), Is.False);
            Assert.That(again.Changes, Is.Empty);
        }

        [Test]
        public void Apply_refuses_an_imports_own_material_a_name_it_lacks_and_a_v1_record()
        {
            Assert.Throws<ArgumentException>(() => MaterialsActions.Apply(_material, Gltf, "Barrel"));
            Assert.Throws<ArgumentException>(() => MaterialsActions.Apply(_material, "{\"version\": 1, \"materials\": []}"));
            Assert.Throws<ArgumentException>(() => MaterialsActions.Apply(_material, "not json"));

            const string model = Folder + "/delivery.fbx";
            DeliveryFbx.Write(model, null, "Crate");
            AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
            var own = AssetDatabase.LoadAllAssetsAtPath(model).OfType<Material>().Single();
            var refused = Assert.Throws<ArgumentException>(() => MaterialsActions.Apply(own, Gltf));
            Assert.That(refused.Message, Does.Contain("next import rewrites"));
        }

        [Test]
        public void Apply_names_an_image_it_cannot_find_and_reads_it_as_absent()
        {
            var ctx = new OpContext();
            MaterialsActions.Apply(_material, Gltf.Replace("Crate_Normal.png", "Crate_Gone.png"), ctx: ctx);
            Assert.That(ctx.Issues.Select(i => i.code), Has.Member(MaterialsActions.MissingImageCode));
            Assert.That(_material.GetTexture(_table.NormalMap), Is.Null, "a flat normal");
        }

        [Test]
        public void Apply_names_a_gltf_material_glTF_left_unnamed_for_its_target()
        {
            // glTF makes a material's name optional; the record requires one.
            string unnamed = Gltf.Replace(@"{""name"": ""Crate"",", "{");
            Assert.That(MaterialsActions.Apply(_material, unnamed), Is.True);
            Assert.That(_material.shader.name, Is.EqualTo(_table.Lit));
            Assert.That(_material.GetTexture(_table.BaseMap), Is.SameAs(Texture("Crate_BaseColor.png")));
        }

        // -- PackMetallicSmoothness ---------------------------------------------------------

        [Test]
        public void Pack_metallic_smoothness_never_overwrites_a_file_of_its_name_it_did_not_write()
        {
            _material.shader = Shader.Find(_table.Lit);
            Image("Crate_ORM_MetallicSmoothness.png", new Color32(1, 2, 3, 4));
            string full = Path.GetFullPath(Folder + "/Crate_ORM_MetallicSmoothness.png");
            byte[] theirs = File.ReadAllBytes(full);
            var ctx = new OpContext();
            var orm = Texture("Crate_ORM.png");
            Assert.That(MaterialsActions.PackMetallicSmoothness(_material, orm, orm, ctx), Is.False);
            Assert.That(File.ReadAllBytes(full), Is.EqualTo(theirs), "left as it is");
            var issue = ctx.Issues.Single();
            Assert.That(issue.code, Is.EqualTo(MaterialsActions.MissingImageCode));
            Assert.That(issue.message, Does.Contain("unitytk did not write").And.Contain("rename or move it"));
            Assert.That(_material.GetTexture(_table.MaskMap), Is.Null);

            // Its own file, once written, it rewrites.
            File.Delete(full);
            AssetDatabase.Refresh();
            Assert.That(MaterialsActions.PackMetallicSmoothness(_material, orm, orm), Is.True);
            byte[] ours = File.ReadAllBytes(full);
            Assert.That(MaskTextures.StampOf(ours), Is.EqualTo("Crate_ORM_MetallicSmoothness"), "stamped as this package's");
        }

        [Test]
        public void Pack_metallic_smoothness_binds_unitys_mask_and_holds_to_the_kernel_checks()
        {
            _material.shader = Shader.Find(_table.Lit);
            var orm = Texture("Crate_ORM.png");
            var plan = new OpContext(dryRun: true);
            Assert.That(MaterialsActions.PackMetallicSmoothness(_material, orm, orm, plan), Is.True);
            Assert.That(_material.GetTexture(_table.MaskMap), Is.Null, "a dry run binds nothing");

            bool changed = OpContext.RunMenu("Pack Metallic Smoothness",
                ctx => MaterialsActions.PackMetallicSmoothness(_material, orm, orm, ctx));
            Assert.That(changed, Is.True);
            var mask = (Texture2D)_material.GetTexture(_table.MaskMap);
            Assert.That(AssetDatabase.GetAssetPath(mask), Is.EqualTo(Folder + "/Crate_ORM_MetallicSmoothness.png"));
            if (_table.OcclusionMap != null) Assert.That(_material.GetTexture(_table.OcclusionMap), Is.SameAs(mask));

            var again = new OpContext(dryRun: true);
            Assert.That(MaterialsActions.PackMetallicSmoothness(_material, orm, orm, again), Is.False);
            Assert.That(again.Changes, Is.Empty);

            Undo.PerformUndo();
            Assert.That(_material.GetTexture(_table.MaskMap), Is.Null, "one Ctrl+Z unbound it (the file it wrote stays)");
            Assert.That(File.Exists(Folder + "/Crate_ORM_MetallicSmoothness.png"), Is.True);
        }

        [Test]
        public void Pack_metallic_smoothness_plans_what_it_then_does()
        {
            _material.shader = Shader.Find(_table.Lit);
            var orm = Texture("Crate_ORM.png");
            var plan = new OpContext(dryRun: true);
            MaterialsActions.PackMetallicSmoothness(_material, orm, null, plan);
            var live = new OpContext();
            MaterialsActions.PackMetallicSmoothness(_material, orm, null, live);
            AssertSamePlan(plan, live);
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "write", "change" }));
        }

        [Test]
        public void Pack_metallic_smoothness_refuses_a_material_off_the_pipelines_lit()
        {
            var orm = Texture("Crate_ORM.png");
            Assert.Throws<ArgumentException>(() => MaterialsActions.PackMetallicSmoothness(_material, orm));
        }

        // -- FixTextureRoles ----------------------------------------------------------------

        [Test]
        public void Fix_texture_roles_imports_each_map_as_its_slot_reads_it()
        {
            _material.shader = Shader.Find(_table.Lit);
            _material.SetTexture(_table.NormalMap, Texture("Crate_Normal.png"));
            _material.SetTexture(_table.MaskMap, Texture("Crate_ORM.png"));
            _material.SetTexture(_table.BaseMap, Texture("Crate_BaseColor.png"));

            var plan = new OpContext(dryRun: true);
            Assert.That(MaterialsActions.FixTextureRoles(_material, plan), Is.EqualTo(2), "the normal map and the mask; the colour map is sRGB already");
            var live = new OpContext();
            Assert.That(MaterialsActions.FixTextureRoles(_material, live), Is.EqualTo(2));
            AssertSamePlan(plan, live);
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(Folder + "/Crate_Normal.png")).textureType,
                        Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(Folder + "/Crate_ORM.png")).sRGBTexture, Is.False);

            var again = new OpContext(dryRun: true);
            Assert.That(MaterialsActions.FixTextureRoles(_material, again), Is.EqualTo(0));
            Assert.That(again.Changes, Is.Empty);
        }

        // -- Reapply --------------------------------------------------------------------------

        [Test]
        public void Reapply_writes_the_record_onto_a_models_remapped_material_and_holds_to_the_kernel_checks()
        {
            const string model = Folder + "/delivery.fbx";
            string record = @"{""version"": 2, ""asset"": {""version"": ""2.0""}, ""materials"": [{""name"": ""Crate"",
                ""pbrMetallicRoughness"": {""baseColorFactor"": [0.5, 0.25, 0.125, 1], ""baseColorTexture"": {""index"": 0},
                  ""metallicFactor"": 0, ""roughnessFactor"": 0.5}, ""emissiveFactor"": [0, 0, 0], ""alphaMode"": ""OPAQUE"",
                ""doubleSided"": false}], ""textures"": [{""source"": 0}], ""images"": [{""uri"": ""Crate_BaseColor.png""}]}";
            DeliveryFbx.Write(model, record, "Crate");
            AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(model);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Crate"), _material);
            importer.SaveAndReimport();
            Assert.That(_material.shader.name, Is.EqualTo("Unlit/Color"), "remapped: the person's material, untouched by the import");

            var plan = new OpContext(dryRun: true);
            Assert.That(MaterialsActions.Reapply(model, plan), Is.EqualTo(1));
            bool done = OpContext.RunMenu("Reapply Materials", ctx => MaterialsActions.Reapply(model, ctx) == 1);
            Assert.That(done, Is.True);
            Assert.That(_material.shader.name, Is.EqualTo(_table.Lit));
            Assert.That(_material.GetTexture(_table.BaseMap), Is.SameAs(Texture("Crate_BaseColor.png")));

            var again = new OpContext(dryRun: true);
            Assert.That(MaterialsActions.Reapply(model, again), Is.EqualTo(0));
            Assert.That(again.Changes, Is.Empty);

            Undo.PerformUndo();
            Assert.That(_material.shader.name, Is.EqualTo("Unlit/Color"), "one Ctrl+Z put it back");
        }

        [Test]
        public void Reapply_says_a_model_carries_no_record()
        {
            const string model = Folder + "/plain.fbx";
            DeliveryFbx.Write(model, null, "Crate");
            AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
            var ctx = new OpContext();
            Assert.That(MaterialsActions.Reapply(model, ctx), Is.EqualTo(0));
            Assert.That(ctx.Issues.Single().code, Is.EqualTo(MaterialsActions.NoRecordCode));
            Assert.Throws<ArgumentException>(() => MaterialsActions.Reapply(Folder + "/none.fbx"));
        }
    }
}
