using System.IO;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Materials.Tests
{
    /// <summary>
    /// The texture-role postprocessor, through a real first import and the carrier's
    /// media: a map a written material names takes its role in its model's folder of
    /// its own, never beside the model, and never once the
    /// texture has import settings of its own; it keeps the project's cap unless the project
    /// asks for full size; a texture no written material names asks nothing, not even for the
    /// media provider.
    /// </summary>
    public class MaterialsTextureRolesTests
    {
        const string Folder = "Assets/UnitytkMaterialsRoleTests";
        const string Model = Folder + "/crate.fbx";
        static readonly string Own = Folder + "/crate" + PersistedStrings.MediaFolderSuffix;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            if (!AssetDatabase.IsValidFolder(Own)) AssetDatabase.CreateFolder(Folder, Path.GetFileName(Own));
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        /// <summary>The materials of the model name these maps, as an import notes them.</summary>
        static void Name(params (string file, bool normal)[] maps)
        {
            foreach (var (file, normal) in maps)
                MaterialsImporter.Note(file, normal ? TextureRole.Normal : TextureRole.Data, Model);
        }

        /// <summary>A texture's first import (no .meta yet) at <paramref name="path"/>; its importer.</summary>
        static TextureImporter Import(string path)
        {
            var pixels = new Texture2D(4, 4);
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return (TextureImporter)AssetImporter.GetAtPath(path);
        }

        [Test]
        public void A_map_takes_its_role_on_its_first_import_in_its_models_own_folder()
        {
            Name(("UnitytkRoleOn_normal.png", true), ("UnitytkRoleOn_rough.png", false),
                 ("UnitytkRoleBeside_normal.png", true));

            Assert.That(Import(Own + "/UnitytkRoleOn_normal.png").textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(Import(Own + "/UnitytkRoleOn_rough.png").sRGBTexture, Is.False, "a data map is linear");
            Assert.That(Import(Folder + "/UnitytkRoleBeside_normal.png").textureType, Is.EqualTo(TextureImporterType.Default),
                        "beside the model: a same-named file dropped there takes no role");

            // Once it has settings of its own, they are the user's.
            var imported = (TextureImporter)AssetImporter.GetAtPath(Own + "/UnitytkRoleOn_normal.png");
            imported.textureType = TextureImporterType.Default;
            imported.SaveAndReimport();
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(Own + "/UnitytkRoleOn_normal.png")).textureType,
                        Is.EqualTo(TextureImporterType.Default));
        }

        [Test]
        public void A_maps_first_import_keeps_the_projects_cap_unless_the_project_asks_for_full_size()
        {
            Name(("UnitytkRoleCapped_rough.png", false), ("UnitytkRoleFull_rough.png", false));
            TextureImporter Wide(string path)
            {
                var image = new Texture2D(4096, 4, TextureFormat.RGBA32, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return (TextureImporter)AssetImporter.GetAtPath(path);
            }
            int unitys = new TextureImporterPlatformSettings().maxTextureSize;
            Assert.That(MaterialsSettings.instance.fullSize, Is.False, "off by default");
            Assert.That(Wide(Own + "/UnitytkRoleCapped_rough.png").maxTextureSize, Is.EqualTo(unitys), "the project's cap stands");
            bool was = MaterialsSettings.instance.fullSize;
            MaterialsSettings.instance.fullSize = true;  // the project asks (not saved: a test's)
            try
            {
                TextureImporter full = Wide(Own + "/UnitytkRoleFull_rough.png");
                Assert.That(full.maxTextureSize, Is.EqualTo(4096), "asked: at its own size");
                Assert.That(full.sRGBTexture, Is.False, "its role as well");
            }
            finally
            {
                MaterialsSettings.instance.fullSize = was;
            }
        }

        [Test]
        public void A_named_map_with_no_role_here_keeps_the_projects_cap_even_where_full_size_is_asked()
        {
            Name(("UnitytkRoleBeside_rough.png", false));
            string path = Folder + "/UnitytkRoleBeside_rough.png";  // beside the model, not in its own folder: no role
            var image = new Texture2D(4096, 4, TextureFormat.RGBA32, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            bool was = MaterialsSettings.instance.fullSize;
            MaterialsSettings.instance.fullSize = true;  // the project asks (not saved: a test's)
            try
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.maxTextureSize, Is.EqualTo(new TextureImporterPlatformSettings().maxTextureSize),
                            "no role here: its first import is left as Unity makes it");
                Assert.That(importer.sRGBTexture, Is.True);
            }
            finally
            {
                MaterialsSettings.instance.fullSize = was;
            }
        }

        [Test]
        public void A_texture_no_shaded_material_names_resolves_no_media_provider()
        {
            int resolved = 0;
            System.Func<IModelMedia> media = () =>
            {
                resolved++;
                return new PureModelMedia();
            };

            // Most textures: no material names them, so nothing is asked, the provider least.
            string unnamed = Own + "/UnitytkRoleUnnamed.png";
            Assert.That(MaterialsTextureRoles.Apply(Import(unnamed), unnamed, media), Is.False);
            Assert.That(resolved, Is.EqualTo(0), "the media provider is not resolved for a texture no material names");

            // A named one asks it, once (the stand-in's model has no folder of its own: no role).
            Name(("UnitytkRoleAsked_normal.png", true));
            string named = Own + "/UnitytkRoleAsked_normal.png";
            Assert.That(MaterialsTextureRoles.Apply(Import(named), named, media), Is.False);
            Assert.That(resolved, Is.EqualTo(1));
        }
    }
}
