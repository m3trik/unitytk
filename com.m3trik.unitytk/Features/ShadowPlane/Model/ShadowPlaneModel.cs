// ----------------------------------------------------------------------------
// The ShadowPlane feature's model: pythontk's ``ShadowProjection.model`` and
// ``ShadowModel.placement`` ported term for term, engine-free (its assembly
// references no UnityEngine), so the same code places a plane in a player, in
// the editor and under `dotnet test`.
//
// The port is pinned against the reference by the golden cases of
// ``ptk.Conformance.cases("shadow_projection")``: under `dotnet test` in
// test/models_dotnet, and in Unity by test_shadow_plane_runtime_integration.py.
// Unity's frame (the plane's space, its axes, the record's unit) is the runtime
// part's (ShadowPlane), never this one's.
// ----------------------------------------------------------------------------
using System;

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// pythontk <c>ShadowProjection.model</c>, <c>ShadowModel.placement</c> and
    /// <c>ShadowProjection.fade</c>, ported term for term (each producer's live
    /// preview mirrors the same body). Double precision on purpose: a directional source is
    /// written as a point <see cref="FarFactor"/> object sizes away, and in
    /// single precision the base/top factor difference that becomes the reach
    /// loses ~10 % there.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own shadows (shadow maps, a <c>Projector</c>, URP's decal
    /// projector) draw the shadow of the mesh a light sees, not the rasterized
    /// silhouette the DCC rig stamped and re-places as the light moves; the WebXR
    /// viewer (<c>shadow_rig.js</c>) and the DCC expressions place it with this one
    /// closed form, so a deliverable shadows alike everywhere only if the reference
    /// is ported and held to its golden cases.
    /// </remarks>
    public static class ShadowPlaneModel
    {
        /// <summary>Default cap on the shadow's reach, in object heights (<c>ShadowProjection.DEFAULT_MAX_STRETCH</c>).</summary>
        public const double DefaultMaxStretch = 6.0;

        /// <summary>How many object sizes back a directional source is placed when it is written as a point (<c>ShadowProjection.FAR_FACTOR</c>).</summary>
        public const double FarFactor = 1.0e6;

        /// <summary>The horizontal distance under which the source is straight overhead (<c>ShadowProjection.EPS</c>).</summary>
        public const double Eps = 1.0e-6;

        /// <summary>The plane's lift above the ground (the producers' <c>GROUND_OFFSET</c>), in units of the record it came from.</summary>
        public const double GroundOffset = 0.01;

        /// <summary>The fade's default exponent on the shadow's stretch (<c>ShadowProjection.DEFAULT_FALLOFF_POWER</c>).</summary>
        public const double DefaultFalloffPower = 1.2;

        /// <summary>
        /// The canvas fractions <c>(u0, u1, w0, w1)</c> a plane is placed with when it
        /// carries no silhouette (<c>ShadowModel.CANVAS</c>): from a radius behind the
        /// anchor to a top radius past where the head lands, half the width either side.
        /// </summary>
        public static readonly double[] DefaultCanvas = { -1.0, 1.0, -0.5, 0.5 };

        /// <summary>The analytic shadow of the bounding cylinder — pythontk <c>ShadowModel</c>.</summary>
        public struct Result
        {
            /// <summary>Where the base-disk centre lands (horizontal, world).</summary>
            public double anchorX, anchorZ;

            /// <summary>Unit u: horizontal, from the light through the target.</summary>
            public double bearingX, bearingZ;

            /// <summary>The base and top disks' projection factors.</summary>
            public double kBase, kTop;

            /// <summary>How far past the anchor the top's projection lands.</summary>
            public double reach;

            /// <summary>The base and top disks' projected radii.</summary>
            public double baseRadius, topRadius;

            /// <summary>Extent across u.</summary>
            public double width;

            /// <summary>The source had no horizontal bearing: the fallback bearing was used.</summary>
            public bool overhead;

            /// <summary>Extent along u: <c>reach + base + top</c> (<c>ShadowModel.length</c>).</summary>
            public double Length => reach + baseRadius + topRadius;

            /// <summary>Unit w — across the bearing, the plane's local +X (x).</summary>
            public double AcrossX => bearingZ;

            /// <summary>Unit w — across the bearing, the plane's local +X (z).</summary>
            public double AcrossZ => -bearingX;

            /// <summary>The plane's yaw about world Y, in degrees: its local +Z turned onto u (atan2 of u's x and z).</summary>
            public double YawDegrees => Math.Atan2(bearingX, bearingZ) * 180.0 / Math.PI;

            /// <summary>
            /// Where a plane carrying a canvas of fractions (u0, u1, w0, w1) sits
            /// at this model: its centre (horizontal, world), its extent along u
            /// and across it — <c>ShadowModel.placement</c>.
            /// </summary>
            public void Placement(double u0, double u1, double w0, double w1,
                out double centreX, out double centreZ, out double along, out double across)
            {
                // The far edge in top radii from where the head lands; the near
                // edge as a fraction of it, so the anchor -- a grounded target's
                // feet -- keeps its place in the texture as the shadow stretches.
                double uHi = Math.Max(0.0, reach + u1 * topRadius);
                double uLo = u0 * uHi;
                double wLo = w0 * width;
                double wHi = w1 * width;
                double cu = 0.5 * (uLo + uHi);
                double cw = 0.5 * (wLo + wHi);
                centreX = anchorX + bearingX * cu + AcrossX * cw;
                centreZ = anchorZ + bearingZ * cu + AcrossZ * cw;
                along = uHi - uLo;
                across = wHi - wLo;
            }
        }

        /// <summary>
        /// The shadow of the bounding cylinder standing on the contact, cast by a
        /// positional source. Lengths in one unit (scene units); ground is the
        /// ground plane's height along Y; maxStretch caps the reach in heights.
        /// </summary>
        public static Result Evaluate(
            double contactX, double contactY, double contactZ,
            double lightX, double lightY, double lightZ,
            double ground, double radius, double height, double maxStretch)
        {
            double dX = contactX - lightX;
            double dZ = contactZ - lightZ;
            double dist = Math.Sqrt(dX * dX + dZ * dZ);
            var m = new Result();
            if (dist > Eps)
            {
                m.bearingX = dX / dist;
                m.bearingZ = dZ / dist;
                m.overhead = false;
            }
            else
            {
                // Straight overhead: the second horizontal axis (+Z), as the DCC
                // expressions fall back.
                m.bearingX = 0.0;
                m.bearingZ = 1.0;
                m.overhead = true;
            }
            double kMax = 1.0 + maxStretch;
            double kBase = Math.Min(Math.Max((lightY - ground) / Math.Max(1e-4, lightY - contactY), 0.0), kMax);
            double kCap = Math.Min(kMax, kBase + maxStretch * height / Math.Max(dist, Eps));
            double kTop = Math.Min(Math.Max((lightY - ground) / Math.Max(1e-4, lightY - contactY - height), 0.0), kCap);
            m.kBase = kBase;
            m.kTop = kTop;
            m.reach = Math.Max(0.0, dist * (kTop - kBase));
            m.width = 2.0 * radius * Math.Max(kTop, kBase);
            m.anchorX = lightX + dX * kBase;
            m.anchorZ = lightZ + dZ * kBase;
            m.baseRadius = radius * kBase;
            m.topRadius = radius * kTop;
            return m;
        }

        /// <summary>
        /// A directional source written as a point: <paramref name="scale"/> x
        /// <see cref="FarFactor"/> back from the contact along the way it shines
        /// (<c>ShadowProjection.far_point</c>). A zero direction is noon (straight down)
        /// here, where the reference (<c>ShadowProjection._unit</c>) raises
        /// <c>ValueError</c>; no golden case covers it.
        /// </summary>
        /// <param name="scale">The object's size: <c>max(height, 2 radius)</c>, floored at 1e-3.</param>
        public static void FarPoint(
            double contactX, double contactY, double contactZ,
            double dirX, double dirY, double dirZ, double scale,
            out double x, out double y, out double z)
        {
            double n = Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
            if (n < 1e-12) { dirX = 0.0; dirY = -1.0; dirZ = 0.0; n = 1.0; }  // a zero direction: noon
            double far = FarFactor * Math.Max(scale, 1e-3);
            x = contactX - dirX / n * far;
            y = contactY - dirY / n * far;
            z = contactZ - dirZ / n * far;
        }

        /// <summary>
        /// A directional source: written as a point FarFactor x max(height,
        /// 2 radius) back along the direction it shines from the contact
        /// (<see cref="FarPoint"/>), then the same body.
        /// </summary>
        public static Result EvaluateDirectional(
            double contactX, double contactY, double contactZ,
            double dirX, double dirY, double dirZ,
            double ground, double radius, double height, double maxStretch)
        {
            FarPoint(contactX, contactY, contactZ, dirX, dirY, dirZ, Math.Max(height, 2.0 * radius),
                     out double lightX, out double lightY, out double lightZ);
            return Evaluate(contactX, contactY, contactZ, lightX, lightY, lightZ, ground, radius, height, maxStretch);
        }

        /// <summary>
        /// How strongly a plane at <paramref name="model"/> draws its shadow, 0 to
        /// <paramref name="intensity"/> (<c>ShadowProjection.fade</c>):
        /// <c>clamp(dist * source * rise, 0, 1)</c>, where <c>dist = intensity /
        /// max(0.001, stretch^falloffPower)</c> with <c>stretch = max(1, length /
        /// max(1e-4, 2 radius))</c>, <c>source = clamp((lightUp - contactUp) / sourceRamp, 0, 1)</c>
        /// and <c>rise = clamp(1 - max(0, contactUp - ground) / max(0.001, fadeHeight), 0, 1)</c>.
        /// Lengths in metres; the floors are the reference's guards.
        /// </summary>
        /// <param name="model">The model at the plane's source and contact.</param>
        /// <param name="lightUp">The source's height (a sun's far point, <see cref="FarPoint"/>).</param>
        /// <param name="contactUp">The contact's height.</param>
        /// <param name="ground">The ground's height.</param>
        /// <param name="radius">The target's footprint radius.</param>
        /// <param name="intensity">The shadow's darkness, 0 to 1: the fade's ceiling.</param>
        /// <param name="falloffPower">The exponent on the stretch (<see cref="DefaultFalloffPower"/>).</param>
        /// <param name="fadeHeight">The rise off the ground at which the shadow is gone.</param>
        /// <param name="sourceRamp">The source's height over the contact at which the shadow is all in, in
        /// metres: a record plane's <c>source_ramp</c> times its <c>unit_scale</c>.</param>
        public static double Fade(Result model, double lightUp, double contactUp, double ground, double radius,
            double intensity, double falloffPower, double fadeHeight, double sourceRamp)
        {
            double stretch = Math.Max(1.0, model.Length / Math.Max(1e-4, 2.0 * radius));
            double dist = intensity / Math.Max(0.001, Math.Pow(stretch, falloffPower));
            double source = Math.Min(Math.Max((lightUp - contactUp) / Math.Max(1e-4, sourceRamp), 0.0), 1.0);
            double rise = 1.0 - Math.Max(0.0, contactUp - ground) / Math.Max(0.001, fadeHeight);
            rise = Math.Min(Math.Max(rise, 0.0), 1.0);
            return Math.Min(Math.Max(dist * source * rise, 0.0), 1.0);
        }

        /// <summary>The fade of a plane cast by a positional source (<see cref="Evaluate"/>, then <see cref="Fade(Result, double, double, double, double, double, double, double, double)"/>).</summary>
        public static double Fade(
            double contactX, double contactY, double contactZ,
            double lightX, double lightY, double lightZ,
            double ground, double radius, double height, double maxStretch,
            double intensity, double falloffPower, double fadeHeight, double sourceRamp)
        {
            Result m = Evaluate(contactX, contactY, contactZ, lightX, lightY, lightZ, ground, radius, height, maxStretch);
            return Fade(m, lightY, contactY, ground, radius, intensity, falloffPower, fadeHeight, sourceRamp);
        }

        /// <summary>The fade of a plane cast by a directional source, from its far point (<see cref="FarPoint"/>).</summary>
        public static double FadeDirectional(
            double contactX, double contactY, double contactZ,
            double dirX, double dirY, double dirZ,
            double ground, double radius, double height, double maxStretch,
            double intensity, double falloffPower, double fadeHeight, double sourceRamp)
        {
            FarPoint(contactX, contactY, contactZ, dirX, dirY, dirZ, Math.Max(height, 2.0 * radius),
                     out double lightX, out double lightY, out double lightZ);
            return Fade(contactX, contactY, contactZ, lightX, lightY, lightZ, ground, radius, height, maxStretch,
                        intensity, falloffPower, fadeHeight, sourceRamp);
        }
    }
}
