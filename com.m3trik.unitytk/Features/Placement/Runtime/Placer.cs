using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Placement
{
    /// <summary>
    /// Where to put what looks at a part, or points at it, so it does its job in the scene as it
    /// stands: a spot a person stands on to see the part (<see cref="Stand"/>), a camera's spot that
    /// sees it close up (<see cref="View"/>), an arrow's tip that points at it where the person sees
    /// it (<see cref="Point"/>). The search is the Placement model's (<see cref="SpotPlacementModel"/>,
    /// pythontk's <c>SpotPlacement</c> ported and held to its golden cases), so a first pose here is the
    /// one each DCC's Shot Manifest makes; this class hands it the scene's colliders, which stand for what
    /// blocks the view (a scene with none gets some for the search: <see cref="Occluders"/>), and reads its
    /// answer as a pose.
    /// </summary>
    /// <remarks>
    /// It finds a first spot; a person or an agent adjusts it after (a <see cref="PlacedSpot"/>
    /// keeps the adjustment), as with any guess a build makes.
    /// Prior art: Unity has no placement solver: Cinemachine frames a target from a camera it is
    /// given, its deoccluder pushes that camera past what blocks it, and XRI's teleport areas are
    /// authored. A search over candidate spots scored by line of sight is what camera planners and
    /// game AI's cover finders do (raycast visibility, a few sampled points); done with Physics
    /// queries, no package, so it runs at build time and in a player alike.
    /// </remarks>
    public static class Placer
    {
        /// <summary>The height of the eyes above where a person stands, in metres.</summary>
        public const float DefaultEyeHeight = (float)SpotPlacementModel.EyeHeight;

        /// <summary>How far from the part a person stands, in metres.</summary>
        public const float DefaultStandOff = (float)SpotPlacementModel.StandOff;

        /// <summary>How far from what a person works on with their hands they stand, in metres: within an arm's reach of it.</summary>
        public const float DefaultReach = 0.6f;

        /// <summary>How far an arrow's tip stays from the part, in metres.</summary>
        public const float DefaultGap = (float)SpotPlacementModel.Gap;

        /// <summary>How long an arrow is, tip to tail, in metres.</summary>
        public const float DefaultArrowLength = (float)SpotPlacementModel.Arrow;

        /// <summary>The most items <see cref="Items"/> parts a part into: past it, many things are pointed at as one.</summary>
        public const int MostItems = 4;

        /// <summary>A person's room to stand, in metres: half their shoulders, and an arm's bend of space (<see cref="Stand"/>'s body).</summary>
        public const float DefaultBodyRadius = (float)SpotPlacementModel.BodyRadius;

        /// <summary>How far below a part's lowest point <see cref="FloorNear"/> looks for the floor, in metres.</summary>
        public const float DefaultFloorDepth = (float)SpotPlacementModel.FloorDepth;

        /// <summary>The share of the view's height a part fills, at most, where a spot stands back to see it whole.</summary>
        public const float DefaultFill = (float)SpotPlacementModel.Fill;

        /// <summary>
        /// The most a panel's middle stands aside of the way the person faces, in degrees (<see cref="Panel"/>): its text
        /// inside a 16:9 desktop view, and where a glance reaches in a headset.
        /// </summary>
        public const float MostAside = 30f;

        /// <summary>The least a picture frames, as a radius in metres: a part smaller than this is shown with what it sits on.</summary>
        public const float LeastFramed = (float)SpotPlacementModel.LeastFramed;

        const float Near = 0.02f;             // a hit this close short of a sampled point still sees it (Panel's line of sight)
        const QueryTriggerInteraction Triggers = QueryTriggerInteraction.Ignore;  // a trigger (a panel's, a zone) blocks nothing

        // The scene's colliders as the model's rays and room: a hit's owner is its collider's own object (a hit's
        // transform is its rigidbody's, which may stand outside the part).
        sealed class PhysicsRays : ISpotRays
        {
            readonly int _layers;

            public PhysicsRays(int layers) => _layers = layers;

            public bool Cast(SpotPlacementModel.Vec origin, SpotPlacementModel.Vec direction, double far, Func<object, bool> skip,
                             out SpotPlacementModel.Hit hit)
            {
                hit = default;
                if (skip == null)
                {
                    if (!Physics.Raycast(V(origin), V(direction), out RaycastHit first, (float)far, _layers, Triggers)) return false;
                    hit = new SpotPlacementModel.Hit(first.distance, W(first.normal), first.collider.transform);
                    return true;
                }
                RaycastHit[] hits = Physics.RaycastAll(V(origin), V(direction), (float)far, _layers, Triggers);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit each in hits)
                {
                    if (skip(each.collider.transform)) continue;
                    hit = new SpotPlacementModel.Hit(each.distance, W(each.normal), each.collider.transform);
                    return true;
                }
                return false;
            }

            public bool OccupiedSphere(SpotPlacementModel.Vec at, double radius)
                => Physics.CheckSphere(V(at), (float)radius, _layers, Triggers);

            public bool OccupiedCapsule(SpotPlacementModel.Vec a, SpotPlacementModel.Vec b, double radius)
                => Physics.CheckCapsule(V(a), V(b), (float)radius, _layers, Triggers);
        }

        static SpotPlacementModel Model(int layers) => new SpotPlacementModel(new PhysicsRays(layers));
        static Vector3 V(SpotPlacementModel.Vec v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
        static SpotPlacementModel.Vec W(Vector3 v) => new SpotPlacementModel.Vec(v.x, v.y, v.z);
        static SpotPlacementModel.Box B(Bounds b) => new SpotPlacementModel.Box(W(b.min), W(b.max));

        // What is root: its colliders' objects and everything under it (null: nothing).
        static Func<object, bool> Under(Transform root)
        {
            if (root == null) return null;
            return o => o is Transform t && t != null && t.IsChildOf(root);
        }

        /// <summary>
        /// The share (0..1) of <paramref name="part"/> seen from <paramref name="from"/>: of rays to
        /// its middle and toward its corners, those that reach it, or hit <paramref name="partRoot"/>'s
        /// own colliders first.
        /// </summary>
        /// <param name="from">Where it is seen from.</param>
        /// <param name="part">What is seen: its renderers' bounds.</param>
        /// <param name="partRoot">Its node: a ray that hits its colliders sees it (null: none).</param>
        /// <param name="layers">The layers that block the view.</param>
        public static float Seen(Vector3 from, Bounds part, Transform partRoot, int layers = Physics.DefaultRaycastLayers)
            => (float)Model(layers).Seen(W(from), B(part), Under(partRoot));

        /// <summary>
        /// Where a person stands to see <paramref name="part"/>: on the floor at
        /// <paramref name="floor"/>'s height, about <paramref name="standOff"/> from the part's side,
        /// with room to stand, the part seen from their eyes; of the spots that see it best, the one on
        /// the <paramref name="delivery"/>'s outer side, then the nearest to <paramref name="near"/> (where
        /// they stood before). Facing the part, level. The model's rule (each DCC's Shot Manifest's), so a
        /// spot placed here is the one placed there.
        /// </summary>
        /// <param name="part">What they look at: its renderers' bounds.</param>
        /// <param name="partRoot">Its node (its colliders do not block its own view; null: none).</param>
        /// <param name="floor">The floor's height: its lowest point. Where rays find a floor around the part at that height,
        /// a spot needs ground under it (up to 0.3 m above; higher is a table or a step); where they find none (a delivery
        /// with no floor), a spot stands at that height, chosen by sight alone.</param>
        /// <param name="near">Where they stood before (null: anywhere).</param>
        /// <param name="pose">The spot on the floor, facing the part.</param>
        /// <param name="standOff">How far from the part's side, in metres.</param>
        /// <param name="eyeHeight">Their eyes' height above the floor.</param>
        /// <param name="layers">The layers that block the view and stand in the way.</param>
        /// <param name="side">The way, from the part, of the side to stand on (a sheet's "the front of the
        /// cabinet": its forward); of the spots that see the part, one that way wins over a nearer one, and the search
        /// starts there (null: any side).</param>
        /// <param name="also">Other parts the person acts on from there (with their nodes): a spot that sees them too
        /// wins, as much as one that sees the part it faces (null: none).</param>
        /// <param name="keepOut">Where things come to stand while the person is there (a door's reach once a later step
        /// opens it): a spot within one, or a body's width of it, is no spot (null: none).</param>
        /// <param name="fieldOfView">Their view's vertical angle, in degrees: where given, the spot stands back far enough
        /// for the part to fill no more than <see cref="DefaultFill"/> of its height, up to 3 m (0: at the stand-off).</param>
        /// <param name="bodyRadius">The room a person needs around them, in metres (<see cref="DefaultBodyRadius"/>).</param>
        /// <param name="bodyHeight">The height they need clear above the floor, in metres; 0: up to their eyes and
        /// <paramref name="bodyRadius"/> above (1.95 m by default).</param>
        /// <param name="delivery">The whole delivery's bounds (its roots'): of spots that see the part alike, one on its outer
        /// side (from its middle toward the part) wins, where no <paramref name="side"/> is named (null: none).</param>
        /// <returns>Whether any spot sees the part (else the pose is the plain one, on <paramref name="side"/>, else the
        /// delivery's outer side).</returns>
        public static bool Stand(Bounds part, Transform partRoot, float floor, Vector3? near, out Pose pose,
                                 float standOff = DefaultStandOff, float eyeHeight = DefaultEyeHeight,
                                 int layers = Physics.DefaultRaycastLayers, Vector3? side = null,
                                 IReadOnlyList<(Bounds part, Transform node)> also = null,
                                 IReadOnlyList<Bounds> keepOut = null, float fieldOfView = 0f,
                                 float bodyRadius = DefaultBodyRadius, float bodyHeight = 0f, Bounds? delivery = null)
        {
            Physics.SyncTransforms();
            List<(SpotPlacementModel.Box, Func<object, bool>)> others = null;
            if (also != null)
            {
                others = new List<(SpotPlacementModel.Box, Func<object, bool>)>(also.Count);
                foreach (var (other, node) in also) others.Add((B(other), Under(node)));
            }
            List<SpotPlacementModel.Box> keep = null;
            if (keepOut != null)
            {
                keep = new List<SpotPlacementModel.Box>(keepOut.Count);
                foreach (Bounds box in keepOut) keep.Add(B(box));
            }
            // Ground is inferred, as the model's reference does (grounded: null): needed where rays find floor at that height.
            bool found = Model(layers).Stand(B(part), out SpotPlacementModel.Vec feet, out SpotPlacementModel.Vec aim,
                delivery: delivery.HasValue ? B(delivery.Value) : (SpotPlacementModel.Box?)null,
                floor: floor, owners: Under(partRoot), side: side.HasValue ? W(side.Value) : (SpotPlacementModel.Vec?)null,
                near: near.HasValue ? W(near.Value) : (SpotPlacementModel.Vec?)null, also: others, keepOut: keep,
                fieldOfView: fieldOfView, standOff: standOff, eyeHeight: eyeHeight, bodyRadius: bodyRadius,
                bodyHeight: bodyHeight);
            Vector3 to = V(aim) - V(feet);
            to.y = 0f;
            pose = new Pose(V(feet), to.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(to, Vector3.up) : Quaternion.identity);
            return found;
        }

        /// <summary>
        /// Where a camera sees <paramref name="part"/> close up: far enough that the part fills
        /// <paramref name="margin"/> of a <paramref name="fieldOfView"/> view (a tiny one is framed with what it sits
        /// on: never nearer than fits <see cref="LeastFramed"/>), in free space, seeing the most of it, read finely (a
        /// part behind a grille is seen through its slots by a few rays, and whole from its other side); of the spots
        /// that see it alike, the one on <paramref name="eyes"/>' side (what the person would see turning to it), then
        /// the most level, a flat part shown face-on before either (a dial seen along its face does not read). Looking at the part,
        /// upright. The spot is an object a person moves, or the picture names its own camera.
        /// </summary>
        /// <param name="part">What it shows: its renderers' bounds.</param>
        /// <param name="partRoot">Its node (null: none).</param>
        /// <param name="eyes">Where the person's eyes are.</param>
        /// <param name="fieldOfView">The camera's vertical field of view, in degrees.</param>
        /// <param name="margin">How much room around the part (1: it just fits).</param>
        /// <param name="pose">The camera's spot, looking at the part.</param>
        /// <param name="layers">The layers that block the view.</param>
        /// <returns>Whether any spot sees the part.</returns>
        public static bool View(Bounds part, Transform partRoot, Vector3 eyes, float fieldOfView, float margin, out Pose pose,
                                int layers = Physics.DefaultRaycastLayers)
        {
            Physics.SyncTransforms();
            bool found = Model(layers).View(B(part), out SpotPlacementModel.Vec at, out SpotPlacementModel.Vec aim,
                eyes: W(eyes), owners: Under(partRoot), fieldOfView: fieldOfView, margin: margin);
            pose = new Pose(V(at), Quaternion.LookRotation(V(aim) - V(at), Vector3.up));
            return found;
        }

        /// <summary>
        /// Where an arrow points at <paramref name="part"/>: its tip <paramref name="gap"/> off the
        /// part's side, its body in free space, seen from <paramref name="eyes"/>; above the part where
        /// that works (the usual arrow), else across the line of sight (never end-on), else beside, else
        /// straight out of what the part is mounted on (a button on a panel, a nut on a door: the one way
        /// clear of the panel, whichever way the panel faces), which is also where it goes when no way is
        /// free. Its rotation turns <c>+Y</c> from the tip toward the tail, so an arrow made tip-down at its
        /// origin points at the part.
        /// </summary>
        /// <param name="part">What it points at: its renderers' bounds.</param>
        /// <param name="partRoot">Its node (null: none).</param>
        /// <param name="eyes">Where the person's eyes are.</param>
        /// <param name="pose">The tip's spot, <c>+Y</c> away from the part.</param>
        /// <param name="gap">How far the tip stays from the part.</param>
        /// <param name="length">The arrow's length, tip to tail.</param>
        /// <param name="layers">The layers that block the view and stand in the way.</param>
        /// <param name="travel">How far it moves away from the part along its axis (a bob): the room it needs is its whole sweep.</param>
        /// <returns>Whether any spot is free and seen.</returns>
        public static bool Point(Bounds part, Transform partRoot, Vector3 eyes, out Pose pose,
                                 float gap = DefaultGap, float length = DefaultArrowLength,
                                 int layers = Physics.DefaultRaycastLayers, float travel = 0f)
        {
            Physics.SyncTransforms();
            bool found = Model(layers).Point(B(part), out SpotPlacementModel.Vec tip, out SpotPlacementModel.Vec aim,
                eyes: W(eyes), owners: Under(partRoot), gap: gap, length: length, travel: travel);
            Vector3 way = V(tip) - V(aim);
            pose = new Pose(V(tip), way.sqrMagnitude > 1e-12f ? Quaternion.FromToRotation(Vector3.up, way.normalized) : Quaternion.identity);
            return found;
        }

        /// <summary>
        /// The floor around <paramref name="part"/>: the lowest of what rays cast straight down around
        /// it (one to three metres out from its side, however large it is) land on. What reaches below the floor (a wall's foot) is never
        /// landed on from above, and what stands on the floor (a table, the part's own cabinet) is higher.
        /// </summary>
        /// <param name="part">What a person stands by: its renderers' bounds.</param>
        /// <param name="floor">The floor's height.</param>
        /// <param name="layers">The layers that are floor and furniture.</param>
        /// <param name="depth">How far below the part's lowest point the floor may lie, in metres.</param>
        /// <returns>Whether any ray landed.</returns>
        public static bool FloorNear(Bounds part, out float floor, int layers = Physics.DefaultRaycastLayers,
                                     float depth = DefaultFloorDepth)
        {
            Physics.SyncTransforms();
            bool found = Model(layers).FloorNear(B(part), out double height, depth);
            floor = (float)height;
            return found;
        }

        /// <summary>
        /// Where a panel (a placard) comes up beside a person, clear of the scene: of offsets around
        /// <paramref name="preferred"/> (it, mirrored left to right, nearer, higher or lower), one whose
        /// panel stands in free space, seen from the eyes, covering the fewest of
        /// <paramref name="keepClear"/>; the nearest to <paramref name="preferred"/> of those. Never further aside than
        /// <see cref="MostAside"/>: brought nearer, it comes in toward the middle rather than out to the edge.
        /// </summary>
        /// <param name="eyes">Where the person's eyes are.</param>
        /// <param name="heading">Which way they face (level).</param>
        /// <param name="preferred">Where it comes up when nothing is in the way: right, up and ahead of the eyes, in metres, turned with the heading.</param>
        /// <param name="half">Half the panel's width and height, in metres.</param>
        /// <param name="keepClear">What the person is to see past it (the parts a step asks them to act on); null: nothing.</param>
        /// <param name="offset">The offset found, as <paramref name="preferred"/> is.</param>
        /// <param name="layers">The layers that stand in the way.</param>
        /// <returns>Whether any offset is clear.</returns>
        public static bool Panel(Vector3 eyes, Quaternion heading, Vector3 preferred, Vector2 half,
                                 IReadOnlyList<Vector3> keepClear, out Vector3 offset,
                                 int layers = Physics.DefaultRaycastLayers)
        {
            Physics.SyncTransforms();
            offset = preferred;
            float best = float.NegativeInfinity;
            float[] sides = { 1f, -1f }, nearer = { 1f, 0.8f, 0.6f }, heights = { 0f, 0.15f, -0.15f, 0.3f };
            foreach (float side in sides)
                foreach (float near in nearer)
                    foreach (float up in heights)
                    {
                        // Nearer, it comes in with its depth (the same way off the eyes' line), and its middle never
                        // stands further aside than MostAside: a desktop view's middle, where a glance in a headset reaches.
                        float depth = preferred.z * near;
                        float aside = Mathf.Min(Mathf.Abs(preferred.x) * near, depth * Mathf.Tan(MostAside * Mathf.Deg2Rad));
                        var local = new Vector3(aside * Mathf.Sign(preferred.x) * side, preferred.y + up, depth);
                        Vector3 at = eyes + heading * local;
                        Quaternion facing = Quaternion.LookRotation(at - eyes, Vector3.up);
                        if (Physics.CheckBox(at, new Vector3(half.x + 0.03f, half.y + 0.03f, 0.03f), facing, layers, Triggers)) continue;
                        if (!Clear(eyes, at, null, layers)) continue;
                        float score = -5f * PanelCover.Count(eyes, at, facing, half, keepClear) - (side < 0f ? 0.5f : 0f)
                                      - 3f * (1f - near) - 2f * Mathf.Abs(up);
                        if (score <= best) continue;
                        best = score;
                        offset = local;
                    }
            return best > float.NegativeInfinity;
        }

        /// <summary>
        /// The separate things <paramref name="part"/> is, each with what is seen of it: past the nodes
        /// that only wrap one child, the children that show something, those touching or nearly
        /// (closer than a quarter of the smaller one's size) one thing. Two locks half a metre apart on a
        /// door are two; a door with its handle is one, the part itself, as is a part of more than
        /// <see cref="MostItems"/> things.
        /// </summary>
        /// <param name="part">The part: one thing, or a group of them.</param>
        /// <param name="skip">What is not of the part (what a build added under it: an arrow); null: nothing.</param>
        /// <returns>The things, each its nodes and their bounds; none where nothing of the part is seen.</returns>
        public static List<(List<Transform> nodes, Bounds seen)> Items(Transform part, Func<Transform, bool> skip = null)
        {
            var items = new List<(List<Transform> nodes, Bounds seen)>();
            if (part == null || !SceneBounds.TryOf(part, out Bounds whole, skip: skip)) return items;
            Transform at = part;
            var shown = new List<(Transform, Bounds)>();
            while (true)
            {
                shown.Clear();
                foreach (Transform child in at)
                    if ((skip == null || !skip(child)) && SceneBounds.TryOf(child, out Bounds seen, skip: skip)) shown.Add((child, seen));
                if (shown.Count != 1 || at.TryGetComponent(out Renderer own) && own.enabled) break;
                at = shown[0].Item1;  // a wrapper: what it wraps
            }
            if (!(at.TryGetComponent(out Renderer mine) && mine.enabled))
                foreach (var (node, seen) in shown) items.Add((new List<Transform> { node }, seen));
            Join(items, Touching);
            if (items.Count < 2 || items.Count > MostItems)
            {
                items.Clear();
                items.Add((new List<Transform> { part }, whole));
            }
            return items;
        }

        // Whether two things touch, or nearly: the gap between their boxes under a quarter of the smaller one's size.
        static bool Touching(Bounds a, Bounds b)
        {
            float size = Mathf.Min(Mathf.Max(a.size.x, Mathf.Max(a.size.y, a.size.z)), Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            return Gap(a, b) <= 0.25f * size;
        }

        /// <summary>
        /// <paramref name="items"/> (the things a part is, as <see cref="Items"/> finds them) gathered into groups, those
        /// closer together than <paramref name="within"/> (metres between their boxes, 0 where they touch) as one: what
        /// shares one arrow, one label, one marker. Each group keeps its things' nodes in the order of its first thing,
        /// and the box around them all.
        /// </summary>
        /// <param name="items">The things, each its nodes and their bounds (null: none).</param>
        /// <param name="within">How close two must be to share a group, in metres; 0 or less: never (each its own).</param>
        /// <returns>The groups, new lists (the items are left as they are).</returns>
        public static List<(List<Transform> nodes, Bounds seen)> Cluster(IReadOnlyList<(List<Transform> nodes, Bounds seen)> items, float within)
        {
            var groups = new List<(List<Transform> nodes, Bounds seen)>();
            if (items == null) return groups;
            foreach (var (nodes, seen) in items) groups.Add((nodes == null ? new List<Transform>() : new List<Transform>(nodes), seen));
            if (within > 0f) Join(groups, (a, b) => Gap(a, b) <= within);
            return groups;
        }

        // Join the groups near() says are close, pairwise, until none are: the first of a pair takes the second.
        static void Join(List<(List<Transform> nodes, Bounds seen)> groups, Func<Bounds, Bounds, bool> near)
        {
            for (bool joined = true; joined;)
            {
                joined = false;
                for (int i = 0; i < groups.Count && !joined; i++)
                    for (int j = i + 1; j < groups.Count && !joined; j++)
                    {
                        if (!near(groups[i].seen, groups[j].seen)) continue;
                        Bounds both = groups[i].seen;
                        both.Encapsulate(groups[j].seen);
                        groups[i].nodes.AddRange(groups[j].nodes);
                        groups[i] = (groups[i].nodes, both);
                        groups.RemoveAt(j);
                        joined = true;
                    }
            }
        }

        // The distance between two boxes: 0 where they touch or overlap.
        static float Gap(Bounds a, Bounds b) => Vector3.Max(Vector3.zero, Vector3.Max(a.min - b.max, b.min - a.max)).magnitude;

        /// <summary>
        /// The way, from <paramref name="of"/>, of the side <paramref name="side"/> names, as the part's axes turn: its
        /// front (<c>+Z</c>), back, left or right, or the other side from <paramref name="stoodAt"/> (across the part
        /// from where the person stood): what <see cref="Stand"/>'s <c>side</c> takes.
        /// </summary>
        /// <param name="side">The side.</param>
        /// <param name="of">The part whose side it is (null: none).</param>
        /// <param name="stoodAt">Where the person stood, for <see cref="StandSide.Other"/> (null: unknown).</param>
        /// <returns>The way; null where it names none (no part, or the other side of an unknown spot).</returns>
        public static Vector3? SideDirection(StandSide side, Transform of, Vector3? stoodAt = null)
        {
            if (of == null) return null;
            switch (side)
            {
                case StandSide.Front: return of.forward;
                case StandSide.Back: return -of.forward;
                case StandSide.Left: return -of.right;
                case StandSide.Right: return of.right;
                case StandSide.Other:
                    return stoodAt.HasValue ? SceneBounds.Of(of).center - stoodAt.Value : (Vector3?)null;
                default: return null;
            }
        }


        // Whether a ray from `from` reaches `to`, or hits `part` first: nothing else stands between.
        static bool Clear(Vector3 from, Vector3 to, Transform part, int layers)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 1e-4f) return true;
            if (!Physics.Raycast(from, d / length, out RaycastHit hit, length, layers, Triggers)) return true;
            // The collider's own object: a hit's transform is its rigidbody's, which may stand outside the part.
            if (part != null && hit.collider.transform.IsChildOf(part)) return true;
            return hit.distance >= length - Near;
        }
    }
}
