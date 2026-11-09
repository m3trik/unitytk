using System.IO;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Lightmaps.Tests
{
    /// <summary>
    /// The texture-role postprocessor, through a real first import and the carrier's
    /// media: a file a model's lightmaps record names takes its role in its model's
    /// folder of its own -- a lightmap imported as a Lightmap, the probe's panorama as a
    /// readable linear source at its full size -- never beside the model, and never once
    /// the texture has import settings of its own; a texture no record names asks
    /// nothing, not even for the media provider; and a role's check never touches the
    /// importer.
    /// </summary>
    public class LightmapsTextureRolesTests
    {
        const string Folder = "Assets/UnitytkLightmapsRoleTests";
        const string Model = Folder + "/room.fbx";
        static readonly string Own = Folder + "/room" + PersistedStrings.MediaFolderSuffix;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            if (!AssetDatabase.IsValidFolder(Own)) AssetDatabase.CreateFolder(Folder, Path.GetFileName(Own));
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        /// <summary>A texture's first import (no .meta yet) at <paramref name="path"/>: an EXR; its importer.</summary>
        static TextureImporter Import(string path)
        {
            File.WriteAllBytes(path, LightmapsFbx.Exr(8, 4, new Color(0.5f, 0.4f, 0.3f, 1f)));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return (TextureImporter)AssetImporter.GetAtPath(path);
        }

        [Test]
        public void A_file_the_record_names_takes_its_role_on_its_first_import_in_its_models_own_folder()
        {
            LightmapsTextureRoles.Note("UnitytkRoleOn_Lightmap.exr", LightmapsTextureRoles.Role.Lightmap, Model);
            LightmapsTextureRoles.Note("UnitytkRoleOn_Probe.exr", LightmapsTextureRoles.Role.Panorama, Model);
            LightmapsTextureRoles.Note("UnitytkRoleBeside_Lightmap.exr", LightmapsTextureRoles.Role.Lightmap, Model);

            TextureImporter map = Import(Own + "/UnitytkRoleOn_Lightmap.exr");
            Assert.That(map.textureType, Is.EqualTo(TextureImporterType.Lightmap), "Unity encodes it for the project's lightmap encoding");
            Assert.That(map.mipmapEnabled, Is.True);
            TextureImporter panorama = Import(Own + "/UnitytkRoleOn_Probe.exr");
            Assert.That(panorama.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(panorama.textureShape, Is.EqualTo(TextureImporterShape.Texture2D));
            Assert.That(panorama.isReadable, Is.True, "the model's import reads its pixels");
            Assert.That(panorama.sRGBTexture, Is.False, "linear light");
            Assert.That(panorama.mipmapEnabled, Is.False);
            Assert.That(panorama.npotScale, Is.EqualTo(TextureImporterNPOTScale.None), "a 2:1 panorama keeps its size");
            Assert.That(panorama.maxTextureSize, Is.EqualTo(16384));
            Assert.That(panorama.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(Import(Folder + "/UnitytkRoleBeside_Lightmap.exr").textureType, Is.EqualTo(TextureImporterType.Default),
                        "beside the model: a same-named file dropped there takes no role");

            // Once it has settings of its own, they are the person's.
            map.textureType = TextureImporterType.Default;
            map.SaveAndReimport();
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(Own + "/UnitytkRoleOn_Lightmap.exr")).textureType,
                        Is.EqualTo(TextureImporterType.Default));
        }

        [Test]
        public void A_texture_no_record_names_resolves_no_media_provider()
        {
            int resolved = 0;
            System.Func<IModelMedia> media = () =>
            {
                resolved++;
                return new PureModelMedia();
            };
            string unnamed = Own + "/UnitytkRoleUnnamed.exr";
            Assert.That(LightmapsTextureRoles.Apply(Import(unnamed), unnamed, media), Is.False);
            Assert.That(resolved, Is.EqualTo(0), "the media provider is not resolved for a texture no record names");

            // A named one asks it, once (the stand-in's model has no folder of its own: no role).
            LightmapsTextureRoles.Note("UnitytkRoleAsked_Lightmap.exr", LightmapsTextureRoles.Role.Lightmap, Model);
            string named = Own + "/UnitytkRoleAsked_Lightmap.exr";
            Assert.That(LightmapsTextureRoles.Apply(Import(named), named, media), Is.False);
            Assert.That(resolved, Is.EqualTo(1));
        }

        [Test]
        public void A_check_says_what_differs_and_touches_nothing()
        {
            string path = Folder + "/UnitytkRoleChecked.exr";
            TextureImporter importer = Import(path);
            Assert.That(LightmapsTextureRoles.Pin(importer, LightmapsTextureRoles.Role.Lightmap, false), Is.True, "a colour texture is no lightmap");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default), "a check never sets");
            Assert.That(LightmapsTextureRoles.Pin(importer, LightmapsTextureRoles.Role.Lightmap, true), Is.True);
            Assert.That(LightmapsTextureRoles.Pin(importer, LightmapsTextureRoles.Role.Lightmap, false), Is.False, "pinned: nothing differs");
            Assert.That(LightmapsTextureRoles.Pin(importer, LightmapsTextureRoles.Role.Panorama, false), Is.True, "another role differs");
        }
    }
}
