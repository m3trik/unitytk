using System;
using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Placement
{
    /// <summary>
    /// Colliders for a placement search, while it runs: each shown mesh under the roots that has
    /// no collider switched on gets a mesh collider of its own (a part whose own colliders wait for
    /// its step blocks the view all the same), so walls, cabinets and tables block the view
    /// and stand in the way (<see cref="Placer"/>); disposing takes them away again. A delivery's
    /// meshes rarely carry colliders, and a search needs none to stay. In a player, a mesh whose data
    /// is not readable is passed over (<see cref="Skipped"/>): Unity cooks a mesh collider only from a
    /// readable mesh there. A solid collider under the roots on an object that shows nothing (a
    /// helper's click box, a bone's) is switched off for the search, and on again after: it blocks no
    /// view, and since a build adds such boxes as it goes, a search that saw them found other spots
    /// on the next build.
    /// </summary>
    public sealed class Occluders : IDisposable
    {
        readonly List<Collider> _made = new List<Collider>();
        readonly List<Collider> _unseen = new List<Collider>();

        /// <summary>Give each shown mesh under <paramref name="roots"/> without a collider one, for now.</summary>
        /// <param name="roots">The scene's parts (a delivery's roots).</param>
        /// <param name="readableOnly">Pass over a mesh whose data a player cannot read (its import's Read/Write off: a mesh
        /// collider cannot be cooked from it there), counted in <see cref="Skipped"/>; null: in a player only (the editor
        /// reads every mesh).</param>
        public Occluders(IEnumerable<GameObject> roots, bool? readableOnly = null)
        {
            bool readable = readableOnly ?? !Application.isEditor;
            foreach (GameObject root in roots)
            {
                if (root == null) continue;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null || Collides(filter.gameObject)) continue;
                    if (!filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled) continue;
                    if (readable && !filter.sharedMesh.isReadable)
                    {
                        Skipped++;
                        continue;
                    }
                    var made = filter.gameObject.AddComponent<MeshCollider>();
                    made.sharedMesh = filter.sharedMesh;
                    _made.Add(made);
                }
                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.isTrigger || (collider.TryGetComponent(out Renderer shown) && shown.enabled)) continue;
                    collider.enabled = false;
                    _unseen.Add(collider);
                }
            }
            Physics.SyncTransforms();
        }

        static bool Collides(GameObject part)
        {
            foreach (Collider collider in part.GetComponents<Collider>())
                if (collider.enabled) return true;
            return false;
        }

        /// <summary>How many colliders it added.</summary>
        public int Count => _made.Count;

        /// <summary>How many shown meshes it passed over, their data unreadable where it runs (they block nothing).</summary>
        public int Skipped { get; private set; }

        /// <summary>Take the colliders it added away.</summary>
        public void Dispose()
        {
            foreach (Collider made in _made)
            {
                if (made == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(made);
                else UnityEngine.Object.DestroyImmediate(made);
            }
            _made.Clear();
            foreach (Collider unseen in _unseen)
                if (unseen != null) unseen.enabled = true;
            _unseen.Clear();
            Physics.SyncTransforms();
        }
    }
}
