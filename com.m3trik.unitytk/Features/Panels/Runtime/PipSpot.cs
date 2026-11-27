using UnityEngine;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// The mark of a spot a build made for a <see cref="PipShot"/>'s picture to be taken from (the panels' action
    /// <c>PanelsActions.AddShotFrom</c> adds it as it makes one): what a later build may take away again when the
    /// shot is given a spot of its own. An object a person or a delivery brought never carries it, so it is never
    /// taken away. It does nothing in a player.
    /// </summary>
    /// <remarks>Prior art: as <see cref="PipView"/>'s; Unity's own <c>EditorOnly</c> tag strips an object from a
    /// build, which a spot the player's picture is taken from must not be.</remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class PipSpot : MonoBehaviour
    {
    }
}
