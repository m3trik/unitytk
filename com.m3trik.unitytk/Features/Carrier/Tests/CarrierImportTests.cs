using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Carrier.Tests
{
    /// <summary>
    /// Where a model's embedded media land (<see cref="CarrierImport"/>), over real
    /// imports of a model embedding its files (<see cref="CarrierFbx.WriteEmbedding"/>):
    /// a lone model's land beside it, each marked its own, asked once per version; an
    /// extraction never writes over a file the model did not extract (they stay in
    /// <c>&lt;model&gt;_Media/</c>); a model that comes to share its folder moves its media
    /// into <c>&lt;model&gt;_Media/</c>; an unmarked (legacy) file beside an FBX, or another
    /// model's extraction, is never bound by another model, and is not the model's own;
    /// and a deleted file an extraction wrote reimports its model and is asked for again.
    /// </summary>
    public class CarrierImportTests
    {
        const string Folder = "Assets/UnitytkCarrierImportTests";
        const string Model = Folder + "/crate.fbx";
        const string Media = Folder + "/crate_Media";
        const string Wood = "wood.png";

        readonly byte[] _red = Png(Color.red), _blue = Png(Color.blue);

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        static byte[] Png(Color colour)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                texture.SetPixels(Enumerable.Repeat(colour, 4).ToArray());
                texture.Apply(false);
                return texture.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        // A model at path embedding wood.png (these bytes), imported.
        static void Deliver(string path, byte[] wood)
        {
            CarrierFbx.WriteEmbedding(path, wood == null ? new Dictionary<string, byte[]>()
                                                         : new Dictionary<string, byte[]> { [Wood] = wood });
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        // A file the project holds that no model extracted (a library's, or an older release's extraction).
        static string Place(string path, byte[] bytes)
        {
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        // What a channel importer asks of a model naming wood.png, applied as the batch's end applies it.
        static void Extract(string model)
        {
            Assert.That(CarrierImport.RequestEmbeddedFiles(model, new[] { Wood }), Is.True, $"{model}: asked");
            CarrierImport.ApplyPendingImportSettings();
        }

        [Test]
        public void A_lone_models_media_land_beside_it_marked_its_own_asked_once_per_version()
        {
            Deliver(Model, _red);
            Extract(Model);
            string beside = Folder + "/" + Wood;
            Assert.That(File.ReadAllBytes(beside), Is.EqualTo(_red), "extracted beside it");
            Assert.That(CarrierImport.IsOwnMedia(Model, beside), Is.True, "marked its own");
            Assert.That(CarrierImport.IsExtractedMedia(beside), Is.True, "an extraction's: another model never binds it");
            Assert.That(AssetDatabase.IsValidFolder(Media), Is.False, "the folder it landed in first is gone");
            Assert.That(CarrierImport.MediaFolder(Model), Is.EqualTo(Folder));
            Assert.That(CarrierImport.RequestEmbeddedFiles(Model, new[] { Wood }), Is.False, "held: the version asks no more");
        }

        [Test]
        public void An_extraction_never_writes_over_a_file_the_model_did_not_extract()
        {
            string library = Place(Folder + "/" + Wood, _blue);
            string guid = AssetDatabase.AssetPathToGUID(library);
            Deliver(Model, _red);
            Extract(Model);
            Assert.That(File.ReadAllBytes(library), Is.EqualTo(_blue), "the library's file keeps its bytes");
            Assert.That(AssetDatabase.AssetPathToGUID(library), Is.EqualTo(guid), "and its GUID");
            Assert.That(CarrierImport.IsOwnMedia(Model, library), Is.False, "never the model's own");
            string own = Media + "/" + Wood;
            Assert.That(File.ReadAllBytes(own), Is.EqualTo(_red), "the model's own stays in its folder of its own");
            Assert.That(CarrierImport.MediaFolder(Model), Is.EqualTo(Media), "where its later extractions go too");
            Assert.That(CarrierImport.OwnMediaCopies(Model, Wood), Is.EqualTo(new[] { own }), "the one it binds");
        }

        [Test]
        public void A_model_that_comes_to_share_its_folder_moves_its_media_into_a_folder_of_its_own()
        {
            Deliver(Model, _red);
            Extract(Model);
            Assert.That(CarrierImport.MediaFolder(Model), Is.EqualTo(Folder), "alone: beside it");

            Deliver(Folder + "/moody.fbx", null);
            Assert.That(CarrierImport.MediaFolder(Model), Is.EqualTo(Media), "sharing its folder: a folder of its own");
            Extract(Model);  // the same version asks again, into its new folder
            Assert.That(File.ReadAllBytes(Media + "/" + Wood), Is.EqualTo(_red), "its media moved into it");
            Assert.That(CarrierImport.MediaFolders(Model), Is.EqualTo(new[] { Media, Folder }), "looked for there first");
            Assert.That(CarrierImport.OwnMediaCopies(Model, Wood), Is.EqualTo(new[] { Media + "/" + Wood }));
        }

        [Test]
        public void An_unmarked_file_beside_an_fbx_is_never_bound_and_never_the_models_own()
        {
            // An older release extracted wood.png beside the model, unmarked.
            string legacy = Place(Folder + "/" + Wood, _red);
            Deliver(Model, _red);
            const string other = Folder + "/Other/door.fbx";
            AssetDatabase.CreateFolder(Folder, "Other");
            Deliver(other, null);
            string library = Place(Folder + "/Library/" + Wood, _red);

            Assert.That(CarrierImport.IsOwnMedia(Model, legacy), Is.False, "unmarked: not the model's own");
            Assert.That(CarrierImport.IsReusableBy(other, legacy), Is.False, "nor another model's to bind: it lies beside an FBX");
            Assert.That(CarrierImport.IsReusableBy(other, library), Is.True, "a library's file is");

            Extract(Model);
            string own = Media + "/" + Wood;
            Assert.That(File.Exists(own), Is.True, "its next extraction goes into its folder of its own");
            Assert.That(CarrierImport.IsReusableBy(other, own), Is.False, "another model's extraction is never bound");
            Assert.That(CarrierImport.IsReusableBy(Model, own), Is.True, "its own is");
        }

        [Test]
        public void A_deleted_file_an_extraction_wrote_reimports_its_model_and_is_asked_for_again()
        {
            Deliver(Model, _red);
            Extract(Model);
            string beside = Folder + "/" + Wood;

            LogAssert.Expect(LogType.Log, new Regex(Regex.Escape(Model) + ": a file its extraction wrote was deleted"));
            AssetDatabase.DeleteAsset(beside);
            Assert.That(File.Exists(beside), Is.False);
            Extract(Model);  // the version asks once more: a file it wrote is gone
            Assert.That(File.ReadAllBytes(beside), Is.EqualTo(_red), "extracted again");
            Assert.That(CarrierImport.IsOwnMedia(Model, beside), Is.True);
        }
    }
}
