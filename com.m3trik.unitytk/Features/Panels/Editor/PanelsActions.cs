using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// The Panels feature's actions: a placard made in the scene, its pages, and the scene's
    /// panels set up to take a pointer's and a controller's input. Each takes plain names, so a
    /// dry run plans parts it has not made, and an optional <see cref="OpContext"/>: a dry run
    /// plans, a menu's run is one undo step, and none just runs; a second run changes nothing.
    /// </summary>
    /// <remarks>
    /// Prior art: as <see cref="Placard"/>'s. A world-space panel's render mode is
    /// serialized-only in Unity 6.3 (<c>PanelSettings.renderMode</c> is internal), so the
    /// package ships its panel settings as an asset made in the editor
    /// (<see cref="WorldPanelPath"/>); the input set-up is the XR Interaction Toolkit's
    /// documented scene configuration for UI Toolkit, which a desktop pointer works under too.
    /// </remarks>
    public static class PanelsActions
    {
        /// <summary>A part named is missing (a placard a page goes on).</summary>
        public const string MissingCode = "unity.panels.missing";
        /// <summary>The folder of the package's panel assets.</summary>
        public const string Folder = "Packages/com.m3trik.unitytk/Features/Panels/Runtime/";
        /// <summary>The package's world-space panel settings: a thousand panel pixels to the metre, a trigger collider over each document, its theme <c>Panels.tss</c> (Unity's default under the panels' look).</summary>
        public const string WorldPanelPath = Folder + "WorldPanel.asset";
        /// <summary>The placard's layout.</summary>
        public const string PlacardLayoutPath = Folder + "Placard.uxml";
        /// <summary>The panels' look, every panel's classes (the package's theme holds it).</summary>
        public const string LookPath = Folder + "Panels.uss";
        /// <summary>The name of the object <see cref="AddShotFrom(PipShot, OpContext)"/> makes for the shot named
        /// <paramref name="shot"/>, under the part it shows: <c>PiP &lt;shot&gt;</c>.</summary>
        /// <param name="shot">The shot's name.</param>
        public static string SpotName(string shot) => $"PiP {shot}";
        // The spot an earlier build made under the shot itself, which the card carried about as it moved: it goes.
        const string CarriedSpotName = "From";
        /// <summary>The name of the object <see cref="SetUpInput"/> makes.</summary>
        public const string InputName = "Panel Input";

        /// <summary>
        /// Make a <see cref="Placard"/> named <paramref name="name"/> in the active scene's top
        /// level: a world-space document of the package's layout and settings; a placard of that
        /// name is reused.
        /// </summary>
        /// <param name="name">The placard's object's name.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The placard; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">The package's panel assets are missing.</exception>
        public static Placard MakePlacard(string name, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the placard", nameof(name));
            return MakeWindow<Placard>(name, "make a placard: a panel in the world telling the user what to do",
                                       Load<VisualTreeAsset>(PlacardLayoutPath), ctx, out _);
        }

        /// <summary>
        /// Make a <see cref="PipView"/> named <paramref name="name"/> in the active scene's top level,
        /// coming up as <paramref name="style"/> says (at <paramref name="anchor"/> for
        /// <see cref="PipStyle.Anchored"/>): a world-space document of the package's settings; a view
        /// of that name is reused, its style and anchor set as asked.
        /// </summary>
        /// <param name="name">The view's object's name.</param>
        /// <param name="style">Where it comes up.</param>
        /// <param name="anchor">Its authored screen, for the Anchored style; null: none.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The view; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">The package's panel assets are missing.</exception>
        public static PipView MakePip(string name, PipStyle style = PipStyle.Beside, Transform anchor = null, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the picture-in-picture", nameof(name));
            PipView view = MakeWindow<PipView>(name, "make a picture-in-picture: a card in the world showing a part close up", null, ctx, out bool made);
            if (view == null) return null;
            if (!made)
            {
                if (view.style == style && view.anchor == anchor) return view;
                if (ctx != null && !ctx.ShouldChange(view, $"come up {style}")) return view;
            }
            view.style = style;
            view.anchor = anchor;
            ctx.Edited(view);
            return view;
        }

        /// <summary>
        /// Make a <see cref="DebugPanel"/> named <paramref name="name"/> in the active scene's top level:
        /// a world-space document of the package's settings and theme, beside the user's eyes, where
        /// commands go (a sequence's Skip, Back, Restart, Autopilot; a scene's own). One of that name is reused.
        /// </summary>
        /// <param name="name">The panel's object's name.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The panel; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">The package's panel assets are missing.</exception>
        public static DebugPanel MakeDebugPanel(string name, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the debug panel", nameof(name));
            return MakeWindow<DebugPanel>(name, "make a debug panel: commands for trying the scene, beside the eyes", null, ctx, out _);
        }

        /// <summary>
        /// Take away the <see cref="DebugPanel"/> named <paramref name="name"/> in the active scene's top level, with
        /// its object and what is on it (a scene that ships leaves its debug tools out). None there: nothing.
        /// </summary>
        /// <param name="name">The panel's object's name.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it took one away (or would, in a dry run).</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        public static bool RemoveDebugPanel(string name, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name the debug panel", nameof(name));
            DebugPanel panel = SceneRoots.Find<DebugPanel>(name);
            if (panel == null) return false;
            if (ctx != null && !ctx.ShouldDelete(panel.gameObject, "take the debug panel away, with what is on it")) return true;
            ctx.Destroy(panel.gameObject);
            return true;
        }

        /// <summary>
        /// Give picture-in-picture <paramref name="pip"/> a shot named <paramref name="shot"/> (an object
        /// under it) showing <paramref name="target"/> with <paramref name="caption"/>, made switched off unless
        /// <paramref name="startEnabled"/>;
        /// a shot there is given them where it shows otherwise.
        /// </summary>
        /// <param name="pip">The view's object's name (<see cref="MakePip"/>).</param>
        /// <param name="shot">The shot's name (a step's ID).</param>
        /// <param name="target">The part it shows.</param>
        /// <param name="caption">What it shows, in words.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The shot; null when the view is missing (raised) or a dry run would make it.</returns>
        public static PipShot AddShot(string pip, string shot, Transform target, string caption, bool startEnabled = false, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(shot)) throw new ArgumentException("name the shot", nameof(shot));
            if (target == null) throw new ArgumentNullException(nameof(target), "name the part it shows");
            GameObject owner = SceneRoots.Find(pip);
            if ((owner == null || !owner.TryGetComponent(out PipView _)) && !ctx.Planned("create", WindowKey(pip, typeof(PipView))))
            {
                ctx.Raise(MissingCode, $"no picture-in-picture '{pip}' in the active scene: make it first (PanelsActions.MakePip)");
                return null;
            }
            Transform child = owner == null ? null : owner.transform.Find(shot);
            PipShot found = child == null ? null : child.GetComponent<PipShot>();
            if (found == null)
            {
                string key = OpContextEdits.Key(Under(pip, shot), typeof(PipShot));
                if (ctx.Planned("create", key)) return null;
                if (ctx != null && !ctx.ShouldCreate(key, $"show '{target.name}' close up")) return null;
                found = ctx.AddComponent<PipShot>(Child(owner, child, shot, ctx));
                found.enabled = startEnabled;
                found.target = target;
                found.caption = caption ?? "";
                ctx.Edited(found);
                return found;
            }
            if (found.target == target && found.caption == (caption ?? "")) return found;
            if (ctx != null && !ctx.ShouldChange(found, $"show '{target.name}' close up")) return found;
            found.target = target;
            found.caption = caption ?? "";
            ctx.Edited(found);
            return found;
        }

        /// <summary>
        /// The shot <see cref="AddShot"/> makes, as a dry run that only planned it hands it on: the object
        /// <paramref name="shot"/> under the view <paramref name="pip"/>, there now or only planned.
        /// </summary>
        /// <param name="pip">The view's object's name.</param>
        /// <param name="shot">The shot's name.</param>
        /// <exception cref="ArgumentException"><paramref name="pip"/> or <paramref name="shot"/> is empty.</exception>
        public static PlannedObject PlannedShot(string pip, string shot)
        {
            if (string.IsNullOrEmpty(pip)) throw new ArgumentException("name the picture-in-picture", nameof(pip));
            if (string.IsNullOrEmpty(shot)) throw new ArgumentException("name the shot", nameof(shot));
            return Under(pip, shot);
        }

        /// <summary>
        /// The page <see cref="AddPage"/> makes, as a dry run that only planned it hands it on: the object
        /// <paramref name="page"/> under the placard <paramref name="placard"/>, there now or only planned.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="page">The page's name.</param>
        /// <exception cref="ArgumentException"><paramref name="placard"/> or <paramref name="page"/> is empty.</exception>
        public static PlannedObject PlannedPage(string placard, string page) => Under(placard, page);

        /// <summary>
        /// The list <see cref="AddList"/> makes, as a dry run that only planned it hands it on: the object
        /// <paramref name="list"/> under the placard <paramref name="placard"/>, there now or only planned.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="list">The list's name.</param>
        /// <exception cref="ArgumentException"><paramref name="placard"/> or <paramref name="list"/> is empty.</exception>
        public static PlannedObject PlannedList(string placard, string list) => Under(placard, list);

        /// <summary>
        /// The tick <see cref="AddTick"/> makes, as a dry run that only planned it hands it on: the object
        /// <see cref="TickName"/> of <paramref name="item"/> under the list <paramref name="list"/> of the placard
        /// <paramref name="placard"/>, there now or only planned.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="list">The list's name.</param>
        /// <param name="item">The item it ticks.</param>
        /// <exception cref="ArgumentException"><paramref name="placard"/>, <paramref name="list"/> or <paramref name="item"/> is empty.</exception>
        public static PlannedObject PlannedTick(string placard, string list, string item)
        {
            if (string.IsNullOrEmpty(item)) throw new ArgumentException("name the item it ticks", nameof(item));
            return new PlannedObject(TickName(item), Under(placard, list));
        }

        /// <summary>
        /// Have <paramref name="shot"/> take its picture from a spot of its own (<see cref="PipShot.from"/>): a camera
        /// under the part it shows (<see cref="SpotName"/>), so it rides the part wherever a step has brought it
        /// (a door swung open carries what is mounted on it, and the spot looking at it), which a build places where
        /// the part is seen and a person moves; one there is reused. It is a camera so a person frames the picture
        /// looking through it (its preview, its frustum, its own field of view, the view's as it is made), as with one
        /// an artist delivered; switched off, it never draws to the screen. The picture's card is no place for it: the card
        /// moves beside the user, and a spot under it was carried about with it (one an earlier build made there goes).
        /// A spot it makes carries the build's mark (<see cref="PipSpot"/>), so a later build that gives the shot one of
        /// its own (<see cref="SetShotFrom(PipShot, Transform, OpContext)"/>) takes it away, and never a person's.
        /// Making the spot and pointing the shot at it are asked apart.
        /// </summary>
        /// <param name="shot">The shot, showing a part.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The spot's object; null in a dry run that would make it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="shot"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="shot"/> shows no part.</exception>
        public static GameObject AddShotFrom(PipShot shot, OpContext ctx = null) => AddShotFrom(shot, null, ctx);

        /// <summary>
        /// <see cref="AddShotFrom(PipShot, OpContext)"/>, its spot named <paramref name="spot"/> (a caller's own name for it:
        /// the helper's name every tool gives a picture's camera); null or empty: <see cref="SpotName"/>.
        /// </summary>
        /// <param name="shot">The shot, showing a part.</param>
        /// <param name="spot">The spot's name (null: <see cref="SpotName"/>).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The spot's object; null in a dry run that would make it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="shot"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="shot"/> shows no part.</exception>
        public static GameObject AddShotFrom(PipShot shot, string spot, OpContext ctx = null) => AddShotFrom(shot, spot, 0f, ctx);

        /// <summary>
        /// <see cref="AddShotFrom(PipShot, string, OpContext)"/>, a spot it makes framing with <paramref name="fieldOfView"/>
        /// (degrees; 0 or less: the view's, as it is made).
        /// </summary>
        /// <param name="shot">The shot, showing a part.</param>
        /// <param name="spot">The spot's name (null: <see cref="SpotName"/>).</param>
        /// <param name="fieldOfView">The made camera's field of view in degrees (0: the picture view's).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The spot's object; null in a dry run that would make it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="shot"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="shot"/> shows no part.</exception>
        public static GameObject AddShotFrom(PipShot shot, string spot, float fieldOfView, OpContext ctx = null)
        {
            if (shot == null) throw new ArgumentNullException(nameof(shot), "name the shot");
            if (shot.target == null) throw new ArgumentException("the shot shows no part: name it first (PanelsActions.AddShot)", nameof(shot));
            string name = string.IsNullOrEmpty(spot) ? SpotName(shot.name) : spot;
            Transform carried = shot.transform.Find(CarriedSpotName);
            if (carried != null && (ctx == null || ctx.ShouldDelete(carried.gameObject, CarriedAction))) ctx.Destroy(carried.gameObject);
            Transform found = shot.target.Find(name);
            if (found == null)
            {
                string key = new PlannedObject(name, shot.target).Path;
                if (ctx.Planned("create", key)) return null;
                if (ctx != null)
                {
                    // Each asked apart: a dry run (false) plans both, as the live run makes both.
                    bool create = ctx.ShouldCreate(key, FromAction);
                    bool look = ctx.ShouldChange(shot, LookAction);
                    if (!create) return null;
                    found = Spot(shot, name, fieldOfView, ctx);
                    if (!look) return found.gameObject;
                }
                else
                {
                    found = Spot(shot, name, fieldOfView, null);
                }
            }
            else
            {
                // One an earlier build made as a plain object becomes a camera, where it stands.
                if (!found.TryGetComponent(out Camera _) && (ctx == null || ctx.ShouldChange(found.gameObject, LensMadeAction)))
                    Lens(found.gameObject, shot, fieldOfView, ctx);
                if (shot.from == found || ctx != null && !ctx.ShouldChange(shot, LookAction)) return found.gameObject;
            }
            shot.from = found;
            ctx.Edited(shot);
            return found.gameObject;
        }

        /// <summary>
        /// <see cref="AddShotFrom(PipShot, OpContext)"/> on <paramref name="shot"/>, a shot a dry run only
        /// planned (<see cref="PlannedShot"/>): the spot under <paramref name="target"/> and the shot's pointing at it,
        /// planned once; nothing in a live run.
        /// </summary>
        /// <param name="shot">The planned shot.</param>
        /// <param name="target">The part it shows.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>The spot's object, as planned, for the actions that place it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="shot"/> or <paramref name="target"/> is null.</exception>
        public static PlannedObject AddShotFrom(PlannedObject shot, Transform target, OpContext ctx = null) => AddShotFrom(shot, target, null, ctx);

        /// <summary><see cref="AddShotFrom(PlannedObject, Transform, OpContext)"/>, its spot named <paramref name="spot"/> (null: <see cref="SpotName"/>).</summary>
        /// <param name="shot">The planned shot.</param>
        /// <param name="target">The part it shows.</param>
        /// <param name="spot">The spot's name (null: <see cref="SpotName"/>).</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>The spot's object, as planned, for the actions that place it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="shot"/> or <paramref name="target"/> is null.</exception>
        public static PlannedObject AddShotFrom(PlannedObject shot, Transform target, string spot, OpContext ctx = null)
        {
            if (shot == null) throw new ArgumentNullException(nameof(shot), "name the shot");
            if (target == null) throw new ArgumentNullException(nameof(target), "name the part it shows");
            var from = new PlannedObject(string.IsNullOrEmpty(spot) ? SpotName(shot.Name) : spot, target);
            if (ctx.PlanCreate(from.Path, FromAction)) ctx.ShouldChange(shot.Path, LookAction);
            return from;
        }

        /// <summary>
        /// Have <paramref name="shot"/> take its picture from <paramref name="from"/>, an object someone made for it (a
        /// camera an artist set up, a locator), used exactly as it stands: where it is, the way it looks and, where it
        /// is a camera, its own field of view. A camera there is switched off, since it frames the picture and never
        /// draws to the screen itself; the spot a build made for the shot (<see cref="AddShotFrom(PipShot, OpContext)"/>,
        /// marked <see cref="PipSpot"/>, or under its default name) goes, once; an object a person or a delivery brought
        /// never does. A shot looking from there already gets nothing.
        /// </summary>
        /// <param name="shot">The shot.</param>
        /// <param name="from">The object its picture is taken from.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="shot"/> or <paramref name="from"/> is null.</exception>
        public static bool SetShotFrom(PipShot shot, Transform from, OpContext ctx = null)
        {
            if (shot == null) throw new ArgumentNullException(nameof(shot), "name the shot (one a dry run only planned: pass its PlannedObject)");
            if (from == null) throw new ArgumentNullException(nameof(from), "name the object its picture is taken from");
            bool changed = false;
            // The spots a build made for the shot go, each once: the one it took its picture from where a build made it
            // (marked: PipSpot), and those under a build's own names; never an object a delivery or a person brought.
            var own = new List<Transform>();
            foreach (Transform spot in new[]
                     {
                         shot.transform.Find(CarriedSpotName), shot.target == null ? null : shot.target.Find(SpotName(shot.name)),
                         shot.from != null && shot.from.TryGetComponent(out PipSpot _) ? shot.from : null,
                     })
                if (spot != null && spot != from && !own.Contains(spot)) own.Add(spot);
            foreach (Transform spot in own)
            {
                changed = true;
                if (ctx == null || ctx.ShouldDelete(spot.gameObject, OwnAction)) ctx.Destroy(spot.gameObject);
            }
            if (from.TryGetComponent(out Camera lens) && lens.enabled)
            {
                changed = true;
                if (ctx == null || ctx.ShouldChange(lens, LensAction))
                {
                    lens.enabled = false;
                    ctx.Edited(lens);
                }
            }
            if (shot.from == from) return changed;
            if (ctx != null && !ctx.ShouldChange(shot, $"take its picture from '{from.name}'")) return true;
            shot.from = from;
            ctx.Edited(shot);
            return true;
        }

        /// <summary>
        /// <see cref="SetShotFrom(PipShot, Transform, OpContext)"/> on <paramref name="shot"/>, a shot a dry run only
        /// planned (<see cref="PlannedShot"/>): its looking from <paramref name="from"/> and the camera there switched
        /// off, planned once; nothing in a live run.
        /// </summary>
        /// <param name="shot">The planned shot.</param>
        /// <param name="from">The object its picture is taken from.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned a change now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="shot"/> or <paramref name="from"/> is null.</exception>
        public static bool SetShotFrom(PlannedObject shot, Transform from, OpContext ctx = null)
        {
            if (shot == null) throw new ArgumentNullException(nameof(shot), "name the shot");
            if (from == null) throw new ArgumentNullException(nameof(from), "name the object its picture is taken from");
            bool lensOn = from.TryGetComponent(out Camera lens) && lens.enabled;
            if (lensOn && ctx != null) ctx.ShouldChange(lens, LensAction);  // a dry run's: listed, as its live run asks
            return ctx.PlanChange(shot.Path, $"take its picture from '{from.name}'") || lensOn;
        }

        // A spot made for shot under the part it shows, named name: a build's (PipSpot: a later build may take it away),
        // and a camera framing with fieldOfView (Lens).
        static Transform Spot(PipShot shot, string name, float fieldOfView, OpContext ctx)
        {
            GameObject spot = Child(shot.target.gameObject, null, name, ctx);
            ctx.AddComponent<PipSpot>(spot);
            Lens(spot, shot, fieldOfView, ctx);
            return spot.transform;
        }

        // The camera a picture's own spot is: framing with fieldOfView (0 or less: the view's) as it is made, never drawing
        // to the screen.
        static void Lens(GameObject spot, PipShot shot, float fieldOfView, OpContext ctx)
        {
            Camera lens = ctx.AddComponent<Camera>(spot);
            PipView view = shot.GetComponentInParent<PipView>(true);
            float angle = fieldOfView > 0f ? fieldOfView : view != null ? view.fieldOfView : PipView.DefaultFieldOfView;
            lens.fieldOfView = Mathf.Clamp(angle, 1f, 179f);
            lens.nearClipPlane = SpotNear;
            lens.farClipPlane = SpotFar;
            lens.enabled = false;
            ctx.Edited(lens);
        }

        /// <summary>
        /// Have <paramref name="lens"/>, a picture's camera, frame with <paramref name="fieldOfView"/> degrees (1 to 179):
        /// the picture's view takes it from the camera it is taken from (<see cref="PipView"/>). One framing so already
        /// gets nothing.
        /// </summary>
        /// <param name="lens">The camera.</param>
        /// <param name="fieldOfView">Its field of view in degrees.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="lens"/> is null.</exception>
        public static bool SetFieldOfView(Camera lens, float fieldOfView, OpContext ctx = null)
        {
            if (lens == null) throw new ArgumentNullException(nameof(lens), "name the picture's camera");
            float angle = Mathf.Clamp(fieldOfView, 1f, 179f);
            if (Mathf.Approximately(lens.fieldOfView, angle)) return false;
            if (ctx != null && !ctx.ShouldChange(lens, $"frame with a {angle.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} degree field of view"))
                return true;
            lens.fieldOfView = angle;
            ctx.Edited(lens);
            return true;
        }

        // A spot camera's own planes, in metres: what its preview shows and how long its frustum is drawn (the picture's
        // own are set as it is framed).
        const float SpotNear = 0.01f, SpotFar = 5f;

        /// <summary>
        /// Have picture <paramref name="view"/> leave out <paramref name="objects"/> (<see cref="PipView.hidden"/>): drawn in
        /// the scene, never in the picture, as the cues pointing at what it shows. A view that leaves out just those gets
        /// nothing.
        /// </summary>
        /// <param name="view">The picture's view.</param>
        /// <param name="objects">What it leaves out; their order is kept as given.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="view"/> or <paramref name="objects"/> is null.</exception>
        public static bool SetHidden(PipView view, IEnumerable<GameObject> objects, OpContext ctx = null)
        {
            if (view == null) throw new ArgumentNullException(nameof(view), "name the picture's view");
            if (objects == null) throw new ArgumentNullException(nameof(objects), "name what it leaves out (none: an empty list)");
            var wanted = new List<GameObject>();
            foreach (GameObject each in objects)
                if (each != null && !wanted.Contains(each)) wanted.Add(each);
            if (wanted.Count == view.hidden.Count && wanted.TrueForAll(view.hidden.Contains)) return false;
            if (ctx != null && !ctx.ShouldChange(view, HiddenAction)) return true;
            view.hidden = wanted;
            ctx.Edited(view);
            return true;
        }

        /// <summary>
        /// <see cref="SetHidden(PipView, IEnumerable{GameObject}, OpContext)"/> on a view a dry run only planned
        /// (<see cref="MakePip"/>'s): what it will leave out, planned once; nothing in a live run.
        /// </summary>
        /// <param name="view">The planned view.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the change now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
        public static bool SetHidden(PlannedObject view, OpContext ctx = null)
        {
            if (view == null) throw new ArgumentNullException(nameof(view), "name the planned view");
            return ctx.PlanChange(view.Path, HiddenAction);
        }

        /// <summary>The picture view named <paramref name="name"/> as a dry run planned it (<see cref="MakePip"/>); null where none is planned.</summary>
        /// <param name="name">The view's object's name.</param>
        /// <param name="ctx">The dry run.</param>
        public static PlannedObject PlannedPip(string name, OpContext ctx)
            => !string.IsNullOrEmpty(name) && ctx.Planned("create", WindowKey(name, typeof(PipView))) ? new PlannedObject(name) : null;

        const string HiddenAction = "leave the cues out of its picture";
        const string FromAction = "the spot its picture is taken from";
        const string LensMadeAction = "make it a camera, to frame the picture through";
        const string LookAction = "take its picture from there";
        const string CarriedAction = "the spot an earlier build made under the shot, which its card carried about";
        const string OwnAction = "the spot a build made for its picture: its row names its own";
        const string LensAction = "never draw to the screen: it frames a picture";

        /// <summary>
        /// Give placard <paramref name="placard"/> a page named <paramref name="page"/> (an object
        /// under it) showing <paramref name="heading"/>, <paramref name="text"/> and
        /// <paramref name="choices"/>, made switched off unless <paramref name="startEnabled"/>; a page there is given them where it
        /// shows otherwise.
        /// </summary>
        /// <param name="placard">The placard's object's name (<see cref="MakePlacard"/>).</param>
        /// <param name="page">The page's name (a step's ID).</param>
        /// <param name="heading">Its heading.</param>
        /// <param name="text">Its text.</param>
        /// <param name="choices">Its choices, in order; null: none.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The page; null when the placard is missing (raised) or a dry run would make it.</returns>
        public static PlacardPage AddPage(string placard, string page, string heading, string text, IList<string> choices,
                                          bool startEnabled = false, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(page)) throw new ArgumentException("name the page", nameof(page));
            if (!Owner(placard, ctx, out GameObject owner)) return null;
            Transform child = owner == null ? null : owner.transform.Find(page);
            PlacardPage found = child == null ? null : child.GetComponent<PlacardPage>();
            var wanted = new List<string>();
            if (choices != null)
                foreach (string choice in choices)
                    if (!string.IsNullOrEmpty(choice)) wanted.Add(choice);
            if (found == null)
            {
                string key = OpContextEdits.Key(Under(placard, page), typeof(PlacardPage));
                if (ctx.Planned("create", key)) return null;
                if (ctx != null && !ctx.ShouldCreate(key, $"show '{heading}' on the placard")) return null;
                found = ctx.AddComponent<PlacardPage>(Child(owner, child, page, ctx));
                found.enabled = startEnabled;
                Fill(found, heading, text, wanted);
                ctx.Edited(found);
                return found;
            }
            if (found.heading == (heading ?? "") && found.text == (text ?? "") && Same(found.choices, wanted)) return found;
            if (ctx != null && !ctx.ShouldChange(found, $"show '{heading}' on the placard")) return found;
            Fill(found, heading, text, wanted);
            ctx.Edited(found);
            return found;
        }

        /// <summary>
        /// Give page <paramref name="page"/> of placard <paramref name="placard"/> a key pad of
        /// <paramref name="keys"/> (one a character), its Enter choosing what was keyed in; empty: none.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="page">The page's name (<see cref="AddPage"/>), which a dry run may only have planned.</param>
        /// <param name="keys">The keys (<c>0123456789</c>); null or empty: no key pad.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetKeys(string placard, string page, string keys, OpContext ctx = null)
            => EditPage(placard, page, ctx, p => p.keys == (keys ?? ""), p => p.keys = keys ?? "",
                        string.IsNullOrEmpty(keys) ? "show no key pad" : $"show a key pad of {keys}");

        /// <summary>
        /// Have page <paramref name="page"/> of placard <paramref name="placard"/> keep the placard
        /// from covering <paramref name="target"/> (what its step asks the user to act on).
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="page">The page's name, which a dry run may only have planned.</param>
        /// <param name="target">What the user acts on, a scene object.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool KeepClear(string placard, string page, Transform target, OpContext ctx = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target), "name what the placard keeps clear of");
            return EditPage(placard, page, ctx, p => p.keepClear.Contains(target), p => p.keepClear.Add(target),
                            $"keep clear of '{target.name}'");
        }

        /// <summary>
        /// Have page <paramref name="page"/> of placard <paramref name="placard"/> bring the placard up
        /// at <paramref name="spot"/> from the eyes (as <see cref="Placard.offset"/> is) instead of the
        /// placard's own offset: a spot clear of the scene from where its step stands the user.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="page">The page's name, which a dry run may only have planned.</param>
        /// <param name="spot">Right, up and ahead of the eyes, in metres, turned with their heading.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetSpot(string placard, string page, Vector3 spot, OpContext ctx = null)
            => EditPage(placard, page, ctx, p => p.ownSpot && (p.spot - spot).sqrMagnitude < 1e-8f,
                        p =>
                        {
                            p.ownSpot = true;
                            p.spot = spot;
                        },
                        $"come up at {spot:F2} from the eyes");

        /// <summary>
        /// Have page <paramref name="page"/> of placard <paramref name="placard"/> say
        /// <paramref name="text"/> once <paramref name="choice"/> is chosen or keyed in and the page
        /// stays (a wrong answer's "Try again"); a reply it has to that choice is reworded.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="page">The page's name, which a dry run may only have planned.</param>
        /// <param name="choice">The choice's label, or a value keyed in.</param>
        /// <param name="text">What it says then.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool AddReply(string placard, string page, string choice, string text, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(choice)) throw new ArgumentException("name the choice it replies to", nameof(choice));
            return EditPage(placard, page, ctx,
                            p => p.replies.Exists(r => r != null && r.choice == choice && r.text == (text ?? "")),
                            p =>
                            {
                                PlacardReply reply = p.replies.Find(r => r != null && r.choice == choice);
                                if (reply == null) p.replies.Add(reply = new PlacardReply { choice = choice });
                                reply.text = text ?? "";
                            },
                            $"say '{text}' once '{choice}' is chosen");
        }

        /// <summary>
        /// Have page <paramref name="page"/> of placard <paramref name="placard"/> hold its choice
        /// <paramref name="choice"/> back until it is revealed (<see cref="PlacardPage.Reveal"/>,
        /// <see cref="RevealOn"/>): a Next shown once its step's actions are done. One held back
        /// already gets nothing.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="page">The page's name, which a dry run may only have planned.</param>
        /// <param name="choice">The choice's label.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool HoldBack(string placard, string page, string choice, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(choice)) throw new ArgumentException("name the choice to hold back", nameof(choice));
            return EditPage(placard, page, ctx, p => p.waiting.Contains(choice), p => p.waiting.Add(choice),
                            $"show '{choice}' once it is revealed");
        }

        /// <summary>
        /// Wire <paramref name="when"/> (an event with no argument: a step's completion) to reveal
        /// the choices page <paramref name="page"/> of placard <paramref name="placard"/> holds back
        /// (<see cref="HoldBack"/>); a wiring that is there gets nothing.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="page">The page's name, which a dry run may only have planned.</param>
        /// <param name="node">The scene object holding the event (its component's object).</param>
        /// <param name="owner">The component holding the event; null only in a dry run where it is planned.</param>
        /// <param name="when">The event; null as <paramref name="owner"/> may be.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool RevealOn(string placard, string page, GameObject node, Object owner, UnityEngine.Events.UnityEvent when, OpContext ctx = null)
            => Reveal(placard, page, Asked(node), owner, when, ctx);

        /// <summary>
        /// <see cref="RevealOn(string, string, GameObject, Object, UnityEngine.Events.UnityEvent, OpContext)"/> on an event of
        /// <paramref name="node"/>, an object a dry run only planned (a step the same build makes): the wiring its live run
        /// makes, planned once; nothing in a live run.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="page">The page's name, which a dry run may only have planned.</param>
        /// <param name="node">The planned object holding the event.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the wiring now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool RevealOn(string placard, string page, PlannedObject node, OpContext ctx = null)
        {
            string asked = Asked(node);
            return ctx != null && ctx.DryRun && Reveal(placard, page, asked, null, null, ctx);
        }

        // RevealOn, the object holding the event named by its path (what a plan calls it, made or only planned).
        static bool Reveal(string placard, string page, string node, Object owner, UnityEngine.Events.UnityEvent when, OpContext ctx)
        {
            if (!Owner(placard, ctx, out GameObject placed)) return false;
            Transform child = placed == null ? null : placed.transform.Find(page);
            PlacardPage found = child == null ? null : child.GetComponent<PlacardPage>();
            string what = $"show page '{page}''s held-back choices";
            if ((found == null || owner == null || when == null) && (ctx == null || !ctx.DryRun))
            {
                ctx.Raise(MissingCode, $"no page '{page}' on placard '{placard}', or no event to reveal it on: make them first");
                return false;
            }
            if (found != null && owner != null && PersistentCalls.Wired(owner, when, found, nameof(PlacardPage.Reveal), ctx: ctx)) return false;
            object asked = owner != null ? owner : (object)node;
            if (ctx.Planned("change", asked, what)) return false;
            if (ctx != null && !ctx.ShouldChange(asked, what)) return true;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(when, found.Reveal);
            ctx.Edited(owner);
            return true;
        }

        /// <summary>
        /// Wire <paramref name="when"/> (an event of any argument: a part refusing a hand that lacks its
        /// tool) to have placard <paramref name="placard"/> say <paramref name="text"/> in place of its text
        /// while what it shows stays (<see cref="Placard.Tell"/>); a wiring of the placard's to that event is
        /// left as it is, so a line a person edited stays theirs.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="node">The scene object holding the event (its component's object).</param>
        /// <param name="owner">The component holding the event; null only in a dry run where it is planned.</param>
        /// <param name="when">The event; null as <paramref name="owner"/> may be.</param>
        /// <param name="text">What the placard says.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
        public static bool SayOn(string placard, GameObject node, Object owner, UnityEngine.Events.UnityEventBase when, string text, OpContext ctx = null)
            => Say(placard, Asked(node), owner, when, text, ctx);

        /// <summary>
        /// <see cref="SayOn(string, GameObject, Object, UnityEngine.Events.UnityEventBase, string, OpContext)"/> on an event
        /// of <paramref name="node"/>, an object a dry run only planned: the wiring its live run makes, planned once;
        /// nothing in a live run.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="node">The planned object holding the event.</param>
        /// <param name="text">What the placard says.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the wiring now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
        public static bool SayOn(string placard, PlannedObject node, string text, OpContext ctx = null)
        {
            string asked = Asked(node);
            return ctx != null && ctx.DryRun && Say(placard, asked, null, null, text, ctx);
        }

        // SayOn, the object holding the event named by its path (what a plan calls it, made or only planned).
        static bool Say(string placard, string node, Object owner, UnityEngine.Events.UnityEventBase when, string text, OpContext ctx)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("name what the placard says", nameof(text));
            if (!Owner(placard, ctx, out GameObject placed)) return false;
            Placard found = placed == null ? null : placed.GetComponent<Placard>();
            string what = $"have placard '{placard}' say \"{text}\" when it fires";
            if ((found == null || owner == null || when == null) && (ctx == null || !ctx.DryRun))
            {
                ctx.Raise(MissingCode, $"no placard '{placard}', or no event on '{node}' to say it on: make them first");
                return false;
            }
            if (found != null && owner != null && PersistentCalls.Wired(owner, when, found, nameof(Placard.Tell), ctx: ctx)) return false;
            object asked = owner != null ? owner : (object)node;
            if (ctx.Planned("change", asked, what)) return false;
            if (ctx != null && !ctx.ShouldChange(asked, what)) return true;
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(when, found.Tell, text);
            ctx.Edited(owner);
            return true;
        }

        /// <summary>
        /// Give placard <paramref name="placard"/> a list named <paramref name="list"/> (an object
        /// under it: a <see cref="PlacardList"/>) of <paramref name="items"/>, made switched off unless
        /// <paramref name="startEnabled"/>; a
        /// list there is given them where it lists otherwise.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="list">The list's name.</param>
        /// <param name="items">Its items, in order.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The list; null when the placard is missing (raised) or a dry run would make it.</returns>
        public static PlacardList AddList(string placard, string list, IList<string> items, bool startEnabled = false, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(list)) throw new ArgumentException("name the list", nameof(list));
            if (!Owner(placard, ctx, out GameObject owner)) return null;
            var wanted = new List<string>();
            if (items != null)
                foreach (string item in items)
                    if (!string.IsNullOrEmpty(item)) wanted.Add(item);
            Transform child = owner == null ? null : owner.transform.Find(list);
            PlacardList found = child == null ? null : child.GetComponent<PlacardList>();
            if (found == null)
            {
                string key = OpContextEdits.Key(Under(placard, list), typeof(PlacardList));
                if (ctx.Planned("create", key)) return null;
                if (ctx != null && !ctx.ShouldCreate(key, $"list {string.Join(", ", wanted)} on the placard")) return null;
                found = ctx.AddComponent<PlacardList>(Child(owner, child, list, ctx));
                found.enabled = startEnabled;
                found.items = wanted;
                ctx.Edited(found);
                return found;
            }
            if (Same(found.items, wanted)) return found;
            if (ctx != null && !ctx.ShouldChange(found, $"list {string.Join(", ", wanted)} on the placard")) return found;
            found.items = wanted;
            ctx.Edited(found);
            return found;
        }

        /// <summary>
        /// Give list <paramref name="list"/> of placard <paramref name="placard"/> a
        /// <see cref="PlacardTick"/> ticking <paramref name="item"/> as it is switched on (an object
        /// under the list, named <see cref="TickName"/>), made switched off unless <paramref name="startEnabled"/>; one
        /// there gets nothing.
        /// </summary>
        /// <param name="placard">The placard's object's name.</param>
        /// <param name="list">The list's name (<see cref="AddList"/>), which a dry run may only have planned.</param>
        /// <param name="item">The item it ticks.</param>
        /// <param name="startEnabled">Make it switched on (it shows from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The tick; null when the list is missing (raised) or a dry run would make it.</returns>
        public static PlacardTick AddTick(string placard, string list, string item, bool startEnabled = false, OpContext ctx = null)
        {
            if (string.IsNullOrEmpty(item)) throw new ArgumentException("name the item it ticks", nameof(item));
            if (!Owner(placard, ctx, out GameObject owner)) return null;
            Transform listNode = owner == null ? null : owner.transform.Find(list);
            PlacardList found = listNode == null ? null : listNode.GetComponent<PlacardList>();
            if (found == null && !ctx.Planned("create", OpContextEdits.Key(Under(placard, list), typeof(PlacardList))))
            {
                ctx.Raise(MissingCode, $"no list '{list}' on placard '{placard}': make it first (PanelsActions.AddList)");
                return null;
            }
            Transform child = listNode == null ? null : listNode.Find(TickName(item));
            PlacardTick tick = child == null ? null : child.GetComponent<PlacardTick>();
            if (tick != null) return tick;
            string key = OpContextEdits.Key(PlannedTick(placard, list, item), typeof(PlacardTick));
            if (ctx.Planned("create", key)) return null;
            if (ctx != null && !ctx.ShouldCreate(key, $"tick '{item}' when switched on")) return null;
            tick = ctx.AddComponent<PlacardTick>(Child(listNode.gameObject, child, TickName(item), ctx));
            tick.enabled = startEnabled;
            tick.list = found;
            tick.item = item;
            ctx.Edited(tick);
            return tick;
        }

        /// <summary>The name of the object <see cref="AddTick"/> puts the tick of <paramref name="item"/> on.</summary>
        /// <param name="item">The item it ticks.</param>
        public static string TickName(string item) => $"Tick ({item})";

        /// <summary>
        /// Set the scene's panels up to take input: a <see cref="PanelInputConfiguration"/> that
        /// processes input on world-space panels itself (a pointer through the main camera) and
        /// redirects none of it through the EventSystem, as the XR Interaction Toolkit asks for a
        /// controller to press them; one in the scene is set so.
        /// </summary>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The configuration; null in a dry run that would make one.</returns>
        public static PanelInputConfiguration SetUpInput(OpContext ctx = null)
        {
            const string what = "take input on world-space panels, redirecting none through the EventSystem";
            PanelInputConfiguration config = Object.FindAnyObjectByType<PanelInputConfiguration>(FindObjectsInactive.Include);
            if (config == null)
            {
                string key = WindowKey(InputName, typeof(PanelInputConfiguration));
                if (ctx.Planned("create", key)) return null;
                if (ctx != null && !ctx.ShouldCreate(key, what)) return null;
                var node = new GameObject(InputName);
                ctx.Created(node);
                config = ctx.AddComponent<PanelInputConfiguration>(node);
            }
            else if (Configured(config) || (ctx != null && !ctx.ShouldChange(config, what)))
            {
                return config;
            }
            config.processWorldSpaceInput = true;
            config.panelInputRedirection = PanelInputConfiguration.PanelInputRedirection.Never;
            ctx.Edited(config);
            return config;
        }

        // -- helpers ---------------------------------------------------------------

        // The top-level window named name, its T made where it lacks one (asked; made: whether it was made now); null in a
        // dry run that would make it. Its document is put in the package's world space (PanelDefaults.WorldSpace), with
        // layout where given; a document there before is asked about first, so undo gives its own settings back.
        static T MakeWindow<T>(string name, string what, VisualTreeAsset layout, OpContext ctx, out bool made) where T : Component
        {
            made = false;
            GameObject node = SceneRoots.Find(name);
            if (node != null && node.TryGetComponent(out T found)) return found;
            string key = WindowKey(name, typeof(T));
            if (ctx.Planned("create", key)) return null;
            UIDocument document = node != null ? node.GetComponent<UIDocument>() : null;
            if (ctx != null)
            {
                // Each asked apart: a dry run (false) plans both, as the live run makes both.
                bool create = ctx.ShouldCreate(key, what);
                bool refit = document == null || ctx.ShouldChange(document, WorldSpaceAction);
                if (!create || !refit) return null;
            }
            if (node == null)
            {
                node = new GameObject(name);
                ctx.Created(node);
            }
            if (document == null) document = ctx.AddComponent<UIDocument>(node);
            PanelDefaults.WorldSpace(document);
            if (layout != null) document.visualTreeAsset = layout;
            ctx.Edited(document);
            made = true;
            return ctx.AddComponent<T>(node);
        }

        const string WorldSpaceAction = "show its document in the world as the package's windows show theirs";

        // What a plan calls part of the top-level object name.
        static string WindowKey(string name, Type part) => OpContextEdits.Key(new PlannedObject(name), part);

        // The object child under the top-level object owner, as a plan names it there or only planned.
        static PlannedObject Under(string owner, string child) => new PlannedObject(child, new PlannedObject(owner));

        // What a plan calls the object holding an event: its path, made or only planned.
        static string Asked(GameObject node)
            => node != null ? OpContextEdits.Label(node) : throw new ArgumentNullException(nameof(node), "name the object holding the event");

        static string Asked(PlannedObject node)
            => node != null ? node.Path : throw new ArgumentNullException(nameof(node), "name the object holding the event");

        // The placard named, or a dry run's planned one (null then); false, raised, when neither.
        static bool Owner(string placard, OpContext ctx, out GameObject owner)
        {
            owner = SceneRoots.Find(placard);
            if (owner != null && owner.TryGetComponent(out Placard _)) return true;
            owner = null;
            if (ctx.Planned("create", WindowKey(placard, typeof(Placard)))) return true;
            ctx.Raise(MissingCode, $"no placard '{placard}' in the active scene: make it first (PanelsActions.MakePlacard)");
            return false;
        }

        static GameObject Child(GameObject owner, Transform child, string name, OpContext ctx)
        {
            if (child != null) return child.gameObject;
            var node = new GameObject(name);
            node.transform.SetParent(owner.transform, false);
            ctx.Created(node);
            return node.gameObject;
        }

        // Change page of placard where has() says it lacks the change; a dry run's planned page counts as lacking it.
        static bool EditPage(string placard, string page, OpContext ctx, Func<PlacardPage, bool> has, Action<PlacardPage> make, string what)
        {
            if (!Owner(placard, ctx, out GameObject owner)) return false;
            Transform child = owner == null ? null : owner.transform.Find(page);
            PlacardPage found = child == null ? null : child.GetComponent<PlacardPage>();
            PlannedObject planned = Under(placard, page);
            if (found == null && !ctx.Planned("create", OpContextEdits.Key(planned, typeof(PlacardPage))))
            {
                ctx.Raise(MissingCode, $"no page '{page}' on placard '{placard}': make it first (PanelsActions.AddPage)");
                return false;
            }
            if (found != null && has(found)) return false;
            object asked = found != null ? (object)found : planned.Path;
            if (ctx.Planned("change", asked, what)) return false;
            if (ctx != null && !ctx.ShouldChange(asked, what)) return true;
            make(found);
            ctx.Edited(found);
            return true;
        }

        static bool Configured(PanelInputConfiguration config)
            => config.processWorldSpaceInput && config.panelInputRedirection == PanelInputConfiguration.PanelInputRedirection.Never;

        static void Fill(PlacardPage page, string heading, string text, List<string> choices)
        {
            page.heading = heading ?? "";
            page.text = text ?? "";
            page.choices = choices;
        }

        static bool Same(List<string> a, List<string> b)
        {
            if (a == null || a.Count != b.Count) return a == null && b.Count == 0;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        internal static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"the package's {path} is missing: reinstall unitytk");
            return asset;
        }
    }
}
