using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// Where the person looking at the scene looks from: the eyes a component was given, else the main
    /// camera's. The one fallback every component that faces, follows or frames the viewer asks.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>Camera.main</c> (the first enabled camera tagged MainCamera, cached by the
    /// engine), the convention a component falls back on when it is given no camera; a headset rig tags its
    /// eye camera so too.
    /// </remarks>
    public static class Viewer
    {
        /// <summary>The eyes: <paramref name="eyes"/>, else the main camera's transform; null where there is neither.</summary>
        /// <param name="eyes">The eyes a component was given (null: none).</param>
        public static Transform Eyes(Transform eyes = null)
        {
            if (eyes != null) return eyes;
            Camera main = Camera.main;
            return main != null ? main.transform : null;
        }

        /// <summary>The camera the viewer looks through: <paramref name="camera"/>, else the main camera; null where there is neither.</summary>
        /// <param name="camera">The camera a component was given (null: none).</param>
        public static Camera ViewCamera(Camera camera = null) => camera != null ? camera : Camera.main;
    }
}
