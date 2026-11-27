using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Interaction
{
    // The boxes around what each bone of a skinned mesh carries (Interactions.BoneBoxes): the vertices it carries
    // most, as bound, in the bone's own space, at least Interactions.LeastBoneBox across.
    internal static class SkinBoxes
    {
        internal static List<(Transform bone, Bounds box)> Of(SkinnedMeshRenderer skin)
        {
            var boxes = new List<(Transform, Bounds)>();
            Mesh mesh = skin == null ? null : skin.sharedMesh;
            if (mesh == null) return boxes;
            Transform[] bones = skin.bones;
            Matrix4x4[] binds = mesh.bindposes;
            BoneWeight[] weights = mesh.boneWeights;
            Vector3[] vertices = mesh.vertices;
            var found = new Dictionary<int, Bounds>();
            for (int i = 0; i < vertices.Length && i < weights.Length; i++)
            {
                int bone = Heaviest(weights[i]);
                if (bone < 0 || bone >= bones.Length || bone >= binds.Length || bones[bone] == null) continue;
                Vector3 at = binds[bone].MultiplyPoint3x4(vertices[i]);
                if (found.TryGetValue(bone, out Bounds box)) box.Encapsulate(at);
                else box = new Bounds(at, Vector3.zero);
                found[bone] = box;
            }
            foreach (var pair in found)
            {
                Transform bone = bones[pair.Key];
                Vector3 scale = bone.lossyScale, size = pair.Value.size;
                for (int axis = 0; axis < 3; axis++)
                {
                    float least = Interactions.LeastBoneBox / Mathf.Max(Mathf.Abs(scale[axis]), 1e-6f);
                    size[axis] = Mathf.Max(size[axis], least);
                }
                boxes.Add((bone, new Bounds(pair.Value.center, size)));
            }
            return boxes;
        }

        // The bone that carries a vertex most.
        static int Heaviest(BoneWeight w)
        {
            int bone = w.boneIndex0;
            float most = w.weight0;
            if (w.weight1 > most) { bone = w.boneIndex1; most = w.weight1; }
            if (w.weight2 > most) { bone = w.boneIndex2; most = w.weight2; }
            if (w.weight3 > most) bone = w.boneIndex3;
            return bone;
        }
    }
}
