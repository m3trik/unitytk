using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The ArticulatedRig sample, imported as the Package Manager imports it: a desk lamp the
    /// DCC's Articulated Rig made -- a post swivelling and tilting on its base, an upper arm
    /// hinged on the post, an extension sliding out of the arm and a shade hinged at its tip
    /// -- with a two-second take, written at its first frame. The rig and every joint resolve
    /// by reference with the record's channels, limits and anchors in Unity's axes; at the
    /// file's own pose every part stands where the DCC placed it; a pose set through the
    /// values, and a grab dragged frame by frame, move the parts as pythontk's model (the
    /// record read in its own frame) poses and solves them; Set Limits From Pose and Return
    /// To Animation work on it; and the demo recipe builds a scene whose pointer grabs it.
    /// The project these run in imports every sample of the package, so a sample that is not
    /// there fails: it is never skipped.
    /// </summary>
    public class ArticulatedRigSampleTests
    {
        const string Package = "com.m3trik.unitytk";
        const string SampleName = "ArticulatedRig";
        const string Recipe = "M3trik.Unitytk.Samples.ArticulatedRig.ArticulatedRigDemoRecipe, M3trik.Unitytk.Samples.ArticulatedRig.Editor";

        /// <summary>
        /// The record the sample delivers (articulation v2, as the recipe's rig wrote it): glTF's
        /// frame, metres, every anchor the identity. The tests read it in its own frame.
        /// </summary>
        const string Record = @"{""version"": 2, ""rigs"": [{""name"": ""lamp"", ""joints"": [
            {""name"": ""lamp_LowerArm_jnt"", ""parent"": null, ""t"": [0, 0.03, 0], ""q"": [0, 0, 0.7071067811865476, 0.7071067811865476],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""zyx"",
             ""channels"": [{""channel"": ""rx"", ""min"": -90, ""max"": 90, ""weight"": 1},
                            {""channel"": ""rz"", ""min"": -35, ""max"": 35, ""weight"": 1}]},
            {""name"": ""lamp_UpperArm_jnt"", ""parent"": 0, ""t"": [0.31, 0, 0], ""q"": [0, 0, -0.7071067811865476, 0.7071067811865476],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -60, ""max"": 45, ""weight"": 1}]},
            {""name"": ""lamp_Extension_jnt"", ""parent"": 1, ""t"": [0.14, 0, 0], ""q"": [0, 0, 0, 1],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""tx"", ""min"": -0.04, ""max"": 0.08, ""weight"": 1}]},
            {""name"": ""lamp_Head_jnt"", ""parent"": 2, ""t"": [0.16, 0, 0], ""q"": [0, 0, -0.7071067811865476, 0.7071067811865476],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -50, ""max"": 70, ""weight"": 1}]}],
            ""grab"": [{""node"": ""LowerArm"", ""joint"": 0}, {""node"": ""UpperArm"", ""joint"": 1},
                       {""node"": ""Extension"", ""joint"": 2}, {""node"": ""Head"", ""joint"": 3}]}]}";

        /// <summary>The rig's space, the node its root joint hangs under.</summary>
        const string Space = "lamp_RIG";

        /// <summary>The take's one clip: the recipe's shot.</summary>
        const string Clip = "Reach";

        /// <summary>
        /// The channel values in slot order (the post's rx and rz, the arm's rz, the extension's
        /// tx, the shade's rz) as the DCC keyed them at frame 1 (the file's own pose) and again at
        /// frame 48 (the take's last), in the record's convention.
        /// </summary>
        static readonly double[] FilePose = { 20, -10, -20, 0.02, 15 };

        /// <summary>
        /// Each part's world pose at frame 1 in the DCC (centimetres, Y up; the rotation as
        /// (x, y, z, w)), read off the recipe's scene.
        /// </summary>
        static readonly (string part, double[] t, double[] q)[] DccParts =
        {
            ("Base", new[] { 0.0, 1.5, 0.0 }, new[] { 0.0, 0.0, 0.0, 1.0 }),
            ("LowerArm", new[] { 2.529227, 18.26452, -0.920563 }, new[] { -0.0151344, 0.1729874, -0.0858317, 0.9810603 }),
            ("UpperArm", new[] { 15.637823, 27.02904, -5.691702 }, new[] { -0.0449435, 0.1677313, -0.254887, 0.9512512 }),
            ("Extension", new[] { 24.589598, 21.52904, -8.949882 }, new[] { -0.0449435, 0.1677313, -0.254887, 0.9512512 }),
            ("Head", new[] { 29.883927, 12.699411, -10.87686 }, new[] { -0.0226656, 0.1721626, -0.1285432, 0.9763826 }),
        };

        /// <summary>The ArticulationConformance <c>solve</c> tolerance (pythontk): degrees, and metres for a slide.</summary>
        const double SolveTolerance = 1.0e-3;

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // ------------------------------------------------------------ the sample
        static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            Assert.That(sample.displayName, Is.EqualTo(SampleName),
                        "the package lists no ArticulatedRig sample: its package.json's samples are written by the workspace's sample sync");
            Assert.That(sample.isImported, Is.True,
                        $"the ArticulatedRig sample is not imported at {sample.importPath}: import it from the Package Manager's " +
                        "Samples tab (the owned project's builder copies every sample in)");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        static string Model() => Folder() + "/ArticulatedRig.fbx";

        static GameObject Prefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Model());
            Assert.That(prefab, Is.Not.Null, $"no model at {Model()}");
            return prefab;
        }

        static Transform Node(GameObject root, string name)
        {
            Transform[] found = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            Assert.That(found, Has.Length.EqualTo(1), $"one node named {name}");
            return found[0];
        }

        static ArticulatedRig RigOf(GameObject root)
        {
            ArticulatedRig[] rigs = root.GetComponentsInChildren<ArticulatedRig>(true);
            Assert.That(rigs, Has.Length.EqualTo(1), "one Articulated Rig: the record's one rig");
            return rigs[0];
        }

        /// <summary>The sample's model placed in a new scene, at the origin, unrotated.</summary>
        static GameObject Placed()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(Prefab());
            Assert.That(root.transform.localToWorldMatrix, Is.EqualTo(Matrix4x4.identity), "the model's root at rest");
            return root;
        }

        static ArticulationRecord.Shape.Rig Spec()
            => ArticulationRecord.Shape.Payload.Read(Json.Parse(Record, false)).rigs.Single();

        // --------------------------------------------- the record's frame <-> Unity's
        // Unity's FBX import lands the record's frame with X mirrored: a point (x, y, z) as
        // (-x, y, z), a rotation (x, y, z, w) as (x, -y, -z, w), and the channels that turn or
        // slide the other way there -- ry, rz and tx -- negated. Stated here, not borrowed from
        // the importer, so the tests hold it to the record.
        static Vector3 P(double[] t) => new Vector3((float)-t[0], (float)t[1], (float)t[2]);
        static Quaternion T(double[] q) => new Quaternion((float)q[0], (float)-q[1], (float)-q[2], (float)q[3]);
        static Vector3 U(ArticulationModel.Vec v) => new Vector3((float)-v.x, (float)v.y, (float)v.z);
        static Quaternion U(ArticulationModel.Quat q) => new Quaternion((float)q.x, (float)-q.y, (float)-q.z, (float)q.w);
        static ArticulationModel.Vec R(Vector3 v) => new ArticulationModel.Vec(-v.x, v.y, v.z);

        static bool Flips(string channel) => channel == "ry" || channel == "rz" || channel == "tx";

        /// <summary>A state between Unity's convention and the record's (its own inverse).</summary>
        static double[] Mirror(ArticulationModel model, double[] state)
        {
            var out_ = new double[state.Length];
            for (int s = 0; s < state.Length; s++) out_[s] = Flips(model.ChannelName(s)) ? -state[s] : state[s];
            return out_;
        }

        static void Near(double[] got, double[] want, double tolerance, string what)
        {
            Assert.That(got, Has.Length.EqualTo(want.Length), what);
            for (int i = 0; i < want.Length; i++)
                Assert.That(got[i], Is.EqualTo(want[i]).Within(tolerance),
                            $"{what}: slot {i}, [{string.Join(", ", got.Select(v => v.ToString("0.#####")))}] for " +
                            $"[{string.Join(", ", want.Select(v => v.ToString("0.#####")))}]");
        }

        static void Near(Transform node, Vector3 position, Quaternion rotation, float tolerance, string what)
        {
            Assert.That(Vector3.Distance(node.position, position), Is.LessThan(tolerance),
                        $"{what}: at {node.position.ToString("F5")} for {position.ToString("F5")}");
            Assert.That(Quaternion.Angle(node.rotation, rotation), Is.LessThan(0.01f),
                        $"{what}: turned {node.rotation.ToString("F5")} for {rotation.ToString("F5")}");
        }

        // ----------------------------------------------------------------- tests
        [Test]
        public void The_rig_and_every_joint_resolve_by_reference_with_the_records_channels_limits_and_anchors()
        {
            GameObject root = Prefab();
            ArticulatedRig rig = RigOf(root);
            ArticulationRecord.Shape.Rig spec = Spec();
            Assert.That(rig.transform, Is.SameAs(Node(root, Space)), "on the node its root joint hangs under");
            Assert.That(rig.joints.Select(j => j.name), Is.EqualTo(spec.joints.Select(j => j.name)), "every joint, parents first");
            Assert.That(rig.followAnimation, Is.True, "the delivery's clip animates the parts");
            Assert.That(rig.IsValid, Is.True, rig.Problem);

            for (int j = 0; j < spec.joints.Count; j++)
            {
                ArticulationRecord.Shape.Joint want = spec.joints[j];
                ArticulatedJoint joint = rig.joints[j];
                Assert.That(joint.transform, Is.SameAs(Node(root, want.name)), $"{want.name}: on its node, by reference");
                Assert.That(joint.parent, Is.SameAs(want.parent.HasValue ? rig.joints[want.parent.Value] : null),
                            $"{want.name}: its parent joint, by reference");
                Assert.That(Vector3.Distance(joint.restPosition, P(want.t)), Is.LessThan(1e-6f), $"{want.name}: its rest, X mirrored");
                Assert.That(Quaternion.Angle(joint.restRotation, T(want.q)), Is.LessThan(1e-3f), $"{want.name}: its rest turn, mirrored");
                Assert.That(joint.anchorPosition, Is.EqualTo(P(want.anchor.t)), $"{want.name}: the producer's identity anchor");
                Assert.That(Quaternion.Angle(joint.anchorRotation, T(want.anchor.q)), Is.LessThan(1e-3f), $"{want.name}: its anchor's axes");
                Assert.That(joint.rotateOrder.ToString().ToLowerInvariant(), Is.EqualTo(want.rotate_order), $"{want.name}: its rotate order");
                Assert.That(joint.channels, Has.Count.EqualTo(want.channels.Count), $"{want.name}: its channels");
                for (int c = 0; c < want.channels.Count; c++)
                {
                    ArticulationRecord.Shape.Channel wc = want.channels[c];
                    ArticulatedJoint.Channel got = joint.channels[c];
                    string what = $"{want.name} {wc.channel}";
                    Assert.That(ArticulationModel.ChannelNames[(int)got.axis], Is.EqualTo(wc.channel), $"{what}: its axis, in order");
                    // A channel that turns or slides the other way in Unity has its limits negated and swapped.
                    double lo = Flips(wc.channel) ? -wc.max.Value : wc.min.Value;
                    double hi = Flips(wc.channel) ? -wc.min.Value : wc.max.Value;
                    Assert.That(got.hasMin && got.hasMax, Is.True, $"{what}: bounded on both sides");
                    Assert.That(got.min, Is.EqualTo((float)lo).Within(1e-4f), $"{what}: min");
                    Assert.That(got.max, Is.EqualTo((float)hi).Within(1e-4f), $"{what}: max");
                    Assert.That(got.weight, Is.EqualTo((float)wc.weight.Value), $"{what}: weight");
                }
            }

            Assert.That(rig.grabs.Select(g => (g.part.name, rig.IndexOf(g.joint))),
                        Is.EqualTo(spec.grab.Select(g => (g.node, g.joint.Value))), "every grab part and the joint it rides");
            foreach (ArticulatedRig.GrabPart grab in rig.grabs)
            {
                Assert.That(grab.part, Is.SameAs(Node(root, grab.part.name)), $"{grab.part.name}: by reference");
                Assert.That(grab.part.parent, Is.SameAs(grab.joint.transform), $"{grab.part.name}: under its joint");
                var collider = grab.part.GetComponent<MeshCollider>();
                Assert.That(collider != null && collider.convex, Is.True, $"{grab.part.name}: a convex collider a pointer hits");
            }
            Assert.That(Node(root, "Base").GetComponent<Collider>(), Is.Null, "the base rides no joint: nothing grabs it");
        }

        [Test]
        public void At_the_files_own_pose_every_part_lands_where_the_dcc_placed_it()
        {
            GameObject root = Placed();
            ArticulatedRig rig = RigOf(root);
            Assert.That(rig.transform.localToWorldMatrix, Is.EqualTo(Matrix4x4.identity), "the rig's space: the DCC's, at its origin");
            ArticulationModel model = rig.Model;
            Near(rig.GetState(), Mirror(model, FilePose), 1e-3, "the values the import read off the file's pose");

            // Every part where the DCC put it at frame 1: centimetres to metres, X mirrored.
            foreach (var (part, t, q) in DccParts)
                Near(Node(root, part), P(t.Select(v => v * 0.01).ToArray()), T(q), 1e-5f, $"{part} as the file holds it");

            // Posed again from its values, through the joint chain, nothing moves.
            Assert.That(rig.ApplyNow(), Is.True);
            foreach (var (part, t, q) in DccParts)
                Near(Node(root, part), P(t.Select(v => v * 0.01).ToArray()), T(q), 1e-5f, $"{part} posed by the rig");
        }

        [Test]
        public void A_pose_set_through_the_values_moves_the_parts_as_the_model_says()
        {
            GameObject root = Placed();
            ArticulatedRig rig = RigOf(root);
            var reference = new ArticulationModel(Spec());
            double[] pose = { -40, 20, -30, 0.05, 45 };  // the record's convention
            double[] unity = Mirror(reference, pose);
            Vector3 headBefore = Node(root, "Head").position;
            for (int s = 0; s < unity.Length; s++)
                rig.joints[reference.ChannelJoint(s)].SetValue((ArticulatedJoint.Axis)reference.ChannelCode(s), (float)unity[s]);
            Assert.That(rig.ApplyNow(), Is.True);

            ArticulationModel.Frame[] world = reference.World(pose);
            for (int j = 0; j < world.Length; j++)
            {
                ArticulatedJoint joint = rig.joints[j];
                Near(joint.transform, rig.transform.TransformPoint(U(world[j].t)), rig.transform.rotation * U(world[j].q), 1e-5f,
                     $"{joint.name} at the model's pose");
            }
            Assert.That(Vector3.Distance(Node(root, "Head").position, headBefore), Is.GreaterThan(0.05f), "the shade moved with them");
            Near(rig.ReadState(), unity, 1e-3, "the values read back off the parts");
        }

        [Test]
        public void A_grab_drag_solves_as_the_models_Solve_does()
        {
            GameObject root = Placed();
            ArticulatedRig rig = RigOf(root);
            rig.grabRotation = ArticulatedRig.GrabRotation.Free;
            var reference = new ArticulationModel(Spec());
            const int Head = 3;
            Transform head = Node(root, "Head");
            Vector3 point = head.position;
            Assert.That(rig.BeginGrab(head, point), Is.True, "the shade is a grab part");
            Assert.That(rig.GrabbedJoint, Is.SameAs(rig.joints[Head]));

            double[] state = Mirror(reference, rig.GetState());
            Near(state, FilePose, 1e-3, "the grab starts from the pose on screen");
            ArticulationModel.Vec local = reference.ToLocal(state, Head, R(rig.transform.InverseTransformPoint(point)));
            Vector3 move = new Vector3(0.04f, 0.05f, 0.02f);
            const int Steps = 4;
            for (int k = 1; k <= Steps; k++)
            {
                Vector3 target = point + move * k / Steps;
                rig.UpdateGrab(target);
                state = reference.Solve(state, Head, local, R(rig.transform.InverseTransformPoint(target)));
                Near(Mirror(reference, rig.GetState()), state, SolveTolerance, $"step {k}: the rig's solve, the model's");
            }
            Vector3 last = point + move;
            float miss = Vector3.Distance(rig.GrabPointWorld, last);
            Assert.That(miss, Is.LessThan(0.1f * move.magnitude), $"the held point follows the drag ({miss:0.#####} m off)");
            rig.EndGrab();
            Assert.That(rig.IsHoldingPose, Is.True, "the released pose holds");
        }

        [Test]
        public void SetLimitsFromPose_and_ReturnToAnimation_work_on_it()
        {
            GameObject root = Placed();
            ArticulatedRig rig = RigOf(root);
            ArticulatedJoint arm = rig.joints[1];
            ArticulatedJoint.Channel hinge = arm.channels.Single();
            Assert.That(arm.rz, Is.EqualTo(20f).Within(1e-3f), "the arm at the file's pose (the DCC's -20, mirrored)");
            Assert.That(ArticulatedRigActions.SetLimitsFromPose(arm, widen: true), Is.Zero, "inside its limits: widening moves none");
            Assert.That(ArticulatedRigActions.SetLimitsFromPose(arm), Is.EqualTo(1), "its max from the pose");
            Assert.That((hinge.min, hinge.max), Is.EqualTo((-45f, arm.rz)), "the max taken from the pose, the min kept");

            // A held pose stands over the clip; returned, the rig follows the clip again.
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Model()).OfType<AnimationClip>().Single(c => c.name == Clip);
            var reference = new ArticulationModel(Spec());
            double[] held = Mirror(reference, new double[] { 0, 0, 0, 0, 0 });
            rig.ApplyState(held);
            Assert.That(rig.IsHoldingPose, Is.True);
            Quaternion posed = arm.transform.localRotation;
            // Its last key (frame 48) is the first's pose, a pose the held one is not; an end
            // key, sampled exactly where an inner one may be reduced by the import.
            float end = clip.length;
            clip.SampleAnimation(root, end);
            rig.Step();
            Near(rig.GetState(), held, 1e-6, "the held pose, over the clip");
            Assert.That(Quaternion.Angle(arm.transform.localRotation, posed), Is.LessThan(1e-3f), "and the parts there");

            rig.ReturnToAnimation();
            Assert.That(rig.IsHoldingPose, Is.False);
            clip.SampleAnimation(root, end);
            rig.Step();
            Near(rig.GetState(), Mirror(reference, FilePose), 1e-3, "the values follow the clip: its last keys");
            Assert.That(arm.rz, Is.EqualTo(20f).Within(1e-3f), "the arm's field, keyable, tracking the clip");
        }

        [Test]
        public void The_demo_recipe_builds_a_scene_whose_pointer_grabs_the_lamp()
        {
            string folder = Folder();
            Type recipe = Type.GetType(Recipe);
            Assert.That(recipe, Is.Not.Null, "the sample's editor assembly is missing: it did not compile, or the sample is not imported");
            var scene = (Scene)recipe.GetMethod("Build").Invoke(null, new object[] { folder });
            Assert.That(scene.path, Is.EqualTo(folder + "/ArticulatedRigDemo.unity"), "saved beside the model");
            GameObject model = scene.GetRootGameObjects().Single(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == Prefab());
            var animator = model.GetComponent<Animator>();
            Assert.That(animator != null && animator.runtimeAnimatorController != null, Is.True, "an Animator plays the take");
            AnimationClip clip = animator.runtimeAnimatorController.animationClips.Single();
            Assert.That(clip.name, Is.EqualTo(Clip));
            Assert.That(clip.length, Is.EqualTo(2f - 1f / 24f).Within(1e-3f), "the take: two seconds, frame 1 to 48");

            ArticulatedRig rig = RigOf(model);
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "a camera");
            var grabber = camera.GetComponent<ArticulatedRigPointerGrab>();
            Assert.That(grabber, Is.Not.Null, "the pointer grab, on the camera");
            Assert.That((grabber.viewCamera, grabber.rig), Is.EqualTo((camera, rig)), "looking through it, at the lamp's rig");

            // A press on the shade as the pointer sees it takes it, a drag moves it, a release holds the pose.
            Transform head = Node(model, "Head");
            Vector3 screen = camera.WorldToScreenPoint(head.position);
            Assert.That(screen.z, Is.GreaterThan(0f), "the shade in front of the camera");
            Assert.That(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight).Contains(screen), Is.True, $"the shade in view: {screen}");
            Physics.SyncTransforms();
            Assert.That(grabber.TryBegin(camera.ScreenPointToRay(screen), camera.transform), Is.True, "a press on the shade takes it");
            Assert.That((grabber.Held, rig.GrabbedPart), Is.EqualTo((rig, head)));
            Vector3 before = head.position;
            grabber.Drag(camera.ScreenPointToRay(screen + new Vector3(camera.pixelWidth * 0.05f, camera.pixelHeight * 0.05f, 0f)),
                         camera.transform, 0f);
            Assert.That(Vector3.Distance(head.position, before), Is.GreaterThan(0.005f), "the drag moves the shade");
            grabber.End();
            Assert.That(rig.IsGrabbing, Is.False);
            Assert.That(rig.IsHoldingPose, Is.True, "the released pose holds over the take");
        }
    }
}
