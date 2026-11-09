using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.ShadowPlane.Tests
{
    /// <summary>
    /// The ShadowPlane actions held to the kernel's checks: Create puts a plane, its
    /// contact and its baked map under a target (its dry run names every change and makes
    /// none, one undo takes the scene edits back, a second run plans nothing); BakeHorizon
    /// writes the C# baker's bytes, imported as data, and the shader's march of that map
    /// agrees with the baker's oracle (<see cref="HeightFieldMap.Alpha"/>) where a graphics
    /// device draws it; RebakeStale rebakes only the planes whose target changed.
    /// </summary>
    public class ShadowPlaneActionsTests
    {
        const string Folder = "Assets/UnitytkShadowPlaneActionsTests";
        const string ScenePath = Folder + "/Scene.unity";
        const string Maps = Folder + "/Scene_ShadowPlanes";

        readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
        }

        [TearDown]
        public void TearDown()
        {
            ShadowPlanePreview.ReleaseAll();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (Object o in _made) Object.DestroyImmediate(o);
            _made.Clear();
            AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>A box of <paramref name="size"/> standing on the ground at <paramref name="at"/> (its base centre).</summary>
        GameObject Box(string name, Vector3 at, Vector3 size, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = at + Vector3.up * (0.5f * size.y);
            go.transform.localScale = size;
            return go;
        }

        /// <summary>A target holding a box, its contact at the world origin unrotated, and a plane over them.</summary>
        ShadowPlane Rig(Light source, out GameObject box)
        {
            var target = new GameObject("Crate");
            box = Box("Crate_mesh", Vector3.zero, new Vector3(0.6f, 1f, 0.4f), target.transform);
            var contact = new GameObject("Crate_contact").transform;
            contact.SetParent(target.transform, false);
            var go = new GameObject("Crate_shadow");
            go.transform.SetParent(target.transform, false);
            var quad = new Mesh
            {
                vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            quad.RecalculateBounds();
            _made.Add(quad);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var material = new Material(Shader.Find(ShadowPlaneImporter.ShaderName));
            _made.Add(material);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var plane = go.AddComponent<ShadowPlane>();
            plane.target = target.transform;
            plane.contact = contact;
            plane.source = source;
            plane.radius = 0.4f;
            plane.height = 1f;
            plane.horizonSize = 64;
            return plane;
        }

        static Light Lamp(Vector3 at)
        {
            var light = new GameObject("Lamp").AddComponent<Light>();
            light.type = LightType.Point;
            light.transform.position = at;
            return light;
        }

        static Scene Active => SceneManager.GetActiveScene();

        /// <summary>The cube's triangles in world space: the frame, at a contact on the origin.</summary>
        static List<(double[] points, int[] triangles)> World(GameObject box)
        {
            Mesh mesh = box.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 m = box.transform.localToWorldMatrix;
            var points = mesh.vertices.SelectMany(v => { Vector3 p = m.MultiplyPoint3x4(v); return new double[] { p.x, p.y, p.z }; }).ToArray();
            return new List<(double[], int[])> { (points, mesh.triangles) };
        }

        static byte[] TopRowFirst(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int w = texture.width, h = texture.height;
            var bytes = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c = pixels[(h - 1 - y) * w + x];
                    int i = (y * w + x) * 4;
                    bytes[i] = c.r;
                    bytes[i + 1] = c.g;
                    bytes[i + 2] = c.b;
                    bytes[i + 3] = c.a;
                }
            return bytes;
        }

        [Test]
        public void BakeHorizon_writes_the_bakers_bytes_imported_as_data()
        {
            ShadowPlane plane = Rig(Lamp(new Vector3(2f, 3f, 1f)), out GameObject box);
            Assert.That(ShadowPlaneActions.BakeHorizon(plane), Is.True);

            HeightFieldMap want = ShadowHorizonBaker.Bake(World(box), 0.0, 1, 64, 2);
            string path = AssetDatabase.GetAssetPath(plane.horizonMap);
            Assert.That(path, Is.EqualTo(Maps + "/Crate_shadow_horizon.png"), "beside the scene");
            var read = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            _made.Add(read);
            Assert.That(read.LoadImage(File.ReadAllBytes(path)), Is.True);
            Assert.That((read.width, read.height), Is.EqualTo((want.Width, want.Height)));
            Assert.That(TopRowFirst(read), Is.EqualTo(want.ToRgba()), "the PNG holds the baker's image, byte for byte");

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(ti.sRGBTexture, Is.False, "data: linear");
            Assert.That(ti.mipmapEnabled, Is.False);
            Assert.That(ti.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(ti.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(ShadowPlaneImporter.PinHorizon(ti, false), Is.False, "pinned as the importer pins a horizon map");

            Assert.That((plane.horizonSize, plane.horizonSpans), Is.EqualTo((want.Size, want.Spans)));
            Assert.That(plane.horizonBounds, Is.EqualTo(new Vector4((float)want.A0, (float)want.A1, (float)want.B0, (float)want.B1)));
            Assert.That(plane.horizonHeightScale, Is.EqualTo((float)want.HeightScale));
            Assert.That(plane.horizonHash, Is.Not.Empty, "its target's hash, stamped");
            Assert.That(plane.CanMarch, Is.True);

            var again = new OpContext(dryRun: true);
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: again), Is.False);
            Assert.That(again.Changes, Is.Empty, "an unchanged target: a second run plans nothing");
        }

        [Test]
        public void The_shaders_march_agrees_with_the_bakers_oracle()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8))
                Assert.Inconclusive("no graphics device draws here (-nographics): nothing to measure");
            Light lamp = Lamp(new Vector3(1.5f, 2f, -1f));
            ShadowPlane plane = Rig(lamp, out GameObject box);
            plane.sourceSize = 0.3f;
            ShadowPlaneActions.BakeHorizon(plane);
            plane.cacheShadow = true;
            plane.ApplyNow();  // placed, and its march drawn into the cache
            Assert.That(plane.cacheSlot, Is.GreaterThanOrEqualTo(0), "the plane holds a cache slot");
            RenderTexture atlas = ShadowPlaneCache.Atlas;
            var block = new MaterialPropertyBlock();
            plane.GetComponent<Renderer>().GetPropertyBlock(block);
            Vector4 rect = block.GetVector("_CacheRect");

            var request = AsyncGPUReadback.Request(atlas, 0, TextureFormat.R8);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False, "the atlas read back");
            NativeArray<byte> data = request.GetData<byte>();

            HeightFieldMap oracle = ShadowHorizonBaker.Bake(World(box), 0.0, 1, plane.horizonSize, plane.horizonSpans);
            int w = atlas.width, h = atlas.height;
            int x0 = Mathf.CeilToInt(rect.x * w), x1 = Mathf.FloorToInt((rect.x + rect.z) * w);
            int y0 = Mathf.CeilToInt(rect.y * h), y1 = Mathf.FloorToInt((rect.y + rect.w) * h);
            var points = new List<double>();
            var drawn = new List<float>();
            for (int y = y0 + 1; y < y1 - 1; y++)
                for (int x = x0 + 1; x < x1 - 1; x++)
                {
                    float u = ((x + 0.5f) / w - rect.x) / rect.z, v = ((y + 0.5f) / h - rect.y) / rect.w;
                    Vector3 p = plane.transform.TransformPoint(new Vector3(u - 0.5f, 0f, v - 0.5f));
                    points.AddRange(new double[] { p.x, p.y, p.z });
                    drawn.Add(data[y * w + x] / 255f);
                }
            Vector3 l = lamp.transform.position;
            float[] want = oracle.Alpha(points.ToArray(), l.x, l.y, l.z, plane.sourceSize);
            Assert.That(want.Count(a => a > 0.5f), Is.GreaterThan(20), "the oracle sees a shadow there");
            var errors = drawn.Select((a, i) => Math.Abs(a - want[i])).OrderBy(e => e).ToArray();
            float mean = errors.Average(), p95 = errors[(int)(0.95 * (errors.Length - 1))];
            Assert.That(mean, Is.LessThan(0.03f), $"mean texel error {mean}");
            Assert.That(p95, Is.LessThan(0.15f), $"95th-percentile texel error {p95} (edges sit between texels)");
        }

        [Test]
        public void A_script_reload_frees_the_cache_atlas_and_the_next_caching_plane_makes_one()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8))
                Assert.Inconclusive("no graphics device draws here (-nographics): no atlas is made");
            ShadowPlane plane = Rig(Lamp(new Vector3(1.5f, 2f, -1f)), out _);
            ShadowPlaneActions.BakeHorizon(plane);
            plane.cacheShadow = true;
            plane.ApplyNow();
            RenderTexture atlas = ShadowPlaneCache.Atlas;
            Assert.That(atlas != null && plane.cacheSlot >= 0, Is.True, "the plane caches into the atlas");

            ShadowPlanePreview.BeforeReload();  // what the editor runs before scripts reload
            Assert.That(atlas == null, Is.True, "freed: a reload empties the static, and an atlas left alive is orphaned");
            Assert.That(ShadowPlaneCache.Atlas == null, Is.True);
            Assert.That(plane.cacheSlot, Is.EqualTo(-1), "its slot given up");

            plane.ApplyNow();
            Assert.That(ShadowPlaneCache.Atlas != null && plane.cacheSlot >= 0, Is.True, "the next caching plane makes an atlas again");
        }

        [Test]
        public void Create_puts_a_plane_under_its_target_its_horizon_baked()
        {
            var target = new GameObject("Crate");
            Box("Crate_mesh", new Vector3(1f, 0f, 2f), new Vector3(0.6f, 1f, 0.8f), target.transform);
            EditorSceneManager.SaveScene(Active);

            var plan = new OpContext(dryRun: true);
            Assert.That(ShadowPlaneActions.Create(target, null, plan), Is.Null);
            Assert.That(target.GetComponentInChildren<ShadowPlane>() == null, "a dry run makes nothing");
            Assert.That(AssetDatabase.IsValidFolder(Maps), Is.False, "...and writes nothing");
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "create", "write", "write" }));

            var live = new OpContext();
            Undo.IncrementCurrentGroup();
            ShadowPlane plane = ShadowPlaneActions.Create(target, null, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))),
                        "the plan named every change the run made");
            Assert.That(plane, Is.Not.Null);
            Assert.That(plane.transform.parent, Is.SameAs(target.transform), "under its target");
            Assert.That(plane.target, Is.SameAs(target.transform));
            Assert.That(plane.contact.position, Is.EqualTo(new Vector3(1f, 0f, 2f)), "the footprint's centre, underneath");
            Assert.That(plane.radius, Is.EqualTo(0.5f).Within(1e-5f), "half the footprint's diagonal");
            Assert.That(plane.height, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(plane.ground, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(plane.source == null, "the scene's sun");
            double[] canvas = ShadowPlaneModel.DefaultCanvas;
            Assert.That(plane.canvas, Is.EqualTo(new Vector4((float)canvas[0], (float)canvas[1], (float)canvas[2], (float)canvas[3])),
                        "placed on the default canvas");
            Assert.That(plane.sourceRamp, Is.EqualTo(1f), "faded in over a metre of the source's height");
            Assert.That(plane.CanMarch, Is.True, "its horizon baked");
            Assert.That(AssetDatabase.GetAssetPath(plane.horizonMap), Does.StartWith(Maps + "/"));
            Assert.That(plane.GetComponent<Renderer>().sharedMaterial.shader.name, Is.EqualTo(ShadowPlaneImporter.ShaderName));

            var again = new OpContext(dryRun: true);
            Assert.That(ShadowPlaneActions.Create(target, null, again), Is.SameAs(plane), "its plane, already there");
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");

            Undo.PerformUndo();
            Assert.That(target.GetComponentInChildren<ShadowPlane>() == null, "one undo takes the plane back");
            Assert.That(target.transform.Find("Crate_contact") == null, "...and its contact");
        }

        [Test]
        public void RebakeStale_rebakes_only_the_planes_whose_target_changed()
        {
            ShadowPlane still = Rig(Lamp(new Vector3(2f, 3f, 1f)), out _);
            ShadowPlane moved = Rig(Lamp(new Vector3(-2f, 3f, 1f)), out GameObject box);
            moved.name = "Crate_shadow_2";
            still.transform.parent.position = new Vector3(5f, 0f, 0f);
            ShadowPlaneActions.BakeHorizon(still);
            ShadowPlaneActions.BakeHorizon(moved);
            string stillHash = still.horizonHash, movedHash = moved.horizonHash;
            Texture2D stillMap = still.horizonMap;

            Assert.That(ShadowPlaneActions.RebakeStale(new OpContext(dryRun: true)), Is.EqualTo(0), "nothing changed: nothing to rebake");
            box.transform.localScale = new Vector3(0.6f, 1.5f, 0.4f);  // the target grew
            still.transform.parent.position += Vector3.right;           // moving a whole rig changes nothing

            var plan = new OpContext(dryRun: true);
            Assert.That(ShadowPlaneActions.RebakeStale(plan), Is.EqualTo(1));
            Assert.That(plan.Changes.Where(c => c.kind == "write").Select(c => c.target),
                        Is.EqualTo(new[] { AssetDatabase.GetAssetPath(moved.horizonMap) }), "the changed target's map alone, written over");
            Assert.That(ShadowPlaneActions.RebakeStale(), Is.EqualTo(1));
            Assert.That(moved.horizonHash, Is.Not.EqualTo(movedHash), "rebaked");
            Assert.That(moved.horizonHeightScale, Is.GreaterThan(1.2f), "from the taller target");
            Assert.That(still.horizonHash, Is.EqualTo(stillHash), "the unchanged one left alone");
            Assert.That(still.horizonMap, Is.SameAs(stillMap));
            Assert.That(ShadowPlaneActions.RebakeStale(new OpContext(dryRun: true)), Is.EqualTo(0), "a second run plans nothing");
        }

        /// <summary>
        /// A dune of <paramref name="side"/> + 1 squared points (a 1.2 m square, up to 1.2 m tall),
        /// its heights random: points that sit anywhere against a millimetre grid.
        /// </summary>
        Mesh Dune(int side, int seed)
        {
            var random = new System.Random(seed);
            var vertices = new Vector3[(side + 1) * (side + 1)];
            for (int z = 0, i = 0; z <= side; z++)
                for (int x = 0; x <= side; x++, i++)
                    vertices[i] = new Vector3(1.2f * x / side - 0.6f, 0.2f + (float)random.NextDouble(), 1.2f * z / side - 0.6f);
            var triangles = new List<int>();
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int i = z * (side + 1) + x;
                    triangles.AddRange(new[] { i, i + side + 1, i + 1, i + 1, i + side + 1, i + side + 2 });
                }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32, vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateBounds();
            _made.Add(mesh);
            return mesh;
        }

        static OpContext Plan => new OpContext(dryRun: true);

        [Test]
        public void A_large_mesh_stays_current_at_a_moved_turned_and_scaled_instance()
        {
            var prop = new GameObject("Prop");
            var target = new GameObject("Dune");
            target.transform.SetParent(prop.transform, false);
            var surface = new GameObject("Dune_mesh");
            surface.transform.SetParent(target.transform, false);
            surface.transform.localPosition = new Vector3(0.013f, 0f, -0.007f);
            Mesh mesh = Dune(140, 7);
            Assert.That(mesh.vertexCount, Is.GreaterThan(19000));
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>();
            ShadowPlane plane = ShadowPlaneActions.Create(target);
            Assert.That(plane, Is.Not.Null);

            var placements = new[]
            {
                (new Vector3(12.3f, 0f, 7.9f), 0f, 1f),
                (new Vector3(12.3f, 0f, 7.9f), 35f, 1f),
                (new Vector3(153.7f, 0f, -88.2f), 0f, 1f),
                (new Vector3(-410.2f, 3.1f, 977.6f), 121f, 1f),
                (new Vector3(5.5f, -2f, 1.25f), 270f, 1.5f),
            };
            foreach (var (at, yaw, scale) in placements)
            {
                prop.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
                prop.transform.localScale = Vector3.one * scale;
                Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: Plan), Is.False,
                            $"placed at {at}, turned {yaw} degrees, scaled {scale}: the map is current");
                Assert.That(ShadowPlaneActions.RebakeStale(Plan), Is.Zero, $"placed at {at}: nothing to rebake");
            }

            Vector3[] points = mesh.vertices;
            points[points.Length / 2] += Vector3.up * 0.01f;
            mesh.vertices = points;
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: Plan), Is.True, "a point raised a centimetre: stale");
        }

        [Test]
        public void A_rebake_of_a_duplicated_plane_writes_its_own_map_and_leaves_the_shared_one()
        {
            var target = new GameObject("Crate");
            Box("Crate_mesh", Vector3.zero, new Vector3(0.6f, 1f, 0.4f), target.transform);
            ShadowPlane original = ShadowPlaneActions.Create(target);
            string shared = AssetDatabase.GetAssetPath(original.horizonMap);
            byte[] bytes = File.ReadAllBytes(shared);

            GameObject twin = Object.Instantiate(target);  // Ctrl+D
            twin.name = "Crate (1)";
            ShadowPlane copy = twin.GetComponentInChildren<ShadowPlane>();
            Assert.That(copy.horizonMap, Is.SameAs(original.horizonMap), "the copy shares the original's map");
            Assert.That(copy.target, Is.SameAs(twin.transform));
            twin.transform.Find("Crate_mesh").localScale = new Vector3(0.6f, 1.6f, 0.4f);

            OpContext plan = Plan;
            Assert.That(ShadowPlaneActions.BakeHorizon(copy, ctx: plan), Is.True);
            Change write = plan.Changes.Single(c => c.kind == "write");
            Assert.That(write.target, Is.Not.EqualTo(shared), "a map another plane holds is never written over");
            Assert.That(write.action, Does.Contain("shared"), "...and the plan says why it is a new file");
            Assert.That(ShadowPlaneActions.BakeHorizon(copy), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(copy.horizonMap), Is.EqualTo(write.target));
            Assert.That(File.ReadAllBytes(shared), Is.EqualTo(bytes), "the original's map is as it was");
            Assert.That(ShadowPlaneActions.BakeHorizon(original, ctx: Plan), Is.False, "...and current for the original");
            Assert.That(copy.GetComponent<Renderer>().sharedMaterial, Is.SameAs(original.GetComponent<Renderer>().sharedMaterial),
                        "two planes, one shared material");
        }

        [Test]
        public void A_skinned_target_bakes_in_its_current_pose()
        {
            var target = new GameObject("Figure");
            Mesh cube = Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
            _made.Add(cube);
            var skin = new GameObject("Figure_skin");
            skin.transform.SetParent(target.transform, false);
            skin.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            Transform bone = new GameObject("Figure_bone").transform;
            bone.SetParent(target.transform, false);
            cube.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, cube.vertexCount).ToArray();
            cube.bindposes = new[] { bone.worldToLocalMatrix * skin.transform.localToWorldMatrix };
            var smr = skin.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = cube;
            smr.bones = new[] { bone };
            smr.rootBone = bone;
            bone.localPosition = new Vector3(0f, 1f, 0f);  // posed: lifted a metre, from 1 to 2 m

            ShadowPlane plane = ShadowPlaneActions.Create(target);
            Assert.That(plane, Is.Not.Null, "a rigged target casts a shadow");
            Assert.That(plane.contact.position.y, Is.EqualTo(1f).Within(1e-5f), "its footprint under the pose, not the bind pose");
            Assert.That(plane.height, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(plane.CanMarch, Is.True);
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: Plan), Is.False, "the pose unchanged: current");
            bone.localRotation = Quaternion.Euler(0f, 0f, 30f);
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: Plan), Is.True, "a new pose: stale");
        }

        [Test]
        public void Inactive_hidden_proxy_and_later_lod_meshes_cast_nothing_and_are_named()
        {
            var target = new GameObject("Crate");
            Box("Crate_mesh", Vector3.zero, new Vector3(0.6f, 1f, 0.4f), target.transform);
            Box("Crate_inactive", Vector3.zero, new Vector3(0.2f, 3f, 0.2f), target.transform).SetActive(false);
            Box("Crate_hidden", Vector3.zero, new Vector3(0.2f, 3f, 0.2f), target.transform).GetComponent<MeshRenderer>().enabled = false;
            Object.DestroyImmediate(Box("Crate_proxy", Vector3.zero, new Vector3(0.2f, 3f, 0.2f), target.transform).GetComponent<MeshRenderer>());
            Renderer near = Box("Crate_lod0", Vector3.zero, new Vector3(0.6f, 1f, 0.4f), target.transform).GetComponent<Renderer>();
            Renderer far = Box("Crate_lod1", Vector3.zero, new Vector3(0.2f, 3f, 0.2f), target.transform).GetComponent<Renderer>();
            target.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.5f, new[] { near }), new LOD(0.1f, new[] { far }) });

            var ctx = new OpContext();
            ShadowPlane plane = ShadowPlaneActions.Create(target, null, ctx);
            Assert.That(plane, Is.Not.Null);
            Assert.That(plane.height, Is.EqualTo(1f).Within(1e-5f), "only what draws casts: no 3 m proxy");
            Issue skipped = ctx.Issues.Single(i => i.code == ShadowPlaneActions.SkippedCode);
            Assert.That(skipped.message, Does.Contain("Crate_inactive").And.Contain("Crate_hidden").And.Contain("Crate_proxy")
                                             .And.Contain("Crate_lod1"));
            Assert.That(skipped.message, Does.Not.Contain("Crate_lod0").And.Not.Contain("Crate_mesh"));
        }

        [Test]
        public void A_moved_ground_reads_stale()
        {
            ShadowPlane plane = Rig(Lamp(new Vector3(2f, 3f, 1f)), out _);
            ShadowPlaneActions.BakeHorizon(plane);
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: Plan), Is.False);
            plane.ground = -0.2f;
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: Plan), Is.True, "the map's heights are above the ground: stale");
        }

        [Test]
        public void A_size_off_the_powers_of_two_stamps_the_size_it_baked()
        {
            ShadowPlane plane = Rig(Lamp(new Vector3(2f, 3f, 1f)), out _);
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, size: 100), Is.True);
            Assert.That(plane.horizonSize, Is.EqualTo(128), "the baker's size: a power of two");
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, size: 100, ctx: Plan), Is.False, "asked again at 100: current");
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: Plan), Is.False, "at its own size: current");
        }

        [Test]
        public void Forced_on_an_unchanged_target_it_bakes_over_its_own_map()
        {
            ShadowPlane plane = Rig(Lamp(new Vector3(2f, 3f, 1f)), out _);
            ShadowPlaneActions.BakeHorizon(plane);
            string path = AssetDatabase.GetAssetPath(plane.horizonMap);
            byte[] bytes = File.ReadAllBytes(path);
            OpContext plan = Plan;
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, force: true, ctx: plan), Is.True);
            Assert.That(plan.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(new[] { ("write", path), ("change", plane.name) }));
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, force: true), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(plane.horizonMap), Is.EqualTo(path), "its own map, written over");
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), "the same target: the same bytes");
        }

        [Test]
        public void RebakeStale_reports_each_plane_and_a_cancel_stops_it()
        {
            ShadowPlane a = Rig(Lamp(new Vector3(2f, 3f, 1f)), out GameObject boxA);
            ShadowPlane b = Rig(Lamp(new Vector3(-2f, 3f, 1f)), out GameObject boxB);
            b.name = "Crate_shadow_2";
            ShadowPlaneActions.BakeHorizon(a);
            ShadowPlaneActions.BakeHorizon(b);
            boxA.transform.localScale = new Vector3(0.6f, 1.5f, 0.4f);
            boxB.transform.localScale = new Vector3(0.6f, 1.5f, 0.4f);
            string hashA = a.horizonHash, hashB = b.horizonHash;

            var reports = new List<string>();
            bool cancel = false;
            var cancelled = new OpContext(cancelled: () => cancel, progress: (f, m) =>
            {
                reports.Add(m);
                cancel = true;  // the person pressed Cancel at the first report
            });
            Assert.Throws<OperationCancelledException>(() => ShadowPlaneActions.RebakeStale(cancelled));
            Assert.That(reports, Has.Count.EqualTo(1));
            Assert.That(reports[0], Does.Contain("Crate_shadow"), "the report names the plane");
            Assert.That((a.horizonHash, b.horizonHash), Is.EqualTo((hashA, hashB)), "cancelled before any bake");

            reports.Clear();
            var fractions = new List<double>();
            Assert.That(ShadowPlaneActions.RebakeStale(new OpContext(progress: (f, m) =>
            {
                fractions.Add(f);
                reports.Add(m);
            })), Is.EqualTo(2));
            Assert.That(reports.Count(r => r.Contains("Crate_shadow")), Is.GreaterThanOrEqualTo(2), "a report per plane");
            Assert.That(fractions, Is.Ordered, "rising");
        }

        [Test]
        public void A_prefab_saved_with_the_inspector_open_holds_the_plane_as_it_was()
        {
            ShadowPlane plane = Rig(Lamp(new Vector3(2f, 3f, 1f)), out _);
            var was = new Vector3(0.25f, 0f, -0.5f);
            plane.transform.localPosition = was;
            var editor = UnityEditor.Editor.CreateEditor(plane);
            try
            {
                Assert.That(ShadowPlanePreview.Showing(plane), Is.True);
                Assert.That(plane.transform.localPosition, Is.Not.EqualTo(was), "placed by the preview");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(plane.target.gameObject, Folder + "/Crate.prefab");
                Assert.That(prefab.GetComponentInChildren<ShadowPlane>().transform.localPosition, Is.EqualTo(was),
                            "the prefab holds the plane's own transform, never the preview's placement");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void The_preview_gives_the_placement_back_before_a_save()
        {
            ShadowPlane plane = Rig(Lamp(new Vector3(2f, 3f, 1f)), out _);
            var was = new Vector3(0.25f, 0f, -0.5f);
            plane.transform.localPosition = was;
            var editor = UnityEditor.Editor.CreateEditor(plane);
            try
            {
                Assert.That(plane.transform.localPosition, Is.Not.EqualTo(was), "placed by the preview");
                EditorSceneManager.SaveScene(Active);
                Assert.That(ShadowPlanePreview.Showing(plane), Is.False, "released as the scene saves");
                Assert.That(plane.transform.localPosition, Is.EqualTo(was), "the scene saved its own transform");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void A_rebake_over_the_same_file_marches_the_cache_again()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8))
                Assert.Inconclusive("no graphics device draws here (-nographics): no cache to march");
            // Two columns side by side: swapping their heights keeps the footprint and the
            // height scale (the cache key's terms), and changes the map.
            var target = new GameObject("Pair");
            GameObject tall = Box("Pair_tall", new Vector3(-0.25f, 0f, 0f), new Vector3(0.5f, 1f, 0.5f), target.transform);
            GameObject low = Box("Pair_low", new Vector3(0.25f, 0f, 0f), new Vector3(0.5f, 0.5f, 0.5f), target.transform);
            ShadowPlane plane = ShadowPlaneActions.Create(target, Lamp(new Vector3(2f, 3f, 1f)));
            plane.cacheShadow = true;
            string path = AssetDatabase.GetAssetPath(plane.horizonMap);
            Vector4 bounds = plane.horizonBounds;
            plane.ApplyNow();
            int marched = ShadowPlaneCache.Bakes;
            plane.ApplyNow();
            Assert.That(ShadowPlaneCache.Bakes, Is.EqualTo(marched), "nothing moved: the cache holds");

            tall.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            tall.transform.localPosition = new Vector3(-0.25f, 0.25f, 0f);
            low.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            low.transform.localPosition = new Vector3(0.25f, 0.5f, 0f);
            Assert.That(ShadowPlaneActions.BakeHorizon(plane), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(plane.horizonMap), Is.EqualTo(path), "over its own file");
            Assert.That(plane.horizonBounds, Is.EqualTo(bounds), "...at the same footprint");
            plane.ApplyNow();
            Assert.That(ShadowPlaneCache.Bakes, Is.GreaterThan(marched), "the new map is marched, not the old one's cache");
        }

        [Test]
        public void A_plane_with_no_target_is_refused_with_its_fix()
        {
            ShadowPlane plane = Rig(null, out _);
            plane.target = null;
            var ctx = new OpContext();
            Assert.That(ShadowPlaneActions.BakeHorizon(plane, ctx: ctx), Is.False);
            Assert.That(ctx.Issues.Single().code, Is.EqualTo(ShadowPlaneActions.NoTargetCode));
            Assert.That(ctx.Issues.Single().message, Does.Contain("set its Target"));
            Assert.That(ctx.Changes, Is.Empty);
        }
    }
}
