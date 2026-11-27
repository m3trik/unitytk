using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// The Interaction feature's actions: a hand, a holdable, a target, a clip player and its
    /// clips put on a scene's objects, and a target's selection wired to what it does (a clip
    /// played through a trigger its step enables, a holdable taken). Each takes an optional <see cref="OpContext"/>: a dry run
    /// plans, a menu's run is one undo step, and none just runs; a second run changes
    /// nothing. Each takes the scene's objects, so a run of several plans in a dry run what
    /// it would make before it is made.
    /// </summary>
    /// <remarks>
    /// Prior art: the XR Interaction Toolkit's interactables (grab, socket, simple) bind
    /// selection to XR input and physics, and uGUI's Button to a canvas; these components
    /// hold no input at all (a pointer, a ray or code calls <see cref="Target.Select"/>), so
    /// one scene serves a desktop and a headset, each through its own door. A selection's
    /// effect is a UnityEvent's persistent call, Unity's own wiring, which the inspector
    /// shows and edits.
    /// </remarks>
    public static class Interactions
    {
        /// <summary>A target requires something no holdable is (nor is planned to be).</summary>
        public const string NotHoldableCode = "unity.interaction.not_holdable";
        /// <summary>A wiring names a target or a clip player that is not there.</summary>
        public const string MissingPartCode = "unity.interaction.missing_part";

        // The input doors registered as the editor loads, in name order (a domain reload
        // empties it and each door registers again).
        static readonly List<ITargetInput> _inputs = new List<ITargetInput>();

        /// <summary>The input doors in this editor, each wiring every target made, in name order.</summary>
        internal static IReadOnlyList<ITargetInput> Inputs => _inputs;

        /// <summary>Register an input door (its editor half, as the editor loads); one of the same name is replaced.</summary>
        internal static void Register(ITargetInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            _inputs.RemoveAll(each => each.Name == input.Name);
            _inputs.Add(input);
            _inputs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }

        /// <summary>
        /// Wire targets for <paramref name="inputs"/> alone until the scope is disposed, the
        /// registered doors restored after: a test of the feature's own changes passes none.
        /// </summary>
        internal static IDisposable OnlyInputs(params ITargetInput[] inputs)
        {
            var registered = new List<ITargetInput>(_inputs);
            _inputs.Clear();
            foreach (ITargetInput input in inputs) Register(input);
            return new InputsRestored(registered);
        }

        sealed class InputsRestored : IDisposable
        {
            readonly List<ITargetInput> _registered;

            public InputsRestored(List<ITargetInput> registered) => _registered = registered;

            public void Dispose()
            {
                _inputs.Clear();
                _inputs.AddRange(_registered);
            }
        }

        /// <summary>The name of the hand <see cref="EquipUser(Camera, Vector3, OpContext)"/> makes under the user's eyes.</summary>
        public const string HandName = "HAND";

        /// <summary>Where <see cref="EquipUser(Camera, Vector3, OpContext)"/> puts a hand it makes, from the eyes: low and to the right of the view, at arm's length.</summary>
        public static readonly Vector3 HandInView = new Vector3(0.2f, -0.18f, 0.45f);

        /// <summary>How near eyes equipped to hold things see, in metres (<see cref="EquipUser(Camera, Vector3, float, OpContext)"/>): a tool held in the hand shows whole.</summary>
        public const float HeldNearClip = 0.05f;

        /// <summary>
        /// Let the user who sees through <paramref name="eyes"/> act in the scene: a
        /// <see cref="Hand"/> in their view (<see cref="HandName"/>, a child of the eyes at
        /// <paramref name="handAt"/>) where the scene has none, and each input the project has
        /// set up for them (an input door's: a pointer's clicks into the scene and its look).
        /// What is there already gets nothing, so a rig that brings its own hands and
        /// interactors keeps them.
        /// </summary>
        /// <param name="eyes">The camera the user sees through, in a scene.</param>
        /// <param name="handAt">Where a hand it makes sits, from the eyes (<see cref="HandInView"/>).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The scene's hand; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="eyes"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="eyes"/> is not in a scene.</exception>
        public static Hand EquipUser(Camera eyes, Vector3 handAt, OpContext ctx = null) => EquipUser(eyes, handAt, 0f, ctx);

        /// <summary>
        /// <see cref="EquipUser(Camera, Vector3, OpContext)"/>, the eyes' near clip plane brought in to
        /// <paramref name="nearClip"/> where it is farther, so what the hand holds shows whole.
        /// </summary>
        /// <param name="eyes">The camera the user sees through, in a scene.</param>
        /// <param name="handAt">Where a hand it makes sits, from the eyes (<see cref="HandInView"/>).</param>
        /// <param name="nearClip">How near the eyes see, at most, in metres (<see cref="HeldNearClip"/>); 0 or less: left as it is.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The scene's hand; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="eyes"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="eyes"/> is not in a scene.</exception>
        public static Hand EquipUser(Camera eyes, Vector3 handAt, float nearClip, OpContext ctx = null)
        {
            if (eyes == null) throw new ArgumentNullException(nameof(eyes), "name the camera the user sees through");
            InScene(eyes.gameObject);
            Hand hand = SceneHand(eyes.gameObject.scene);
            string key = new PlannedObject(HandName, eyes.transform).Path;
            if (hand == null && !ctx.Planned("create", key) && (ctx == null || ctx.ShouldCreate(key, HandAction)))
            {
                var node = new GameObject(HandName);
                node.transform.SetParent(eyes.transform, false);
                node.transform.localPosition = handAt;
                ctx.Created(node);
                hand = ctx.AddComponent<Hand>(node);
            }
            foreach (ITargetInput input in _inputs) input.Equip(eyes, ctx);
            if (nearClip > 0f && eyes.nearClipPlane > nearClip && (ctx == null || ctx.ShouldChange(eyes, NearClipAction)))
            {
                eyes.nearClipPlane = nearClip;
                ctx.Edited(eyes);
            }
            return hand;
        }

        /// <summary>
        /// <see cref="EquipUser(Camera, Vector3, float, OpContext)"/> for <paramref name="eyes"/>, a camera a dry run only
        /// planned (made new, as a scene with no camera gets one): what its live run asks, in its order, planned once each; a
        /// hand's place is set as it is made, so nothing more is planned. Nothing in a live run.
        /// </summary>
        /// <param name="eyes">The planned camera the user sees through.</param>
        /// <param name="nearClip">How near the eyes see, at most, in metres (<see cref="HeldNearClip"/>); 0 or less: left as made.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>The scene's hand where it has one; null where the live run makes one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="eyes"/> is null.</exception>
        public static Hand EquipUser(PlannedObject eyes, float nearClip, OpContext ctx = null)
        {
            if (eyes == null) throw new ArgumentNullException(nameof(eyes), "name the planned camera the user sees through");
            Hand hand = SceneHand(eyes.Scene);
            if (hand == null) ctx.PlanCreate(new PlannedObject(HandName, eyes).Path, HandAction);
            foreach (ITargetInput input in _inputs) input.Equip(eyes, ctx);
            if (nearClip > 0f && NewCameraNearClip > nearClip) ctx.PlanChange(eyes.Path, NearClipAction);
            return hand;
        }

        /// <summary>How near a camera made new sees, in metres: Unity's default near clip plane, a planned camera's.</summary>
        internal const float NewCameraNearClip = 0.3f;

        const string HandAction = "give the user a hand in view, to hold what they take";
        const string NearClipAction = "see what the hand holds: its near clip plane brought in";

        // The first hand in scene, inactive ones too; null where it has none.
        static Hand SceneHand(Scene scene)
        {
            foreach (Hand each in UnityEngine.Object.FindObjectsByType<Hand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (each.gameObject.scene == scene) return each;
            return null;
        }

        /// <summary>Put a <see cref="Hand"/> on <paramref name="node"/>; one that has one gets nothing.</summary>
        /// <param name="node">A scene object: the hand's anchor (a controller, a point in the camera's view).</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The hand; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static Hand AddHand(GameObject node, OpContext ctx = null)
        {
            InScene(node);
            Hand existing = node.GetComponent<Hand>();
            if (existing != null || Planned<Hand>(node, ctx)) return existing;
            if (ctx != null && !ctx.ShouldCreate(Key<Hand>(node), "add a Hand")) return null;
            return ctx.AddComponent<Hand>(node);
        }

        /// <summary>
        /// Make <paramref name="node"/> a <see cref="Holdable"/> resting where it stands now;
        /// one that is one gets nothing.
        /// </summary>
        /// <param name="node">A scene object: the tool or part.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The holdable; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static Holdable MakeHoldable(GameObject node, OpContext ctx = null)
        {
            InScene(node);
            Holdable existing = node.GetComponent<Holdable>();
            if (existing != null || Planned<Holdable>(node, ctx)) return existing;
            if (ctx != null && !ctx.ShouldCreate(Key<Holdable>(node), "make it holdable, resting where it stands")) return null;
            Holdable holdable = ctx.AddComponent<Holdable>(node);
            holdable.CaptureHome();
            ctx.Edited(holdable);
            return holdable;
        }

        /// <summary>
        /// Give holdable <paramref name="node"/> a <see cref="SendHome"/>, disabled unless
        /// <paramref name="startEnabled"/>: enabling it (a step, as it ends) sends the holdable back
        /// where it rests. One that has one gets nothing.
        /// </summary>
        /// <param name="node">A holdable (or one planned to be).</param>
        /// <param name="startEnabled">Make it switched on (it sends the holdable home as the scene starts); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The component; null in a dry run that would make one, or where the node is no holdable.</returns>
        public static SendHome AddSendHome(GameObject node, bool startEnabled = false, OpContext ctx = null)
        {
            InScene(node);
            SendHome existing = node.GetComponent<SendHome>();
            if (existing != null || Planned<SendHome>(node, ctx)) return existing;
            if (!Part(node, ctx, out Holdable _)) return null;
            if (ctx != null && !ctx.ShouldCreate(Key<SendHome>(node), "send it back to its rest when enabled")) return null;
            SendHome send = ctx.AddComponent<SendHome>(node);
            send.enabled = startEnabled;
            ctx.Edited(send);
            return send;
        }

        /// <summary>
        /// Make <paramref name="node"/> a <see cref="Target"/>, requiring
        /// <paramref name="requires"/> in the selecting hand where one is named (one named
        /// that is no holdable, nor planned to be, is raised as <see cref="NotHoldableCode"/>
        /// and not required). A target keeps the requirement it has where none is named. Where the
        /// project has an input (an input door's: a mouse through the EventSystem, a headset's
        /// controllers), its meshes get colliders, once for every input (<see cref="AddColliders"/>; give
        /// them first for colliders of another kind), and it is wired for each, so it is selectable as it is made.
        /// </summary>
        /// <param name="node">A scene object: what is selected.</param>
        /// <param name="requires">The holdable the hand must hold; null: none asked.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The target; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static Target MakeTarget(GameObject node, GameObject requires = null, OpContext ctx = null)
        {
            InScene(node);
            Holdable needed = requires == null ? null : requires.GetComponent<Holdable>();
            if (requires != null && needed == null && !Planned<Holdable>(requires, ctx))
            {
                ctx.Raise(NotHoldableCode, $"'{node.name}' would require '{requires.name}' in hand, which is no Holdable: " +
                                           "make it one first (Interactions.MakeHoldable)");
                requires = null;
            }
            Target target = node.GetComponent<Target>();
            if (target == null && !Planned<Target>(node, ctx))
            {
                if (ctx == null || ctx.ShouldCreate(Key<Target>(node), "make it a target")) target = ctx.AddComponent<Target>(node);
            }
            if ((target != null || Planned<Target>(node, ctx)) && _inputs.Count > 0)
            {
                AddColliders(node, ctx: ctx);  // every input's rays find it: given once, for all of them
                foreach (ITargetInput input in _inputs) input.Wire(node, ctx);
            }
            if (requires == null) return target;
            // A holdable only planned (a dry run's) is no requirement yet: it differs from any.
            bool differs = target == null || needed == null || target.requires != needed;
            string what = $"require '{OpContextEdits.Label(requires)}' in hand";
            if (!differs || ((target == null || needed == null) && ctx.Planned("change", node, what))) return target;
            if (ctx != null && !ctx.ShouldChange(Asked(target, node), what))
                return target;
            target.requires = needed;
            ctx.Edited(target);
            return target;
        }

        /// <summary>
        /// Give each mesh under <paramref name="node"/> a collider (a <see cref="MeshCollider"/>
        /// of its own mesh, convex where <paramref name="convex"/>), so a pointer's or a controller's
        /// ray finds it; a mesh that has a collider gets none. A skinned mesh (a plug on its cable) gets a box on each bone that
        /// carries it (<see cref="BoneBoxes"/>), moving with the bone, so it is found where it is
        /// drawn however its bones carry it; a collider of its mesh as bound, which would stay where
        /// it was exported, goes. A node with no mesh under it at all (a helper an artist placed: where a part
        /// is brought) gets a box <see cref="EmptyTargetBox"/> across at its origin. A held <see cref="Holdable"/>
        /// switches its colliders off, so it never stands between the user and what they aim at.
        /// </summary>
        /// <remarks>
        /// Prior art: Unity's colliders take a mesh as it is bound, never as it is skinned, and a
        /// ragdoll's are primitives on its bones. Baking the skin into a mesh collider as the bones move
        /// (<c>SkinnedMeshRenderer.BakeMesh</c>) was tried first: it costs a bake and a cook a moving
        /// frame, and a collider was seen left with no mesh after a take moved its bones, without a
        /// word from Unity. A box fitted once to what each bone carries needs neither, and a part a
        /// user selects is a plug, a hose: a few bones.
        /// </remarks>
        /// <param name="node">A scene object: a target, or what a ray should find.</param>
        /// <param name="convex">Make each mesh's collider convex (its hull: cheaper to test, and what a moving rigidbody needs); false: the mesh as it is.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>How many colliders it gave (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static int AddColliders(GameObject node, bool convex = false, OpContext ctx = null)
        {
            InScene(node);
            int added = 0;
            if (node.GetComponentsInChildren<MeshFilter>(true).Length == 0 && node.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
            {
                if (node.GetComponent<Collider>() != null || Planned<BoxCollider>(node, ctx)) return 0;
                if (ctx != null && !ctx.ShouldCreate(Key<BoxCollider>(node), "give it a box, so a ray finds it: it shows no mesh")) return 1;
                ctx.AddComponent<BoxCollider>(node).size = Vector3.one * EmptyTargetBox;
                return 1;
            }
            foreach (MeshFilter mesh in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.sharedMesh == null || mesh.GetComponent<Collider>() != null || Planned<MeshCollider>(mesh.gameObject, ctx)) continue;
                added++;
                if (ctx != null && !ctx.ShouldCreate(Key<MeshCollider>(mesh.gameObject), convex ? "give it a convex collider, so a ray finds it"
                                                                                                 : "give it a collider, so a ray finds it")) continue;
                MeshCollider made = ctx.AddComponent<MeshCollider>(mesh.gameObject);
                made.convex = convex;
            }
            const string unbound = "take away its collider of the mesh as bound: its bones carry it elsewhere";
            foreach (SkinnedMeshRenderer skin in node.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                // A collider of the mesh as bound stays where the part was exported, catching clicks there.
                if (skin.TryGetComponent(out MeshCollider bound) && bound.sharedMesh == skin.sharedMesh
                    && !ctx.Planned("delete", bound, unbound) && (ctx == null || ctx.ShouldDelete(bound, unbound)))
                    ctx.Destroy(bound);
                foreach (var (bone, box) in BoneBoxes(skin))
                {
                    if (bone.GetComponent<Collider>() != null || Planned<BoxCollider>(bone.gameObject, ctx)) continue;
                    added++;
                    if (ctx != null && !ctx.ShouldCreate(Key<BoxCollider>(bone.gameObject), $"give it a box around what of '{skin.name}' it carries, so a ray finds it")) continue;
                    BoxCollider boxed = ctx.AddComponent<BoxCollider>(bone.gameObject);
                    boxed.center = box.center;
                    boxed.size = box.size;
                }
            }
            return added;
        }

        /// <summary>The thinnest a bone's box is, in metres: a cable's own thickness, still found by a ray.</summary>
        public const float LeastBoneBox = 0.01f;

        /// <summary>How wide the box a node with no mesh gets is (<see cref="AddColliders"/>), in metres: a hand's width.</summary>
        public const float EmptyTargetBox = 0.1f;

        /// <summary>
        /// The box around what each bone of <paramref name="skin"/> carries, in the bone's own space: the
        /// vertices it carries most, as bound (their bind pose taken into the bone), at least
        /// <see cref="LeastBoneBox"/> across. A bone carrying no vertex most gets none.
        /// </summary>
        /// <param name="skin">A skinned mesh.</param>
        /// <returns>Each bone with its box.</returns>
        public static List<(Transform bone, Bounds box)> BoneBoxes(SkinnedMeshRenderer skin) => SkinBoxes.Of(skin);

        /// <summary>
        /// Let <paramref name="root"/>'s <see cref="ClipPlayer"/> (made, with an Animator, where
        /// it has none) play <paramref name="clip"/>, lending it every holdable under the root it
        /// moves, over the stretch in which it moves anything (<see cref="Window"/>). A clip it plays
        /// already has its holdables and its stretch brought up to date (the stretch only while its
        /// entry says so, <see cref="ClipPlayer.Entry.auto"/>: one set by hand stays).
        /// </summary>
        /// <param name="root">The delivery's instance in the scene: what the clip animates.</param>
        /// <param name="clip">One of the delivery's takes.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The clip player; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="clip"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="root"/> is not in a scene.</exception>
        public static ClipPlayer AddClip(GameObject root, AnimationClip clip, OpContext ctx = null) => AddClip(root, clip, null, ctx);

        /// <summary>
        /// <see cref="AddClip(GameObject, AnimationClip, OpContext)"/>, its stretch found by <paramref name="rules"/>
        /// (<see cref="Window"/>).
        /// </summary>
        /// <param name="root">The delivery's instance in the scene: what the clip animates.</param>
        /// <param name="clip">One of the delivery's takes.</param>
        /// <param name="rules">How the clip's curves are read (null: <see cref="WindowRules"/>' defaults).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The clip player; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="clip"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="root"/> is not in a scene.</exception>
        public static ClipPlayer AddClip(GameObject root, AnimationClip clip, WindowRules rules, OpContext ctx = null)
            => AddClip(root, clip, rules, null, ctx);

        /// <summary>
        /// <see cref="AddClip(GameObject, AnimationClip, WindowRules, OpContext)"/>, playing over <paramref name="window"/>:
        /// x where it starts, y where it ends, in seconds into the clip, each below 0 found from its motion
        /// (<see cref="Window"/>). The entry's <see cref="ClipPlayer.Entry.auto"/> is on where both are found, off where
        /// either is given; null: its stretch as the entry says.
        /// </summary>
        /// <param name="root">The delivery's instance in the scene: what the clip animates.</param>
        /// <param name="clip">One of the delivery's takes.</param>
        /// <param name="rules">How the clip's curves are read (null: <see cref="WindowRules"/>' defaults).</param>
        /// <param name="window">Where it plays from and to (null: as its entry says).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The clip player; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="clip"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="root"/> is not in a scene.</exception>
        public static ClipPlayer AddClip(GameObject root, AnimationClip clip, WindowRules rules, Vector2? window, OpContext ctx = null)
        {
            InScene(root);
            if (clip == null) throw new ArgumentNullException(nameof(clip), "name one of the delivery's takes");
            if (root.GetComponent<Animator>() == null && !Planned<Animator>(root, ctx))
            {
                if (ctx == null || ctx.ShouldCreate(Key<Animator>(root), "add an Animator for its clips"))
                    ctx.AddComponent<Animator>(root);
            }
            ClipPlayer player = root.GetComponent<ClipPlayer>();
            if (player == null && !Planned<ClipPlayer>(root, ctx))
            {
                if (ctx == null || ctx.ShouldCreate(Key<ClipPlayer>(root), "add a Clip Player")) player = ctx.AddComponent<ClipPlayer>(root);
            }
            List<Holdable> lends = ClipMotion.Lends(root, clip);
            List<Holdable> rests = ClipMotion.Rests(root, clip, lends);
            Vector2 found = Window(clip, ClipMotion.Paths(root, lends), rules);
            ClipPlayer.Entry entry = player == null ? null : player.Find(clip.name);
            // Given, the window is the caller's (each part below 0: found); else a stretch set by hand (Auto off) stays.
            bool auto = window == null ? entry == null || entry.auto : window.Value.x < 0f && window.Value.y < 0f;
            Vector2 want = window == null || auto ? found
                : new Vector2(window.Value.x >= 0f ? window.Value.x : found.x, window.Value.y >= 0f ? window.Value.y : found.y);
            bool timed = entry != null && (window == null
                ? !entry.auto || Mathf.Approximately(entry.from, want.x) && Mathf.Approximately(entry.until, want.y)
                : entry.auto == auto && Mathf.Approximately(entry.from, want.x) && Mathf.Approximately(entry.until, want.y));
            if (entry != null && Same(entry.lends, lends) && Same(entry.rests, rests) && timed) return player;
            string what = entry == null ? Adds(clip.name) : $"bring '{clip.name}' up to date: the holdables it moves, the stretch it plays";
            if (player == null && ctx.Planned("change", root, what)) return player;
            if (ctx != null && !ctx.ShouldChange(Asked(player, root), what)) return player;
            if (entry == null) player.clips.Add(entry = new ClipPlayer.Entry { clip = clip });
            entry.lends = lends;
            entry.rests = rests;
            entry.auto = auto;
            if (window != null || entry.auto)
            {
                entry.from = want.x;
                entry.until = want.y;
            }
            ctx.Edited(player);
            return player;
        }

        /// <summary>
        /// The stretch of <paramref name="clip"/> worth playing, in seconds into it (x: where it
        /// starts, y: where it ends): from its first change to its last, so the still stretches a
        /// take made for one long animation stands through are left out; and, for the tools it
        /// lends (<paramref name="tools"/>), never the poses a tool pops into around its work. Such
        /// a take sets a tool in place on a frame as it starts (a key from its table to its lock)
        /// and onto the next take's first pose, or back to its rest, on a frame as it ends: a tool
        /// comes from the hand and goes back to it instead, so what plays starts once the tool is
        /// in place and ends before it pops away.
        /// </summary>
        /// <remarks>
        /// A pop is a change over <see cref="WindowRules.popSeconds"/> at most (a frame or two of a
        /// take keyed every frame; two keys at one instant where the import reduced them) that is
        /// <see cref="WindowRules.popTimes"/> times faster than its curve usually moves, or stands
        /// alone between two holds. Prior art: Unity's clip import takes a start and an end frame by
        /// hand (the Animation tab's clip ranges); this finds the stretch from the curves, so a
        /// delivery plays right as it stands.
        /// </remarks>
        /// <param name="clip">A clip.</param>
        /// <param name="tools">The paths, from the clip's root, of the holdables it lends: their curves and those under them are a tool's. Null: none.</param>
        /// <param name="rules">How its curves are read: what stands still, what is a pop (null: <see cref="WindowRules"/>' defaults).</param>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> is null.</exception>
        public static Vector2 Window(AnimationClip clip, IEnumerable<string> tools = null, WindowRules rules = null)
            => ClipWindow.Of(clip, tools, rules);

        /// <summary>
        /// How <see cref="Window"/> reads a clip's curves: what stands still, and what is a pop (a part set in
        /// place on a frame or two). The defaults read a take keyed every frame at 30 fps, or reduced on
        /// import; a project whose takes are keyed otherwise passes its own.
        /// </summary>
        public sealed class WindowRules
        {
            /// <summary>How little a key's value differs from the one before it for the curve to stand still between them.</summary>
            public float still = 1e-5f;

            /// <summary>The longest a pop lasts, in seconds: two frames of a 30 fps take, and the overshoot key a resample leaves before one.</summary>
            public float popSeconds = 0.1f;

            /// <summary>How many times its curve's usual rate a quick change is, at least, to be a pop within motion.</summary>
            public float popTimes = 12f;
        }

        /// <summary>
        /// Have <paramref name="root"/>'s clip player play <paramref name="clip"/> when
        /// <paramref name="after"/> ends (a lid that opens as its latch's clip ends); both clips
        /// must be the player's (or planned to be).
        /// </summary>
        /// <param name="root">The delivery's instance (its <see cref="ClipPlayer"/>).</param>
        /// <param name="clip">The clip to play.</param>
        /// <param name="after">The clip whose end plays it.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool PlayAfter(GameObject root, string clip, string after, OpContext ctx = null)
        {
            InScene(root);
            if (!Part(root, ctx, out ClipPlayer player) || !Plays(root, player, after, ctx) || !Plays(root, player, clip, ctx)) return false;
            ClipPlayer.Entry first = player == null ? null : player.Find(after);
            if (first != null && first.then == clip) return false;
            string what = $"play '{clip}' when '{after}' ends";
            if (first == null && ctx.Planned("change", root, what)) return false;
            if (ctx != null && !ctx.ShouldChange(Asked(player, root), what))
                return true;
            first.then = clip;
            ctx.Edited(player);
            return true;
        }

        /// <summary>
        /// Say what becomes of <paramref name="placed"/>, a holdable <paramref name="clip"/> puts in place, as the clip
        /// ends (<see cref="ClipPlayer.Entry.leaves"/>): left there, out of the hand (<paramref name="leaves"/>: a part
        /// fitted into its slot), or brought back to the hand as any tool is. One the clip moves is left where the clip
        /// puts it, that place its rest; one it does not move, which works for it (it rides what the clip moves,
        /// <see cref="AddClipTrigger"/>), stays at its work. The clip must be the player's (or planned to be). One that
        /// is so already gets nothing.
        /// </summary>
        /// <param name="root">The delivery's instance (its <see cref="ClipPlayer"/>).</param>
        /// <param name="clip">The clip.</param>
        /// <param name="placed">The holdable (or one planned to be) it puts in place.</param>
        /// <param name="leaves">Whether the clip leaves it where it puts it.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetLeaves(GameObject root, AnimationClip clip, GameObject placed, bool leaves = true, OpContext ctx = null)
        {
            InScene(root);
            InScene(placed);
            if (clip == null) throw new ArgumentNullException(nameof(clip), "name one of the delivery's takes");
            if (!Part(root, ctx, out ClipPlayer player) || !Plays(root, player, clip.name, ctx)) return false;
            if (!Part(placed, ctx, out Holdable held)) return false;
            ClipPlayer.Entry entry = player == null ? null : player.Find(clip.name);
            bool left = entry != null && held != null && entry.leaves.Contains(held);
            if (left == leaves) return false;
            string what = left ? $"bring '{placed.name}' back to the hand as '{clip.name}' ends" : $"leave '{placed.name}' where '{clip.name}' puts it";
            if (ctx.Planned("change", root, what)) return false;  // a dry run asked once: its live run changes it once
            if (ctx != null && !ctx.ShouldChange(Asked(player, root), what)) return true;
            if (left) entry.leaves.Remove(held);
            else entry.leaves.Add(held);
            ctx.Edited(player);
            return true;
        }

        /// <summary>
        /// Have <paramref name="root"/>'s clip player play <paramref name="clip"/> at <paramref name="speed"/>
        /// (<see cref="ClipPlayer.Entry.speed"/>: 1 as it was animated); the clip must be the player's (or planned to be).
        /// One played so already gets nothing.
        /// </summary>
        /// <param name="root">The delivery's instance (its <see cref="ClipPlayer"/>).</param>
        /// <param name="clip">The clip.</param>
        /// <param name="speed">How fast it plays; under 0.01 is 0.01.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool SetSpeed(GameObject root, string clip, float speed, OpContext ctx = null)
        {
            InScene(root);
            if (!Part(root, ctx, out ClipPlayer player) || !Plays(root, player, clip, ctx)) return false;
            speed = Mathf.Max(0.01f, speed);
            ClipPlayer.Entry entry = player == null ? null : player.Find(clip);
            if (entry != null ? Mathf.Approximately(entry.speed, speed) : Mathf.Approximately(speed, 1f)) return false;  // a clip just added plays at 1
            string what = $"play '{clip}' at {speed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} of its speed";
            if (entry == null && ctx.Planned("change", root, what)) return false;
            if (ctx != null && !ctx.ShouldChange(Asked(player, root), what)) return true;
            entry.speed = speed;
            ctx.Edited(player);
            return true;
        }

        /// <summary>
        /// Put a <see cref="ClipTrigger"/> on <paramref name="target"/> (a <see cref="Target"/>, or
        /// planned to be), disabled unless <paramref name="startEnabled"/>, wired to the target's events once:
        /// while something enables it, a selection plays <paramref name="clip"/> on <paramref name="player"/>'s
        /// clip player, a press plays it while held (<paramref name="whileHeld"/>: a button that runs a pump while
        /// it is held), or a press held <paramref name="pressSeconds"/> plays it (a button held to confirm). A
        /// target used several times gets a trigger for each clip, so each plays its own. Where
        /// <paramref name="tool"/> (a holdable) is one the clip does not move, it works, in the selecting hand's
        /// place, at the part under the target the clip moves (the target itself where it moves none). A trigger
        /// for the clip there already is brought to play as asked (<see cref="SetPress"/>,
        /// <see cref="SetReverseOnRelease"/>). A clip the player does not play (nor is planned to) is raised as
        /// <see cref="MissingPartCode"/> and nothing is made; a tool that is no holdable (nor planned to be), as
        /// <see cref="NotHoldableCode"/>, and left out.
        /// </summary>
        /// <param name="target">The selected object (a <see cref="Target"/>, or planned to be).</param>
        /// <param name="player">The delivery's root (its <see cref="ClipPlayer"/>, playing the clip, or planned to be).</param>
        /// <param name="clip">The clip's name.</param>
        /// <param name="tool">The tool the selection needs in hand (null: none).</param>
        /// <param name="whileHeld">Play it while a press is held rather than on a click. A trigger for the clip there
        /// played the other way is turned this way (one trigger a clip, so a click and a press never both play it).</param>
        /// <param name="pressSeconds">Without <paramref name="whileHeld"/>: play it once a press has been held this long, in
        /// seconds (<see cref="ClipTrigger.pressSeconds"/>); 0: on a click.</param>
        /// <param name="reverseOnRelease">With <paramref name="whileHeld"/>: let go, play it back to its start
        /// (<see cref="ClipTrigger.reverseOnRelease"/>); false: stop it where it stands.</param>
        /// <param name="startEnabled">Make it switched on (a selection plays its clip from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The trigger; null in a dry run that would make one, or where a part is missing (raised).</returns>
        public static ClipTrigger AddClipTrigger(GameObject target, GameObject player, string clip, GameObject tool = null,
                                                 bool whileHeld = false, float pressSeconds = 0f, bool reverseOnRelease = false,
                                                 bool startEnabled = false, OpContext ctx = null)
        {
            InScene(target);
            InScene(player);
            if (!Part(target, ctx, out Target selected) || !Part(player, ctx, out ClipPlayer clips) || !Plays(player, clips, clip, ctx)) return null;
            pressSeconds = Mathf.Max(0f, pressSeconds);
            ClipTrigger trigger = TriggerOf(target, clip, whileHeld);
            ClipTrigger other = TriggerOf(target, clip, !whileHeld);
            if (trigger == null && other != null)
            {
                Turn(other, selected, whileHeld, ctx);  // a dry run plans the rest on it as it stands: turning changes nothing else
                trigger = other;
            }
            if (trigger != null)
            {
                SetPress(trigger, pressSeconds, ctx);
                SetReverseOnRelease(trigger, reverseOnRelease, ctx);
                // The tool that works for it, as asked now: one made before its row named a tool takes it.
                ClipPlayer.Entry played = clips == null ? null : clips.Find(clip);
                Holdable works = tool == null ? null : tool.GetComponent<Holdable>();
                bool toBe = tool != null && works == null && IsHoldable(tool, ctx);  // a dry run only planned it holdable: left as it is
                if (works != null && played != null && played.lends.Contains(works)) works = null;  // the clip moves it: lent, never at work
                if (played == null || toBe || trigger.tool == works) return trigger;
                string hand = works != null ? $"have '{works.name}' work for '{clip}'" : $"play '{clip}' with no tool at work";
                if (ctx != null && !ctx.ShouldChange(trigger, hand)) return trigger;
                trigger.tool = works;
                trigger.workAt = works == null ? null : ClipMotion.MovedUnder(player, played.clip, target.transform);
                ctx.Edited(trigger);
                return trigger;
            }
            string key = $"{OpContextEdits.Key(target, typeof(ClipTrigger))} {clip}";
            if (ctx.Planned("create", key)) return null;
            if (tool != null && !IsHoldable(tool, ctx))
            {
                ctx.Raise(NotHoldableCode, $"'{target.name}' would play '{clip}' with '{tool.name}' at work, which is no Holdable: " +
                                           "make it one first (Interactions.MakeHoldable)");
                tool = null;
            }
            string what = whileHeld ? $"play '{clip}' while pressed" + (reverseOnRelease ? ", back to its start as it is let go" : "")
                        : pressSeconds > 0f ? $"play '{clip}' once a press has been held {pressSeconds:0.#} s"
                        : $"play '{clip}' when selected";
            bool create = ctx == null || ctx.ShouldCreate(key, what);
            bool wire = ctx == null || ctx.ShouldChange(Asked(selected, target), what);
            if (!create || !wire) return null;
            trigger = ctx.AddComponent<ClipTrigger>(target);
            trigger.enabled = startEnabled;
            trigger.player = clips;
            trigger.clip = clip;
            trigger.whileHeld = whileHeld;
            trigger.pressSeconds = pressSeconds;
            trigger.reverseOnRelease = reverseOnRelease;
            ClipPlayer.Entry entry = clips.Find(clip);
            Holdable holdable = tool == null ? null : tool.GetComponent<Holdable>();
            if (holdable != null && !entry.lends.Contains(holdable))
            {
                trigger.tool = holdable;
                trigger.workAt = ClipMotion.MovedUnder(player, entry.clip, target.transform);
            }
            Wire(selected, trigger);
            ctx.Edited(trigger);
            ctx.Edited(selected);
            return trigger;
        }

        // Turn trigger to play its clip while held, or on a click, its calls on selected's events following; each asked
        // apart, so a dry run (false) plans both, as the live run makes both.
        static void Turn(ClipTrigger trigger, Target selected, bool whileHeld, OpContext ctx)
        {
            string what = whileHeld ? $"play '{trigger.clip}' while pressed" : $"play '{trigger.clip}' when selected";
            bool turn = ctx == null || ctx.ShouldChange(trigger, what);
            bool rewire = ctx == null || ctx.ShouldChange(selected, what);
            if (!turn || !rewire) return;
            Unwire(selected, trigger);
            trigger.whileHeld = whileHeld;
            Wire(selected, trigger);
            ctx.Edited(trigger);
            ctx.Edited(selected);
        }

        // A trigger's calls on its target's events: a press and its release while held or timed, else a selection.
        static void Wire(Target selected, ClipTrigger trigger)
        {
            if (trigger.PlaysOnPress)
            {
                UnityEventTools.AddPersistentListener(selected.onPressed, trigger.Hold);
                UnityEventTools.AddPersistentListener(selected.onReleased, trigger.Release);
            }
            else UnityEventTools.AddPersistentListener(selected.onSelected, trigger.Play);
        }

        static void Unwire(Target selected, ClipTrigger trigger)
        {
            UnityEventTools.RemovePersistentListener<Hand>(selected.onPressed, trigger.Hold);
            UnityEventTools.RemovePersistentListener<Hand>(selected.onReleased, trigger.Release);
            UnityEventTools.RemovePersistentListener<Hand>(selected.onSelected, trigger.Play);
        }

        /// <summary>
        /// Have <paramref name="trigger"/> play its clip once a press has been held <paramref name="seconds"/>
        /// (<see cref="ClipTrigger.pressSeconds"/>; 0: on a click), its calls on the target's events following. One so
        /// already gets nothing. A trigger played while held stays so.
        /// </summary>
        /// <param name="trigger">A trigger there (one only planned is made so: <see cref="AddClipTrigger"/>'s <c>pressSeconds</c>).</param>
        /// <param name="seconds">How long the press is held first; 0: a click.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="trigger"/> is null.</exception>
        public static bool SetPress(ClipTrigger trigger, float seconds, OpContext ctx = null)
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger), "name the trigger (one a dry run only planned: give AddClipTrigger its press)");
            seconds = Mathf.Max(0f, seconds);
            if (Mathf.Approximately(trigger.pressSeconds, seconds)) return false;
            Target selected = trigger.GetComponent<Target>();
            string what = seconds > 0f ? $"play its clip once a press has been held {seconds:0.#} s" : "play its clip on a click";
            bool change = ctx == null || ctx.ShouldChange(trigger, what);
            bool rewire = selected == null || ctx == null || ctx.ShouldChange(selected, what);
            if (!change || !rewire) return true;
            if (selected != null) Unwire(selected, trigger);
            trigger.pressSeconds = seconds;
            if (selected != null) Wire(selected, trigger);
            ctx.Edited(trigger);
            if (selected != null) ctx.Edited(selected);
            return true;
        }

        /// <summary>
        /// Have <paramref name="trigger"/>, a trigger played while held, play its clip back to its start as the press
        /// is let go (<paramref name="reverse"/>, <see cref="ClipTrigger.reverseOnRelease"/>), or stop it where it
        /// stands. One so already gets nothing.
        /// </summary>
        /// <param name="trigger">A trigger there (one only planned is made so: <see cref="AddClipTrigger"/>'s <c>reverseOnRelease</c>).</param>
        /// <param name="reverse">Play it back as the press is let go.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="trigger"/> is null.</exception>
        public static bool SetReverseOnRelease(ClipTrigger trigger, bool reverse, OpContext ctx = null)
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger), "name the trigger (one a dry run only planned: give AddClipTrigger its release)");
            if (trigger.reverseOnRelease == reverse) return false;
            if (ctx != null && !ctx.ShouldChange(trigger, reverse ? "play its clip back to its start as the press is let go"
                                                                  : "stop its clip where it stands as the press is let go")) return true;
            trigger.reverseOnRelease = reverse;
            ctx.Edited(trigger);
            return true;
        }

        /// <summary>
        /// Fit <paramref name="tool"/>'s holdable to its work where its rig is off: its tip
        /// <paramref name="tip"/> metres from its pivot along its own axes, turned by
        /// <paramref name="turn"/> (Euler degrees about its own axes) to line up with what it works
        /// at (<see cref="Holdable.tipPosition"/>, <see cref="Holdable.tipRotation"/>). A tool fitted
        /// so already gets nothing.
        /// </summary>
        /// <param name="tool">A holdable (or planned to be).</param>
        /// <param name="tip">Where it works, from its pivot.</param>
        /// <param name="turn">The turn that lines it up.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool FitTool(GameObject tool, Vector3 tip, Vector3 turn, OpContext ctx = null) => FitTool(tool, tip, turn, -1f, ctx);

        /// <summary>
        /// <see cref="FitTool(GameObject, Vector3, Vector3, OpContext)"/>, its way to and from its work
        /// taking <paramref name="seconds"/> (<see cref="Holdable.travelSeconds"/>; 0: there at once, with no
        /// animation; below 0: left as it is).
        /// </summary>
        /// <param name="tool">The tool, a holdable or one to be (a dry run may only have planned it).</param>
        /// <param name="tip">Where it works, from its pivot, in metres along its own axes.</param>
        /// <param name="turn">The turn, in degrees about its own axes, that lines its axes up with its work's.</param>
        /// <param name="seconds">Its travel time; below 0: its own.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool FitTool(GameObject tool, Vector3 tip, Vector3 turn, float seconds, OpContext ctx = null)
        {
            InScene(tool);
            if (!Part(tool, ctx, out Holdable holdable)) return false;
            bool keeps = seconds < 0f;
            if (holdable != null && holdable.tipPosition == tip && holdable.tipRotation == turn
                && (keeps || Mathf.Approximately(holdable.travelSeconds, seconds))) return false;
            string what = keeps ? $"fit it to its work: tip {tip}, turned {turn}" : $"fit it to its work: tip {tip}, turned {turn}, {seconds:0.##} s to get there";
            if (holdable == null && ctx.Planned("change", tool, what)) return false;
            if (ctx != null && !ctx.ShouldChange(Asked(holdable, tool), what)) return true;
            holdable.tipPosition = tip;
            holdable.tipRotation = turn;
            if (!keeps) holdable.travelSeconds = seconds;
            ctx.Edited(holdable);
            return true;
        }

        /// <summary>The trigger on <paramref name="target"/> that plays <paramref name="clip"/> (on a click, or while held); null when it has none.</summary>
        /// <param name="target">A target.</param>
        /// <param name="clip">The clip's name.</param>
        /// <param name="whileHeld">The trigger plays it while a press is held.</param>
        public static ClipTrigger TriggerOf(GameObject target, string clip, bool whileHeld = false)
        {
            if (target == null) return null;
            foreach (ClipTrigger trigger in target.GetComponents<ClipTrigger>())
                if (trigger.clip == clip && trigger.whileHeld == whileHeld) return trigger;
            return null;
        }

        /// <summary>
        /// Put a <see cref="SelectNear"/> on <paramref name="node"/> (a target, or planned to be),
        /// disabled unless <paramref name="startEnabled"/>: while something enables it (a step), the
        /// target selects itself once what it requires, in hand, comes within reach. A node that has
        /// one gets nothing.
        /// </summary>
        /// <param name="node">The target (a slot).</param>
        /// <param name="startEnabled">Make it switched on (it acts from the scene's start); false: off, for something to enable.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The component; null in a dry run that would make one, or where the node is no target (raised).</returns>
        public static SelectNear AddSelectNear(GameObject node, bool startEnabled = false, OpContext ctx = null)
        {
            InScene(node);
            SelectNear existing = node.GetComponent<SelectNear>();
            if (existing != null || Planned<SelectNear>(node, ctx)) return existing;
            if (!Part(node, ctx, out Target _)) return null;
            if (ctx != null && !ctx.ShouldCreate(Key<SelectNear>(node), "select it once what it requires comes near")) return null;
            SelectNear near = ctx.AddComponent<SelectNear>(node);
            near.enabled = startEnabled;
            ctx.Edited(near);
            return near;
        }


        /// <summary>
        /// Wire <paramref name="node"/>'s selection to put it in the selecting hand (its
        /// <see cref="Target"/> to its <see cref="Holdable"/>); a wiring that is there gets nothing.
        /// </summary>
        /// <param name="node">A target that is holdable (or planned to be both).</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it changed anything (or would, in a dry run).</returns>
        public static bool TakeOnSelect(GameObject node, OpContext ctx = null)
        {
            InScene(node);
            if (!Part(node, ctx, out Target selected) || !Part(node, ctx, out Holdable holdable)) return false;
            if (selected != null && holdable != null && PersistentCalls.Wired(selected, selected.onSelected, holdable, nameof(Holdable.TakeBy), null, ctx)) return false;
            const string what = "take it into the hand when selected";
            if ((selected == null || holdable == null) && ctx.Planned("change", node, what)) return false;
            if (ctx != null && !ctx.ShouldChange(Asked(selected, node), what))
                return true;
            UnityEventTools.AddPersistentListener(selected.onSelected, holdable.TakeBy);
            ctx.Edited(selected);
            return true;
        }

        /// <summary>
        /// Switch <paramref name="part"/> off once <paramref name="node"/>'s <see cref="Holdable"/> is taken
        /// (its <see cref="Holdable.onTaken"/> wired to the part's <c>enabled</c>, a persistent call the
        /// inspector shows): a cue at something that has done its job once it is in hand (an arrow at a tool,
        /// which would ride into the hand). What switches the part on (a step) still does. A wiring there
        /// gets nothing; a node that is no holdable, nor planned to be, gets nothing either (a cue at a part
        /// no one takes stays as it is).
        /// </summary>
        /// <param name="node">The holdable (or one a dry run planned).</param>
        /// <param name="part">What goes off, a component on any object.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether it wired it (or would, in a dry run).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="part"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static bool DisableOnTake(GameObject node, Behaviour part, OpContext ctx = null)
        {
            InScene(node);
            if (part == null) throw new ArgumentNullException(nameof(part), "name what goes off (a dry run that only planned it: pass its PlannedObject)");
            Holdable held = node.GetComponent<Holdable>();
            if (held == null && !Planned<Holdable>(node, ctx)) return false;
            string what = $"switch '{OpContextEdits.Label(part.gameObject)}' off once it is taken";
            if (held != null && PersistentCalls.Wired(held, held.onTaken, part, EnabledSetter.Name, false, ctx)) return false;
            if (ctx.Planned("change", node, what)) return false;
            if (ctx != null && !ctx.ShouldChange(Asked(held, node), what)) return true;
            var off = (UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>), part, EnabledSetter);
            UnityEventTools.AddBoolPersistentListener(held.onTaken, off, false);
            ctx.Edited(held);
            return true;
        }

        /// <summary>
        /// <see cref="DisableOnTake(GameObject, Behaviour, OpContext)"/> for a part on <paramref name="part"/>, an
        /// object a dry run only planned: the change its live run asks for, planned once; nothing in a live run.
        /// </summary>
        /// <param name="node">The holdable (or one a dry run planned).</param>
        /// <param name="part">The planned object the part that goes off is on.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the change now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="part"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static bool DisableOnTake(GameObject node, PlannedObject part, OpContext ctx = null)
        {
            InScene(node);
            if (part == null) throw new ArgumentNullException(nameof(part), "name the planned object the part is on");
            return IsHoldable(node, ctx) && ctx.PlanChange(node, $"switch '{part.Path}' off once it is taken");
        }

        /// <summary>
        /// Have <paramref name="cue"/> (an arrow, a glow: any behaviour on <paramref name="node"/>) show only while it has
        /// something to point out: a <see cref="TargetCue"/> on <paramref name="node"/>, made switched off for something
        /// to enable (a step), showing the cue while one of <paramref name="targets"/> can still be acted on. The cue's
        /// own one there is reused: given the targets it lacks, and set to wait and to go as asked. It names the objects
        /// and reads each one's <see cref="Target"/> as it comes on, so they need not be targets yet (whatever makes
        /// them so may run later); one that never is has nothing to wait for.
        /// </summary>
        /// <param name="node">The object the cue is on.</param>
        /// <param name="cue">What it shows and hides.</param>
        /// <param name="targets">What the cue points out: the objects a user acts on.</param>
        /// <param name="waitsForHand">Hidden until what a target requires is in a hand (<see cref="TargetCue.waitsForHand"/>).</param>
        /// <param name="goesOnceActed">Gone once a target has been selected (<see cref="TargetCue.goesOnceActed"/>).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The cue's switch; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/>, <paramref name="cue"/> or <paramref name="targets"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static TargetCue AddTargetCue(GameObject node, Behaviour cue, IEnumerable<GameObject> targets, bool waitsForHand = true,
                                             bool goesOnceActed = true, OpContext ctx = null)
        {
            InScene(node);
            if (cue == null) throw new ArgumentNullException(nameof(cue), "name what it shows (a dry run that only planned it: pass its PlannedObject and type)");
            if (targets == null) throw new ArgumentNullException(nameof(targets), "name the targets it points out");
            var watched = new List<GameObject>();
            foreach (GameObject each in targets)
                if (each != null && !watched.Contains(each)) watched.Add(each);
            TargetCue found = null;
            foreach (TargetCue each in node.GetComponents<TargetCue>())
                if (each.cue == cue) found = each;
            string key = CueKey(OpContextEdits.Key(node, typeof(TargetCue)), cue.GetType());
            if (found == null)
            {
                if (ctx.Planned("create", key)) return null;
                if (ctx != null && !ctx.ShouldCreate(key, CueAction)) return null;
                found = ctx.AddComponent<TargetCue>(node);
                found.enabled = false;
                found.cue = cue;
                found.targets.AddRange(watched);
                found.waitsForHand = waitsForHand;
                found.goesOnceActed = goesOnceActed;
                ctx.Edited(found);
                return found;
            }
            var lacks = new List<GameObject>();
            foreach (GameObject target in watched)
                if (!found.targets.Contains(target)) lacks.Add(target);
            if (lacks.Count == 0 && found.waitsForHand == waitsForHand && found.goesOnceActed == goesOnceActed) return found;
            if (ctx != null && !ctx.ShouldChange(found, CueAction)) return found;
            found.targets.AddRange(lacks);
            found.waitsForHand = waitsForHand;
            found.goesOnceActed = goesOnceActed;
            ctx.Edited(found);
            return found;
        }

        /// <summary>
        /// <see cref="AddTargetCue(GameObject, Behaviour, IEnumerable{GameObject}, bool, bool, OpContext)"/> for a
        /// <paramref name="cue"/> on <paramref name="node"/>, an object a dry run only planned: the switch its live run
        /// makes, planned once; nothing in a live run.
        /// </summary>
        /// <param name="node">The planned object the cue is on.</param>
        /// <param name="cue">The cue's type (an arrow's, a glow's), which the plan names.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Whether it planned the switch now.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="cue"/> is null.</exception>
        public static bool AddTargetCue(PlannedObject node, Type cue, OpContext ctx = null)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name the planned object the cue is on");
            if (cue == null) throw new ArgumentNullException(nameof(cue), "name the cue's type");
            return ctx.PlanCreate(CueKey(OpContextEdits.Key(node, typeof(TargetCue)), cue), CueAction);
        }

        /// <summary>The name of the object <see cref="AddWorkSpot(ClipTrigger, OpContext)"/> makes for the tool named
        /// <paramref name="tool"/>, under the part it works on: <c>Work &lt;tool&gt;</c>.</summary>
        /// <param name="tool">The tool's name.</param>
        public static string WorkSpotName(string tool) => $"Work {tool}";

        /// <summary>
        /// Give <paramref name="trigger"/> a spot of its own where its tool works (<see cref="ClipTrigger.workAt"/>): an
        /// object under the part its clip moves (<see cref="WorkSpotName"/>), riding it, made on that part's own pivot
        /// and axes (the rig's place for a tool) and moved by a person from there; the tool's tip rests on it, lined up
        /// with it (<see cref="Holdable.WorkPose"/>), so where a tool works is an object in the scene. One there is reused: a
        /// tool works at one spot on a part, whichever clip it works for. A trigger with no tool at work (its clip
        /// moves the tool) gets none. Making the spot and having the trigger use it are asked apart.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The spot's object; null where it has no tool at work, or a dry run would make it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="trigger"/> is null.</exception>
        public static GameObject AddWorkSpot(ClipTrigger trigger, OpContext ctx = null)
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger), "name the trigger (one a dry run only planned: pass its target, player, clip and tool)");
            if (trigger.tool == null) return null;
            string name = WorkSpotName(trigger.tool.name);
            Transform at = trigger.WorkPoint;  // the part its clip moves, else the target itself
            Transform rides = at.name == name && at.parent != null ? at.parent : at;
            Transform found = rides.Find(name);
            if (found == null)
            {
                string key = new PlannedObject(name, rides).Path;
                if (ctx.Planned("create", key))
                {
                    ctx.ShouldChange(trigger, UseSpotAction);  // a dry run's: another clip's trigger planned the spot, this one its use of it
                    return null;
                }
                bool use = true;
                if (ctx != null)
                {
                    // Each asked apart: a dry run (false) plans both, as the live run makes both.
                    bool create = ctx.ShouldCreate(key, SpotAction);
                    use = ctx.ShouldChange(trigger, UseSpotAction);
                    if (!create) return null;
                }
                var node = new GameObject(name);
                node.transform.SetParent(rides, false);
                ctx.Created(node);
                found = node.transform;
                if (!use) return node;
            }
            else if (trigger.workAt == found || ctx != null && !ctx.ShouldChange(trigger, UseSpotAction)) return found.gameObject;
            trigger.workAt = found;
            ctx.Edited(trigger);
            return found.gameObject;
        }

        /// <summary>
        /// <see cref="AddWorkSpot(ClipTrigger, OpContext)"/> for the trigger <see cref="AddClipTrigger"/> makes on
        /// <paramref name="target"/> for <paramref name="clip"/> with <paramref name="tool"/>, which a dry run only
        /// planned: the spot and the trigger's use of it, planned once; nothing in a live run.
        /// </summary>
        /// <param name="target">The selected object.</param>
        /// <param name="root">The delivery's root, whose animator the clip is read under.</param>
        /// <param name="clip">The clip.</param>
        /// <param name="tool">The tool the selection needs in hand.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>The spot, as planned, for the actions that place it; null where the clip moves the tool (none works).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static PlannedObject AddWorkSpot(GameObject target, GameObject root, AnimationClip clip, GameObject tool, OpContext ctx = null)
        {
            InScene(target);
            InScene(root);
            if (clip == null) throw new ArgumentNullException(nameof(clip), "name one of the delivery's takes");
            if (tool == null) throw new ArgumentNullException(nameof(tool), "name the tool");
            if (MovesIt(root, clip, tool)) return null;
            Transform rides = ClipMotion.MovedUnder(root, clip, target.transform);
            if (rides == null) rides = target.transform;
            var spot = new PlannedObject(WorkSpotName(tool.name), rides);
            if (ctx == null || !ctx.DryRun) return spot;
            if (rides.Find(spot.Name) == null) ctx.PlanCreate(spot.Path, SpotAction);  // made already for another clip of the part: reused
            ctx.ShouldChange(OpContextEdits.Label(target), UseSpotAction);  // each trigger's own use of it
            return spot;
        }

        /// <summary>Whether <paramref name="clip"/> moves <paramref name="part"/> or anything under it (read under <paramref name="root"/>'s animator).</summary>
        /// <param name="root">The delivery's root.</param>
        /// <param name="clip">The clip.</param>
        /// <param name="part">A part under the root.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static bool MovesIt(GameObject root, AnimationClip clip, GameObject part)
        {
            if (root == null) throw new ArgumentNullException(nameof(root), "name the delivery's root");
            if (clip == null) throw new ArgumentNullException(nameof(clip), "name a clip");
            if (part == null) throw new ArgumentNullException(nameof(part), "name the part");
            string own = AnimationUtility.CalculateTransformPath(part.transform, root.transform);
            foreach (string path in ClipMotion.Moving(clip))
                if (path == own || path.StartsWith(own + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        const string SpotAction = "the spot its tool works at";
        const string UseSpotAction = "have its tool work at its own spot";

        // What a plan calls the switch of one cue on an object: an object may carry a cue of each kind.
        static string CueKey(string key, Type cue) => $"{key} {cue.Name}";

        const string CueAction = "show its cue only while its target can be acted on";

        /// <summary>
        /// Put an <see cref="InteractionActor"/> on <paramref name="node"/>: how a player of steps beside it (an
        /// autopilot) acts on this feature's targets and waits for its clips. A node that has one gets nothing.
        /// </summary>
        /// <param name="node">The scene object the player of steps is on.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The actor; null in a dry run that would make one.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="node"/> is not in a scene.</exception>
        public static InteractionActor AddStepActor(GameObject node, OpContext ctx = null)
        {
            InScene(node);
            InteractionActor existing = node.GetComponent<InteractionActor>();
            if (existing != null || Planned<InteractionActor>(node, ctx)) return existing;
            if (ctx != null && !ctx.ShouldCreate(Key<InteractionActor>(node), StepActorAction)) return null;
            return ctx.AddComponent<InteractionActor>(node);
        }

        /// <summary>
        /// <see cref="AddStepActor(GameObject, OpContext)"/> on <paramref name="node"/>, an object a dry run only planned:
        /// the actor its live run puts on it, planned once; nothing in a live run.
        /// </summary>
        /// <param name="node">The planned object the player of steps goes on.</param>
        /// <param name="ctx">The run's context.</param>
        /// <returns>Null: there is nothing to put it on.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static InteractionActor AddStepActor(PlannedObject node, OpContext ctx = null)
        {
            ctx.PlanCreate(OpContextEdits.Key(node, typeof(InteractionActor)), StepActorAction);
            return null;
        }

        const string StepActorAction = "act on its targets and wait for its clips for a player of steps";

        /// <summary>Whether <paramref name="node"/> is a <see cref="Holdable"/>, or this dry run planned it one (<see cref="MakeHoldable"/>).</summary>
        /// <param name="node">A scene object.</param>
        /// <param name="ctx">The run's context (null: what the scene holds).</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static bool IsHoldable(GameObject node, OpContext ctx = null)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name a scene object");
            return node.GetComponent<Holdable>() != null || Planned<Holdable>(node, ctx);
        }

        // The setter a part's switch is persisted by (Unity names a property's setter set_<name>).
        static readonly System.Reflection.MethodInfo EnabledSetter = typeof(Behaviour).GetProperty(nameof(Behaviour.enabled)).GetSetMethod();

        // -- helpers ---------------------------------------------------------------

        // What a plan calls T on node (OpContextEdits.Key).
        static string Key<T>(GameObject node) => OpContextEdits.Key(node, typeof(T));

        // What a change to node's part is asked on: the part, else (a dry run only planned it) node, which a plan
        // names alike (a component by its object's label), so a dry run plans what its live run changes.
        internal static UnityEngine.Object Asked(Component part, GameObject node) => part != null ? (UnityEngine.Object)part : node;

        // Whether this dry run already planned to make a T on node (as if it had been made).
        static bool Planned<T>(GameObject node, OpContext ctx) => ctx.Planned("create", Key<T>(node));

        // What AddClip asks to give a player a clip it does not play yet.
        static string Adds(string clip) => $"play '{clip}'";

        // Whether root's player plays clip, or this dry run planned it to (AddClip); false, raised, when neither.
        static bool Plays(GameObject root, ClipPlayer player, string clip, OpContext ctx)
        {
            if (player != null && player.Find(clip) != null || ctx.Planned("change", root, Adds(clip))) return true;
            ctx.Raise(MissingPartCode, $"'{root.name}' plays no '{clip}': add it first (Interactions.AddClip)");
            return false;
        }

        // The T on node, or a dry run's planned one (null then); false, raised, when neither.
        static bool Part<T>(GameObject node, OpContext ctx, out T part) where T : Component
        {
            part = node.GetComponent<T>();
            if (part != null || Planned<T>(node, ctx)) return true;
            ctx.Raise(MissingPartCode, $"'{node.name}' has no {typeof(T).Name}: add one first (Interactions)");
            return false;
        }

        /// <summary>
        /// Pose <paramref name="root"/>'s parts as they stand once <paramref name="clips"/> have
        /// played, in order, each holding its last pose (as a <see cref="ClipPlayer"/> keeps it: where
        /// its motion ends, <see cref="Window"/>): what a clip moves takes its end, the rest stays as it
        /// was (a take keys every node, most where they rest), and so does a holdable with its parts,
        /// which goes back to the hand or its rest as its clip ends. Disposing puts every part back. A
        /// build uses it to see the scene as a step does: a door an earlier step opened stands open.
        /// </summary>
        /// <param name="root">The delivery's root, its clips' animator root.</param>
        /// <param name="clips">The clips played so far, in order (nulls skipped).</param>
        /// <param name="rules">How the clips' curves are read for where each ends (null: <see cref="WindowRules"/>' defaults).</param>
        /// <returns>What puts the parts back.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
        public static IDisposable PosedAfter(GameObject root, IEnumerable<AnimationClip> clips, WindowRules rules = null)
            => ClipMotion.PosedAfter(root, clips, rules);

        /// <summary>
        /// The parts under <paramref name="root"/> that <paramref name="clip"/> moves (a curve on it
        /// whose keys change), each once, none under another: where its motion goes. A holdable it
        /// lends is left out, with its parts: it goes back to the hand or its rest as the clip ends.
        /// </summary>
        /// <param name="root">The delivery's root, the clip's animator root.</param>
        /// <param name="clip">One of its clips.</param>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="clip"/> is null.</exception>
        public static List<Transform> Moves(GameObject root, AnimationClip clip) => ClipMotion.Moves(root, clip);

        static bool Same(List<Holdable> a, List<Holdable> b)
        {
            if (a == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static void InScene(GameObject node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node), "name a scene object");
            if (!node.scene.IsValid()) throw new ArgumentException($"'{node.name}' is not in a scene (a prefab asset?): name its instance", nameof(node));
        }
    }
}
