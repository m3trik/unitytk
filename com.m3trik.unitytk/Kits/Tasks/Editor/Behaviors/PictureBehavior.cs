using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>picture</c> ("A picture shows the bolt, captioned 'The bolt'"): the task's picture
    /// (<see cref="TaskKit.PictureName(ManifestScene)"/>, coming up as the task's <see cref="PictureSettings.style"/> says)
    /// shows the row's part close up from its step on, captioned with the row's Label (none without one), for its
    /// <see cref="PictureSettings.until"/> (a span: <c>step</c>, or until a named step starts; empty: until the next picture
    /// row's step, whose picture replaces it). The picture is taken from the object the row names as where it is (its
    /// <c>where</c>: a camera an artist set up, used exactly as it stands,
    /// <see cref="Panels.PanelsActions.SetShotFrom(PipShot, Transform, OpContext)"/>), else from a spot the kit places for the
    /// step under the part, riding it (<see cref="Panels.PanelsActions.AddShotFrom(PipShot, OpContext)"/>): one that sees the
    /// part as the scene then stands (a door an earlier step opened stands open), on the user's side where one does, kept where
    /// a person moved it by its manifest (<see cref="Manifest.moved"/>). Where the picture comes up is the task's
    /// (<see cref="PictureSettings.style"/>, <see cref="PictureSettings.anchor"/>: its one screen), never a row's.
    /// </summary>
    [TaskBehavior("picture", Settings = new[] { nameof(PictureSettings.until), nameof(PictureSettings.fieldOfView) },
                  About = "The task's picture shows the object close up from its step on, captioned with its Label, for its span.")]
    internal sealed class PictureBehavior : IManifestBehavior, IManifestBound
    {
        // Its setting a person's change to the camera the kit made is kept as (IManifestBound), by its path.
        const string FieldOfView = nameof(TaskSettings.picture) + "." + nameof(PictureSettings.fieldOfView);

        /// <inheritdoc/>
        public IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                            ManifestScene scene, string target)
        {
            // The camera the kit made for the picture (a delivered one's field of view is the DCC's): a person's change of
            // its field of view is the row's.
            if (action.targets == null || action.targets.Count == 0 || target != action.targets[0]) yield break;
            Camera lens = OwnLens(scene, step);
            if (lens != null) yield return (FieldOfView, lens, FieldOfViewProperty);
        }

        // Camera's serialized name for its field of view.
        const string FieldOfViewProperty = "field of view";

        // The camera of step's picture's own spot, the one the kit made and placed (Spots.View); null where the picture is
        // taken from a delivered camera, or none is made yet.
        static Camera OwnLens(ManifestScene scene, SheetPlan.Shape.PlanStep step)
        {
            GameObject root = SceneRoots.Find(TaskKit.PictureName(scene));
            Transform node = root == null ? null : root.transform.Find(step.id);
            PipShot shot = node != null ? node.GetComponent<PipShot>() : null;
            if (shot == null || shot.from == null) return null;
            bool made = shot.from.TryGetComponent(out Placement.PlacedSpot spot) && !spot.key.StartsWith(Spots.GivenKey, System.StringComparison.Ordinal);
            return made && shot.from.TryGetComponent(out Camera lens) ? lens : null;
        }

        // How long action's picture shows: its until, else until the next picture row's step (none: to the end).
        static string Until(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, PictureSettings own)
            => string.IsNullOrEmpty(own.until) ? Spans.Next(scene, step, action) : own.until;

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (action.targets == null || action.targets.Count == 0)
            {
                yield return "it names nothing to show: set its Target";
                yield break;
            }
            SheetPlan.Shape.PlanStep step = PlanOrder.StepOf(scene.Plan, action);
            string until = Spans.Problem(scene.Plan, step, scene.Settings<TaskSettings>(step?.id ?? "", action.targets[0], action).picture.until, "until");
            if (until != null) yield return until;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string picture = TaskKit.PictureName(scene);
            PictureSettings own = scene.Settings<TaskSettings>(step.id, action.targets[0], action).picture;
            PictureSettings task = scene.Settings<TaskSettings>().picture;  // the task's one screen
            Transform anchor = task.style == PipStyle.Anchored ? TaskKit.PictureAnchor(scene, ctx) : null;
            PipView view = PanelsActions.MakePip(picture, task.style, anchor, ctx);
            Transform part = scene.Node(action.targets[0]);
            string caption = (action.Field(TaskKit.Fields.Label) ?? "").Trim();
            PipShot shot = PanelsActions.AddShot(picture, step.id, part, caption, ctx: ctx);
            string named = TaskKit.Where(action);
            Transform from = named == null ? null : scene.Node(named);
            float angle = Angle(own.fieldOfView, task.fieldOfView, view, from, step, ctx);
            if (from == null)
            {
                Spots.View(scene, step, part, picture, shot, angle, view != null ? view.margin : PipView.DefaultMargin, TaskKit.Helper(action, 0), ctx);
                Camera lens = OwnLens(scene, step);
                if (lens != null) PanelsActions.SetFieldOfView(lens, angle, ctx);
            }
            else if (shot != null) PanelsActions.SetShotFrom(shot, from, ctx);  // the object is the place: nothing is searched for
            else PanelsActions.SetShotFrom(PanelsActions.PlannedShot(picture, step.id), from, ctx);
            Spots.Given(scene, from, ctx);
            string until = Until(action, step, scene, own);
            if (shot != null) Spans.Show(scene, step, until, shot.gameObject, shot, ctx);
            else Spans.Show<PipShot>(scene, step, until, PanelsActions.PlannedShot(picture, step.id), ctx);
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
                                                         $"its own field of view: its fieldOfView {rows:0.##} is unused (set it on that camera where it was made)");
                return view != null ? view.fieldOfView : PipView.DefaultFieldOfView;
            }
            float angle = rows > 0f ? rows : view != null ? view.fieldOfView : PipView.DefaultFieldOfView;
            float taken = Mathf.Clamp(angle, LeastAngle, MostAngle);
            if (!Mathf.Approximately(taken, angle))
                ctx.Raise(ManifestsActions.NoteCode, $"{step.id}: a field of view of {angle:0.##} degrees is no camera's: its picture is framed and " +
                                                     $"placed for {taken:0.##} (between {LeastAngle:0} and {MostAngle:0}; 0: the picture view's)");
            return taken;
        }
    }
}
