using System;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Indicators
{
    /// <summary>
    /// The Indicators feature's actions: an <see cref="Indicator"/> put over a scene object, a
    /// <see cref="Marker"/> at its foot, an indicator's look set, and one taken away.
    /// Each takes an optional <see cref="OpContext"/>: a dry run plans, a menu's run is one
    /// undo step, and none just runs; a second run changes nothing. An object a dry run only
    /// planned is handed over as a <see cref="PlannedObject"/>, so the dry run plans under the
    /// keys its live run asks with.
    /// </summary>
    /// <remarks>Prior art: as <see cref="Indicator"/>'s.</remarks>
    public static class IndicatorsActions
    {
        /// <summary>The name of the child an indicator sits on.</summary>
        public const string ChildName = "Indicator";
        /// <summary>The name of the child a marker sits on.</summary>
        public const string MarkerName = "Marker";

        const string MarkAction = "mark where it stands with a ring on the floor";
        const string PointAction = "point an arrow at it";
        const string RemoveAction = "take away an indicator nothing points with";

        /// <summary>
        /// Whether this dry run plans an arrow made or taken away (<see cref="AddIndicator(GameObject, string, bool, bool, OpContext)"/>,
        /// <see cref="RemoveIndicator"/>): what follows from the set of arrows in a scene (a picture that leaves them out) is
        /// planned by it, since the dry run's scene still holds the arrows as they were. False in a live run.
        /// </summary>
        /// <param name="ctx">The run's context.</param>
        public static bool PlansArrows(OpContext ctx)
        {
            if (ctx == null || !ctx.DryRun) return false;
            foreach (Core.Change change in ctx.Changes)
                if (change.action == PointAction || change.action == RemoveAction) return true;
            return false;
        }

        /// <summary>
        /// Put a <see cref="Marker"/> at <paramref name="target"/>'s foot, on a child of its own,
        /// disabled unless <paramref name="startEnabled"/>: it shows while something enables it (a step,
        /// from the one that turns it on). A target that has one gets nothing (its settings stay).
        /// </summary>
        /// <param name="target">Where it marks, a scene object (a viewpoint, a spot to stand).</param>
        /// <param name="ownMesh">Show the target's own mesh (a destination disc) in place of a ring, hiding it while the
        /// marker is off (<see cref="Marker.ownMesh"/>); false: a ring at its foot, its mesh left as it is.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The marker; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> is not in a scene.</exception>
        public static Marker AddMarker(GameObject target, bool ownMesh = false, bool startEnabled = false, OpContext ctx = null)
        {
            InScene(target, nameof(target), "name where it marks");
            foreach (Transform child in target.transform)
                if (child.name == MarkerName && child.TryGetComponent(out Marker existing)) return existing;
            string key = OpContextEdits.Key(target, typeof(Marker));
            if (ctx.Planned("create", key)) return null;
            if (ctx != null && !ctx.ShouldCreate(key, MarkAction)) return null;
            var node = new GameObject(MarkerName);
            node.transform.SetParent(target.transform, false);
            ctx.Created(node);
            Marker marker = ctx.AddComponent<Marker>(node);
            marker.ownMesh = ownMesh;
            marker.enabled = startEnabled;
            ctx.Edited(marker);
            return marker;
        }

        /// <summary>
        /// <see cref="AddMarker(GameObject, bool, bool, OpContext)"/> on <paramref name="target"/>, an object a dry
        /// run only planned: the marker its live run makes, planned once; nothing in a live run.
        /// </summary>
        /// <param name="target">The planned object it marks.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to make it on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        public static Marker AddMarker(PlannedObject target, OpContext ctx = null)
        {
            ctx.PlanCreate(OpContextEdits.Key(target, typeof(Marker)), MarkAction);
            return null;
        }

        /// <summary>
        /// Put an <see cref="Indicator"/> over <paramref name="target"/>, on a child of its own
        /// (<see cref="ChildName"/>), following the target's top, disabled unless
        /// <paramref name="startEnabled"/>: it shows while something enables it (a step, as it runs). A
        /// target that has one gets nothing.
        /// </summary>
        /// <param name="target">What it points at, a scene object.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The indicator; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> is not in a scene.</exception>
        public static Indicator AddIndicator(GameObject target, bool startEnabled = false, OpContext ctx = null)
            => AddIndicator(target, ChildName, follow: true, startEnabled, ctx);

        /// <summary>
        /// <see cref="AddIndicator(GameObject, bool, OpContext)"/> on a child named <paramref name="name"/>:
        /// one target may get several (one per step that points at it from where the user then
        /// stands), each placed where it points from (its child's pose: its arrow made tip-down at
        /// its origin) unless it follows the target's top (<paramref name="follow"/>). A child so named
        /// that has one gets nothing.
        /// </summary>
        /// <param name="target">What it points at, a scene object.</param>
        /// <param name="name">Its child's name.</param>
        /// <param name="follow">Move its arrow over the target's top (<see cref="Indicator.follow"/>); false: it stays where
        /// its child is placed, riding the target.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The indicator; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> is not in a scene, or <paramref name="name"/> is empty.</exception>
        public static Indicator AddIndicator(GameObject target, string name, bool follow = false, bool startEnabled = false, OpContext ctx = null)
        {
            InScene(target, nameof(target), "name what it points at");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name its child", nameof(name));
            Indicator existing = Find(target, name);
            if (existing != null) return existing;
            string key = ChildKey(target, name);
            if (ctx.Planned("create", key)) return null;
            if (ctx != null && !ctx.ShouldCreate(key, PointAction)) return null;
            var node = new GameObject(name);
            node.transform.SetParent(target.transform, false);
            ctx.Created(node);
            Indicator indicator = ctx.AddComponent<Indicator>(node);
            indicator.enabled = startEnabled;
            indicator.follow = follow;
            ctx.Edited(indicator);
            return indicator;
        }

        const string Hide = "hide it until something shows it";

        /// <summary>
        /// <see cref="AddIndicator(GameObject, bool, OpContext)"/> showing <paramref name="authored"/>, an arrow
        /// the delivery placed: shown where it stands, bobbing in place, and hidden until something shows
        /// it. A target that has an indicator gets nothing.
        /// </summary>
        /// <param name="target">What it points at, a scene object.</param>
        /// <param name="authored">The delivery's arrow for it, a scene object.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The indicator; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="authored"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> is not in a scene.</exception>
        public static Indicator AddIndicator(GameObject target, Transform authored, bool startEnabled = false, OpContext ctx = null)
        {
            if (authored == null) throw new ArgumentNullException(nameof(authored), "name the delivery's arrow");
            if (target != null)
            {
                Indicator existing = Find(target, ChildName);
                if (existing != null) return existing;
            }
            Indicator indicator = AddIndicator(target, ChildName, follow: false, startEnabled, ctx);
            if (indicator == null)
            {
                ctx.PlanChange(authored.gameObject, Hide);  // planned with the indicator, once
                return null;
            }
            if (ctx != null && !ctx.ShouldChange(authored.gameObject, Hide)) return indicator;
            authored.gameObject.SetActive(false);
            ctx.Edited(authored.gameObject);
            indicator.arrow = authored;  // the indicator is this run's: its undo takes the fields with it
            ctx.Edited(indicator);
            return indicator;
        }

        /// <summary>
        /// Put an <see cref="Indicator"/> at <paramref name="place"/>, an object that says where the arrow is (a helper a
        /// delivery or a person placed, drawing nothing itself): on a child of it (<see cref="ChildName"/>), the arrow it
        /// makes resting its tip on the place and pointing along the place's forward, going wherever the place goes;
        /// pointing at <paramref name="target"/>. A place that has one gets nothing: the steps that name it share its arrow.
        /// </summary>
        /// <param name="target">What it points at, a scene object.</param>
        /// <param name="place">Where the arrow is, a scene object.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The indicator; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="place"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> or <paramref name="place"/> is not in a scene.</exception>
        public static Indicator AddIndicatorAt(GameObject target, Transform place, bool startEnabled = false, OpContext ctx = null)
        {
            InScene(target, nameof(target), "name what it points at");
            if (place == null) throw new ArgumentNullException(nameof(place), "name where the arrow is");
            Indicator existing = Find(place.gameObject, ChildName);
            if (existing != null) return existing;
            Indicator indicator = AddIndicator(place.gameObject, ChildName, follow: false, startEnabled, ctx);
            if (indicator == null) return null;
            // The arrow it makes points down its own Y: its object is turned so that is the place's forward.
            indicator.transform.localRotation = Quaternion.FromToRotation(Vector3.down, Vector3.forward);
            indicator.target = target.transform;
            ctx.Edited(indicator);
            return indicator;
        }

        /// <summary>
        /// Give <paramref name="indicator"/> the look of the arrow it makes: its <paramref name="color"/> and its
        /// <paramref name="size"/> (<see cref="Indicator.color"/>, <see cref="Indicator.size"/>). One with that look
        /// gets nothing.
        /// </summary>
        /// <param name="indicator">An indicator.</param>
        /// <param name="color">Its arrow's colour.</param>
        /// <param name="size">Its arrow's length, in metres (at least 0.01).</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="indicator"/> is null.</exception>
        public static bool SetStyle(Indicator indicator, Color color, float size, OpContext ctx = null)
        {
            if (indicator == null) throw new ArgumentNullException(nameof(indicator), "name the indicator (a dry run that only planned it: pass its target and name)");
            size = Mathf.Max(0.01f, size);
            if (indicator.color == color && Mathf.Approximately(indicator.size, size)) return false;
            if (ctx != null && !ctx.ShouldChange(indicator, StyleAction)) return true;
            indicator.color = color;
            indicator.size = size;
            ctx.Edited(indicator);
            return true;
        }

        /// <summary>
        /// <see cref="SetStyle(Indicator, Color, float, OpContext)"/> on the indicator on <paramref name="target"/>'s child
        /// <paramref name="name"/>; where a dry run only planned it, the change its live run makes on a new one
        /// (one whose look differs from <see cref="Indicator.DefaultColor"/>, <see cref="Indicator.DefaultSize"/>), planned once.
        /// </summary>
        /// <param name="target">What it points at, a scene object.</param>
        /// <param name="name">Its child's name (<see cref="ChildName"/>, or the one <see cref="AddIndicator(GameObject, string, bool, bool, OpContext)"/> gave it).</param>
        /// <param name="color">Its arrow's colour.</param>
        /// <param name="size">Its arrow's length, in metres (at least 0.01).</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run); false where there is no indicator, nor one planned.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> is not in a scene.</exception>
        public static bool SetStyle(GameObject target, string name, Color color, float size, OpContext ctx = null)
        {
            InScene(target, nameof(target), "name what it points at");
            Indicator indicator = Find(target, name);
            if (indicator != null) return SetStyle(indicator, color, size, ctx);
            string key = ChildKey(target, name);
            if (ctx == null || !ctx.DryRun || !ctx.Planned("create", key)) return false;
            if (Indicator.DefaultColor == color && Mathf.Approximately(Indicator.DefaultSize, Mathf.Max(0.01f, size))) return false;
            return ctx.PlanChange(key, StyleAction);  // the live run's ask names the new indicator by its object's path
        }

        const string StyleAction = "give its arrow its colour and size";

        /// <summary>
        /// Take away the indicator on <paramref name="target"/>'s child <paramref name="name"/>, with its child (one a
        /// build made that nothing points with any more). None there: nothing.
        /// </summary>
        /// <param name="target">What it points at, a scene object.</param>
        /// <param name="name">Its child's name.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it took one away (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> is not in a scene.</exception>
        public static bool RemoveIndicator(GameObject target, string name, OpContext ctx = null)
        {
            InScene(target, nameof(target), "name what it points at");
            Indicator indicator = Find(target, name);
            if (indicator == null) return false;
            if (ctx.Planned("delete", indicator.gameObject, RemoveAction)) return false;
            if (ctx != null && !ctx.ShouldDelete(indicator.gameObject, RemoveAction)) return true;
            ctx.Destroy(indicator.gameObject);
            return true;
        }

        // The indicator on target's child named name; null where it has none.
        static Indicator Find(GameObject target, string name)
        {
            foreach (Transform child in target.transform)
                if (child.name == name && child.TryGetComponent(out Indicator found)) return found;
            return null;
        }

        // What a plan calls the indicator made on target's child named name: the child's path.
        static string ChildKey(GameObject target, string name) => new PlannedObject(name, target.transform).Path;

        static void InScene(GameObject node, string parameter, string what)
        {
            if (node == null) throw new ArgumentNullException(parameter, what);
            if (!node.scene.IsValid()) throw new ArgumentException($"'{node.name}' is not in a scene (a prefab asset?): name its instance", parameter);
        }
    }
}
