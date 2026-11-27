using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Indicators
{
    /// <summary>
    /// A ring on the floor where its target stands, pulsing: where the user goes next (a viewpoint, a
    /// spot to stand). It shows while enabled (a step turns it on) and hides when disabled. Its ring is
    /// the object given as <see cref="ring"/>; else, where asked (<see cref="ownMesh"/>), the target's own
    /// mesh (a destination disc: hidden while the marker is off, from the scene's start); else a plain
    /// flat ring it makes the first time it shows, in its <see cref="color"/>, its size in metres however
    /// its target is scaled.
    /// </summary>
    /// <remarks>Prior art: as <see cref="Indicator"/>'s; a teleport anchor's own visual (the XR
    /// Interaction Toolkit's) marks every place one may go, not the one a step asks for.</remarks>
    [AddComponentMenu("unitytk/Indicators/Marker")]
    [DisallowMultipleComponent]
    public sealed class Marker : MonoBehaviour
    {
        [Tooltip("Where it marks (its object's parent when empty): a viewpoint, or an object, whose foot it rings.")]
        public Transform target;

        [Tooltip("The ring it shows, lying flat, its centre at its origin (the target's own mesh, else a plain ring it makes, when empty).")]
        public Transform ring;

        [Tooltip("Show the target's own mesh (a destination disc) in place of a ring, where it has one and no ring is given; " +
                 "the mesh is then hidden while the marker is off. Off: a ring at its foot, its mesh left as it is.")]
        public bool ownMesh;

        [Tooltip("The ring's radius, in metres.")]
        public float radius = 0.35f;

        [Tooltip("The width of the ring it makes, as a share of its radius.")]
        [Range(0.01f, 1f)]
        public float width = 0.2f;

        [Tooltip("How many sides the ring it makes has.")]
        [Min(3)]
        public int sides = 48;

        [Tooltip("How far above the floor the ring lies, in metres: clear of the floor's own surface.")]
        [Min(0f)]
        public float lift = 0.005f;

        [Tooltip("The colour of the ring it makes (a ring given, or the target's own mesh, keeps its own).")]
        public Color color = Color.white;

        [Tooltip("How much it swells and shrinks, a fraction of its size.")]
        public float pulse = 0.08f;

        [Tooltip("Pulses a second.")]
        public float rate = 1f;

        [Tooltip("Pulse in real time, unaffected by the time scale (a paused or slowed scene).")]
        public bool unscaledTime;

        readonly List<Renderer> _renderers = new List<Renderer>();
        float _clock;
        Mesh _mesh;          // the ring it made: its mesh and material are its own, destroyed with it
        Material _material;

        /// <summary>Where it marks.</summary>
        public Transform Target => target != null ? target : transform.parent;

        /// <summary>The target's own mesh it shows in place of a ring; null where it rings the target.</summary>
        public Renderer OwnMesh => ring == null && ownMesh && Target != null && Target.TryGetComponent(out MeshRenderer mesh) ? mesh : null;

        /// <summary>Show the ring at the target's foot, or the target's own mesh (what enabling it does).</summary>
        public void Show()
        {
            Renderer own = OwnMesh;
            if (own != null)
            {
                own.enabled = true;
                return;
            }
            SceneBounds.Renderers(Target, _renderers, includeInactive: true, skip: Indicator.CuesOn(Target, ring));
            if (ring == null) ring = MakeRing();
            ring.gameObject.SetActive(true);
            _clock = 0f;
            Place(0f);
        }

        /// <summary>Hide the ring, or the target's own mesh (what disabling it does).</summary>
        public void Hide()
        {
            Renderer own = OwnMesh;
            if (own != null) own.enabled = false;
            if (ring != null) ring.gameObject.SetActive(false);
        }

        /// <summary>
        /// Where the ring lies: under the centre of the target's renderers, at their bottom; the
        /// target's own position where it has none (a viewpoint stands on the floor).
        /// </summary>
        public Vector3 Spot()
        {
            Vector3 spot = Target == null ? transform.position : Target.position;
            return SceneBounds.TryOf(_renderers, out Bounds bounds) ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) : spot;
        }

        /// <summary>Put the ring where it is at <paramref name="clock"/> seconds (a player does each frame).</summary>
        /// <param name="clock">Seconds since it showed.</param>
        public void Place(float clock)
        {
            if (ring == null || OwnMesh != null) return;
            ring.SetPositionAndRotation(Spot() + Vector3.up * lift, Quaternion.identity);
            float size = radius * (1f + pulse * Mathf.Sin(clock * rate * 2f * Mathf.PI));
            // In metres, whatever scale its parents carry (a viewpoint's disc is often scaled).
            Vector3 parent = ring.parent != null ? ring.parent.lossyScale : Vector3.one;
            ring.localScale = new Vector3(size / NonZero(parent.x), 1f / NonZero(parent.y), size / NonZero(parent.z));
        }

        static float NonZero(float value) => Mathf.Abs(value) < 1e-6f ? 1f : value;

        // From the scene's start, a marker off hides the target's own mesh it shows.
        void Awake()
        {
            if (!enabled) Hide();
        }

        void OnEnable() => Show();

        void OnDisable() => Hide();

        void LateUpdate()
        {
            _clock += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            Place(_clock);
        }

        void OnDestroy()
        {
            foreach (Object made in new Object[] { _mesh, _material })
            {
                if (made == null) continue;
                if (Application.isPlaying) Destroy(made);
                else DestroyImmediate(made);
            }
        }

        // A flat ring of radius 1 (scaled to the radius), its width a share of it, facing up, in its colour (a plain
        // material on the pipeline's Lit).
        Transform MakeRing()
        {
            int count = Mathf.Max(3, sides);
            float inner = 1f - Mathf.Clamp(width, 0.01f, 1f);
            var vertices = new Vector3[count * 2];
            var triangles = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                float a = i * 2f * Mathf.PI / count;
                var round = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i * 2] = round;
                vertices[i * 2 + 1] = round * inner;
                int next = (i + 1) % count;
                triangles[i * 6] = i * 2;
                triangles[i * 6 + 1] = i * 2 + 1;
                triangles[i * 6 + 2] = next * 2;
                triangles[i * 6 + 3] = next * 2;
                triangles[i * 6 + 4] = i * 2 + 1;
                triangles[i * 6 + 5] = next * 2 + 1;
            }
            _mesh = new Mesh { name = "Marker Ring", vertices = vertices, triangles = triangles };
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            var made = new GameObject("Ring");
            made.transform.SetParent(transform, false);
            made.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _material = PlainMaterial.Of("Marker Ring", color);
            made.AddComponent<MeshRenderer>().sharedMaterial = _material;
            return made.transform;
        }
    }
}
