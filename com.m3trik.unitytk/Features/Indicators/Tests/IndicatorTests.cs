using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Indicators.Tests
{
    /// <summary><see cref="Indicator"/>: an arrow over its target's top, bobbing, shown and hidden.</summary>
    public class IndicatorTests
    {
        GameObject _target;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_target);

        [Test]
        public void A_marker_shows_its_targets_own_disc_else_a_ring_in_metres_however_scaled()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cylinder);  // a delivery's destination disc
            var marker = new GameObject("Marker").AddComponent<Marker>();
            marker.transform.SetParent(_target.transform, false);
            marker.ownMesh = true;  // asked to show the disc itself
            marker.Hide();  // what a marker off does from the scene's start
            Assert.That(_target.GetComponent<MeshRenderer>().enabled, Is.False);
            marker.Show();
            Assert.That(_target.GetComponent<MeshRenderer>().enabled, Is.True, "the disc itself shows");
            Assert.That(marker.ring, Is.Null, "no ring made");

            var view = new GameObject("VIEW");
            view.transform.SetParent(_target.transform, false);
            view.transform.localScale = Vector3.one * 6f;  // a viewpoint scaled with its disc
            var ringed = new GameObject("Marker").AddComponent<Marker>();
            ringed.transform.SetParent(view.transform, false);
            ringed.Show();
            Assert.That(ringed.ring.lossyScale.x, Is.EqualTo(ringed.radius).Within(1e-3f), "its radius in metres");
            ringed.Hide();
            Assert.That(ringed.ring.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void Two_cues_on_one_target_place_themselves_by_the_target_alone_never_by_each_others_parts()
        {
            // Each counted the other's arrow (and a marker's ring) as the target's: they climbed over each other.
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);  // its top at 0.5
            Indicator Over(string name)
            {
                var made = new GameObject(name).AddComponent<Indicator>();
                made.transform.SetParent(_target.transform, false);
                made.bob = 0f;
                return made;
            }
            Indicator first = Over("Indicator"), second = Over("Indicator 2");
            var marker = new GameObject("Marker").AddComponent<Marker>();
            marker.transform.SetParent(_target.transform, false);
            marker.Show();
            first.Show();
            second.Show();
            first.Show();  // shown again with the second's arrow over the target
            float top = 0.5f + first.height;
            Assert.That(first.TipAt(0f).y, Is.EqualTo(top).Within(1e-4f), "over the target's top, not the second's arrow");
            Assert.That(second.TipAt(0f).y, Is.EqualTo(top).Within(1e-4f));
            Assert.That(marker.Spot().y, Is.EqualTo(-0.5f).Within(1e-4f), "the ring at the target's foot, not under the arrows");
        }

        [Test]
        public void A_flat_arrow_points_at_its_target_and_turns_its_face_to_the_eyes_wherever_they_are()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var eyes = new GameObject("eyes").transform;
            eyes.SetParent(_target.transform, false);
            var indicator = new GameObject("Indicator").AddComponent<Indicator>();
            indicator.transform.SetParent(_target.transform, false);
            indicator.eyes = eyes;
            eyes.position = new Vector3(3f, 1.6f, 0f);
            indicator.Show();
            Transform arrow = indicator.arrow;
            Assert.That(arrow.GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("Indicator Flat Arrow"), "flat by default");
            Assert.That(Vector3.Angle(arrow.up, Vector3.up), Is.LessThan(0.01f), "still pointing down at the target");
            Vector3 across = Vector3.ProjectOnPlane(eyes.position - arrow.position, Vector3.up).normalized;
            Assert.That(Vector3.Angle(arrow.forward, across), Is.LessThan(0.01f), "its face to the eyes");
            eyes.position = new Vector3(-1f, 1.6f, -3f);  // the user walks round it
            indicator.Place(0.2f);
            across = Vector3.ProjectOnPlane(eyes.position - arrow.position, Vector3.up).normalized;
            Assert.That(Vector3.Angle(arrow.forward, across), Is.LessThan(0.01f), "turned with them");
            Assert.That(Vector3.Angle(arrow.up, Vector3.up), Is.LessThan(0.01f));
            eyes.position = arrow.position + new Vector3(0.05f, 2f, 0f);  // looking almost straight down its axis
            indicator.Place(0.2f);
            Assert.That(Vector3.Angle(arrow.forward, eyes.position - arrow.position), Is.LessThan(0.01f),
                        "seen end-on, it faces the eyes whole rather than show a sliver");
            // A head moving across where it once snapped from one to the other (about 15 degrees off its axis): it turns a little.
            Quaternion Seen(float degrees)
            {
                eyes.position = arrow.position + Quaternion.AngleAxis(degrees, Vector3.forward) * Vector3.up * 2f;
                indicator.Place(0.2f);
                return arrow.rotation;
            }
            Assert.That(Quaternion.Angle(Seen(15.1f), Seen(15.6f)), Is.LessThan(8f), "no snap (some 75 degrees once) as the eyes move half a degree");
            indicator.Hide();

            var solid = new GameObject("Solid").AddComponent<Indicator>();
            solid.transform.SetParent(_target.transform, false);
            solid.flat = false;
            solid.Show();
            Assert.That(solid.arrow.GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("Indicator Arrow"), "a solid one when asked");
        }

        [Test]
        public void An_arrow_it_made_flat_or_solid_takes_its_meshes_and_materials_with_it()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foreach (bool flat in new[] { true, false })
            {
                var indicator = new GameObject("Indicator").AddComponent<Indicator>();
                indicator.transform.SetParent(_target.transform, false);
                indicator.flat = flat;
                indicator.Show();
                var made = new System.Collections.Generic.List<Object>();
                foreach (MeshFilter mesh in indicator.arrow.GetComponentsInChildren<MeshFilter>(true)) made.Add(mesh.sharedMesh);
                foreach (MeshRenderer shown in indicator.arrow.GetComponentsInChildren<MeshRenderer>(true)) made.Add(shown.sharedMaterial);
                Assert.That(made.Count, Is.EqualTo(flat ? 4 : 2), "the arrow's mesh and material (and its outline's)");
                // What Unity calls as it goes (Edit Mode calls none of it).
                typeof(Indicator).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                 .Invoke(indicator, null);
                Assert.That(made.TrueForAll(each => each == null), Is.True, "each destroyed with it");
            }
        }

        [Test]
        public void The_arrow_it_makes_is_its_size_in_metres_however_the_target_is_scaled()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _target.transform.localScale = Vector3.one * 0.01f;  // a delivery in centimetres
            var indicator = new GameObject("Indicator").AddComponent<Indicator>();
            indicator.transform.SetParent(_target.transform, false);
            indicator.size = 0.26f;  // twice the default
            indicator.Show();
            Assert.That(indicator.arrow.lossyScale.x, Is.EqualTo(0.26f / Indicator.DefaultSize).Within(1e-4f), "its size, not the target's scale");
            Assert.That(indicator.arrow.lossyScale.y, Is.EqualTo(0.26f / Indicator.DefaultSize).Within(1e-4f));
        }

        [Test]
        public void The_arrow_hovers_over_the_targets_top_and_bobs()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);  // one metre, centred on the origin
            var indicator = new GameObject("Indicator").AddComponent<Indicator>();
            indicator.transform.SetParent(_target.transform, false);
            indicator.Show();
            Assert.That(indicator.arrow, Is.Not.Null, "made: a cone");
            Assert.That(indicator.arrow.gameObject.activeSelf);
            Vector3 low = indicator.TipAt(0.75f / indicator.rate);
            Vector3 high = indicator.TipAt(0.25f / indicator.rate);
            Assert.That(low.y, Is.EqualTo(0.5f + indicator.height).Within(1e-4), "the tip just over the cube's top");
            Assert.That(high.y - low.y, Is.EqualTo(indicator.bob).Within(1e-4), "it bobs its height");
            Assert.That(new Vector2(low.x, low.z).magnitude, Is.LessThan(1e-4), "over the centre");
            indicator.Hide();
            Assert.That(indicator.arrow.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void A_placed_arrow_shows_where_it_stands_bobs_along_its_own_axis_and_rides_its_part()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var authored = new GameObject("DOOR_ARROW").transform;
            authored.SetParent(_target.transform, false);
            authored.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 0f, 90f));
            authored.gameObject.SetActive(false);
            var indicator = new GameObject("Indicator").AddComponent<Indicator>();
            indicator.transform.SetParent(_target.transform, false);
            indicator.arrow = authored;
            indicator.follow = false;

            indicator.Show();
            Assert.That(authored.gameObject.activeSelf, "shown");
            indicator.Place(0.75f / indicator.rate);
            Assert.That(authored.position, Is.EqualTo(new Vector3(1f, 2f, 3f)).Using(UnityEngine.TestTools.Utils.Vector3EqualityComparer.Instance), "at its low, where it was authored");
            indicator.Place(0.25f / indicator.rate);
            Assert.That(authored.position - new Vector3(1f, 2f, 3f), Is.EqualTo(authored.up * indicator.bob)
                        .Using(UnityEngine.TestTools.Utils.Vector3EqualityComparer.Instance), "it bobs its height along its own axis (it points sideways)");
            _target.transform.position += Vector3.forward;  // the part moves (a door opens) while it shows
            indicator.Place(0.75f / indicator.rate);
            Assert.That(authored.position, Is.EqualTo(new Vector3(1f, 2f, 4f)).Using(UnityEngine.TestTools.Utils.Vector3EqualityComparer.Instance), "it rides the part");
            _target.transform.position -= Vector3.forward;
            Assert.That(authored.rotation.eulerAngles.z, Is.EqualTo(90f).Within(1e-3), "turned as authored");
            indicator.Hide();
            Assert.That(authored.gameObject.activeSelf, Is.False);
            Assert.That(authored.position, Is.EqualTo(new Vector3(1f, 2f, 3f)).Using(UnityEngine.TestTools.Utils.Vector3EqualityComparer.Instance), "back where it was authored");
        }

        [Test]
        public void A_marker_rings_its_targets_foot_leaving_its_mesh_alone_unless_asked_to_show_it()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);  // a part, not a destination disc
            var marker = new GameObject("Marker").AddComponent<Marker>();
            marker.transform.SetParent(_target.transform, false);
            marker.Hide();
            Assert.That(_target.GetComponent<MeshRenderer>().enabled, Is.True, "a marker off hides nothing of what it marks");
            marker.Show();
            Assert.That(marker.ring != null && marker.ring.gameObject.activeSelf, Is.True, "a ring at its foot");
            Assert.That(_target.GetComponent<MeshRenderer>().enabled, Is.True);
        }

        [Test]
        public void The_ring_and_the_arrows_outline_wear_the_colours_and_shape_asked()
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var marker = new GameObject("Marker").AddComponent<Marker>();
            marker.transform.SetParent(_target.transform, false);
            marker.color = Color.green;
            marker.lift = 0.02f;
            marker.sides = 12;
            marker.Show();
            Assert.That(marker.ring.GetComponent<MeshRenderer>().sharedMaterial.color, Is.EqualTo(Color.green));
            Assert.That(marker.ring.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(24), "its sides, an outer and an inner point each");
            Assert.That(marker.ring.position.y, Is.EqualTo(-0.5f + 0.02f).Within(1e-4f), "lifted off the floor as asked");

            var indicator = new GameObject("Indicator").AddComponent<Indicator>();
            indicator.transform.SetParent(_target.transform, false);
            indicator.outlineColor = Color.red;
            indicator.Show();
            Assert.That(indicator.arrow.Find("Outline").GetComponent<MeshRenderer>().sharedMaterial.color, Is.EqualTo(Color.red));
        }
    }
}
