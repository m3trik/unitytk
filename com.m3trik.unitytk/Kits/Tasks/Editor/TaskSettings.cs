using M3trik.Unitytk.Indicators;
using M3trik.Unitytk.Panels;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// A task's own choices the kit builds by (<see cref="Manifests.ManifestScene.Settings{T}()"/>): where
    /// the user stands, how the picture-in-picture comes up, how long a step that goes on by itself runs,
    /// what a wrong answer says, how large its arrows are, how a row's words read, what its objects are
    /// named, where its adjustments are kept and whether it gets its debug tools. A manifest keeps its own
    /// (<see cref="Manifests.Manifest.settings"/>, edited in the Manifest window), so two tasks in one
    /// project build differently; one with none builds by these defaults. A row's or one object's own
    /// values go over them (<see cref="Manifests.ManifestScene.Settings{T}(string, string, Manifests.SheetPlan.Shape.PlanAction)"/>),
    /// for the settings a behavior lists as its own; the rest are the task's alone, one for every row: where a spot is
    /// searched for (<see cref="standOff"/>, <see cref="viewAngle"/>), a step's pace (<see cref="wordsPerSecond"/>,
    /// <see cref="minimumDwell"/>, <see cref="voiceTail"/>), the picture's screen (<see cref="pipStyle"/>,
    /// <see cref="pipAnchorPattern"/>) and the key pad's keys (<see cref="numberKeys"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "unitytk/Tasks Settings", fileName = "Tasks Settings")]
    public sealed class TaskSettings : ScriptableObject
    {
        [Tooltip("How far from what a teleport row faces the user stands, in metres: a spot the build places stands at least " +
                 "this far from the part's side; a viewpoint the delivery authors brings the user toward the part to this far (0: on it).")]
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

        [Tooltip("Where the picture-in-picture comes up: beside the placard, at the delivery's screen (Anchored), or in a corner of the view.")]
        public PipStyle pipStyle = PipStyle.Beside;

        [Tooltip("The field of view of the camera the kit makes for a picture, in degrees, 1 to 179 (0: the picture view's). Set " +
                 "on that camera, it is kept as the row's; a camera the delivery brings keeps its own.")]
        [Range(0f, 179f)]
        public float pipFieldOfView;

        [Tooltip("The delivery's authoring anchors, by name (a regular expression, ^UI_ for UI_ placeholders): their meshes are " +
                 "hidden as the task is built. Empty: none (every mesh of the delivery stays shown).")]
        public string anchorPattern = "";

        [Tooltip("Which node is the picture-in-picture's screen, by name (a regular expression, ^UI_PIP for a UI_PIP placeholder), " +
                 "for the Anchored style. Empty: none.")]
        public string pipAnchorPattern = "";

        [Tooltip("What a pip row says its picture shows (a regular expression whose 'what' group is the caption): " +
                 "'PiP screen shows the bolt' is captioned 'The bolt'. A row it does not match is captioned with its own words.")]
        public string captionPattern = @"(?:showing|shows|with)\s+(?<what>.+?)(?:\s+appears)?\.?$";

        [Tooltip("Where a picture's caption comes from. Words: its row's Label, else what the row's own words say it shows " +
                 "(Caption Pattern). Label: its Label alone, and none without one (a row written in cells, whose words only " +
                 "name its object).")]
        public CaptionFrom captionFrom = CaptionFrom.Words;

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

        [Tooltip("What a wrong answer says (the page stays).")]
        public string wrongChoiceReply = "Try again.";

        [Tooltip("What a part selected without the tool it needs says (the page stays): {0} is the tool, its name made readable (KEY_GRP: key); empty says nothing.")]
        public string refusedReply = "Take the {0} first.";

        [Tooltip("The last words of a node's name a person does not say, ignoring case, where it says a node (KEY_GRP: key).")]
        public string[] nameSuffixes = { "grp", "group", "geo" };

        [Tooltip("What splits a buttons row's names into one button each: punctuation as written, a word where it stands alone " +
                 "('YES and NO', 'A, B or C').")]
        public string[] labelSeparators = { ",", "&", "and", "or" };

        [Tooltip("The keys of a number pad, one a character.")]
        public string numberKeys = "0123456789";

        [Tooltip("How long the arrows the kit makes are, tip to tail, in metres (the scale of the parts they point at).")]
        [Min(0.01f)]
        public float arrowSize = Indicator.DefaultSize;

        /// <summary>The <see cref="arrowMerge"/> of new settings: things a hand's width apart share an arrow.</summary>
        public const float DefaultArrowMerge = 0.15f;

        [Tooltip("Things one row points at that stand this close together (metres between them) share one arrow, pointed at " +
                 "them all: two latches side by side. 0: an arrow at each.")]
        [Min(0f)]
        public float arrowMerge = DefaultArrowMerge;

        [Tooltip("How a Use row's object plays its take: a click (the usual), held to play (a button held down while the " +
                 "lid closes: let go, it stops), or a timed press (held for Press Seconds, then it plays). Set it for one " +
                 "object in the Manifest window: the chip's Settings for that object.")]
        public UseTrigger useTrigger = UseTrigger.Click;

        [Tooltip("With a timed press: how long the object is held pressed before its take plays, in seconds.")]
        [Min(0.1f)]
        public float pressSeconds = 1.5f;

        [Tooltip("A take played while held (Press Hold rows, and Use rows held to play) plays back to its start as it is " +
                 "let go, rather than stopping where it stands: the user holds until it is done.")]
        public bool releaseRewinds;

        [Tooltip("Where a Pause row holds its step: as it finishes (its button offered once its actions are done), or " +
                 "at its start (its button starts them). A row's words may say which; set one object's in the Manifest window.")]
        public PauseWhen pauseWhen = PauseWhen.Finish;

        [Tooltip("Whether a Marker row puts the ring on the floor up or takes it away (its words say which).")]
        public Showing markerShow = Showing.Show;

        [Tooltip("Whether a PiP row puts the picture up or takes it away (its words say which).")]
        public Showing pipShow = Showing.Show;

        [Tooltip("Whether a Checklist row puts the list on the placard or takes it away (the sheet's until says where it goes).")]
        public Showing checklistShow = Showing.Show;

        [Tooltip("Which values keyed in an Answer row takes: its own, or any other (its words say which).")]
        public AnswerKind answerKind = AnswerKind.Value;

        [Tooltip("The colour of the arrows the kit makes (the highlight's, so the task's cues read as one).")]
        public Color arrowColor = RenderEffects.Highlight.DefaultColor;

        [Tooltip("When an arrow or a highlight at something the user acts on shows. Ready: once what that needs is in hand, until " +
                 "they have acted on it (the keys are pointed out first, then the locks). Step: for the whole step.")]
        public CueWhen cueWhen = CueWhen.Ready;

        [Tooltip("A highlight pulses as long as its step runs; off: it pulses once, then glows steady.")]
        public bool highlightLoop = true;

        [Tooltip("How a highlight glows. Auto: pulsing where the task has the user act on the object, steady where they only look. " +
                 "Pulse and Steady say it outright (the sheet's style).")]
        public HighlightStyle highlightStyle = HighlightStyle.Auto;

        [Tooltip("The colour a highlight glows in (the sheet's color). By default the colour the delivery's own highlights are keyed in, " +
                 "so one built here and one built there read alike.")]
        [ColorUsage(false, true)]
        public Color highlightColor = RenderEffects.Highlight.DefaultColor;

        [Tooltip("How long a fade in or out takes, in seconds (the sheet's fade).")]
        [Min(0f)]
        public float fadeSeconds = RenderEffects.FadeTo.DefaultSeconds;

        [Tooltip("How fast a row's take plays: 1 as it was animated, 2 twice as fast (a Play row's speed in the sheet). " +
                 "Set on its clip's entry of the Clip Player, it is kept as the row's.")]
        [Min(0.01f)]
        public float playSpeed = 1f;

        [Tooltip("Seconds into a row's take it plays from (below 0: found from the take's motion). Set on its clip's entry " +
                 "of the Clip Player, it is kept as the row's, and the entry's Auto goes off.")]
        public float playFrom = -1f;

        [Tooltip("Seconds into a row's take it plays to, and holds (below 0: found from the take's motion). Set on its " +
                 "clip's entry of the Clip Player, it is kept as the row's, and the entry's Auto goes off.")]
        public float playUntil = -1f;

        [Tooltip("A part brought to its slot (a Place row) stays where its take puts it, out of the hand: fitted. Off: it " +
                 "comes back to the hand as its take ends, as a tool does (a scanner held up to a tag).")]
        public bool placedStays = true;

        [Tooltip("Fade out an object its step also sends back where it rests, as the sheet's fade says: it rests there " +
                 "hidden. Off: it goes back shown, the return being how it leaves the hand.")]
        public bool fadeReturned;

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

        [Tooltip("How far behind the task's first viewpoint a desktop rig just made starts, in metres, so its marker shows ahead.")]
        [Min(0f)]
        public float startBack = 2f;

        [Tooltip("The names of the objects the build makes in the scene's top level, and of a viewpoint it places.")]
        public TaskNames names = new TaskNames();

        [Tooltip("Give the task its debug tools (a panel beside the eyes: the step, Skip, Back, Restart, Autopilot), for trying " +
                 "it in the editor or reviewing it in a headset; off for a task that ships.")]
        public bool debugTools;
    }
}
