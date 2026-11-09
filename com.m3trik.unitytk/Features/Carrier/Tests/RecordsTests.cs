using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Carrier;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Carrier.Tests
{
    /// <summary>
    /// The carrier feature: it provides the records, media and import-settings
    /// contracts, and its records audit lists every record a reader would refuse.
    /// </summary>
    public class RecordsTests
    {
        [Test]
        public void The_carrier_provides_the_records_and_media_over_the_stand_ins()
        {
            Assert.That(Provided.One<IRecordSource>(), Is.InstanceOf<CarrierRecords>());
            Assert.That(Provided.One<IModelMedia>(), Is.InstanceOf<CarrierRecords>());
            Assert.That(Provided.One<IModelImportSettings>(), Is.InstanceOf<CarrierRecords>());
        }

        [Test]
        public void A_model_is_on_a_detection_pass_only_while_a_setting_asked_of_it_is_off()
        {
            // A model of the project's own (OBJ, which Unity imports through its ModelImporter).
            const string folder = "Assets/UnitytkCarrierTests";
            const string model = folder + "/tri.obj";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "UnitytkCarrierTests");
            File.WriteAllLines(model, new[] { "v 0 0 0", "v 1 0 0", "v 0 1 0", "f 1 2 3" });
            try
            {
                AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(model);
                IModelImportSettings[] sources = { new PureModelImportSettings(), new CarrierRecords() };
                foreach (IModelImportSettings settings in sources)
                {
                    Assert.That(settings.IsDetectionPass(model, importer), Is.False, $"{settings.GetType().Name}: nothing asked");
                    Assert.That(settings.IsDetectionPass(model, null), Is.False, $"{settings.GetType().Name}: no model importer");
                }
                Assume.That(importer.resampleCurves, Is.True, "Unity's default");
                var pure = new PureModelImportSettings();
                pure.RequestSparseCurves(model);
                Assert.That(pure.SparseCurves, Is.EquivalentTo(new[] { model }), "the stand-in keeps what was asked");
                Assert.That(pure.IsDetectionPass(model, importer), Is.False, "...and applies nothing to wait on");
                var carrier = new CarrierRecords();
                carrier.RequestSparseCurves(model);
                Assert.That(carrier.IsDetectionPass(model, importer), Is.True, "unresampled curves asked, and still resampled");
                importer.resampleCurves = false;  // what the reimport applies
                Assert.That(carrier.IsDetectionPass(model, importer), Is.False, "every setting asked is on");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);  // the request is dropped with its model
            }
        }

        [Test]
        public void The_stand_in_reads_a_node_as_the_carrier_does()
        {
            // One fixture against the stand-in and the provider: neither may drift.
            IRecordSource[] sources = { new PureRecordSource(), new CarrierRecords() };
            var cases = new (string[] names, object[] values, string key, bool found, string json)[]
            {
                (new[] { "audio" }, new object[] { "{}" }, "audio", true, "{}"),
                (new[] { "AUDIO" }, new object[] { "{}" }, "audio", true, "{}"),
                (new[] { "audio" }, new object[] { "" }, "audio", false, ""),
                (new[] { "audio" }, new object[] { 3 }, "audio", false, null),
                (new[] { "other" }, new object[] { "{}" }, "audio", false, null),
            };
            foreach (IRecordSource source in sources)
                foreach (var c in cases)
                {
                    bool found = source.TryGetRecord(c.names, c.values, c.key, out string json);
                    Assert.That((found, json), Is.EqualTo((c.found, c.json)), $"{source.GetType().Name}: {string.Join(",", c.names)}");
                }
        }

        [Test]
        public void The_stand_in_looks_for_a_lone_models_files_where_the_carrier_does()
        {
            // A model alone in its folder, with no media folder of its own: both look beside it alone.
            const string model = "Assets/UnitytkNoSuchFolder/Model.fbx";
            IModelMedia[] media = { new PureModelMedia(), new CarrierRecords() };
            foreach (IModelMedia m in media)
                Assert.That(m.MediaFolders(model), Is.EqualTo(new[] { "Assets/UnitytkNoSuchFolder" }), m.GetType().Name);
        }

        [Test]
        public void A_file_is_in_a_models_folder_of_its_own_only_inside_its_media_folder()
        {
            const string model = "Assets/UnitytkNoSuchFolder/Model.fbx";
            string own = "Assets/UnitytkNoSuchFolder/Model" + PersistedStrings.MediaFolderSuffix;
            var carrier = new CarrierRecords();
            Assert.That(carrier.InOwnMediaFolder(model, own + "/wood.png"), Is.True);
            Assert.That(carrier.InOwnMediaFolder(model, own.ToLowerInvariant() + "/wood.png"), Is.True,
                "ignoring case, as the file systems Unity extracts onto do");
            Assert.That(carrier.InOwnMediaFolder(model, "Assets/UnitytkNoSuchFolder/wood.png"), Is.False,
                "beside the model: a file dropped there is not its own");
            Assert.That(carrier.InOwnMediaFolder(model, "Assets/UnitytkNoSuchFolder/Other" + PersistedStrings.MediaFolderSuffix + "/wood.png"),
                Is.False, "another model's");
            Assert.That(new PureModelMedia().InOwnMediaFolder(model, own + "/wood.png"), Is.False,
                "the stand-in's model embeds nothing, so it has no folder of its own");
        }

        [Test]
        public void A_node_that_is_no_carrier_is_never_read()
        {
            // An artist's custom attribute of a record's name, on a node that is no
            // carrier, in a file with no carrier: no channel takes it, on either reader.
            var names = new (string name, bool carrier)[]
            {
                ("data_export", true), ("data_export1", true), ("data_export.001", true),
                ("MOD:data_export", true), ("Grp|MOD:data_export.002", true),
                ("data_exportShape", false), ("data_export_old", false), ("data_export.", false),
                ("data_export.1a", false), ("Lamp_geo", false), (null, false),
            };
            foreach (var c in names)
                Assert.That(CarrierFile.IsCarrier(c.name), Is.EqualTo(c.carrier), c.name ?? "null");

            var root = new GameObject("Model");
            try
            {
                var lamp = new GameObject("Lamp_geo");
                lamp.transform.SetParent(root.transform);
                var pending = new Dictionary<string, string>();
                CarrierImport.Collect(pending, "Assets/Lamp.fbx", lamp, "{\"stale\": 1}");
                Assert.That(pending, Is.Empty, "a node that is no carrier is never read");

                string path = CarrierFbx.Write(FileUtil.GetUniqueTempPathInProject() + ".fbx",
                    ("Lamp_geo", null, "materials", "{\"stale\": 1}"));
                try
                {
                    Assert.That(CarrierFile.Record(path, "materials"), Is.Null, "...nor off the file");
                }
                finally
                {
                    File.Delete(path);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void A_carrier_under_a_group_is_its_modules_never_the_scenes()
        {
            // The scene's own carrier is the root-level data_export; one of the same name
            // under a group is a grouped module's own, read only where the scene's carries
            // no such channel, whichever comes first (pythontk SceneStoreBase.is_scene_carrier).
            var root = new GameObject("Model");
            try
            {
                var group = new GameObject("Module");
                group.transform.SetParent(root.transform);
                var nested = new GameObject(CarrierImport.CARRIER);
                nested.transform.SetParent(group.transform);
                var own = new GameObject(CarrierImport.CARRIER);
                own.transform.SetParent(root.transform);
                Assert.That(CarrierFile.IsSceneCarrier(own.name, true), Is.True);
                Assert.That(CarrierFile.IsSceneCarrier("Module|data_export", true), Is.False);
                Assert.That(CarrierFile.IsSceneCarrier("MOD:data_export", true), Is.False);

                foreach (var order in new[] { new[] { nested, own }, new[] { own, nested } })
                {
                    var pending = new Dictionary<string, string>();
                    foreach (var node in order)
                        CarrierImport.Collect(pending, "Assets/A.fbx", node, node == own ? "scene" : "module");
                    Assert.That(pending["Assets/A.fbx"], Is.EqualTo("scene"), $"{order[0].transform.parent.name} first");
                }
                var alone = new Dictionary<string, string>();
                CarrierImport.Collect(alone, "Assets/A.fbx", nested, "module");
                Assert.That(alone["Assets/A.fbx"], Is.EqualTo("module"), "a module's where the scene's carries none");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            string path = CarrierFbx.Write(FileUtil.GetUniqueTempPathInProject() + ".fbx",
                (CarrierImport.CARRIER, "Module", "lightmaps", "module"),
                (CarrierImport.CARRIER, "Module", "audio", "module audio"),
                (CarrierImport.CARRIER, null, "lightmaps", "scene"));
            var nodes = new GameObject[0];
            try
            {
                Assert.That(CarrierFile.Record(path, "lightmaps"), Is.EqualTo("scene"), "the file's root carrier");
                Assert.That(CarrierFile.Record(path, "audio"), Is.EqualTo("module audio"), "a module's where the scene's has none");

                // As an import hands them over: before Unity parents the hierarchy, both
                // look like roots, and the file tells them apart.
                nodes = new[] { new GameObject(CarrierImport.CARRIER), new GameObject(CarrierImport.CARRIER) };
                var pending = new Dictionary<string, string>();
                CarrierImport.Collect(pending, path, nodes[0], "module");
                CarrierImport.Collect(pending, path, nodes[1], "scene");
                Assert.That(pending[path], Is.EqualTo("scene"), "the grouped module's, met first, yields");
            }
            finally
            {
                foreach (var node in nodes) UnityEngine.Object.DestroyImmediate(node);
                File.Delete(path);
            }
        }

        [Test]
        public void The_audit_lists_a_record_of_another_version_and_nothing_current()
        {
            // Whichever record a loaded reader declares: the audit knows no feature.
            var reader = ReadsRecordAttribute.Read(AppDomain.CurrentDomain.GetAssemblies()).First(r => r.Value > 1);
            var records = new PureRecordSource()
                .With("Assets/Old.fbx", reader.Key, "{\"version\": " + (reader.Value - 1) + "}")
                .With("Assets/Current.fbx", reader.Key, "{\"version\": " + reader.Value + "}");
            var ctx = new OpContext();
            var refused = Records.Audit(new[] { "Assets/Old.fbx", "Assets/Current.fbx", "Assets/None.fbx" }, records, ctx);
            Assert.That(refused.Select(r => (r.assetPath, r.key, r.version)), Is.EqualTo(new[] { ("Assets/Old.fbx", reader.Key, reader.Value - 1) }));
            Assert.That(refused[0].supported, Is.EqualTo(reader.Value));
            Assert.That(refused[0].message, Does.Contain("re-export"));
            Assert.That(ctx.Issues.Single().code, Is.EqualTo(Records.RefusedCode));
            Assert.That(ctx.Issues.Single().location, Is.EqualTo("Assets/Old.fbx"));
            Assert.That(ctx.Changes, Is.Empty, "the audit reads only");
        }
    }
}
