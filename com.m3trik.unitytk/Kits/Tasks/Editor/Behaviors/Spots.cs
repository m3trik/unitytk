using M3trik.Unitytk.Manifests;
using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;
using M3trik.Unitytk.Placement;
using M3trik.Unitytk.Viewpoints;
using UnityEngine;
using PlanAction = M3trik.Unitytk.Manifests.SheetPlan.Shape.PlanAction;
using PlanStep = M3trik.Unitytk.Manifests.SheetPlan.Shape.PlanStep;

namespace M3trik.Unitytk.Kits.Tasks
{
    // The spots the kit places as the scene stands at a step (its parts where earlier steps' clips
    // left them): where a row that travels brings the user when the sheet names no viewpoint, where
    // the picture looks from, where an arrow points from. Each is a PlacedSpot keyed by its
    // step, so an adjustment its manifest keeps puts it there on every build.
    static class Spots
    {
        // The clips played before step, and step's own too where atEnd, in plan order.
        internal static List<AnimationClip> PlayedBefore(ManifestScene scene, PlanStep step, bool atEnd)
        {
            var clips = new List<AnimationClip>();
            bool reached = false;
            foreach (var (each, action) in PlanOrder.All(scene.Plan))
            {
                if (each == step) reached = true;
                else if (reached) break;
                if (each == step && !atEnd) break;
                if (!string.IsNullOrEmpty(action.clip)) clips.Add(scene.Clip(action.clip));
            }
            return clips;
        }

        // The scene as it stands at step (its own clips played too, atEnd), its meshes blocking the
        // view for the search; disposing puts it back.
        internal static IDisposable At(ManifestScene scene, PlanStep step, bool atEnd)
        {
            IDisposable posed = Interactions.PosedAfter(scene.Root, PlayedBefore(scene, step, atEnd));
            return new Both(new Occluders(scene.Roots), posed);
        }

        // Whether action, a row of step, brings the user to a viewpoint (TaskBehaviorAttribute.Travels).
        static bool Travels(PlanAction action, PlanStep step) => TaskBehaviors.Traits(action, step).Travels;

        // Whether travel, a row of step, brings the user there as the step ends (TravelSettings.when): once its choice is made.
        internal static bool AtEnd(ManifestScene scene, PlanStep step, PlanAction travel)
            => scene.Settings<TaskSettings>(step.id, "", travel).travel.when == Moment.End;

        // What travel brings the user to face (its Target); null where it names none.
        internal static Transform Faced(ManifestScene scene, PlanAction travel)
            => travel != null && travel.targets != null && travel.targets.Count > 0 ? scene.Node(travel.targets[0]) : null;

        // Where the user stands while step runs: the spot of the last travel before it (or one
        // early in it), else (not earlierOnly) the task's first; null where none is placed yet.
        // except: a travel not counted (the one being placed). Viewpoints the kit places are all
        // placed before any other spot (TaskBuild.Begin), so a spot is placed by the same ones on
        // every build.
        internal static Transform Standing(ManifestScene scene, PlanStep step, PlanAction except = null, bool earlierOnly = false)
            => StandingBy(scene, step, except, earlierOnly).spot;

        // Standing's spot, with the travel that brings the user there and the step it is in.
        static (Transform spot, PlanAction by, PlanStep at) StandingBy(ManifestScene scene, PlanStep step, PlanAction except = null, bool earlierOnly = false)
        {
            (Transform, PlanAction, PlanStep) spot = default, first = default;
            bool reached = false;
            foreach (var (each, action) in PlanOrder.All(scene.Plan))
            {
                if (each == step) reached = true;
                else if (reached) break;
                if (!Travels(action, each) || action == except) continue;
                Transform at = Existing(scene, each, action);
                if (first.Item1 == null) first = (at, action, each);
                if (each == step && AtEnd(scene, each, action)) continue;  // they go there as it ends
                if (at != null) spot = (at, action, each);
            }
            if (spot.Item1 != null || earlierOnly) return spot;
            if (first.Item1 != null) return first;
            foreach (var (each, action) in PlanOrder.All(scene.Plan))
            {
                if (!Travels(action, each) || action == except) continue;
                Transform later = Existing(scene, each, action);
                if (later != null) return (later, action, each);
            }
            return default;
        }

        // Where the user's eyes are and the way they face while step runs: as the travel that brought
        // them there left them (its viewpoint, as it stands, facing its forward: a spot the kit placed was
        // placed facing what its row faces, and one an artist made is the truth). Null where no spot is
        // placed yet. Asked before the scene is posed for the step itself.
        internal static (Vector3 eyes, Quaternion heading)? User(ManifestScene scene, PlanStep step)
        {
            var (spot, by, at) = StandingBy(scene, step);
            if (spot == null) return null;
            TaskSettings settings = scene.Settings<TaskSettings>();
            using (Interactions.PosedAfter(scene.Root, PlayedBefore(scene, at, AtEnd(scene, at, by))))
            {
                TravelTo.Stand(spot, null, 0f, out Vector3 floor, out Quaternion heading);
                return (floor + Vector3.up * settings.eyeHeight, heading);
            }
        }

        // The user's eyes while step runs; null where no spot is placed yet.
        internal static Vector3? Eyes(ManifestScene scene, PlanStep step) => User(scene, step)?.eyes;

        // Whether a person placed the spot kept under key (an adjustment its manifest keeps): it stands where they put it,
        // so what the search found, or could not, is no longer said.
        static bool Kept(ManifestScene scene, string key) => scene.Tuning().TryGet(key, out _);

        // How a person's move of a spot is kept, closing the note that asks for it.
        const string Keeps = "(its manifest keeps it from the next build on)";

        // The key prefix of a helper someone made and a row names (Given): no search's.
        internal const string GivenKey = "where/";

        // The fix a placed spot that no longer does its job is given: its first placement holds until a person asks.
        const string Again = "Place Again (the row's menu in the Manifest window) searches anew, or move it where it does";

        // Whether travel's viewpoint is one the kit places (its row names no where).
        internal static bool Placed(PlanAction travel) => travel == null || TaskKit.Where(travel) == null;

        // The key a travel's spot is kept by: its step's, and, for each after the first a step places (one as the step ends
        // beside one as it starts), its place among them, so each keeps its own adjustment.
        internal static string TravelKey(PlanStep step, PlanAction travel)
        {
            int at = PlacedAt(step, travel);
            return at > 1 ? $"travel/{step.id}/{at}" : $"travel/{step.id}";
        }

        // Whether key is one a placed travel of step is kept by (TravelKey).
        internal static bool IsTravelKey(string key, PlanStep step)
            => key == $"travel/{step.id}" || key != null && key.StartsWith($"travel/{step.id}/", StringComparison.Ordinal);

        // Which of step's travels that the kit places travel is, from 1, in the step's order; 0 where it is none of them.
        internal static int PlacedAt(PlanStep step, PlanAction travel)
        {
            int at = 0;
            if (step == null || step.actions == null) return 0;
            foreach (PlanAction each in step.actions)
            {
                if (each == null || !Travels(each, step) || !Placed(each)) continue;
                at++;
                if (each == travel) return at;
            }
            return 0;
        }

        // A travel's viewpoint, made or not: the one its row names (its where), else the one the kit places for its step.
        internal static Transform Existing(ManifestScene scene, PlanStep step, PlanAction action)
        {
            if (!Placed(action)) return scene.Node(TaskKit.Where(action));
            GameObject root = SceneRoots.Find(TaskKit.ViewpointsName(scene));
            return root == null ? null : root.transform.Find(TaskKit.ViewpointName(step, action, scene.Settings<TaskSettings>()));
        }

        // Where travel's TravelTo is: the one made on its viewpoint, else, in a dry run that only plans it, the planned object
        // its live run puts it on.
        internal static (TravelTo made, PlannedObject planned) Travel(ManifestScene scene, PlanStep step, PlanAction travel)
        {
            Transform view = Existing(scene, step, travel);
            PlannedObject planned = view != null ? new PlannedObject(view.name, view.parent)
                : ViewpointsActions.PlannedViewpoint(TaskKit.ViewpointsName(scene), TaskKit.ViewpointName(step, travel, scene.Settings<TaskSettings>()));
            TravelTo made = view != null ? view.GetComponent<TravelTo>() : null;
            return (made, made == null ? planned : null);
        }

        // The viewpoint action (a travel) brings the user to: the one its row names (its where), else one the kit
        // places for its step (ViewpointsActions.AddViewpoint: TaskKit.ViewpointName under
        // TaskKit.ViewpointsName) where what the row faces (its Target) is seen, as the scene then stands; null in a
        // dry run that plans it.
        internal static GameObject Viewpoint(ManifestScene scene, PlanStep step, PlanAction action, OpContext ctx)
        {
            if (!Placed(action)) return scene.Node(TaskKit.Where(action)).gameObject;
            TaskSettings settings = scene.Settings<TaskSettings>();
            string holder = TaskKit.ViewpointsName(scene), name = TaskKit.ViewpointName(step, action, settings);
            GameObject node = ViewpointsActions.AddViewpoint(holder, name, default, ctx);
            if (node == null)
            {
                // A dry run's: the viewpoint is planned, so is the spot placed on it.
                Placements.Place(ViewpointsActions.PlannedViewpoint(holder, name), TravelKey(step, action), ctx);
                return null;
            }
            Transform facing = Faced(scene, action);
            bool kept = Kept(scene, TravelKey(step, action));
            // What the plan asks of the spot: what it faces, what the user acts on there, the side named. Where things stand is
            // no part of it, so a person's move of something never has the search run again.
            List<(Bounds part, Transform node)> acting = ActedOnThere(scene, step, action);
            string basis = scene.Under(facing) + "|" + string.Join(",", acting.Select(a => scene.Under(a.node)).OrderBy(p => p, StringComparer.Ordinal))
                           + "|" + action.Field(TaskKit.Fields.Side) + "/" + action.Field(TaskKit.Fields.SideOf);
            PlacedSpot first = Placements.PlacedAs(node, TravelKey(step, action), facing, basis);
            if (first != null)
            {
                // Placed before: its first placement holds (the search runs once); checked, never searched again.
                if (!kept)
                    using (At(scene, step, AtEnd(scene, step, action)))
                        if (Placer.Seen(node.transform.position + Vector3.up * settings.eyeHeight, SceneBounds.Of(facing), facing) <= 0f)
                            ctx.Raise(TaskKit.SpotCode, $"{step.id}: {name} no longer sees '{facing.name}' (the scene changed since it was placed): {Again}");
                scene.Hold(Placements.Place(node, TravelKey(step, action), facing, PlacedSpot.ToWorld(facing, new Pose(first.placedPosition, first.placedRotation)),
                                            scene.Tuning(), ctx, basis));
                return node;
            }
            Transform before = Standing(scene, step, action, earlierOnly: true);
            Vector3? near = before == null ? (Vector3?)null : before.position;
            // Found as the scene then stands; placed once it is back (a spot is kept relative to the part at rest).
            Pose pose;
            List<Bounds> later = MovesThere(scene, step, action);
            using (At(scene, step, AtEnd(scene, step, action)))
            {
                Bounds part = SceneBounds.Of(facing);
                Placer.FloorNear(part, out float floor);
                // Where the rows there have the user act on parts of what they face (the plugs and the nut of a lock on a
                // door), they stand by those, within reach: seeing the whole door from across the room serves nobody who
                // has to touch a nut on it. What they act on elsewhere (a tool on a table) only has to be seen.
                List<(Bounds part, Transform node)> acted = acting;
                Bounds by = part;
                int within = 0;
                foreach (var (box, each) in acted)
                {
                    if (each == facing || !each.IsChildOf(facing)) continue;
                    if (within++ == 0) by = box;
                    else by.Encapsulate(box);
                }
                float off = within > 0 ? Mathf.Min(settings.standOff, Placer.DefaultReach) : settings.standOff;
                if (!Placer.Stand(by, facing, floor, near, out pose, off, settings.eyeHeight, side: Side(scene, action, facing, near),
                                  also: acted, keepOut: later, fieldOfView: settings.viewAngle, delivery: Delivered(scene)) && !kept)
                    ctx.Raise(TaskKit.SpotCode, $"{step.id}: no spot on the floor sees '{facing.name}': move {name} where it does {Keeps}");
            }
            scene.Hold(Placements.Place(node, TravelKey(step, action), facing, pose, scene.Tuning(), ctx, basis));  // its row holds it: moved, the row reads adjusted
            return node;
        }

        // What the delivery shows, as the scene now stands (its roots, extras too): the side of it a part faces out from is
        // where a spot stands where the row names none, as each DCC's Shot Manifest places it. Null where it shows nothing.
        static Bounds? Delivered(ManifestScene scene)
        {
            Bounds? all = null;
            foreach (GameObject root in scene.Roots)
            {
                if (root == null || !SceneBounds.TryOf(root.transform, out Bounds shown)) continue;
                if (all.HasValue) shown.Encapsulate(all.Value);
                all = shown;
            }
            return all;
        }

        // The rows after travel up to the next row that travels, with their steps; where the user is brought
        // there as travel's step ends, that step's own rows are done before and left out.
        static IEnumerable<(PlanStep step, PlanAction action)> RowsThere(ManifestScene scene, PlanStep step, PlanAction travel)
        {
            bool after = false, ends = AtEnd(scene, step, travel);
            foreach (var (each, action) in PlanOrder.All(scene.Plan))
            {
                if (action == travel)
                {
                    after = true;
                    continue;
                }
                if (!after) continue;
                if (Travels(action, each)) yield break;
                if (ends && each == step) continue;
                yield return (each, action);
            }
        }

        // Where things come to stand while the user is where travel brings them: what each clip played
        // there moves, as it stands once moved (a door's reach, open).
        static List<Bounds> MovesThere(ManifestScene scene, PlanStep step, PlanAction travel)
        {
            var reach = new List<Bounds>();
            List<AnimationClip> played = PlayedBefore(scene, step, AtEnd(scene, step, travel));
            foreach (var (_, action) in RowsThere(scene, step, travel))
            {
                AnimationClip clip = string.IsNullOrEmpty(action.clip) ? null : scene.Clip(action.clip);
                if (clip == null) continue;
                played.Add(clip);
                using (Interactions.PosedAfter(scene.Root, played))
                    foreach (Transform moved in Interactions.Moves(scene.Root, clip))
                        if (SceneBounds.TryOf(moved, out Bounds box)) reach.Add(box);
            }
            return reach;
        }

        // What the user acts on where travel brings them (each part a row that ActsOn names), as the scene stands now.
        static List<(Bounds part, Transform node)> ActedOnThere(ManifestScene scene, PlanStep step, PlanAction travel)
        {
            var parts = new List<(Bounds, Transform)>();
            var named = new HashSet<Transform>();
            foreach (var (each, action) in RowsThere(scene, step, travel))
            {
                if (!TaskBehaviors.Traits(action, each).ActsOn || action.targets == null) continue;
                foreach (string path in action.targets)
                {
                    Transform node = scene.Node(path);
                    if (node != null && named.Add(node)) parts.Add((SceneBounds.Of(node), node));
                }
            }
            return parts;
        }

        // The way, from the part, of the side a travel row says the user stands on (its side_of, else what it
        // faces: "to the front of the cabinet", the cabinet's forward; "the other side of the door", across it from
        // where they stood; Placer.SideDirection); null where it says none, or a side Placement does not know.
        static Vector3? Side(ManifestScene scene, PlanAction action, Transform facing, Vector3? near)
        {
            string side = action.Field(TaskKit.Fields.Side), sideOf = action.Field(TaskKit.Fields.SideOf);
            if (string.IsNullOrEmpty(side) || !Enum.TryParse(side.Trim(), true, out StandSide named) || !Enum.IsDefined(typeof(StandSide), named))
                return null;
            return Placer.SideDirection(named, string.IsNullOrEmpty(sideOf) ? facing : scene.Node(sideOf), near);
        }

        // A helper a row names as where it happens (an artist's viewpoint, arrow or camera, which the delivery brought)
        // is used as it stands and marked a placed spot (Placements.PlaceGiven), keyed by its path under the delivery: so
        // one a person moves here is kept, listed on its row and put back like any spot a build placed.
        internal static void Given(ManifestScene scene, Transform helper, OpContext ctx)
        {
            if (helper == null) return;
            scene.Hold(Placements.PlaceGiven(helper.gameObject, GivenKey + scene.Under(helper), scene.Tuning(), ctx));
        }

        // An arrow's key: its step and what it points at, named as the plan names a node (its path under the delivery), so
        // the delivery's own name, which a re-export under a new (dated) file name changes, is no part of it; an aim outside
        // the delivery goes by where it stands in the scene.
        static string PointKey(ManifestScene scene, PlanStep step, Transform aim) => $"indicate/{step.id}/{scene.Under(aim)}";

        // Point indicator (on part's child named name) at the things aims names of part (part itself, or
        // one of the several things it is) from where the user sees them as step runs.
        internal static void Point(ManifestScene scene, PlanStep step, Transform part, IReadOnlyList<Transform> aims, string name,
                                   Indicators.Indicator indicator, OpContext ctx)
        {
            Transform aim = aims[0];
            if (indicator == null)
            {
                Placements.Place(new PlannedObject(name, part), PointKey(scene, step, aim), ctx);  // a dry run's: the indicator is planned
                return;
            }
            Vector3? eyes = Eyes(scene, step);  // before the scene is posed for the step
            float back = scene.Settings<TaskSettings>().fallbackDistance;
            bool kept = Kept(scene, PointKey(scene, step, aim));
            string basis = string.Join(",", aims.Select(a => scene.Under(a)).OrderBy(p => p, StringComparer.Ordinal));  // what it points at
            PlacedSpot first = Placements.PlacedAs(indicator.gameObject, PointKey(scene, step, aim), part, basis);
            if (first != null)
            {
                // Placed before: its first placement holds (the search runs once); checked, never searched again.
                if (!kept && eyes.HasValue)
                    using (At(scene, step, false))
                        if (Placer.Seen(eyes.Value, new Bounds(indicator.transform.position, Vector3.one * 0.02f), indicator.transform) <= 0f)
                            ctx.Raise(TaskKit.SpotCode, $"{step.id}: the arrow at '{aim.name}' is no longer seen from where the user stands: {Again}");
                scene.Hold(Placements.Place(indicator.gameObject, PointKey(scene, step, aim), part,
                                            PlacedSpot.ToWorld(part, new Pose(first.placedPosition, first.placedRotation)), scene.Tuning(), ctx, basis));
                return;
            }
            // Found as the scene then stands; placed once it is back, where it rides the part to.
            Pose relative;
            using (At(scene, step, false))
            {
                Bounds seen = SceneBounds.Of(aim);
                foreach (Transform also in aims) seen.Encapsulate(SceneBounds.Of(also));
                Vector3 from = eyes ?? seen.center + Vector3.back * back;
                if (!Placer.Point(seen, aims.Count == 1 ? aim : part, from, out Pose pose, length: indicator.size, travel: indicator.bob) && !kept)
                    ctx.Raise(TaskKit.SpotCode, $"{step.id}: no spot beside '{aim.name}' is both free and seen from where the user stands, so its " +
                                                    $"arrow may be hidden or in the way: move it where it is seen {Keeps}");
                relative = PlacedSpot.ToRelative(part, pose);
            }
            scene.Hold(Placements.Place(indicator.gameObject, PointKey(scene, step, aim), part, PlacedSpot.ToWorld(part, relative), scene.Tuning(), ctx, basis));
        }

        // Where shot's camera (a shot of the task's picture, named picture) looks at part from, as the scene stands as step starts: the shot's own spot
        // (PanelsActions.AddShotFrom: under the part, riding it, named name where the row's helper has one), placed where
        // it sees the part, on the user's side where one does.
        internal static void View(ManifestScene scene, PlanStep step, Transform part, string picture, PipShot shot,
                                  float fieldOfView, float margin, string name, OpContext ctx)
        {
            string key = $"picture/{step.id}";
            if (shot == null)
            {
                // A dry run's: the shot is planned, so are its spot and the spot's place.
                Placements.Place(PanelsActions.AddShotFrom(PanelsActions.PlannedShot(picture, step.id), part, name, ctx), key, ctx);
                return;
            }
            GameObject node = PanelsActions.AddShotFrom(shot, name, fieldOfView, ctx);
            if (node == null)
            {
                Placements.Place(new PlannedObject(string.IsNullOrEmpty(name) ? PanelsActions.SpotName(shot.name) : name, part), key, ctx);  // a dry run's: the spot is planned
                return;
            }
            Vector3? eyes = Eyes(scene, step);
            float back = scene.Settings<TaskSettings>().fallbackDistance;
            bool kept = Kept(scene, key);
            string basis = scene.Under(part);  // what it shows
            PlacedSpot first = Placements.PlacedAs(node, key, part, basis);
            if (first != null)
            {
                // Placed before: its first placement holds (the search runs once); checked, never searched again.
                if (!kept)
                    using (At(scene, step, false))
                        if (Placer.Seen(node.transform.position, SceneBounds.Of(part), part) <= 0f)
                            ctx.Raise(TaskKit.SpotCode, $"{step.id}: the picture's camera no longer sees '{part.name}' (the scene changed since it was placed): {Again}");
                scene.Hold(Placements.Place(node, key, part, PlacedSpot.ToWorld(part, new Pose(first.placedPosition, first.placedRotation)), scene.Tuning(), ctx, basis));
                return;
            }
            // Found as the scene then stands; placed once it is back, where it rides the part to.
            Pose relative;
            using (At(scene, step, false))
            {
                Bounds seen = SceneBounds.Of(part);
                if (!Placer.View(seen, part, eyes ?? seen.center + Vector3.back * back, fieldOfView, margin, out Pose pose) && !kept)
                    ctx.Raise(TaskKit.SpotCode, $"{step.id}: no free spot sees '{part.name}' whole: its picture cuts away what stands between; " +
                                                    $"move its camera where it sees the part {Keeps}");
                relative = PlacedSpot.ToRelative(part, pose);
            }
            // Its row holds the spot (it stands under the part, not on the shot): moved, the row reads adjusted.
            scene.Hold(Placements.Place(node, key, part, PlacedSpot.ToWorld(part, relative), scene.Tuning(), ctx, basis));
        }

        // The key a tool's working spot is kept by: the part it works on and the tool, by what stays
        // across versions of the task (no step: it works at one spot on a part, whichever step has it work there).
        static string WorkKey(ManifestScene scene, Transform rides, string tool) => $"work/{scene.Under(rides)}/{tool}";

        // Where tool works at node as step's clip plays, where the clip does not move it: the trigger's own spot
        // (Interactions.AddWorkSpot: an object under the part the clip moves, riding it), made on the part's own pivot
        // and axes. That is the rig's convention (a tool's pivot at its tip, its axes the part's), so a tool rigged for
        // its work works as rigged, and nothing is guessed for one that is not: a person adjusts it after a build, on
        // the tool (its tip offset, for every part it works on) or here (the spot, for this part), kept like any spot.
        internal static void Work(ManifestScene scene, PlanStep step, GameObject node, string clip, GameObject tool, ClipTrigger trigger,
                                  OpContext ctx)
        {
            string name = Interactions.WorkSpotName(tool.name);
            GameObject spot = null;
            Transform rides;
            if (trigger == null)
            {
                // A dry run's: the trigger is planned, so are its spot and the spot's place.
                PlannedObject planned = Interactions.AddWorkSpot(node, scene.Root, scene.Clip(clip), tool, ctx);
                if (planned == null) return;  // the clip moves the tool: none works
                rides = planned.Parent;
                if (rides.Find(name) != null) return;  // made for another clip of the part, and placed then
            }
            else
            {
                if (trigger.tool == null) return;  // the clip moves the tool: none works
                spot = Interactions.AddWorkSpot(trigger, ctx);
                rides = spot != null ? spot.transform.parent : trigger.WorkPoint.name == name ? trigger.WorkPoint.parent : trigger.WorkPoint;
            }
            string key = WorkKey(scene, rides, tool.name);
            if (!scene.State<TaskState>().Worked.Add(key))
            {
                // Placed by an earlier row of this build: this row's tool works there too, so it holds the spot with that row.
                Transform shared = rides.Find(name);
                if (shared != null) scene.Hold(shared.GetComponent<PlacedSpot>());
                return;
            }
            var at = new Pose(rides.position, rides.rotation);
            // A dry run's: the spot is planned where it is made, on the part, so no move with it.
            if (spot == null) Placements.Place(new PlannedObject(name, rides), key, false, ctx);
            else scene.Hold(Placements.Place(spot, key, rides, at, scene.Tuning(), ctx));  // its row holds it: moved, the row reads adjusted
        }

        // Where page (placard's, the page of step) comes up: clear of the scene from where the user
        // stands as step runs, facing what its travel faces, covering the fewest of what the step
        // shows (keepClear). Set on every build, so it never falls back on the placard's mirroring.
        internal static void Panel(ManifestScene scene, PlanStep step, string placard, Placard made,
                                   IEnumerable<Transform> keepClear, OpContext ctx)
        {
            Vector3 preferred = made != null ? made.offset : Placard.DefaultOffset;
            (Vector3 eyes, Quaternion heading)? user = User(scene, step);
            if (user == null)
            {
                bool travels = false;
                foreach (var (each, action) in PlanOrder.All(scene.Plan)) travels |= Travels(action, each);
                if (travels) PanelsActions.SetSpot(placard, step.id, preferred, ctx);  // a dry run's: the spot is planned
                return;
            }
            Vector3 spot = preferred;
            using (At(scene, step, false))
            {
                var points = new List<Vector3>();
                foreach (Transform shown in keepClear) if (shown != null) points.Add(SceneBounds.Of(shown).center);
                Vector2 half = made != null ? made.HalfExtents : Placard.DefaultHalfExtents;
                if (!Placer.Panel(user.Value.eyes, user.Value.heading, preferred, half, points, out spot))
                    ctx.Raise(TaskKit.SpotCode, $"{step.id}: no spot beside the user is clear for the placard: it comes up where it does by itself");
            }
            PanelsActions.SetSpot(placard, step.id, spot, ctx);
        }

        sealed class Both : IDisposable
        {
            readonly IDisposable _first, _second;

            internal Both(IDisposable first, IDisposable second)
            {
                _first = first;
                _second = second;
            }

            public void Dispose()
            {
                _first.Dispose();
                _second.Dispose();
            }
        }
    }
}
