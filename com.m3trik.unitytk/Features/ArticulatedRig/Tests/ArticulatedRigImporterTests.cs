using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// What the importer does to an imported model, over the records stand-in: a v2 record
    /// fills a rig and its joints by reference, in Unity's axes, posed where the file stands,
    /// its grab parts collidable; namespaced names bind by their leaf; a tie stays unbound and
    /// is reported, leaving its rig out; roots under different nodes import, each chain posed
    /// and grabbed in its own root's space, and two rigs may share a node; every rest, anchor
    /// and limit is mirrored as the reference's mirror says (pinned against the reference
    /// model); a version 1 record is refused with its fix; a re-export without the record
    /// drops the stale components. And it declares the record it reads.
    /// </summary>
    public class ArticulatedRigImporterTests
    {
        const string Asset = "Assets/Arm.fbx";

        GameObject _root;
        Transform _part;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void A_v2_record_fills_the_rig_and_its_joints_by_reference_in_unitys_axes()
        {
            _root = ArmRig.Build(out _part);
            Transform elbowNode = _part.parent;
            elbowNode.localRotation = Quaternion.Euler(0f, 0f, 30f);  // the file's own pose
            var records = new PureRecordSource();

            var rigs = ArticulatedRigImporter.Wire(_root, ArmRig.Record, Asset, records);
            Assert.That(records.Refusals, Is.Empty);
            Assert.That(rigs, Has.Count.EqualTo(1));
            ArticulatedRig rig = rigs[0];
            Assert.That(rig.gameObject.name, Is.EqualTo("arm_RIG"), "on the node its roots hang under");
            Assert.That(rig.joints.Select(j => j.name), Is.EqualTo(new[] { "base", "elbow" }));
            ArticulatedJoint elbow = rig.joints[1];
            Assert.That(elbow.transform, Is.SameAs(elbowNode));
            Assert.That(elbow.parent, Is.SameAs(rig.joints[0]), "its parent joint, by reference");
            Assert.That(Vector3.Distance(elbow.restPosition, new Vector3(-0.1f, 0f, 0f)), Is.LessThan(1e-6f), "the rest, X mirrored");
            Assert.That(elbow.anchorPosition, Is.EqualTo(Vector3.zero));
            Assert.That(elbow.anchorRotation, Is.EqualTo(Quaternion.identity), "the producer's identity anchor");
            ArticulatedJoint.Channel bottom = rig.joints[0].channels.Single();
            Assert.That((bottom.axis, bottom.min, bottom.max), Is.EqualTo((ArticulatedJoint.Axis.Rz, -60f, 90f)),
                        "rz turns the other way in Unity: its limits negated and swapped");
            Assert.That(elbow.rz, Is.EqualTo(30f).Within(1e-3f), "the values where the file stands");
            Assert.That(rig.followAnimation, Is.True, "the delivery's clips animate the parts");
            Assert.That(rig.grabs.Single().part, Is.SameAs(_part));
            Assert.That(rig.grabs.Single().joint, Is.SameAs(elbow));
            var collider = _part.GetComponent<MeshCollider>();
            Assert.That(collider != null && collider.convex, "the grab part's mesh got a convex collider");

            ArticulatedRigImporter.Wire(_root, ArmRig.Record, Asset, records);
            Assert.That(_root.GetComponentsInChildren<ArticulatedRig>(true), Has.Length.EqualTo(1), "a reimport keeps one rig");
            Assert.That(_root.GetComponentsInChildren<ArticulatedJoint>(true), Has.Length.EqualTo(2), "and its joints");
            Assert.That(_part.GetComponents<Collider>(), Has.Length.EqualTo(1), "and one collider");
        }

        [Test]
        public void Namespaced_names_bind_by_their_leaf()
        {
            _root = ArmRig.Build(out _part, "lamp:");
            var rigs = ArticulatedRigImporter.Wire(_root, ArmRig.Record, Asset, new PureRecordSource());
            Assert.That(rigs, Has.Count.EqualTo(1));
            Assert.That(rigs[0].joints.Select(j => j.name), Is.EqualTo(new[] { "lamp:base", "lamp:elbow" }));
            Assert.That(rigs[0].grabs.Single().part, Is.SameAs(_part));
        }

        [Test]
        public void A_tie_stays_unbound_and_is_reported_and_leaves_its_rig_out()
        {
            _root = ArmRig.Build(out _part, "a:");
            ArmRig.Child("b:elbow", _root.transform, Vector3.zero);  // a namesake's leaf
            LogAssert.Expect(LogType.Warning, new Regex("joint 'elbow' matches several objects equally .*a:elbow.*b:elbow.*the rig is left out"));
            var rigs = ArticulatedRigImporter.Wire(_root, ArmRig.Record, Asset, new PureRecordSource());
            Assert.That(rigs, Is.Empty);
            Assert.That(_root.GetComponentsInChildren<ArticulatedJoint>(true), Is.Empty, "no guess");
        }

        [Test]
        public void An_exact_name_wins_over_its_namesakes_leaf()
        {
            _root = ArmRig.Build(out _part);
            ArmRig.Child("other:elbow", _root.transform, Vector3.zero);
            var rigs = ArticulatedRigImporter.Wire(_root, ArmRig.Record, Asset, new PureRecordSource());
            Assert.That(rigs, Has.Count.EqualTo(1));
            Assert.That(rigs[0].joints[1].transform, Is.SameAs(_part.parent));
        }

        [Test]
        public void A_repeated_grab_part_name_binds_under_its_own_joint()
        {
            _root = ArmRig.Build(out _part);
            ArmRig.Child("elbow_geo", _root.transform.GetChild(0).GetChild(0), new Vector3(0.3f, 0f, 0f));  // under the base only
            var rigs = ArticulatedRigImporter.Wire(_root, ArmRig.Record, Asset, new PureRecordSource());
            Assert.That(rigs.Single().grabs.Single().part, Is.SameAs(_part));
        }

        const string TwoRoots = @"{""version"": 2, ""rigs"": [{""name"": ""pair"", ""joints"": [
            {""name"": ""left"", ""parent"": null, ""t"": [0, 0.1, 0], ""q"": [0, 0, 0, 1],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": null, ""max"": null, ""weight"": 1}]},
            {""name"": ""right"", ""parent"": null, ""t"": [0, 0.1, 0], ""q"": [0, 0, 0, 1],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": null, ""max"": null, ""weight"": 1}]}],
            ""grab"": [{""node"": ""right_geo"", ""joint"": 1}]}]}";

        [Test]
        public void Roots_under_different_nodes_import_and_each_poses_and_grabs_in_its_own_space()
        {
            _root = new GameObject("Pair");
            Transform one = ArmRig.Child("one", _root.transform, Vector3.zero);
            Transform two = ArmRig.Child("two", _root.transform, new Vector3(1f, 0f, 0f));
            two.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Transform left = ArmRig.Child("left", one, new Vector3(0f, 0.1f, 0f));
            Transform right = ArmRig.Child("right", two, new Vector3(0f, 0.1f, 0f));
            Transform geo = ArmRig.Child("right_geo", right, new Vector3(-0.2f, 0f, 0f));
            geo.gameObject.AddComponent<MeshFilter>().sharedMesh = ArmRig.Cube();

            var rigs = ArticulatedRigImporter.Wire(_root, TwoRoots, Asset, new PureRecordSource());
            Assert.That(rigs, Has.Count.EqualTo(1), "imported, not refused");
            ArticulatedRig rig = rigs[0];
            Assert.That(rig.transform, Is.SameAs(_root.transform), "on the node the roots' spaces share");
            Assert.That(rig.SpaceOf(rig.joints[1]), Is.SameAs(two));

            rig.ApplyState(new[] { 0.0, 90.0 });
            Assert.That(Vector3.Distance(right.localPosition, new Vector3(0f, 0.1f, 0f)), Is.LessThan(1e-6f), "posed under its own node");
            Assert.That(Quaternion.Angle(right.localRotation, Quaternion.Euler(0f, 0f, 90f)), Is.LessThan(1e-3f));
            Assert.That(left.localRotation, Is.EqualTo(Quaternion.identity));

            rig.ReturnToAnimation();
            rig.ApplyState(new[] { 0.0, 0.0 });
            rig.ReturnToAnimation();
            Assert.That(rig.BeginGrab(geo, geo.position), Is.True);
            Vector3 target = two.TransformPoint(new Vector3(0f, 0.1f, 0f) + Quaternion.Euler(0f, 0f, 30f) * new Vector3(-0.2f, 0f, 0f));
            rig.UpdateGrab(target);
            Assert.That(Vector3.Distance(geo.position, target), Is.LessThan(1e-4f), "a grab solves in its chain's space");
        }

        [Test]
        public void Two_rigs_whose_roots_share_a_node_both_import()
        {
            const string twoRigs = @"{""version"": 2, ""rigs"": [
                {""name"": ""a"", ""joints"": [{""name"": ""left"", ""parent"": null, ""t"": [0, 0, 0], ""q"": [0, 0, 0, 1],
                 ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
                 ""channels"": [{""channel"": ""rz"", ""min"": null, ""max"": null, ""weight"": 1}]}], ""grab"": []},
                {""name"": ""b"", ""joints"": [{""name"": ""right"", ""parent"": null, ""t"": [0, 0, 0], ""q"": [0, 0, 0, 1],
                 ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
                 ""channels"": [{""channel"": ""rx"", ""min"": null, ""max"": null, ""weight"": 1}]}], ""grab"": []}]}";
            _root = new GameObject("Group");
            ArmRig.Child("left", _root.transform, Vector3.zero);
            ArmRig.Child("right", _root.transform, Vector3.zero);
            var rigs = ArticulatedRigImporter.Wire(_root, twoRigs, Asset, new PureRecordSource());
            Assert.That(rigs, Has.Count.EqualTo(2), "each chain poses in its own root's space: nothing is ambiguous");
            Assert.That(_root.GetComponents<ArticulatedRig>(), Has.Length.EqualTo(2), "both on the node they share");
            Assert.That(rigs.Select(r => r.joints.Single().name), Is.EqualTo(new[] { "left", "right" }));

            ArticulatedRigImporter.Wire(_root, twoRigs, Asset, new PureRecordSource());
            Assert.That(_root.GetComponents<ArticulatedRig>(), Has.Length.EqualTo(2), "a reimport keeps two");
        }

        // A record whose channels turn and slide on every mirrored and unmirrored axis, with one-sided
        // limits, a rest turned about y and z, and an anchor off the pivot.
        const string Mirror = @"{""version"": 2, ""rigs"": [{""name"": ""mirror"", ""joints"": [
            {""name"": ""base"", ""parent"": null, ""t"": [0.1, 0.2, 0.3], ""q"": [0.1, 0.2, 0.3, 0.927362],
             ""anchor"": {""t"": [0.05, -0.02, 0.01], ""q"": [0.2, -0.1, 0.3, 0.927362]}, ""rotate_order"": ""yzx"",
             ""channels"": [{""channel"": ""ry"", ""min"": null, ""max"": 30, ""weight"": 1},
                           {""channel"": ""tx"", ""min"": -0.2, ""max"": null, ""weight"": 2},
                           {""channel"": ""rx"", ""min"": -10, ""max"": 20, ""weight"": 1}]},
            {""name"": ""tip"", ""parent"": 0, ""t"": [-0.15, 0.05, 0.02], ""q"": [0, 0.3, -0.2, 0.932738],
             ""anchor"": {""t"": [0.03, 0, -0.04], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -45, ""max"": null, ""weight"": 1},
                           {""channel"": ""ty"", ""min"": null, ""max"": 0.1, ""weight"": 1}]}],
            ""grab"": []}]}";

        [Test]
        public void The_importer_mirrors_each_rest_anchor_and_limit_as_unitys_fbx_import_lands_the_file()
        {
            _root = new GameObject("Model");
            Transform space = ArmRig.Child("space", _root.transform, Vector3.zero);
            Transform bottom = ArmRig.Child("base", space, Vector3.zero);
            ArmRig.Child("tip", bottom, Vector3.zero);
            var rigs = ArticulatedRigImporter.Wire(_root, Mirror, Asset, new PureRecordSource());
            Assert.That(rigs, Has.Count.EqualTo(1));
            ArticulatedJoint b = rigs[0].joints[0], t = rigs[0].joints[1];

            // The mirror the reviewer's stress script proves: (x, y, z) -> (-x, y, z); (x, y, z, w) ->
            // (x, -y, -z, w); ry, rz and tx turn and slide the other way, their limits negated and swapped.
            Assert.That(Vector3.Distance(b.restPosition, new Vector3(-0.1f, 0.2f, 0.3f)), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(b.restRotation, new Quaternion(0.1f, -0.2f, -0.3f, 0.927362f)), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(b.anchorPosition, new Vector3(-0.05f, -0.02f, 0.01f)), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(b.anchorRotation, new Quaternion(0.2f, 0.1f, -0.3f, 0.927362f)), Is.LessThan(1e-3f));
            Assert.That(b.rotateOrder, Is.EqualTo(ArticulatedJoint.RotateOrder.YZX));
            var ry = b.channels[0];
            Assert.That((ry.axis, ry.hasMin, ry.min, ry.hasMax), Is.EqualTo((ArticulatedJoint.Axis.Ry, true, -30f, false)),
                        "ry: a max of 30 alone is a min of -30 alone");
            var tx = b.channels[1];
            Assert.That((tx.axis, tx.hasMin, tx.hasMax, tx.weight), Is.EqualTo((ArticulatedJoint.Axis.Tx, false, true, 2f)));
            Assert.That(tx.max, Is.EqualTo(0.2f).Within(1e-6f), "tx: a min of -0.2 alone is a max of 0.2 alone");
            var rx = b.channels[2];
            Assert.That((rx.hasMin, rx.min, rx.hasMax, rx.max), Is.EqualTo((true, -10f, true, 20f)), "rx turns alike: kept");
            var rz = t.channels[0];
            Assert.That((rz.hasMin, rz.hasMax, rz.max), Is.EqualTo((false, true, 45f)), "rz: a min of -45 alone is a max of 45 alone");
            var ty = t.channels[1];
            Assert.That((ty.hasMin, ty.hasMax), Is.EqualTo((false, true)), "ty slides alike: kept");
            Assert.That(ty.max, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(Vector3.Distance(t.anchorPosition, new Vector3(-0.03f, 0f, -0.04f)), Is.LessThan(1e-6f));

            // And the rig it fills poses as the mirror of the reference at the mirrored values.
            ArticulationModel reference = ArticulationModel.FromRecord((IDictionary<string, object>)Json.Parse(Mirror));
            double[] state = { 25.0, 0.05, -5.0, 30.0, -0.04 };
            double[] sign = { -1.0, -1.0, 1.0, -1.0, 1.0 };  // ry, tx, rx, rz, ty
            ArticulationModel.Frame[] want = reference.Pose(state);
            rigs[0].ApplyState(state.Select((v, i) => v * sign[i]).ToArray());
            for (int j = 0; j < 2; j++)
            {
                Transform part = rigs[0].joints[j].transform;
                var t3 = new Vector3((float)-want[j].t.x, (float)want[j].t.y, (float)want[j].t.z);
                var q4 = new Quaternion((float)want[j].q.x, (float)-want[j].q.y, (float)-want[j].q.z, (float)want[j].q.w);
                Assert.That(Vector3.Distance(part.localPosition, t3), Is.LessThan(1e-5f), $"joint {j}: the mirror of the reference's place");
                Assert.That(Quaternion.Angle(part.localRotation, q4), Is.LessThan(1e-2f), $"joint {j}: and of its turn");
            }
        }

        [Test]
        public void A_version_1_record_is_refused_with_its_fix_and_drops_the_stale_rig()
        {
            _root = ArmRig.Build(out _part);
            var records = new PureRecordSource();
            ArticulatedRigImporter.Wire(_root, ArmRig.Record, Asset, records);
            Assert.That(ArticulatedRigImporter.Wire(_root, ArmRig.Version1, Asset, records), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<ArticulatedRig>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<ArticulatedJoint>(true), Is.Empty);
            Assert.That(records.Refusals, Has.Count.EqualTo(1));
            Assert.That(records.Refusals[0], Does.Contain("articulation record v1").And.Contain(Asset).And.Contain("re-export"));
        }

        [Test]
        public void A_model_without_the_record_drops_the_stale_rig()
        {
            _root = ArmRig.Build(out _part);
            var records = new PureRecordSource();
            ArticulatedRigImporter.Wire(_root, ArmRig.Record, Asset, records);
            Assert.That(ArticulatedRigImporter.Wire(_root, null, Asset, records), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<ArticulatedRig>(true), Is.Empty);
            Assert.That(_root.GetComponentsInChildren<ArticulatedJoint>(true), Is.Empty);
            Assert.That(records.Refusals, Is.Empty, "no record is no refusal");
        }

        const string UnitFolder = "Assets/UnitytkArticulatedRigUnitTests";

        // A delivery as a DCC writes one: every local the file's own value, the file's
        // unit in its UnitScaleFactor alone
        // (the plug-in's conversion rides the root's scale). arm_RIG > base > elbow (10 out)
        // > extend (5 out, a tx slide 2 back, 5 out) > extend_geo; the record states each
        // value x 0.01, as glTF frames the file.
        const string UnitRecord = @"{""version"": 2, ""rigs"": [{""name"": ""arm"", ""joints"": [
            {""name"": ""base"", ""parent"": null, ""t"": [0, 0, 0], ""q"": [0, 0, 0, 1],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -90, ""max"": 60, ""weight"": 1}]},
            {""name"": ""elbow"", ""parent"": 0, ""t"": [0.1, 0, 0], ""q"": [0, 0, 0, 1],
             ""anchor"": {""t"": [0.02, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -150, ""max"": 150, ""weight"": 1}]},
            {""name"": ""extend"", ""parent"": 1, ""t"": [0.05, 0, 0], ""q"": [0, 0, 0, 1],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""tx"", ""min"": -0.02, ""max"": 0.05, ""weight"": 1}]}],
            ""grab"": [{""node"": ""extend_geo"", ""joint"": 2}]}]}";

        // The delivery as an ASCII FBX 7.4 (Unity's importer reads it as a binary one).
        static string UnitFbx(double unitScaleFactor)
        {
            string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            var objects = new System.Text.StringBuilder();
            var links = new System.Text.StringBuilder();
            int models = 0;
            void Null(long id, string name, long parent, double x, string extra = "")
            {
                models++;
                objects.Append($"\tModel: {id}, \"Model::{name}\", \"Null\" {{\n\t\tVersion: 232\n\t\tProperties70:  {{\n" +
                               $"\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",{F(x)},0,0\n{extra}\t\t}}\n" +
                               "\t\tShading: T\n\t\tCulling: \"CullingOff\"\n\t}\n");
                links.Append($"\tC: \"OO\",{id},{parent}\n");
            }
            Null(1000, "arm_RIG", 0, 0);
            Null(1001, "base", 1000, 0);
            Null(1002, "elbow", 1001, 10);
            Null(1003, "extend", 1002, 5);
            Null(1004, "extend_geo", 1003, 2);
            // One line: an ASCII FBX string holds no line break.
            Null(9000, "data_export", 0, 0,
                 $"\t\t\tP: \"articulation\", \"KString\", \"\", \"U\", \"{UnitRecord.Replace("\r", " ").Replace("\n", " ").Replace("\"", "&quot;")}\"\n");
            return "; FBX 7.4.0 project file\n" +
                   "FBXHeaderExtension:  {\n\tFBXHeaderVersion: 1003\n\tFBXVersion: 7400\n\tCreator: \"unitytk tests\"\n}\n" +
                   "GlobalSettings:  {\n\tVersion: 1000\n\tProperties70:  {\n" +
                   "\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1\n\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   "\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2\n\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   "\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0\n\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   $"\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",{F(unitScaleFactor)}\n" +
                   "\t}\n}\n" +
                   $"Definitions:  {{\n\tVersion: 100\n\tCount: {models + 1}\n" +
                   "\tObjectType: \"GlobalSettings\" {\n\t\tCount: 1\n\t}\n" +
                   $"\tObjectType: \"Model\" {{\n\t\tCount: {models}\n\t}}\n}}\n" +
                   "Objects:  {\n" + objects + "}\n" +
                   "Connections:  {\n" + links + "}\n";
        }

        [Test]
        public void A_record_lands_on_the_joints_as_unitys_import_frames_the_file_in_every_unit()
        {
            // The real import of a delivery in centimetres, metres and millimetres
            // (UnitScaleFactor 1, 100, 0.1), and of the centimetre one at a global scale of 2:
            // every joint's rest, anchor and slide limit is where Unity's import put the file,
            // so the rig stands still at rest. The record was laid on as it stands, so a metre
            // file's rig snapped every joint to a hundredth of its place.
            var cases = new[] { (factor: 1.0, global: 1f, k: 1f), (100.0, 1f, 100f), (0.1, 1f, 0.1f), (1.0, 2f, 2f) };
            try
            {
                foreach (var (factor, global, k) in cases)
                {
                    if (!AssetDatabase.IsValidFolder(UnitFolder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(UnitFolder));
                    string asset = $"{UnitFolder}/arm_{Label(factor)}_{Label(global)}.fbx";
                    File.WriteAllText(asset, UnitFbx(factor));
                    AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var importer = (ModelImporter)AssetImporter.GetAtPath(asset);
                    Assert.That(importer, Is.Not.Null, asset);
                    if (global != 1f)
                    {
                        importer.globalScale = global;
                        importer.SaveAndReimport();
                    }
                    Assert.That(M3trik.Unitytk.Kernel.Imports.Frame(importer).Length(1.0), Is.EqualTo(k).Within(1e-5f * k), asset);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                    ArticulatedRig rig = model.GetComponentInChildren<ArticulatedRig>(true);
                    Assert.That(rig, Is.Not.Null, $"{asset}: the rig is wired");
                    Assert.That(rig.joints.Select(j => j.name), Is.EqualTo(new[] { "base", "elbow", "extend" }), asset);
                    foreach (ArticulatedJoint joint in rig.joints)
                        Assert.That(Vector3.Distance(joint.restPosition, joint.transform.localPosition),
                                    Is.LessThan(1e-5f * k), $"{asset}: {joint.name}'s rest is where the import put it");
                    ArticulatedJoint elbow = rig.joints[1], extend = rig.joints[2];
                    Assert.That(Vector3.Distance(elbow.anchorPosition, new Vector3(-0.02f * k, 0f, 0f)), Is.LessThan(1e-5f * k),
                                $"{asset}: the anchor point, mirrored, in the import's frame");
                    ArticulatedJoint.Channel slide = extend.channels.Single();
                    Assert.That(slide.min, Is.EqualTo(-0.05f * k).Within(1e-5f * k), $"{asset}: tx mirrored, its limits in the import's frame");
                    Assert.That(slide.max, Is.EqualTo(0.02f * k).Within(1e-5f * k), asset);
                    Assert.That(extend.tx, Is.EqualTo(0f).Within(1e-5f * k), $"{asset}: the slide reads 0 where the file stands");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(UnitFolder);
            }
        }

        static string Label(double v) => v.ToString(CultureInfo.InvariantCulture).Replace('.', 'p');

        [Test]
        public void The_importer_reads_the_articulation_record_at_its_generated_version()
        {
            var read = ReadsRecordAttribute.Read(new[] { typeof(ArticulatedRigImporter).Assembly });
            Assert.That(read[UnitytkRecords.Articulation], Is.EqualTo(ArticulationRecord.CurrentVersion));
            Assert.That(ArticulationRecord.CurrentVersion, Is.EqualTo(2));
        }
    }
}
