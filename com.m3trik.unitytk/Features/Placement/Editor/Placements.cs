using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Placement
{
    /// <summary>
    /// The Placement feature's actions: what a person's menu item and an agent's code
    /// do to a project. Each takes an optional <see cref="OpContext"/>: a dry run
    /// plans, a menu's run is one undo step, and none just runs.
    /// A build places a spot where <see cref="Placer"/> finds it (<see cref="Place"/>); a person
    /// or an agent moves it where it works better; <see cref="Capture"/> takes every such
    /// adjustment into a <see cref="Tuning"/>, which the build's caller keeps with its other
    /// adjustments (a manifest keeps the Tasks kit's: <see cref="Write"/>, <see cref="TryRead"/>),
    /// and the next build puts the spot there instead.
    /// </summary>
    /// <remarks>
    /// Prior art: a prefab override keeps an edit to one asset's instance, and a scene keeps any
    /// edit until it is built anew; a build that makes the scene from data each time keeps an
    /// adjustment only as data it reads, so the adjustment is data here, keyed by what the build
    /// names the spot, and kept where the build's other adjustments are: one store, never a file of
    /// its own. A kept spot is written in the package's own JSON (<c>Core.Json</c>): no package.
    /// </remarks>
    public static class Placements
    {
        /// <summary>
        /// The delivery moved a helper a person adjusted here (its maker's pose changed since): the adjustment still wins,
        /// and this says so on the row that holds it, whose Revert takes the delivery's pose.
        /// </summary>
        public const string MovedCode = "unity.placement.moved";

        /// <summary>The adjustments made to placed spots, by key: what a build's caller keeps (<see cref="Capture"/>).</summary>
        [Serializable]
        public sealed class Tuning
        {
            /// <summary>The adjusted spots, sorted by key.</summary>
            public List<Tuned> spots = new List<Tuned>();

            /// <summary>The adjustment kept under <paramref name="key"/>.</summary>
            /// <param name="key">A spot's key.</param>
            /// <param name="tuned">Its adjustment.</param>
            /// <returns>Whether one is kept.</returns>
            public bool TryGet(string key, out Tuned tuned)
            {
                tuned = spots.Find(t => t.key == key);
                return tuned != null;
            }
        }

        /// <summary>One adjusted spot: where it stands relative to the part it is placed by.</summary>
        [Serializable]
        public sealed class Tuned
        {
            /// <summary>The spot's key.</summary>
            public string key = "";

            /// <summary>The part it is placed by, for a person reading it (the key says which spot).</summary>
            public string relativeTo = "";

            /// <summary>Its offset in the part's axes, in metres.</summary>
            public Vector3 position;

            /// <summary>Its turn relative to the part's.</summary>
            public Quaternion rotation = Quaternion.identity;
        }

        /// <summary>
        /// Place <paramref name="node"/> at <paramref name="pose"/> as the spot named
        /// <paramref name="key"/>, placed by <paramref name="relativeTo"/>: a <see cref="PlacedSpot"/>
        /// remembers where the build put it, and where <paramref name="tuning"/> keeps an adjustment
        /// under the key, it goes there instead. A spot placed before under the key, by the same part, keeps
        /// that first placement, whatever <paramref name="pose"/> says now (it follows its part): the search for
        /// a first spot runs once, and only <see cref="PlaceAgain"/> has the next build place it anew. Adding the
        /// spot and moving the node are asked apart (a dry run plans both, one undo takes both back); a spot
        /// already so is left as it is.
        /// </summary>
        /// <param name="node">What is placed (a viewpoint, a camera's spot, an arrow).</param>
        /// <param name="key">Its name from build to build.</param>
        /// <param name="relativeTo">The part it is placed by (null: the scene's origin).</param>
        /// <param name="pose">Where the build puts it, in the world.</param>
        /// <param name="tuning">The adjustments kept (null: none).</param>
        /// <param name="ctx">The run's context.</param>
        /// <param name="basis">What the search was asked, in the plan's terms (<see cref="PlacedSpot.basis"/>): a spot placed by
        /// another basis is placed anew (null: any basis keeps it).</param>
        /// <returns>The spot; null in a dry run that plans it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        public static PlacedSpot Place(GameObject node, string key, Transform relativeTo, Pose pose, Tuning tuning = null, OpContext ctx = null,
                                       string basis = null)
            => Place(node, key, relativeTo, pose, tuning, ctx, false, basis);

        /// <summary>
        /// Mark <paramref name="node"/>, a helper someone made (a viewpoint, an arrow or a camera an artist set up, which
        /// the delivery brought), as a placed spot kept under <paramref name="key"/>: placed where its maker has it (its
        /// pose in the model it is an instance of; for one that is no instance of a model, where it stood when first
        /// marked), and never moved by a build to get it there. So one a person moves in the scene is an adjustment like
        /// one moved from where a build placed it: kept by <see cref="Capture"/>, put
        /// where the person had it in a scene built anew, and put back where its maker has it by
        /// <see cref="Restore"/>. One a person had moved before it was ever marked stays where they put it.
        /// </summary>
        /// <param name="node">The helper.</param>
        /// <param name="key">Its name from build to build.</param>
        /// <param name="tuning">The adjustments kept (null: none).</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>The spot; null in a dry run that plans it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        public static PlacedSpot PlaceGiven(GameObject node, string key, Tuning tuning = null, OpContext ctx = null)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name the helper");
            Keyed(key);
            Transform t = node.transform, parent = t.parent;
            Transform source = PrefabUtility.GetCorrespondingObjectFromSource(t);
            PlacedSpot spot = node.GetComponent<PlacedSpot>();
            Pose made = source != null
                ? (parent != null ? new Pose(parent.TransformPoint(source.localPosition), parent.rotation * source.localRotation)
                                  : new Pose(source.localPosition, source.localRotation))
                : spot != null && spot.key == key ? PlacedSpot.ToWorld(spot.relativeTo, new Pose(spot.placedPosition, spot.placedRotation))
                : new Pose(t.position, t.rotation);
            return Place(node, key, parent, made, tuning, ctx, true, null);
        }

        // Place; given: node is a helper someone made, which is left where it stands as it is first marked.
        static PlacedSpot Place(GameObject node, string key, Transform relativeTo, Pose pose, Tuning tuning, OpContext ctx, bool given, string basis)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name what is placed");
            Keyed(key);
            PlacedSpot spot = node.GetComponent<PlacedSpot>();
            // Placed before under this key by this part: its first placement holds, following the part (place once).
            if (!given && spot != null && spot.key == key && spot.relativeTo == relativeTo && (basis == null || spot.basis == basis))
                pose = PlacedSpot.ToWorld(relativeTo, new Pose(spot.placedPosition, spot.placedRotation));
            Pose placed = PlacedSpot.ToRelative(relativeTo, pose);
            Transform t = node.transform;
            Pose at = tuning != null && tuning.TryGet(key, out Tuned tuned)
                ? PlacedSpot.ToWorld(relativeTo, new Pose(tuned.position, tuned.rotation))
                : given && spot == null ? new Pose(t.position, t.rotation)
                : pose;
            bool there = Near(t.position, at.position) && Near(t.rotation, at.rotation);
            if (spot == null)
            {
                string made = OpContextEdits.Key(node, typeof(PlacedSpot));
                if (ctx.Planned("create", made)) return null;
                bool move = !there;
                if (ctx != null)
                {
                    // Each asked apart: a dry run (false) plans both, as the live run makes both.
                    bool create = ctx.ShouldCreate(made, PlaceAction(key));
                    move = move && ctx.ShouldChange(t, MoveAction);
                    if (!create) return null;
                }
                spot = ctx.AddComponent<PlacedSpot>(node);
                Fill(spot, key, relativeTo, placed, basis);
                ctx.Edited(spot);
                if (!move) return spot;
                t.SetPositionAndRotation(at.position, at.rotation);
                ctx.Edited(t);
                return spot;
            }
            bool kept = spot.key == key && spot.relativeTo == relativeTo && (basis == null || spot.basis == basis)
                        && Near(spot.placedPosition, placed.position) && Near(spot.placedRotation, placed.rotation);
            if (kept && there) return spot;
            // A dry run that placed it already this run (a viewpoint placed before the rows, then by its own row): its
            // live run finds it placed the second time, so nothing more is planned.
            if (ctx.Planned("change", spot, PlaceAction(key)) || ctx.Planned("change", t, MoveAction)) return spot;
            if (ctx != null)
            {
                // Each asked apart, and only where it changes: a dry run (false) plans both, as the live run makes both.
                bool replace = kept || ctx.ShouldChange(spot, PlaceAction(key));
                bool move = there || ctx.ShouldChange(t, MoveAction);
                if (!replace || !move) return spot;
            }
            if (!kept)
            {
                // A helper its maker moved since a person adjusted it here: theirs still wins, said, so the newer pose is
                // a Revert away, never lost without a word.
                if (given && spot.key == key && spot.relativeTo == relativeTo && tuning != null && tuning.TryGet(key, out _))
                    ctx.Raise(MovedCode, $"the delivery moved '{node.name}' since it was adjusted here: the adjustment still wins " +
                                         "(Revert it to take the delivery's)");
                Fill(spot, key, relativeTo, placed, basis);
                ctx.Edited(spot);
            }
            if (there) return spot;
            t.SetPositionAndRotation(at.position, at.rotation);
            ctx.Edited(t);
            return spot;
        }

        /// <summary>
        /// <see cref="Place(GameObject, string, Transform, Pose, Tuning, OpContext)"/> on <paramref name="node"/>, an
        /// object a dry run only planned: the spot its live run adds and the move it makes, planned once, under the
        /// keys the live run asks with; nothing in a live run.
        /// </summary>
        /// <param name="node">What is placed, only planned.</param>
        /// <param name="key">Its name from build to build.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to place.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        public static PlacedSpot Place(PlannedObject node, string key, OpContext ctx = null) => Place(node, key, true, ctx);

        /// <summary>
        /// <see cref="Place(PlannedObject, string, OpContext)"/>, where the caller knows whether the live run moves the
        /// object it makes (<paramref name="moves"/>; false: it is made where it belongs, so only its spot is planned).
        /// </summary>
        /// <param name="node">What is placed, only planned.</param>
        /// <param name="key">Its name from build to build.</param>
        /// <param name="moves">Whether the live run moves it from where it is made.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to place.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
        public static PlacedSpot Place(PlannedObject node, string key, bool moves, OpContext ctx = null)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name what is placed");
            Keyed(key);
            if (ctx.PlanCreate(OpContextEdits.Key(node, typeof(PlacedSpot)), PlaceAction(key)) && moves) ctx.ShouldChange(node.Path, MoveAction);
            return null;
        }

        /// <summary>
        /// Whether this dry run planned to move <paramref name="node"/> to its place (<see cref="Place(GameObject, string, Transform, Pose, Tuning, OpContext)"/>):
        /// what follows from where it stands (a rig stood at a viewpoint) is planned by that. False in a live run, which has moved it.
        /// </summary>
        /// <param name="node">What is placed (null: nothing).</param>
        /// <param name="ctx">The run's context.</param>
        public static bool Moving(GameObject node, OpContext ctx) => node != null && ctx.Planned("change", node.transform, MoveAction);

        /// <summary>Whether an object at <paramref name="at"/> stands where <paramref name="pose"/> is, as a placement tells them apart.</summary>
        /// <param name="at">An object.</param>
        /// <param name="pose">A pose in the world.</param>
        /// <exception cref="ArgumentNullException"><paramref name="at"/> is null.</exception>
        public static bool There(Transform at, Pose pose)
        {
            if (at == null) throw new ArgumentNullException(nameof(at), "name an object");
            return Near(at.position, pose.position) && Near(at.rotation, pose.rotation);
        }

        const string MoveAction = "move it there";

        static string PlaceAction(string key) => $"place it as '{key}'";

        static void Keyed(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("name the spot: its key keeps its adjustment", nameof(key));
        }

        static void Fill(PlacedSpot spot, string key, Transform relativeTo, Pose placed, string basis)
        {
            if (basis != null) spot.basis = basis;
            spot.key = key;
            spot.relativeTo = relativeTo;
            spot.placedPosition = placed.position;
            spot.placedRotation = placed.rotation;
        }

        /// <summary>
        /// The spot <paramref name="node"/> was placed as under <paramref name="key"/> by <paramref name="relativeTo"/>
        /// (<see cref="Place(GameObject, string, Transform, Pose, Tuning, OpContext)"/>): what a build keeps as first
        /// placed, searching no more; null where it holds none so.
        /// </summary>
        /// <param name="node">What is placed (null: nothing).</param>
        /// <param name="key">Its name from build to build.</param>
        /// <param name="relativeTo">The part it is placed by.</param>
        /// <param name="basis">What the search was asked (<see cref="PlacedSpot.basis"/>; null: any).</param>
        public static PlacedSpot PlacedAs(GameObject node, string key, Transform relativeTo, string basis = null)
        {
            if (node == null || !node.TryGetComponent(out PlacedSpot spot)) return null;
            return spot.key == key && spot.relativeTo == relativeTo && (basis == null || spot.basis == basis) ? spot : null;
        }

        /// <summary>
        /// Have <paramref name="spot"/> placed anew: its first placement dropped (the <see cref="PlacedSpot"/> taken off its
        /// node), so the next build places the node where the search then finds it. Asked before it is taken off.
        /// </summary>
        /// <param name="spot">A placed spot.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it took it off (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="spot"/> is null.</exception>
        public static bool PlaceAgain(PlacedSpot spot, OpContext ctx = null)
        {
            if (spot == null) throw new ArgumentNullException(nameof(spot), "name a placed spot");
            if (ctx != null && !ctx.ShouldDelete(spot, AgainAction)) return true;
            ctx.Destroy(spot);
            return true;
        }

        const string AgainAction = "place it again: the next build searches anew for where it stands";

        /// <summary>
        /// Put <paramref name="spot"/> back where its build placed it: its adjustment gone, and kept by no later
        /// <see cref="Capture"/>.
        /// </summary>
        /// <param name="spot">A placed spot.</param>
        /// <param name="ctx">The run's context: a dry run lists the move; null just makes it.</param>
        /// <returns>Whether it stood elsewhere (and was moved back, or would be in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="spot"/> is null.</exception>
        public static bool Restore(PlacedSpot spot, OpContext ctx = null)
        {
            if (spot == null) throw new ArgumentNullException(nameof(spot), "name a placed spot");
            if (!spot.Adjusted) return false;
            Transform t = spot.transform;
            if (ctx != null && !ctx.ShouldChange(t, "move it back where the build placed it")) return true;
            Pose placed = PlacedSpot.ToWorld(spot.relativeTo, new Pose(spot.placedPosition, spot.placedRotation));
            t.SetPositionAndRotation(placed.position, placed.rotation);
            ctx.Edited(t);
            return true;
        }

        /// <summary>
        /// Take the adjustments made to the placed spots under <paramref name="roots"/> into <paramref name="tuning"/>:
        /// each spot standing elsewhere than the build put it is kept where it stands, one back where the build put it
        /// drops its adjustment, and adjustments to spots not among them stay (another build's, in a scene two builds
        /// share). One kept already and standing where it is kept keeps its own numbers (a scene built anew puts it there
        /// to within a float's noise, which is no new adjustment).
        /// </summary>
        /// <param name="tuning">The adjustments kept, updated in place.</param>
        /// <param name="roots">The objects whose spots are kept (what one build made); null: every spot in the open scenes.</param>
        /// <returns>Whether it changed <paramref name="tuning"/>: what the caller stores again.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
        public static bool Capture(Tuning tuning, IEnumerable<GameObject> roots)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning), "name the adjustments kept");
            var before = new List<string>();
            foreach (Tuned tuned in tuning.spots) before.Add(tuned.key + "=" + Write(tuned));
            foreach (PlacedSpot spot in Spots(roots))
            {
                if (string.IsNullOrEmpty(spot.key)) continue;
                Pose now = spot.Relative;
                Tuned was = tuning.spots.Find(t => t.key == spot.key);
                if (was != null && spot.Adjusted && was.relativeTo == (spot.relativeTo == null ? "" : spot.relativeTo.name)
                    && (was.position - now.position).sqrMagnitude < 1e-8f && PlacedSpot.Same(was.rotation, now.rotation)) continue;
                tuning.spots.RemoveAll(t => t.key == spot.key);
                if (!spot.Adjusted) continue;
                tuning.spots.Add(new Tuned
                {
                    key = spot.key,
                    relativeTo = spot.relativeTo == null ? "" : spot.relativeTo.name,
                    position = now.position,
                    rotation = now.rotation,
                });
            }
            tuning.spots.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            var after = new List<string>();
            foreach (Tuned tuned in tuning.spots) after.Add(tuned.key + "=" + Write(tuned));
            return !before.SequenceEqual(after);
        }

        /// <summary>
        /// <paramref name="tuned"/> as text (the package's JSON: where it stands relative to what, floats as short as they
        /// read), the form a store keeps it in under its key; <see cref="TryRead"/> reads it back.
        /// </summary>
        /// <param name="tuned">A kept spot.</param>
        /// <exception cref="ArgumentNullException"><paramref name="tuned"/> is null.</exception>
        public static string Write(Tuned tuned)
        {
            if (tuned == null) throw new ArgumentNullException(nameof(tuned), "name the kept spot");
            return Json.Write(new Dictionary<string, object>
            {
                ["relativeTo"] = tuned.relativeTo ?? "",
                ["position"] = new List<object> { Number(tuned.position.x), Number(tuned.position.y), Number(tuned.position.z) },
                ["rotation"] = new List<object>
                {
                    Number(tuned.rotation.x), Number(tuned.rotation.y), Number(tuned.rotation.z), Number(tuned.rotation.w),
                },
            });
        }

        /// <summary>The spot kept under <paramref name="key"/> as <paramref name="text"/> (<see cref="Write"/>).</summary>
        /// <param name="key">Its key.</param>
        /// <param name="text">What a store kept.</param>
        /// <param name="tuned">The kept spot; null where the text is none.</param>
        /// <returns>Whether the text reads as a kept spot.</returns>
        public static bool TryRead(string key, string text, out Tuned tuned)
        {
            tuned = null;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return false;
            object tree;
            try
            {
                tree = Json.Parse(text, false);
            }
            catch (FormatException)
            {
                return false;
            }
            if (!(tree is IDictionary<string, object> map) || !(Field(map, "position") is IList<object> p) || p.Count != 3
                || !(Field(map, "rotation") is IList<object> r) || r.Count != 4) return false;
            tuned = new Tuned
            {
                key = key,
                relativeTo = Field(map, "relativeTo") as string ?? "",
                position = new Vector3(Float(p[0]), Float(p[1]), Float(p[2])),
                rotation = new Quaternion(Float(r[0]), Float(r[1]), Float(r[2]), Float(r[3])),
            };
            return true;
        }

        /// <summary>
        /// Hide the meshes of <paramref name="anchor"/>, an authoring anchor: a placeholder a delivery carries to mark
        /// where something goes (a screen, a place to stand), a place rather than a thing to see. Each of its renderers
        /// that shows is switched off, asked; the node stays, a place to put things at.
        /// </summary>
        /// <param name="anchor">The anchor, a scene object.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>How many renderers it switched off (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="anchor"/> is null.</exception>
        public static int HideAnchor(GameObject anchor, OpContext ctx = null)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor), "name the authoring anchor");
            int hidden = 0;
            foreach (Renderer shown in anchor.GetComponents<Renderer>())
            {
                if (!shown.enabled) continue;
                hidden++;
                if (ctx != null && !ctx.ShouldChange(shown, $"hide the authoring anchor '{anchor.name}': a place, not a thing to see")) continue;
                shown.enabled = false;
                ctx.Edited(shown);
            }
            return hidden;
        }

        // The spots under roots (each once), or every one in the open scenes where roots is null.
        static IEnumerable<PlacedSpot> Spots(IEnumerable<GameObject> roots)
        {
            if (roots == null) return Object.FindObjectsByType<PlacedSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var found = new List<PlacedSpot>();
            var seen = new HashSet<PlacedSpot>();
            foreach (GameObject root in roots)
                if (root != null)
                    foreach (PlacedSpot spot in root.GetComponentsInChildren<PlacedSpot>(true))
                        if (seen.Add(spot)) found.Add(spot);
            return found;
        }

        // A float as the double its shortest text reads as: 0.1f written 0.1, not 0.10000000149011612.
        static double Number(float value)
            => double.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);

        static float Float(object value) => value is double d ? (float)d : 0f;

        static object Field(IDictionary<string, object> map, string name) => map != null && map.TryGetValue(name, out object value) ? value : null;

        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;

        // By their dot, not Quaternion.Angle: that reads ~0.04 degrees of float noise between equal turns.
        static bool Near(Quaternion a, Quaternion b) => PlacedSpot.Same(a, b);
    }
}
