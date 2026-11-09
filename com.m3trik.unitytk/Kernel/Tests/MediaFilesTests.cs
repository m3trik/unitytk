using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// A record's file found in a model's media (<see cref="MediaFiles.Nearest"/>): the
    /// folders asked in order, by the file's name alone, never a project-wide search;
    /// what the three importers' own copies each did right, together.
    /// </summary>
    public class MediaFilesTests
    {
        const string Root = "Assets/UnitytkMediaFilesTests";
        const string Near = Root + "/near.fbm";
        const string Far = Root;

        [SetUp]
        public void SetUp()
        {
            AssetFolders.Ensure(Near);
            Png(Far + "/both.png");
            Png(Near + "/both.png");
            Png(Far + "/far.png");
            AssetDatabase.ImportAsset(Root, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ImportRecursive);
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Root);

        static void Png(string path)
        {
            var tex = new Texture2D(2, 2);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void The_nearest_folder_that_holds_the_file_wins()
        {
            var folders = new[] { Near, Far };
            Assert.That(MediaFiles.Nearest(folders, "both.png"), Is.EqualTo(Near + "/both.png"));
            Assert.That(MediaFiles.Nearest(folders, "far.png"), Is.EqualTo(Far + "/far.png"));
            Assert.That(MediaFiles.Nearest(folders, "none.png"), Is.Null, "never a search by name elsewhere");
        }

        [Test]
        public void A_record_naming_a_folder_is_read_for_its_file_name_with_either_separator()
        {
            var folders = new[] { Near, Far };
            Assert.That(MediaFiles.Nearest(folders, "textures/far.png"), Is.EqualTo(Far + "/far.png"));
            Assert.That(MediaFiles.Nearest(folders, "C:\\maps\\both.png"), Is.EqualTo(Near + "/both.png"));
        }

        [Test]
        public void A_file_on_disk_is_found_by_its_name_before_it_imports()
        {
            // The probe's panorama: the record names it with a folder, and the file is there
            // before Unity has imported it.
            File.WriteAllBytes(Far + "/late.hdr", new byte[] { 1, 2, 3 });
            var folders = new[] { null, Near, Far };
            Assert.That(MediaFiles.OnDisk(folders, "textures/late.hdr"), Is.EqualTo(Far + "/late.hdr"));
            Assert.That(MediaFiles.Nearest(folders, "textures/late.hdr"), Is.Null, "not loadable yet");
            Assert.That(MediaFiles.OnDisk(folders, "textures\\both.png"), Is.EqualTo(Near + "/both.png"), "the nearest first");
            Assert.That(MediaFiles.OnDisk(null, "late.hdr"), Is.Null);
        }

        [Test]
        public void No_folders_no_name_or_the_wrong_type_find_nothing()
        {
            Assert.That(MediaFiles.Nearest(null, "both.png"), Is.Null);
            Assert.That(MediaFiles.Nearest(new[] { Near }, ""), Is.Null);
            Assert.That(MediaFiles.Nearest(new[] { Near }, null), Is.Null);
            Assert.That(MediaFiles.Nearest<Cubemap>(new[] { Near, Far }, "both.png"), Is.Null, "a 2D texture is no cubemap");
            Assert.That(MediaFiles.Nearest<Texture2D>(new[] { Near, Far }, "far.png"), Is.EqualTo(Far + "/far.png"));
        }
    }
}
