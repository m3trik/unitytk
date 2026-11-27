// ----------------------------------------------------------------------------
// The Interaction feature's XRI door, its editor half (M3trik.Unitytk.Interaction.XRI.Editor),
// compiled only with the toolkit 3.x in the project, as the door's runtime half is: it
// registers itself with Interactions as the editor loads.
// ----------------------------------------------------------------------------
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace M3trik.Unitytk.Interaction.XRI
{
    /// <summary>
    /// Every target made, selectable by a headset's controllers: an <see cref="XRSimpleInteractable"/>
    /// and an <see cref="XRSelect"/> put on it, and the interactable's select and its end wired to it
    /// (its meshes' colliders are <see cref="Interactions.MakeTarget"/>'s, given once for every input).
    /// </summary>
    /// <remarks>
    /// Prior art: as <see cref="XRSelect"/>'s. The toolkit's own setup is a component added
    /// by hand on each interactable (Add Component, or its Starter Assets' prefabs); the door
    /// does it for every target as the target is made.
    /// </remarks>
    [InitializeOnLoad]
    sealed class XRInput : ITargetInput
    {
        static XRInput() => Interactions.Register(new XRInput());

        public string Name => "xr";

        public void Wire(GameObject node, OpContext ctx)
        {
            XRSimpleInteractable interactable = node.GetComponent<XRSimpleInteractable>();
            string interactableKey = OpContextEdits.Key(node, typeof(XRSimpleInteractable));
            if (interactable == null && !ctx.Planned("create", interactableKey)
                && (ctx == null || ctx.ShouldCreate(interactableKey, "let a controller select it")))
                interactable = ctx.AddComponent<XRSimpleInteractable>(node);
            XRSelect select = node.GetComponent<XRSelect>();
            string selectKey = OpContextEdits.Key(node, typeof(XRSelect));
            if (select == null && !ctx.Planned("create", selectKey)
                && (ctx == null || ctx.ShouldCreate(selectKey, "select it for the controller's hand")))
                select = ctx.AddComponent<XRSelect>(node);

            const string what = "select and press it for the controller's hand while the controller selects";
            bool entered = interactable != null && select != null
                           && PersistentCalls.Wired(interactable, interactable.selectEntered, select, nameof(XRSelect.OnSelectEntered), ctx: ctx);
            bool exited = interactable != null && select != null
                          && PersistentCalls.Wired(interactable, interactable.selectExited, select, nameof(XRSelect.OnSelectExited), ctx: ctx);
            if (entered && exited) return;
            if ((interactable == null || select == null) && ctx.Planned("change", node, what)) return;
            if (ctx != null && !ctx.ShouldChange(Interactions.Asked(interactable, node), what)) return;
            if (!entered) UnityEventTools.AddPersistentListener(interactable.selectEntered, select.OnSelectEntered);
            if (!exited) UnityEventTools.AddPersistentListener(interactable.selectExited, select.OnSelectExited);
            ctx.Edited(interactable);
        }

        // A headset's rig brings its own interactors and hands: nothing to give the eyes.
        public void Equip(Camera eyes, OpContext ctx)
        {
        }

        public void Equip(PlannedObject eyes, OpContext ctx)
        {
        }
    }
}
