using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// The ShadowPlane feature's actions, in Unity's terms: put a shadow plane under a
    /// target (<see cref="Create"/>), bake or rebake a plane's horizon map from its
    /// target's meshes (<see cref="BakeHorizon"/>, the ported baker
    /// <see cref="ShadowHorizonBaker"/>), and rebake every plane whose target changed
    /// since (<see cref="RebakeStale"/>). Each takes an optional <see cref="OpContext"/>:
    /// a dry run plans, a menu's run is one undo step for its scene edits, and none just
    /// runs. A second run of each changes nothing. A map is written beside the scene
    /// (<c>&lt;scene&gt;_ShadowPlanes/</c>), or beside the model of an unsaved scene's
    /// plane, each file asked as a write.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity bakes no height field for a ground shadow -- its shadow maps are
    /// per frame and per light, its lightmapper bakes one light's result -- and has no
    /// action that makes one; the map is the shadows record's own format, which the
    /// producers bake in Python, so its baker is ported and held to their golden cases
    /// (<see cref="ShadowHorizonBaker"/>) rather than replaced. Unity's own
    /// <c>Lightmapping</c> bake shows the shape copied here: the map a file beside the
    /// scene, in a folder named after it.
    /// </remarks>
    public static class ShadowPlaneActions
    {
        /// <summary>A plane has no target to bake from.</summary>
        public const string NoTargetCode = "unity.shadow_plane.no_target";
        /// <summary>A plane has no contact, the frame its map is baked in.</summary>
        public const string NoContactCode = "unity.shadow_plane.no_contact";
        /// <summary>A target holds no mesh to bake.</summary>
        public const string NoMeshesCode = "unity.shadow_plane.no_meshes";
        /// <summary>Meshes under a target that cast no shadow (inactive, no renderer or a disabled one, a later LOD).</summary>
        public const string SkippedCode = "unity.shadow_plane.skipped";
        /// <summary>The render pipeline does not draw the plane's shader.</summary>
        public const string NotDrawnCode = "unity.shadow_plane.not_drawn";

        /// <summary>The name a plane's contact takes: its target's, then this.</summary>
        public const string ContactSuffix = "_contact";
        /// <summary>The name a plane takes: its target's, then this.</summary>
        public const string PlaneSuffix = "_shadow";
        /// <summary>The shared material's file name, in the folder the maps go to.</summary>
        public const string MaterialFile = "ShadowPlane.mat";

        /// <summary>
        /// Put a shadow plane under <paramref name="target"/>: its contact at the centre of
        /// the target's footprint on its underside, a quad on the package shader (one shared
        /// material, beside the scene), and a <see cref="ShadowPlane"/> sized from the
        /// target's bounds -- its radius half the footprint's diagonal, its ground the
        /// target's lowest point -- in the frame of what the target stands on (its parent),
        /// cast by <paramref name="source"/> (the scene's sun when null), its horizon map
        /// baked (<see cref="BakeHorizon"/>). A target that has a plane already gets nothing.
        /// </summary>
        /// <param name="target">A scene object holding the meshes the shadow is of.</param>
        /// <param name="source">The light the shadow is cast by; null: the scene's sun.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The plane (the existing one, where the target has one); null in a dry run, or with no mesh.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> is not in a scene (a prefab asset).</exception>
        public static ShadowPlane Create(GameObject target, Light source = null, OpContext ctx = null)
        {
            InScene(target);
            ShadowPlane existing = target.GetComponentsInChildren<ShadowPlane>(true).FirstOrDefault(p => p.target == target.transform);
            if (existing != null) return existing;
            Bounds bounds;
            using (Gathered meshes = Gather(target.transform))
            {
                if (meshes.Parts.Count == 0)
                {
                    ctx.Raise(NoMeshesCode, $"'{target.name}' holds no mesh to cast a shadow from{meshes.LeftOut()}: pick the " +
                                            "object whose meshes the shadow is of.", location: target.name);
                    return null;
                }
                ReportSkipped(meshes, target.name, ctx);
                bounds = WorldBounds(meshes);
            }
            Shader shader = Shader.Find(ShadowPlaneImporter.ShaderName);
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            if (shader == null || host == null || !host.Draws(shader))
                ctx.Raise(NotDrawnCode, $"the {RenderPipelineHosts.NameOf(host)} render pipeline does not draw " +
                                        $"'{ShadowPlaneImporter.ShaderName}': the plane is made, and draws nothing there.",
                          location: target.name);

            string folder = FolderFor(target);
            string materialPath = folder + "/" + MaterialFile;
            string mapPath = MapPathFor(folder, target.name + PlaneSuffix);
            bool live = ctx == null || ctx.ShouldCreate(target.name + ContactSuffix, $"add the shadow's contact under '{target.name}'");
            live &= ctx == null || ctx.ShouldCreate(target.name + PlaneSuffix, $"add a shadow plane under '{target.name}'");
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
                live &= ctx == null || ctx.ShouldWrite(materialPath, "write the shadow planes' shared material");
            live &= ctx == null || ctx.ShouldWrite(mapPath, BakeAction(target.name + PlaneSuffix, target.name,
                                                                         ShadowHorizonBaker.DefaultSize, ShadowHorizonBaker.DefaultSpans));
            if (!live) return null;
            ctx?.Progress(0.0, $"Adding '{target.name}''s shadow plane");

            Transform space = target.transform.parent;
            Matrix4x4 toSpace = space != null ? space.worldToLocalMatrix : Matrix4x4.identity;
            var contact = new GameObject(target.name + ContactSuffix).transform;
            contact.SetParent(target.transform, false);
            contact.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            contact.rotation = space != null ? space.rotation : Quaternion.identity;
            ctx.Created(contact.gameObject);

            var go = new GameObject(target.name + PlaneSuffix);
            go.transform.SetParent(target.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SharedMaterial(materialPath, shader);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var plane = go.AddComponent<ShadowPlane>();
            plane.target = target.transform;
            plane.contact = contact;
            plane.source = source;
            plane.BindSpace(space);
            // Lengths in the space's frame: the bounds' extent along its axes.
            Vector3 size = toSpace.MultiplyVector(bounds.size);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            plane.radius = 0.5f * Mathf.Sqrt(size.x * size.x + size.z * size.z);
            plane.height = size.y;
            plane.ground = toSpace.MultiplyPoint3x4(contact.position).y;
            plane.fadeHeight = 2f * plane.height;
            ctx.Created(go);
            ctx?.Progress(0.2, $"Baking '{go.name}''s horizon map from '{target.name}'");
            using (Gathered meshes = Gather(target.transform))
                Bake(plane, meshes, ShadowHorizonBaker.DefaultSize, ShadowHorizonBaker.DefaultSpans, mapPath);
            ctx?.Progress(1.0, $"'{go.name}' baked");
            ctx.Edited(plane);
            ctx.Log($"Create Shadow Plane: '{go.name}' under '{target.name}', its horizon map '{mapPath}'.");
            return plane;
        }

        /// <summary>
        /// Bake <paramref name="plane"/>'s horizon map from its target's meshes, in the
        /// plane's frame (its contact's, lengths in its space's units), through
        /// <see cref="ShadowHorizonBaker.Bake"/>, and bind it: written as a PNG beside the
        /// scene (asked as a write; a map this action wrote before is written over, unless
        /// another plane in the open scenes holds it too -- a duplicate's -- which gets a new file),
        /// imported as data -- linear, no mips, clamp, point, as the importer pins a
        /// horizon map -- with the layout and the target's geometry signature on the plane. A
        /// plane whose target is unchanged since its map was baked is left alone unless
        /// <paramref name="force"/>. Meshes that draw nothing (<see cref="Gather"/>) are left
        /// out, and said (<see cref="SkippedCode"/>).
        /// </summary>
        /// <param name="plane">A scene's shadow plane with a target and a contact.</param>
        /// <param name="size">Footprint pixels per side, rounded up to a power of two of at least 8; 0: the plane's.</param>
        /// <param name="spans">Solid spans per pixel column; 0: the plane's.</param>
        /// <param name="force">Bake even where the target is unchanged.</param>
        /// <param name="ctx">The run's context: a dry run lists the write; null just bakes.</param>
        /// <returns>Whether it baked (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="plane"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="plane"/> is not in a scene.</exception>
        public static bool BakeHorizon(ShadowPlane plane, int size = 0, int spans = 0, bool force = false, OpContext ctx = null)
        {
            if (plane == null) throw new ArgumentNullException(nameof(plane));
            InScene(plane.gameObject);
            if (!CanBake(plane, ctx)) return false;
            size = BakedSize(size > 0 ? size : plane.horizonSize);
            spans = Math.Max(1, spans > 0 ? spans : plane.horizonSpans);
            using (Gathered meshes = Gather(plane.target))
            {
                if (meshes.Parts.Count == 0)
                {
                    ctx.Raise(NoMeshesCode, $"'{plane.target.name}', '{plane.name}''s target, holds no mesh to bake" +
                                            $"{meshes.LeftOut()}.", location: plane.name);
                    return false;
                }
                if (!force && Current(plane, meshes, size, spans)) return false;
                ReportSkipped(meshes, plane.target.name, ctx);
                string path = MapPathFor(FolderFor(plane.gameObject), plane, out ShadowPlane sharer);
                string action = BakeAction(plane.name, plane.target.name, size, spans);
                if (sharer != null)
                    action += $"; {NewFileNote} '{AssetDatabase.GetAssetPath(plane.horizonMap)}' is shared with '{sharer.name}'";
                bool live = ctx == null || ctx.ShouldWrite(path, action);
                live &= ctx == null || ctx.ShouldChange(plane, $"bind '{plane.name}''s new horizon map");
                if (!live) return true;
                Bake(plane, meshes, size, spans, path);
            }
            ctx.Edited(plane);
            return true;
        }

        /// <summary>
        /// The words a bake's write states when the plane's map is one another plane holds
        /// too (a duplicate's): the bake writes a new file, never over the shared one.
        /// </summary>
        public const string NewFileNote = "a new file, since its map";

        /// <summary>
        /// Rebake every shadow plane in the open scenes whose target's geometry changed
        /// since its map was baked (its signature differs: a mesh, its points, its pose in
        /// the plane's frame or the ground), at the plane's own size and spans
        /// (<see cref="BakeHorizon"/>), each plane a progress report. A plane with no
        /// target, contact or mesh is skipped.
        /// </summary>
        /// <param name="ctx">The run's context: a dry run lists each write; null just bakes.</param>
        /// <returns>How many planes it rebaked (in a dry run, would).</returns>
        public static int RebakeStale(OpContext ctx = null)
        {
            int baked = 0;
            List<ShadowPlane> planes = OpenPlanes();
            for (int i = 0; i < planes.Count; i++)
            {
                ShadowPlane plane = planes[i];
                ctx?.Progress((double)i / planes.Count, $"'{plane.name}' ({i + 1} of {planes.Count})");
                if (plane.target == null || plane.contact == null) continue;
                using (Gathered meshes = Gather(plane.target))
                {
                    if (meshes.Parts.Count == 0) continue;
                    if (Current(plane, meshes, BakedSize(plane.horizonSize), Math.Max(1, plane.horizonSpans))) continue;
                }
                if (BakeHorizon(plane, force: true, ctx: ctx)) baked++;
            }
            ctx?.Progress(1.0, $"{baked} plane(s) rebaked");
            if (ctx == null || !ctx.DryRun) ctx.Log($"Rebake Stale Shadow Planes: {baked} plane(s) rebaked.");
            return baked;
        }

        /// <summary>
        /// <paramref name="plane"/>'s geometry signature at its own size and spans
        /// (<see cref="Signature"/>); null without a target, a contact or a mesh.
        /// </summary>
        internal static string GeometryHash(ShadowPlane plane)
        {
            if (plane.target == null || plane.contact == null) return null;
            using (Gathered meshes = Gather(plane.target))
                return Signature(plane, meshes, BakedSize(plane.horizonSize), Math.Max(1, plane.horizonSpans));
        }

        /// <summary>
        /// The size the baker gives a map asked for at <paramref name="size"/>: a power of
        /// two of at least 8 (<see cref="ShadowHorizonBaker.Bake"/>'s rule).
        /// </summary>
        static int BakedSize(int size)
        {
            int s = 8;
            while (s < size) s <<= 1;
            return s;
        }

        // -- the signature ------------------------------------------------------------

        /// <summary>The signature's layout; another reads as stale.</summary>
        const string SignatureTag = "sig2";

        /// <summary>A signature's lengths match within this fraction of the target's extent.</summary>
        internal const double SignatureTolerance = 1e-4;

        /// <summary>
        /// What <paramref name="plane"/>'s map is baked from, as a string to keep on the plane
        /// (<see cref="ShadowPlane.horizonHash"/>) and compare (<see cref="Matches"/>), the
        /// same wherever the instance is placed: an exact digest of the map's size and spans
        /// and of each mesh's triangles and counts (a mesh asset's own points too); and, in
        /// the map's frame, the ground and each mesh's box and mean (a mesh asset's placement
        /// by four corners of its own bounds besides), compared within
        /// <see cref="SignatureTolerance"/> of the target's extent. The frame's points are
        /// carried in double through the local transforms from the contact's and the mesh's
        /// common ancestor down, so a far placement adds no float noise; a skinned mesh's
        /// pose (<c>BakeMesh</c>) is Unity's, in float.
        /// </summary>
        static string Signature(ShadowPlane plane, Gathered meshes, int size, int spans)
        {
            if (meshes.Parts.Count == 0) return null;
            plane.HorizonAxes(out _, out Vector3 axisA, out Vector3 axisB, out Vector3 axisUp,
                out _, out float sA, out float sB, out float sUp);
            var lengths = new List<double> { 0.0, GroundInFrame(plane, axisUp, sUp) };
            double extent = 0.0;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(size);
                writer.Write(spans);
                foreach (Part part in meshes.Parts)
                {
                    Mesh mesh = part.Mesh;
                    Transform common = CommonAncestor(part.Transform, plane.contact);
                    double[] toCommon = Relative(part.Transform, common);
                    double[] contact = Relative(plane.contact, common);
                    Matrix4x4 commonToWorld = common != null ? common.localToWorldMatrix : Matrix4x4.identity;
                    double[][] rows = { Row(commonToWorld, axisA, sA), Row(commonToWorld, axisUp, sUp), Row(commonToWorld, axisB, sB) };
                    var at = new double[3];
                    void InFrame(Vector3 v)
                    {
                        double x = toCommon[0] * v.x + toCommon[1] * v.y + toCommon[2] * v.z + toCommon[3] - contact[3];
                        double y = toCommon[4] * v.x + toCommon[5] * v.y + toCommon[6] * v.z + toCommon[7] - contact[7];
                        double z = toCommon[8] * v.x + toCommon[9] * v.y + toCommon[10] * v.z + toCommon[11] - contact[11];
                        for (int k = 0; k < 3; k++) at[k] = rows[k][0] * x + rows[k][1] * y + rows[k][2] * z;
                    }

                    Vector3[] vertices = mesh.vertices;
                    int[] triangles = mesh.triangles;
                    writer.Write(part.Baked);
                    writer.Write(vertices.Length);
                    writer.Write(triangles.Length);
                    foreach (int t in triangles) writer.Write(t);
                    var lo = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
                    var hi = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
                    var sum = new double[3];
                    foreach (Vector3 v in vertices)
                    {
                        // A mesh asset's points are its own: placement-free, so exact. A
                        // skinned mesh's are Unity's pose, which a placement's float noise reaches.
                        if (!part.Baked)
                        {
                            writer.Write(v.x);
                            writer.Write(v.y);
                            writer.Write(v.z);
                        }
                        InFrame(v);
                        for (int k = 0; k < 3; k++)
                        {
                            lo[k] = Math.Min(lo[k], at[k]);
                            hi[k] = Math.Max(hi[k], at[k]);
                            sum[k] += at[k];
                        }
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        lengths.Add(lo[k]);
                        lengths.Add(hi[k]);
                        lengths.Add(sum[k] / Math.Max(1, vertices.Length));
                        extent = Math.Max(extent, hi[k] - lo[k]);
                    }
                    if (part.Baked) continue;
                    // The mesh's placement in the frame: its bounds' corner and the three beside it.
                    Bounds b = mesh.bounds;
                    foreach (Vector3 corner in new[] { b.min, b.min + new Vector3(b.size.x, 0f, 0f),
                                                       b.min + new Vector3(0f, b.size.y, 0f), b.min + new Vector3(0f, 0f, b.size.z) })
                    {
                        InFrame(corner);
                        lengths.AddRange(at);
                    }
                }
                writer.Flush();
                lengths[0] = extent;
                string digest;
                using (var sha = SHA1.Create())
                {
                    byte[] bytes = sha.ComputeHash(stream.ToArray());
                    var hex = new StringBuilder();
                    for (int i = 0; i < 10; i++) hex.Append(bytes[i].ToString("x2"));
                    digest = hex.ToString();
                }
                return SignatureTag + ":" + digest + ":" +
                       string.Join(",", lengths.Select(l => l.ToString("G10", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// Whether two signatures (<see cref="Signature"/>) say one geometry: their exact
        /// parts equal, every length within <see cref="SignatureTolerance"/> of the larger extent.
        /// </summary>
        internal static bool Matches(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string[] pa = a.Split(':'), pb = b.Split(':');
            if (pa.Length != 3 || pb.Length != 3 || pa[0] != SignatureTag || pa[0] != pb[0] || pa[1] != pb[1]) return false;
            string[] la = pa[2].Split(','), lb = pb[2].Split(',');
            if (la.Length != lb.Length || la.Length == 0) return false;
            var va = new double[la.Length];
            var vb = new double[lb.Length];
            for (int i = 0; i < la.Length; i++)
                if (!double.TryParse(la[i], NumberStyles.Float, CultureInfo.InvariantCulture, out va[i]) ||
                    !double.TryParse(lb[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vb[i]))
                    return false;
            double tolerance = SignatureTolerance * Math.Max(1e-6, Math.Max(va[0], vb[0]));
            for (int i = 0; i < va.Length; i++)
                if (!(Math.Abs(va[i] - vb[i]) <= tolerance)) return false;
            return true;
        }

        /// <summary>
        /// The ground's height under the contact in the map's frame (the bake's
        /// <c>ground</c>), the contact's height in the space taken through the local
        /// transforms from the space down where the space holds it, so its placement adds
        /// no float noise.
        /// </summary>
        static double GroundInFrame(ShadowPlane plane, Vector3 axisUp, float sUp)
        {
            Matrix4x4 spaceToWorld = plane.SpaceToWorld();
            double contactUp;
            if (plane.space != null && plane.contact.IsChildOf(plane.space))
            {
                double[] c = Relative(plane.contact, plane.space);
                Matrix4x4 rest = plane.SpaceRest;
                contactUp = rest.m10 * c[3] + rest.m11 * c[7] + rest.m12 * c[11] + rest.m13;
            }
            else
            {
                contactUp = spaceToWorld.inverse.MultiplyPoint3x4(plane.contact.position).y;
            }
            double along = Vector3.Dot(spaceToWorld.MultiplyVector(Vector3.up), axisUp);
            return (plane.ground - contactUp) * along / sUp;
        }

        /// <summary>The lowest transform both <paramref name="a"/> and <paramref name="b"/> sit under (or are); null: the world.</summary>
        static Transform CommonAncestor(Transform a, Transform b)
        {
            for (Transform t = a; t != null; t = t.parent)
                if (b.IsChildOf(t)) return t;
            return null;
        }

        /// <summary>
        /// The affine map from <paramref name="from"/>'s local frame to <paramref name="to"/>'s
        /// (an ancestor; null: the world), row-major 3x4 in double, composed from each local
        /// position, rotation and scale on the way: nothing above <paramref name="to"/> enters.
        /// </summary>
        static double[] Relative(Transform from, Transform to)
        {
            double[] m = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
            for (Transform t = from; t != null && t != to; t = t.parent)
            {
                Matrix4x4 l = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
                var n = new double[12];
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 4; c++)
                        n[4 * r + c] = l[r, 0] * m[c] + l[r, 1] * m[4 + c] + l[r, 2] * m[8 + c] + (c == 3 ? l[r, 3] : 0.0);
                m = n;
            }
            return m;
        }

        /// <summary>A frame axis read in a frame whose linear map to world is <paramref name="toWorld"/>'s: L^T * axis / s.</summary>
        static double[] Row(Matrix4x4 toWorld, Vector3 axis, float s)
        {
            var row = new double[3];
            for (int j = 0; j < 3; j++)
                row[j] = ((double)toWorld[0, j] * axis.x + (double)toWorld[1, j] * axis.y + (double)toWorld[2, j] * axis.z) / s;
            return row;
        }

        // -- the frame meshes -------------------------------------------------------------

        /// <summary>
        /// The plane's target's meshes in the map's frame (<see cref="ShadowPlane.HorizonAxes"/>):
        /// each point as (along Frame A, up, along Frame B) from the contact, in the space's
        /// units, and the ground's height there.
        /// </summary>
        internal static List<(double[] points, int[] triangles)> FrameMeshes(ShadowPlane plane, out double ground)
        {
            using (Gathered meshes = Gather(plane.target))
                return FrameMeshes(plane, meshes, out ground);
        }

        static List<(double[] points, int[] triangles)> FrameMeshes(ShadowPlane plane, Gathered gathered, out double ground)
        {
            plane.HorizonAxes(out Vector3 origin, out Vector3 axisA, out Vector3 axisB, out Vector3 axisUp,
                out float groundInFrame, out float sA, out float sB, out float sUp);
            ground = groundInFrame / sUp;
            var meshes = new List<(double[], int[])>();
            foreach (Part part in gathered.Parts)
            {
                Mesh mesh = part.Mesh;
                Matrix4x4 toWorld = part.Transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                var points = new double[vertices.Length * 3];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 d = toWorld.MultiplyPoint3x4(vertices[i]) - origin;
                    points[3 * i] = Vector3.Dot(d, axisA) / sA;
                    points[3 * i + 1] = Vector3.Dot(d, axisUp) / sUp;
                    points[3 * i + 2] = Vector3.Dot(d, axisB) / sB;
                }
                meshes.Add((points, mesh.triangles));
            }
            return meshes;
        }

        /// <summary>
        /// The map's image in a <c>Texture2D</c>'s row order, from
        /// <see cref="HeightFieldMap.ToRgba"/>'s (top row first): its rows reversed.
        /// </summary>
        internal static byte[] BottomRowFirst(byte[] topRowFirst, int width, int height)
        {
            var flipped = new byte[topRowFirst.Length];
            int row = width * 4;
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(topRowFirst, y * row, flipped, (height - 1 - y) * row, row);
            return flipped;
        }

        // -- helpers ------------------------------------------------------------------

        static string BakeAction(string plane, string target, int size, int spans)
            => $"bake '{plane}''s horizon map from '{target}' ({size} px, {spans} span(s))";

        // The bake itself, after the asks: the map written and imported as data, the plane filled.
        static void Bake(ShadowPlane plane, Gathered gathered, int size, int spans, string path)
        {
            var meshes = FrameMeshes(plane, gathered, out double ground);
            HeightFieldMap map = ShadowHorizonBaker.Bake(meshes, ground, 1, size, spans);
            byte[] rgba = BottomRowFirst(map.ToRgba(), map.Width, map.Height);
            var texture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false, true);
            byte[] png;
            try
            {
                texture.SetPixelData(rgba, 0);
                texture.Apply(false, false);
                png = texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetFolders.Ensure(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && ShadowPlaneImporter.PinHorizon(ti, true))
                ti.SaveAndReimport();
            plane.horizonMap = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            // Written over in place, the map keeps its Texture2D: its cached march must go.
            plane.ForgetMap();
            plane.horizonSize = map.Size;
            plane.horizonSpans = map.Spans;
            plane.horizonBounds = new Vector4((float)map.A0, (float)map.A1, (float)map.B0, (float)map.B1);
            plane.horizonHeightScale = (float)map.HeightScale;
            plane.horizonRect = new Vector4(1f, 1f, 0f, 0f);
            // The size and spans the baker gave, which the plane now holds.
            plane.horizonHash = Signature(plane, gathered, map.Size, map.Spans) ?? "";
        }

        // Whether the plane's map was baked from its target as it is now, at that size.
        static bool Current(ShadowPlane plane, Gathered meshes, int size, int spans)
        {
            if (plane.horizonMap == null || string.IsNullOrEmpty(plane.horizonHash)) return false;
            if (plane.horizonSize != size || plane.horizonSpans != spans) return false;
            return Matches(plane.horizonHash, Signature(plane, meshes, size, spans));
        }

        static bool CanBake(ShadowPlane plane, OpContext ctx)
        {
            if (plane.target == null)
            {
                ctx.Raise(NoTargetCode, $"'{plane.name}' has no target to bake its horizon map from: set its Target to the " +
                                        "object whose meshes the shadow is of.", location: plane.name);
                return false;
            }
            if (plane.contact == null)
            {
                ctx.Raise(NoContactCode, $"'{plane.name}' has no contact, the frame its map is baked in: set its Contact " +
                                         "(the footprint's centre under the target).", location: plane.name);
                return false;
            }
            return true;
        }

        /// <summary>One mesh a shadow is of: a mesh asset's, or a skinned mesh baked in its pose.</summary>
        internal struct Part
        {
            public Mesh Mesh;
            public Transform Transform;
            /// <summary>A pose this gather baked (<c>SkinnedMeshRenderer.BakeMesh</c>), destroyed with it.</summary>
            public bool Baked;
        }

        /// <summary>
        /// The meshes under a target a shadow is of (<see cref="Gather"/>), and those left
        /// out, by name and why. Disposing it destroys the poses it baked.
        /// </summary>
        internal sealed class Gathered : IDisposable
        {
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<string> Skipped = new List<string>();

            /// <summary>The left-out meshes as a clause (" (n left out: ...)"), or "" with none.</summary>
            public string LeftOut() => Skipped.Count == 0 ? "" : $" ({Skipped.Count} left out: {string.Join(", ", Skipped)})";

            public void Dispose()
            {
                foreach (Part part in Parts)
                    if (part.Baked && part.Mesh != null) UnityEngine.Object.DestroyImmediate(part.Mesh);
                Parts.Clear();
            }
        }

        /// <summary>
        /// The meshes under <paramref name="root"/> that draw, and so cast: each
        /// <c>MeshRenderer</c>'s mesh, and each <c>SkinnedMeshRenderer</c>'s baked in its
        /// current pose; of a <c>LODGroup</c>, LOD 0's alone. Left out, by name: a mesh on an
        /// inactive object, one with no renderer (a collider's proxy) or a disabled one, and
        /// a later LOD's. Never a shadow plane's own.
        /// </summary>
        internal static Gathered Gather(Transform root)
        {
            var gathered = new Gathered();
            var later = new HashSet<Renderer>();
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length == 0) continue;
                var first = new HashSet<Renderer>(lods[0].renderers.Where(r => r != null));
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null && !first.Contains(r)) later.Add(r);
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<ShadowPlane>() != null) continue;
                var skinned = t.GetComponent<SkinnedMeshRenderer>();
                Renderer renderer = skinned;
                Mesh mesh;
                if (skinned != null)
                {
                    mesh = skinned.sharedMesh;
                }
                else
                {
                    var filter = t.GetComponent<MeshFilter>();
                    if (filter == null) continue;
                    mesh = filter.sharedMesh;
                    renderer = t.GetComponent<MeshRenderer>();
                }
                if (mesh == null || mesh.vertexCount < 3) continue;
                string why = !ActiveUnder(t, root) ? "inactive"
                    : renderer == null ? "no renderer"
                    : !renderer.enabled ? "its renderer is off"
                    : later.Contains(renderer) ? "a later LOD"
                    : null;
                if (why != null)
                {
                    gathered.Skipped.Add($"'{t.name}' ({why})");
                    continue;
                }
                if (skinned != null)
                {
                    var pose = new Mesh { name = mesh.name + " (pose)", hideFlags = HideFlags.HideAndDontSave };
                    skinned.BakeMesh(pose, true);
                    gathered.Parts.Add(new Part { Mesh = pose, Transform = t, Baked = true });
                }
                else
                {
                    gathered.Parts.Add(new Part { Mesh = mesh, Transform = t });
                }
            }
            return gathered;
        }

        // Active from root down to t: activeInHierarchy, read the same in a scene, a prefab
        // asset and an import (where no object is in a hierarchy, so none reads active).
        static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
                if (t == root) break;
            }
            return true;
        }

        // The meshes a gather left out, said once (info): they cast no shadow.
        static void ReportSkipped(Gathered meshes, string target, OpContext ctx)
        {
            if (meshes.Skipped.Count == 0) return;
            ctx.Raise(SkippedCode, $"'{target}': {meshes.Skipped.Count} mesh(es) draw nothing, so they cast no shadow and are " +
                                   $"left out of its map: {string.Join(", ", meshes.Skipped)}.", "info", target);
        }

        static Bounds WorldBounds(Gathered meshes)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (Part part in meshes.Parts)
            {
                Matrix4x4 toWorld = part.Transform.localToWorldMatrix;
                foreach (Vector3 v in part.Mesh.vertices)
                {
                    Vector3 p = toWorld.MultiplyPoint3x4(v);
                    if (!any) bounds = new Bounds(p, Vector3.zero);
                    else bounds.Encapsulate(p);
                    any = true;
                }
            }
            return bounds;
        }

        /// <summary>A unit quad lying in XZ, normal +Y, its UVs the unit square.</summary>
        static Mesh Quad()
        {
            var mesh = new Mesh
            {
                name = "ShadowPlaneQuad",
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // The planes' shared material at path, written there the first time (already asked).
        static Material SharedMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            AssetFolders.Ensure(Path.GetDirectoryName(path));
            material = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { name = "ShadowPlane" };
            ShadowPlaneImporter.ConfigureShadowPlane(material, material.shader, null);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Where <paramref name="go"/>'s maps go: <c>&lt;scene&gt;_ShadowPlanes/</c> beside its
        /// scene; for an unsaved scene, beside the model it is an instance of, else under Assets.
        /// </summary>
        static string FolderFor(GameObject go)
        {
            Scene scene = go.scene;
            string dir, name;
            if (!string.IsNullOrEmpty(scene.path))
            {
                dir = Path.GetDirectoryName(scene.path).Replace('\\', '/');
                name = scene.name;
            }
            else
            {
                string model = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                dir = string.IsNullOrEmpty(model) ? "Assets" : Path.GetDirectoryName(model).Replace('\\', '/');
                name = string.IsNullOrEmpty(model) ? "Untitled" : Path.GetFileNameWithoutExtension(model);
            }
            return $"{dir}/{AssetFolders.SafeName(name)}_ShadowPlanes";
        }

        // The map's path: the plane's own where this action wrote it there before and no other
        // plane in the open scenes holds it (a duplicate's: its sharer), else a free name in
        // folder ending as the importer's pin expects.
        static string MapPathFor(string folder, ShadowPlane plane, out ShadowPlane sharer)
        {
            sharer = null;
            Texture2D current = plane.horizonMap;
            string was = current != null ? AssetDatabase.GetAssetPath(current) : null;
            if (!string.IsNullOrEmpty(was) && was.StartsWith(folder + "/", StringComparison.Ordinal))
            {
                sharer = OpenPlanes().FirstOrDefault(p => p != plane && p.horizonMap == current);
                if (sharer == null) return was;
            }
            return MapPathFor(folder, plane.name);
        }

        // A free map path in folder for a plane of that name.
        static string MapPathFor(string folder, string plane)
        {
            string path = $"{folder}/{AssetFolders.SafeName(plane)}{ShadowPlaneImporter.HorizonStemMarker}.png";
            if (!File.Exists(path)) return path;
            for (int i = 1; ; i++)
            {
                string next = $"{folder}/{AssetFolders.SafeName(plane)}_{i}{ShadowPlaneImporter.HorizonStemMarker}.png";
                if (!File.Exists(next)) return next;
            }
        }


        /// <summary>Every shadow plane in the open scenes, inactive ones included.</summary>
        static List<ShadowPlane> OpenPlanes()
            => UnityEngine.Object.FindObjectsByType<ShadowPlane>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .Where(p => p.gameObject.scene.IsValid() && p.gameObject.scene.isLoaded
                                      && !EditorSceneManager.IsPreviewScene(p.gameObject.scene))
                          .OrderBy(PathOf, StringComparer.Ordinal).ToList();

        // A plane's scene and hierarchy path: the order a run visits them in, the same every run.
        static string PathOf(ShadowPlane plane)
        {
            var names = new List<string>();
            for (Transform t = plane.transform; t != null; t = t.parent) names.Insert(0, t.name);
            return plane.gameObject.scene.path + ":" + string.Join("/", names);
        }

        static void InScene(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            if (!go.scene.IsValid() || EditorUtility.IsPersistent(go))
                throw new ArgumentException($"'{go.name}' is not in a scene: a shadow plane is baked in the scene it shadows, " +
                                            "so place the prefab in a scene (or open it in Prefab Mode) and run it there", nameof(go));
        }
    }
}
