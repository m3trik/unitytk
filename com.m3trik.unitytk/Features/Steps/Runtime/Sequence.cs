using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Steps.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Steps.Tests")]

namespace M3trik.Unitytk.Steps
{
    /// <summary>
    /// Steps run one at a time, in order: the current one completes when every signal it
    /// awaits arrives (<see cref="Signal"/>), and goes on by itself or by a choice
    /// (<see cref="Choose"/>, <see cref="Next"/>); a choice may go to any step
    /// (<see cref="GoTo"/>). Every component a step lists is disabled until that step runs.
    /// </summary>
    /// <remarks>
    /// Prior art: an Animator state machine or Visual Scripting's state graphs hold states
    /// and transitions, edited as a graph in their own window and driven by their own
    /// parameters; a flow of steps is a list with a few branches, and
    /// what completes a step is the scene's own events (a clip ends, a target is
    /// selected), wired as UnityEvents to <see cref="Signal"/>, so a step list and a signal
    /// call are all it needs. Each step is a child object, so a person reads and edits the
    /// flow in the hierarchy and the inspector.
    /// </remarks>
    [AddComponentMenu("unitytk/Steps/Sequence")]
    [DisallowMultipleComponent]
    public sealed class Sequence : MonoBehaviour
    {
        [Tooltip("The steps, in order.")]
        public List<Step> steps = new List<Step>();

        [Tooltip("Begin at the first step when the scene starts.")]
        public bool beginOnStart = true;

        [Tooltip("A step started (the step).")]
        public StepEvent onStepEntered = new StepEvent();

        [Tooltip("The last step ended.")]
        public UnityEvent onFinished = new UnityEvent();

        [Tooltip("Count the steps' time unscaled (a paused game's time scale of 0 still runs it); off: the game's time.")]
        public bool unscaledTime;

        [Tooltip("Start over by loading its scene again, while playing (what the steps changed is undone); off: begin again, the scene as it stands.")]
        public bool reloadOnRestart = true;

        [Tooltip("How its scene loads again on a start over: Single (it alone, the other scenes unloaded), or Additive (it alone unloaded and loaded again, the scenes loaded beside it kept).")]
        public LoadSceneMode reloadMode = LoadSceneMode.Single;

        /// <summary>The step running; null before the sequence begins and after it finishes.</summary>
        public Step Current { get; private set; }

        /// <summary>Whether the last step has ended.</summary>
        public bool Finished { get; private set; }

        /// <summary>The step whose ID is <paramref name="id"/>; null when there is none.</summary>
        /// <param name="id">A step's ID.</param>
        public Step Find(string id)
        {
            foreach (Step step in steps)
                if (step != null && step.id == id) return step;
            return null;
        }

        /// <summary>Start at the first step, every step's components disabled until it runs.</summary>
        public void Begin()
        {
            foreach (Step step in steps)
                if (step != null) step.Rewind();
            Finished = false;
            Current = null;
            Enter(steps.Count > 0 ? steps[0] : null);
        }

        /// <summary>
        /// A signal arrived (a clip's name as it ends, a selection's): the current step takes
        /// it if it awaits it, and goes on by itself once complete where it may.
        /// </summary>
        /// <param name="signal">The signal.</param>
        public void Signal(string signal)
        {
            // The step taken first: what completing it raises may move the sequence itself (a listener's Next or GoTo).
            Step step = Current;
            if (step == null || !step.Receive(signal)) return;
            if (Current == step && step.GoesOn) Next();
        }

        /// <summary>
        /// Count <paramref name="seconds"/> on the current step's clock (a player does each frame): a
        /// step going on by itself goes on once complete and run its <see cref="Step.dwell"/>.
        /// </summary>
        /// <param name="seconds">The time passed.</param>
        public void Tick(float seconds)
        {
            Step step = Current;
            if (step == null) return;
            step.Elapsed += seconds;
            if (step.GoesOn) Next();  // its onComplete raised already: as it completed, or as it started awaiting nothing
        }

        /// <summary>Leave the current step for the one after it; past the last, the sequence finishes.</summary>
        public void Next()
        {
            if (Current == null) return;
            int index = steps.IndexOf(Current);
            Enter(index >= 0 && index + 1 < steps.Count ? steps[index + 1] : null);
        }

        /// <summary>Leave the current step for the step whose ID is <paramref name="id"/> (none: nothing happens).</summary>
        /// <param name="id">A step's ID.</param>
        public void GoTo(string id)
        {
            Step step = Find(id);
            if (step == null)
            {
                Debug.LogWarning($"{name}: no step '{id}' to go to: add it, or name another", this);
                return;
            }
            Enter(step);
        }

        /// <summary>
        /// Take the current step's choice labelled <paramref name="label"/>, else the one it takes
        /// for any other label (<see cref="StepChoice.otherwise"/>): its step, or the next, or the
        /// end of the sequence for a choice that finishes it.
        /// </summary>
        /// <param name="label">The choice's label (ignoring case), or an entered value.</param>
        /// <returns>Whether the current step offers it.</returns>
        public bool Choose(string label)
        {
            Step step = Current;
            StepChoice choice = step == null ? null : step.ChoiceFor(label);
            if (choice == null) return false;
            if (choice.Starts)
            {
                // Held at its start: the step begins, and stays; awaiting nothing, it is complete as it begins.
                if (!step.BeginActions()) return false;
                if (step.awaits.Count == 0) step.Complete();
                return true;
            }
            if (choice.waits && !step.IsComplete) return false;
            if (choice.Restarts) Restart();
            else if (choice.Finishes) Enter(null);
            else if (string.IsNullOrEmpty(choice.to)) Next();
            else GoTo(choice.to);
            return true;
        }

        /// <summary>
        /// Loads a scene the build does not list again while playing, as the mode says: the editor part's
        /// answer (Play Mode loads any scene asset), registered whenever its domain loads; null in a player,
        /// where every scene that runs is listed. Returns whether it loads it.
        /// </summary>
        internal static Func<Scene, LoadSceneMode, bool> LoadAgain;

        /// <summary>
        /// Whether <see cref="Restart"/> loads the scene again (playing, <see cref="reloadOnRestart"/> on, and the build
        /// lists the scene or the editor loads it).
        /// </summary>
        public bool Reloads => Application.isPlaying && reloadOnRestart && (gameObject.scene.buildIndex >= 0 || LoadAgain != null);

        /// <summary>
        /// Start over: while playing, the scene is loaded again as <see cref="reloadMode"/> says, so all the
        /// steps changed (a door opened, a tool taken) is undone (in the editor's Play Mode too, listed or not);
        /// outside play, with <see cref="reloadOnRestart"/> off, or where nothing loads it, the sequence begins again
        /// (<see cref="Begin"/>) and the scene stays as it is.
        /// </summary>
        public void Restart() => StartOver();

        // Restart, saying whether its scene is loaded again (false: the sequence began again, the scene as it stands).
        internal bool StartOver()
        {
            Scene scene = gameObject.scene;
            if (Application.isPlaying && reloadOnRestart)
            {
                if (scene.buildIndex >= 0)
                {
                    int index = scene.buildIndex;
                    if (reloadMode == LoadSceneMode.Single) SceneManager.LoadScene(index);
                    else ReplaceScene(scene, () => SceneManager.LoadSceneAsync(index, LoadSceneMode.Additive));
                    return true;
                }
                if (LoadAgain != null && LoadAgain(scene, reloadMode)) return true;
                Debug.LogWarning($"{name}: '{scene.name}' is not in the build's scene list, so it begins again without " +
                                 "loading the scene again (what its steps changed stays): add it to the build's scenes", this);
            }
            Begin();
            return false;
        }

        /// <summary>
        /// Load <paramref name="scene"/> again beside the scenes loaded with it: <paramref name="load"/> starts loading a
        /// new copy additively, and once it is loaded the new copy is made the active scene where the old one was, and
        /// the old one is unloaded (the last scene loaded is never unloaded before its copy is there).
        /// </summary>
        /// <param name="scene">The scene loaded again.</param>
        /// <param name="load">What starts loading its new copy, additively.</param>
        internal static void ReplaceScene(Scene scene, Func<AsyncOperation> load)
        {
            bool active = SceneManager.GetActiveScene() == scene;
            string path = scene.path;
            load().completed += _ =>
            {
                if (active)
                    for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                    {
                        Scene made = SceneManager.GetSceneAt(i);
                        if (made == scene || made.path != path) continue;
                        SceneManager.SetActiveScene(made);
                        break;
                    }
                SceneManager.UnloadSceneAsync(scene);
            };
        }

        /// <summary>
        /// <see cref="Choose"/>, for an event: a panel's choice wired here takes the current
        /// step's choice of that label (none: nothing happens).
        /// </summary>
        /// <param name="label">The choice's label (ignoring case).</param>
        public void Pick(string label) => Choose(label);

        void Start()
        {
            if (beginOnStart) Begin();
        }

        void Update() => Tick(unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        // Leave the current step for step (null: finish); a step awaiting nothing completes as it starts
        // (its onComplete raised then, whether it goes on by itself or waits for a choice), and one going on
        // by itself with no dwell is passed straight through. What its events do may move the sequence (a
        // listener on the step it leaves, or on the one it enters, calling Next or GoTo): then that move stands,
        // and nothing of this one is done after it. The step being left is left once, though its own exit moves.
        void Enter(Step step)
        {
            int move = ++_moves;
            while (true)
            {
                Step leaving = Current;
                if (leaving != null && leaving != _leaving)
                {
                    Step outer = _leaving;
                    _leaving = leaving;
                    try
                    {
                        leaving.Exit();
                    }
                    finally
                    {
                        _leaving = outer;
                    }
                    if (_moves != move) return;  // its exit moved the sequence
                }
                Current = step;
                if (step == null)
                {
                    Finished = true;
                    onFinished.Invoke();
                    return;
                }
                step.Enter();
                if (_moves != move) return;  // its entry moved the sequence: the step it went to was said entered
                onStepEntered.Invoke(step);
                if (_moves != move) return;
                if (step.Started && step.awaits.Count == 0) step.Complete();
                if (_moves != move || !step.GoesOn) return;
                int index = steps.IndexOf(step);
                step = index + 1 < steps.Count ? steps[index + 1] : null;
            }
        }

        // How many moves the sequence has made (Enter counts each): one that sees the count change while an event of its
        // own runs knows that event moved the sequence.
        int _moves;

        // The step whose exit is under way: a move its exit makes does not leave it again.
        Step _leaving;
    }
}
