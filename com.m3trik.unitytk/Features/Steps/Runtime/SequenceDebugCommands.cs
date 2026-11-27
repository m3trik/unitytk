using M3trik.Unitytk.Contracts;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Steps
{
    /// <summary>
    /// A sequence's commands on a command surface (a debug panel: <see cref="Surface"/>), for trying it in the editor
    /// or a headset: its status says the step it is on; Skip goes on as the step's completion would, Back goes to the
    /// step before, the scene standing as the steps before it left it (loaded again, the autopilot walking there at
    /// once), Restart starts the sequence over, and Autopilot has it play itself from where it is
    /// (<see cref="SequenceAutopilot"/>) or stop.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's Rendering Debugger holds a pipeline's switches, not a scene's commands; asset-store
    /// consoles are screen overlays and a dependency. A sequence already says where it stands and how it moves, so
    /// its commands are a few calls of its own, put on whatever surface the scene shows (<see cref="ICommandSurface"/>).
    /// </remarks>
    [AddComponentMenu("unitytk/Steps/Sequence Debug Commands")]
    [DisallowMultipleComponent]
    public sealed class SequenceDebugCommands : MonoBehaviour
    {
        [Tooltip("The label of the command that skips the step.")]
        public string skipLabel = "Skip";

        [Tooltip("The label of the command that goes to the step before.")]
        public string backLabel = "Back";

        [Tooltip("The label of the command that starts the sequence over.")]
        public string restartLabel = "Restart";

        [Tooltip("The label of the command that switches the autopilot on or off.")]
        public string autopilotLabel = "Autopilot";

        [Tooltip("The sequence (the scene's when empty).")]
        public Sequence sequence;

        [Tooltip("What plays the sequence by itself (the scene's when empty; none: no Autopilot command).")]
        public SequenceAutopilot autopilot;

        ICommandSurface _surface;
        readonly SceneLookup<Sequence> _foundSequence = new SceneLookup<Sequence>();  // the scene's, while none is named
        readonly SceneLookup<SequenceAutopilot> _foundPilot = new SceneLookup<SequenceAutopilot>();
        Step _saidStep;       // what the status was last written from: rewritten only when one changes
        bool _saidFinished, _saidPiloted, _saidOnce;

        /// <summary>
        /// The sequence: the one named, else the scene's (looked up once, and again once it is gone; where there is none,
        /// again once it is switched on).
        /// </summary>
        public Sequence Sequence => sequence != null ? sequence : _foundSequence.Get();

        /// <summary>
        /// What plays the sequence by itself: the one named, else the scene's (looked up as <see cref="Sequence"/> is); null
        /// when there is none.
        /// </summary>
        public SequenceAutopilot Autopilot => autopilot != null ? autopilot : _foundPilot.Get();

        /// <summary>
        /// Where its commands go and its status is said: the one set, else a component on its object that is one (looked
        /// up once, and again once it is gone); null where there is neither (the commands are still called by code).
        /// </summary>
        public ICommandSurface Surface
        {
            get
            {
                if (_surface is Object unitySurface ? unitySurface == null : _surface == null) _surface = GetComponent<ICommandSurface>();
                return _surface;
            }
            set => _surface = value;
        }

        /// <summary>Go on from the step as its completion would.</summary>
        public void SkipStep()
        {
            Sequence walked = Sequence;
            if (walked != null && !walked.Finished && walked.Current != null) walked.Next();
        }

        /// <summary>
        /// Go to the step before the one running, the scene as the steps before it left it: the scene is loaded
        /// again and the autopilot walks it there at once (what the step undid, a part faded or taken, is back).
        /// Where the scene cannot load again or there is no autopilot, straight to it (the scene stays as it is).
        /// </summary>
        public void StepBack()
        {
            Sequence walked = Sequence;
            if (walked == null || walked.Current == null) return;
            int at = walked.steps.IndexOf(walked.Current);
            if (at <= 0) return;
            string back = walked.steps[at - 1].id;
            string path = walked.gameObject.scene.path;  // read now: a single load unloads the scene before its copy is there
            if (Autopilot == null || !walked.Reloads || !walked.StartOver())
            {
                walked.GoTo(back);
                return;
            }
            // Loading again (it loads on a later frame): the new copy's autopilot walks it back there.
            UnityAction<Scene, LoadSceneMode> resume = null;
            resume = (loaded, mode) =>
            {
                if (Resume(loaded, path, back)) SceneManager.sceneLoaded -= resume;
            };
            SceneManager.sceneLoaded += resume;
        }

        /// <summary>
        /// On <paramref name="loaded"/>'s load: where it is the new copy of the scene at <paramref name="path"/>, the
        /// autopilot among its own objects (an additive start over keeps the old copy, its autopilot too, loaded until the
        /// new one is there) walks it to <paramref name="back"/> at once. Any other scene's load is left alone.
        /// </summary>
        /// <param name="loaded">The scene just loaded.</param>
        /// <param name="path">The path of the scene loaded again.</param>
        /// <param name="back">The step to walk to.</param>
        /// <returns>Whether it was the copy (the wait is over, an autopilot found there or not).</returns>
        internal static bool Resume(Scene loaded, string path, string back)
        {
            if (loaded.path != path) return false;
            foreach (GameObject root in loaded.GetRootGameObjects())
            {
                SequenceAutopilot pilot = root.GetComponentInChildren<SequenceAutopilot>(true);
                if (pilot == null) continue;
                pilot.fastForward = true;
                pilot.stopAt = back;
                pilot.enabled = true;
                break;
            }
            return true;
        }

        /// <summary>Start the sequence over (<see cref="Sequence.Restart"/>).</summary>
        public void RestartSequence()
        {
            Sequence walked = Sequence;
            if (walked != null) walked.Restart();
        }

        /// <summary>Have the sequence play itself from where it is, or stop.</summary>
        public void ToggleAutopilot()
        {
            SequenceAutopilot pilot = Autopilot;
            if (pilot == null) return;
            if (pilot.sequence == null) pilot.sequence = Sequence;
            if (!pilot.enabled)
            {
                // From here to the end, as a person plays it (not a walk Back left set up).
                pilot.fastForward = false;
                pilot.stopAt = "";
            }
            pilot.enabled = !pilot.enabled;
        }

        /// <summary>What the status says now: the step running, by ID and title.</summary>
        public string Status()
        {
            Sequence walked = Sequence;
            if (walked == null) return "";
            string where = walked.Finished ? "Finished" : walked.Current == null ? "Not begun"
                : string.IsNullOrEmpty(walked.Current.title) || walked.Current.title == walked.Current.id
                    ? $"Step {walked.Current.id}" : $"Step {walked.Current.id}   {walked.Current.title}";
            SequenceAutopilot pilot = Autopilot;
            return pilot != null && pilot.enabled ? where + "   (autopilot)" : where;
        }

        /// <summary>Put its commands on its surface (what switching it on does); a surface already holding them gets them anew.</summary>
        /// <returns>Whether there is a surface to put them on.</returns>
        public bool Show()
        {
            ICommandSurface on = Surface;
            if (on == null) return false;
            on.Add(this, skipLabel, SkipStep);
            on.Add(this, backLabel, StepBack);
            on.Add(this, restartLabel, RestartSequence);
            if (Autopilot != null) on.Add(this, autopilotLabel, ToggleAutopilot);
            _saidOnce = false;
            return true;
        }

        void OnEnable()
        {
            _foundSequence.Forget();
            _foundPilot.Forget();
            Show();
        }

        void OnDisable()
        {
            ICommandSurface on = Surface;
            if (on != null) on.Remove(this);
        }

        void Update()
        {
            // Per frame it compares what it holds: the status is built again only when the step, the end or the autopilot changes.
            Sequence walked = Sequence;
            SequenceAutopilot pilot = Autopilot;
            Step step = walked != null ? walked.Current : null;
            bool finished = walked != null && walked.Finished, piloted = pilot != null && pilot.enabled;
            if (_saidOnce && step == _saidStep && finished == _saidFinished && piloted == _saidPiloted) return;
            _saidOnce = true;
            _saidStep = step;
            _saidFinished = finished;
            _saidPiloted = piloted;
            ICommandSurface on = Surface;
            if (on != null) on.SetStatus(Status());
        }
    }
}
