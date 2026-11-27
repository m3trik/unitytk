// ----------------------------------------------------------------------------
// The Interaction feature's uGUI door, its editor half
// (M3trik.Unitytk.Interaction.UGUI.Editor), compiled only with com.unity.ugui in
// the project, as the door's runtime half is: it registers itself as the editor
// loads, so every target Interactions.MakeTarget makes is clickable without the
// feature naming a uGUI type.
// ----------------------------------------------------------------------------
using System;
using M3trik.Unitytk.Kernel;
using UnityEditor.EventSystems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Interaction.UGUI
{
    /// <summary>
    /// The uGUI door's actions: a camera set up to send its clicks into the scene. Each takes
    /// an optional <see cref="OpContext"/>: a dry run plans, a menu's run is one undo step,
    /// and none just runs; a second run changes nothing. Every target made is wired by the
    /// door itself (<see cref="PointerSelect"/> and its meshes' colliders).
    /// </summary>
    /// <remarks>
    /// Prior art: as <see cref="PointerSelect"/>'s. A missing EventSystem is made as Unity's
    /// own GameObject &gt; UI &gt; Event System makes it: uGUI's <c>InputModuleComponentFactory</c>
    /// adds the input module, which the Input System package overrides with its own when the
    /// project reads input through it.
    /// </remarks>
    public static class PointerActions
    {
        /// <summary>The name of the EventSystem <see cref="SetUpPointer"/> makes.</summary>
        public const string EventSystemName = "EventSystem";

        /// <summary>
        /// Let <paramref name="camera"/>'s clicks reach the scene's targets: a
        /// <see cref="PhysicsRaycaster"/> on it, a <see cref="PointerAssist"/> where
        /// <paramref name="assist"/> (a click just off a target in play selects it), and an EventSystem
        /// in its scene when no scene has one, with the input module the project's input handling uses.
        /// </summary>
        /// <param name="camera">The camera the user sees through; null: the scene's main camera.</param>
        /// <param name="assist">Give it a <see cref="PointerAssist"/>; false: a click selects only what it hits (one there stays).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <exception cref="ArgumentException">No camera is named and the scene has no main camera.</exception>
        public static void SetUpPointer(Camera camera = null, bool assist = true, OpContext ctx = null)
        {
            camera = Viewer.ViewCamera(camera);
            if (camera == null) throw new ArgumentException("the scene has no main camera: name the camera the user sees through", nameof(camera));
            if (!camera.gameObject.scene.IsValid()) throw new ArgumentException($"'{camera.name}' is not in a scene (a prefab asset?): name its instance", nameof(camera));
            string key = OpContextEdits.Key(camera.gameObject, typeof(PhysicsRaycaster));
            if (camera.GetComponent<PhysicsRaycaster>() == null && !ctx.Planned("create", key)
                && (ctx == null || ctx.ShouldCreate(key, RaycasterAction)))
                ctx.AddComponent<PhysicsRaycaster>(camera.gameObject);
            string assisted = OpContextEdits.Key(camera.gameObject, typeof(PointerAssist));
            if (assist && camera.GetComponent<PointerAssist>() == null && !ctx.Planned("create", assisted)
                && (ctx == null || ctx.ShouldCreate(assisted, AssistAction)))
                ctx.AddComponent<PointerAssist>(camera.gameObject);
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null || ctx.Planned("create", EventSystemName)) return;
            if (ctx != null && !ctx.ShouldCreate(EventSystemName, EventSystemAction)) return;
            var events = new GameObject(EventSystemName, typeof(EventSystem));
            SceneManager.MoveGameObjectToScene(events, camera.gameObject.scene);
            ctx.Created(events);
            InputModuleComponentFactory.AddInputModule(events);
            ctx.Edited(events);
        }

        /// <summary>
        /// <see cref="SetUpPointer(Camera, bool, OpContext)"/> for <paramref name="camera"/>, a camera a dry run only planned:
        /// what its live run makes on it (and an EventSystem where no scene has one), planned once each; nothing in a live run.
        /// </summary>
        /// <param name="camera">The planned camera the user sees through.</param>
        /// <param name="assist">Give it a <see cref="PointerAssist"/>.</param>
        /// <param name="ctx">The run's context.</param>
        /// <exception cref="ArgumentNullException"><paramref name="camera"/> is null.</exception>
        public static void SetUpPointer(PlannedObject camera, bool assist = true, OpContext ctx = null)
        {
            ctx.PlanCreate(OpContextEdits.Key(camera, typeof(PhysicsRaycaster)), RaycasterAction);
            if (assist) ctx.PlanCreate(OpContextEdits.Key(camera, typeof(PointerAssist)), AssistAction);
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null) ctx.PlanCreate(EventSystemName, EventSystemAction);
        }

        const string RaycasterAction = "send its clicks into the scene";
        const string AssistAction = "let a click just off a target select it";
        const string EventSystemAction = "route the pointer's clicks, with the input module the project's input handling uses";

        /// <summary>
        /// Let a desktop user look around: a <see cref="PointerLook"/> on <paramref name="camera"/>,
        /// so a drag with the right mouse button turns it. A camera that has one gets nothing. The
        /// drag is the EventSystem's (<see cref="SetUpPointer"/> makes one).
        /// </summary>
        /// <param name="camera">The camera the user sees through, in a scene (the scene's main camera when null).</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The look; null in a dry run that would add one.</returns>
        /// <exception cref="ArgumentException">No camera is named and the scene has no main camera, or it is not in a scene.</exception>
        public static PointerLook SetUpLook(Camera camera = null, OpContext ctx = null)
        {
            camera = Viewer.ViewCamera(camera);
            if (camera == null) throw new ArgumentException("the scene has no main camera: name the camera the user sees through", nameof(camera));
            if (!camera.gameObject.scene.IsValid()) throw new ArgumentException($"'{camera.name}' is not in a scene (a prefab asset?): name its instance", nameof(camera));
            PointerLook look = camera.GetComponent<PointerLook>();
            string key = OpContextEdits.Key(camera.gameObject, typeof(PointerLook));
            if (look != null || ctx.Planned("create", key)) return look;
            if (ctx != null && !ctx.ShouldCreate(key, LookAction)) return null;
            return ctx.AddComponent<PointerLook>(camera.gameObject);
        }

        /// <summary>
        /// <see cref="SetUpLook(Camera, OpContext)"/> for <paramref name="camera"/>, a camera a dry run only planned: the look
        /// its live run puts on it, planned once; nothing in a live run.
        /// </summary>
        /// <param name="camera">The planned camera the user sees through.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to put it on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="camera"/> is null.</exception>
        public static PointerLook SetUpLook(PlannedObject camera, OpContext ctx = null)
        {
            ctx.PlanCreate(OpContextEdits.Key(camera, typeof(PointerLook)), LookAction);
            return null;
        }

        const string LookAction = "turn the eyes with a right-button drag";
    }
}
