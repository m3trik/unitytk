using System;
using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// What is seen of a part, as one box: the union of its renderers' world bounds (what a component
    /// places itself by: over a part's top, under its middle, framing it), or of its meshes' bounds in a
    /// space of the caller's (an import, where a renderer's bounds are not yet placed). The one union every
    /// part asks, its variants parameters: switched-off renderers counted or not, inactive objects or not,
    /// a branch left out, a fallback size where nothing shows.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>Renderer.bounds</c> (a renderer's world box) and <c>Bounds.Encapsulate</c>,
    /// which this unions; Unity has no call for a subtree's box. <c>Mesh.bounds</c> is the mesh's own box,
    /// in its local space, placed here through each mesh's matrix (its eight corners, or every vertex for a
    /// tight box).
    /// </remarks>
    public static class SceneBounds
    {
        /// <summary>
        /// The union of the world bounds of the renderers under <paramref name="node"/> (its own included).
        /// </summary>
        /// <param name="node">A part (null: none).</param>
        /// <param name="bounds">What it shows; default where it shows nothing.</param>
        /// <param name="includeDisabled">Count a switched-off renderer too (false: only those that draw).</param>
        /// <param name="includeInactive">Count a renderer on an inactive object too.</param>
        /// <param name="skip">A branch under <paramref name="node"/> left out (true: that object and all under
        /// it; <paramref name="node"/> itself is never asked); null leaves out nothing.</param>
        /// <returns>Whether any renderer counted.</returns>
        public static bool TryOf(Transform node, out Bounds bounds, bool includeDisabled = false, bool includeInactive = false,
                                 Func<Transform, bool> skip = null)
        {
            var renderers = new List<Renderer>();
            Renderers(node, renderers, includeInactive, skip);
            return TryOf(renderers, out bounds, includeDisabled);
        }

        /// <summary>
        /// The renderers under <paramref name="node"/> (its own included) that
        /// <see cref="TryOf(Transform, out Bounds, bool, bool, Func{Transform, bool})"/> counts, switched off or not, put in
        /// <paramref name="into"/> (emptied first): for a component that gathers them once and asks
        /// <see cref="TryOf(IReadOnlyList{Renderer}, out Bounds, bool)"/> every frame.
        /// </summary>
        /// <param name="node">A part (null: none).</param>
        /// <param name="into">The list they go in.</param>
        /// <param name="includeInactive">Take a renderer on an inactive object too.</param>
        /// <param name="skip">A branch under <paramref name="node"/> left out (true: that object and all under
        /// it; <paramref name="node"/> itself is never asked); null leaves out nothing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
        public static void Renderers(Transform node, List<Renderer> into, bool includeInactive = false, Func<Transform, bool> skip = null)
        {
            if (into == null) throw new ArgumentNullException(nameof(into), "name the list they go in");
            into.Clear();
            if (node == null) return;
            node.GetComponentsInChildren(includeInactive, into);
            if (skip != null) into.RemoveAll(renderer => Skipped(renderer.transform, node, skip));
        }

        /// <summary>
        /// The union of <paramref name="renderers"/>' world bounds, null ones left out: for a component that
        /// keeps its renderers and asks every frame (it allocates nothing).
        /// </summary>
        /// <param name="renderers">The renderers (null: none).</param>
        /// <param name="bounds">What they show; default where none counts.</param>
        /// <param name="includeDisabled">Count a switched-off renderer too.</param>
        /// <returns>Whether any renderer counted.</returns>
        public static bool TryOf(IReadOnlyList<Renderer> renderers, out Bounds bounds, bool includeDisabled = false)
        {
            bounds = default;
            if (renderers == null) return false;
            bool any = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !includeDisabled && !renderer.enabled) continue;
                Grow(ref bounds, ref any, renderer.bounds);
            }
            return any;
        }

        /// <summary>
        /// What is seen of <paramref name="node"/> (<see cref="TryOf(Transform, out Bounds, bool, bool, Func{Transform, bool})"/>),
        /// else a box <paramref name="fallbackSize"/> across at its position (0: a point there).
        /// </summary>
        /// <param name="node">A part.</param>
        /// <param name="fallbackSize">The size of the box where it shows nothing.</param>
        /// <param name="includeDisabled">Count a switched-off renderer too.</param>
        /// <param name="includeInactive">Count a renderer on an inactive object too.</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static Bounds Of(Transform node, float fallbackSize = 0f, bool includeDisabled = false, bool includeInactive = false)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            return TryOf(node, out Bounds bounds, includeDisabled, includeInactive)
                ? bounds
                : new Bounds(node.position, Vector3.one * Mathf.Max(0f, fallbackSize));
        }

        /// <summary>
        /// The union of the bounds of the meshes under <paramref name="node"/> (a mesh filter's or a skinned
        /// renderer's shared mesh, on inactive objects too), in <paramref name="space"/>'s local axes: where a
        /// renderer's world bounds are not placed yet (a model's import) or a box in a part's own frame is wanted.
        /// </summary>
        /// <param name="node">A part (null: none).</param>
        /// <param name="bounds">What its meshes take up; default where it has none.</param>
        /// <param name="space">The space the box is in (null: world).</param>
        /// <param name="everyVertex">A tight box over every vertex (the mesh must be readable); false: each mesh's
        /// own box's eight corners, which a turned mesh overshoots.</param>
        /// <returns>Whether any mesh counted.</returns>
        public static bool TryOfMeshes(Transform node, out Bounds bounds, Transform space = null, bool everyVertex = false)
        {
            bounds = default;
            if (node == null) return false;
            var meshes = new List<(Mesh, Matrix4x4)>();
            Matrix4x4 into = space != null ? space.worldToLocalMatrix : Matrix4x4.identity;
            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                          : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh != null) meshes.Add((mesh, into * renderer.transform.localToWorldMatrix));
            }
            return TryOfMeshes(meshes, out bounds, everyVertex);
        }

        /// <summary>
        /// The union of <paramref name="meshes"/>' bounds, each placed by its matrix (its local space to the
        /// box's): for a caller that gathered its meshes itself.
        /// </summary>
        /// <param name="meshes">Each mesh with the matrix from its local space to the box's; a null mesh is left out.</param>
        /// <param name="bounds">What they take up; default where none counts.</param>
        /// <param name="everyVertex">A tight box over every vertex (each mesh readable); false: each mesh's own
        /// box's eight corners.</param>
        /// <returns>Whether any mesh counted.</returns>
        public static bool TryOfMeshes(IEnumerable<(Mesh mesh, Matrix4x4 toSpace)> meshes, out Bounds bounds, bool everyVertex = false)
        {
            bounds = default;
            if (meshes == null) return false;
            bool any = false;
            foreach (var (mesh, toSpace) in meshes)
            {
                if (mesh == null) continue;
                if (everyVertex)
                {
                    foreach (Vector3 vertex in mesh.vertices) Grow(ref bounds, ref any, toSpace.MultiplyPoint3x4(vertex));
                    continue;
                }
                Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
                for (int i = 0; i < 8; i++)
                    Grow(ref bounds, ref any, toSpace.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
            }
            return any;
        }

        static void Grow(ref Bounds bounds, ref bool any, Bounds more)
        {
            if (any) bounds.Encapsulate(more);
            else
            {
                bounds = more;
                any = true;
            }
        }

        static void Grow(ref Bounds bounds, ref bool any, Vector3 point)
        {
            if (any) bounds.Encapsulate(point);
            else
            {
                bounds = new Bounds(point, Vector3.zero);
                any = true;
            }
        }

        // Whether t, or an object between it and under (under itself never asked), is one skip leaves out.
        static bool Skipped(Transform t, Transform under, Func<Transform, bool> skip)
        {
            for (; t != null && t != under; t = t.parent)
                if (skip(t)) return true;
            return false;
        }
    }
}
