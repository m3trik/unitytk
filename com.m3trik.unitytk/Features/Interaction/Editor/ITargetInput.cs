using System.Runtime.CompilerServices;
using M3trik.Unitytk.Kernel;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Interaction.Tests")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Interaction.UGUI.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Interaction.UGUI.Tests")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Interaction.XRI.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Interaction.XRI.Tests")]

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// The seam <see cref="Interactions.MakeTarget"/> and <see cref="Interactions.EquipUser(Camera, Vector3, float, OpContext)"/>
    /// reach an input package through: each input door's editor half (compiled only with its
    /// package in the project) registers one as the editor loads
    /// (<see cref="Interactions.Register"/>), every target made is wired for each, and a user
    /// equipped is equipped for each, so the feature names no input type and one scene serves
    /// every input the project has (a mouse, a headset's controllers).
    /// </summary>
    internal interface ITargetInput
    {
        /// <summary>The input's name; the inputs wire in its order, so a dry run plans as its live run makes.</summary>
        string Name { get; }

        /// <summary>
        /// Make the target on <paramref name="node"/> (made, or planned in this dry run)
        /// selectable by this input, asking <paramref name="ctx"/> first; a node it already
        /// serves gets nothing.
        /// </summary>
        void Wire(GameObject node, OpContext ctx);

        /// <summary>
        /// Let the user who sees through <paramref name="eyes"/> act through this input, asking
        /// <paramref name="ctx"/> first: a pointer's way into the scene and its look; nothing for
        /// an input whose rig brings its own (a headset's controllers). What is there already
        /// gets nothing.
        /// </summary>
        void Equip(Camera eyes, OpContext ctx);

        /// <summary>
        /// <see cref="Equip(Camera, OpContext)"/> for <paramref name="eyes"/>, a camera this dry run only planned: what the
        /// live run asks of the camera it makes, planned once each (<see cref="OpContextEdits.PlanCreate"/>).
        /// </summary>
        void Equip(PlannedObject eyes, OpContext ctx);
    }
}
