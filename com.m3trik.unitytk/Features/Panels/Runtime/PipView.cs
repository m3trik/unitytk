using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels
{
    /// <summary>Where a <see cref="PipView"/> comes up.</summary>
    public enum PipStyle
    {
        /// <summary>Beside the placard, on the side toward the middle of the view, turned as it is: one glance reads both.</summary>
        Beside = 0,
        /// <summary>At an authored anchor in the scene (a screen the scene holds for it), facing the eyes.</summary>
        Anchored = 1,
        /// <summary>In a corner of the view, following the eyes (a headset's or a screen's inset).</summary>
        Corner = 2,
    }

    /// <summary>
    /// A picture-in-picture: a card in the world showing what its own camera sees of a part, with
    /// a caption (what the picture shows, in words), fading in and out. The camera
    /// frames the part from the eyes' side and clips what stands between (its near plane just
    /// before the part), so a part behind a panel or a door is shown cut away; an authored spot to
    /// look from may be given instead. The card comes up beside the placard, at an anchor or in a
    /// corner of the view (<see cref="style"/>). A <see cref="PipShot"/> shows its part while it is
    /// switched on (a step turns it on), as a page shows on the placard.
    /// </summary>
    /// <remarks>
    /// Prior art: a second camera rendering into a texture is Unity's own picture-in-picture (a
    /// Camera's target texture); a UI Toolkit world-space document shows it as a panel shows any
    /// image, so the PiP shares the placard's input, theme and look. Cinemachine frames a target
    /// for the main view, which a PiP must not take over; this frames its own camera, once a show.
    /// </remarks>
    [AddComponentMenu("unitytk/Panels/PiP View")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PipView : MonoBehaviour
    {
        /// <summary>The class the card carries.</summary>
        public const string CardClass = "pip";
        /// <summary>The class its picture carries.</summary>
        public const string ImageClass = "pip-image";
        /// <summary>The class its caption carries.</summary>
        public const string CaptionClass = "pip-caption";

        [Tooltip("Where it comes up: beside the placard, at an anchor, or in a corner of the view.")]
        public PipStyle style = PipStyle.Beside;

        [Tooltip("The placard it sits beside (the scene's switched-on placard when empty).")]
        public Placard placard;

        [Tooltip("Where it comes up in the Anchored style (an authored screen in the scene); its own mesh is hidden.")]
        public Transform anchor;

        [Tooltip("The user's eyes (the main camera when empty).")]
        public Transform eyes;

        [Tooltip("Where it sits in the Corner style, in metres from the eyes: right, up and ahead.")]
        public Vector3 cornerOffset = new Vector3(0.32f, 0.16f, 0.8f);

        [Tooltip("The gap between it and the placard, in metres, in the Beside style.")]
        public float gap = 0.04f;

        [Tooltip("Its camera's field of view, in degrees (1 to 179).")]
        [Range(1f, 179f)]
        public float fieldOfView = DefaultFieldOfView;

        [Tooltip("What the picture leaves out: objects drawn in the scene but not in the picture (the cues pointing at what " +
                 "it shows, an arrow), hidden only while its own camera draws.")]
        public List<GameObject> hidden = new List<GameObject>();

        // The renderers hidden while its camera draws, given back after; a buffer for finding them (no frame allocates).
        readonly List<Renderer> _hiding = new List<Renderer>();
        static readonly List<Renderer> Found = new List<Renderer>();

        /// <summary>The <see cref="fieldOfView"/> of a view just made, in degrees.</summary>
        public const float DefaultFieldOfView = 30f;

        [Tooltip("How much room the part has around it in the picture (1: edge to edge).")]
        public float margin = DefaultMargin;

        /// <summary>The <see cref="margin"/> of a view just made.</summary>
        public const float DefaultMargin = 1.4f;

        [Tooltip("The picture's size, in pixels: its shape is the picture's on the card.")]
        public Vector2Int resolution = new Vector2Int(640, 480);

        [Tooltip("Seconds it takes to fade in or out.")]
        public float fade = 0.25f;

        [Tooltip("Fade by unscaled time (a paused game's time scale of 0 still fades it); off: by the game's time.")]
        public bool unscaledTime;

        [Tooltip("The picture's height on the card, in metres; its width follows the resolution's shape, and the card fits it.")]
        [Min(0f)]
        public float pictureHeight = DefaultPictureHeight;

        /// <summary>The <see cref="pictureHeight"/> of a view just made, in metres.</summary>
        public const float DefaultPictureHeight = 0.27f;

        /// <summary>The picture's width on the card, in metres: <see cref="pictureHeight"/> in <see cref="resolution"/>'s shape.</summary>
        public float PictureWidth => Mathf.Max(0f, pictureHeight) * resolution.x / Mathf.Max(1, resolution.y);

        [Tooltip("A style sheet over its panel settings' theme (the package's theme holds Panels.uss: its pip classes); empty: the theme alone.")]
        public StyleSheet look;

        readonly List<Renderer> _renderers = new List<Renderer>();  // the target's, gathered as it is shown: framed every frame, allocating nothing
        RenderTexture _texture;
        Camera _camera;
        float _opacity;
        bool _stale = true;

        /// <summary>The part shown; null while it shows nothing.</summary>
        public Transform Target { get; private set; }

        /// <summary>Where the camera looks from (null: the eyes' side).</summary>
        public Transform From { get; private set; }

        /// <summary>The caption shown.</summary>
        public string Caption { get; private set; } = "";

        /// <summary>What put the part there (a shot), so only it takes it away again.</summary>
        public Object ShownBy { get; private set; }

        /// <summary>The camera that draws the picture (made the first time it shows).</summary>
        public Camera PictureCamera => _camera;

        /// <summary>The user's eyes: <see cref="eyes"/>, else the main camera's; null when there is none.</summary>
        public Transform Eyes => Viewer.Eyes(eyes);

        /// <summary>The placard it sits beside: the one named, else the scene's switched-on one.</summary>
        public Placard Placard => placard != null ? placard : Placard.InScene();

        /// <summary>The first switched-on PiP in the loaded scenes; null when there is none.</summary>
        public static PipView InScene() => SceneObjects.FirstEnabled<PipView>();

        /// <summary>
        /// Show <paramref name="target"/> with <paramref name="caption"/>: its camera framed on it
        /// (from <paramref name="from"/>, else the eyes' side, cutting away what stands between), the
        /// card brought up where its style puts it and faded in.
        /// </summary>
        /// <param name="target">The part to show.</param>
        /// <param name="caption">What the picture shows, in words; null: none.</param>
        /// <param name="by">What shows it (a shot), so that only it takes it away again.</param>
        /// <param name="from">An authored spot to look from; null: the eyes' side.</param>
        public void Show(Transform target, string caption, Object by = null, Transform from = null)
        {
            Target = target;
            From = from;
            Caption = caption ?? "";
            ShownBy = by;
            _renderers.Clear();
            if (target != null) target.GetComponentsInChildren(_renderers);
            if (!Application.isPlaying) _opacity = 1f;
            Place();  // the card, and the camera aimed on the part from where it stands
            Render();
        }

        /// <summary>Show nothing, unless <paramref name="by"/> names something other than what showed the part.</summary>
        /// <param name="by">What takes it away (a shot); null: whatever showed it.</param>
        public void Clear(Object by = null)
        {
            if (by != null && by != ShownBy) return;
            Target = null;
            From = null;
            Caption = "";
            ShownBy = null;
            _renderers.Clear();
            if (!Application.isPlaying) _opacity = 0f;
            SwitchCamera();
            Render();
        }

        // The near plane: this share of the way to the part, within these bounds (metres).
        const float NearShare = 0.01f, MinNear = 0.005f, MaxNear = 0.05f;

        /// <summary>
        /// Put the camera where the picture is taken from: at its spot exactly as the spot stands (where it is, the way
        /// it looks and, where it is a camera, its own field of view), else on the eyes' side, aimed at the part and
        /// near enough to fill the picture.
        /// </summary>
        public void Frame()
        {
            if (Target == null) return;
            EnsureCamera();
            Bounds seen = SceneBounds.TryOf(_renderers, out Bounds shown, includeDisabled: true) ? shown : new Bounds(Target.position, Vector3.one * 0.1f);
            float radius = Mathf.Max(seen.extents.magnitude, 0.01f);
            float angle = Mathf.Clamp(fieldOfView, 1f, 179f);  // a script's value too: 0 frames from infinitely far
            float distance = radius * margin / Mathf.Sin(angle * 0.5f * Mathf.Deg2Rad);
            Vector3 at;
            Quaternion look;
            if (From != null)
            {
                // The spot is the truth: what a person sees looking along it is the picture.
                at = From.position;
                look = From.rotation;
                if (From.TryGetComponent(out Camera lens)) angle = Mathf.Clamp(lens.fieldOfView, 1f, 179f);
            }
            else
            {
                Transform view = Eyes;
                Vector3 toward = view != null ? view.position - seen.center : Vector3.back;
                if (toward.sqrMagnitude < 1e-8f) toward = Vector3.back;
                at = seen.center + toward.normalized * distance;
                look = Quaternion.LookRotation(seen.center - at, Vector3.up);
            }
            distance = Vector3.Distance(at, seen.center);
            _camera.transform.SetPositionAndRotation(at, look);
            _camera.fieldOfView = angle;
            // A near plane as near as depth allows: a plane just before the part sliced what stands around it
            // (a cabinet's frame showed cut open). The spot it is seen from is placed with a clear line of sight.
            _camera.nearClipPlane = Mathf.Clamp(distance * NearShare, MinNear, MaxNear);
            _camera.farClipPlane = distance + radius * 4f + 1f;
            SwitchCamera();
        }

        // Its camera draws only while its picture is seen: a part shown, unfolded and not faded out (a 640 x 480 view
        // a frame is a headset's to spare only for a picture someone sees).
        void SwitchCamera()
        {
            if (_camera != null) _camera.enabled = Target != null && !_frame.Folded && _opacity > 0f;
        }

        [Tooltip("Dragged anywhere, it swings around the eyes (sideways) and rises or lowers (up or down), and keeps that spot. The pointer shows it can move.")]
        public bool movable = true;

        [Tooltip("Show an arrow in its corner that folds the picture away (its caption stays), and unfolds it again.")]
        public bool collapsible = true;

        [Tooltip("How far a drag may swing it from where it comes up, in degrees either way.")]
        public float maxSwing = 150f;

        [Tooltip("How far a drag may raise or lower it, in metres either way.")]
        public float maxLift = 0.5f;

        [Tooltip("Panel pixels to the metre: its document's panel settings' (the package's world panel: 1000).")]
        public float pixelsPerMetre = Placard.DefaultPixelsPerMetre;

        [Tooltip("The layer it stands on, where it stands on the default one, and the layer its camera leaves out, so the picture never shows a window; empty: as it is, and every layer shown.")]
        public string windowLayer = WindowFrame.DefaultLayer;

        [Tooltip("What its fold arrow shows and says.")]
        public WindowFold fold = new WindowFold("Fold the picture away");

        readonly WindowFrame _frame = new WindowFrame();

        /// <summary>Whether its picture is folded away.</summary>
        public bool Collapsed => _frame.Folded;

        /// <summary>Move it as it is dragged <paramref name="pixels"/> across its card since the press (as the placard's <see cref="Placard.Drag"/>).</summary>
        /// <param name="pixels">The drag, in panel pixels (right and down positive).</param>
        public void Drag(Vector2 pixels)
        {
            Transform view = Eyes;
            float radius = view == null ? 1f : Vector3.Distance(view.position, transform.position);
            _frame.Drag(pixels, radius, maxSwing, maxLift, pixelsPerMetre);
            Place();
        }

        /// <summary>Fold its picture away (<paramref name="on"/>), its caption staying, or unfold it.</summary>
        /// <param name="on">Fold it.</param>
        public void Collapse(bool on)
        {
            _frame.Folded = on;
            _stale = true;
            SwitchCamera();
        }

        /// <summary>
        /// Bring the card up where its style puts it, facing the eyes; its camera, a child of the card,
        /// aimed on the part again from where it stood, so moving the card never moves the picture.
        /// </summary>
        public void Place() => Place(true);

        // Place, its collider synced to where it now stands (a click this very frame) unless it rides the eyes every frame.
        void Place(bool sync)
        {
            Transform view = Eyes;
            Vector3 at;
            switch (style)
            {
                case PipStyle.Anchored when anchor != null:
                    at = anchor.position;
                    HideAnchorMesh();
                    break;
                case PipStyle.Corner when view != null:
                    at = view.position + view.rotation * cornerOffset;
                    break;
                default:
                    Placard beside = Placard;
                    if (beside == null || view == null)
                    {
                        at = view != null ? view.position + view.rotation * cornerOffset : transform.position;
                        break;
                    }
                    // Toward the middle of the view, past the placard's edge.
                    Transform card = beside.transform;
                    float side = Vector3.Dot(card.position - view.position, view.right) > 0f ? -1f : 1f;
                    float reach = HalfWidth(beside.gameObject) + HalfWidth(gameObject, PictureWidth * 0.5f) + gap;
                    at = card.position + card.right * side * reach;
                    break;
            }
            // Facing the eyes; where the user moved it, but at an anchor, which stays where the scene holds it.
            Pose pose = view == null ? new Pose(at, transform.rotation)
                      : style == PipStyle.Anchored ? new Pose(at, WindowFrame.Toward(view.position, at, transform.rotation))
                      : _frame.Beside(view.position, at, transform.rotation);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (sync) Physics.SyncTransforms();
            Frame();
        }

        void HideAnchorMesh()
        {
            foreach (Renderer shown in anchor.GetComponents<Renderer>()) shown.enabled = false;
        }

        // Half of what's width in metres: its collider's (a document's rect), else a fallback.
        static float HalfWidth(GameObject what, float fallback = 0.24f)
        {
            if (what.TryGetComponent(out BoxCollider box))
            {
                float width = box.size.x * what.transform.lossyScale.x;
                if (width > 0f) return width * 0.5f;
            }
            return fallback;
        }

        void EnsureCamera()
        {
            if (_texture == null)
            {
                _texture = new RenderTexture(Mathf.Max(16, resolution.x), Mathf.Max(16, resolution.y), 24) { name = "PiP" };
                _stale = true;
            }
            if (_camera != null) return;
            var node = new GameObject("PiP Camera") { hideFlags = HideFlags.DontSave };
            node.transform.SetParent(transform, false);
            _camera = node.AddComponent<Camera>();
            int windows = WindowFrame.LayerOf(windowLayer);
            if (windows >= 0) _camera.cullingMask &= ~(1 << windows);  // the world, never its windows
            _camera.targetTexture = _texture;
            _camera.enabled = false;
        }

        void OnEnable()
        {
            _stale = true;  // the document builds its tree anew as it is switched on
            WindowFrame.PutOnLayer(gameObject, windowLayer);
            SwitchCamera();  // switched off with it: on again where its picture is seen
            CameraDrawing.Starting += BeginDrawing;
            CameraDrawing.Ended += EndDrawing;
        }

        void OnDisable()
        {
            if (_camera != null) _camera.enabled = false;
            CameraDrawing.Starting -= BeginDrawing;
            CameraDrawing.Ended -= EndDrawing;
            Unhide();
        }

        // Its own camera's drawing, whichever pipeline draws (the kernel's CameraDrawing): what it is given is left out.
        void BeginDrawing(Camera drawing) { if (drawing == _camera) Hide(); }
        void EndDrawing(Camera drawing) { if (drawing == _camera) Unhide(); }

        /// <summary>Leave out what <see cref="hidden"/> names (its renderers drawn by no camera) until <see cref="Unhide"/>: what drawing the picture does.</summary>
        public void Hide()
        {
            Unhide();
            for (int i = 0; i < hidden.Count; i++)
            {
                if (hidden[i] == null) continue;
                hidden[i].GetComponentsInChildren(false, Found);
                for (int r = 0; r < Found.Count; r++)
                {
                    if (Found[r].forceRenderingOff) continue;  // off already: another's to give back
                    Found[r].forceRenderingOff = true;
                    _hiding.Add(Found[r]);
                }
            }
            Found.Clear();
        }

        /// <summary>Give back what <see cref="Hide"/> left out.</summary>
        public void Unhide()
        {
            for (int i = 0; i < _hiding.Count; i++)
                if (_hiding[i] != null) _hiding[i].forceRenderingOff = false;
            _hiding.Clear();
        }

        // Its picture's texture goes with it: Release frees the GPU's copy alone, and the texture object stayed behind.
        void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_texture == null) return;
            _texture.Release();
            if (Application.isPlaying) Destroy(_texture);
            else DestroyImmediate(_texture);
            _texture = null;
        }

        void LateUpdate()
        {
            float goal = Target != null ? 1f : 0f;
            if (!Mathf.Approximately(_opacity, goal))
            {
                float seconds = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                _opacity = fade <= 0f ? goal : Mathf.MoveTowards(_opacity, goal, seconds / fade);
                _stale = true;
                SwitchCamera();
            }
            if (Target != null && style == PipStyle.Corner) Place(false);  // riding the eyes: no physics sync every frame
            if (_stale) Render();
        }

        // Put the picture and caption on the document's tree; stale until its root is there.
        void Render()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _stale = root == null;
            if (_stale) return;
            if (look != null && !root.styleSheets.Contains(look)) root.styleSheets.Add(look);
            VisualElement card = root.Q(className: CardClass);
            if (card == null)
            {
                card = new VisualElement { name = "pip" };
                card.AddToClassList(CardClass);
                var image = new VisualElement { name = "image" };
                image.AddToClassList(ImageClass);
                card.Add(image);
                var caption = new Label { name = "caption" };
                caption.AddToClassList(CaptionClass);
                card.Add(caption);
                root.Add(card);
            }
            _frame.Bind(card, movable && style != PipStyle.Anchored, collapsible, Drag, Collapse, fold);
            VisualElement picture = card.Q("image");
            picture.style.backgroundImage = _texture != null ? Background.FromRenderTexture(_texture) : default(StyleBackground);
            picture.style.display = _frame.Folded ? DisplayStyle.None : DisplayStyle.Flex;
            // Its size the fields', in panel pixels, so where it comes up before the card lays out is where it stays.
            float width = PictureWidth * pixelsPerMetre;
            picture.style.width = width;
            picture.style.height = Mathf.Max(0f, pictureHeight) * pixelsPerMetre;
            Label label = card.Q<Label>("caption");
            label.style.maxWidth = width;  // a long caption wraps under the picture, never widening the card
            label.text = Caption;
            label.style.display = Caption.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            card.style.opacity = _opacity;
            card.style.display = Target != null || _opacity > 0f ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
