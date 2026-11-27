using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;
using M3trik.Unitytk.Steps;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>pip</c> ("PiP screen shows the bolt"): the task's picture-in-picture
    /// (<see cref="TaskKit.PipName(ManifestScene)"/>, coming up as the task's <see cref="TaskSettings.pipStyle"/> says) shows
    /// the row's part close up from its step on, captioned with what the row says it shows (<see cref="TaskSettings.captionPattern"/>),
    /// until a hiding <c>pip</c> row's step (<see cref="TaskSettings.pipShow"/>) or the next showing one's, whose picture replaces
    /// it. The picture is taken from the object the row names as where it is (its <c>where</c>: a camera an artist set up, used
    /// exactly as it stands, <see cref="Panels.PanelsActions.SetShotFrom(PipShot, Transform, OpContext)"/>), else from a spot the
    /// kit places for the step under the part, riding it (<see cref="Panels.PanelsActions.AddShotFrom(PipShot, OpContext)"/>):
    /// one that sees the part as the scene then stands (a door an earlier step opened stands open), on the user's side where one
    /// does, kept where a person moved it by its manifest (<see cref="Manifest.moved"/>). Where the picture comes up is the
    /// task's (<see cref="TaskSettings.pipStyle"/>, <see cref="TaskSettings.pipAnchorPattern"/>: its one screen), never a row's.
    /// </summary>
    [TaskBehavior("pip",
                      About = "The picture-in-picture shows the object close up from its step on, or (by its setting) the picture an earlier row put up goes.",
                      Settings = new[] { nameof(TaskSettings.pipShow), nameof(TaskSettings.captionPattern), nameof(TaskSettings.captionFrom),
                                         nameof(TaskSettings.pipFieldOfView) },
                      Suffix = new[] { nameof(TaskSettings.pipShow) }, Fields = new[] { TaskKit.Fields.From })]
    internal sealed class PipBehavior : IManifestBehavior, IManifestBound
    {
        /// <inheritdoc/>
        public IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                            ManifestScene scene, string target)
        {
            // The camera the kit made for the picture (a delivered one's field of view is the DCC's): a person's change of
            // its field of view is the row's.
            if (action.targets == null || action.targets.Count == 0 || target != action.targets[0]) yield break;
            Camera lens = OwnLens(scene, step);
            if (lens != null) yield return (nameof(TaskSettings.pipFieldOfView), lens, FieldOfViewProperty);
        }

        // Camera's serialized name for its field of view.
        const string FieldOfViewProperty = "field of view";

        // The camera of step's picture's own spot, the one the kit made and placed (Spots.View); null where the picture is
        // taken from a delivered camera, or none is made yet.
        static Camera OwnLens(ManifestScene scene, SheetPlan.Shape.PlanStep step)
        {
            GameObject root = SceneRoots.Find(TaskKit.PipName(scene));
            Transform node = root == null ? null : root.transform.Find(step.id);
            PipShot shot = node != null ? node.GetComponent<PipShot>() : null;
            if (shot == null || shot.from == null) return null;
            bool made = shot.from.TryGetComponent(out Placement.PlacedSpot spot) && !spot.key.StartsWith(Spots.GivenKey, System.StringComparison.Ordinal);
            return made && shot.from.TryGetComponent(out Camera lens) ? lens : null;
        }

        /// <summary>
        /// What a <c>pip</c> row says its picture shows (<paramref name="pattern"/>'s <c>what</c> group: "PiP screen shows
        /// the bolt", "The bolt"); its text where the pattern does not match it, or names no such group.
        /// </summary>
        /// <param name="action">The plan's action.</param>
        /// <param name="pattern">A regular expression (<see cref="TaskSettings.captionPattern"/>, <see cref="TaskKit.Pattern"/>); null: the row's text.</param>
        /// <param name="fromWords">Whether a row with no Label is captioned from its words at all (<see cref="TaskSettings.captionFrom"/>); false: it has no caption.</param>
        internal static string Caption(SheetPlan.Shape.PlanAction action, Regex pattern, bool fromWords = true)
        {
            // A row in cells says its caption in its Label (else what it shows): the plan's label field, as written.
            string label = action.Field("label");
            if (!string.IsNullOrEmpty(label)) return label.Trim();
            if (!fromWords) return "";  // its Label alone: with none, no caption (never its object's name)
            string text = (action.text ?? "").Trim();
            Match said = pattern == null ? Match.Empty : pattern.Match(text);
            string what = said.Success && said.Groups["what"].Success ? said.Groups["what"].Value.Trim() : text;
            return what.Length == 0 ? what : char.ToUpperInvariant(what[0]) + what.Substring(1);
        }

        /// <summary>The row of action's behavior whose picture is still shown as <paramref name="action"/> comes, with its step; none where a hiding row took it away, or none came before.</summary>
        internal static (SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action) Shown(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            foreach (var each in PlanOrder.Before(scene.Plan, action))
            {
                if (each.action.behavior != action.behavior) continue;
                return Hides(each.action, each.step, scene) ? default : each;
            }
            return default;
        }

        /// <summary>Whether <paramref name="action"/>, a pip row of <paramref name="step"/>, takes the picture away (<see cref="TaskSettings.pipShow"/>: "PiP screen fades away").</summary>
        internal static bool Hides(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene)
            => scene.Settings<TaskSettings>(step.id, action.targets != null && action.targets.Count > 0 ? action.targets[0] : "", action).pipShow == Showing.Hide;

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            SheetPlan.Shape.PlanStep step = PlanOrder.StepOf(scene.Plan, action);
            if (step != null && Hides(action, step, scene))
            {
                if (Shown(action, scene).step == null) yield return "no row before it shows a picture-in-picture";
                yield break;
            }
            if (action.targets == null || action.targets.Count == 0)
                yield return "it names nothing to show: list the part in Asset Names";
            string from = action.Field(TaskKit.Fields.From);
            if (!string.IsNullOrEmpty(from) && scene.Node(from) == null)
                yield return $"the delivery has no '{from}' to take its picture from: correct its where, or have the artist add the camera";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            if (Hides(action, step, scene))
            {
                var (shown, _) = Shown(action, scene);
                Hide(TaskKit.PipName(scene), shown.id, step.id, scene, ctx);
                return;
            }
            string pip = TaskKit.PipName(scene);
            TaskSettings own = scene.Settings<TaskSettings>(step.id, action.targets[0], action);
            PipStyle style = scene.Settings<TaskSettings>().pipStyle;
            Transform anchor = style == PipStyle.Anchored ? TaskKit.PipAnchor(scene, ctx) : null;
            PipView view = PanelsActions.MakePip(pip, style, anchor, ctx);
            Transform part = scene.Node(action.targets[0]);
            PipShot shot = PanelsActions.AddShot(pip, step.id, part, Caption(action, TaskKit.Pattern(scene, nameof(own.captionPattern), own.captionPattern, ctx, RegexOptions.IgnoreCase), own.captionFrom == CaptionFrom.Words), ctx: ctx);
            string named = action.Field(TaskKit.Fields.From);
            Transform from = string.IsNullOrEmpty(named) ? null : scene.Node(named);
            float angle = Angle(own.pipFieldOfView, scene.Settings<TaskSettings>().pipFieldOfView, view, from, step, ctx);
            if (from == null)
            {
                Spots.View(scene, step, part, pip, shot, angle, view != null ? view.margin : PipView.DefaultMargin, TaskKit.Helper(action, 0), ctx);
                Camera lens = OwnLens(scene, step);
                if (lens != null) PanelsActions.SetFieldOfView(lens, angle, ctx);
            }
            else if (shot != null) PanelsActions.SetShotFrom(shot, from, ctx);  // the object is the place: nothing is searched for
            else PanelsActions.SetShotFrom(PanelsActions.PlannedShot(pip, step.id), from, ctx);
            Spots.Given(scene, from, ctx);
            if (shot != null) scene.EnableFrom(step.id, shot.gameObject, shot, ctx);
            else scene.EnableFrom<PipShot>(step.id, PanelsActions.PlannedShot(pip, step.id), ctx);
            var (before, _) = Shown(action, scene);
            if (before != null && before.id != step.id) Hide(pip, before.id, step.id, scene, ctx);
        }

        /// <summary>The narrowest and widest field of view a camera takes, in degrees (as Unity clamps a camera's).</summary>
        internal const float LeastAngle = 1f, MostAngle = 179f;

        // The field of view the picture's own camera frames with, and is placed for: the row's (a person's, kept; 0: none),
        // else the picture view's; one outside what a camera takes is brought within it, and said. A row whose picture is
        // taken from a camera the delivery brings (its where) keeps that camera's own: a field of view the row sets of its own
        // (other than the task's, kit) is said unused.
        static float Angle(float rows, float kit, PipView view, Transform from, SheetPlan.Shape.PlanStep step, OpContext ctx)
        {
            if (from != null)
            {
                if (rows > 0f && !Mathf.Approximately(rows, kit))
                    ctx.Raise(ManifestsActions.NoteCode, $"{step.id}: its picture is taken from '{from.name}', a camera the delivery brings, which keeps " +
                                                         $"its own field of view: its pipFieldOfView {rows:0.##} is unused (set it on that camera where it was made)");
                return view != null ? view.fieldOfView : PipView.DefaultFieldOfView;
            }
            float angle = rows > 0f ? rows : view != null ? view.fieldOfView : PipView.DefaultFieldOfView;
            float taken = Mathf.Clamp(angle, LeastAngle, MostAngle);
            if (!Mathf.Approximately(taken, angle))
                ctx.Raise(ManifestsActions.NoteCode, $"{step.id}: a field of view of {angle:0.##} degrees is no camera's: its picture is framed and " +
                                                     $"placed for {taken:0.##} (between {LeastAngle:0} and {MostAngle:0}; 0: the picture view's)");
            return taken;
        }

        /// <summary>Take the picture <paramref name="shown"/>'s step put up away as step <paramref name="id"/> starts.</summary>
        internal static void Hide(string pip, string shown, string id, ManifestScene scene, OpContext ctx)
        {
            GameObject root = SceneRoots.Find(pip);
            Transform node = root == null ? null : root.transform.Find(shown);
            PipShot shot = node != null ? node.GetComponent<PipShot>() : null;
            if (shot != null) scene.DisableFrom(id, shot.gameObject, shot, ctx);
            else scene.DisableFrom<PipShot>(id, PanelsActions.PlannedShot(pip, shown), ctx);
        }
    }
}
