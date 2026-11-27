using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Placement.Tests
{
    /// <summary>
    /// <see cref="Occluders"/>: meshes with no collider switched on get one for a search, taken away after.
    /// </summary>
    public class OccludersTests : RoomFixture
    {
        [Test]
        public void Occluders_give_meshes_colliders_for_the_search_and_take_them_away()
        {
            var bare = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bare.transform.SetParent(Room.transform);
            Object.DestroyImmediate(bare.GetComponent<Collider>());
            Part.GetComponent<Collider>().enabled = false;  // a part whose colliders wait for its step
            using (var occluders = new Occluders(new[] { Room }))
            {
                Assert.That(occluders.Count, Is.EqualTo(2), "the mesh with none, and the one whose collider is off");
                Assert.That(bare.GetComponent<MeshCollider>(), Is.Not.Null);
            }
            Assert.That(bare.GetComponent<Collider>() == null, "gone after");
        }

        [Test]
        public void A_collider_that_shows_nothing_blocks_no_search_and_is_back_after()
        {
            // A helper's click box (a slot an artist placed) and a bone's box show nothing: built by a later row,
            // they moved an earlier row's arrow on the next build (a real task: three arrows by the slot).
            var helper = new GameObject("SLOT");
            helper.transform.SetParent(Room.transform);
            helper.transform.position = new Vector3(0f, 1f, 3f);
            var box = helper.AddComponent<BoxCollider>();
            var ray = new Ray(new Vector3(2f, 1f, 3f), Vector3.left);
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(ray, out RaycastHit before, 10f) && before.collider == box, "it is found by a click's ray");
            using (new Occluders(new[] { Room }))
                Assert.That(Physics.Raycast(ray, out RaycastHit during, 10f) && during.collider == box, Is.False, "the search sees through it");
            Assert.That(box.enabled, "it is back for the clicks after");
        }

        [Test]
        public void Occluders_pass_over_a_mesh_a_player_cannot_read_where_asked()
        {
            // A player cooks a mesh collider only from a readable mesh: one whose data stays on the GPU is passed over.
            var bare = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bare.transform.SetParent(Room.transform);
            Object.DestroyImmediate(bare.GetComponent<Collider>());
            Mesh sealedMesh = Object.Instantiate(bare.GetComponent<MeshFilter>().sharedMesh);
            sealedMesh.UploadMeshData(true);  // no longer readable
            bare.GetComponent<MeshFilter>().sharedMesh = sealedMesh;
            try
            {
                using (var occluders = new Occluders(new[] { bare }, readableOnly: true))
                {
                    Assert.That((occluders.Count, occluders.Skipped), Is.EqualTo((0, 1)));
                    Assert.That(bare.GetComponent<MeshCollider>() == null, "none made from it");
                }
                using (var occluders = new Occluders(new[] { bare }, readableOnly: false))
                    Assert.That(occluders.Count, Is.EqualTo(1), "the editor reads it: it blocks the view there");
            }
            finally
            {
                Object.DestroyImmediate(sealedMesh);
            }
        }
    }
}
