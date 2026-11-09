using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The pointer an <c>ArticulatedRigPointerGrab</c> reads: a button by index
    /// (0 left, 1 right, 2 middle), the pointer's position on screen and the
    /// scroll. The runtime part reads the legacy Input Manager's mouse when
    /// nothing is registered (<c>ArticulatedRigPointerGrab.UsePointer</c>); the
    /// Input System door registers the Input System's mouse, and a touch or a
    /// custom pointer registers its own.
    /// </summary>
    public interface IPointerInput
    {
        /// <summary>Whether <paramref name="button"/> can be read at all (a mouse is there).</summary>
        bool Available(int button);

        /// <summary>Whether <paramref name="button"/> went down this frame.</summary>
        bool Pressed(int button);

        /// <summary>Whether <paramref name="button"/> is down.</summary>
        bool Held(int button);

        /// <summary>The pointer's position in screen pixels.</summary>
        Vector3 Position { get; }

        /// <summary>This frame's scroll: positive pushes the held point away from the camera.</summary>
        float Scroll { get; }
    }
}
