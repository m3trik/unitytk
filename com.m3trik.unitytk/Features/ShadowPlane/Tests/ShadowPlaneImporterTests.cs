using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.ShadowPlane.Tests
{
    /// <summary>
    /// The importer's work on a model, read through the record and media stand-ins: a
    /// v3 record fills the plane's component by REFERENCE (its source's Light, a bare
    /// source node's stand-in, its contact, its target), in metres, binds the delivered
    /// horizon map and depends on it, and puts the plane on the shared material where the
    /// pipeline's host draws the package shader (the host's unlit shader where it is
    /// absent or undrawn, Unity's material where no host serves the pipeline); names
    /// resolve exactly, else by their leaf, and a tie stays unbound and is reported; the
    /// silhouette extension is ignored; a v2 record is refused with its fix and a stale
    /// plane goes; an asset that is no delivery is left alone; a horizon map's first
    /// import is pinned; and the record it reads is declared.
    /// </summary>
    public class ShadowPlaneImporterTests
    {
        const string Folder = "Assets/UnitytkShadowPlaneTests";
        const string Model = Folder + "/crate.fbx";
        const string MapFile = "UnitytkTestCrate_horizon.asset";
        const string Plane = "Crate_shadow";

        /// <summary>The fixture's canvas: lopsided, so a side or an edge read wrong shows.</summary>
        const string Canvas = "[-0.4, 1.6, -0.8, 0.3]";

        /// <summary>A v3 record in centimetres (unit_scale 0.01), its silhouette an extension Unity ignores.</summary>
        static string Record(string name = Plane, string source = "Lamp", string contact = "Crate_contact",
                             string sourceType = "point", int version = 3, string unitScale = "0.01",
                             string sourceRamp = "1")
            => @"{""version"": " + version + @", ""unit_scale"": " + unitScale + @", ""planes"": [" +
               PlaneRecord(name, source, contact, sourceType, sourceRamp) + "]}";

        /// <summary>A v3 record of several planes, each <c>(name, source, contact)</c>.</summary>
        static string Records(params (string name, string source, string contact)[] planes)
            => @"{""version"": 3, ""unit_scale"": 0.01, ""planes"": [" +
               string.Join(", ", planes.Select(p => PlaneRecord(p.name, p.source, p.contact, "directional"))) + "]}";

        static string PlaneRecord(string name, string source, string contact, string sourceType, string sourceRamp = "1")
            => @"{
            ""name"": """ + name + @""", ""source"": """ + source + @""", ""contact"": """ + contact + @""",
            ""intensity"": 0.8, ""source_type"": """ + sourceType + @""", ""source_size"": 5, ""source_angle"": 0.0093,
            ""follow_source"": true, ""ground"": 0, ""radius"": 40, ""height"": 90, ""max_stretch"": 4,
            ""canvas"": " + Canvas + @", ""falloff_power"": 1.5, ""fade_height"": 180, ""source_ramp"": " + sourceRamp + @",
            ""horizon"": {""texture"": """ + MapFile + @""", ""size"": 64, ""spans"": 2, ""bounds"": [-50, 60, -40, 40],
                         ""height_scale"": 95, ""frame_a"": [1, 0, 0], ""frame_b"": [0, 0, 1], ""rect"": [1, 1, 0, 0]},
            ""silhouette"": {""texture"": ""crate_shadow.png"", ""draw"": true}}";

        /// <summary>A media stand-in: its answer to the extraction ask, and its media folders.</summary>
        sealed class Media : IModelMedia
        {
            public bool Asks;
            public string[] Folders;
            public bool RequestEmbeddedFiles(string assetPath, IEnumerable<string> names, System.Func<string, bool> holds = null) => Asks;
            public bool HoldsCurrent(string assetPath, string name) => false;
            public string MediaFolder(string assetPath) => Folders[0];
            public string[] MediaFolders(string assetPath) => Folders;
            public bool IsOwnMedia(string modelAssetPath, string fileAssetPath) => false;
            public bool InOwnMediaFolder(string modelAssetPath, string fileAssetPath) => false;
            public bool IsReusableBy(string modelAssetPath, string fileAssetPath) => true;
            public IEnumerable<string> OwnMediaCopies(string assetPath, string name) => new string[0];
        }

        GameObject _root;
        Transform _crate;
        MeshRenderer _renderer;
        Material _material;
        Mesh _quad;
        Mesh _box;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("crate");
            _crate = Child(_root.transform, "Crate");
            _box = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0f, 0.9f, 0.3f) },
                triangles = new[] { 0, 1, 2 },
            };
            _crate.gameObject.AddComponent<MeshFilter>().sharedMesh = _box;
            _crate.gameObject.AddComponent<MeshRenderer>();
            Child(_crate, "Crate_contact");
            var lamp = Child(_root.transform, "Lamp").gameObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            var plane = Child(_root.transform, Plane);
            _quad = new Mesh
            {
                vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) },
                uv = new[] { new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(0.5f, 1f) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            plane.gameObject.AddComponent<MeshFilter>().sharedMesh = _quad;
            _material = new Material(Shader.Find("Standard"));
            _renderer = plane.gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_material);
            Object.DestroyImmediate(_quad);
            Object.DestroyImmediate(_box);
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        ShadowPlane Component => _renderer.GetComponent<ShadowPlane>();

        /// <summary>The map the record names, beside the model (the stand-in's media folder).</summary>
        static Texture2D MakeMap()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            AssetDatabase.CreateAsset(new Texture2D(64 * 4, 64), Folder + "/" + MapFile);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + MapFile);
        }

        int Wire(string record, PureRecordSource records = null, List<string> depends = null, IRenderPipelineHost host = null,
                 bool builtIn = true)
            => ShadowPlaneImporter.Wire(_root, record, Model, records ?? new PureRecordSource(), new PureModelMedia(),
                                        builtIn ? RenderPipelineHosts.BuiltIn : host, Shader.Find(ShadowPlaneImporter.ShaderName),
                                        depends != null ? (System.Action<string>)depends.Add : null);

        [Test]
        public void A_v3_record_fills_the_plane_by_reference_in_metres_and_binds_its_map()
        {
            Texture2D map = MakeMap();
            var records = new PureRecordSource();
            var depends = new List<string>();
            Assert.That(Wire(Record(), records, depends), Is.EqualTo(1));
            Assert.That(records.Refusals, Is.Empty, "the silhouette extension is no refusal");

            ShadowPlane plane = Component;
            Assert.That(plane, Is.Not.Null);
            Assert.That(plane.source, Is.SameAs(_root.transform.Find("Lamp").GetComponent<Light>()), "the source node's Light");
            Assert.That(plane.contact, Is.SameAs(_crate.Find("Crate_contact")));
            Assert.That(plane.target, Is.SameAs(_crate), "the node the contact hangs on");
            Assert.That(plane.space, Is.SameAs(_root.transform), "placed in the model's frame");
            Assert.That(plane.radius, Is.EqualTo(0.4f).Within(1e-6f), "metres");
            Assert.That(plane.height, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(plane.ground, Is.EqualTo(0f));
            Assert.That(plane.sourceSize, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(plane.sourceAngle, Is.EqualTo(0.0093f * Mathf.Rad2Deg).Within(1e-5f), "degrees");
            Assert.That((plane.intensity, plane.falloffPower, plane.maxStretch), Is.EqualTo((0.8f, 1.5f, 4f)));
            Assert.That(plane.fadeHeight, Is.EqualTo(1.8f).Within(1e-6f));
            Assert.That(plane.unit, Is.EqualTo(0.01f).Within(1e-9f));
            Assert.That(plane.sourceRamp, Is.EqualTo(0.01f).Within(1e-9f), "the record's source ramp (one unit), in metres");
            Assert.That(plane.canvas, Is.EqualTo(new Vector4(-0.4f, 1.6f, -0.3f, 0.8f)),
                        "a Y-up producer's canvas, its sides along Unity's mirrored local +X");
            Assert.That(plane.followSource, Is.True);

            Assert.That(plane.horizonMap, Is.SameAs(map), "the delivered map, bound");
            Assert.That(depends, Is.EqualTo(new[] { Folder + "/" + MapFile }), "...and the model depends on it");
            Assert.That((plane.horizonSize, plane.horizonSpans), Is.EqualTo((64, 2)));
            Assert.That(Vector4.Distance(plane.horizonBounds, new Vector4(-0.5f, 0.6f, -0.4f, 0.4f)), Is.LessThan(1e-6f));
            Assert.That(plane.horizonHeightScale, Is.EqualTo(0.95f).Within(1e-6f));
            Assert.That(plane.horizonFrameA, Is.EqualTo(Vector3.left), "FBX axes: X mirrored");
            Assert.That(plane.horizonFrameB, Is.EqualTo(Vector3.forward));
            Assert.That(plane.horizonHash, Is.Not.Empty, "the delivered geometry's hash");

            Shader shader = Shader.Find(ShadowPlaneImporter.ShaderName);
            Assert.That(_material.shader, Is.SameAs(shader));
            Assert.That(_material.enableInstancing, Is.True);
            Assert.That(_material.GetTexture("_HorizonMap"), Is.SameAs(map));
            Assert.That(_material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(_quad.uv, Is.EqualTo(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }),
                        "the plane's UVs, the unit square the cache marches");
            Assert.That(_renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(_renderer.receiveShadows, Is.False);
            Assert.That(_renderer.lightProbeUsage, Is.EqualTo(LightProbeUsage.Off));
            Assert.That(_renderer.reflectionProbeUsage, Is.EqualTo(ReflectionProbeUsage.Off));
        }

        [Test]
        public void A_bare_source_node_casts_through_a_switched_off_stand_in_light()
        {
            Child(_root.transform, "Sun_locator");
            Wire(Record(source: "Sun_locator", sourceType: "directional"));
            Light light = Component.source;
            Assert.That(light, Is.Not.Null);
            Assert.That(light.name, Is.EqualTo(ShadowPlaneImporter.StandInName));
            Assert.That(light.transform.parent, Is.SameAs(_root.transform.Find("Sun_locator")), "hung under the node");
            Assert.That(light.type, Is.EqualTo(LightType.Directional), "the record's type");
            Assert.That(light.enabled, Is.False, "it lights nothing");
            Assert.That(Vector3.Dot(light.transform.forward, -light.transform.parent.forward), Is.EqualTo(1f).Within(1e-5f),
                        "shining along the node's -Z, as a bare source node shines");
        }

        [Test]
        public void Names_resolve_exactly_else_by_their_leaf()
        {
            Wire(Record(name: "rig:" + Plane, source: "rig:Lamp", contact: "rig:Crate_contact"));
            Assert.That(Component, Is.Not.Null, "a namespaced plane name binds its leaf");
            Assert.That(Component.source, Is.SameAs(_root.transform.Find("Lamp").GetComponent<Light>()));
            Assert.That(Component.contact, Is.SameAs(_crate.Find("Crate_contact")));

            // An exact name wins over a leaf.
            var exact = Child(_root.transform, "rig:Lamp").gameObject.AddComponent<Light>();
            Wire(Record(source: "rig:Lamp"));
            Assert.That(Component.source, Is.SameAs(exact));
        }

        [Test]
        public void A_tied_name_stays_unbound_and_is_reported()
        {
            Child(Child(_root.transform, "other"), "Crate_contact");
            LogAssert.Expect(LogType.Warning, new Regex("contact 'Crate_contact' matches several objects equally.*Crate/Crate_contact.*other/Crate_contact"));
            Wire(Record());
            Assert.That(Component, Is.Not.Null, "the plane is still made");
            Assert.That(Component.contact == null, "a tie binds nothing");
            Assert.That(Component.target == null, "...nor a target");

            var twin = Child(_root.transform, "ns:" + Plane);
            twin.gameObject.AddComponent<MeshFilter>().sharedMesh = _quad;
            twin.gameObject.AddComponent<MeshRenderer>();
            LogAssert.Expect(LogType.Warning, new Regex("shadow plane 'x:" + Plane + "' matches several objects equally"));
            Assert.That(Wire(Record(name: "x:" + Plane)), Is.EqualTo(0), "two planes of one leaf: neither guessed");
        }

        [Test]
        public void A_v2_record_is_refused_with_its_fix_and_a_stale_plane_goes()
        {
            var records = new PureRecordSource();
            Wire(Record(), records);
            Assert.That(Component, Is.Not.Null);
            int wired = Wire(Record(version: 2), records);
            Assert.That(wired, Is.EqualTo(0));
            Assert.That(Component == null, "the stale plane went");
            Assert.That(records.Refusals.Single(), Does.Contain("shadows record v2").And.Contain("re-export"));
        }

        [Test]
        public void An_asset_that_is_no_delivery_is_left_alone()
        {
            var records = new PureRecordSource();
            Assert.That(ShadowPlaneImporter.Import(_root, Record(), Path.ChangeExtension(Model, ".obj"), records,
                                                   new PureModelMedia()), Is.EqualTo(0));
            Assert.That(Component == null, "nothing attached");
            Assert.That(_material.shader.name, Is.EqualTo("Standard"), "nothing rewired");

            Assert.That(ShadowPlaneImporter.Import(_root, Record(), Model, records, new PureModelMedia()), Is.EqualTo(1));
            Assert.That(Component, Is.Not.Null);
        }

        [Test]
        public void Without_the_package_shader_a_plane_takes_the_hosts_unlit_shader()
        {
            IRenderPipelineHost host = RenderPipelineHosts.BuiltIn;
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(ShadowPlaneImporter.ShaderName) + "' not found"));
            ShadowPlaneImporter.Wire(_root, Record(), Model, new PureRecordSource(), new PureModelMedia(), host, null);
            Assert.That(_material.shader.name, Is.EqualTo(host.UnlitShader(true)));
            Assert.That(Component, Is.Not.Null, "the plane still follows its source");
        }

        [Test]
        public void A_pipeline_no_host_serves_keeps_unitys_material()
        {
            LogAssert.Expect(LogType.Warning, new Regex("render pipeline has no unitytk host"));
            Wire(Record(), builtIn: false);
            Assert.That(_material.shader.name, Is.EqualTo("Standard"), "Unity's material stays");
            Assert.That(Component, Is.Not.Null, "the plane still follows its source");
            Assert.That(_renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
        }

        [Test]
        public void A_host_that_does_not_draw_the_package_shader_takes_its_own_unlit_shader_and_blend_switch()
        {
            var host = new PureRenderPipelineHost { DrawsShader = shader => false };
            Assume.That(Shader.Find(host.UnlitShader(true)), Is.Not.Null, "URP is installed (the URP owned project)");
            LogAssert.Expect(LogType.Warning, new Regex("the URP-like render pipeline does not draw"));
            Wire(Record(), host: host, builtIn: false);
            Assert.That(_material.shader.name, Is.EqualTo(host.UnlitShader(true)));
            Assert.That(host.Blended, Is.EqualTo(new[] { (_material, true) }), "blended once, by the host's own switch");
            Assert.That(Component, Is.Not.Null);
        }

        [Test]
        public void A_horizon_maps_first_import_is_pinned()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string path = Folder + "/UnitytkTestOn_horizon.png";
            var pixels = new Texture2D(4, 4);
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var on = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(on.sRGBTexture, Is.False, "a horizon map is data");
            Assert.That(on.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(on.mipmapEnabled, Is.False);
            Assert.That(on.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(ShadowPlaneImporter.PinHorizon(on, false), Is.False);
        }

        [Test]
        public void The_package_shader_is_built_ins_and_its_drawing_pass_states_no_light_mode()
        {
            Shader shader = Shader.Find(ShadowPlaneImporter.ShaderName);
            Assert.That(RenderPipelineHosts.BuiltIn.Draws(shader), Is.True);
            Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(shader, ""), Is.True);
        }

        int WireWith(string record, IModelMedia media, List<string> depends = null)
            => ShadowPlaneImporter.Wire(_root, record, Model, new PureRecordSource(), media, RenderPipelineHosts.BuiltIn,
                                        Shader.Find(ShadowPlaneImporter.ShaderName),
                                        depends != null ? (System.Action<string>)depends.Add : null);

        /// <summary>A second plane under the root, on its own imported material.</summary>
        MeshRenderer SecondPlane(string name, out Material material)
        {
            var go = Child(_root.transform, name).gameObject;
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            material = new Material(Shader.Find("Standard"));
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        [Test]
        public void Two_planes_share_one_material_and_one_bare_sources_stand_in()
        {
            Texture2D map = MakeMap();
            Child(_root.transform, "Sun_locator");
            Transform chair = Child(_root.transform, "Chair");
            chair.gameObject.AddComponent<MeshFilter>().sharedMesh = _box;
            chair.gameObject.AddComponent<MeshRenderer>();
            Child(chair, "Chair_contact");
            MeshRenderer second = SecondPlane("Chair_shadow", out Material own);
            try
            {
                Assert.That(Wire(Records((Plane, "Sun_locator", "Crate_contact"), ("Chair_shadow", "Sun_locator", "Chair_contact"))),
                            Is.EqualTo(2));
                Assert.That(second.sharedMaterial, Is.SameAs(_material), "one shared material, the first plane's");
                Assert.That(_material.enableInstancing, Is.True);
                Light light = Component.source;
                Assert.That(second.GetComponent<ShadowPlane>().source, Is.SameAs(light), "one stand-in for both");
                Assert.That(_root.transform.Find("Sun_locator").GetComponentsInChildren<Light>(true), Has.Length.EqualTo(1));
                Assert.That(second.GetComponent<ShadowPlane>().target, Is.SameAs(chair));
            }
            finally
            {
                Object.DestroyImmediate(own);
            }
        }

        [Test]
        public void A_contact_with_no_occluder_of_its_own_names_no_target()
        {
            Child(_root.transform, "Root_contact");
            Wire(Record(contact: "Root_contact"));
            Assert.That(Component.contact, Is.Not.Null);
            Assert.That(Component.target == null, "a contact on the model's root: no target");

            Child(_renderer.transform, "Plane_contact");
            Wire(Record(contact: "Plane_contact"));
            Assert.That(Component.target == null, "a node holding only the shadow plane's own mesh: no target");

            LogAssert.Expect(LogType.Warning, new Regex("contact 'Nowhere' is not in the file"));
            Wire(Record(contact: "Nowhere"));
            Assert.That(Component.target == null, "no contact: no target");
        }

        [Test]
        public void A_rigged_target_is_the_node_its_contact_hangs_on()
        {
            Transform figure = Child(_root.transform, "Figure");
            Transform skin = Child(figure, "Figure_skin");
            skin.gameObject.AddComponent<SkinnedMeshRenderer>().sharedMesh = _box;
            Child(figure, "Figure_contact");
            Wire(Record(contact: "Figure_contact"));
            Assert.That(Component.target, Is.SameAs(figure), "a skinned mesh is an occluder");
        }

        [Test]
        public void A_z_up_producers_contact_sets_the_planes_z_up_frames()
        {
            Wire(Record());
            Assert.That(Component.zUpFrames, Is.False, "a Y-up contact");
            _crate.Find("Crate_contact").localRotation = Quaternion.Euler(-90f, 0f, 0f);  // its +Z up
            Wire(Record());
            Assert.That(Component.zUpFrames, Is.True, "a contact whose +Z is the model's up");
        }

        [Test]
        public void On_the_import_that_asks_for_the_maps_it_reports_nothing_missing()
        {
            LogAssert.Expect(LogType.Log, new Regex("asked of the extraction"));
            Assert.That(WireWith(Record(contact: "Nowhere"), new Media { Asks = true, Folders = new[] { Folder } }), Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
            Assert.That(Component.horizonMap == null, "the map comes on the reimport the extraction brings");
        }

        [Test]
        public void A_record_with_an_invalid_unit_scale_is_refused_and_a_stale_plane_goes()
        {
            Wire(Record());
            Assert.That(Component, Is.Not.Null);
            LogAssert.Expect(LogType.Warning, new Regex("unit scale of 0, so its shadow planes cannot be sized"));
            Assert.That(Wire(Record(unitScale: "0")), Is.Zero);
            Assert.That(Component == null, "the stale plane went");
        }

        [Test]
        public void A_map_in_the_models_fbm_folder_binds_without_a_dependency()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            AssetDatabase.CreateFolder(Folder, "crate.fbm");
            string fbm = Folder + "/crate.fbm";
            AssetDatabase.CreateAsset(new Texture2D(64 * 4, 64), fbm + "/" + MapFile);
            var depends = new List<string>();
            Assert.That(WireWith(Record(), new Media { Folders = new[] { fbm } }, depends), Is.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(Component.horizonMap), Is.EqualTo(fbm + "/" + MapFile), "bound");
            Assert.That(depends, Is.Empty, "Unity rewrites the .fbm on every import: a dependency there loops the import");
        }

        [Test]
        public void A_source_lights_type_against_the_record_is_reported()
        {
            LogAssert.Expect(LogType.Warning, new Regex("shadow source 'Lamp' is a Point light, and the record states a directional source"));
            Wire(Record(sourceType: "directional"));
            Assert.That(Component.source.type, Is.EqualTo(LightType.Point), "it casts as the Light does");
        }

        [Test]
        public void Each_fallback_plane_draws_at_its_intensity()
        {
            var host = new PureRenderPipelineHost { DrawsShader = shader => false };
            Assume.That(Shader.Find(host.UnlitShader(true)), Is.Not.Null, "URP is installed (the URP owned project)");
            LogAssert.Expect(LogType.Warning, new Regex("the URP-like render pipeline does not draw"));
            Wire(Record(), host: host, builtIn: false);
            Assert.That(_material.GetColor(host.BaseColorProperty).a, Is.EqualTo(0.8f).Within(1e-6f),
                        "the record's intensity: no per-frame fade on the pipeline's own shader");
        }

        [Test]
        public void The_shared_material_draws_nothing_until_a_plane_writes_its_fade()
        {
            Wire(Record());
            Assert.That(_material.GetFloat("_Fade"), Is.Zero, "an unplaced plane draws nothing, never another plane's map");
            Shader shader = Shader.Find(ShadowPlaneImporter.ShaderName);
            Assert.That(shader.GetPropertyDefaultFloatValue(shader.FindPropertyIndex("_Fade")), Is.Zero);
        }

        [Test]
        public void A_z_up_producers_canvas_keeps_its_sides()
        {
            // A Z-up producer's quad lies in its local XY: its record's 2D frame reaches Unity
            // through a turn and the import's mirror, which together keep the sides; a Y-up
            // producer's (the fill test's) reaches it through the mirror alone, which swaps them.
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _quad.RecalculateBounds();
            Wire(Record());
            Assert.That(Component.canvas, Is.EqualTo(new Vector4(-0.4f, 1.6f, -0.8f, 0.3f)));
        }

        [Test]
        public void A_centimetre_source_ramp_in_a_metre_record_fades_as_the_model_does()
        {
            // A metre record of a centimetre rig: its source ramp is 0.01 of its unit. A lamp
            // half a centimetre straight over the contact brings in half the ramp's shadow.
            Wire(Record(unitScale: "1", sourceRamp: "0.01"));
            ShadowPlane plane = Component;
            Assert.That(plane.sourceRamp, Is.EqualTo(0.01f).Within(1e-9f));
            Assert.That(plane.unit, Is.EqualTo(1f), "a metre record");
            Light lamp = plane.source;
            lamp.transform.position = new Vector3(0f, 0.005f, 0f);
            plane.ApplyNow();
            System.Func<double, double> fade = ramp => ShadowPlaneModel.Fade(0.0, 0.0, 0.0, 0.0, 0.005, 0.0, plane.ground,
                plane.radius, plane.height, plane.maxStretch, plane.intensity, plane.falloffPower, plane.fadeHeight, ramp);
            Assert.That(fade(0.01), Is.EqualTo(100.0 * fade(1.0)).Within(1e-9), "half the ramp in, a metre ramp a two-hundredth: a hundred times the fade");
            Assert.That(plane.Fade, Is.EqualTo((float)fade(0.01)).Within(1e-6f), "faded over the record's ramp, not its unit");
        }

        [Test]
        public void A_source_ramp_that_is_not_over_zero_fades_over_one_unit_and_is_reported()
        {
            LogAssert.Expect(LogType.Warning, new Regex("shadow plane 'Crate_shadow' states a source ramp of 0.*re-export"));
            Wire(Record(sourceRamp: "0"));
            Assert.That(Component.sourceRamp, Is.EqualTo(0.01f).Within(1e-9f), "one unit of the record, the contract's default");
        }

        [Test]
        public void The_record_it_reads_is_declared()
        {
            var read = ReadsRecordAttribute.Read(new[] { typeof(ShadowPlaneImporter).Assembly });
            Assert.That(read[UnitytkRecords.Shadows], Is.EqualTo(ShadowRecord.CurrentVersion));
            Assert.That(ShadowRecord.CurrentVersion, Is.EqualTo(3));
        }
    }
}
