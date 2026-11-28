using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// A task's own choices the kit builds by (<see cref="Manifests.ManifestScene.Settings{T}()"/>): one group a word that
    /// has settings, named as the word, its fields named as the sheet's settings (<see cref="indicate"/>'s <c>size</c>,
    /// <c>color</c>, <c>style</c>) beside the kit's own for that word; then the task's alone, one for every row: where a
    /// spot is searched for (<see cref="standOff"/>, <see cref="viewAngle"/>), a step's pace (<see cref="wordsPerSecond"/>,
    /// <see cref="minimumDwell"/>, <see cref="voiceTail"/>), the key pad's keys (<see cref="numberKeys"/>), what its objects
    /// are named, its rig and whether it gets its debug tools. A manifest keeps its own (<see cref="Manifests.Manifest.settings"/>,
    /// edited in the Manifest window), so two tasks in one project build differently; one with none builds by these
    /// defaults. A row's settings go over its word's group (<c>size=0.2</c> on an <c>indicate</c> row: <c>indicate.size</c>),
    /// and one object's own values over those (<see cref="Manifests.ManifestScene.Settings{T}(string, string, Manifests.SheetPlan.Shape.PlanAction)"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "unitytk/Tasks Settings", fileName = "Tasks Settings")]
    public sealed class TaskSettings : ScriptableObject
    {
        [Tooltip("How a highlight glows: its style, its colour, whether it pulses on, and when it shows.")]
        public HighlightSettings highlight = new HighlightSettings();

        [Tooltip("How an arrow looks: its style, size and colour, what shares one, and when it shows.")]
        public IndicateSettings indicate = new IndicateSettings();

        [Tooltip("How an object appears: its fade.")]
        public ShowSettings show = new ShowSettings();

        [Tooltip("How an object goes: its fade, and whether one its step returns goes hidden.")]
        public HideSettings hide = new HideSettings();

        [Tooltip("The state an object's indicator light turns to.")]
        public SetSettings set = new SetSettings();

        [Tooltip("How an object sets its take off (selected, held, or brought what the hand holds), and how the take plays.")]
        public UseSettings use = new UseSettings();

        [Tooltip("What a take that plays by itself follows, and how it plays.")]
        public PlaySettings play = new PlaySettings();

        [Tooltip("When a button that goes on holds its step, and what a wrong answer says.")]
        public ButtonSettings button = new ButtonSettings();

        [Tooltip("When the user is brought to a viewpoint, and how long its ring shows.")]
        public TravelSettings travel = new TravelSettings();

        [Tooltip("Where the task's picture comes up, the angle of the camera made for it, and how long it shows.")]
        public PictureSettings picture = new PictureSettings();

        [Tooltip("How long a list stays on the placard.")]
        public ListSettings list = new ListSettings();

        [Tooltip("How far from what a travel row faces the user stands, in metres: a spot the build places stands at least " +
                 "this far from the part's side.")]
        [Min(0f)]
        public float standOff = 1.5f;

        [Tooltip("The user's view, top to bottom, in degrees: a spot the build places stands back far enough to see what it " +
                 "faces whole, up to 3 m (0: at the stand-off, however large the part).")]
        [Range(0f, 120f)]
        public float viewAngle = 60f;

        [Tooltip("How far in front of a part (its -Z side) an arrow or a picture is placed from, in metres, where no viewpoint " +
                 "before its step says where the user stands.")]
        [Min(0f)]
        public float fallbackDistance = 2f;

        [Tooltip("The delivery's authoring anchors, by name (a regular expression, ^UI_ for UI_ placeholders): their meshes are " +
                 "hidden as the task is built. Empty: none (every mesh of the delivery stays shown).")]
        public string anchorPattern = "";

        [Tooltip("How fast a step's voice line (else its placard) is read, in words a second: how long a step that goes on by itself runs.")]
        [Min(0.1f)]
        public float wordsPerSecond = 2.5f;

        [Tooltip("The least a step that goes on by itself runs, in seconds.")]
        [Min(0f)]
        public float minimumDwell = 2f;

        [Tooltip("How long a step with a sound runs past its end, in seconds.")]
        [Min(0f)]
        public float voiceTail = 0.3f;

        [Tooltip("Which steps get a page on the placard. Needed: a step with something to show (its placard text, a button, " +
                 "the key pad, a reply); the placard itself only where a step has a page or a row puts a list, a tick or a " +
                 "refusal on it. Every Step: each step a page, its text its placard text, else its voice line.")]
        public PlacardPages pages = PlacardPages.Needed;

        [Tooltip("The last words of a node's name a person does not say, ignoring case, where it says a node (KEY_GRP: key).")]
        public string[] nameSuffixes = { "grp", "group", "geo" };

        [Tooltip("The keys of the key pad an answer row puts on its page, one a character.")]
        public string numberKeys = "0123456789";

        [Tooltip("The user's rig, where the scene has none of its own (a rig that travels: anything with a Traveler, a headset's " +
                 "origin given one). Desktop: the scene's main camera (one made where it has none) is the user: the steps' " +
                 "viewpoints move it, a right-button drag turns it, a hand in its view holds what is taken, and its clicks reach " +
                 "the scene. None: the scene's own.")]
        public TaskRig rig = TaskRig.Desktop;

        [Tooltip("How high a desktop rig's eyes stand over a viewpoint, in metres.")]
        [Min(0f)]
        public float eyeHeight = 1.6f;

        [Tooltip("Where a desktop rig's hand sits, from the eyes, in metres: what the user holds shows there.")]
        public Vector3 handInView = Interaction.Interactions.HandInView;

        [Tooltip("How near a desktop rig's eyes see, at most, in metres, so what the hand holds shows whole: a farther near clip " +
                 "plane is brought in to it; 0: left as it is.")]
        [Min(0f)]
        public float nearClip = Interaction.Interactions.HeldNearClip;

        [Tooltip("How far behind the task's first viewpoint a desktop rig just made starts, in metres, so its ring shows ahead.")]
        [Min(0f)]
        public float startBack = 2f;

        [Tooltip("The names of the objects the build makes in the scene's top level, and of a viewpoint it places.")]
        public TaskNames names = new TaskNames();

        [Tooltip("Give the task its debug tools (a panel beside the eyes: the step, Skip, Back, Restart, Autopilot), for trying " +
                 "it in the editor or reviewing it in a headset; off for a task that ships.")]
        public bool debugTools;
    }
}
