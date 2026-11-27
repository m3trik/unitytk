using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary>
    /// <see cref="TargetCue"/>: a cue shows while one of its targets can still be acted on. The real task showed every
    /// arrow of a step at once: at the keys on the table and at both locks, and each lock's arrow stayed over the key
    /// as it turned.
    /// </summary>
    public class TargetCueTests
    {
        GameObject _scene;
        Hand _hand;
        Holdable _keys;
        Target _upper, _lower;

        [SetUp]
        public void SetUp()
        {
            _scene = new GameObject("scene");
            _hand = Child("hand").AddComponent<Hand>();
            _keys = Child("KEYS").AddComponent<Holdable>();
            _keys.CaptureHome();
            _upper = Child("LOCK_UPPER").AddComponent<Target>();
            _upper.requires = _keys;
            _lower = Child("LOCK_LOWER").AddComponent<Target>();
            _lower.requires = _keys;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_scene);

        GameObject Child(string name)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_scene.transform);
            return node;
        }

        // A cue (any behaviour: a light stands in for an arrow) at what, switched on as a step does (Edit Mode calls no
        // OnEnable or Update: Watch, Rest and Refresh are what they do).
        (TargetCue shows, Behaviour cue) Cue(string name, params Target[] what)
        {
            GameObject node = Child(name);
            Behaviour cue = node.AddComponent<Light>();
            cue.enabled = false;
            var shows = node.AddComponent<TargetCue>();
            shows.cue = cue;
            shows.targets.AddRange(what.Select(each => each.gameObject));
            shows.Watch();
            return (shows, cue);
        }

        [Test]
        public void A_cue_two_targets_share_hides_while_a_take_one_of_them_set_off_plays_and_comes_back_for_the_other()
        {
            // The real task's one arrow at two screws stayed over the screwdriver as it turned the first one out: the
            // second was still to do, so the arrow was still wanted.
            var turn = new AnimationClip { name = "Turn" };
            turn.SetCurve("LOCK_UPPER", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0, 0, 1, 90));
            var ctx = new OpContext();
            ClipPlayer player = Interactions.AddClip(_scene, turn, ctx);
            Interactions.AddClipTrigger(_upper.gameObject, _scene, "Turn", ctx: ctx).enabled = true;
            InteractionsTests.Live(_upper);
            var (both, arrow) = Cue("arrow", _upper, _lower);
            _hand.Take(_keys);
            both.Refresh();
            Assert.That(arrow.enabled, "the keys are in hand: the locks are pointed out");

            Assert.That(_upper.Select(_hand), Is.True);
            Assert.That(player.IsPlaying("Turn"));
            both.Refresh();
            Assert.That(arrow.enabled, Is.False, "its take plays: nothing is pointed out over it");
            player.Finish();
            both.Refresh();
            Assert.That(arrow.enabled, "the take is done: the other lock is still to do");
            Assert.That(_lower.Select(_hand), Is.True);
            Assert.That(arrow.enabled, Is.False, "both done");

            // For the whole step, as its setting says: it stays through the take too.
            var (stays, steady) = Cue("steady", _upper);
            stays.goesOnceActed = false;
            stays.Watch();
            Assert.That(steady.enabled);
            Assert.That(_upper.Select(_hand), Is.True);
            stays.Refresh();
            Assert.That(player.IsPlaying("Turn") && steady.enabled, "a cue set to show for its whole step shows through its take");
            Object.DestroyImmediate(turn);
        }

        [Test]
        public void A_cue_at_a_lock_waits_for_the_keys_and_goes_as_its_own_lock_is_turned()
        {
            var (upper, upperArrow) = Cue("arrow upper", _upper);
            var (lower, lowerArrow) = Cue("arrow lower", _lower);
            var (both, glow) = Cue("glow", _upper, _lower);
            Assert.That(upperArrow.enabled || lowerArrow.enabled || glow.enabled, Is.False, "nothing to do at the locks yet: the keys are on the table");

            _hand.Take(_keys);
            foreach (TargetCue each in new[] { upper, lower, both }) each.Refresh();
            Assert.That(upperArrow.enabled && lowerArrow.enabled && glow.enabled, "the keys are in hand: the locks are pointed out");

            Assert.That(_upper.Select(_hand), Is.True);
            Assert.That(upperArrow.enabled, Is.False, "its lock is turned: its arrow is gone at once, never over the key at work");
            Assert.That(lowerArrow.enabled && glow.enabled, "the other lock is still to do");
            Assert.That(_lower.Select(_hand), Is.True);
            Assert.That(lowerArrow.enabled || glow.enabled, Is.False, "both done");
        }

        [Test]
        public void A_cue_at_what_is_taken_goes_as_it_is_taken_and_a_selection_refused_changes_nothing()
        {
            Target take = _keys.gameObject.AddComponent<Target>();
            take.onSelected.AddListener(_keys.TakeBy);
            var (_, arrow) = Cue("arrow keys", take);
            Assert.That(arrow.enabled, "it needs nothing in hand: shown from the start");
            var (_, lockArrow) = Cue("arrow lock", _upper);
            Assert.That(_upper.Select(_hand), Is.False, "refused: the keys are not in hand");
            Assert.That(lockArrow.enabled, Is.False);
            Assert.That(take.Select(_hand), Is.True);
            Assert.That(arrow.enabled, Is.False, "in hand: no arrow rides it");
        }

        [Test]
        public void A_cue_set_neither_to_wait_nor_to_go_shows_for_as_long_as_it_is_on()
        {
            var (shows, cue) = Cue("arrow", _upper);
            shows.waitsForHand = false;
            shows.goesOnceActed = false;
            shows.Refresh();
            Assert.That(cue.enabled, "before the keys are in hand");
            _hand.Take(_keys);
            _upper.Select(_hand);
            shows.Refresh();
            Assert.That(cue.enabled, "and after its target was used");
            shows.enabled = false;
            shows.Rest();
            Assert.That(cue.enabled, Is.False, "its step over, it is off");
        }

        [Test]
        public void A_target_switched_off_does_not_count_and_a_step_run_again_shows_its_cue_again()
        {
            _hand.Take(_keys);
            var (shows, cue) = Cue("arrow", _upper);
            Assert.That(cue.enabled);
            _upper.enabled = false;  // another step's: nothing to do here now
            shows.Refresh();
            Assert.That(cue.enabled, Is.False);
            _upper.enabled = true;
            shows.Refresh();
            _upper.Select(_hand);
            Assert.That(cue.enabled, Is.False, "done");
            shows.Rest();
            shows.Watch();  // the step runs again (Back, or the task restarted)
            Assert.That(cue.enabled, "to do again");
            shows.Rest();
            _upper.Select(_hand);
            Assert.That(cue.enabled, Is.False, "off, it listens to nothing");
        }

        [Test]
        public void A_cues_switch_is_planned_made_once_given_what_it_lacks_and_undone()
        {
            Behaviour arrow = Child("arrow").AddComponent<Light>();
            void Run(OpContext ctx) => Interactions.AddTargetCue(arrow.gameObject, arrow, new[] { _upper.gameObject }, ctx: ctx);
            var dry = new OpContext(dryRun: true);
            Run(dry);
            Assert.That(arrow.GetComponent<TargetCue>(), Is.Null, "a dry run makes nothing");
            OpContext.RunMenu("Cue", ctx =>
            {
                Run(ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            TargetCue made = arrow.GetComponent<TargetCue>();
            Assert.That(made.enabled, Is.False, "made off, for a step to enable");
            Assert.That((made.cue, made.targets.Single(), made.waitsForHand, made.goesOnceActed), Is.EqualTo((arrow, _upper.gameObject, true, true)));
            var again = new OpContext(dryRun: true);
            Run(again);
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");

            Assert.That(Interactions.AddTargetCue(arrow.gameObject, arrow, new[] { _upper.gameObject, _lower.gameObject }, false, false), Is.SameAs(made));
            Assert.That(made.targets, Is.EqualTo(new[] { _upper.gameObject, _lower.gameObject }), "given the target it lacked");
            Assert.That(made.waitsForHand || made.goesOnceActed, Is.False, "and set as asked");
        }

        [Test]
        public void An_object_that_is_no_target_has_nothing_to_wait_for_and_one_made_a_target_later_is_read_as_it_comes_on()
        {
            GameObject part = Child("PART");
            Behaviour arrow = Child("arrow").AddComponent<Light>();
            arrow.enabled = false;
            // Its switch is made before anything makes the part a target (a row built before the row that has the user act on it).
            TargetCue shows = Interactions.AddTargetCue(arrow.gameObject, arrow, new[] { part }, ctx: new OpContext());
            shows.enabled = true;
            shows.Watch();
            Assert.That(arrow.enabled, "no target: it shows for as long as it is on");
            shows.Rest();
            Target made = part.AddComponent<Target>();
            made.requires = _keys;
            shows.Watch();
            Assert.That(arrow.enabled, Is.False, "a target now, read as it comes on: it waits for the keys");
            _hand.Take(_keys);
            shows.Refresh();
            Assert.That(arrow.enabled);
        }
    }
}
