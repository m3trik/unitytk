using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary>
    /// <see cref="Hand"/> and <see cref="Holdable"/>: one thing held at the anchor, the rest at rest, and every
    /// way a holdable goes (into the hand, to its work, into a clip's pose, back) one eased move of its whole
    /// shape from where it is to where it belongs.
    /// </summary>
    public class HandTests
    {
        GameObject _scene;
        Hand _hand;
        Holdable _key;
        Holdable _wrench;

        [SetUp]
        public void SetUp()
        {
            _scene = new GameObject("scene");
            var anchor = new GameObject("anchor").transform;
            anchor.SetParent(_scene.transform);
            anchor.position = new Vector3(0, 1.5f, 0);
            _hand = new GameObject("hand").AddComponent<Hand>();
            _hand.transform.SetParent(_scene.transform);
            _hand.anchor = anchor;
            _key = Thing("KEY", new Vector3(1, 0, 0));
            _wrench = Thing("WRENCH", new Vector3(2, 0, 0));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_scene);

        Holdable Thing(string name, Vector3 at)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_scene.transform);
            node.transform.localPosition = at;
            var holdable = node.AddComponent<Holdable>();
            holdable.CaptureHome();
            return holdable;
        }

        // The move it is on, done.
        static void Arrive(Holdable thing)
        {
            thing.Tick(0f);
            thing.Tick(thing.TravelFor + 0.01f);
        }

        [Test]
        public void A_taken_thing_moves_to_the_anchor_and_goes_back_to_its_rest()
        {
            int taken = 0, returned = 0;
            _key.onTaken.AddListener(() => taken++);
            _key.onReturned.AddListener(() => returned++);
            _key.TakeBy(_hand);
            Assert.That(_hand.Holds(_key) && _key.Holder == _hand);
            Assert.That(_key.Travelling && _key.transform.localPosition == new Vector3(1, 0, 0), "it leaves its rest, eased: no jump into the hand");
            _key.Tick(0f);
            _key.Tick(_key.TravelFor / 2f);
            Assert.That(Vector3.Distance(_key.transform.position, (_hand.Anchor.position + new Vector3(1, 0, 0)) / 2f), Is.LessThan(1e-3f),
                        "half way, on the straight line between");
            Arrive(_key);
            Assert.That(_key.transform.position, Is.EqualTo(_hand.Anchor.position));
            _hand.Release();
            Assert.That(_hand.Held == null && !_key.IsHeld);
            Assert.That(_key.Travelling && _key.transform.position == _hand.Anchor.position, "and leaves the hand the same way");
            Arrive(_key);
            Assert.That(_key.transform.localPosition, Is.EqualTo(new Vector3(1, 0, 0)));
            Assert.That((taken, returned), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Its_move_takes_its_travel_seconds_paced_by_how_far_it_goes_or_none()
        {
            _key.TakeBy(_hand);  // 1.8 m to the hand
            _key.Tick(0f);
            Assert.That(_key.TravelFor, Is.EqualTo(_key.travelSeconds * 2f).Within(1e-4f), "a long way takes longer, twice at most");
            Arrive(_key);
            _hand.Release();
            _key.paced = false;
            _hand.Take(_key);
            _key.Tick(0f);
            Assert.That(_key.TravelFor, Is.EqualTo(_key.travelSeconds), "unpaced: its travel seconds, however far");
            Arrive(_key);
            _key.travelSeconds = 0f;
            _hand.Release();
            Assert.That(!_key.Travelling && _key.transform.localPosition == new Vector3(1, 0, 0), "no travel time: there at once");
        }

        [Test]
        public void A_held_thing_switches_its_colliders_off_and_back_on_as_it_goes_back()
        {
            var on = _key.gameObject.AddComponent<BoxCollider>();
            var child = new GameObject("KEY_GEO");
            child.transform.SetParent(_key.transform, false);
            var off = child.AddComponent<SphereCollider>();
            off.enabled = false;

            _hand.Take(_key);
            Assert.That(on.enabled, Is.False, "held, it stands between the user and what they aim at past it no more");
            var other = new GameObject("other").AddComponent<Hand>();
            other.transform.SetParent(_scene.transform);
            other.Take(_key);
            Assert.That(on.enabled, Is.False, "passed to another hand, still held");
            other.Release();
            Assert.That(on.enabled, Is.True, "back at rest, a ray finds it again");
            Assert.That(off.enabled, Is.False, "one that was off stays off");
        }

        [Test]
        public void Taking_another_puts_the_first_back_and_another_hand_lets_go()
        {
            _hand.Take(_key);
            _hand.Take(_wrench);
            Assert.That(_hand.Held, Is.SameAs(_wrench));
            Arrive(_key);
            Assert.That(!_key.IsHeld && _key.transform.localPosition == new Vector3(1, 0, 0));
            var other = new GameObject("other").AddComponent<Hand>();
            other.transform.SetParent(_scene.transform);
            other.Take(_wrench);
            Assert.That(_hand.Held == null && other.Holds(_wrench));
        }

        [Test]
        public void A_lent_thing_keeps_the_clips_pose_until_it_is_reclaimed()
        {
            _hand.Take(_key);
            Arrive(_key);
            _key.Lend();
            Arrive(_key);
            _key.transform.position = new Vector3(9, 9, 9);  // the clip's pose
            _hand.Release();
            _key.Tick(0.1f);
            Assert.That(_key.transform.position, Is.EqualTo(new Vector3(9, 9, 9)), "let go while lent: the clip still moves it");
            _key.Reclaim();
            Assert.That(_key.Travelling && _key.transform.position == new Vector3(9, 9, 9), "it leaves the clip's pose, eased");
            _key.Tick(0f);
            _key.Tick(_key.TravelFor / 2f);
            Vector3 half = _key.transform.localPosition;
            Assert.That(half.x > 1f && half.x < 9f, "half way home");
            Arrive(_key);
            Assert.That(_key.transform.localPosition, Is.EqualTo(new Vector3(1, 0, 0)));
        }

        [Test]
        public void A_lent_thing_moves_from_the_hand_into_the_clips_pose()
        {
            _hand.Take(_key);
            Arrive(_key);
            _key.Lend();
            Assert.That(_key.Travelling, "on its way: a clip that waits for it stands at its first pose meanwhile");
            _key.transform.position = new Vector3(9, 1.5f, 0);  // the clip poses it each frame
            _key.Tick(0f);
            _key.transform.position = new Vector3(9, 1.5f, 0);
            _key.Tick(_key.TravelFor / 2f);
            Assert.That(_key.transform.position.x, Is.EqualTo(4.5f).Within(1e-3f), "half way from the hand");
            _key.transform.position = new Vector3(9, 1.5f, 0);
            _key.Tick(_key.TravelFor);
            Assert.That(!_key.Travelling && _key.transform.position == new Vector3(9, 1.5f, 0), "then the clip's own");
        }

        [Test]
        public void A_thing_a_clip_puts_in_place_stays_there_out_of_the_hand()
        {
            // A filter fitted into its slot came back to the hand as its take ended, and was carried for the rest of the task.
            _hand.Take(_key);
            Arrive(_key);
            _key.Lend();
            var fitted = new Vector3(9, 1.5f, 0);
            _key.transform.position = fitted;  // the clip's last pose
            Arrive(_key);
            _key.transform.position = fitted;
            _key.Leave();                      // the clip ended, and puts it in place for good
            _key.Tick(_key.TravelFor * 3f);
            Assert.That(_key.Holder, Is.Null, "the hand let go");
            Assert.That(_hand.Held, Is.Null);
            Assert.That(_key.IsLent, Is.False);
            Assert.That(Vector3.Distance(_key.transform.position, fitted), Is.LessThan(1e-4f), "where the clip left it");
            _key.ReturnHome();
            _key.Tick(_key.TravelFor * 3f);
            Assert.That(Vector3.Distance(_key.transform.position, fitted), Is.LessThan(1e-4f), "and that is where it rests from then on");
        }

        [Test]
        public void A_clip_that_follows_another_at_once_poses_what_it_keys_and_leaves_the_rest_in_its_own_shape()
        {
            var bit = new GameObject("BIT").transform;
            bit.SetParent(_key.transform, false);
            _hand.Take(_key);
            Arrive(_key);
            var inLock = new Vector3(0, 1, 2);
            _key.Lend(atRest: true);
            void Turned()                                   // the first clip places the bit and turns it
            {
                bit.localPosition = inLock;
                bit.localRotation = Quaternion.Euler(0, 0, 90);
            }
            Turned();
            Arrive(_key);
            Turned();
            _key.Tick(0f);
            _key.Reclaim();                                 // it ended, the bit turned in the lock
            _key.Lend(atRest: true);                        // and the next began before the tool was back
            void Placed() => bit.localPosition = inLock;    // it keys the bit's place, the same one, never its turn
            Placed();
            _key.Tick(0f);
            Assert.That(Quaternion.Angle(bit.localRotation, Quaternion.Euler(0, 0, 90)), Is.LessThan(0.1f), "it starts as the first clip left it: no pop");
            Placed();
            _key.Tick(_key.TravelFor * 3f);
            Assert.That(!_key.Travelling && bit.localPosition == inLock, "where the clip puts it, not back in its own place");
            Assert.That(Quaternion.Angle(bit.localRotation, Quaternion.identity), Is.LessThan(0.1f), "its own turn: the clip does not key it");
        }

        [Test]
        public void A_tools_parts_blend_into_a_clips_shape_and_back_into_their_own()
        {
            // A ring of keys: the clip keys one key (its bit), not the ring, from where the ring rests (a table).
            var bit = new GameObject("BIT").transform;
            bit.SetParent(_key.transform, false);
            bit.localPosition = new Vector3(0, 0, 0.1f);   // its own shape: the bit 10 cm ahead of the ring
            var tag = new GameObject("TAG").transform;      // a part the clip does not key
            tag.SetParent(_key.transform, false);
            tag.localPosition = new Vector3(0.05f, 0, 0);
            _hand.Take(_key);
            Arrive(_key);
            Vector3 held = bit.position;
            Assert.That(held, Is.EqualTo(_hand.Anchor.position + new Vector3(0, 0, 0.1f)), "held: its own shape at the hand");

            _key.Lend(atRest: true);
            var inLock = new Vector3(0, 1, 2);              // where the clip puts the bit: a lock, from the ring's rest
            void Clip() => bit.localPosition = inLock;      // the clip writes its pose each frame
            Clip();
            _key.Tick(0f);
            Assert.That(Vector3.Distance(bit.position, held), Is.LessThan(1e-4f), "it starts where the hand held it: no pop to the table, none to the lock");
            Clip();
            _key.Tick(_key.TravelFor / 2f);
            Vector3 atLock = _scene.transform.TransformPoint(new Vector3(1, 0, 0) + inLock);
            Assert.That(Vector3.Distance(bit.position, (held + atLock) / 2f), Is.LessThan(1e-3f), "half way, straight between the hand and the lock");
            Clip();
            _key.Tick(_key.TravelFor);
            Assert.That(!_key.Travelling && _key.transform.localPosition == new Vector3(1, 0, 0), "the ring where it rests: the clip was made from there");
            Assert.That(Vector3.Distance(bit.position, atLock), Is.LessThan(1e-4f), "the bit in the lock");
            Assert.That(tag.localPosition, Is.EqualTo(new Vector3(0.05f, 0, 0)), "a part the clip does not key keeps its own shape");

            _key.Reclaim();                                 // the clip ended; its last pose still stands on the bit
            Clip();
            _key.Tick(0f);
            Assert.That(Vector3.Distance(bit.position, atLock), Is.LessThan(1e-4f), "it leaves from the lock");
            Clip();
            _key.Tick(_key.TravelFor / 2f);
            Assert.That(Vector3.Distance(bit.position, (held + atLock) / 2f), Is.LessThan(1e-3f), "half way back, straight to the hand: never by the table");
            Clip();
            _key.Tick(_key.TravelFor);
            Assert.That(_key.transform.position, Is.EqualTo(_hand.Anchor.position), "back in hand");
            Assert.That(Vector3.Distance(bit.position, held), Is.LessThan(1e-4f), "in its own shape again");
            Clip();                                         // the clip's layer keeps writing its last pose
            _key.Tick(0.02f);
            Assert.That(bit.localPosition, Is.EqualTo(new Vector3(0, 0, 0.1f)), "and held in it, whatever the clip left");
        }

        [Test]
        public void A_tool_works_at_a_target_tip_to_pivot_its_axes_corrected_and_rides_it()
        {
            // Its tip in the world: metres along its own axes, whatever its scale.
            Vector3 Tip() => _key.transform.position + _key.transform.rotation * _key.tipPosition;
            _key.transform.localScale = Vector3.one * 0.01f;  // a delivery in centimetres
            var screw = new GameObject("SCREW").transform;
            screw.SetParent(_scene.transform);
            screw.SetPositionAndRotation(new Vector3(0, 1, 2), Quaternion.Euler(0, 90, 0));
            _key.tipPosition = new Vector3(0, 0, 0.1f);  // its tip 10 cm along its own +Z
            _key.tipRotation = new Vector3(0, 0, 45);
            _hand.Take(_key);
            Arrive(_key);
            _key.WorkAt(screw);
            Assert.That(_key.Travelling && _key.transform.position == _hand.Anchor.position, "it leaves the hand, eased");
            Arrive(_key);
            Assert.That(Vector3.Distance(Tip(), screw.position), Is.LessThan(1e-4f), "its tip on the screw's pivot");
            Assert.That(Quaternion.Angle(_key.transform.rotation * Quaternion.Euler(_key.tipRotation), screw.rotation), Is.LessThan(0.01f), "its corrected axes on the screw's");
            screw.position += Vector3.forward * 0.05f;
            screw.Rotate(0, 0, 90f);
            _key.Tick(0.02f);
            Assert.That(Vector3.Distance(Tip(), screw.position), Is.LessThan(1e-4f), "it rides the screw out");
            _key.StopWork();
            Arrive(_key);
            Assert.That(_key.transform.position, Is.EqualTo(_hand.Anchor.position), "back in hand");
        }

        [Test]
        public void A_tool_goes_straight_to_its_work_or_with_an_approach_in_along_its_tip_and_out_the_same_way()
        {
            Vector3 Tip() => _key.transform.position + _key.transform.rotation * _key.tipPosition;
            var screw = new GameObject("SCREW").transform;
            screw.SetParent(_scene.transform);
            screw.SetPositionAndRotation(new Vector3(0, 1, 2), Quaternion.Euler(0, 90, 0));
            _key.tipPosition = new Vector3(0, 0, 0.1f);
            Vector3 axis = screw.rotation * Vector3.forward;  // its tip's axis, lined up on the screw's
            float OffAxis() => Vector3.Cross(Tip() - screw.position, axis).magnitude;
            _hand.Take(_key);
            Arrive(_key);

            Assert.That(_key.approach, Is.EqualTo(0f), "by default it goes straight there");
            Vector3 from = _key.transform.position;
            _key.WorkAt(screw);
            _key.WorkPose(screw, out Vector3 at, out _);
            _key.Tick(0f);
            _key.Tick(_key.TravelFor / 2f);
            Assert.That(Vector3.Distance(_key.transform.position, (from + at) / 2f), Is.LessThan(1e-3f), "half way, on the straight line");
            Arrive(_key);
            _key.StopWork();
            Arrive(_key);

            _key.approach = 0.05f;
            _key.WorkAt(screw);
            _key.Tick(0f);
            _key.Tick(_key.TravelFor * 0.9f);
            Assert.That(OffAxis(), Is.LessThan(1e-4f), "going in: its tip on the screw's axis");
            Assert.That(Vector3.Dot(Tip() - screw.position, axis), Is.LessThan(0f).And.GreaterThan(-_key.approach),
                        "short of the screw, on the side it comes in from");
            Arrive(_key);
            Assert.That(Vector3.Distance(Tip(), screw.position), Is.LessThan(1e-4f), "in");
            _key.StopWork();
            _key.Tick(0f);
            _key.Tick(_key.TravelFor * 0.1f);
            Assert.That(OffAxis(), Is.LessThan(1e-4f), "coming out: back along the same axis first");
            Assert.That(Vector3.Dot(Tip() - screw.position, axis), Is.LessThan(0f));
            Arrive(_key);
            Assert.That(_key.transform.position, Is.EqualTo(_hand.Anchor.position), "back in hand");
        }

        // What a player calls as the scene starts (Edit Mode calls none of it).
        static void Call(Object on, string message)
            => on.GetType().GetMethod(message, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(on, null);

        [Test]
        public void A_holdable_added_by_hand_rests_where_it_stood_as_the_scene_starts()
        {
            var node = new GameObject("PART");
            node.transform.SetParent(_scene.transform);
            node.transform.localPosition = new Vector3(3f, 0f, 1f);
            node.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            var part = node.AddComponent<Holdable>();  // no CaptureHome: added as a person adds it
            Call(part, "Awake");
            part.Tick(0.1f);
            Assert.That(node.transform.localPosition, Is.EqualTo(new Vector3(3f, 0f, 1f)), "no snap to its parent's origin");
            _hand.Take(part);
            Arrive(part);
            _hand.Release();
            Arrive(part);
            Assert.That(node.transform.localPosition, Is.EqualTo(new Vector3(3f, 0f, 1f)), "let go, it goes back where it stood");
            Assert.That(Quaternion.Angle(node.transform.localRotation, Quaternion.Euler(0f, 30f, 0f)), Is.LessThan(0.01f));
        }

        [Test]
        public void The_scenes_hand_is_its_first_switched_on_one()
        {
            Assert.That(Hand.InScene(), Is.SameAs(_hand));
            var other = new GameObject("other").AddComponent<Hand>();
            other.transform.SetParent(_scene.transform);
            _hand.enabled = false;
            Assert.That(Hand.InScene(), Is.SameAs(other), "a hand switched off is no one's");
            other.gameObject.SetActive(false);
            Assert.That(Hand.InScene(), Is.Null, "none switched on: none");
        }

        [Test]
        public void Its_pace_is_its_own_distance_and_limits()
        {
            _key.paceDistance = 0.9f;                 // some 1.8 m to the hand: about twice its distance
            _key.paceLimits = new Vector2(0.5f, 3f);
            float far = Vector3.Distance(_hand.Anchor.position, _key.transform.position);
            _key.TakeBy(_hand);
            _key.Tick(0f);
            Assert.That(_key.TravelFor, Is.EqualTo(_key.travelSeconds * far / 0.9f).Within(1e-4f), "its distance's share of its time");
            Arrive(_key);
            _hand.Release();
            Arrive(_key);
            _key.paceLimits = new Vector2(0.5f, 1.5f);
            _key.TakeBy(_hand);
            _key.Tick(0f);
            Assert.That(_key.TravelFor, Is.EqualTo(_key.travelSeconds * 1.5f).Within(1e-4f), "no more than its most");
        }
    }
}
