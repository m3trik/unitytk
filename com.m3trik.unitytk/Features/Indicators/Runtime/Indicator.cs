using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Indicators
{
    /// <summary>
    /// An arrow hovering over its target, pointing down at it and bobbing: what to act on next.
    /// It shows while enabled (a step enables it as it runs) and hides when disabled. Its arrow is the object given as <see cref="arrow"/>
    /// (one a delivery authored), or one it makes the first time it shows (a flat arrow turning about
    /// its own axis to face the eyes, or a solid one: <see cref="flat"/>); an authored
    /// arrow with <see cref="follow"/> off stays where it was placed (riding what it sits under), bobbing
    /// along its own axis: one a build placed beside the target, pointing at it from where the user sees it.
    /// </summary>
    /// <remarks>
    /// Prior art: XR Interaction Toolkit's affordance visuals tint what is hovered, not what a
    /// step asks for, and are deprecated since its 3.0; a pointer above the target's bounds is
    /// what step-by-step guidance draws, so it is drawn here, from the target's renderers,
    /// with no input and no render pipeline of its own.
    /// </remarks>
    [AddComponentMenu("unitytk/Indicators/Indicator")]
    [DisallowMultipleComponent]
    public sealed class Indicator : MonoBehaviour
    {
        [Tooltip("What it points at (its object's parent when empty).")]
        public Transform target;

        [Tooltip("The arrow it moves, its tip at its origin pointing down (one it makes when empty: flat, or solid).")]
        public Transform arrow;

        [Tooltip("How far above the target's top the tip hovers, in metres.")]
        public float height = 0.05f;

        [Tooltip("How far it bobs up and down, in metres.")]
        public float bob = 0.03f;

        [Tooltip("Bobs a second.")]
        public float rate = 1.5f;

        /// <summary>The <see cref="color"/> of an indicator just added: a clear blue that reads against most scenes.</summary>
        public static Color DefaultColor => new Color(0.1f, 0.45f, 1f);

        [Tooltip("The colour of the arrow it makes (an authored arrow keeps its own).")]
        public Color color = DefaultColor;

        [Tooltip("How brightly the arrow it makes glows: its colour's emission, times this (over 1 stays bright under a filmic tone map).")]
        [Min(0f)]
        public float glow = 2f;

        /// <summary>The <see cref="outlineColor"/> of an indicator just added: a near black that sets the arrow off against a bright scene.</summary>
        public static Color DefaultOutlineColor => new Color(0.02f, 0.03f, 0.05f);

        [Tooltip("The colour of the thin outline just behind the flat arrow it makes.")]
        public Color outlineColor = DefaultOutlineColor;

        [Tooltip("Moves the arrow over the target's top; off, the arrow stays where it was placed (riding what it sits under), bobbing along its own axis.")]
        public bool follow = true;

        /// <summary>The <see cref="size"/> of an indicator just added, in metres.</summary>
        public const float DefaultSize = 0.13f;

        [Tooltip("How long the arrow it makes is, tip to tail, in metres, however the target is scaled.")]
        [Min(0.01f)]
        public float size = DefaultSize;

        [Tooltip("Make a flat arrow that turns about its own axis to face the eyes, wherever they are (a solid one when off).")]
        public bool flat = true;

        [Tooltip("The eyes a flat arrow faces (the main camera when empty).")]
        public Transform eyes;

        [Tooltip("Bob in real time, unaffected by the time scale (a paused or slowed scene).")]
        public bool unscaledTime;

        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<Object> _owned = new List<Object>();  // the meshes and materials of the arrow it made, destroyed with it
        bool _made;          // the arrow is its own (a made one turns to face the eyes; an authored one keeps its turn)
        Quaternion _turn = Quaternion.identity;  // how an arrow that does not follow was authored (in its parent)
        float _clock;
        Vector3 _authored;  // where an arrow that does not follow was authored (in its parent), while it shows

        /// <summary>What it points at.</summary>
        public Transform Target => target != null ? target : transform.parent;

        /// <summary>Show the arrow over the target (what enabling it does).</summary>
        public void Show()
        {
            SceneBounds.Renderers(Target, _renderers, includeInactive: true, skip: CuesOn(Target, arrow));
            if (arrow == null)
            {
                arrow = flat ? MakeFlatArrow() : MakeArrow();
                _made = true;
            }
            if (_made)
            {
                // Its size in metres, whatever scale the target (a delivery in centimetres) gives it.
                Vector3 parent = transform.lossyScale;
                float s = size / DefaultSize;
                arrow.localScale = new Vector3(s / Safe(parent.x), s / Safe(parent.y), s / Safe(parent.z));
            }
            _authored = arrow.localPosition;
            _turn = arrow.localRotation;
            arrow.gameObject.SetActive(true);
            _clock = 0f;
            Place(0f);
        }

        /// <summary>Hide the arrow (what disabling it does).</summary>
        public void Hide()
        {
            if (arrow == null) return;
            if (!follow) arrow.SetLocalPositionAndRotation(_authored, _turn);
            arrow.gameObject.SetActive(false);
        }

        /// <summary>Where the tip sits at <paramref name="clock"/> seconds: over the target's top, bobbing.</summary>
        /// <param name="clock">Seconds since it showed.</param>
        public Vector3 TipAt(float clock)
        {
            Vector3 top = Target == null ? transform.position : Target.position;
            if (SceneBounds.TryOf(_renderers, out Bounds bounds)) top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            return top + Vector3.up * (height + Bob(clock));
        }

        // How far over its low the arrow is at clock seconds: 0 to bob and back, rate times a second.
        float Bob(float clock) => bob * 0.5f * (1f + Mathf.Sin(clock * rate * 2f * Mathf.PI));

        /// <summary>Put the arrow where it is at <paramref name="clock"/> seconds (a player does each frame).</summary>
        /// <param name="clock">Seconds since it showed.</param>
        public void Place(float clock)
        {
            if (arrow == null) return;
            if (follow) arrow.SetPositionAndRotation(TipAt(clock), Quaternion.identity);
            else arrow.position = (arrow.parent != null ? arrow.parent.TransformPoint(_authored) : _authored) + arrow.up * Bob(clock);
            if (_made && flat) Face();
        }

        /// <summary>The eyes a flat arrow faces: the ones named, else the main camera's; null when there are none.</summary>
        public Transform Eyes => Viewer.Eyes(eyes);

        // Seen this near end-on (the sine squared of the angle between the eyes and its axis), it faces the eyes
        // whole; this far off, it turns about its axis alone; between, it blends: about 10 to 20 degrees.
        const float EndOn = 0.03f, Across = 0.12f;

        // Turn the flat arrow about the way it points so its face is to the eyes; looking nearly straight down
        // that way, it faces them whole, blending from one to the other as the eyes move (a snap between them,
        // at one angle, flickered as a head moved about it).
        void Face()
        {
            Transform view = Eyes;
            if (view == null) return;
            Vector3 axis = follow ? Vector3.up : (arrow.parent != null ? arrow.parent.rotation * _turn : _turn) * Vector3.up;
            Vector3 toEyes = view.position - arrow.position;
            if (toEyes.sqrMagnitude < 1e-8f) return;
            Vector3 across = Vector3.ProjectOnPlane(toEyes, axis);
            float off = across.sqrMagnitude / toEyes.sqrMagnitude;
            Quaternion whole = Quaternion.LookRotation(toEyes, view.up);
            if (off <= EndOn)
            {
                arrow.rotation = whole;
                return;
            }
            Quaternion about = Quaternion.LookRotation(across, axis);
            arrow.rotation = off >= Across ? about : Quaternion.Slerp(whole, about, (off - EndOn) / (Across - EndOn));
        }

        void OnEnable() => Show();

        void OnDisable() => Hide();

        void LateUpdate()
        {
            _clock += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            Place(_clock);
        }

        static float Safe(float scale) => Mathf.Abs(scale) > 1e-6f ? scale : 1f;

        /// <summary>
        /// What the cues on <paramref name="target"/> draw, left out of what one of them places itself by: every
        /// indicator's and marker's object under it, the arrow or ring each shows, and <paramref name="own"/> (the asker's,
        /// wherever it is); so two cues on one target never stack on each other's parts.
        /// </summary>
        /// <param name="target">What the cues are on (null: none).</param>
        /// <param name="own">The asker's own arrow or ring (null: none).</param>
        internal static System.Func<Transform, bool> CuesOn(Transform target, Transform own)
        {
            var drawn = new HashSet<Transform>();
            if (own != null) drawn.Add(own);
            if (target != null)
            {
                foreach (Indicator each in target.GetComponentsInChildren<Indicator>(true))
                {
                    drawn.Add(each.transform);
                    if (each.arrow != null) drawn.Add(each.arrow);
                }
                foreach (Marker each in target.GetComponentsInChildren<Marker>(true))
                {
                    drawn.Add(each.transform);
                    if (each.ring != null) drawn.Add(each.ring);
                }
            }
            return drawn.Contains;
        }

        // A flat arrow 13 cm long (a head and a shaft, both faces), its tip at its origin pointing down
        // and its face toward +Z, glowing in its colour, a thin outline in its outline colour just behind it.
        Transform MakeFlatArrow()
        {
            const float head = 0.035f, headLength = 0.05f, shaft = 0.012f, length = 0.13f, rim = 0.006f;
            var made = new GameObject("Arrow");
            made.transform.SetParent(transform, false);
            Shown(made, Flat("Indicator Flat Arrow", 0f, head, headLength, shaft, length, 0f),
                  PlainMaterial.Of("Indicator Arrow", color, glow));
            var edge = new GameObject("Outline");
            edge.transform.SetParent(made.transform, false);
            Shown(edge, Flat("Indicator Flat Arrow Outline", rim * 1.6f, head + rim * 1.4f, headLength + rim * 0.4f,
                             shaft + rim, length + rim, -0.002f), PlainMaterial.Of("Indicator Arrow Outline", outlineColor));
            return made.transform;
        }

        // A flat arrow's shape, its tip pushed out by tip below the origin, at depth z, with both faces.
        static Mesh Flat(string name, float tip, float head, float headLength, float shaft, float length, float z)
        {
            var points = new[]
            {
                new Vector3(0f, -tip, z), new Vector3(head, headLength, z), new Vector3(shaft, headLength, z),
                new Vector3(shaft, length, z), new Vector3(-shaft, length, z), new Vector3(-shaft, headLength, z),
                new Vector3(-head, headLength, z),
            };
            int[] front = { 0, 6, 1, 2, 5, 3, 3, 5, 4 };
            var vertices = new List<Vector3>(points);
            vertices.AddRange(points);  // the back face's own vertices, so each face keeps its own normal
            var triangles = new List<int>(front);
            for (int i = 0; i < front.Length; i += 3)
                triangles.AddRange(new[] { front[i] + points.Length, front[i + 2] + points.Length, front[i + 1] + points.Length });
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // What shows mesh in material, casting no shadow; both its own, destroyed with it.
        void Shown(GameObject what, Mesh mesh, Material material)
        {
            _owned.Add(mesh);
            _owned.Add(material);
            what.AddComponent<MeshFilter>().sharedMesh = mesh;
            var shown = what.AddComponent<MeshRenderer>();
            shown.sharedMaterial = material;
            shown.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // An arrow 13 cm long (a head and a shaft), its tip at its origin pointing down, glowing in its
        // colour (a plain material on the pipeline's Lit, its emission on), casting no shadow.
        Transform MakeArrow()
        {
            const int sides = 16;
            const float headRadius = 0.022f, headLength = 0.04f, shaftRadius = 0.008f, length = DefaultSize;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            // A ring of sides at height y, radius r; the first of its vertices.
            int Ring(float y, float r)
            {
                int first = vertices.Count;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * 2f * Mathf.PI / sides;
                    vertices.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                }
                return first;
            }
            // A fan from a point to a ring (a cone's side or a cap), facing out as `down` says.
            void Fan(Vector3 point, int ring, bool down)
            {
                int apex = vertices.Count;
                vertices.Add(point);
                for (int i = 0; i < sides; i++)
                {
                    int a = ring + i, b = ring + (i + 1) % sides;
                    triangles.AddRange(down ? new[] { apex, b, a } : new[] { apex, a, b });
                }
            }
            Fan(Vector3.zero, Ring(headLength, headRadius), true);                    // the head's side, from its tip
            Fan(new Vector3(0f, headLength, 0f), Ring(headLength, headRadius), false);  // its back
            int low = Ring(headLength, shaftRadius), high = Ring(length, shaftRadius);   // the shaft
            for (int i = 0; i < sides; i++)
            {
                int a = low + i, b = low + (i + 1) % sides, c = high + i, d = high + (i + 1) % sides;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            Fan(new Vector3(0f, length, 0f), Ring(length, shaftRadius), false);        // its end
            var mesh = new Mesh { name = "Indicator Arrow" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var made = new GameObject("Arrow");
            made.transform.SetParent(transform, false);
            Shown(made, mesh, PlainMaterial.Of("Indicator Arrow", color, glow));
            return made.transform;
        }

        void OnDestroy()
        {
            foreach (Object made in _owned)
            {
                if (made == null) continue;
                if (Application.isPlaying) Destroy(made);
                else DestroyImmediate(made);
            }
            _owned.Clear();
        }
    }
}
