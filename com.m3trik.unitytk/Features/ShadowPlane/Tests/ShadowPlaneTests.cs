using NUnit.Framework;
using UnityEditor;
using Object = UnityEngine.Object;
using UnityEngine;

namespace M3trik.Unitytk.ShadowPlane.Tests
{
    /// <summary>
    /// The component in Edit Mode: it places its plane where the ported model says, in
    /// its space's frame, from a Light it holds by reference (the scene's sun when it
    /// holds none); it computes the fade the model gives and draws at its intensity
    /// where it has no contact or source; it judges a horizon layout as the WebXR viewer
    /// does; its shader draws the horizon alone; and its inspector's preview gives the
    /// placement back.
    /// </summary>
    public class ShadowPlaneTests
    {
        GameObject _root;
        Mesh _quad;
        Light _sun;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ShadowPlaneTests_model");
            _quad = new Mesh
            {
                vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            _quad.RecalculateBounds();
            _sun = RenderSettings.sun;
        }

        [TearDown]
        public void TearDown()
        {
            RenderSettings.sun = _sun;
            ShadowPlanePreview.ReleaseAll();
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_quad);
        }

        Transform Child(string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_root.transform, false);
            t.localPosition = at;
            return t;
        }

        Light Lamp(Vector3 at, LightType type = LightType.Point)
        {
            var light = Child("Lamp", at).gameObject.AddComponent<Light>();
            light.type = type;
            return light;
        }

        ShadowPlane Plane(Light source, Transform contact)
        {
            var go = Child("Crate_shadow", Vector3.zero).gameObject;
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            go.AddComponent<MeshRenderer>();
            var plane = go.AddComponent<ShadowPlane>();
            plane.source = source;
            plane.contact = contact;
            plane.radius = 0.5f;
            plane.height = 1f;
            plane.maxStretch = 6f;
            plane.BindSpace(_root.transform);
            return plane;
        }

        [Test]
        public void ApplyNow_places_the_plane_where_the_model_says()
        {
            Light lamp = Lamp(new Vector3(2f, 3f, 1f));
            ShadowPlane plane = Plane(lamp, Child("Crate_contact", new Vector3(0.25f, 0f, -0.5f)));

            plane.ApplyNow();

            var m = ShadowPlaneModel.Evaluate(0.25, 0.0, -0.5, 2.0, 3.0, 1.0, 0.0, 0.5, 1.0, 6.0);
            m.Placement(-1.0, 1.0, -0.5, 0.5, out double cx, out double cz, out double along, out double across);
            Vector3 at = plane.transform.position;
            Assert.That(at.x, Is.EqualTo((float)cx).Within(1e-4f));
            Assert.That(at.y, Is.EqualTo((float)ShadowPlaneModel.GroundOffset).Within(1e-4f), "lifted a hundredth of its unit");
            Assert.That(at.z, Is.EqualTo((float)cz).Within(1e-4f));
            Assert.That(plane.transform.localScale.x, Is.EqualTo((float)across).Within(1e-4f));
            Assert.That(plane.transform.localScale.z, Is.EqualTo((float)along).Within(1e-4f));
            Assert.That(plane.transform.eulerAngles.y, Is.EqualTo(Mathf.Repeat((float)m.YawDegrees, 360f)).Within(1e-3f));
        }

        [Test]
        public void A_moved_space_carries_its_shadow()
        {
            ShadowPlane plane = Plane(Lamp(new Vector3(2f, 3f, 1f)), Child("Crate_contact", Vector3.zero));
            plane.ApplyNow();
            Vector3 rest = plane.transform.localPosition;
            float fade = plane.Fade;

            _root.transform.position = new Vector3(5f, 1f, -2f);
            plane.ApplyNow();
            Assert.That(Vector3.Distance(plane.transform.localPosition, rest), Is.LessThan(1e-4f),
                        "the plane keeps its place in its space's frame");
            Assert.That(plane.Fade, Is.EqualTo(fade).Within(1e-6f), "and its fade: its ground moved with it");
        }

        [Test]
        public void The_fade_is_the_models_and_falls_off_as_the_target_rises()
        {
            // A lamp straight above and no falloff: the stretch term is 1, so the fade is
            // the intensity times the rise's ramp.
            Transform contact = Child("Crate_contact", Vector3.zero);
            ShadowPlane plane = Plane(Lamp(new Vector3(0f, 50f, 0f)), contact);
            plane.intensity = 0.8f;
            plane.falloffPower = 0f;
            plane.fadeHeight = 2f;
            plane.sourceRamp = 1f;
            plane.ApplyNow();
            Assert.That(plane.Fade, Is.EqualTo(0.8f).Within(1e-5f), "grounded: the intensity");

            contact.localPosition = new Vector3(0f, 0.6f, 0f);
            plane.ApplyNow();
            Assert.That(plane.Fade, Is.EqualTo(0.8f * (1f - 0.6f / 2f)).Within(1e-4f), "risen 0.3 of its fade height");
            double model = ShadowPlaneModel.Fade(0.0, 0.6, 0.0, 0.0, 50.0, 0.0, 0.0, 0.5, 1.0, 6.0,
                                                 0.8, plane.falloffPower, 2.0, 1.0);
            Assert.That(plane.Fade, Is.EqualTo((float)model).Within(1e-6f), "the model's fade, term for term");

            contact.localPosition = new Vector3(0f, 2.5f, 0f);
            plane.ApplyNow();
            Assert.That(plane.Fade, Is.EqualTo(0f), "past its fade height: gone");
        }

        [Test]
        public void A_low_sun_fades_the_shadow_by_its_stretch()
        {
            Light sun = Lamp(Vector3.zero, LightType.Directional);
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -0.3f, 0.2f));
            ShadowPlane plane = Plane(sun, Child("Crate_contact", Vector3.zero));
            plane.ApplyNow();
            Vector3 d = sun.transform.forward;
            double want = ShadowPlaneModel.FadeDirectional(0.0, 0.0, 0.0, d.x, d.y, d.z, 0.0, 0.5, 1.0, 6.0,
                                                           plane.intensity, plane.falloffPower, plane.fadeHeight, plane.sourceRamp);
            Assert.That(want, Is.LessThan(0.5), "a long shadow lightens");
            Assert.That(plane.Fade, Is.EqualTo((float)want).Within(1e-5f));
        }

        [Test]
        public void A_plane_made_in_unity_is_placed_on_the_default_canvas_and_fades_in_over_a_metre()
        {
            var plane = _root.AddComponent<ShadowPlane>();
            double[] c = ShadowPlaneModel.DefaultCanvas;
            Assert.That(plane.canvas, Is.EqualTo(new Vector4((float)c[0], (float)c[1], (float)c[2], (float)c[3])));
            Assert.That(plane.sourceRamp, Is.EqualTo(1f), "one metre: a metre scene's unit, as a record's default is one of its own");
        }

        [Test]
        public void A_lopsided_canvas_places_the_quad_where_the_model_says()
        {
            Light lamp = Lamp(new Vector3(2f, 3f, 1f));
            ShadowPlane plane = Plane(lamp, Child("Crate_contact", new Vector3(0.25f, 0f, -0.5f)));
            plane.canvas = new Vector4(-0.4f, 1.6f, -0.8f, 0.3f);

            plane.ApplyNow();

            var m = ShadowPlaneModel.Evaluate(0.25, 0.0, -0.5, 2.0, 3.0, 1.0, 0.0, 0.5, 1.0, 6.0);
            m.Placement(-0.4f, 1.6f, -0.8f, 0.3f, out double cx, out double cz, out double along, out double across);
            m.Placement(-1.0, 1.0, -0.5, 0.5, out double dx, out double dz, out _, out _);
            Assert.That(Vector2.Distance(new Vector2((float)cx, (float)cz), new Vector2((float)dx, (float)dz)), Is.GreaterThan(0.1f),
                        "a canvas whose centre is not the default's");
            Vector3 at = plane.transform.position;
            Assert.That(at.x, Is.EqualTo((float)cx).Within(1e-4f));
            Assert.That(at.z, Is.EqualTo((float)cz).Within(1e-4f));
            Assert.That(plane.transform.localScale.x, Is.EqualTo((float)across).Within(1e-4f));
            Assert.That(plane.transform.localScale.z, Is.EqualTo((float)along).Within(1e-4f));
        }

        [Test]
        public void The_shadow_comes_in_over_its_source_ramp_whatever_its_unit()
        {
            // A lamp straight above, a quarter metre over the contact: the source term alone
            // shapes the fade (no falloff, a fade height far off).
            Transform contact = Child("Crate_contact", Vector3.zero);
            ShadowPlane plane = Plane(Lamp(new Vector3(0f, 0.25f, 0f)), contact);
            plane.falloffPower = 0f;
            plane.fadeHeight = 1000f;
            plane.unit = 1f;
            plane.sourceRamp = 0.5f;
            plane.ApplyNow();
            double model = ShadowPlaneModel.Fade(0.0, 0.0, 0.0, 0.0, 0.25, 0.0, 0.0, 0.5, 1.0, 6.0,
                                                 1.0, 0.0, 1000.0, 0.5);
            Assert.That(model, Is.EqualTo(0.5).Within(1e-9), "half the ramp: half the shadow");
            Assert.That(plane.Fade, Is.EqualTo((float)model).Within(1e-6f), "the ramp is sourceRamp's, not the unit's");
        }

        [Test]
        public void With_no_source_it_casts_from_the_scenes_sun_and_with_neither_draws_at_its_intensity()
        {
            ShadowPlane plane = Plane(null, Child("Crate_contact", Vector3.zero));
            plane.intensity = 0.7f;
            RenderSettings.sun = null;
            plane.ApplyNow();
            Assert.That(plane.SourceLight == null, "no source and no sun");
            Assert.That(plane.Fade, Is.EqualTo(0.7f), "unresolved: its intensity");

            Light sun = Lamp(Vector3.zero, LightType.Directional);
            sun.transform.rotation = Quaternion.LookRotation(Vector3.down + Vector3.forward * 0.5f);
            RenderSettings.sun = sun;
            Assert.That(plane.SourceLight, Is.SameAs(sun), "the scene's sun");
            Assert.That(plane.IsDirectional, Is.True);
            Vector3 before = plane.transform.position;
            plane.ApplyNow();
            Assert.That(plane.transform.position, Is.Not.EqualTo(before), "placed from the sun");
        }

        [Test]
        public void A_horizon_layout_is_judged_as_the_viewer_judges_it()
        {
            var bounds = new Vector4(-1f, 1f, -1f, 1f);
            Assert.That(ShadowPlane.HorizonRefusal(128, 2, 1f, bounds), Is.Null);
            Assert.That(ShadowPlane.HorizonRefusal(4, 2, 1f, bounds), Does.Contain("under 8 pixels"));
            Assert.That(ShadowPlane.HorizonRefusal(128, 0, 1f, bounds), Does.Contain("spans"));
            Assert.That(ShadowPlane.HorizonRefusal(128, 2, 0f, bounds), Does.Contain("height scale"));
            Assert.That(ShadowPlane.HorizonRefusal(128, 2, 1f, new Vector4(1f, -1f, -1f, 1f)), Does.Contain("out of order"));

            var plane = _root.AddComponent<ShadowPlane>();
            Assert.That(plane.CanMarch, Is.False, "no map: it draws nothing");
            var map = new Texture2D(8, 8);
            try
            {
                plane.horizonMap = map;
                Assert.That(plane.CanMarch, Is.True);
                plane.horizonSpans = 0;
                Assert.That(plane.CanMarch, Is.False, "a layout the shader cannot march");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void Its_shader_draws_the_horizon_alone_at_the_fade()
        {
            Shader shader = Shader.Find(ShadowPlaneImporter.ShaderName);
            Assert.That(shader, Is.Not.Null, "the package shader imported with the package");
            Assert.That(shader.FindPropertyIndex("_Fade"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindPropertyIndex("_HorizonMap"), Is.GreaterThanOrEqualTo(0));
            foreach (string gone in new[] { "_Mode", "_BaseMap", "_Rect", "_RecordIntensity" })
                Assert.That(shader.FindPropertyIndex(gone), Is.EqualTo(-1), gone + ": the projected mode is gone");
        }

        [Test]
        public void With_no_sun_source_set_the_scenes_sun_is_its_brightest_enabled_directional_light()
        {
            // Unity's own fallback, measured in 6000.3 (RenderSettings.sun with Sun Source None),
            // which SourceLight, Create and the default source lean on.
            RenderSettings.sun = null;
            Light bright = Lamp(Vector3.zero, LightType.Directional);
            bright.intensity = 90f;
            Light dim = Lamp(Vector3.zero, LightType.Directional);
            dim.intensity = 80f;
            ShadowPlane plane = Plane(null, Child("Crate_contact", Vector3.zero));
            Assert.That(plane.SourceLight, Is.SameAs(bright), "the brightest");
            bright.enabled = false;
            Assert.That(plane.SourceLight, Is.SameAs(dim), "...that is enabled");
        }

        [Test]
        public void A_scaled_and_turned_space_keeps_its_fade_and_its_place()
        {
            ShadowPlane plane = Plane(Lamp(new Vector3(2f, 3f, 1f)), Child("Crate_contact", new Vector3(0.1f, 0.4f, -0.2f)));
            plane.ApplyNow();
            float fade = plane.Fade;
            Vector3 local = plane.transform.localPosition;
            Assert.That(fade, Is.GreaterThan(0.05f).And.LessThan(0.95f), "a fade that every term shapes");

            _root.transform.SetPositionAndRotation(new Vector3(4f, 1f, -3f), Quaternion.Euler(0f, 70f, 0f));
            _root.transform.localScale = Vector3.one * 2.5f;
            plane.ApplyNow();
            Assert.That(plane.Fade, Is.EqualTo(fade).Within(1e-5f), "the fade is the space's: a scaled, turned instance keeps it");
            Assert.That(Vector3.Distance(plane.transform.localPosition, local), Is.LessThan(1e-4f), "...and its place in it");
        }

        [Test]
        public void A_plane_on_unitys_quad_with_a_y_up_contact_keeps_a_level_footprint()
        {
            var go = Child("Quad_shadow", Vector3.zero).gameObject;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            go.AddComponent<MeshRenderer>();
            var plane = go.AddComponent<ShadowPlane>();
            plane.contact = Child("Quad_contact", Vector3.zero);
            plane.source = Lamp(new Vector3(2f, 3f, 1f));
            plane.BindSpace(_root.transform);
            plane.HorizonAxes(out _, out Vector3 a, out Vector3 b, out Vector3 up, out _, out _, out _, out _);
            Assert.That(Vector3.Dot(up, Vector3.up), Is.EqualTo(1f).Within(1e-5f), "its map's up is its contact's +Y");
            Assert.That(Vector3.Dot(a, Vector3.right), Is.EqualTo(1f).Within(1e-5f));
            plane.ApplyNow();
            Assert.That(Mathf.Abs(Vector3.Dot(plane.transform.forward, Vector3.up)), Is.EqualTo(1f).Within(1e-4f),
                        "the quad (in its XY) laid flat on the ground");
        }

        [Test]
        public void A_z_up_plane_reads_its_contacts_frame_on_its_plus_z()
        {
            // A Z-up producer's plane: its quad in its local XY, its contact's +Z up (the FBX
            // export turns only the roots upright).
            var quad = new Mesh
            {
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            quad.RecalculateBounds();
            try
            {
                var go = Child("Crate_shadow", Vector3.zero).gameObject;
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                go.AddComponent<MeshRenderer>();
                Transform contact = Child("Crate_contact", Vector3.zero);
                contact.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                Assert.That(Vector3.Dot(contact.forward, Vector3.up), Is.EqualTo(1f).Within(1e-5f), "its +Z up");
                var plane = go.AddComponent<ShadowPlane>();
                plane.contact = contact;
                plane.source = Lamp(new Vector3(2f, 3f, 1f));
                plane.radius = 0.5f;
                plane.height = 1f;
                plane.zUpFrames = true;
                plane.BindSpace(_root.transform);
                plane.HorizonAxes(out _, out Vector3 a, out Vector3 b, out Vector3 up, out _, out _, out _, out _);
                Assert.That(Vector3.Dot(up, Vector3.up), Is.EqualTo(1f).Within(1e-5f), "the map's up is the contact's +Z");
                Assert.That(Vector3.Dot(a, Vector3.right), Is.EqualTo(1f).Within(1e-5f), "Frame A along the contact's X");
                Assert.That(Mathf.Abs(Vector3.Dot(b, Vector3.forward)), Is.EqualTo(1f).Within(1e-5f), "Frame B level");
                plane.ApplyNow();
                Assert.That(Vector3.Dot(plane.transform.forward, Vector3.up), Is.EqualTo(1f).Within(1e-4f), "laid flat, its normal up");
            }
            finally
            {
                Object.DestroyImmediate(quad);
            }
        }

        [Test]
        public void With_follow_source_off_the_preview_leaves_the_transform_to_its_owner()
        {
            ShadowPlane plane = Plane(Lamp(new Vector3(2f, 3f, 1f)), Child("Crate_contact", Vector3.zero));
            plane.followSource = false;
            plane.transform.localPosition = new Vector3(1f, 0f, 1f);
            var editor = UnityEditor.Editor.CreateEditor(plane);
            try
            {
                Assert.That(ShadowPlanePreview.Showing(plane), Is.True, "shown: its fade follows");
                Assert.That(plane.transform.localPosition, Is.EqualTo(new Vector3(1f, 0f, 1f)), "not placed");
                plane.transform.localPosition = new Vector3(3f, 0f, 3f);  // the person's move
                ShadowPlanePreview.Show(new Object[] { plane });
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            Assert.That(plane.transform.localPosition, Is.EqualTo(new Vector3(3f, 0f, 3f)), "the move stays");
        }

        [Test]
        public void Turning_follow_source_off_mid_preview_gives_the_placement_back_at_once()
        {
            ShadowPlane plane = Plane(Lamp(new Vector3(2f, 3f, 1f)), Child("Crate_contact", Vector3.zero));
            plane.transform.localPosition = new Vector3(9f, 9f, 9f);
            var editor = UnityEditor.Editor.CreateEditor(plane);
            try
            {
                Assert.That(plane.transform.localPosition, Is.Not.EqualTo(new Vector3(9f, 9f, 9f)), "placed");
                plane.followSource = false;
                ShadowPlanePreview.Show(new Object[] { plane });  // as the inspector does after an edit
                Assert.That(plane.transform.localPosition, Is.EqualTo(new Vector3(9f, 9f, 9f)), "its own transform, at once");
                plane.transform.localPosition = new Vector3(3f, 0f, 3f);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            Assert.That(plane.transform.localPosition, Is.EqualTo(new Vector3(3f, 0f, 3f)), "a move after that stays");
        }

        [Test]
        public void The_preview_gives_the_placement_back_as_play_starts_and_before_a_reload()
        {
            ShadowPlane plane = Plane(Lamp(new Vector3(2f, 3f, 1f)), Child("Crate_contact", Vector3.zero));
            plane.transform.localPosition = new Vector3(9f, 9f, 9f);
            var editor = UnityEditor.Editor.CreateEditor(plane);
            try
            {
                Assert.That(ShadowPlanePreview.Showing(plane), Is.True);
                ShadowPlanePreview.OnPlayMode(PlayModeStateChange.ExitingEditMode);
                Assert.That(ShadowPlanePreview.Showing(plane), Is.False, "play mode: given back");
                Assert.That(plane.transform.localPosition, Is.EqualTo(new Vector3(9f, 9f, 9f)));

                ShadowPlanePreview.Show(new Object[] { plane });
                Assert.That(plane.transform.localPosition, Is.Not.EqualTo(new Vector3(9f, 9f, 9f)));
                ShadowPlanePreview.ReleaseAll();  // AssemblyReloadEvents.beforeAssemblyReload's handler
                Assert.That(ShadowPlanePreview.Showing(plane), Is.False, "a reload: given back");
                Assert.That(plane.transform.localPosition, Is.EqualTo(new Vector3(9f, 9f, 9f)));
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void The_inspectors_preview_places_the_plane_and_gives_the_placement_back()
        {
            ShadowPlane plane = Plane(Lamp(new Vector3(2f, 3f, 1f)), Child("Crate_contact", Vector3.zero));
            plane.transform.localPosition = new Vector3(9f, 9f, 9f);
            var editor = UnityEditor.Editor.CreateEditor(plane);
            try
            {
                Assert.That(ShadowPlanePreview.Showing(plane), Is.True, "the open inspector shows it");
                Assert.That(plane.transform.localPosition, Is.Not.EqualTo(new Vector3(9f, 9f, 9f)), "placed from its source");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
            Assert.That(ShadowPlanePreview.Showing(plane), Is.False, "closed: given back");
            Assert.That(plane.transform.localPosition, Is.EqualTo(new Vector3(9f, 9f, 9f)), "its transform as it was");
        }
    }
}
