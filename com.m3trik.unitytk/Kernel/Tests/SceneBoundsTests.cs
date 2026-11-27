using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// The one renderer-bounds union: each variant a parameter (switched-off renderers, inactive objects,
    /// a branch left out, a fallback size), the per-frame list form, and the meshes' box in a space.
    /// </summary>
    public class SceneBoundsTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("part");

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        GameObject Cube(string name, Vector3 at, Transform parent = null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent != null ? parent : _root.transform, false);
            cube.transform.localPosition = at;
            return cube;
        }

        [Test]
        public void The_union_covers_every_drawing_renderer_and_each_variant_is_a_parameter()
        {
            Cube("a", Vector3.zero);
            GameObject off = Cube("off", new Vector3(4f, 0f, 0f));
            off.GetComponent<Renderer>().enabled = false;
            GameObject hidden = Cube("hidden", new Vector3(-4f, 0f, 0f));
            hidden.SetActive(false);

            Assert.That(SceneBounds.TryOf(_root.transform, out Bounds seen), Is.True);
            Assert.That(seen.min.x, Is.EqualTo(-0.5f).Within(1e-4f), "the switched-off and the inactive left out");
            Assert.That(seen.max.x, Is.EqualTo(0.5f).Within(1e-4f));

            SceneBounds.TryOf(_root.transform, out Bounds all, includeDisabled: true);
            Assert.That(all.max.x, Is.EqualTo(4.5f).Within(1e-4f), "the switched-off one counted");
            SceneBounds.TryOf(_root.transform, out Bounds every, includeDisabled: true, includeInactive: true);
            Assert.That(every.min.x, Is.EqualTo(-4.5f).Within(1e-4f), "the inactive one counted");

            SceneBounds.TryOf(_root.transform, out Bounds skipped, includeDisabled: true, skip: t => t.name == "off");
            Assert.That(skipped.max.x, Is.EqualTo(0.5f).Within(1e-4f), "a branch left out");
        }

        [Test]
        public void A_part_that_shows_nothing_falls_back_to_its_position()
        {
            _root.transform.position = new Vector3(1f, 2f, 3f);
            Assert.That(SceneBounds.TryOf(_root.transform, out _), Is.False);
            Assert.That(SceneBounds.TryOf((Transform)null, out _), Is.False);
            Bounds fallback = SceneBounds.Of(_root.transform, 0.1f);
            Assert.That(fallback.center, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(fallback.size.x, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(SceneBounds.Of(_root.transform).size, Is.EqualTo(Vector3.zero), "a point by default");
        }

        [Test]
        public void A_kept_list_unions_alike_its_nulls_left_out()
        {
            var renderers = new List<Renderer> { Cube("a", Vector3.zero).GetComponent<Renderer>(), null,
                                                 Cube("b", new Vector3(0f, 3f, 0f)).GetComponent<Renderer>() };
            Assert.That(SceneBounds.TryOf(renderers, out Bounds seen), Is.True);
            Assert.That(seen.max.y, Is.EqualTo(3.5f).Within(1e-4f));
            renderers[2].enabled = false;
            SceneBounds.TryOf(renderers, out seen);
            Assert.That(seen.max.y, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(SceneBounds.TryOf((IReadOnlyList<Renderer>)null, out _), Is.False);
        }

        [Test]
        public void The_meshes_box_is_in_the_space_asked_corners_or_every_vertex()
        {
            _root.transform.position = new Vector3(10f, 0f, 0f);
            GameObject cube = Cube("a", new Vector3(1f, 0f, 0f));
            cube.SetActive(false);  // meshes count on inactive objects too
            Assert.That(SceneBounds.TryOfMeshes(_root.transform, out Bounds local, _root.transform), Is.True);
            Assert.That(local.center, Is.EqualTo(new Vector3(1f, 0f, 0f)));
            SceneBounds.TryOfMeshes(_root.transform, out Bounds world);
            Assert.That(world.center.x, Is.EqualTo(11f).Within(1e-4f));

            cube.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            SceneBounds.TryOfMeshes(_root.transform, out Bounds corners, _root.transform);
            Mesh mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            SceneBounds.TryOfMeshes(new[] { (mesh, Matrix4x4.TRS(new Vector3(1f, 0f, 0f), cube.transform.localRotation, Vector3.one)) },
                                    out Bounds tight, everyVertex: true);
            Assert.That(tight.size.x, Is.EqualTo(Mathf.Sqrt(2f)).Within(1e-3f), "every vertex: the turned cube's own width");
            Assert.That(corners.size.x, Is.EqualTo(Mathf.Sqrt(2f)).Within(1e-3f), "a cube's corners are its vertices");
        }
    }
}
