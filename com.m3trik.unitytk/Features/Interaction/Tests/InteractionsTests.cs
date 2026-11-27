using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary>
    /// The Interaction actions, held to the kernel's three checks as a chain a builder runs
    /// (a key made holdable and taken on selection, a lock that requires it playing a clip):
    /// one Ctrl+Z restores the scene, the dry run's plan names every change the run makes
    /// (what an earlier step only planned counting as made), and a second run plans nothing.
    /// </summary>
    public class InteractionsTests
    {
        GameObject _root;
        GameObject _key;
        GameObject _lock;
        AnimationClip _unlock;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("delivery");
            _key = Child("KEY", new Vector3(1, 0, 0));
            _lock = Child("LOCK", new Vector3(0, 1, 0));
            _unlock = new AnimationClip { name = "Unlock" };
            _unlock.SetCurve("KEY", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
            _unlock.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 1, 90));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && _root.TryGetComponent(out ClipPlayer player)) player.Clear();
            Object.DestroyImmediate(_root);
        }

        GameObject Child(string name, Vector3 at)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_root.transform);
            node.transform.localPosition = at;
            return node;
        }

        [Test]
        public void Posed_after_clips_the_parts_stand_at_their_ends_the_unmoved_where_they_were_then_back()
        {
            // A later take keys the lock where it rests (a take keys every node): it must not shut it again.
            var keyOnly = new AnimationClip { name = "KeyOnly" };
            keyOnly.SetCurve("KEY", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 1, 1, 2));
            keyOnly.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Constant(0, 1, 0));
            _lock.transform.localPosition = new Vector3(0, 1, 0);
            using (Interactions.PosedAfter(_root, new[] { _unlock, null, keyOnly }))
            {
                Assert.That(_lock.transform.localEulerAngles.z, Is.EqualTo(90f).Within(0.01f), "the lock the first clip turned stays turned");
                Assert.That(_key.transform.localPosition.x, Is.EqualTo(2f).Within(1e-4f), "the key at the second clip's end");
            }
            Assert.That(_lock.transform.localEulerAngles.z, Is.EqualTo(0f).Within(0.01f), "back after");
            Assert.That(_key.transform.localPosition, Is.EqualTo(new Vector3(1, 0, 0)));
        }

        // What a builder asks for a part taken into the hand and a lock it opens, in order.
        void Chain(OpContext ctx)
        {
            Interactions.MakeHoldable(_key, ctx);
            Interactions.MakeTarget(_key, null, ctx);
            Interactions.TakeOnSelect(_key, ctx);
            Interactions.MakeTarget(_lock, _key, ctx);
            Interactions.AddClip(_root, _unlock, ctx);
            Interactions.AddClipTrigger(_lock, _root, "Unlock", _key, ctx: ctx);
        }

        [Test]
        public void The_chain_plans_what_it_then_does_and_a_second_run_plans_nothing()
        {
            using (Interactions.OnlyInputs())  // the feature's own changes, whatever input doors the project has
            {
                var dry = new OpContext(dryRun: true);
                Chain(dry);
                Assert.That(_root.GetComponentsInChildren<Component>().Length, Is.EqualTo(3), "a dry run makes nothing (three Transforms)");
                var live = new OpContext();
                Chain(live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(live.Changes.Count, Is.EqualTo(10), "the trigger made, and the lock wired to it");
                var again = new OpContext(dryRun: true);
                Chain(again);
                Assert.That(again.Changes, Is.Empty);
                Assert.That(live.Issues, Is.Empty);
            }
        }

        [Test]
        public void A_user_equipped_gets_a_hand_in_view_and_each_input_as_planned_once_and_undone()
        {
            var eyes = new GameObject("EYES", typeof(Camera));
            try
            {
                Camera camera = eyes.GetComponent<Camera>();
                using (Interactions.OnlyInputs(new MarkerInput()))
                {
                    var dry = new OpContext(dryRun: true);
                    Assert.That(Interactions.EquipUser(camera, Interactions.HandInView, dry), Is.Null, "a dry run makes nothing");
                    Assert.That(dry.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(new[] { ("create", "EYES/HAND"), ("create", "EYES/Marker") }));
                    Hand hand = null;
                    OpContext.RunMenu("Equip user", ctx =>
                    {
                        hand = Interactions.EquipUser(camera, Interactions.HandInView, ctx);
                        Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                    });
                    Assert.That(hand, Is.Not.Null);
                    Assert.That(hand.transform.parent, Is.SameAs(eyes.transform), "in the user's view");
                    Assert.That(hand.transform.localPosition, Is.EqualTo(Interactions.HandInView));
                    Assert.That(eyes.transform.Find("Marker"), Is.Not.Null, "each input the project has equips them");
                    var again = new OpContext(dryRun: true);
                    Assert.That(Interactions.EquipUser(camera, Interactions.HandInView, again), Is.SameAs(hand), "the scene's hand is the user's");
                    Assert.That(again.Changes, Is.Empty, "a second run changes nothing");
                    Undo.PerformUndo();
                    Assert.That(eyes.transform.childCount, Is.EqualTo(0), "one Ctrl+Z took the hand away");
                }
            }
            finally
            {
                Object.DestroyImmediate(eyes);
            }
        }

        // An input door's stand-in: each target it wires, and the eyes it equips, get a marker child, planned first.
        sealed class MarkerInput : ITargetInput
        {
            public string Name => "marker";

            public void Wire(GameObject node, OpContext ctx)
            {
                string key = new PlannedObject("Marker", node.transform).Path;
                if (node.transform.Find("Marker") != null || ctx.Planned("create", key)) return;
                if (ctx != null && !ctx.ShouldCreate(key, "mark it")) return;
                var marker = new GameObject("Marker");
                marker.transform.SetParent(node.transform, false);
                ctx.Created(marker);
            }

            public void Equip(Camera eyes, OpContext ctx) => Wire(eyes.gameObject, ctx);

            public void Equip(PlannedObject eyes, OpContext ctx) => ctx.PlanCreate(new PlannedObject("Marker", eyes).Path, "mark it");
        }

        [Test]
        public void A_target_made_is_wired_for_each_input_door_as_planned_and_once()
        {
            using (Interactions.OnlyInputs(new MarkerInput()))
            {
                var dry = new OpContext(dryRun: true);
                Chain(dry);
                Assert.That(dry.Changes.Count(c => c.target.EndsWith("/Marker")), Is.EqualTo(2), "the key and the lock, once each");
                var live = new OpContext();
                Chain(live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(_key.transform.Find("Marker") != null && _lock.transform.Find("Marker") != null);
                var again = new OpContext(dryRun: true);
                Chain(again);
                Assert.That(again.Changes, Is.Empty);
            }
            Assert.That(Interactions.Inputs.Any(i => i.Name == "marker"), Is.False, "the registered doors are restored");
        }

        [Test]
        public void A_node_with_no_mesh_gets_a_box_a_ray_finds()
        {
            // A helper an artist placed (where a part is brought, 2026-11-25) is an empty object: a click on it found nothing.
            var slot = new GameObject("SLOT_C01");
            slot.transform.SetParent(_lock.transform, false);
            var dry = new OpContext(dryRun: true);
            Assert.That(Interactions.AddColliders(slot, ctx: dry), Is.EqualTo(1), "planned");
            Assert.That(Interactions.AddColliders(slot, ctx: new OpContext()), Is.EqualTo(1));
            var box = slot.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null);
            Assert.That(box.size, Is.EqualTo(Vector3.one * Interactions.EmptyTargetBox), "a hand's width across, at its origin");
            Assert.That(Interactions.AddColliders(slot, ctx: new OpContext(dryRun: true)), Is.Zero, "a second pass gives none");
        }

        [Test]
        public void Each_mesh_under_a_node_gets_a_collider_planned_once_and_undone()
        {
            var geo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(geo.GetComponent<Collider>());
            geo.name = "LOCK_GEO";
            geo.transform.SetParent(_lock.transform, false);
            var dry = new OpContext(dryRun: true);
            Assert.That(Interactions.AddColliders(_lock, ctx: dry), Is.EqualTo(1));
            Assert.That(Interactions.AddColliders(_lock, ctx: dry), Is.Zero, "a collider the dry run planned counts as made");
            OpContext.RunMenu("Colliders", ctx =>
            {
                Interactions.AddColliders(_lock, ctx: ctx);
                Interactions.AddColliders(_lock, ctx: ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(geo.GetComponent<MeshCollider>().sharedMesh, Is.SameAs(geo.GetComponent<MeshFilter>().sharedMesh));
            Assert.That(Interactions.AddColliders(_lock, ctx: new OpContext(dryRun: true)), Is.Zero);
            Undo.PerformUndo();
            Assert.That(geo.GetComponent<Collider>() == null, "one Ctrl+Z took it away");
        }

        [Test]
        public void A_skinned_part_gets_a_box_on_each_bone_around_what_it_carries_moving_with_it()
        {
            // A plug on its cable: a cable segment on one bone, the plug's head on the next, 0.2 m along it.
            var plug = new GameObject("PLUG_GEO");
            plug.transform.SetParent(_lock.transform, false);
            var root = new GameObject("jnt_1").transform;
            root.SetParent(_lock.transform, false);
            var head = new GameObject("jnt_2").transform;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0f, 0.2f, 0f);
            var mesh = new Mesh { name = "plug" };
            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            foreach (var (y, bone, half) in new[] { (0f, 0, 0.002f), (0.15f, 0, 0.002f), (0.2f, 1, 0.03f), (0.28f, 1, 0.03f) })
                foreach (float x in new[] { -half, half })
                    foreach (float z in new[] { -half, half })
                    {
                        vertices.Add(new Vector3(x, y, z));
                        weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                    }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(Enumerable.Range(0, vertices.Count - 2).SelectMany(i => new[] { i, i + 1, i + 2 }).ToArray(), 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = new[] { root.worldToLocalMatrix * plug.transform.localToWorldMatrix, head.worldToLocalMatrix * plug.transform.localToWorldMatrix };
            var skin = plug.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { root, head };
            plug.AddComponent<MeshCollider>().sharedMesh = mesh;  // an earlier build's collider of the mesh as bound

            var dry = new OpContext(dryRun: true);
            Assert.That(Interactions.AddColliders(_lock, ctx: dry), Is.EqualTo(2), "a box on each bone");
            var live = new OpContext();
            Interactions.AddColliders(_lock, ctx: live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(plug.GetComponent<MeshCollider>() == null, "the collider of the mesh as bound is gone");
            BoxCollider cable = root.GetComponent<BoxCollider>(), plugHead = head.GetComponent<BoxCollider>();
            Assert.That(cable.size.y, Is.EqualTo(0.15f).Within(1e-4f), "the cable: what its bone carries");
            Assert.That(plugHead.size.x, Is.EqualTo(0.06f).Within(1e-4f));
            Assert.That(plugHead.center.y, Is.EqualTo(0.04f).Within(1e-4f), "in the bone's own space");
            Assert.That(cable.size.x, Is.EqualTo(Interactions.LeastBoneBox).Within(1e-4f), "a thin cable: at least a ray's catch across");

            head.localRotation = Quaternion.Euler(0f, 0f, 90f);  // the take swings the plug's head aside
            Physics.SyncTransforms();
            Assert.That(plugHead.bounds.size.x, Is.EqualTo(0.08f).Within(1e-3f), "its box swings with it");
            Assert.That(Interactions.AddColliders(_lock, ctx: new OpContext(dryRun: true)), Is.Zero, "a second build plans nothing");
        }

        [Test]
        public void A_part_two_steps_ask_for_is_planned_once_as_it_is_made_once()
        {
            void Twice(OpContext ctx)
            {
                Interactions.MakeHoldable(_key, ctx);
                Interactions.AddHand(_lock, ctx);
                Interactions.MakeHoldable(_key, ctx);
                Interactions.AddHand(_lock, ctx);
            }
            var dry = new OpContext(dryRun: true);
            Twice(dry);
            var live = new OpContext();
            Twice(live);
            Assert.That(dry.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(live.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(live.Changes.Count, Is.EqualTo(2));
        }

        [Test]
        public void The_chain_is_one_undo_step()
        {
            OpContext.RunMenu("Wire the lock", Chain);
            Assert.That(_lock.GetComponent<Target>().requires, Is.SameAs(_key.GetComponent<Holdable>()));
            Undo.PerformUndo();
            Assert.That(_key.GetComponent<Holdable>() == null && _lock.GetComponent<Target>() == null && _root.GetComponent<ClipPlayer>() == null,
                        "one Ctrl+Z took the whole chain away");
        }

        // A persistent call runs in a player only (Unity's default); an Edit Mode test runs it here.
        internal static void Live(Target target)
        {
            for (int i = 0; i < target.onSelected.GetPersistentEventCount(); i++)
                target.onSelected.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
        }

        [Test]
        public void The_wiring_takes_the_key_and_plays_the_lock_with_it_in_hand()
        {
            Chain(new OpContext());
            Live(_key.GetComponent<Target>());
            Live(_lock.GetComponent<Target>());
            var hand = Interactions.AddHand(Child("HAND", new Vector3(0, 2, 0)), new OpContext());
            var player = _root.GetComponent<ClipPlayer>();
            Assert.That(player.Find("Unlock").lends, Is.EqualTo(new[] { _key.GetComponent<Holdable>() }), "the clip moves the key: lent");
            ClipTrigger trigger = Interactions.TriggerOf(_lock, "Unlock");
            Assert.That(trigger.tool == null, "a tool the clip moves is the clip's: it works nowhere");

            Assert.That(_lock.GetComponent<Target>().Select(hand), Is.False, "the lock wants the key in hand");
            Assert.That(_key.GetComponent<Target>().Select(hand), Is.True);
            Assert.That(hand.Holds(_key.GetComponent<Holdable>()));
            Assert.That(_lock.GetComponent<Target>().Select(hand) && !player.Busy, Is.True, "its trigger is off until its step enables it");
            trigger.enabled = true;
            Assert.That(_lock.GetComponent<Target>().Select(hand), Is.True);
            Assert.That(player.IsPlaying("Unlock"));
        }

        [Test]
        public void A_target_two_steps_use_plays_only_the_clip_of_the_trigger_switched_on()
        {
            var close = new AnimationClip { name = "Lock" };
            close.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 90, 1, 0));
            var ctx = new OpContext();
            Interactions.MakeTarget(_lock, null, ctx);
            Interactions.AddClip(_root, _unlock, ctx);
            Interactions.AddClip(_root, close, ctx);
            ClipTrigger open = Interactions.AddClipTrigger(_lock, _root, "Unlock", ctx: ctx);
            ClipTrigger shut = Interactions.AddClipTrigger(_lock, _root, "Lock", ctx: ctx);
            Assert.That(open != shut && !open.enabled && !shut.enabled, "a trigger per clip, each made off for its step");
            Assert.That(Interactions.AddClipTrigger(_lock, _root, "Lock", ctx: new OpContext(dryRun: true)), Is.SameAs(shut), "once");
            Live(_lock.GetComponent<Target>());
            var player = _root.GetComponent<ClipPlayer>();

            open.enabled = true;
            _lock.GetComponent<Target>().Select(null);
            Assert.That(player.IsPlaying("Unlock") && !player.IsPlaying("Lock"), Is.True, "the step that opens it opens it, and no more");
            player.Advance(2f);
            Assert.That(_lock.transform.localEulerAngles.z, Is.EqualTo(90f).Within(1e-3f), "and it stays open");
            open.enabled = false;
            shut.enabled = true;
            _lock.GetComponent<Target>().Select(null);
            Assert.That(player.IsPlaying("Lock") && !player.IsPlaying("Unlock"), Is.True, "a later step closes it");
        }

        [Test]
        public void A_tool_whose_parts_a_clip_moves_rests_while_it_plays()
        {
            // The key's ring is keyed, not the key: the clip moved the ring from where the key rests; the lock turns at 0.5 s.
            var ring = new GameObject("RING");
            ring.transform.SetParent(_key.transform, false);
            var turn = new AnimationClip { name = "Turn" };
            turn.SetCurve("KEY/RING", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
            var lockTurn = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 0), new Keyframe(1, 90));
            turn.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.z", lockTurn);
            var ctx = new OpContext();
            Holdable key = Interactions.MakeHoldable(_key, ctx);
            ClipPlayer player = Interactions.AddClip(_root, turn, ctx);
            ClipPlayer.Entry entry = player.Find("Turn");
            Assert.That(entry.lends, Is.EqualTo(new[] { key }));
            Assert.That(entry.rests, Is.EqualTo(new[] { key }), "the clip keys its ring, not its place: its motion was made from its rest");
            Assert.That(entry.waits, "the clip waits for it to come from the hand");
            Assert.That(Interactions.AddClip(_root, turn, new OpContext(dryRun: true)), Is.SameAs(player));
            ClipPlayer.Entry unlock = Interactions.AddClip(_root, _unlock, ctx).Find("Unlock");
            Assert.That(unlock.rests, Is.Empty, "a clip keying the key's own place poses it itself");
        }

        [Test]
        public void A_take_that_keys_a_tool_where_it_rests_does_not_lend_it_and_the_tool_works_at_what_it_moves()
        {
            // A take keys every node, the tool held still: the screw (under the lock) turns alone.
            var bolt = new GameObject("BOLT");
            bolt.transform.SetParent(_lock.transform, false);
            bolt.transform.localPosition = new Vector3(0, 0, 0.2f);
            var turn = new AnimationClip { name = "Unscrew" };
            turn.SetCurve("KEY", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, 1, 1));
            turn.SetCurve("LOCK", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0, 1, 1));
            turn.SetCurve("LOCK/BOLT", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 1, 720));
            turn.SetCurve("LOCK/BOLT", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0, 0.2f, 1, 0.3f));
            var ctx = new OpContext();
            Holdable key = Interactions.MakeHoldable(_key, ctx);
            Interactions.MakeTarget(_lock, _key, ctx);
            ClipPlayer player = Interactions.AddClip(_root, turn, ctx);
            Assert.That(player.Find("Unscrew").lends, Is.Empty, "a tool keyed where it rests is not the clip's");
            ClipTrigger trigger = Interactions.AddClipTrigger(_lock, _root, "Unscrew", _key, ctx: ctx);
            Assert.That(trigger.tool == key && trigger.workAt == bolt.transform, "it works at the part the clip moves");

            var hand = Interactions.AddHand(Child("HAND", new Vector3(0, 2, 0)), ctx);
            hand.Take(key);
            trigger.enabled = true;
            Live(_lock.GetComponent<Target>());
            _lock.GetComponent<Target>().Select(hand);
            Assert.That(key.WorkingAt == bolt.transform && key.Travelling, "on its way to the bolt");
            player.Advance(0.5f);
            Assert.That(player.IsWaiting("Unscrew") && bolt.transform.localPosition.z == 0.2f, "the clip waits for it: the bolt has not turned");
            key.Tick(0f);
            key.Tick(key.TravelFor + 0.01f);
            Assert.That(Vector3.Distance(key.transform.position, bolt.transform.position), Is.LessThan(1e-4f), "its tip on the bolt before it turns");
            player.Advance(0.5f);
            key.Tick(0.02f);
            Assert.That(bolt.transform.localPosition.z, Is.EqualTo(0.25f).Within(1e-4f), "arrived: the clip plays");
            Assert.That(Vector3.Distance(key.transform.position, bolt.transform.position), Is.LessThan(1e-4f), "and it rides the bolt out");
            player.Advance(1f);
            Assert.That(key.WorkingAt == null && key.Travelling, "the clip ended: on its way back");
            key.Tick(0f);
            key.Tick(key.TravelFor + 0.01f);
            Assert.That(key.transform.position, Is.EqualTo(hand.Anchor.position), "back in hand");
        }

        // A take as a delivery made for one long animation carries it: every frame keyed (30 a second).
        static AnimationCurve Sampled(params (int frame, float value)[] at)
        {
            var keys = new List<Keyframe>();
            for (int i = 0; i + 1 < at.Length; i++)
                for (int f = at[i].frame; f < at[i + 1].frame; f++)
                    keys.Add(new Keyframe(f / 30f, Mathf.Lerp(at[i].value, at[i + 1].value, (f - at[i].frame) / (float)(at[i + 1].frame - at[i].frame))));
            keys.Add(new Keyframe(at[at.Length - 1].frame / 30f, at[at.Length - 1].value));
            var curve = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyBroken(curve, i, true);
            return curve;
        }

        // A take whose keys the import reduced: a key where its curve turns, and a pop as two keys an instant apart.
        static AnimationCurve Reduced(params (float time, float value)[] at)
        {
            var keys = new Keyframe[at.Length];
            for (int i = 0; i < at.Length; i++) keys[i] = new Keyframe(at[i].time, at[i].value);
            return new AnimationCurve(keys);
        }

        [Test]
        public void A_clips_window_is_its_changes_and_for_a_tool_never_the_poses_it_pops_into_around_its_work()
        {
            // A key take, keyed every frame: on its table at frame 0, popped to the lock at frame 1, pushed in to
            // frame 50, its bit turned from 51 to 90, held, then popped onto the next take's first pose at frame
            // 100 and held to 111; the lock it turns springs back after it has gone, to frame 110.
            var take = new AnimationClip { name = "KeyTake" };
            take.SetCurve("KEY", typeof(Transform), "localPosition.x", Sampled((0, 0f), (1, 1f), (100, 1f), (101, -0.2f), (111, -0.2f)));
            take.SetCurve("KEY", typeof(Transform), "localPosition.y", Sampled((0, 0f), (1, 1f), (50, 1.1f), (100, 1.1f), (101, 0.9f), (111, 0.9f)));
            take.SetCurve("KEY/RING", typeof(Transform), "localEulerAnglesRaw.y", Sampled((0, 0f), (51, 0f), (90, 80f), (100, 80f), (101, 0f), (111, 0f)));
            take.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.z", Sampled((0, 0f), (51, 0f), (90, 80f), (101, 80f), (110, 0f), (111, 0f)));
            Vector2 window = Interactions.Window(take, new[] { "KEY" });
            Assert.That(window.x, Is.EqualTo(1 / 30f).Within(1e-4f), "from where the key is in place: past its pop off the table");
            Assert.That(window.y, Is.EqualTo(100 / 30f).Within(2e-3f), "to where it pops away: the lock's spring back is no part of it");
            Assert.That(Interactions.Window(take), Is.EqualTo(new Vector2(0f, 110 / 30f)), "with no tool named, every change plays");

            // The next lock's, as an import reduces it: the key starts in place, eases in, turns, and pops back to
            // its table (two keys an instant apart) at 3 s, before the take ends.
            var next = new AnimationClip { name = "KeyTakeNext" };
            next.SetCurve("KEY", typeof(Transform), "localPosition.x", Reduced((0f, 0.2f), (1.2f, 0.19f), (3f, 0.19f), (3.0001f, 0f), (4f, 0f)));
            next.SetCurve("KEY/RING", typeof(Transform), "localEulerAnglesRaw.y", Reduced((0f, 0f), (1f, 0f), (2f, 80f), (3f, 80f), (3.0001f, 0f), (4f, 0f)));
            next.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.z", Reduced((0f, 0f), (1f, 0f), (2f, 80f), (3.0001f, 80f), (3.5f, 0f), (4f, 0f)));
            window = Interactions.Window(next, new[] { "KEY" });
            Assert.That(window.x, Is.EqualTo(0f), "in place from its start");
            Assert.That(window.y, Is.EqualTo(3f).Within(2e-3f), "and gone before it pops to the table");

            // A tool's take, reduced: popped to its work a second in (an overshoot key before the pop), at work
            // to 14 s, back at its rest on its last frame. Nothing else moves.
            var work = new AnimationClip { name = "ToolTake" };
            work.SetCurve("TOOL", typeof(Transform), "localPosition.x",
                          Reduced((0f, 0f), (1f, 0f), (1.0001f, 0.02f), (1.0002f, 0.6f), (2.5f, 0.598f), (4f, 0.594f), (12f, 0.594f),
                                  (14f, 0.598f), (16.9999f, 0.598f), (17f, 0f)));
            window = Interactions.Window(work, new[] { "TOOL" });
            Assert.That(window.x, Is.EqualTo(1.0002f).Within(1e-4f), "from where it is at its work");
            Assert.That(window.y, Is.EqualTo(14f).Within(1e-4f), "to where its work ends, short of its pop home");

            // Motion keyed sparsely is motion, however few its keys; a clip of pops alone plays whole; and
            // what is no tool's pops as it was made to (a part swapped in mid-take).
            var door = new AnimationClip { name = "Open" };
            door.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0f, 0f, 2f, 90f));
            Assert.That(Interactions.Window(door, new[] { "LOCK" }), Is.EqualTo(new Vector2(0f, 2f)));
            var swap = new AnimationClip { name = "Swap" };
            swap.SetCurve("KEY", typeof(Transform), "localPosition.x", Sampled((0, 0f), (30, 0f), (31, 5f), (60, 5f)));
            Assert.That(Interactions.Window(swap, new[] { "KEY" }), Is.EqualTo(new Vector2(0f, 2f)), "a tool the clip only pops, and nothing else: the whole clip");
            Assert.That(Interactions.Window(swap), Is.EqualTo(new Vector2(1f, 31 / 30f)), "no tool's: the pop is the take's change");
        }

        [Test]
        public void A_clip_added_plays_its_window_unless_its_entry_is_set_by_hand()
        {
            var take = new AnimationClip { name = "Take" };
            take.SetCurve("KEY", typeof(Transform), "localPosition.y", Sampled((0, 0f), (1, 1f), (31, 1.5f), (60, 1.5f), (61, 0f), (70, 0f)));
            take.SetCurve("LOCK", typeof(Transform), "localPosition.y", Sampled((0, 1f), (31, 1f), (60, 1.5f), (70, 1.5f)));
            var ctx = new OpContext();
            Interactions.MakeHoldable(_key, ctx);
            ClipPlayer player = Interactions.AddClip(_root, take, ctx);
            ClipPlayer.Entry entry = player.Find("Take");
            Assert.That(entry.from, Is.EqualTo(1 / 30f).Within(1e-4f), "the key it lends is in place");
            Assert.That(entry.until, Is.EqualTo(60 / 30f).Within(2e-3f), "and has not popped away");
            var again = new OpContext(dryRun: true);
            Interactions.AddClip(_root, take, again);
            Assert.That(again.Changes, Is.Empty, "a second run changes nothing");

            entry.auto = false;  // a person sets the stretch
            entry.from = 0.5f;
            again = new OpContext(dryRun: true);
            Interactions.AddClip(_root, take, again);
            Assert.That(again.Changes, Is.Empty, "set by hand: left as it is");
            entry.auto = true;
            Interactions.AddClip(_root, take, new OpContext());
            Assert.That(entry.from, Is.EqualTo(1 / 30f).Within(1e-4f), "found again once it asks");

            // The scene as it stands after the take: the lock where its motion ended, the key (a tool, gone
            // back to the hand or its rest) where it was.
            using (Interactions.PosedAfter(_root, new[] { take }))
            {
                Assert.That(_lock.transform.localPosition.y, Is.EqualTo(1.5f).Within(1e-3f));
                Assert.That(_key.transform.localPosition, Is.EqualTo(new Vector3(1, 0, 0)));
            }
        }

        // The window a caller gives (a sheet's) had no test of its own: one side given keeps what is found for the other,
        // both found is the entry's Auto, and a second run plans nothing.
        [Test]
        public void A_clip_added_over_a_window_given_plays_it_each_side_below_0_found_planned_once()
        {
            var take = new AnimationClip { name = "Take" };
            take.SetCurve("LOCK", typeof(Transform), "localPosition.y", Sampled((0, 0f), (15, 0f), (45, 1f), (60, 1f)));
            Vector2 found = Interactions.Window(take);
            Assert.That(found.x, Is.GreaterThan(0f), "premise: the still start is left out");

            var dry = new OpContext(dryRun: true);
            Interactions.AddClip(_root, take, null, new Vector2(0.25f, -1f), dry);
            var live = new OpContext();
            ClipPlayer player = Interactions.AddClip(_root, take, null, new Vector2(0.25f, -1f), live);
            Assert.That(live.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.action))), "the plan is the run");
            ClipPlayer.Entry entry = player.Find("Take");
            Assert.That((entry.from, entry.auto), Is.EqualTo((0.25f, false)), "its start as given: set by the caller, never found again");
            Assert.That(entry.until, Is.EqualTo(found.y).Within(1e-4f), "its end, below 0: found");
            var again = new OpContext(dryRun: true);
            Interactions.AddClip(_root, take, null, new Vector2(0.25f, -1f), again);
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");

            Interactions.AddClip(_root, take, null, new Vector2(-1f, -1f), new OpContext());
            Assert.That(entry.auto, Is.True, "both below 0: found, its Auto on");
            Assert.That(new[] { entry.from, entry.until }, Is.EqualTo(new[] { found.x, found.y }).Within(1e-4f));
            again = new OpContext(dryRun: true);
            Interactions.AddClip(_root, take, null, new Vector2(-1f, -1f), again);
            Assert.That(again.Changes, Is.Empty, "again: nothing");
        }

        [Test]
        public void A_press_plays_its_clip_while_held_as_planned_once()
        {
            var dry = new OpContext(dryRun: true);
            void Run(OpContext ctx)
            {
                Interactions.MakeTarget(_lock, null, ctx);
                Interactions.AddClip(_root, _unlock, ctx);
                Interactions.AddClipTrigger(_lock, _root, "Unlock", whileHeld: true, ctx: ctx);
            }
            Run(dry);
            var live = new OpContext();
            Run(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var again = new OpContext(dryRun: true);
            Run(again);
            Assert.That(again.Changes, Is.Empty);
            Target target = _lock.GetComponent<Target>();
            foreach (var when in new UnityEngine.Events.UnityEventBase[] { target.onPressed, target.onReleased })
                for (int i = 0; i < when.GetPersistentEventCount(); i++)
                    when.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            var player = _root.GetComponent<ClipPlayer>();
            Interactions.TriggerOf(_lock, "Unlock", whileHeld: true).enabled = true;
            target.Press(null);
            Assert.That(player.IsPlaying("Unlock") && !player.IsPaused("Unlock"), Is.True);
            target.Release();
            Assert.That(player.IsPaused("Unlock"), Is.True);

            ClipTrigger trigger = Interactions.TriggerOf(_lock, "Unlock", whileHeld: true);
            Assert.That(Interactions.SetReverseOnRelease(trigger, true) && trigger.reverseOnRelease, Is.True);
            Assert.That(Interactions.SetReverseOnRelease(trigger, true), Is.False, "so already");
            target.Press(null);
            target.Release();
            Assert.That(player.IsRewinding("Unlock"), Is.True, "let go: it plays back to its start");
        }

        [Test]
        public void A_trigger_played_the_other_way_is_turned_never_doubled()
        {
            Interactions.MakeTarget(_lock, null);
            Interactions.AddClip(_root, _unlock);
            ClipTrigger click = Interactions.AddClipTrigger(_lock, _root, "Unlock");
            ClipTrigger held = Interactions.AddClipTrigger(_lock, _root, "Unlock", whileHeld: true);
            Assert.That(held, Is.SameAs(click), "one trigger a clip");
            Assert.That(_lock.GetComponents<ClipTrigger>().Length, Is.EqualTo(1));
            Assert.That(held.whileHeld, Is.True);
            Target target = _lock.GetComponent<Target>();
            Assert.That((target.onSelected.GetPersistentEventCount(), target.onPressed.GetPersistentEventCount(), target.onReleased.GetPersistentEventCount()),
                        Is.EqualTo((0, 1, 1)), "a click no longer plays it; a press and its release do");
            Assert.That(Interactions.AddClipTrigger(_lock, _root, "Unlock").whileHeld, Is.False, "and back");
            Assert.That((target.onSelected.GetPersistentEventCount(), target.onPressed.GetPersistentEventCount()), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Two_meshes_of_one_name_are_planned_apart_as_they_are_made()
        {
            foreach (GameObject node in new[] { _key, _lock })
            {
                var geo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(geo.GetComponent<Collider>());
                geo.name = "button_GEO";
                geo.transform.SetParent(node.transform, false);
            }
            var dry = new OpContext(dryRun: true);
            Assert.That(Interactions.AddColliders(_key, ctx: dry) + Interactions.AddColliders(_lock, ctx: dry), Is.EqualTo(2));
            var live = new OpContext();
            Assert.That(Interactions.AddColliders(_key, ctx: live) + Interactions.AddColliders(_lock, ctx: live), Is.EqualTo(2));
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(dry.Changes.Select(c => c.target), Is.EqualTo(new[] { "delivery/KEY/button_GEO/MeshCollider", "delivery/LOCK/button_GEO/MeshCollider" }));
        }

        [Test]
        public void A_tool_a_clip_does_not_move_works_at_a_spot_of_its_own_under_what_the_clip_moves()
        {
            // A tool stood on its part's own pivot, axis on axis, so only a rig made for it worked: where it works is an object.
            var turn = new AnimationClip { name = "Turn" };
            turn.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 1, 90));
            void Run(OpContext ctx)
            {
                Interactions.MakeHoldable(_key, ctx);
                Interactions.MakeTarget(_lock, _key, ctx);
                Interactions.AddClip(_root, turn, ctx);
                ClipTrigger made = Interactions.AddClipTrigger(_lock, _root, "Turn", _key, ctx: ctx);
                if (made != null) Interactions.AddWorkSpot(made, ctx);
                else Interactions.AddWorkSpot(_lock, _root, turn, _key, ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            Assert.That(_lock.transform.childCount, Is.Zero, "a dry run makes nothing");
            var live = new OpContext();
            Run(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            ClipTrigger trigger = Interactions.TriggerOf(_lock, "Turn");
            Transform spot = _lock.transform.Find(Interactions.WorkSpotName("KEY"));
            Assert.That(spot, Is.Not.Null, "under the part the clip moves: it rides it");
            Assert.That(trigger.workAt, Is.SameAs(spot));
            var again = new OpContext(dryRun: true);
            Run(again);
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");

            // The spot is the place: moved and turned by a person, that is where the tool works, and it rides the part.
            spot.SetLocalPositionAndRotation(new Vector3(0.1f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            Holdable key = _key.GetComponent<Holdable>();
            key.WorkPose(trigger.WorkPoint, out Vector3 at, out Quaternion turned);
            Assert.That(Vector3.Distance(at, spot.position), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(turned, spot.rotation), Is.LessThan(0.01f));
            _lock.transform.Rotate(0f, 0f, 90f);
            key.WorkPose(trigger.WorkPoint, out Vector3 after, out _);
            Assert.That(Vector3.Distance(after, _lock.transform.TransformPoint(new Vector3(0.1f, 0f, 0f))), Is.LessThan(1e-5f), "it rode the lock as it turned");

            // A tool the clip moves is lent to it: no spot, nothing at work.
            Assert.That(Interactions.MovesIt(_root, _unlock, _key) && !Interactions.MovesIt(_root, turn, _key));
            Assert.That(Interactions.AddWorkSpot(_lock, _root, _unlock, _key, new OpContext(dryRun: true)), Is.Null);
        }

        [Test]
        public void A_part_put_in_place_riding_what_its_clip_moves_stays_there_out_of_the_hand()
        {
            // A part carried to its slot stayed in the hand while its stand-in slid home, and was held, unseen, into the next step.
            var turn = new AnimationClip { name = "Turn" };
            turn.SetCurve("LOCK", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 2));
            var ctx = new OpContext();
            Interactions.MakeHoldable(_key, ctx);
            Interactions.MakeTarget(_lock, _key, ctx);
            Interactions.AddClip(_root, turn, ctx);
            ClipTrigger trigger = Interactions.AddClipTrigger(_lock, _root, "Turn", _key, ctx: ctx);
            Assert.That(Interactions.SetLeaves(_root, turn, _key, true, ctx), Is.True, "a part its clip does not move is its to leave too: it stays at its work");
            Assert.That(Interactions.SetLeaves(_root, turn, _key, true, new OpContext(dryRun: true)), Is.False, "once");
            var hand = Interactions.AddHand(Child("HAND", new Vector3(5, 0, 0)), ctx);
            Holdable key = _key.GetComponent<Holdable>();
            key.CaptureHome();
            hand.Take(key);
            key.Arrive();
            ClipPlayer player = _root.GetComponent<ClipPlayer>();
            player.Play("Turn", key, trigger.WorkPoint);
            key.Arrive();
            player.Advance(0.5f);
            Assert.That(key.Holder, Is.SameAs(hand), "while its clip plays it is the hand's, at its work");
            player.Advance(2f);
            key.Tick(0.1f);
            Assert.That(player.IsPlaying("Turn"), Is.False);
            Assert.That(key.Holder, Is.Null, "the clip ended: the hand let go");
            Assert.That(hand.Held, Is.Null);
            Assert.That(key.WorkingAt, Is.SameAs(trigger.WorkPoint), "and it stays at its work");
            _lock.transform.position += new Vector3(0, 3, 0);  // what it was fitted to moves later (a door swings shut)
            key.Tick(0.1f);
            Assert.That(Vector3.Distance(key.transform.position, trigger.WorkPoint.position), Is.LessThan(1e-4f), "riding it");
        }

        [Test]
        public void A_slot_selects_itself_once_what_it_requires_comes_within_reach()
        {
            var ctx = new OpContext();
            Interactions.MakeHoldable(_key, ctx);
            Target slot = Interactions.MakeTarget(_lock, _key, ctx);
            SelectNear near = Interactions.AddSelectNear(_lock, ctx: ctx);
            Assert.That(near.enabled, Is.False, "made disabled, for a step to enable");
            Assert.That(Interactions.AddSelectNear(_lock, ctx: new OpContext(dryRun: true)), Is.SameAs(near));
            var hand = Interactions.AddHand(Child("HAND", new Vector3(5, 0, 0)), ctx);
            int selected = 0;
            slot.onSelected.AddListener(_ => selected++);
            Assert.That(near.Check(hand), Is.False, "not in hand");
            hand.Take(_key.GetComponent<Holdable>());
            _key.transform.position = _lock.transform.position + Vector3.right;
            Assert.That(near.Check(hand), Is.False, "in hand, out of reach");
            _key.transform.position = _lock.transform.position + Vector3.right * 0.1f;
            Assert.That(near.Check(hand) && selected == 1, Is.True);
            Assert.That(near.Check(hand), Is.False, "once");
        }

        [Test]
        public void A_send_home_is_made_disabled_on_a_holdable_alone()
        {
            var ctx = new OpContext();
            Assert.That(Interactions.AddSendHome(_lock, ctx: ctx), Is.Null);
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { Interactions.MissingPartCode }));
            Interactions.MakeHoldable(_key);
            SendHome send = Interactions.AddSendHome(_key);
            Assert.That(send != null && !send.enabled, "made disabled: it acts when something enables it");
            Assert.That(Interactions.AddSendHome(_key), Is.SameAs(send));
        }

        [Test]
        public void A_requirement_that_is_no_holdable_is_raised_and_left_out()
        {
            var ctx = new OpContext();
            Target target = Interactions.MakeTarget(_lock, _key, ctx);
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { Interactions.NotHoldableCode }));
            Assert.That(target.requires == null);
        }

        [Test]
        public void A_wiring_to_a_missing_part_is_raised_and_makes_nothing()
        {
            var ctx = new OpContext();
            Assert.That(Interactions.AddClipTrigger(_lock, _root, "Unlock", ctx: ctx), Is.Null);
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { Interactions.MissingPartCode }));
            Assert.That(ctx.Changes, Is.Empty);
        }

        [Test]
        public void A_trigger_set_to_a_timed_press_is_wired_to_the_press_planned_once_and_back()
        {
            Interactions.MakeTarget(_lock, null);
            Interactions.AddClip(_root, _unlock);
            ClipTrigger trigger = Interactions.AddClipTrigger(_lock, _root, "Unlock");
            Target target = _lock.GetComponent<Target>();
            var dry = new OpContext(dryRun: true);
            Assert.That(Interactions.SetPress(trigger, 0.5f, dry), Is.True);
            Assert.That(trigger.pressSeconds, Is.Zero, "a dry run changes nothing");
            var live = new OpContext();
            Assert.That(Interactions.SetPress(trigger, 0.5f, live) && trigger.pressSeconds == 0.5f, Is.True);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That((target.onSelected.GetPersistentEventCount(), target.onPressed.GetPersistentEventCount(), target.onReleased.GetPersistentEventCount()),
                        Is.EqualTo((0, 1, 1)), "a press and its release time it; a click no longer plays it");
            Assert.That(Interactions.SetPress(trigger, 0.5f, new OpContext(dryRun: true)), Is.False, "so already");
            Assert.That(Interactions.SetPress(trigger, 0f) && trigger.pressSeconds == 0f, Is.True);
            Assert.That((target.onSelected.GetPersistentEventCount(), target.onPressed.GetPersistentEventCount()), Is.EqualTo((1, 0)), "a click again");
            Assert.Throws<System.ArgumentNullException>(() => Interactions.SetPress(null, 1f), "a trigger only planned is made timed: AddClipTrigger's press");
            Assert.Throws<System.ArgumentNullException>(() => Interactions.SetReverseOnRelease(null, true));
        }

        [Test]
        public void A_timed_press_and_a_press_played_back_are_made_and_wired_once_as_planned()
        {
            var close = new AnimationClip { name = "Lock" };
            close.SetCurve("LOCK", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 90, 1, 0));
            void Run(OpContext ctx)
            {
                Interactions.MakeTarget(_lock, null, ctx);
                Interactions.AddClip(_root, _unlock, ctx);
                Interactions.AddClip(_root, close, ctx);
                Interactions.AddClipTrigger(_lock, _root, "Unlock", pressSeconds: 0.5f, ctx: ctx);
                Interactions.AddClipTrigger(_lock, _root, "Lock", whileHeld: true, reverseOnRelease: true, ctx: ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            var live = new OpContext();
            Run(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(live.Changes.Count(c => c.target.EndsWith("ClipTrigger Unlock") || c.target.EndsWith("ClipTrigger Lock")), Is.EqualTo(2),
                        "each trigger made once, under one plan");
            ClipTrigger timed = Interactions.TriggerOf(_lock, "Unlock"), held = Interactions.TriggerOf(_lock, "Lock", whileHeld: true);
            Assert.That((timed.pressSeconds, held.reverseOnRelease), Is.EqualTo((0.5f, true)));
            Target target = _lock.GetComponent<Target>();
            Assert.That((target.onSelected.GetPersistentEventCount(), target.onPressed.GetPersistentEventCount(), target.onReleased.GetPersistentEventCount()),
                        Is.EqualTo((0, 2, 2)), "both on the press and its release, each wired once");
            var again = new OpContext(dryRun: true);
            Run(again);
            Assert.That(again.Changes, Is.Empty, "a second run changes nothing");

            var changed = new OpContext(dryRun: true);
            Interactions.AddClipTrigger(_lock, _root, "Unlock", pressSeconds: 1f, ctx: changed);
            Assert.That(changed.Changes.Select(c => c.action), Has.Some.Contains("held 1 s"), "one there is brought to play as asked");
        }

        [Test]
        public void A_trigger_for_a_clip_the_player_does_not_play_or_with_a_tool_that_is_no_holdable_is_raised()
        {
            Interactions.MakeTarget(_lock, null);
            Interactions.AddClip(_root, _unlock);
            var missing = new OpContext();
            Assert.That(Interactions.AddClipTrigger(_lock, _root, "Spin", ctx: missing), Is.Null);
            Assert.That(missing.Issues.Select(i => i.code), Is.EqualTo(new[] { Interactions.MissingPartCode }));
            Assert.That(missing.Changes, Is.Empty, "nothing made for a clip it does not play");
            var notHeld = new OpContext();
            ClipTrigger trigger = Interactions.AddClipTrigger(_lock, _root, "Unlock", _key, ctx: notHeld);
            Assert.That(notHeld.Issues.Select(i => i.code), Is.EqualTo(new[] { Interactions.NotHoldableCode }));
            Assert.That(trigger != null && trigger.tool == null, "made, the tool left out");
            var planned = new OpContext(dryRun: true);
            Interactions.MakeHoldable(_key, planned);
            Interactions.AddClip(_root, new AnimationClip { name = "Spin" }, planned);
            Interactions.AddClipTrigger(_lock, _root, "Spin", _key, ctx: planned);
            Assert.That(planned.Issues, Is.Empty, "a clip and a holdable this dry run planned count as there");
        }

        [Test]
        public void A_clip_plays_after_another_as_planned_once_and_a_missing_one_is_raised()
        {
            var lift = new AnimationClip { name = "Lift" };
            lift.SetCurve("LOCK", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 1, 1, 2));
            void Run(OpContext ctx)
            {
                Interactions.AddClip(_root, _unlock, ctx);
                Interactions.AddClip(_root, lift, ctx);
                Interactions.PlayAfter(_root, "Lift", "Unlock", ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            var live = new OpContext();
            Run(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(_root.GetComponent<ClipPlayer>().Find("Unlock").then, Is.EqualTo("Lift"));
            var again = new OpContext(dryRun: true);
            Run(again);
            Assert.That(again.Changes, Is.Empty, "a second run changes nothing");
            var missing = new OpContext();
            Assert.That(Interactions.PlayAfter(_root, "Spin", "Unlock", missing), Is.False);
            Assert.That(missing.Issues.Select(i => i.code), Is.EqualTo(new[] { Interactions.MissingPartCode }));
        }

        [Test]
        public void A_tool_is_fitted_to_its_work_as_planned_once_its_travel_kept_unless_given()
        {
            void Run(OpContext ctx)
            {
                Interactions.MakeHoldable(_key, ctx);
                Interactions.FitTool(_key, new Vector3(0, 0, 0.1f), new Vector3(0, 90, 0), ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            Assert.That(dry.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "change" }), "fitted though it is only planned holdable");
            var live = new OpContext();
            Run(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Holdable tool = _key.GetComponent<Holdable>();
            Assert.That((tool.tipPosition, tool.tipRotation, tool.travelSeconds), Is.EqualTo((new Vector3(0, 0, 0.1f), new Vector3(0, 90, 0), Holdable.DefaultTravelSeconds)));
            var again = new OpContext(dryRun: true);
            Run(again);
            Assert.That(again.Changes, Is.Empty);
            Assert.That(Interactions.FitTool(_key, tool.tipPosition, tool.tipRotation, 0f) && tool.travelSeconds == 0f, Is.True, "its travel when given");
            Assert.That(Interactions.FitTool(_lock, Vector3.zero, Vector3.zero, new OpContext()), Is.False, "no holdable: nothing to fit");
        }

        // A skinned part on one bone under node, with an earlier build's collider of its mesh as bound.
        static void SkinnedPlug(GameObject node)
        {
            var plug = new GameObject("PLUG_GEO");
            plug.transform.SetParent(node.transform, false);
            var bone = new GameObject("jnt_1").transform;
            bone.SetParent(node.transform, false);
            var mesh = new Mesh { name = "plug" };
            mesh.SetVertices(new List<Vector3> { Vector3.zero, Vector3.up * 0.1f, Vector3.right * 0.1f });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } };
            mesh.bindposes = new[] { bone.worldToLocalMatrix * plug.transform.localToWorldMatrix };
            var skin = plug.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone };
            plug.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        [Test]
        public void A_skinned_part_two_inputs_wire_plans_its_bound_colliders_removal_once_as_it_is_removed_once()
        {
            SkinnedPlug(_lock);
            var dry = new OpContext(dryRun: true);
            Interactions.AddColliders(_lock, ctx: dry);
            Interactions.AddColliders(_lock, ctx: dry);  // a second input door's wiring
            var live = new OpContext();
            Interactions.AddColliders(_lock, ctx: live);
            Interactions.AddColliders(_lock, ctx: live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(dry.Changes.Count(c => c.kind == "delete"), Is.EqualTo(1));
        }

        [Test]
        public void Two_skinned_parts_of_one_name_plan_their_bound_colliders_removal_apart_as_both_are_removed()
        {
            SkinnedPlug(_key);
            SkinnedPlug(_lock);
            var dry = new OpContext(dryRun: true);
            Interactions.AddColliders(_key, ctx: dry);
            Interactions.AddColliders(_lock, ctx: dry);
            var live = new OpContext();
            Interactions.AddColliders(_key, ctx: live);
            Interactions.AddColliders(_lock, ctx: live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(dry.Changes.Where(c => c.kind == "delete").Select(c => c.target),
                        Is.EqualTo(new[] { "delivery/KEY/PLUG_GEO", "delivery/LOCK/PLUG_GEO" }), "each its own, by its path");
        }

        [Test]
        public void A_slot_selects_itself_for_the_hand_that_holds_what_it_requires_not_the_first_hand_found()
        {
            var ctx = new OpContext();
            var empty = Interactions.AddHand(Child("EMPTY_HAND", new Vector3(5, 0, 0)), ctx);  // found first
            Interactions.MakeHoldable(_key, ctx);
            Target slot = Interactions.MakeTarget(_lock, _key, ctx);
            SelectNear near = Interactions.AddSelectNear(_lock, ctx: ctx);
            near.enabled = true;
            var holding = Interactions.AddHand(Child("HAND", new Vector3(0, 2, 0)), ctx);
            holding.Take(_key.GetComponent<Holdable>());
            _key.transform.position = _lock.transform.position + Vector3.right * 0.1f;
            Hand selectedBy = null;
            slot.onSelected.AddListener(by => selectedBy = by);
            Assert.That(near.Check(), Is.True, "a player's frame");
            Assert.That(selectedBy, Is.SameAs(holding), "the hand holding it brought it near");
            Assert.That(empty.Held, Is.Null);
        }

        [Test]
        public void A_part_made_for_a_step_starts_switched_on_where_asked()
        {
            Interactions.MakeHoldable(_key);
            Interactions.MakeTarget(_lock, _key);
            Interactions.AddClip(_root, _unlock);
            Assert.That(Interactions.AddSendHome(_key, startEnabled: true).enabled, Is.True);
            Assert.That(Interactions.AddSelectNear(_lock, startEnabled: true).enabled, Is.True);
            Assert.That(Interactions.AddClipTrigger(_lock, _root, "Unlock", startEnabled: true).enabled, Is.True);
        }

        [Test]
        public void A_part_switched_off_once_its_holdable_is_taken_is_wired_as_planned_once_and_undone()
        {
            var arrow = new GameObject("ARROW").AddComponent<Light>();  // any component a step switches on
            arrow.transform.SetParent(_key.transform, false);
            void Run(OpContext ctx)
            {
                Interactions.MakeHoldable(_key, ctx);
                Interactions.DisableOnTake(_key, arrow, ctx);
                Interactions.DisableOnTake(_key, arrow, ctx);  // a second step's ask
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            Assert.That(_key.GetComponent<Holdable>() == null, "a dry run makes nothing");
            OpContext.RunMenu("Switch off once taken", ctx =>
            {
                Run(ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Holdable key = _key.GetComponent<Holdable>();
            Assert.That(key.onTaken.GetPersistentEventCount(), Is.EqualTo(1), "wired once");
            var again = new OpContext(dryRun: true);
            Run(again);
            Assert.That(again.Changes, Is.Empty, "a second run changes nothing");

            key.onTaken.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            var hand = Interactions.AddHand(Child("HAND", new Vector3(0, 2, 0)), new OpContext());
            hand.Take(key);
            Assert.That(arrow.enabled, Is.False, "taken: the part goes off");
            Undo.PerformUndo();
            Assert.That(_key.GetComponent<Holdable>() == null, "one Ctrl+Z took it away");
            Assert.That(Interactions.DisableOnTake(_lock, arrow, new OpContext()), Is.False, "a part no one takes gets nothing");
        }

        [Test]
        public void Two_same_named_parts_switched_off_on_take_and_two_same_named_tools_required_are_each_planned_apart()
        {
            // The plan named each by its bare name, which a dry run took for the one it had planned already.
            Light Arrow(GameObject under)
            {
                var made = new GameObject("ARROW").AddComponent<Light>();
                made.transform.SetParent(under.transform, false);
                return made;
            }
            Light near = Arrow(_key), far = Arrow(_lock);
            GameObject left = Child("LEFT", Vector3.zero), right = Child("RIGHT", Vector3.zero);
            GameObject spanner = new GameObject("SPANNER"), other = new GameObject("SPANNER");
            spanner.transform.SetParent(left.transform, false);
            other.transform.SetParent(right.transform, false);
            void Run(OpContext ctx)
            {
                Interactions.MakeHoldable(_key, ctx);
                Interactions.DisableOnTake(_key, near, ctx);
                Interactions.DisableOnTake(_key, far, ctx);
                Interactions.MakeHoldable(spanner, ctx);
                Interactions.MakeHoldable(other, ctx);
                Interactions.MakeTarget(_lock, spanner, ctx);
                Interactions.MakeTarget(_lock, other, ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            var live = new OpContext();
            Run(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(dry.Changes.Select(c => c.action), Has.Some.EqualTo("switch 'delivery/LOCK/ARROW' off once it is taken"));
            Assert.That(dry.Changes.Select(c => c.action), Has.Some.EqualTo("require 'delivery/RIGHT/SPANNER' in hand"));
        }

        [Test]
        public void A_part_only_planned_is_switched_off_on_take_as_its_live_run_wires_it()
        {
            Interactions.MakeHoldable(_key);
            var dry = new OpContext(dryRun: true);
            Assert.That(Interactions.DisableOnTake(_key, new PlannedObject("ARROW", _key.transform), dry), Is.True);
            Assert.That(Interactions.DisableOnTake(_key, new PlannedObject("ARROW", _key.transform), dry), Is.False, "planned once");
            var arrow = new GameObject("ARROW").AddComponent<Light>();
            arrow.transform.SetParent(_key.transform, false);
            var live = new OpContext();
            Interactions.DisableOnTake(_key, arrow, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(Interactions.DisableOnTake(_key, new PlannedObject("ARROW", _key.transform), new OpContext()), Is.False, "nothing in a live run");
        }

        [Test]
        public void A_holdable_is_one_there_or_one_this_dry_run_planned()
        {
            var dry = new OpContext(dryRun: true);
            Assert.That(Interactions.IsHoldable(_key, dry), Is.False);
            Interactions.MakeHoldable(_key, dry);
            Assert.That(Interactions.IsHoldable(_key, dry) && !Interactions.IsHoldable(_key), Is.True, "planned: holdable to the dry run alone");
            Interactions.MakeHoldable(_key);
            Assert.That(Interactions.IsHoldable(_key), Is.True);
        }

        [Test]
        public void A_meshs_collider_is_convex_where_asked()
        {
            var geo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(geo.GetComponent<Collider>());
            geo.transform.SetParent(_lock.transform, false);
            Assert.That(Interactions.AddColliders(_lock, convex: true), Is.EqualTo(1));
            Assert.That(geo.GetComponent<MeshCollider>().convex, Is.True);
        }

        [Test]
        public void A_clips_window_is_read_by_the_rules_given()
        {
            // A part set in place over 0.2 s, then moved slowly: a pop to rules that allow it that long.
            var take = new AnimationClip { name = "Take" };
            take.SetCurve("KEY", typeof(Transform), "localPosition.x", Reduced((0f, 0f), (0.2f, 1f), (2f, 1.5f), (3f, 1.5f)));
            Assert.That(Interactions.Window(take, new[] { "KEY" }).x, Is.EqualTo(0f), "by default the 0.2 s change is motion");
            var rules = new Interactions.WindowRules { popSeconds = 0.25f };
            Assert.That(Interactions.Window(take, new[] { "KEY" }, rules).x, Is.EqualTo(0.2f).Within(1e-4f), "a pop to rules that allow it: in place from there");
            Interactions.MakeHoldable(_key);
            ClipPlayer player = Interactions.AddClip(_root, take, rules);
            Assert.That(player.Find("Take").from, Is.EqualTo(0.2f).Within(1e-4f), "the clip added plays by them");
        }

        [Test]
        public void A_user_only_planned_is_equipped_as_its_live_run_equips_the_camera_made()
        {
            using (Interactions.OnlyInputs(new MarkerInput()))
            {
                var planned = new PlannedObject("EYES");
                var dry = new OpContext(dryRun: true);
                Assert.That(Interactions.EquipUser(planned, Interactions.HeldNearClip, dry), Is.Null, "the scene has no hand: one is planned");
                Interactions.EquipUser(planned, Interactions.HeldNearClip, dry);  // planned once
                var eyes = new GameObject("EYES", typeof(Camera));
                try
                {
                    var live = new OpContext();
                    Interactions.EquipUser(eyes.GetComponent<Camera>(), Interactions.HandInView, Interactions.HeldNearClip, live);
                    Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                    Assert.That(dry.Changes.Select(c => c.target), Is.EqualTo(new[] { "EYES/HAND", "EYES/Marker", "EYES" }), "the hand, each input, the near clip");
                    Assert.That(Interactions.EquipUser(planned, Interactions.HeldNearClip, new OpContext()), Is.Not.Null, "the scene's hand; nothing planned in a live run");
                }
                finally
                {
                    Object.DestroyImmediate(eyes);
                }
            }
            var made = new GameObject("CAMERA", typeof(Camera));
            Assert.That(made.GetComponent<Camera>().nearClipPlane, Is.EqualTo(Interactions.NewCameraNearClip), "a planned camera sees as a new one does");
            Object.DestroyImmediate(made);
        }

        [Test]
        public void A_user_equipped_sees_what_the_hand_holds_its_near_clip_brought_in_as_planned_once()
        {
            var eyes = new GameObject("EYES", typeof(Camera));
            try
            {
                Camera camera = eyes.GetComponent<Camera>();
                camera.nearClipPlane = 0.3f;
                using (Interactions.OnlyInputs())
                {
                    var dry = new OpContext(dryRun: true);
                    Interactions.EquipUser(camera, Interactions.HandInView, Interactions.HeldNearClip, dry);
                    Assert.That(dry.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(new[] { ("create", "EYES/HAND"), ("change", "EYES") }));
                    var live = new OpContext();
                    Interactions.EquipUser(camera, Interactions.HandInView, Interactions.HeldNearClip, live);
                    Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                    Assert.That(camera.nearClipPlane, Is.EqualTo(Interactions.HeldNearClip));
                    var again = new OpContext(dryRun: true);
                    Interactions.EquipUser(camera, Interactions.HandInView, Interactions.HeldNearClip, again);
                    Assert.That(again.Changes, Is.Empty);
                    camera.nearClipPlane = 0.01f;
                    Interactions.EquipUser(camera, Interactions.HandInView, Interactions.HeldNearClip);
                    Assert.That(camera.nearClipPlane, Is.EqualTo(0.01f), "nearer already: left as it is");
                }
            }
            finally
            {
                Object.DestroyImmediate(eyes);
            }
        }
    }
}
