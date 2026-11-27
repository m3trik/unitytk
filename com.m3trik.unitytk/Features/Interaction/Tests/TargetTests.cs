using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary><see cref="Target"/>: a selection accepted or refused by what the hand holds.</summary>
    public class TargetTests
    {
        GameObject _scene;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_scene);

        // What a player calls as a target is switched on or off (Edit Mode calls none of them).
        static void Call(Object on, string message)
            => on.GetType().GetMethod(message, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(on, null);

        [Test]
        public void A_target_off_takes_no_clicks_on_its_colliders_and_leaves_a_target_inside_it_its_own()
        {
            _scene = GameObject.CreatePrimitive(PrimitiveType.Cube);  // a door, its collider its own
            _scene.name = "DOOR";
            var door = _scene.AddComponent<Target>();
            var screw = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            screw.transform.SetParent(_scene.transform, false);
            screw.AddComponent<Target>();
            var tool = GameObject.CreatePrimitive(PrimitiveType.Capsule).AddComponent<Holdable>();
            tool.transform.SetParent(_scene.transform, false);
            var toolTarget = tool.gameObject.AddComponent<Target>();
            var hand = new GameObject("hand").AddComponent<Hand>();
            hand.transform.SetParent(_scene.transform);

            door.enabled = false;
            Call(door, "OnDisable");
            Assert.That(_scene.GetComponent<Collider>().enabled, Is.False, "a door not in play lets clicks through to what is behind it");
            Assert.That(screw.GetComponent<Collider>().enabled, Is.True, "the screw on it is the screw's target's");
            door.enabled = true;
            Call(door, "OnEnable");
            Assert.That(_scene.GetComponent<Collider>().enabled, Is.True);

            hand.Take(tool);
            Call(toolTarget, "OnEnable");
            Assert.That(tool.GetComponent<Collider>().enabled, Is.False, "held, it stays off: its holdable's");
            toolTarget.enabled = false;
            Call(toolTarget, "OnDisable");
            tool.ReturnHome();
            Assert.That(tool.GetComponent<Collider>().enabled, Is.False, "sent home with its target off, it stays off");
        }

        [Test]
        public void A_target_requiring_a_tool_takes_only_a_hand_that_holds_it()
        {
            _scene = new GameObject("scene");
            var hand = new GameObject("hand").AddComponent<Hand>();
            hand.transform.SetParent(_scene.transform);
            var wrench = new GameObject("WRENCH").AddComponent<Holdable>();
            wrench.transform.SetParent(_scene.transform);
            var nut = new GameObject("NUT").AddComponent<Target>();
            nut.transform.SetParent(_scene.transform);
            nut.requires = wrench;
            Hand selectedBy = null, refusedBy = null;
            nut.onSelected.AddListener(h => selectedBy = h);
            nut.onRefused.AddListener(h => refusedBy = h);

            Assert.That(nut.Select(hand), Is.False);
            Assert.That((selectedBy, refusedBy), Is.EqualTo(((Hand)null, hand)));
            hand.Take(wrench);
            Assert.That(nut.Select(hand), Is.True);
            Assert.That(selectedBy, Is.SameAs(hand));
            Assert.That(nut.Select(null), Is.False, "no hand holds nothing");
        }

        [Test]
        public void A_switched_off_target_takes_no_selection_and_refuses_none()
        {
            _scene = new GameObject("DOOR");
            var door = _scene.AddComponent<Target>();
            int heard = 0;
            door.onSelected.AddListener(_ => heard++);
            door.onRefused.AddListener(_ => heard++);
            door.enabled = false;
            Assert.That(door.Select(null), Is.False);
            Assert.That(heard, Is.Zero);
        }

        [Test]
        public void A_press_is_held_until_let_go_by_the_selections_rule()
        {
            _scene = new GameObject("PUMP");
            var pump = _scene.AddComponent<Target>();
            var wrench = new GameObject("WRENCH").AddComponent<Holdable>();
            wrench.transform.SetParent(_scene.transform);
            var hand = new GameObject("hand").AddComponent<Hand>();
            hand.transform.SetParent(_scene.transform);
            pump.requires = wrench;
            var heard = new System.Collections.Generic.List<string>();
            pump.onPressed.AddListener(_ => heard.Add("pressed"));
            pump.onReleased.AddListener(by => heard.Add(by == hand ? "released by hand" : "released"));
            pump.onRefused.AddListener(_ => heard.Add("refused"));

            Assert.That(pump.Press(hand) || pump.Release(), Is.False, "refused: nothing held to let go");
            hand.Take(wrench);
            Assert.That(pump.Press(hand) && pump.IsPressed, Is.True);
            Assert.That(pump.Press(hand), Is.False, "a press held takes no second one");
            Assert.That(pump.Release() && !pump.IsPressed, Is.True);
            Assert.That(heard, Is.EqualTo(new[] { "refused", "pressed", "released by hand" }));
        }

        [Test]
        public void A_press_refused_and_the_selection_ending_its_gesture_say_so_once_and_the_next_gesture_again()
        {
            _scene = new GameObject("LOCK");
            var latch = _scene.AddComponent<Target>();
            latch.requires = new GameObject("KEY").AddComponent<Holdable>();
            latch.requires.transform.SetParent(_scene.transform);
            var hand = new GameObject("hand").AddComponent<Hand>();
            hand.transform.SetParent(_scene.transform);
            int refused = 0;
            latch.onRefused.AddListener(_ => refused++);
            Assert.That(latch.Press(hand) || latch.Select(hand), Is.False);
            Assert.That(refused, Is.EqualTo(1), "one gesture, one refusal: a press and the selection that ends it");
            Assert.That(latch.Select(hand), Is.False);
            Assert.That(refused, Is.EqualTo(2), "a selection of its own says so again");
            Assert.That(latch.Press(hand) || latch.Select(hand), Is.False);
            Assert.That(refused, Is.EqualTo(3), "and so does the next gesture");
        }

        [Test]
        public void A_target_requiring_nothing_takes_any_selection()
        {
            _scene = new GameObject("DOOR");
            var door = _scene.AddComponent<Target>();
            int selected = 0;
            door.onSelected.AddListener(_ => selected++);
            Assert.That(door.Select(null) && selected == 1);
        }
    }
}
