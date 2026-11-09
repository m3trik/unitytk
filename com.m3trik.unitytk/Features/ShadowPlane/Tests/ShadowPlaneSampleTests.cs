using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.ShadowPlane.Tests
{
    /// <summary>
    /// The ShadowPlane sample, imported as the Package Manager imports it: a box, a chair
    /// on four legs and an awning cantilevered past its post, on a ground, each with one
    /// shadow plane the DCC's Shadow Rig made -- the box's and the awning's cast by the
    /// point light <c>Lamp</c>, the chair's by the directional <c>Sun</c> -- and the
    /// lamp keyed to sweep over them. Every plane resolves its target, source Light and
    /// contact by reference and binds its delivered horizon map; placed and faded at a
    /// known light it is where <see cref="ShadowPlaneModel"/> puts it, at the fade the
    /// record's terms give; Bake Horizon in Unity (the ported baker) reproduces each
    /// delivered map from the imported geometry; and the demo recipe builds a scene whose
    /// planes draw. The project these run in imports every sample of the package, so a
    /// sample that is not there fails: it is never skipped.
    /// </summary>
    public class ShadowPlaneSampleTests
    {
        const string Package = "com.m3trik.unitytk";
        const string SampleName = "ShadowPlane";
        const string Recipe = "M3trik.Unitytk.Samples.ShadowPlane.ShadowPlaneDemoRecipe, M3trik.Unitytk.Samples.ShadowPlane.Editor";
        const string Scratch = "Assets/UnitytkShadowPlaneSampleTests";

        /// <summary>The record's unit: the recipe's scene is in centimetres.</summary>
        const double Unit = 0.01;

        /// <summary>The fade's source ramp, in metres: the rig's one unit (the record's 1).</summary>
        const double SourceRamp = 1.0 * Unit;

        /// <summary>One plane as the recipe's rig made it, its lengths the record's (centimetres).</summary>
        sealed class Want
        {
            public string plane, target, source, contact;
            public LightType type;
            public double radius, height, fadeHeight;
            /// <summary>The record's canvas, (u0, u1, w0, w1): the rig's, fitted to the silhouette it drew.</summary>
            public double[] canvas;
            /// <summary>
            /// The canvas in Unity's terms: the recipe's producer is Y-up, and its ground frame
            /// reaches Unity through the import's mirror alone, which swaps the sides
            /// (<c>(u0, u1, -w1, -w0)</c>).
            /// </summary>
            public Vector4 UnityCanvas => new Vector4((float)canvas[0], (float)canvas[1], (float)-canvas[3], (float)-canvas[2]);
            public string Map => plane.Substring(0, plane.Length - "_shadow".Length) + "_horizon";
        }

        /// <summary>
        /// Every plane, its nodes, and its terms as the record states them: ground 0,
        /// intensity 1, falloff power 1.2, a stretch capped at 6 heights, and a fade height
        /// of two target heights (the rig's defaults).
        /// </summary>
        static readonly Want[] Planes =
        {
            new Want { plane = "Box_Lamp_shadow", target = "Box", source = "Lamp", contact = "Box_Lamp_contact_loc",
                       type = LightType.Point, radius = 42.426407, height = 60.0, fadeHeight = 120.0,
                       canvas = new[] { -0.20659, 1.164003, -0.582001, 0.582001 } },
            new Want { plane = "Chair_Sun_shadow", target = "Chair", source = "Sun", contact = "Chair_Sun_contact_loc",
                       type = LightType.Directional, radius = 35.355339, height = 90.0, fadeHeight = 180.0,
                       canvas = new[] { -0.349778, 1.149028, -0.574515, 0.574515 } },
            new Want { plane = "Awning_Lamp_shadow", target = "Awning", source = "Lamp", contact = "Awning_Lamp_contact_loc",
                       type = LightType.Point, radius = 62.64982, height = 118.0, fadeHeight = 236.0,
                       canvas = new[] { -0.097411, 0.709269, -0.476083, 0.502088 } },
        };

        const double Intensity = 1.0, FalloffPower = 1.2, MaxStretch = 6.0, Ground = 0.0;
        const int MapSize = 128, MapSpans = 2;

        /// <summary>The sun's rotation in the DCC (degrees, rotated X, then Y, then Z): it shines along its -Z.</summary>
        static readonly Vector3 SunRotation = new Vector3(-50f, 35f, 0f);

        /// <summary>
        /// The ShadowHorizonConformance <c>texel</c> tolerance (pythontk): codes, a 16-bit pair
        /// read as one code. What a port holds to the reference on the reference's own input.
        /// </summary>
        const int TexelTolerance = 1;

        readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            ShadowPlanePreview.ReleaseAll();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (Object o in _made) Object.DestroyImmediate(o);
            _made.Clear();
            AssetDatabase.DeleteAsset(Scratch);
        }

        static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            Assert.That(sample.displayName, Is.EqualTo(SampleName),
                        "the package lists no ShadowPlane sample: its package.json's samples are written by the workspace's sample sync");
            Assert.That(sample.isImported, Is.True,
                        $"the ShadowPlane sample is not imported at {sample.importPath}: import it from the Package Manager's " +
                        "Samples tab (the owned project's builder copies every sample in)");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        static string Model() => Folder() + "/ShadowPlane.fbx";

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

        static Dictionary<string, ShadowPlane> ByName(GameObject root)
        {
            ShadowPlane[] planes = root.GetComponentsInChildren<ShadowPlane>(true);
            Assert.That(planes.Select(p => p.name), Is.EquivalentTo(Planes.Select(w => w.plane)), "one Shadow Plane per plane of the record");
            return planes.ToDictionary(p => p.name);
        }

        /// <summary>The sample's model placed in a new scene, at the origin, unrotated.</summary>
        static GameObject Placed()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(Prefab());
            Assert.That(root.transform.localToWorldMatrix, Is.EqualTo(Matrix4x4.identity), "the model's root at rest");
            return root;
        }

        [Test]
        public void Every_plane_resolves_its_target_source_and_contact_by_reference_and_binds_its_map()
        {
            GameObject root = Prefab();
            Dictionary<string, ShadowPlane> planes = ByName(root);
            foreach (Want want in Planes)
            {
                ShadowPlane plane = planes[want.plane];
                Assert.That(plane.target, Is.SameAs(Node(root, want.target)), $"{want.plane}: its target, the node its contact hangs on");
                Assert.That(plane.contact, Is.SameAs(Node(root, want.contact)), $"{want.plane}: its contact");
                Assert.That(plane.contact.parent, Is.SameAs(plane.target), $"{want.plane}: the contact under its target");
                Assert.That(plane.space, Is.SameAs(root.transform), $"{want.plane}: in the model's frame");

                Light source = plane.source;
                Assert.That(source, Is.Not.Null, $"{want.plane}: a source Light");
                Transform node = Node(root, want.source);
                Assert.That(source.transform == node || source.transform.parent == node, Is.True,
                            $"{want.plane}: the Light of {want.source} (its own, or the stand-in under it), not {source.name}");
                Assert.That(source.type, Is.EqualTo(want.type), $"{want.plane}: a {want.type} source");

                Texture2D map = plane.horizonMap;
                Assert.That(map, Is.Not.Null, $"{want.plane}: its delivered horizon map, bound");
                Assert.That(map.name, Is.EqualTo(want.Map), $"{want.plane}: the map its record names");
                string path = AssetDatabase.GetAssetPath(map);
                Assert.That(path, Does.StartWith(Folder() + "/"), $"{want.plane}: extracted from the FBX beside the model, not another model's");
                Assert.That(ShadowPlaneImporter.PinHorizon((TextureImporter)AssetImporter.GetAtPath(path), false), Is.False,
                            $"{path}: imported as data");
                Assert.That((plane.horizonSize, plane.horizonSpans), Is.EqualTo((MapSize, MapSpans)), $"{want.plane}: the map's layout");
                Assert.That(plane.CanMarch, Is.True, $"{want.plane}: a map the shader marches");
                Assert.That(plane.horizonHash, Is.Not.Empty, $"{want.plane}: the delivered geometry's hash, stamped");

                // The record's lengths, in metres.
                Assert.That(plane.radius, Is.EqualTo((float)(want.radius * Unit)).Within(1e-6f), $"{want.plane}: radius");
                Assert.That(plane.height, Is.EqualTo((float)(want.height * Unit)).Within(1e-6f), $"{want.plane}: height");
                Assert.That(plane.fadeHeight, Is.EqualTo((float)(want.fadeHeight * Unit)).Within(1e-6f), $"{want.plane}: fade height");
                Assert.That(plane.unit, Is.EqualTo((float)Unit), $"{want.plane}: the record's unit");
                Assert.That(plane.sourceRamp, Is.EqualTo((float)SourceRamp).Within(1e-9f), $"{want.plane}: the record's source ramp, in metres");
                Assert.That(plane.canvas, Is.EqualTo(want.UnityCanvas), $"{want.plane}: the record's canvas, in Unity's terms");
                Assert.That((plane.intensity, plane.falloffPower, plane.maxStretch, plane.ground),
                            Is.EqualTo(((float)Intensity, (float)FalloffPower, (float)MaxStretch, (float)Ground)), $"{want.plane}: its terms");
                Assert.That(plane.followSource, Is.True, $"{want.plane}: placed from its source");

                var renderer = plane.GetComponent<Renderer>();
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off), $"{want.plane}: a shadow casts none");
            }
            // The two lamp planes share one Light: the lamp is one node.
            Assert.That(planes["Box_Lamp_shadow"].source, Is.SameAs(planes["Awning_Lamp_shadow"].source), "one lamp");
        }

        [Test]
        public void A_plane_placed_and_faded_at_a_known_light_matches_the_model_and_the_records_terms()
        {
            GameObject root = Placed();
            Dictionary<string, ShadowPlane> planes = ByName(root);
            Light lamp = planes["Box_Lamp_shadow"].source;
            Light sun = planes["Chair_Sun_shadow"].source;
            lamp.transform.position = new Vector3(-0.6f, 2.2f, 1.4f);

            // The sun shines along the DCC's -Z, turned as it was there; the import mirrors X.
            Vector3 dcc = Quaternion.AngleAxis(SunRotation.y, Vector3.up) * (Quaternion.AngleAxis(SunRotation.x, Vector3.right) * Vector3.back);
            Vector3 shine = new Vector3(-dcc.x, dcc.y, dcc.z);
            Assert.That(Vector3.Angle(planes["Chair_Sun_shadow"].ShineDirection(), shine), Is.LessThan(0.01f),
                        $"the sun shines as it did in the DCC: {planes["Chair_Sun_shadow"].ShineDirection()} for {shine}");

            foreach (Want want in Planes)
            {
                ShadowPlane plane = planes[want.plane];
                plane.ApplyNow();
                Vector3 c = plane.contact.position;
                double r = want.radius * Unit, h = want.height * Unit, fadeHeight = want.fadeHeight * Unit;
                double lx, ly, lz;
                if (want.type == LightType.Directional)
                {
                    ShadowPlaneModel.FarPoint(c.x, c.y, c.z, shine.x, shine.y, shine.z, Math.Max(h, 2.0 * r), out lx, out ly, out lz);
                    Assert.That(plane.source, Is.SameAs(sun));
                }
                else
                {
                    Vector3 l = lamp.transform.position;
                    lx = l.x; ly = l.y; lz = l.z;
                }
                ShadowPlaneModel.Result m = ShadowPlaneModel.Evaluate(c.x, c.y, c.z, lx, ly, lz, Ground, r, h, MaxStretch);
                double fade = ShadowPlaneModel.Fade(m, ly, c.y, Ground, r, Intensity, FalloffPower, fadeHeight, SourceRamp);
                Assert.That(fade, Is.GreaterThan(0.05).And.LessThan(1.0), $"{want.plane}: a fade the light's place sets ({fade})");
                Assert.That(plane.Fade, Is.EqualTo((float)fade).Within(1e-4f), $"{want.plane}: the model's fade at the record's terms");

                Vector4 canvas = want.UnityCanvas;
                m.Placement(canvas.x, canvas.y, canvas.z, canvas.w, out double cx, out double cz, out double along, out double across);
                Vector3 at = plane.transform.position;
                Assert.That(new Vector2(at.x, at.z), Is.EqualTo(new Vector2((float)cx, (float)cz)).Using(Vector2EqualityComparer(1e-4f)),
                            $"{want.plane}: centred where the model places it");
                Assert.That(at.y, Is.EqualTo((float)(Ground + ShadowPlaneModel.GroundOffset * Unit)).Within(1e-5f),
                            $"{want.plane}: on the ground, lifted a hundredth of the record's unit");
                Extents(plane, new Vector3((float)m.bearingX, 0f, (float)m.bearingZ), out float a, out float w);
                Assert.That(a, Is.EqualTo((float)along).Within(1e-3f), $"{want.plane}: its length along the bearing");
                Assert.That(w, Is.EqualTo((float)across).Within(1e-3f), $"{want.plane}: its width across it");
            }

            // The fade follows the lamp: as it sinks toward the horizon the box's shadow
            // lengthens and pales.
            ShadowPlane box = planes["Box_Lamp_shadow"];
            float high = box.Fade;
            lamp.transform.position = new Vector3(-3.8f, 1.5f, 2.2f);
            box.ApplyNow();
            Assert.That(box.Fade, Is.LessThan(high), $"a low lamp pales the shadow: {box.Fade} after {high}");
        }

        [Test]
        public void At_the_files_own_pose_each_plane_lands_where_the_dcc_placed_it()
        {
            // Every plane's canvas is lopsided (its rig fitted it to the silhouette it drew), and
            // the awning's sides are too: the file holds each quad where the producer's rig put it for
            // the lamp and the sun where they stand in it, so placing it again from them lands
            // it there -- 7 cm off for the awning, read with its sides unswapped.
            GameObject root = Placed();
            foreach (Want want in Planes)
            {
                ShadowPlane plane = ByName(root)[want.plane];
                Bounds before = plane.GetComponent<Renderer>().bounds;
                plane.ApplyNow();
                Bounds after = plane.GetComponent<Renderer>().bounds;
                Assert.That(new Vector2(after.center.x, after.center.z),
                            Is.EqualTo(new Vector2(before.center.x, before.center.z)).Using(Vector2EqualityComparer(1e-4f)),
                            $"{want.plane}: centred where the DCC put it ({after.center} for {before.center})");
                Assert.That(new Vector2(after.size.x, after.size.z),
                            Is.EqualTo(new Vector2(before.size.x, before.size.z)).Using(Vector2EqualityComparer(1e-3f)),
                            $"{want.plane}: its footprint the DCC's ({after.size} for {before.size})");
            }
        }

        /// <summary>The plane's quad's world extents along <paramref name="bearing"/> and across it.</summary>
        static void Extents(ShadowPlane plane, Vector3 bearing, out float along, out float across)
        {
            Bounds b = plane.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 u = bearing.normalized, w = new Vector3(u.z, 0f, -u.x);
            float aMin = float.MaxValue, aMax = float.MinValue, wMin = float.MaxValue, wMax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 p = plane.transform.TransformPoint(corner);
                float pa = Vector3.Dot(p, u), pw = Vector3.Dot(p, w);
                aMin = Mathf.Min(aMin, pa); aMax = Mathf.Max(aMax, pa);
                wMin = Mathf.Min(wMin, pw); wMax = Mathf.Max(wMax, pw);
            }
            along = aMax - aMin;
            across = wMax - wMin;
        }

        static IEqualityComparer<Vector2> Vector2EqualityComparer(float tolerance) => new Near(tolerance);

        sealed class Near : IEqualityComparer<Vector2>
        {
            readonly float _tolerance;
            public Near(float tolerance) { _tolerance = tolerance; }
            public bool Equals(Vector2 a, Vector2 b) => (a - b).magnitude <= _tolerance;
            public int GetHashCode(Vector2 v) => 0;
        }

        [Test]
        public void BakeHorizon_on_each_target_reproduces_the_delivered_map()
        {
            GameObject root = Placed();
            if (!AssetDatabase.IsValidFolder(Scratch)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Scratch));
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Scratch + "/Baked.unity");
            var report = new List<string>();
            foreach (Want want in Planes)
            {
                ShadowPlane plane = ByName(root)[want.plane];
                string delivered = AssetDatabase.GetAssetPath(plane.horizonMap);
                byte[] before = TopRowFirst(delivered, out int width, out int height);
                Vector4 bounds = plane.horizonBounds;
                float heightScale = plane.horizonHeightScale;

                Assert.That(ShadowPlaneActions.BakeHorizon(plane), Is.False,
                            $"{want.plane}: the delivered map is current for the delivered geometry (the import stamped its hash)");
                Assert.That(ShadowPlaneActions.BakeHorizon(plane, force: true), Is.True, $"{want.plane}: rebaked");
                string baked = AssetDatabase.GetAssetPath(plane.horizonMap);
                Assert.That(baked, Does.StartWith(Scratch + "/Baked_ShadowPlanes/"), $"{want.plane}: written beside the scene");
                byte[] after = TopRowFirst(baked, out int w2, out int h2);

                Assert.That((w2, h2), Is.EqualTo((width, height)), $"{want.plane}: the image's size");
                Assert.That((plane.horizonSize, plane.horizonSpans), Is.EqualTo((MapSize, MapSpans)), $"{want.plane}: the layout");
                // The producer baked in centimetres, Unity in metres: the layout scales by the record's unit.
                float scale = Mathf.Max(Mathf.Abs(bounds.x), Mathf.Abs(bounds.y), Mathf.Abs(bounds.z), Mathf.Abs(bounds.w));
                Assert.That((plane.horizonBounds - bounds).magnitude, Is.LessThan(1e-5f * scale),
                            $"{want.plane}: the footprint's bounds, {plane.horizonBounds} for {bounds}");
                Assert.That(plane.horizonHeightScale, Is.EqualTo(heightScale).Within(1e-5f * heightScale), $"{want.plane}: the height scale");

                // Every code within the conformance's tolerance, a span's occupancy mask within
                // one sub-bin (MaskBits). The baker is watertight on an edge two triangles share (a
                // top-left rule; the producer's and its port alike), so the producer's quads, split
                // on the other diagonal than Unity's import splits them, bake the same codes.
                int off = 0, codes = 0, worst = 0, maskBits = 0, outside = 0;
                string worstAt = "";
                foreach (var (x, y, channel, a, b) in HorizonCodes.Of(after, before, MapSize, MapSpans))
                {
                    codes++;
                    if (a == b) continue;
                    off++;
                    if (channel == "BA" && x < MapSize * MapSpans)
                    {
                        maskBits = Math.Max(maskBits, Bits(a ^ b));
                        continue;
                    }
                    if (Math.Abs(a - b) > worst)
                    {
                        worst = Math.Abs(a - b);
                        worstAt = $"texel ({x}, {y}) {channel}: {a} for {b}";
                    }
                    if (Math.Abs(a - b) > TexelTolerance) outside++;
                }
                report.Add($"{want.plane}: {off} of {codes} codes differ: every code by at most {worst} {worstAt} " +
                           $"and every mask by at most {maskBits} bit(s)");
                Assert.That(outside, Is.Zero, $"{want.plane}: past the tolerance: {report.Last()}");
                Assert.That(maskBits, Is.LessThanOrEqualTo(MaskBits), $"{want.plane}: {report.Last()}");
            }
            TestContext.Out.WriteLine(string.Join("\n", report));
        }

        /// <summary>
        /// The sub-bins a span's occupancy mask may differ by from the producer's: the imported
        /// geometry is the producer's in float32 metres, the producer's double centimetres, so a
        /// face that sits on a sub-bin's boundary lands on either side of it, one bit -- where the
        /// conformance's one code (on the reference's own input) reads a whole 16-bit mask.
        /// </summary>
        const int MaskBits = 1;

        static int Bits(int v)
        {
            int n = 0;
            for (; v != 0; v &= v - 1) n++;
            return n;
        }

        /// <summary>The PNG at <paramref name="path"/> as RGBA bytes, top row first (the baker's order).</summary>
        byte[] TopRowFirst(string path, out int width, out int height)
        {
            var read = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            _made.Add(read);
            Assert.That(read.LoadImage(File.ReadAllBytes(path)), Is.True, $"{path}: a PNG");
            width = read.width;
            height = read.height;
            Color32[] pixels = read.GetPixels32();
            var bytes = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color32 c = pixels[(height - 1 - y) * width + x];
                    int i = (y * width + x) * 4;
                    bytes[i] = c.r;
                    bytes[i + 1] = c.g;
                    bytes[i + 2] = c.b;
                    bytes[i + 3] = c.a;
                }
            return bytes;
        }

        [Test]
        public void The_demo_recipe_builds_a_scene_whose_planes_draw()
        {
            string folder = Folder();
            Type recipe = Type.GetType(Recipe);
            Assert.That(recipe, Is.Not.Null, "the sample's editor assembly is missing: it did not compile, or the sample is not imported");
            var scene = (Scene)recipe.GetMethod("Build").Invoke(null, new object[] { folder });
            Assert.That(scene.path, Is.EqualTo(folder + "/ShadowPlaneDemo.unity"), "saved beside the model");
            GameObject model = scene.GetRootGameObjects().Single(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == Prefab());
            var animator = model.GetComponent<Animator>();
            Assert.That(animator != null && animator.runtimeAnimatorController != null, Is.True, "an Animator plays the lamp's sweep");
            AnimationClip clip = animator.runtimeAnimatorController.animationClips.Single();
            Assert.That(clip.length, Is.EqualTo(3f - 1f / 24f).Within(1e-3f), "the sweep: three seconds, frame 1 to 72");

            Dictionary<string, ShadowPlane> planes = ByName(model);
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            Assert.That(lights.Where(l => l.enabled).Select(l => l.type),
                        Is.EquivalentTo(new[] { LightType.Point, LightType.Directional }), "the model's lamp and sun light it, nothing else");
            Assert.That(lights.All(l => l.shadows == LightShadows.None), Is.True, "no shadow map: the planes are the shadows");
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            foreach (Want want in Planes)
            {
                ShadowPlane plane = planes[want.plane];
                Assert.That(plane.source != null && plane.source.isActiveAndEnabled, Is.True, $"{want.plane}: a real Light, on");
                Assert.That(plane.isActiveAndEnabled && plane.CanMarch, Is.True, $"{want.plane}: on, marching its map");
                var renderer = plane.GetComponent<Renderer>();
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo(ShadowPlaneImporter.ShaderName),
                            $"{want.plane}: on the package shader the pipeline draws");
                plane.ApplyNow();
                Assert.That(plane.Fade, Is.GreaterThan(0.05f), $"{want.plane}: a shadow at the scene's first frame ({plane.Fade})");
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_Fade"), Is.EqualTo(plane.Fade), $"{want.plane}: the fade drawn");
                // In view, if only in part: a long shadow's quad reaches past the frame's edge.
                Assert.That(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), renderer.bounds), Is.True,
                            $"{want.plane} in view: {renderer.bounds}");
            }

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Inconclusive("no graphics device draws here (-nographics): the planes' pixels are not measured");
            // Drawn: each plane darkens the ground in view, against the frame drawn without it.
            Color32[] lit = Render(camera);
            foreach (ShadowPlane plane in planes.Values)
            {
                var renderer = plane.GetComponent<Renderer>();
                renderer.enabled = false;
                Color32[] without = Render(camera);
                renderer.enabled = true;
                int darker = 0, lighter = 0;
                for (int i = 0; i < lit.Length; i++)
                {
                    int d = Grey(without[i]) - Grey(lit[i]);
                    if (d > 2) darker++;
                    else if (d < -2) lighter++;
                }
                TestContext.Out.WriteLine($"{plane.name}: {darker} pixels darker, {lighter} lighter for it");
                Assert.That(darker, Is.GreaterThan(30), $"{plane.name} darkens the ground it lies on ({darker} pixels)");
                Assert.That(lighter, Is.EqualTo(0), $"{plane.name} only darkens: a shadow ({lighter} pixels lighter)");
            }
        }

        [Test]
        public void At_scale_factor_2_the_planes_scale_with_the_meshes()
            => PlanesKeepToTheMeshes(importer => importer.globalScale = 2f, 2f);

        [Test]
        public void With_convert_units_off_the_planes_scale_with_the_meshes()
            => PlanesKeepToTheMeshes(importer => importer.useFileScale = false, 0f);

        // The sample's model copied in and imported, then reimported with set applied to its
        // importer: every length of every plane scales as the meshes do (the planes' quads aside).
        static void PlanesKeepToTheMeshes(Action<ModelImporter> set, float expected)
        {
            Dictionary<string, float[]> Read(GameObject model) => ByName(model).ToDictionary(p => p.Key, p => Lengths(p.Value));
            var (before, after, scale) = ImportScaleCheck.Rescaled(Model(), Scratch, set, Read, expected,
                                                                   r => r.GetComponent<ShadowPlane>() == null);
            foreach (var pair in after)
            {
                float[] now = pair.Value, was = before[pair.Key];
                for (int i = 0; i < now.Length; i++)
                    Assert.That(now[i], Is.EqualTo(was[i] * scale).Within(1e-4f * Mathf.Max(1f, Mathf.Abs(was[i] * scale))),
                                $"{pair.Key}: {LengthNames[i]}, x{scale}");
            }
        }

        static readonly string[] LengthNames =
        {
            "radius", "height", "ground", "fadeHeight", "sourceRamp", "sourceSize", "horizonHeightScale", "unit",
            "horizonBounds.x", "horizonBounds.y", "horizonBounds.z", "horizonBounds.w",
        };

        static float[] Lengths(ShadowPlane p) => new[]
        {
            p.radius, p.height, p.ground, p.fadeHeight, p.sourceRamp, p.sourceSize, p.horizonHeightScale, p.unit,
            p.horizonBounds.x, p.horizonBounds.y, p.horizonBounds.z, p.horizonBounds.w,
        };

        static int Grey(Color32 c) => (c.r * 299 + c.g * 587 + c.b * 114) / 1000;

        /// <summary>What <paramref name="camera"/> draws now, every plane placed first: its pixels, bottom row first.</summary>
        Color32[] Render(Camera camera)
        {
            const int w = 320, h = 180;
            var target = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture was = camera.targetTexture, active = RenderTexture.active;
            try
            {
                foreach (ShadowPlane plane in Object.FindObjectsByType<ShadowPlane>(FindObjectsSortMode.None)) plane.ApplyNow();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                _made.Add(read);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply();
                return read.GetPixels32();
            }
            finally
            {
                camera.targetTexture = was;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
