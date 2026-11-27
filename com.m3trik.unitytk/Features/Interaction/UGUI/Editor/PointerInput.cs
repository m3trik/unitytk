// ----------------------------------------------------------------------------
// The uGUI door's editor half: its input, registered with Interactions as the
// editor loads.
// ----------------------------------------------------------------------------
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Interaction.UGUI
{
    /// <summary>
    /// Every target made, clickable: a <see cref="PointerSelect"/> put on it (its meshes' colliders are
    /// <see cref="Interactions.MakeTarget"/>'s, given once for every input), registered with
    /// <see cref="Interactions"/> as the editor loads.
    /// </summary>
    [InitializeOnLoad]
    sealed class PointerInput : ITargetInput
    {
        static PointerInput() => Interactions.Register(new PointerInput());

        public string Name => "pointer";

        public void Wire(GameObject node, OpContext ctx)
        {
            string key = OpContextEdits.Key(node, typeof(PointerSelect));
            if (node.GetComponent<PointerSelect>() != null || ctx.Planned("create", key)) return;
            if (ctx != null && !ctx.ShouldCreate(key, "select it with a click")) return;
            ctx.AddComponent<PointerSelect>(node);
        }

        // A desktop user's: their clicks into the scene, and their look.
        public void Equip(Camera eyes, OpContext ctx)
        {
            PointerActions.SetUpPointer(eyes, ctx: ctx);
            PointerActions.SetUpLook(eyes, ctx);
        }

        public void Equip(PlannedObject eyes, OpContext ctx)
        {
            PointerActions.SetUpPointer(eyes, ctx: ctx);
            PointerActions.SetUpLook(eyes, ctx);
        }
    }
}
