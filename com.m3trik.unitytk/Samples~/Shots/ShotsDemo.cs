// The Shots sample's player half: plays a model's shots in order and shows the
// playing shot's description. Its demo scene is built by Editor/ShotsDemoRecipe.cs.
using M3trik.Unitytk.Shots;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace M3trik.Unitytk.Samples.Shots
{
    /// <summary>
    /// Plays the clips of a model's <see cref="ShotList"/> one after another, each
    /// from its start, and shows the playing shot's description: what a reader of
    /// the shots does, asking the list for the entry of the clip it plays.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class ShotsDemo : MonoBehaviour
    {
        [Tooltip("The model's shots: the ShotList its import wrote.")]
        public ShotList shots;

        PlayableGraph _graph;
        int _index;
        float _started;

        /// <summary>The clip playing now, or null when there are no shots.</summary>
        public AnimationClip Playing { get; private set; }

        void OnEnable() => Play(0);

        void OnDisable()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        void Update()
        {
            if (Playing != null && Time.time - _started >= Playing.length) Play(_index + 1);
        }

        /// <summary>Play the shot at <paramref name="index"/> (wrapping round the list) from its start.</summary>
        public void Play(int index)
        {
            if (shots == null || shots.entries.Count == 0) return;
            _index = index % shots.entries.Count;
            if (_graph.IsValid()) _graph.Destroy();
            Playing = shots.entries[_index].clip;
            AnimationPlayableUtilities.PlayClip(GetComponent<Animator>(), Playing, out _graph);
            _started = Time.time;
        }

        void OnGUI()
        {
            Shot shot = shots != null ? shots.Entry(Playing) : null;
            if (shot != null) GUI.Label(new Rect(16, 16, 640, 48), $"{Playing.name}: {shot.description}");
        }
    }
}
