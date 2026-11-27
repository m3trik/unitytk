// ----------------------------------------------------------------------------
// The Placement feature's model: pythontk's ``SpotPlacement`` ported term for
// term, engine-free (its assembly references no UnityEngine), so the same rules
// place a viewpoint, a camera and an arrow in a player, in the editor, under
// `dotnet test`, and in each DCC's Shot Manifest (the reference itself).
//
// The port is pinned against the reference by the golden cases of
// ``ptk.Conformance.cases("spot_placement")``: scenes of boxes (BoxRays) under
// `dotnet test` in tests/models_dotnet. Unity's scene reaches it through the
// runtime part's physics adapter (Placer), held to the same box scenes in
// PlacerTests.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3trik.Unitytk.Placement
{
    /// <summary>
    /// What a placement casts and checks through, in the scene's units: rays (the nearest hit within a distance,
    /// passing over what <c>skip</c> names; a ray that starts inside a thing need not hit it) and room (whether a
    /// sphere or a capsule overlaps anything). pythontk's <c>SpotRays</c>.
    /// </summary>
    public interface ISpotRays
    {
        /// <summary>The nearest hit along the unit <paramref name="direction"/> from <paramref name="origin"/> within <paramref name="far"/>.</summary>
        /// <param name="origin">Where the ray starts.</param>
        /// <param name="direction">Its way, a unit vector.</param>
        /// <param name="far">How far it reaches.</param>
        /// <param name="skip">The owners it passes over (null: none).</param>
        /// <param name="hit">The hit, where there is one.</param>
        /// <returns>Whether it hit anything.</returns>
        bool Cast(SpotPlacementModel.Vec origin, SpotPlacementModel.Vec direction, double far, Func<object, bool> skip,
                  out SpotPlacementModel.Hit hit);

        /// <summary>Whether a sphere at <paramref name="at"/> of <paramref name="radius"/> overlaps anything.</summary>
        bool OccupiedSphere(SpotPlacementModel.Vec at, double radius);

        /// <summary>Whether the capsule from <paramref name="a"/> to <paramref name="b"/>, <paramref name="radius"/> round, overlaps anything.</summary>
        bool OccupiedCapsule(SpotPlacementModel.Vec a, SpotPlacementModel.Vec b, double radius);
    }

    /// <summary>
    /// Where a thing placed for a purpose first stands: where a person stands to see a part (<see cref="Stand"/>), where
    /// a camera frames it (<see cref="View"/>), where an arrow's tip points at it (<see cref="Point"/>). pythontk's
    /// <c>SpotPlacement</c>, ported term for term: its module docstring holds the rules and the scenes that forced
    /// each. Without rays, the plain first pose from bounds alone.
    /// </summary>
    /// <remarks>
    /// Lengths given as a setting (a stand-off, a gap) are metres; positions and boxes are in the scene's units
    /// (<see cref="Unit"/> of them in a metre). The level axes turn from the first after up toward the second.
    /// Prior art: Unity has no placement solver (Cinemachine frames a target from a camera it is given; XRI's teleport
    /// areas are authored); a search over candidate spots scored by line of sight is what camera planners and game
    /// AI's cover finders do. The search is the reference's, so a first pose is the same in every tool that makes one.
    /// </remarks>
    public sealed class SpotPlacementModel
    {
        /// <summary>A 3-vector in doubles.</summary>
        public readonly struct Vec
        {
            /// <summary>The components.</summary>
            public readonly double x, y, z;
            /// <summary>The vector (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).</summary>
            public Vec(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            /// <summary>Component <paramref name="i"/> (0 = x, 1 = y, 2 = z).</summary>
            public double this[int i] => i == 0 ? x : (i == 1 ? y : z);
            /// <summary>This vector with component <paramref name="i"/> set to <paramref name="value"/>.</summary>
            public Vec With(int i, double value) => new Vec(i == 0 ? value : x, i == 1 ? value : y, i == 2 ? value : z);
            /// <summary>The sum.</summary>
            public static Vec operator +(Vec a, Vec b) => new Vec(a.x + b.x, a.y + b.y, a.z + b.z);
            /// <summary>The difference.</summary>
            public static Vec operator -(Vec a, Vec b) => new Vec(a.x - b.x, a.y - b.y, a.z - b.z);
            /// <summary>The opposite.</summary>
            public static Vec operator -(Vec a) => new Vec(-a.x, -a.y, -a.z);
            /// <summary>Scaled by <paramref name="k"/>.</summary>
            public static Vec operator *(Vec a, double k) => new Vec(a.x * k, a.y * k, a.z * k);
            /// <inheritdoc/>
            public override string ToString()
                => $"({x.ToString("R", CultureInfo.InvariantCulture)}, {y.ToString("R", CultureInfo.InvariantCulture)}, {z.ToString("R", CultureInfo.InvariantCulture)})";
        }

        /// <summary>A box: its lowest and highest corners.</summary>
        public readonly struct Box
        {
            /// <summary>The corners.</summary>
            public readonly Vec low, high;
            /// <summary>The box from <paramref name="low"/> to <paramref name="high"/>.</summary>
            public Box(Vec low, Vec high) { this.low = low; this.high = high; }
        }

        /// <summary>A ray's nearest hit: how far, the face's normal (world), and what owns what it struck.</summary>
        public readonly struct Hit
        {
            /// <summary>How far along the ray.</summary>
            public readonly double distance;
            /// <summary>The face's normal.</summary>
            public readonly Vec normal;
            /// <summary>What owns the thing struck (a box's owner, a collider's transform).</summary>
            public readonly object owner;
            /// <summary>A hit.</summary>
            public Hit(double distance, Vec normal, object owner) { this.distance = distance; this.normal = normal; this.owner = owner; }
        }

        /// <summary>Metres between a viewpoint and the side of what its user faces.</summary>
        public const double StandOff = 1.5;
        /// <summary>A person's eyes above the floor, in metres.</summary>
        public const double EyeHeight = 1.6;
        /// <summary>A person's room to stand, in metres: half their shoulders and an arm's bend.</summary>
        public const double BodyRadius = 0.35;
        /// <summary>What a person steps over, in metres: the body's room starts this far above the floor.</summary>
        public const double Knees = 0.3;
        /// <summary>The farthest a viewpoint stands back to see its part whole, in metres.</summary>
        public const double MostBack = 3.0;
        /// <summary>The share of the view's height a part fills, at most, where a viewpoint stands back to see it whole.</summary>
        public const double Fill = 0.8;
        /// <summary>How far above the floor's lowest point its walkable top may be, in metres.</summary>
        public const double FloorRise = 0.3;
        /// <summary>How far below a part's lowest point the floor may lie, in metres.</summary>
        public const double FloorDepth = 100.0;
        /// <summary>A hit this close short of a sampled point still sees it, in metres.</summary>
        public const double Near = 0.02;
        /// <summary>A camera's room in free space, as a radius in metres.</summary>
        public const double Free = 0.05;
        /// <summary>Metres between an arrow's tip and its part.</summary>
        public const double Gap = 0.04;
        /// <summary>An arrow's length, tip to tail, in metres.</summary>
        public const double Arrow = 0.13;
        /// <summary>A picture's vertical field of view, in degrees.</summary>
        public const double ViewAngle = 30.0;
        /// <summary>The room around a picture's part (1: it just fits).</summary>
        public const double Margin = 1.4;
        /// <summary>The least a picture frames, as a radius in metres: a smaller part is shown with what it sits on.</summary>
        public const double LeastFramed = 0.06;
        /// <summary>An arrow within this many degrees of the line of sight is end-on (seen as a dot).</summary>
        public const double EndOn = 30.0;
        /// <summary>How much a picture of a flat part prefers its face, among spots that see it alike: over the eyes' side (0.4 either way) and the level.</summary>
        public const double FaceOn = 2.0;
        /// <summary>A later candidate must beat the best by this much: equal scores keep the first, in every runtime alike.</summary>
        public const double Eps = 1.0e-9;

        const double Degrees = Math.PI / 180.0;
        static readonly Func<object, bool> Nothing = _ => false;

        /// <summary>What it casts and checks through (null: bounds alone).</summary>
        public ISpotRays Rays { get; }
        /// <summary>The up axis's index (1: Y; 2: Z).</summary>
        public int Up { get; }
        /// <summary>The scene's units in a metre.</summary>
        public double Unit { get; }

        /// <summary>A placement over <paramref name="rays"/> (null: bounds alone), <paramref name="up"/> the up axis's index, <paramref name="unit"/> the scene's units in a metre.</summary>
        public SpotPlacementModel(ISpotRays rays = null, int up = 1, double unit = 1.0)
        {
            Rays = rays;
            Up = up;
            Unit = unit;
        }

        // ---------------------------------------------------------------- vector arithmetic
        static double Dot(Vec a, Vec b) => a.x * b.x + a.y * b.y + a.z * b.z;
        static Vec Cross(Vec a, Vec b) => new Vec(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        static double Norm(Vec a) => Math.Sqrt(Dot(a, a));

        static Vec UnitOf(Vec a)
        {
            double length = Norm(a);
            return length < 1e-12 ? new Vec(0, 0, 0) : new Vec(a.x / length, a.y / length, a.z / length);
        }

        static Vec Centre(Box b) => new Vec((b.low.x + b.high.x) * 0.5, (b.low.y + b.high.y) * 0.5, (b.low.z + b.high.z) * 0.5);
        static Vec Extents(Box b) => new Vec(Math.Abs(b.high.x - b.low.x) * 0.5, Math.Abs(b.high.y - b.low.y) * 0.5, Math.Abs(b.high.z - b.low.z) * 0.5);
        static double Reach(Vec extents, Vec way) => Math.Abs(extents.x * way.x) + Math.Abs(extents.y * way.y) + Math.Abs(extents.z * way.z);

        /// <summary>(up, first level axis, second level axis): the turn from the first to the second is the same in both frames.</summary>
        void Frame(out Vec up, out Vec h1, out Vec h2)
        {
            if (Up == 2) { up = new Vec(0, 0, 1); h1 = new Vec(1, 0, 0); h2 = new Vec(0, -1, 0); }
            else if (Up == 0) { up = new Vec(1, 0, 0); h1 = new Vec(0, 1, 0); h2 = new Vec(0, 0, 1); }
            else { up = new Vec(0, 1, 0); h1 = new Vec(1, 0, 0); h2 = new Vec(0, 0, 1); }
        }

        Vec UpAxis { get { Frame(out Vec up, out _, out _); return up; } }

        // A unit direction angle (radians) round from the level unit lead, and elevation above level: lead itself at zero.
        Vec Turn(Vec lead, double angle, double elevation)
        {
            Vec up = UpAxis;
            Vec side = Cross(lead, up);
            double c = Math.Cos(elevation);
            return lead * (c * Math.Cos(angle)) + side * (c * Math.Sin(angle)) + up * Math.Sin(elevation);
        }

        Vec Flat(Vec way)
        {
            Vec up = UpAxis;
            return UnitOf(way + up * -Dot(way, up));
        }

        static List<Vec> Samples(Box target, bool fine)
        {
            Vec centre = Centre(target), extents = Extents(target);
            var points = new List<Vec>(27);
            if (fine)
            {
                Vec e = extents * 0.8;
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                            points.Add(new Vec(centre.x + x * e.x, centre.y + y * e.y, centre.z + z * e.z));
                return points;
            }
            Vec f = extents * 0.6;
            points.Add(centre);
            for (int i = 1; i < 9; i++)
                points.Add(new Vec(centre.x + ((i & 1) == 0 ? f.x : -f.x), centre.y + ((i & 2) == 0 ? f.y : -f.y),
                                   centre.z + ((i & 4) == 0 ? f.z : -f.z)));
            return points;
        }

        // ---------------------------------------------------------------- what is seen
        /// <summary>Whether nothing but what <paramref name="owners"/> names (the thing looked at) stands between <paramref name="origin"/> and <paramref name="point"/>. Without rays, always.</summary>
        public bool Clear(Vec origin, Vec point, Func<object, bool> owners = null)
        {
            if (Rays == null) return true;
            Vec to = point - origin;
            double length = Norm(to);
            if (length < 1e-4 * Unit) return true;
            if (!Rays.Cast(origin, to * (1.0 / length), length, null, out Hit hit)) return true;
            if ((owners ?? Nothing)(hit.owner)) return true;
            return hit.distance >= length - Near * Unit;
        }

        /// <summary>The share (0..1) of <paramref name="target"/> seen from <paramref name="origin"/>: of its sample points (its middle and corners drawn in to 0.6; <paramref name="fine"/>: 27 through it at 0.8), those a clear line reaches.</summary>
        public double Seen(Vec origin, Box target, Func<object, bool> owners = null, bool fine = false)
        {
            List<Vec> samples = Samples(target, fine);
            int seen = 0;
            foreach (Vec p in samples)
                if (Clear(origin, p, owners)) seen++;
            return seen / (double)samples.Count;
        }

        /// <summary>
        /// The floor around <paramref name="target"/>: the lowest top that rays cast straight down, one to three metres out from
        /// its side, land on. A ray goes on through every top it lands on, so a ceiling or a shelf over the floor never stands
        /// for it; a face it meets turned away (a thing's inside: a wall's foot that reaches below the floor) is no top.
        /// </summary>
        /// <param name="target">What a person stands by.</param>
        /// <param name="floor">The floor's height; not found, the target's lowest point.</param>
        /// <param name="depth">How far below the target's lowest point the floor may lie, in metres (default <see cref="FloorDepth"/>).</param>
        /// <returns>Whether any ray landed.</returns>
        public bool FloorNear(Box target, out double floor, double depth = FloorDepth)
        {
            Frame(out Vec up, out Vec h1, out Vec h2);
            double u = Unit;
            floor = target.low[Up];
            if (Rays == null) return false;
            Vec centre = Centre(target), extents = Extents(target);
            double top = target.high[Up] + 1.0 * u;
            double edge = Math.Max(Math.Abs(Dot(extents, h1)), Math.Abs(Dot(extents, h2)));
            double far = top - target.low[Up] + Math.Max(0.0, depth) * u;
            bool found = false;
            double lowest = 0.0;
            for (int ring = 1; ring < 4; ring++)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * 22.5 * Degrees;
                    double reach = edge + ring * u;
                    Vec start = (centre + h1 * (Math.Cos(a) * reach) + h2 * (Math.Sin(a) * reach)).With(Up, top);
                    double gone = 0.0;
                    while (far - gone > 0.0 && Rays.Cast(start - up * gone, -up, far - gone, null, out Hit hit))
                    {
                        gone += hit.distance;
                        if (Dot(hit.normal, up) > 0.0)  // a top, met from above
                        {
                            double landed = top - gone;
                            lowest = found ? Math.Min(lowest, landed) : landed;
                            found = true;
                        }
                        gone += 1e-3 * u;  // on from a millimetre past it
                    }
                }
            if (found) floor = lowest;
            return found;
        }

        /// <summary>The level way out of <paramref name="delivery"/> at <paramref name="target"/>: from the delivery's middle toward the target's; over the middle, or with no delivery, the second level axis (a model's front).</summary>
        public Vec Outer(Box target, Box? delivery = null)
        {
            Frame(out _, out _, out Vec h2);
            Vec centre = Centre(target);
            Vec middle = delivery.HasValue ? Centre(delivery.Value) : centre;
            bool over = true;
            for (int i = 0; i < 3; i++)
                if (i != Up && !(target.low[i] <= middle[i] && middle[i] <= target.high[i])) over = false;
            Vec way = Flat(centre - middle);
            return over || Norm(way) < 1e-9 ? h2 : way;
        }

        // ---------------------------------------------------------------- the purposes
        /// <summary>
        /// Where a person stands to see <paramref name="target"/>, and the point they face (level): on the floor (ground
        /// under the spot where <paramref name="grounded"/>), around the part at the stand-off, with room for a body and
        /// outside every keep-out box, its eyes seeing the part; of those, the one that sees most of it and of
        /// <paramref name="also"/>; a named <paramref name="side"/> outranks sight, the delivery's outer side only breaks
        /// ties; then the nearest ring, then the nearest to <paramref name="near"/>.
        /// </summary>
        /// <param name="target">What they look at.</param>
        /// <param name="feet">Where they stand.</param>
        /// <param name="aim">The point they face, level with their feet.</param>
        /// <param name="delivery">The whole delivery's bounds (null: none).</param>
        /// <param name="floor">The floor's height (null: <see cref="FloorNear"/>'s, else the delivery's lowest point).</param>
        /// <param name="owners">What is the target (null: nothing).</param>
        /// <param name="side">The way, from the target, of the side to stand on (null: any).</param>
        /// <param name="near">Where they stood before (null: anywhere).</param>
        /// <param name="also">Other parts they act on from there, with what is each (null: none).</param>
        /// <param name="keepOut">Where things come to stand while they are there (null: none).</param>
        /// <param name="fieldOfView">Their view's vertical angle in degrees: they stand back for the part to fit (0: at the stand-off).</param>
        /// <param name="standOff">Metres from the part's side (null: <see cref="StandOff"/>).</param>
        /// <param name="eyeHeight">Metres (null: <see cref="EyeHeight"/>).</param>
        /// <param name="bodyRadius">Metres (null: <see cref="BodyRadius"/>).</param>
        /// <param name="bodyHeight">The height they need clear, metres; 0: to their eyes and a body's radius over.</param>
        /// <param name="grounded">Whether a spot needs ground under it at the floor's height (null: where rays find floor within <see cref="FloorRise"/> of it).</param>
        /// <returns>Whether any spot sees the part (else the plain pose).</returns>
        public bool Stand(Box target, out Vec feet, out Vec aim, Box? delivery = null, double? floor = null,
                          Func<object, bool> owners = null, Vec? side = null, Vec? near = null,
                          IReadOnlyList<(Box part, Func<object, bool> owners)> also = null, IReadOnlyList<Box> keepOut = null,
                          double fieldOfView = 0.0, double? standOff = null, double? eyeHeight = null, double? bodyRadius = null,
                          double bodyHeight = 0.0, bool? grounded = null)
        {
            Frame(out Vec up, out Vec h1, out _);
            double u = Unit;
            int ui = Up;
            Vec centre = Centre(target), extents = Extents(target);
            double off = (standOff ?? StandOff) * u;
            double eye = (eyeHeight ?? EyeHeight) * u;
            double radius = Math.Max(0.01, bodyRadius ?? BodyRadius) * u;
            double top = Math.Max(Knees * u + 2.0 * radius, bodyHeight > 0 ? bodyHeight * u : eye + radius) - radius;
            bool found = false;
            double foundFloor = 0.0;
            if (Rays != null && (!floor.HasValue || !grounded.HasValue)) found = FloorNear(target, out foundFloor);
            double level = floor ?? (found ? foundFloor : (delivery ?? target).low[ui]);
            bool ground = (grounded ?? (found && Math.Abs(foundFloor - level) <= FloorRise * u)) && Rays != null;
            if (fieldOfView > 0)
            {
                double whole = extents[ui] / Math.Tan(Math.Min(Math.Max(fieldOfView, 1.0), 179.0) * Degrees * 0.5 * Fill);
                off = Math.Max(off, Math.Min(whole, MostBack * u));
            }
            Vec? named = null;
            if (side.HasValue)
            {
                Vec flat = Flat(side.Value);
                if (Norm(flat) > 0.5) named = flat;
            }
            Vec? outer = delivery.HasValue ? Outer(target, delivery) : (Vec?)null;
            Vec? lead = named ?? outer;
            Vec first = lead ?? h1;
            Vec floorBase = centre.With(ui, level);
            double best = 0.0;
            bool any = false;
            feet = aim = default;
            for (int ring = 0; ring < 3; ring++)
                for (int k = 0; k < 24; k++)
                {
                    Vec way = Turn(first, 15.0 * k * Degrees, 0.0);
                    Vec spot = floorBase + way * (Reach(extents, way) + off + ring * 0.5 * u);
                    if (ground)
                    {
                        Vec origin = spot + up * (FloorRise * u + eye);
                        if (!Rays.Cast(origin, -up, eye + FloorRise * u + 0.3 * u, null, out Hit hit)) continue;
                        double height = origin[ui] - hit.distance;
                        if (height < level - 0.05 * u || height > level + FloorRise * u) continue;
                        spot = spot.With(ui, height);
                    }
                    Vec eyes = spot + up * eye;
                    if (Rays != null && Rays.OccupiedCapsule(spot + up * (Knees * u + radius), spot + up * top, radius)) continue;
                    if (Within(keepOut, spot, bodyHeight > 0 ? spot[ui] + bodyHeight * u : eyes[ui], radius)) continue;
                    double seen = Seen(eyes, target, owners);
                    if (seen <= 0.0) continue;
                    double others = 0.0;
                    if (also != null && also.Count > 0)
                    {
                        foreach (var (part, its) in also) others += Seen(eyes, part, its);
                        others /= also.Count;
                    }
                    double score = seen * 10.0 + others * 10.0 - ring;
                    if (near.HasValue) score -= 0.5 * Norm(spot - near.Value) / u;
                    if (named.HasValue) score += 6.0 * Dot(way, named.Value);
                    else if (outer.HasValue) score += Dot(way, outer.Value);
                    if (any && !(score > best + Eps)) continue;
                    any = true;
                    best = score;
                    feet = spot;
                    aim = centre.With(ui, spot[ui]);
                }
            if (any) return true;
            Vec plain = lead ?? Outer(target);
            feet = floorBase + plain * (Reach(extents, plain) + off);
            aim = centre.With(ui, level);
            return false;
        }

        // Whether a person standing at feet (the top of their head at top) is within one of boxes, or radius of it.
        bool Within(IReadOnlyList<Box> boxes, Vec feet, double top, double radius)
        {
            if (boxes == null) return false;
            foreach (Box box in boxes)
            {
                if (box.high[Up] < feet[Up] + Knees * Unit || box.low[Up] > top) continue;  // under their knees, or over their head
                bool inside = true;
                for (int i = 0; i < 3; i++)
                    if (i != Up && !(box.low[i] - radius < feet[i] && feet[i] < box.high[i] + radius)) inside = false;
                if (inside) return true;
            }
            return false;
        }

        // The eyes of Stand's spot for target.
        Vec EyesFor(Box target, Box? delivery, Func<object, bool> owners)
        {
            Stand(target, out Vec feet, out _, delivery, owners: owners);
            return feet + UpAxis * (EyeHeight * Unit);
        }

        /// <summary>
        /// Where a camera frames <paramref name="target"/>, and the point it looks at: at the distance that frames it (a
        /// tiny part framed with what it sits on), in free space, seeing the most of it read finely (27 points pass a
        /// grille's slots), first; of spots that see it alike, a flat part face-on, then the eyes' side, then the most level.
        /// </summary>
        /// <param name="target">What it shows.</param>
        /// <param name="at">Where the camera stands.</param>
        /// <param name="aim">The point it looks at.</param>
        /// <param name="delivery">The whole delivery's bounds (null: none).</param>
        /// <param name="eyes">Where the person's eyes are (null: those of <see cref="Stand"/>'s spot).</param>
        /// <param name="owners">What is the target (null: nothing).</param>
        /// <param name="fieldOfView">The camera's vertical angle in degrees (null: <see cref="ViewAngle"/>).</param>
        /// <param name="margin">The room around the part (null: <see cref="Margin"/>).</param>
        /// <returns>Whether any spot sees the part (else straight toward the eyes).</returns>
        public bool View(Box target, out Vec at, out Vec aim, Box? delivery = null, Vec? eyes = null,
                         Func<object, bool> owners = null, double? fieldOfView = null, double? margin = null)
        {
            Frame(out _, out Vec h1, out Vec h2);
            double u = Unit;
            Vec centre = Centre(target), extents = Extents(target);
            double radius = Math.Max(Norm(extents), LeastFramed * u);
            double distance = radius * (margin ?? Margin) / Math.Sin(Math.Min(Math.Max(fieldOfView ?? ViewAngle, 1.0), 179.0) * Degrees * 0.5);
            Vec from = eyes ?? EyesFor(target, delivery, owners);
            Vec to = from - centre;
            Vec toEyes = Norm(to) > 1e-4 * u ? UnitOf(to) : -h2;
            at = centre + toEyes * distance;
            aim = centre;
            Vec lead = Flat(toEyes);
            Vec first = Norm(lead) > 0.5 ? lead : h1;
            int axis = Face(extents);
            double bestSeen = 0.0, best = 0.0;
            bool any = false;
            double[] elevations = { 10.0, 35.0, -10.0, 60.0 };
            foreach (double elevation in elevations)
                for (int k = 0; k < 18; k++)
                {
                    Vec way = Turn(first, 20.0 * k * Degrees, elevation * Degrees);
                    Vec spot = centre + way * distance;
                    if (Rays != null && Rays.OccupiedSphere(spot, Free * u)) continue;
                    double seen = Seen(spot, target, owners, fine: true);
                    if (seen <= 0.0) continue;
                    // How much of it is seen decides; among spots that see it alike, a flat part's face, the eyes' side, the level.
                    double rest = (axis >= 0 ? FaceOn * Math.Abs(way[axis]) : 0.0) + 0.4 * Dot(way, toEyes) - Math.Abs(elevation) / 60.0;
                    if (any && !(seen > bestSeen + Eps || (seen > bestSeen - Eps && rest > best + Eps))) continue;
                    any = true;
                    bestSeen = seen;
                    best = rest;
                    at = spot;
                }
            return any;
        }

        /// <summary>
        /// Where an arrow's tip rests by <paramref name="target"/>, and the point it points at: the tip just off the part,
        /// its body and its bob in free space with room to turn, seen from the eyes; above where that works, else across
        /// the line of sight (never end-on), else beside, else straight out of what the part is mounted on, where it also
        /// goes when nothing is free. Its body lies behind the tip, away from the aim.
        /// </summary>
        /// <param name="target">What it points at.</param>
        /// <param name="tip">Where its tip rests.</param>
        /// <param name="aim">The point it points at (the target's middle).</param>
        /// <param name="delivery">The whole delivery's bounds (null: none).</param>
        /// <param name="eyes">Where the person's eyes are (null: those of <see cref="Stand"/>'s spot).</param>
        /// <param name="owners">What is the target (null: nothing).</param>
        /// <param name="gap">Metres between the tip and the part (null: <see cref="Gap"/>).</param>
        /// <param name="length">Metres tip to tail (null: <see cref="Arrow"/>).</param>
        /// <param name="travel">How far it bobs away from the part along its axis, in metres.</param>
        /// <returns>Whether any spot is free and seen.</returns>
        public bool Point(Box target, out Vec tip, out Vec aim, Box? delivery = null, Vec? eyes = null,
                          Func<object, bool> owners = null, double? gap = null, double? length = null, double travel = 0.0)
        {
            Frame(out Vec up, out _, out Vec h2);
            double u = Unit;
            int ui = Up;
            double off = (gap ?? Gap) * u, size = (length ?? Arrow) * u;
            Vec centre = Centre(target), extents = Extents(target);
            Vec from = eyes ?? EyesFor(target, delivery, owners);
            Vec level = Flat(from - centre);
            Vec toEyes = Norm(level) > 0.5 ? level : -h2;
            Vec side = Cross(up, toEyes);
            // Ways from the world's up and the eyes alone can all be blocked (a button on a door swung open sideways):
            // the way out of what the part is mounted on is one more, and the one taken where none is free.
            Vec? mount = Mount(target, owners, from);
            Vec face = mount ?? toEyes;
            Vec[] ways =
            {
                up, UnitOf(up + toEyes), toEyes, UnitOf(toEyes + side), UnitOf(toEyes - side), UnitOf(up + side),
                UnitOf(up - side), side, -side, UnitOf(face + up), face,
            };
            Vec TipOf(Vec way) => centre + way * (Reach(extents, way) + off);
            tip = mount.HasValue ? TipOf(face) : centre + up * (extents[ui] + off);
            aim = centre;
            Vec sight = UnitOf(centre - from);
            double endOn = 1.0 - Math.Cos(EndOn * Degrees);
            double best = 0.0;
            bool any = false;
            for (int i = 0; i < ways.Length; i++)
            {
                Vec way = ways[i];
                if (Dot(way, way) < 0.5) continue;  // a blend of opposites
                Vec at = TipOf(way);
                Vec tail = at + way * size;
                // Room for it to turn about its axis (a flat one faces the eyes), over its whole sweep.
                if (Rays != null && Rays.OccupiedCapsule(at + way * (0.01 * u), tail + way * (Math.Max(0.0, travel) * u), Math.Max(0.005 * u, size * 0.3)))
                    continue;
                int seen = (Clear(from, at) ? 1 : 0) + (Clear(from, tail) ? 1 : 0);
                if (seen == 0) continue;
                // Read best across the line of sight; within EndOn of it, it is no arrow at all.
                double across = 1.0 - Math.Abs(Dot(way, sight));
                double score = seen * 5.0 + 3.0 * Dot(way, up) + 3.0 * across - (across < endOn ? 8.0 : 0.0)
                               - (Clear(at, centre, owners) ? 0.0 : 5.0) - i * 0.01;
                if (any && !(score > best + Eps)) continue;
                any = true;
                best = score;
                tip = at;
            }
            return any;
        }

        // The axis a flat part's face looks along (its thinnest), or -1 where it is no flat part: its thinnest extent under
        // three quarters of the next (a rod's two thin sides are no face) and under half its face's size.
        static int Face(Vec extents)
        {
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (a, b) => extents[a] != extents[b] ? extents[a].CompareTo(extents[b]) : a.CompareTo(b));
            double thin = extents[order[0]], mid = extents[order[1]], large = extents[order[2]];
            return thin < 0.75 * mid && thin < 0.25 * (mid + large) ? order[0] : -1;
        }

        // The way out of what target is mounted on, toward the eyes: the face of the first thing a ray from the eyes' side
        // meets at or behind it, its own owners passed over; null where nothing is behind it.
        Vec? Mount(Box target, Func<object, bool> owners, Vec eyes)
        {
            if (Rays == null) return null;
            double u = Unit;
            Vec centre = Centre(target), extents = Extents(target);
            Vec toward = centre - eyes;
            if (Norm(toward) < 1e-4 * u) return null;
            toward = UnitOf(toward);
            double depth = Norm(extents), far = depth + 0.5 * u;
            Vec origin = centre + toward * -far;
            var passed = new List<object>();
            Func<object, bool> mine = owners ?? Nothing;
            Func<object, bool> skip = o => mine(o) || passed.Contains(o);
            while (true)
            {
                if (!Rays.Cast(origin, toward, far + depth + 0.25 * u, skip, out Hit hit)) return null;
                if (hit.distance < far - depth)  // in front of the part: in the way, not what it sits on
                {
                    passed.Add(hit.owner);
                    continue;
                }
                return UnitOf(Dot(hit.normal, toward) < 0 ? hit.normal : -hit.normal);
            }
        }
    }

    /// <summary>
    /// A scene of axis-aligned boxes, as <see cref="ISpotRays"/>: the engine-free scene the model's golden cases run in
    /// (pythontk's <c>BoxRays</c>). A ray starting inside a box passes it, as a physics engine's does; room is exact for a
    /// sphere, and for a capsule read along its axis every quarter radius.
    /// </summary>
    public sealed class BoxRays : ISpotRays
    {
        readonly List<(object owner, SpotPlacementModel.Box box)> _boxes = new List<(object, SpotPlacementModel.Box)>();

        /// <summary>Add a box for <paramref name="owner"/>.</summary>
        public void Add(object owner, SpotPlacementModel.Vec low, SpotPlacementModel.Vec high)
            => _boxes.Add((owner, new SpotPlacementModel.Box(low, high)));

        /// <inheritdoc/>
        public bool Cast(SpotPlacementModel.Vec origin, SpotPlacementModel.Vec direction, double far, Func<object, bool> skip,
                         out SpotPlacementModel.Hit hit)
        {
            hit = default;
            bool any = false;
            foreach (var (owner, box) in _boxes)
            {
                if (skip != null && skip(owner)) continue;
                bool inside = true;
                for (int a = 0; a < 3; a++)
                    if (!(box.low[a] < origin[a] && origin[a] < box.high[a])) inside = false;
                if (inside) continue;  // starts inside it
                double enter = 0.0, leave = far;
                int axis = -1;
                for (int a = 0; a < 3; a++)
                {
                    double d = Math.Abs(direction[a]) > 1e-12 ? direction[a] : 1e-12;
                    double t1 = (box.low[a] - origin[a]) / d, t2 = (box.high[a] - origin[a]) / d;
                    if (Math.Min(t1, t2) > enter)
                    {
                        enter = Math.Min(t1, t2);
                        axis = a;
                    }
                    leave = Math.Min(leave, Math.Max(t1, t2));
                }
                if (!(leave >= enter && enter <= far) || (any && !(enter < hit.distance))) continue;
                double nx = axis == 0 ? (direction[0] > 0 ? -1.0 : 1.0) : 0.0;
                double ny = axis == 1 ? (direction[1] > 0 ? -1.0 : 1.0) : 0.0;
                double nz = axis == 2 ? (direction[2] > 0 ? -1.0 : 1.0) : 0.0;
                hit = new SpotPlacementModel.Hit(enter, new SpotPlacementModel.Vec(nx, ny, nz), owner);
                any = true;
            }
            return any;
        }

        static double GapTo(SpotPlacementModel.Vec point, SpotPlacementModel.Box box)
        {
            double sum = 0.0;
            for (int a = 0; a < 3; a++)
            {
                double g = Math.Max(Math.Max(box.low[a] - point[a], 0.0), point[a] - box.high[a]);
                sum += g * g;
            }
            return Math.Sqrt(sum);
        }

        /// <inheritdoc/>
        public bool OccupiedSphere(SpotPlacementModel.Vec at, double radius)
        {
            foreach (var (_, box) in _boxes)
                if (GapTo(at, box) < radius) return true;
            return false;
        }

        /// <inheritdoc/>
        public bool OccupiedCapsule(SpotPlacementModel.Vec a, SpotPlacementModel.Vec b, double radius)
        {
            SpotPlacementModel.Vec ab = b - a;
            double length = Math.Sqrt(ab.x * ab.x + ab.y * ab.y + ab.z * ab.z);
            int steps = radius > 0 ? Math.Max(1, (int)Math.Ceiling(length / (radius * 0.25))) : 1;
            foreach (var (_, box) in _boxes)
                for (int s = 0; s <= steps; s++)
                {
                    var p = new SpotPlacementModel.Vec(a.x + ab.x * s / steps, a.y + ab.y * s / steps, a.z + ab.z * s / steps);
                    if (GapTo(p, box) < radius) return true;
                }
            return false;
        }
    }
}
