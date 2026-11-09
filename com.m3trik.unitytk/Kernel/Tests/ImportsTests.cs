using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// The import gate: an FBX, in any case, is a delivery; any other asset, or
    /// none, is not.
    /// </summary>
    public class ImportsTests
    {
        [TestCase("Assets/Models/shot.fbx")]
        [TestCase("Assets/Models/SHOT.FBX")]
        [TestCase("Assets/Models/shot.Fbx")]
        public void An_fbx_is_a_delivery(string assetPath)
            => Assert.That(Imports.IsDelivery(assetPath), Is.True);

        [TestCase("Assets/Models/shot.obj")]
        [TestCase("Assets/Textures/mask_EMask.png")]
        [TestCase("Assets/Models/shot.fbx.meta")]
        [TestCase("")]
        [TestCase(null)]
        public void Anything_else_is_not(string assetPath)
            => Assert.That(Imports.IsDelivery(assetPath), Is.False);

        [Test]
        public void With_no_importer_a_record_lands_in_the_default_frame()
        {
            M3trik.Unitytk.Core.GltfFrame frame = Imports.Frame(null);
            Assert.That(frame.Units(100.0, 0.01), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(frame.Length(1.0), Is.EqualTo(1.0));
        }

        [Test]
        public void The_frame_reads_a_model_importers_own_scale()
        {
            const string folder = "Assets/UnitytkImportsTests";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "UnitytkImportsTests");
            try
            {
                // A metre file (UnitScaleFactor 100) at Scale Factor 2: a file unit lands as 2.
                string model = folder + "/metres.fbx";
                File.WriteAllText(model, M3trik.Unitytk.Testing.TwoTakeFbx.Write("animation", "{}", 100.0));
                AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(model);
                importer.globalScale = 2f;
                importer.SaveAndReimport();
                M3trik.Unitytk.Core.GltfFrame frame = Imports.Frame(importer);
                Assert.That(frame.ConvertsUnits, Is.True);
                Assert.That(frame.FileScale, Is.EqualTo(1.0).Within(1e-6), "a metre file's unit, as the importer states it");
                Assert.That(frame.GlobalScale, Is.EqualTo(2.0).Within(1e-6));
                Assert.That(frame.Units(1.0, 1.0), Is.EqualTo(2.0).Within(1e-6), "a metre of the record");
                Assert.That(frame.Length(0.01), Is.EqualTo(2.0).Within(1e-6), "an in-frame length: the file's value 1");
                importer.useFileScale = false;
                importer.SaveAndReimport();
                Assert.That(Imports.Frame(importer).ConvertsUnits, Is.False, "Convert Units off");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
