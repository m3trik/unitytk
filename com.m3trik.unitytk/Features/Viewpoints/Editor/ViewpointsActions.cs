using System;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Viewpoints
{
    /// <summary>
    /// The Viewpoints feature's actions: the user's eyes made where a scene has none, the user's rig
    /// made a <see cref="Traveler"/> and stood at a viewpoint, a viewpoint made under a holder, and a
    /// scene object made a viewpoint a step brings the user to (<see cref="TravelTo"/>). Each takes an
    /// optional <see cref="OpContext"/>: a dry run plans, a menu's run is one undo step, and none just
    /// runs; a second run changes nothing. A viewpoint a dry run only planned is handed over as a
    /// <see cref="PlannedObject"/>, so the dry run plans under the keys its live run asks with.
    /// </summary>
    /// <remarks>Prior art: as <see cref="Traveler"/>'s.</remarks>
    public static class ViewpointsActions
    {
        /// <summary>The name of the camera <see cref="AddEyes"/> makes where a scene has no main camera.</summary>
        public const string EyesName = "Main Camera";

        /// <summary>
        /// The eyes of <paramref name="scene"/>'s user: its main camera (the first switched-on camera in it tagged
        /// <c>MainCamera</c>), else one made (a camera with an audio listener, tagged <c>MainCamera</c>, named
        /// <paramref name="name"/>).
        /// </summary>
        /// <param name="scene">The scene (default: the active one).</param>
        /// <param name="name">The name of a camera it makes.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The eyes; null in a dry run that would make them.</returns>
        /// <exception cref="ArgumentException">The scene is not loaded, or <paramref name="name"/> is empty.</exception>
        public static Camera AddEyes(Scene scene = default, string name = EyesName, OpContext ctx = null)
        {
            if (!scene.IsValid()) scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("name a loaded scene", nameof(scene));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the camera it makes", nameof(name));
            foreach (Camera each in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
                if (each.enabled && each.gameObject.scene == scene && each.CompareTag("MainCamera")) return each;
            if (ctx.Planned("create", name)) return null;
            if (ctx != null && !ctx.ShouldCreate(name, "the user's eyes: the scene has no main camera")) return null;
            var made = new GameObject(name, typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            SceneManager.MoveGameObjectToScene(made, scene);
            ctx.Created(made);
            return made.GetComponent<Camera>();
        }

        /// <summary>
        /// Make <paramref name="rig"/> the user's body a viewpoint moves: a
        /// <see cref="Traveler"/> on it. A rig that has one gets nothing.
        /// </summary>
        /// <param name="rig">The user's rig, a scene object: the camera on a desktop, the origin in a headset.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The traveler; null in a dry run that would add one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rig"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="rig"/> is not in a scene.</exception>
        public static Traveler AddTraveler(GameObject rig, OpContext ctx = null)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig), "name the user's rig: the camera on a desktop, the origin in a headset");
            InScene(rig, nameof(rig));
            Traveler existing = rig.GetComponent<Traveler>();
            string key = OpContextEdits.Key(rig, typeof(Traveler));
            if (existing != null || ctx.Planned("create", key)) return existing;
            if (ctx != null && !ctx.ShouldCreate(key, TravelerAction)) return null;
            return ctx.AddComponent<Traveler>(rig);
        }

        /// <summary>
        /// <see cref="AddTraveler(GameObject, float, OpContext)"/> on <paramref name="rig"/>, an object a dry run only planned
        /// (the eyes <see cref="AddEyes"/> would make): the traveler its live run puts on it, planned once; a new one's eye
        /// height is set as it is made, so nothing more is planned. Nothing in a live run.
        /// </summary>
        /// <param name="rig">The planned rig.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to put it on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rig"/> is null.</exception>
        public static Traveler AddTraveler(PlannedObject rig, OpContext ctx = null)
        {
            ctx.PlanCreate(OpContextEdits.Key(rig, typeof(Traveler)), TravelerAction);
            return null;
        }

        const string TravelerAction = "bring the user to each viewpoint";

        /// <summary>
        /// <see cref="AddTraveler(GameObject, OpContext)"/>, its eyes standing <paramref name="eyeHeight"/> over a
        /// viewpoint where the rig is its own eyes (<see cref="Traveler.eyeHeight"/>): one made gets it, one there
        /// is changed to it, asked.
        /// </summary>
        /// <param name="rig">The user's rig, a scene object: the camera on a desktop, the origin in a headset.</param>
        /// <param name="eyeHeight">How high the eyes stand over a viewpoint, in metres.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The traveler; null in a dry run that would add one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rig"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="rig"/> is not in a scene.</exception>
        public static Traveler AddTraveler(GameObject rig, float eyeHeight, OpContext ctx = null)
        {
            bool had = rig != null && rig.GetComponent<Traveler>() != null;
            Traveler traveler = AddTraveler(rig, ctx);
            if (traveler == null || Mathf.Approximately(traveler.eyeHeight, eyeHeight)) return traveler;
            // A new one stands its eyes so as it is made; one already there is changed, asked.
            if (had && ctx != null && !ctx.ShouldChange(traveler, $"stand its eyes {eyeHeight:0.##} m over a viewpoint")) return traveler;
            traveler.eyeHeight = eyeHeight;
            ctx.Edited(traveler);
            return traveler;
        }

        /// <summary>
        /// Stand <paramref name="traveler"/> at <paramref name="viewpoint"/> now, as its travel does
        /// (<see cref="TravelTo.Floor"/>, <see cref="TravelTo.Heading"/>), <paramref name="back"/> metres behind it
        /// along the way it faces: where the user starts, a viewpoint's marker in view ahead. One standing so
        /// already gets nothing; whether it would move is <see cref="Traveler.Solve"/>'s, so a dry run moves nothing.
        /// </summary>
        /// <param name="traveler">The user's rig.</param>
        /// <param name="viewpoint">The viewpoint.</param>
        /// <param name="back">Metres behind the viewpoint (0: on it).</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it moved the rig (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="traveler"/> or <paramref name="viewpoint"/> is null.</exception>
        public static bool GoTo(Traveler traveler, TravelTo viewpoint, float back = 0f, OpContext ctx = null)
        {
            if (traveler == null) throw new ArgumentNullException(nameof(traveler), "name the user's rig (AddTraveler)");
            if (viewpoint == null) throw new ArgumentNullException(nameof(viewpoint), "name the viewpoint (AddTravelTo)");
            Quaternion heading = viewpoint.Heading;
            Vector3 floor = viewpoint.Floor - heading * Vector3.forward * back;
            Transform rig = traveler.transform;
            traveler.Solve(floor, heading, out Vector3 position, out Quaternion rotation);
            if ((rig.position - position).sqrMagnitude <= 1e-10f && Quaternion.Angle(rig.rotation, rotation) <= 1e-3f) return false;
            if (ctx != null && !ctx.ShouldChange(rig, StandAction(viewpoint.name, back))) return true;
            traveler.GoTo(floor, heading);
            ctx.Edited(rig);
            return true;
        }

        /// <summary>
        /// <see cref="GoTo(Traveler, TravelTo, float, OpContext)"/> for <paramref name="rig"/>, a rig a dry run only planned
        /// (<see cref="AddTraveler(PlannedObject, OpContext)"/>): the move its live run makes, planned once; nothing in a live run.
        /// </summary>
        /// <param name="rig">The planned rig.</param>
        /// <param name="viewpoint">The viewpoint.</param>
        /// <param name="back">Metres behind the viewpoint (0: on it).</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the move now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rig"/> or <paramref name="viewpoint"/> is null.</exception>
        public static bool GoTo(PlannedObject rig, TravelTo viewpoint, float back = 0f, OpContext ctx = null)
        {
            if (viewpoint == null) throw new ArgumentNullException(nameof(viewpoint), "name the viewpoint (AddTravelTo)");
            return PlanGoTo(rig, viewpoint.name, back, ctx);
        }

        /// <summary>
        /// <see cref="GoTo(PlannedObject, TravelTo, float, OpContext)"/> to <paramref name="viewpoint"/>, a viewpoint the dry
        /// run only planned too (<see cref="AddTravelTo(PlannedObject, OpContext)"/>, <see cref="AddFacing(PlannedObject, Transform, OpContext)"/>).
        /// </summary>
        /// <param name="rig">The planned rig.</param>
        /// <param name="viewpoint">The planned viewpoint.</param>
        /// <param name="back">Metres behind the viewpoint (0: on it).</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the move now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rig"/> or <paramref name="viewpoint"/> is null.</exception>
        public static bool GoTo(PlannedObject rig, PlannedObject viewpoint, float back = 0f, OpContext ctx = null)
        {
            if (viewpoint == null) throw new ArgumentNullException(nameof(viewpoint), "name the planned viewpoint");
            return PlanGoTo(rig, viewpoint.Name, back, ctx);
        }

        static bool PlanGoTo(PlannedObject rig, string viewpoint, float back, OpContext ctx)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig), "name the planned rig (AddTraveler)");
            return ctx.PlanChange(rig.Path, StandAction(viewpoint, back));
        }

        // What standing the user at the viewpoint named viewpoint, back metres behind it, says.
        static string StandAction(string viewpoint, float back)
            => $"stand the user at '{viewpoint}'" + (back > 0f ? $", {back:0.##} m behind it" : "");

        /// <summary>
        /// A viewpoint named <paramref name="name"/> under the top-level object <paramref name="holder"/> of
        /// <paramref name="scene"/> (made where missing, and the holder too): a spot a builder then places. One there
        /// is kept as it stands.
        /// </summary>
        /// <param name="holder">The name of the top-level object that holds the viewpoints.</param>
        /// <param name="name">The viewpoint's name.</param>
        /// <param name="scene">The scene (default: the active one).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The viewpoint; null in a dry run that would make it (<see cref="PlannedViewpoint"/> names it then).</returns>
        /// <exception cref="ArgumentException"><paramref name="holder"/> or <paramref name="name"/> is empty.</exception>
        public static GameObject AddViewpoint(string holder, string name, Scene scene = default, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(holder)) throw new ArgumentException("name the object that holds the viewpoints", nameof(holder));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the viewpoint", nameof(name));
            if (!scene.IsValid()) scene = SceneManager.GetActiveScene();
            GameObject root = SceneRoots.Find(holder, scene);
            if (root == null)
            {
                if (ctx.Planned("create", holder) || ctx != null && !ctx.ShouldCreate(holder, "hold the spots the user is brought to"))
                {
                    ctx.PlanCreate(PlannedViewpoint(holder, name, scene).Path, ViewpointAction);
                    return null;
                }
                root = new GameObject(holder);
                SceneManager.MoveGameObjectToScene(root, scene);
                ctx.Created(root);
            }
            Transform found = root.transform.Find(name);
            if (found != null) return found.gameObject;
            string key = new PlannedObject(name, root.transform).Path;
            if (ctx.Planned("create", key) || ctx != null && !ctx.ShouldCreate(key, ViewpointAction)) return null;
            var node = new GameObject(name);
            node.transform.SetParent(root.transform, false);
            ctx.Created(node);
            return node;
        }

        /// <summary>The viewpoint <see cref="AddViewpoint"/> makes, as a dry run that only planned it hands it on.</summary>
        /// <param name="holder">The name of the top-level object that holds the viewpoints.</param>
        /// <param name="name">The viewpoint's name.</param>
        /// <param name="scene">The scene (default: the active one).</param>
        public static PlannedObject PlannedViewpoint(string holder, string name, Scene scene = default)
        {
            if (!scene.IsValid()) scene = SceneManager.GetActiveScene();
            GameObject root = SceneRoots.Find(holder, scene);
            return root != null ? new PlannedObject(name, root.transform) : new PlannedObject(name, new PlannedObject(holder));
        }

        const string ViewpointAction = "a spot the user is brought to";

        /// <summary>
        /// Make <paramref name="viewpoint"/> a place the user is brought to: a
        /// <see cref="TravelTo"/> on it, disabled unless <paramref name="startEnabled"/>, so it brings them
        /// while something enables it (a step, as it runs). They stand on it, facing its forward (+Z). A
        /// viewpoint that has one gets nothing.
        /// </summary>
        /// <param name="viewpoint">Where the user stands, a scene object.</param>
        /// <param name="startEnabled">Make it switched on (it brings the user there as the scene starts); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The travel; null in a dry run that would add one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="viewpoint"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="viewpoint"/> is not in a scene.</exception>
        public static TravelTo AddTravelTo(GameObject viewpoint, bool startEnabled = false, OpContext ctx = null)
        {
            if (viewpoint == null) throw new ArgumentNullException(nameof(viewpoint), "name where the user stands");
            InScene(viewpoint, nameof(viewpoint));
            TravelTo existing = viewpoint.GetComponent<TravelTo>();
            string key = OpContextEdits.Key(viewpoint, typeof(TravelTo));
            if (existing != null || ctx.Planned("create", key)) return existing;
            if (ctx != null && !ctx.ShouldCreate(key, TravelAction)) return null;
            TravelTo made = ctx.AddComponent<TravelTo>(viewpoint);
            made.enabled = startEnabled;
            ctx.Edited(made);
            return made;
        }

        /// <summary>
        /// <see cref="AddTravelTo(GameObject, bool, OpContext)"/> on the viewpoint where
        /// <paramref name="viewpoint"/> stands facing <paramref name="facing"/>
        /// (<see cref="AddFacing(GameObject, Transform, OpContext)"/>, named <see cref="FacingName(GameObject, Transform)"/>):
        /// a dry run plans both.
        /// </summary>
        /// <param name="viewpoint">Where the user stands, a scene object.</param>
        /// <param name="facing">What the user faces there.</param>
        /// <param name="standOff">Metres the user stands from <paramref name="facing"/>, brought toward it
        /// when the viewpoint is farther (<see cref="TravelTo.standOff"/>); 0: at the viewpoint.</param>
        /// <param name="startEnabled">Make it switched on (it brings the user there as the scene starts); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The travel; null in a dry run that would add one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="viewpoint"/> or <paramref name="facing"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="viewpoint"/> is not in a scene.</exception>
        public static TravelTo AddTravelTo(GameObject viewpoint, Transform facing, float standOff = 0f, bool startEnabled = false, OpContext ctx = null)
        {
            GameObject place = AddFacing(viewpoint, facing, ctx);
            if (place == null)
            {
                // Only planned (a dry run's; a refusal makes nothing): the travel its live run puts on it.
                ctx.PlanCreate(OpContextEdits.Key(new PlannedObject(FacingName(viewpoint, facing), viewpoint.transform), typeof(TravelTo)), TravelAction);
                return null;
            }
            bool had = place.GetComponent<TravelTo>() != null;
            TravelTo travel = AddTravelTo(place, startEnabled, ctx);
            if (travel == null || (travel.facing == facing && Mathf.Approximately(travel.standOff, standOff))) return travel;
            // A new one faces it as it is made; one already there is changed, asked.
            if (had && ctx != null && !ctx.ShouldChange(travel, $"face '{OpContextEdits.Label(facing.gameObject)}' where it stands, from {standOff:0.##} m")) return travel;
            travel.facing = facing;
            travel.standOff = standOff;
            ctx.Edited(travel);
            return travel;
        }

        /// <summary>
        /// <see cref="AddTravelTo(GameObject, bool, OpContext)"/> on <paramref name="viewpoint"/>, an object a dry run
        /// only planned: the travel its live run puts on it, planned once; nothing in a live run.
        /// </summary>
        /// <param name="viewpoint">The planned viewpoint.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to put it on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="viewpoint"/> is null.</exception>
        public static TravelTo AddTravelTo(PlannedObject viewpoint, OpContext ctx = null)
        {
            ctx.PlanCreate(OpContextEdits.Key(viewpoint, typeof(TravelTo)), TravelAction);
            return null;
        }

        /// <summary>
        /// <see cref="AddTravelTo(GameObject, Transform, float, bool, OpContext)"/> on <paramref name="viewpoint"/>, an
        /// object a dry run only planned: the facing viewpoint and its travel its live run makes, planned once each;
        /// nothing in a live run.
        /// </summary>
        /// <param name="viewpoint">The planned viewpoint.</param>
        /// <param name="facing">What the user faces there.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to put it on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="viewpoint"/> or <paramref name="facing"/> is null.</exception>
        public static TravelTo AddTravelTo(PlannedObject viewpoint, Transform facing, OpContext ctx = null)
        {
            PlannedObject place = AddFacing(viewpoint, facing, ctx);
            ctx.PlanCreate(OpContextEdits.Key(place, typeof(TravelTo)), TravelAction);
            return null;
        }

        const string TravelAction = "bring the user here while a step runs";

        /// <summary>
        /// A viewpoint where <paramref name="viewpoint"/> stands, turned level toward
        /// <paramref name="facing"/> (the centre of its renderers' bounds, else its pivot): a child
        /// of the viewpoint named for what it faces (<see cref="FacingName(GameObject, Transform)"/>), so one place
        /// serves steps that face different things, two of one name among them. Made once: one there is kept as it
        /// stands (turned by hand, it stays turned).
        /// </summary>
        /// <param name="viewpoint">Where the user stands, a scene object.</param>
        /// <param name="facing">What the user faces there.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The facing viewpoint; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="viewpoint"/> or <paramref name="facing"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="viewpoint"/> is not in a scene.</exception>
        public static GameObject AddFacing(GameObject viewpoint, Transform facing, OpContext ctx = null)
        {
            if (viewpoint == null) throw new ArgumentNullException(nameof(viewpoint), "name where the user stands");
            if (facing == null) throw new ArgumentNullException(nameof(facing), "name what the user faces");
            InScene(viewpoint, nameof(viewpoint));
            string name = FacingName(viewpoint, facing);
            Transform existing = viewpoint.transform.Find(name);
            if (existing != null) return existing.gameObject;
            string key = new PlannedObject(name, viewpoint.transform).Path;
            if (ctx.Planned("create", key)) return null;
            if (ctx != null && !ctx.ShouldCreate(key, FacingAction(facing))) return null;
            var made = new GameObject(name);
            made.transform.SetParent(viewpoint.transform, false);
            Vector3 at = viewpoint.transform.position;
            Vector3 to = SceneBounds.Of(facing, includeDisabled: true).center - at;
            to.y = 0f;
            made.transform.SetPositionAndRotation(at, to.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(to.normalized, Vector3.up)
                : viewpoint.transform.rotation);
            ctx.Created(made);
            return made;
        }

        /// <summary>
        /// <see cref="AddFacing(GameObject, Transform, OpContext)"/> at <paramref name="viewpoint"/>, an object a dry run
        /// only planned: the facing viewpoint its live run makes, planned once, handed on for what is made on it.
        /// </summary>
        /// <param name="viewpoint">The planned viewpoint.</param>
        /// <param name="facing">What the user faces there.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>The facing viewpoint, as planned.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="viewpoint"/> or <paramref name="facing"/> is null.</exception>
        public static PlannedObject AddFacing(PlannedObject viewpoint, Transform facing, OpContext ctx = null)
        {
            if (viewpoint == null) throw new ArgumentNullException(nameof(viewpoint), "name the planned viewpoint");
            if (facing == null) throw new ArgumentNullException(nameof(facing), "name what the user faces");
            var place = new PlannedObject(FacingName(viewpoint, facing), viewpoint);
            ctx.PlanCreate(place.Path, FacingAction(facing));
            return place;
        }

        static string FacingAction(Transform facing) => $"stand the user here facing '{OpContextEdits.Label(facing.gameObject)}'";

        /// <summary>
        /// Take away the viewpoint named <paramref name="name"/> under <paramref name="holder"/>
        /// (<see cref="AddViewpoint"/>), with what is on it: one nothing brings the user to any more. None there: nothing.
        /// </summary>
        /// <param name="holder">The top-level object holding the viewpoints.</param>
        /// <param name="name">The viewpoint's name.</param>
        /// <param name="scene">The scene (default: the active one).</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it took one away (or would, in a dry run).</returns>
        /// <exception cref="ArgumentException"><paramref name="holder"/> or <paramref name="name"/> is empty.</exception>
        public static bool RemoveViewpoint(string holder, string name, Scene scene = default, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(holder)) throw new ArgumentException("name the object that holds the viewpoints", nameof(holder));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the viewpoint", nameof(name));
            if (!scene.IsValid()) scene = SceneManager.GetActiveScene();
            GameObject root = SceneRoots.Find(holder, scene);
            Transform found = root == null ? null : root.transform.Find(name);
            if (found == null) return false;
            if (ctx != null && !ctx.ShouldDelete(found.gameObject, "the spot no step brings the user to any more")) return true;
            ctx.Destroy(found.gameObject);
            return true;
        }

        /// <summary>
        /// Take away the place beside <paramref name="viewpoint"/> turned toward <paramref name="facing"/>
        /// (<see cref="AddFacing(GameObject, Transform, OpContext)"/>), with its travel: a viewpoint now stood on as it
        /// stands has no use for it. None there: nothing.
        /// </summary>
        /// <param name="viewpoint">Where the user stands.</param>
        /// <param name="facing">What the place faced.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it took one away (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="viewpoint"/> or <paramref name="facing"/> is null.</exception>
        public static bool RemoveFacing(GameObject viewpoint, Transform facing, OpContext ctx = null)
        {
            if (viewpoint == null) throw new ArgumentNullException(nameof(viewpoint), "name where the user stands");
            if (facing == null) throw new ArgumentNullException(nameof(facing), "name what the place faced");
            Transform existing = viewpoint.transform.Find(FacingName(viewpoint, facing));
            if (existing == null) return false;
            if (ctx != null && !ctx.ShouldDelete(existing.gameObject, "the place facing a part: the viewpoint is stood on as it stands")) return true;
            ctx.Destroy(existing.gameObject);
            return true;
        }

        /// <summary>
        /// The name <see cref="AddFacing(GameObject, Transform, OpContext)"/> gives the viewpoint at <paramref name="viewpoint"/>
        /// facing <paramref name="facing"/>: <c>&lt;viewpoint&gt;_FACING_&lt;facing's path&gt;</c>, the path from its scene's
        /// root with <c>|</c> between its parts (<c>/</c> would read as a path where it is found by name), so two faced
        /// objects of one name (<c>A/SCREW</c>, <c>B/SCREW</c>) get a viewpoint each.
        /// </summary>
        /// <param name="viewpoint">Where the user stands.</param>
        /// <param name="facing">What the user faces there.</param>
        public static string FacingName(GameObject viewpoint, Transform facing) => $"{viewpoint.name}_FACING_{Faced(facing)}";

        /// <summary>The name <see cref="AddFacing(PlannedObject, Transform, OpContext)"/> gives the viewpoint at <paramref name="viewpoint"/>, only planned, facing <paramref name="facing"/> (as <see cref="FacingName(GameObject, Transform)"/>).</summary>
        /// <param name="viewpoint">Where the user stands, as planned.</param>
        /// <param name="facing">What the user faces there.</param>
        public static string FacingName(PlannedObject viewpoint, Transform facing) => $"{viewpoint.Name}_FACING_{Faced(facing)}";

        // What a facing viewpoint's name says it faces: the faced object's path, one name.
        static string Faced(Transform facing) => OpContextEdits.Label(facing.gameObject).Replace('/', '|');

        static void InScene(GameObject node, string parameter)
        {
            if (!node.scene.IsValid())
                throw new ArgumentException($"'{node.name}' is not in a scene (a prefab asset?): name its instance", parameter);
        }
    }
}
