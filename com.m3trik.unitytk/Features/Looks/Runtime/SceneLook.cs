using System;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Looks.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Looks.Tests")]

namespace M3trik.Unitytk.Looks
{
    /// <summary>
    /// A look on a scene object: while enabled in a scene, the game cameras it names (every one by default)
    /// show the scene through its tone mapping at its exposure, or through a lookup table of its own, in place
    /// of the project's own (through the render pipeline's host, <see cref="IRenderPipelineHost.Display"/>; a
    /// pipeline that reads no lookup table, Built-in, shows none, as its import says). The import puts one on a model whose handoff record states a look;
    /// a person changes its values here, or takes it off for the project's own look.
    /// </summary>
    /// <remarks>
    /// Of two looks shown at once the one of higher <see cref="priority"/> shows; two at one priority blend as
    /// the pipeline orders them. Nothing it makes is saved: the transform is made again whenever it is
    /// enabled, and put away around every save of a scene (<see cref="ISaveSuspended"/>), so a saved scene
    /// holds none of it (a camera it turned post-processing on for included).
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("unitytk/Looks/Scene Look")]
    public sealed class SceneLook : MonoBehaviour, ISaveSuspended
    {
        [Tooltip("The tone mapping, by three.js's name (ACESFilmic, Linear, None): the handoff record's renderer.toneMapping. " +
                 "A table of its own, below, takes its place.")]
        public string toneMapping = LookCurve.AcesFilmic;

        [Tooltip("The exposure the scene shows at, before tone mapping (or before its own table): the handoff record's " +
                 "renderer.toneMappingExposure.")]
        [Min(0f)]
        public float exposure = 1f;

        [Tooltip("A look of its own in place of the tone mapping: a strip lookup table as the pipeline's colour lookup reads " +
                 "it (Display Size squared wide, Display Size high, sRGB in and out), its input the scene's light over the span. " +
                 "Empty: the tone mapping.")]
        public Texture2D table;

        [Tooltip("The brightest scene light the look spans: past it every colour shows as the brightest.")]
        [Min(1f)]
        public float span = LookCurve.Brightest;

        [Tooltip("The cameras it shows on (empty: every game camera, those there now and each one later). A camera already " +
                 "post-processing shows it either way.")]
        public Camera[] cameras = new Camera[0];

        [Tooltip("Its weight over the project's own looks and other Scene Looks: the highest shows; two at one weight blend " +
                 "as the pipeline orders them.")]
        public float priority = float.MaxValue;

        [Tooltip("Switch the project's bloom off while it shows (a delivery's look is the whole of it).")]
        public bool bloomOff = true;

        [Tooltip("Switch the project's vignette off while it shows.")]
        public bool vignetteOff = true;

        IDisposable _shown;
        Texture2D _made;
        bool _suspended;

        /// <summary>
        /// The render pipeline's host it shows through: null, the pipeline drawing now
        /// (<see cref="RenderPipelineHosts.Current"/>); a test's stand-in, where a test gives one.
        /// </summary>
        [NonSerialized] internal IRenderPipelineHost host;

        /// <summary>Whether its look is shown now.</summary>
        public bool Shown => _shown != null;

        /// <summary>
        /// Whether a scene is one the editor only previews in (a model's import, Prefab Mode), where a look is
        /// never shown: the editor part's answer, registered whenever its domain loads; null in a player.
        /// </summary>
        internal static Func<Scene, bool> Previewed;

        void OnEnable() => Show();

        void OnDisable() => Hide();

#if UNITY_EDITOR
        // An inspector edit: shown again once the editor is past validating (nothing is made while it is).
        void OnValidate() => UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) Refresh();
        };
#endif

        /// <summary>Show its look again with its values as they now stand (shown only while it is enabled).</summary>
        public void Refresh()
        {
            Hide();
            if (isActiveAndEnabled) Show();
        }

        /// <summary>
        /// Put its look away for a save (what showing it changed on a camera given back: a look in one open
        /// scene turns on another's cameras too), to be shown again once saved; nothing where it is not shown.
        /// </summary>
        void ISaveSuspended.Suspend()
        {
            if (_shown == null) return;
            Hide();
            _suspended = true;
        }

        /// <summary>Show its look again after a save put it away.</summary>
        void ISaveSuspended.Resume()
        {
            if (!_suspended) return;
            _suspended = false;
            if (isActiveAndEnabled) Show();
        }

        // Show the look through its host: only from a scene (never a model's import or a prefab asset).
        internal void Show()
        {
            Hide();
            IRenderPipelineHost shownBy = host ?? RenderPipelineHosts.Current;
            if (!gameObject.scene.IsValid() || shownBy == null || Previewed != null && Previewed(gameObject.scene)) return;
            // A pipeline that reads no lookup table (Built-in) shows no look at all: its import says so once, not each
            // placed copy.
            if (shownBy.DisplaySize <= 0) return;
            if (table == null && !LookCurve.Knows(toneMapping))
            {
                Debug.LogWarning($"{name}: its look's tone mapping '{toneMapping}' is not one unitytk shows ({LookCurve.Names}), " +
                                 "and it has no table of its own: the project's own look stands", this);
                return;
            }
            float range = Mathf.Max(1f, span);
            Texture2D shown = table;
            if (shown == null)
                shown = _made = shownBy.DisplaySize >= 2 ? LookCurve.Table(toneMapping, exposure, shownBy.DisplaySize, range) : null;
            // Its own table takes the exposure on the way in; a named curve has it in its table.
            float scale = (table != null ? Mathf.Max(0f, exposure) : 1f) / range;
            var options = new DisplayOptions
            {
                priority = priority,
                bloomOff = bloomOff,
                vignetteOff = vignetteOff,
                cameras = cameras,
            };
            _shown = shownBy.Display(shown, scale, out string why, options);
            if (_shown != null) return;
            Debug.LogWarning($"{name}: its look is not shown in the {shownBy.Name} pipeline: {why}", this);
            DestroyMade();
        }

        void Hide()
        {
            _shown?.Dispose();
            _shown = null;
            DestroyMade();
        }

        void DestroyMade()
        {
            if (_made == null) return;
            if (Application.isPlaying) Destroy(_made);
            else DestroyImmediate(_made);
            _made = null;
        }
    }
}
