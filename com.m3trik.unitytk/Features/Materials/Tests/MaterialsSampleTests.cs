using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Materials.Tests
{
    /// <summary>
    /// The Materials sample, imported as the Package Manager imports it, through
    /// whichever pipeline the project draws with: the metal sphere's colour map, its
    /// metal and roughness (packed in glTF's layout at export, repacked into Unity's
    /// mask by the import, metal on the left half, smooth at the top) and its normal map
    /// (imported as one); the glow four times its colour; the unlit colour; the tiled,
    /// offset colour map back in the producer's own UV terms; and the sample's demo
    /// recipe building its scene, its fifth sphere written by Apply. The project these
    /// run in imports every sample of the package, so a sample that is not there fails:
    /// it is never skipped.
    /// </summary>
    public class MaterialsSampleTests
    {
        const string Package = "com.m3trik.unitytk";
        const string SampleName = "Materials";
        const string Recipe = "M3trik.Unitytk.Samples.Materials.MaterialsDemoRecipe, M3trik.Unitytk.Samples.Materials.Editor";

        /// <summary>The imported sample's folder, project-relative; a failure when it is not imported.</summary>
        static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            Assert.That(sample.displayName, Is.EqualTo(SampleName),
                        "the package lists no Materials sample: its package.json's samples are written by the workspace's sample sync");
            Assert.That(sample.isImported, Is.True,
                        $"the Materials sample is not imported at {sample.importPath}: import it from the Package Manager's " +
                        "Samples tab (the owned project's builder copies every sample in)");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        static string Model() => Folder() + "/Materials.fbx";

        static string NameOf(Texture texture) => texture == null ? null : texture.name;

        [Test]
        public void The_mask_is_compressed_as_the_build_targets_platform_compresses_its_source_image()
        {
            // Measured: with the project on WebGL (ASTC), the mask the import made was DXT5 while
            // every image beside it imported ASTC 6x6: Texture2D.Compress picks the editor's own.
            // Each platform's source here states its own format, so the expectation is the
            // override's, in its family's alpha form.
            const string folder = "Assets/UnitytkMaskCompressionTests";
            string source = folder + "/source.png";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                var image = new Texture2D(8, 8, TextureFormat.RGB24, false);
                File.WriteAllBytes(source, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(source);
                var cases = new (string platform, TextureImporterFormat set, TextureFormat expected)[]
                {
                    ("Android", TextureImporterFormat.ETC2_RGB4, TextureFormat.ETC2_RGBA8),
                    ("WebGL", TextureImporterFormat.ASTC_8x8, TextureFormat.ASTC_8x8),
                    ("Standalone", TextureImporterFormat.BC7, TextureFormat.BC7),
                };
                foreach (var (platform, set, _) in cases)
                    importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                    {
                        name = platform, overridden = true, format = set, compressionQuality = 100,
                    });
                importer.SaveAndReimport();
                foreach (var (platform, _, expected) in cases)
                {
                    var mask = new Texture2D(8, 8, TextureFormat.RGBA32, true, true);
                    try
                    {
                        M3trik.Unitytk.Materials.MaskTextures.Compress(mask, platform, importer);
                        Assert.That(mask.format, Is.EqualTo(expected), $"{platform}: its source's override, with alpha");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(mask);
                    }
                }
                var none = new Texture2D(8, 8, TextureFormat.RGBA32, true, true);
                M3trik.Unitytk.Materials.MaskTextures.Compress(none, "Standalone", null);
                Assert.That(none.format, Is.Not.EqualTo(TextureFormat.RGBA32), "no source: the editor's own compression");
                UnityEngine.Object.DestroyImmediate(none);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void The_import_compresses_the_mask_for_the_active_build_target()
        {
            // The importer's glue: the build target the import runs for, its platform's
            // settings on the mask's source image. BC7 is no default of any desktop target.
            var orm = (TextureImporter)AssetImporter.GetAtPath(Folder() + "/Metal_ORM.png");
            Assert.That(orm, Is.Not.Null, "the sample's packed image");
            string platform = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)).TargetName;
            try
            {
                orm.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = platform, overridden = true, format = TextureImporterFormat.BC7, compressionQuality = 50,
                });
                orm.SaveAndReimport();
                AssetDatabase.ImportAsset(Model(), ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Texture2D mask = AssetDatabase.LoadAllAssetsAtPath(Model()).OfType<Texture2D>()
                                              .Single(t => t.name == "Metal_ORM_MetallicSmoothness");
                Assert.That(mask.format, Is.EqualTo(TextureFormat.BC7), $"the {platform} override on its source");
            }
            finally
            {
                orm.ClearPlatformTextureSettings(platform);
                orm.SaveAndReimport();
                AssetDatabase.ImportAsset(Model(), ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        static Material Material(string name)
        {
            var found = AssetDatabase.LoadAllAssetsAtPath(Model()).OfType<Material>().FirstOrDefault(m => m.name == name);
            Assert.That(found, Is.Not.Null, $"the model has no material {name}");
            return found;
        }

        static LitTable Table()
        {
            var host = RenderPipelineHosts.Current;
            Assume.That(host != null, "a pipeline a unitytk host serves");
            return host.Lit;
        }

        [Test]
        public void The_metal_sphere_reads_its_maps_through_unitys_mask()
        {
            var table = Table();
            var metal = Material("Metal_MAT");
            Assert.That(metal.shader.name, Is.EqualTo(table.Lit));
            Assert.That(NameOf(metal.GetTexture(table.BaseMap)), Is.EqualTo("Metal_BaseColor"));
            var mask = metal.GetTexture(table.MaskMap) as Texture2D;
            Assert.That(mask, Is.Not.Null, "the export's packed image, repacked by the import");
            Assert.That(mask.name, Is.EqualTo("Metal_ORM_MetallicSmoothness"));
            Assert.That(AssetDatabase.GetAssetPath(mask), Is.EqualTo(Model()), "owned by the model's import");
            Assert.That(mask.GetPixel(2, 8).r, Is.EqualTo(1f).Within(0.1f), "metal on the left half");
            Assert.That(mask.GetPixel(13, 8).r, Is.EqualTo(0f).Within(0.1f), "dielectric on the right");
            Assert.That(mask.GetPixel(8, 15).a, Is.GreaterThan(0.85f), "smooth at the top (roughness 0 at the image's top row)");
            Assert.That(mask.GetPixel(8, 0).a, Is.LessThan(0.15f), "matte at the bottom");
            var normal = metal.GetTexture(table.NormalMap);
            Assert.That(NameOf(normal), Is.EqualTo("Metal_Normal"));
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal));
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap), "its role on its first import");
        }

        [Test]
        public void The_glow_is_four_times_its_colour()
        {
            var table = Table();
            var glow = Material("Glow_MAT");
            var stored = glow.GetColor(table.EmissionColor);
            var expected = MaterialColor.Encode(new Color(4f, 2f, 0.8f, 1f));
            Assert.That(new[] { stored.r, stored.g, stored.b }, Is.EqualTo(new[] { expected.r, expected.g, expected.b }).Within(1e-3f),
                        "emissionColor (1, 0.5, 0.2) x emission 4: KHR_materials_emissive_strength");
            Assert.That(glow.IsKeywordEnabled(table.EmissionKeyword), Is.True);
        }

        [Test]
        public void The_flat_sphere_is_the_pipelines_unlit_colour()
        {
            var table = Table();
            var flat = Material("Flat_MAT");
            Assert.That(flat.shader.name, Is.EqualTo(table.UnlitUntextured ?? table.Unlit), "KHR_materials_unlit, no map");
            var stored = flat.GetColor(table.UnlitColor ?? table.BaseColor);
            var expected = MaterialColor.Encode(new Color(0.2f, 0.6f, 1f, 1f));
            Assert.That(new[] { stored.r, stored.g, stored.b }, Is.EqualTo(new[] { expected.r, expected.g, expected.b }).Within(1e-3f));
        }

        [Test]
        public void The_tiled_map_keeps_the_producers_repeat_and_offset()
        {
            var table = Table();
            var tiled = Material("Tiled_MAT");
            Assert.That(NameOf(tiled.GetTexture(table.BaseMap)), Is.EqualTo("Tiled_BaseColor"));
            Assert.That(tiled.GetTextureScale(table.BaseMap), Is.EqualTo(new Vector2(3f, 2f)));
            var offset = tiled.GetTextureOffset(table.BaseMap);
            Assert.That(new[] { offset.x, offset.y }, Is.EqualTo(new[] { 0.25f, 0.5f }).Within(1e-5f),
                        "the producer's own UV offset, through glTF's UV space and back into Unity's");
        }

        [Test]
        public void The_demo_recipe_builds_its_scene_with_no_errors()
        {
            var table = Table();
            string folder = Folder();
            Type recipe = Type.GetType(Recipe);
            Assert.That(recipe, Is.Not.Null, "the sample's editor assembly is missing: it did not compile, or the sample is not imported");
            try
            {
                var scene = (Scene)recipe.GetMethod("Build").Invoke(null, new object[] { folder });
                Assert.That(scene.path, Is.EqualTo(folder + "/MaterialsDemo.unity"), "saved beside the model");
                var roots = scene.GetRootGameObjects();
                Assert.That(roots.Any(go => PrefabUtility.GetCorrespondingObjectFromSource(go)
                                            == AssetDatabase.LoadAssetAtPath<GameObject>(Model())), "the sample's model, placed");
                var gold = roots.Single(go => go.name == "Gold").GetComponent<Renderer>().sharedMaterial;
                Assert.That(gold.shader.name, Is.EqualTo(table.Lit), "written by Apply from a glTF material");
                Assert.That(gold.GetFloat(table.Smoothness), Is.EqualTo(0.7f).Within(1e-5f), "1 - roughness 0.3");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
