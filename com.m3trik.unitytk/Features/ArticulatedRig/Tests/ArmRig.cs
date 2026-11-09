using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The articulated rigs the feature's tests pose. The arm: two hinges about z, a base
    /// and an elbow 0.1 m out, with a grab part on the elbow -- as Unity's FBX import lands a
    /// delivery (X mirrored, metres), with its v2 record. And a flat model built in Unity:
    /// every part a child of the root, its hinges in its anchors, chained by its parents.
    /// </summary>
    static class ArmRig
    {
        /// <summary>The arm's record (version 2): every joint, anchor, channel and limit, and the grab part.</summary>
        public const string Record = @"{""version"": 2, ""rigs"": [{""name"": ""arm"", ""joints"": [
            {""name"": ""base"", ""parent"": null, ""t"": [0, 0, 0], ""q"": [0, 0, 0, 1],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -90, ""max"": 60, ""weight"": 1}]},
            {""name"": ""elbow"", ""parent"": 0, ""t"": [0.1, 0, 0], ""q"": [0, 0, 0, 1],
             ""anchor"": {""t"": [0, 0, 0], ""q"": [0, 0, 0, 1]}, ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -150, ""max"": 150, ""weight"": 1}]}],
            ""grab"": [{""node"": ""elbow_geo"", ""joint"": 1}]}]}";

        /// <summary>The same rig as a version 1 record wrote it: no anchors, lengths in the file's units.</summary>
        public const string Version1 = @"{""version"": 1, ""rigs"": [{""name"": ""arm"", ""joints"": [
            {""name"": ""base"", ""parent"": null, ""t"": [0, 0, 0], ""q"": [0, 0, 0, 1], ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -90, ""max"": 60, ""weight"": 1}]},
            {""name"": ""elbow"", ""parent"": 0, ""t"": [10, 0, 0], ""q"": [0, 0, 0, 1], ""rotate_order"": ""xyz"",
             ""channels"": [{""channel"": ""rz"", ""min"": -150, ""max"": 150, ""weight"": 1}]}],
            ""grab"": [{""node"": ""elbow_geo"", ""joint"": 1}]}]}";

        /// <summary>
        /// The model root: <c>Arm</c> &gt; <c>arm_RIG</c> &gt; <c>base</c> &gt; <c>elbow</c>
        /// (the record's 0.1 m, mirrored) &gt; <c>elbow_geo</c>, a cube mesh with no collider,
        /// 0.05 past the elbow. Names take <paramref name="prefix"/> (a namespace).
        /// </summary>
        public static GameObject Build(out Transform part, string prefix = "")
        {
            var root = new GameObject("Arm");
            var space = Child(prefix + "arm_RIG", root.transform, Vector3.zero);
            var bottom = Child(prefix + "base", space, Vector3.zero);
            var elbow = Child(prefix + "elbow", bottom, new Vector3(-0.1f, 0f, 0f));
            part = Child(prefix + "elbow_geo", elbow, new Vector3(-0.05f, 0f, 0f));
            part.localScale = Vector3.one * 0.02f;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = Cube();
            part.gameObject.AddComponent<MeshRenderer>();
            return root;
        }

        /// <summary>
        /// The arm of <see cref="Build"/> made a rig in Unity's terms, as the importer fills it:
        /// the rig on <c>arm_RIG</c>, a joint on <c>base</c> and <c>elbow</c> (rz hinges, the
        /// record's limits mirrored), the grab part on the elbow.
        /// </summary>
        public static ArticulatedRig Rig(GameObject root, Transform part)
        {
            Transform space = root.transform.GetChild(0);
            var rig = space.gameObject.AddComponent<ArticulatedRig>();
            ArticulatedJoint bottom = Joint(rig, space.GetChild(0), null, Hinge(ArticulatedJoint.Axis.Rz, -60f, 90f));
            ArticulatedJoint elbow = Joint(rig, bottom.transform.GetChild(0), bottom, Hinge(ArticulatedJoint.Axis.Rz, -150f, 150f));
            rig.grabs.Add(new ArticulatedRig.GrabPart { part = part, joint = elbow });
            return rig;
        }

        /// <summary>A joint on <paramref name="part"/>, at rest where it stands, listed on <paramref name="rig"/>.</summary>
        public static ArticulatedJoint Joint(ArticulatedRig rig, Transform part, ArticulatedJoint parent,
                                             params ArticulatedJoint.Channel[] channels)
        {
            var joint = part.gameObject.AddComponent<ArticulatedJoint>();
            joint.parent = parent;
            joint.restPosition = part.localPosition;
            joint.restRotation = part.localRotation;
            joint.channels = new List<ArticulatedJoint.Channel>(channels);
            rig.joints.Add(joint);
            rig.Rebuild();
            return joint;
        }

        /// <summary>A bounded channel.</summary>
        public static ArticulatedJoint.Channel Hinge(ArticulatedJoint.Axis axis, float min, float max)
            => new ArticulatedJoint.Channel { axis = axis, hasMin = true, min = min, hasMax = true, max = max };

        /// <summary>An unbounded channel.</summary>
        public static ArticulatedJoint.Channel Free(ArticulatedJoint.Axis axis) => new ArticulatedJoint.Channel { axis = axis };

        /// <summary>
        /// A flat model: <c>Flat</c> (the rig) &gt; <c>hub</c>, <c>upper</c>, <c>lower</c>, each a
        /// child of the root at its origin, unturned (every rest the identity), with a z hinge in
        /// its anchor -- the hub's at the origin, the upper's at (0.2, 0, 0), the lower's at
        /// (0.5, 0, 0) -- chained hub &lt; upper &lt; lower by their parents alone.
        /// </summary>
        public static ArticulatedRig Flat(out ArticulatedJoint hub, out ArticulatedJoint upper, out ArticulatedJoint lower)
        {
            var root = new GameObject("Flat");
            var rig = root.AddComponent<ArticulatedRig>();
            hub = Joint(rig, Child("hub", root.transform, Vector3.zero), null, Free(ArticulatedJoint.Axis.Rz));
            upper = Joint(rig, Child("upper", root.transform, Vector3.zero), hub, Free(ArticulatedJoint.Axis.Rz));
            upper.anchorPosition = new Vector3(0.2f, 0f, 0f);
            lower = Joint(rig, Child("lower", root.transform, Vector3.zero), upper, Free(ArticulatedJoint.Axis.Rz));
            lower.anchorPosition = new Vector3(0.5f, 0f, 0f);
            rig.Rebuild();
            return rig;
        }

        /// <summary>A cube mesh, from a primitive whose collider and object go at once.</summary>
        public static Mesh Cube()
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(primitive);
            return mesh;
        }

        /// <summary>A child of <paramref name="parent"/> at <paramref name="local"/>.</summary>
        public static Transform Child(string name, Transform parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }
    }
}
