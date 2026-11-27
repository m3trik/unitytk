using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels.Tests
{
    /// <summary>
    /// A placard: what it shows is on its document, a button per choice; a choice it offers is
    /// raised with its label, one it does not is not; it comes up in front of the eyes, facing
    /// them, turned with their heading alone; and a page takes away only what it put there.
    /// </summary>
    public class PlacardTests
    {
        GameObject _eyes;
        Placard _placard;

        [SetUp]
        public void SetUp()
        {
            _eyes = new GameObject("EYES");
            _eyes.transform.SetPositionAndRotation(new Vector3(1f, 1.6f, 2f), Quaternion.Euler(30f, 90f, 0f));
            _placard = PanelsActions.MakePlacard("PLACARD (test)");
            _placard.eyes = _eyes.transform;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_placard.gameObject);
            Object.DestroyImmediate(_eyes);
        }

        VisualElement Root => _placard.GetComponent<UIDocument>().rootVisualElement;

        [Test]
        public void Dragged_it_swings_around_the_eyes_and_later_pages_keep_the_spot_and_its_arrow_folds_it()
        {
            _placard.Show("A01", "Take the keys", new[] { "Next" });
            VisualElement frame = Root.Q("placard");
            Assert.That(Root.Q("bar") == null && Root.Q("handle") == null, "no bar drawn: it is dragged anywhere but a button");
            Assert.That(frame.ClassListContains("window--movable"), "movable: the pointer shows it");
            Label fold = frame.Q<Label>(className: "window-fold");
            Assert.That(fold != null && fold.text == "▾", "a bare arrow folds it");
            Vector3 eyes = _eyes.transform.position;
            Vector3 usual = _placard.transform.position;
            float radius = Vector3.Distance(eyes, usual);
            float pixels = radius * 20f * Mathf.Deg2Rad * Placard.DefaultPixelsPerMetre;  // the arc of 20 degrees
            _placard.Drag(new Vector2(pixels, -100f));
            Assert.That(_placard.Swing, Is.EqualTo(20f).Within(1e-3f));
            Assert.That(_placard.Lift, Is.EqualTo(0.1f).Within(1e-4f), "dragged up 100 mm");
            Vector3 flatUsual = Vector3.ProjectOnPlane(usual - eyes, Vector3.up);
            Vector3 flatNow = Vector3.ProjectOnPlane(_placard.transform.position - eyes, Vector3.up);
            Assert.That(Vector3.SignedAngle(flatUsual, flatNow, Vector3.up), Is.EqualTo(20f).Within(1e-2f), "around the eyes, to the right");
            Assert.That(Vector3.Distance(eyes, _placard.transform.position - Vector3.up * 0.1f), Is.EqualTo(radius).Within(1e-4f), "at the same radius");
            _placard.Show("A02", "Open the door", null);
            Assert.That(Vector3.SignedAngle(flatUsual, Vector3.ProjectOnPlane(_placard.transform.position - eyes, Vector3.up), Vector3.up),
                        Is.EqualTo(20f).Within(1e-2f), "the next page comes up where the user put it");

            _placard.Collapse(true);
            Assert.That(Root.ClassListContains(Placard.CollapsedClass), Is.True);
            Assert.That(fold.text, Is.EqualTo("▸"), "the arrow says it is folded");
            Assert.That(Root.Q<Label>("text").style.display.value, Is.EqualTo(DisplayStyle.None), "folded to its heading");
            _placard.Show("A03", "Close the door", new[] { "Next" });
            Assert.That(_placard.Collapsed, Is.True, "it stays folded");
            _placard.Collapse(false);
            Assert.That(Root.Q<Label>("text").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            _placard.ResetSpot();
            Assert.That(_placard.transform.position, Is.EqualTo(usual).Using(UnityEngine.TestTools.Utils.Vector3EqualityComparer.Instance));
        }

        [Test]
        public void A_pip_frames_its_part_cut_away_from_the_eyes_side_beside_the_placard_and_a_shot_shows_and_takes_it()
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.transform.localScale = Vector3.one * 0.1f;
            part.transform.position = _eyes.transform.position + new Vector3(3f, -0.4f, 0f);  // ahead (+X), 3 m
            var pip = PanelsActions.MakePip("PIP (test)");
            pip.eyes = _eyes.transform;
            pip.placard = _placard;
            var shot = PanelsActions.AddShot("PIP (test)", "B02", part.transform, "The light, behind the panel");
            try
            {
                Assert.That(shot.enabled, Is.False, "made off, for a step to switch on");
                Assert.That(PanelsActions.AddShot("PIP (test)", "B02", part.transform, "The light, behind the panel", ctx: new M3trik.Unitytk.Kernel.OpContext(dryRun: true)),
                            Is.SameAs(shot), "made once");
                _placard.Show("B02", "Watch the light", null);
                shot.Show();
                Assert.That((pip.Target, pip.Caption, pip.ShownBy), Is.EqualTo((part.transform, "The light, behind the panel", (Object)shot)));
                Assert.That(UnityEditor.AssetDatabase.GetAssetPath(pip.GetComponent<UIDocument>().panelSettings), Is.EqualTo(PanelsActions.WorldPanelPath),
                            "made on the package's settings, whose theme holds the panels' look");
                Camera camera = pip.PictureCamera;
                Vector3 toPart = part.transform.position - camera.transform.position;
                Assert.That(Vector3.Angle(camera.transform.forward, toPart), Is.LessThan(0.1f), "it looks at the part");
                Assert.That(Vector3.Dot(camera.transform.position - part.transform.position, _eyes.transform.position - part.transform.position), Is.GreaterThan(0f),
                            "from the eyes' side");
                Assert.That(camera.nearClipPlane, Is.LessThanOrEqualTo(0.05f), "near: nothing around the part is sliced open");
                Assert.That(camera.cullingMask & (1 << LayerMask.NameToLayer("UI")), Is.Zero, "it never shows a window: they stand on the UI layer");
                Vector3 eyes = _eyes.transform.position, right = _eyes.transform.right;
                float placardSide = Vector3.Dot(_placard.transform.position - eyes, right);
                float pipSide = Vector3.Dot(pip.transform.position - eyes, right);
                Assert.That(pipSide, Is.GreaterThan(placardSide), "beside the placard, toward the middle of the view");
                Vector3 seenFrom = camera.transform.position;
                Quaternion seenAs = camera.transform.rotation;
                Vector3 card = pip.transform.position;
                pip.Drag(new Vector2(240f, -80f));
                Assert.That((pip.transform.position - card).magnitude, Is.GreaterThan(0.05f), "dragged");
                Assert.That((camera.transform.position - seenFrom).magnitude + Quaternion.Angle(camera.transform.rotation, seenAs), Is.LessThan(1e-3f),
                            "moving the window never moves its picture");
                pip.Clear(_placard);
                Assert.That(pip.Target, Is.SameAs(part.transform), "only what showed it takes it away");
                pip.Clear(shot);
                Assert.That(pip.Target, Is.Null);

                var screen = new GameObject("UI_PIP_01").AddComponent<MeshRenderer>();
                screen.transform.position = eyes + new Vector3(1f, 0f, 1f);
                Assert.That(PanelsActions.MakePip("PIP (test)", PipStyle.Anchored, screen.transform), Is.SameAs(pip));
                shot.Show();
                Assert.That(pip.transform.position, Is.EqualTo(screen.transform.position), "at its authored screen");
                Assert.That(screen.enabled, Is.False, "the screen's own mesh hidden");
                Object.DestroyImmediate(screen.gameObject);
            }
            finally
            {
                Object.DestroyImmediate(pip.gameObject);
                Object.DestroyImmediate(part);
            }
        }

        [Test]
        public void It_comes_up_on_the_side_that_keeps_clear_of_what_the_user_acts_on()
        {
            // Eyes at (1, 1.6, 2) heading +X: the offset's left (-0.4) lies toward +Z.
            _placard.Show("A01", "Take the keys", null);
            Vector3 usual = _placard.transform.position;
            Assert.That(usual.z, Is.GreaterThan(2f), "on its offset's side");
            var keys = GameObject.CreatePrimitive(PrimitiveType.Cube);
            keys.transform.localScale = Vector3.one * 0.1f;
            keys.transform.position = _eyes.transform.position + (usual - _eyes.transform.position) * 3f;  // behind it
            try
            {
                _placard.Show("A01", "Take the keys", null, null, null, null, new[] { keys.transform });
                Assert.That(_placard.transform.position.z, Is.LessThan(2f), "mirrored: the keys stay in sight");
                Assert.That(_placard.KeepClear, Is.EqualTo(new[] { keys.transform }));
            }
            finally
            {
                Object.DestroyImmediate(keys);
            }
        }

        [Test]
        public void Content_with_a_spot_of_its_own_comes_up_there_unmirrored()
        {
            // Eyes at (1, 1.6, 2) heading +X: a spot 0.3 m to the right lies toward -Z.
            var keys = GameObject.CreatePrimitive(PrimitiveType.Cube);
            keys.transform.localScale = Vector3.one * 0.1f;
            try
            {
                var spot = new Vector3(0.3f, -0.1f, 0.6f);
                keys.transform.position = _eyes.transform.position + Quaternion.Euler(0f, 90f, 0f) * spot * 3f;  // behind it
                _placard.Show("A01", "Take the keys", null, null, null, null, new[] { keys.transform }, spot);
                Assert.That(_placard.transform.position - _eyes.transform.position,
                            Is.EqualTo(Quaternion.Euler(0f, 90f, 0f) * spot).Using(UnityEngine.TestTools.Utils.Vector3EqualityComparer.Instance),
                            "where its page was placed (clear of the scene), not mirrored by the placard");
            }
            finally
            {
                Object.DestroyImmediate(keys);
            }
        }

        [Test]
        public void What_it_shows_is_on_its_document_a_button_per_choice()
        {
            _placard.Show("B03   Did it light?", "Did the lamp light?", new[] { "YES", "NO" });
            Assert.That(Root.Q<Label>("heading").text, Is.EqualTo("B03   Did it light?"));
            Assert.That(Root.Q<Label>("text").text, Is.EqualTo("Did the lamp light?"));
            List<Button> buttons = Root.Query<Button>(className: Placard.ChoiceClass).ToList();
            Assert.That(buttons.Select(b => b.text), Is.EqualTo(new[] { "YES", "NO" }));
            Assert.That(Root.ClassListContains(Placard.EmptyClass), Is.False);

            _placard.Show("B04   Done", "", null);
            Assert.That(Root.Query<Button>(className: Placard.ChoiceClass).ToList(), Is.Empty, "the last page's buttons are gone");
            _placard.Clear();
            Assert.That(Root.ClassListContains(Placard.EmptyClass), Is.True, "nothing to show: the panel hides");
        }

        [Test]
        public void A_page_holds_a_choice_back_until_it_is_revealed_as_planned_once()
        {
            PlacardPage page = PanelsActions.AddPage(_placard.name, "C03", "Swap tools", "Take the screwdriver.", new List<string> { "Next" });
            var dry = new OpContext(dryRun: true);
            Assert.That(PanelsActions.HoldBack(_placard.name, "C03", "Next", dry), Is.True);
            Assert.That(page.waiting, Is.Empty, "a dry run changes nothing");
            Assert.That(PanelsActions.HoldBack(_placard.name, "C03", "Next"), Is.True);
            Assert.That(PanelsActions.HoldBack(_placard.name, "C03", "Next", new OpContext(dryRun: true)), Is.False, "once");
            var owner = new GameObject("STEP");
            UnityEngine.Events.UnityEvent done = owner.AddComponent<DebugCommand>().onRun;  // an event its owner holds
            try
            {
                Assert.That(PanelsActions.RevealOn(_placard.name, "C03", owner, owner.GetComponent<DebugCommand>(), done), Is.True);
                Assert.That(PanelsActions.RevealOn(_placard.name, "C03", owner, owner.GetComponent<DebugCommand>(), done, new OpContext(dryRun: true)), Is.False, "once");
                done.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                page.Show();
                Assert.That(Root.Query<Button>(className: Placard.ChoiceClass).ToList(), Is.Empty, "held back while its step runs");
                Assert.That(page.Offered, Is.Empty);
                done.Invoke();  // its step is complete
                Assert.That(page.Offered, Is.EqualTo(new[] { "Next" }));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void A_choice_offered_is_raised_with_its_label_and_one_not_offered_is_not()
        {
            var chosen = new List<string>();
            _placard.onChosen.AddListener(chosen.Add);
            _placard.Show("B03_5", "Try again.", new[] { "Try Again" });
            Assert.That(_placard.Choose("try again"), Is.True);
            Assert.That(_placard.Choose("Maybe"), Is.False);
            Assert.That(chosen, Is.EqualTo(new[] { "Try Again" }), "raised with the label as offered");
        }

        [Test]
        public void Its_buttons_are_named_by_place_its_enter_styled_by_a_class_and_its_arrow_says_what_it_is_given()
        {
            // A label is any text: two choices of one wording, or one with spaces, named the elements alike.
            _placard.fold.open = "-";
            _placard.fold.foldTooltip = "Hide";
            _placard.Show("Q01", "Which?", new[] { "Try Again", "Try Again" }, null, "12");
            List<Button> buttons = Root.Query<Button>(className: Placard.ChoiceClass).ToList();
            Assert.That(buttons.Select(b => b.name), Is.EqualTo(new[] { "choice-0", "choice-1" }));
            Button enter = Root.Query<Button>(className: Placard.KeyClass).ToList().Single(k => k.text == _placard.enterLabel);
            Assert.That(enter.ClassListContains(Placard.EnterClass), Is.True, "the look styles Enter by a class, not its name");
            Label fold = Root.Q<Label>(className: "window-fold");
            Assert.That((fold.text, fold.tooltip), Is.EqualTo(("-", "Hide")), "a project words and draws its own arrow");
        }

        [Test]
        public void A_key_pad_keys_in_its_keys_and_enter_raises_what_was_keyed_in()
        {
            var chosen = new List<string>();
            _placard.onChosen.AddListener(chosen.Add);
            _placard.Show("D08", "Enter the code.", null, null, "0123456789");
            List<Button> keys = Root.Query<Button>(className: Placard.KeyClass).ToList();
            Assert.That(keys.Select(k => k.text), Is.EqualTo(new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "Clear", "Enter" }));
            Assert.That(_placard.Enter(), Is.False, "nothing keyed in, nothing sent");
            Assert.That(_placard.Press('4') && _placard.Press('2') && !_placard.Press('x'), Is.True, "a key it lacks is refused");
            Assert.That(Root.Q<Label>("entry").text, Is.EqualTo("42"));
            _placard.Erase();
            _placard.Press('7');
            Assert.That(_placard.Enter(), Is.True);
            Assert.That((chosen.Single(), _placard.Entry), Is.EqualTo(("7", "")), "sent, and cleared");
            _placard.Show("D09", "Done.", null);
            Assert.That(Root.Query<Button>(className: Placard.KeyClass).ToList(), Is.Empty, "the next page has no key pad");
        }

        [Test]
        public void A_reply_shows_once_its_choice_leaves_the_page_shown()
        {
            PlacardPage page = PanelsActions.AddPage(_placard.name, "Q01", "Q01", "Which tool loosens the nut?", new[] { "Wrench", "Hammer" });
            page.replies.Add(new PlacardReply { choice = "hammer", text = "Try again." });
            page.Show();
            _placard.Choose("Wrench");
            Assert.That(_placard.Text, Is.EqualTo("Which tool loosens the nut?"), "no reply to the right one");
            _placard.Choose("Hammer");
            Assert.That(Root.Q<Label>("text").text, Is.EqualTo("Try again."));
            PlacardPage next = PanelsActions.AddPage(_placard.name, "Q02", "Q02", "Next question.", new[] { "Hammer" });
            _placard.onChosen.AddListener(_ => next.Show());  // a choice that moves on: no reply over the next page
            page.Show();
            _placard.Choose("Hammer");
            Assert.That(_placard.Text, Is.EqualTo("Next question."));
        }

        [Test]
        public void A_list_stays_as_pages_change_its_items_ticked_one_by_one()
        {
            PlacardList list = PanelsActions.AddList(_placard.name, "C01 list", new[] { "Panel removed", "Fuse checked" });
            PlacardTick tick = PanelsActions.AddTick(_placard.name, "C01 list", "fuse checked");
            Assert.That(list.Show(), Is.True);  // what switching it on does (Edit Mode calls no OnEnable)
            Assert.That((_placard.Items.Count, _placard.ListedBy), Is.EqualTo((2, (Object)list)));
            _placard.Show("C02", "Check the fuse.", null);
            _placard.Clear();
            Assert.That(_placard.Items.Count, Is.EqualTo(2), "a page's coming and going leaves the list");
            Assert.That(tick.Tick(), Is.True);
            Assert.That(_placard.IsTicked("Fuse checked") && !_placard.IsTicked("Panel removed"), Is.True);
            List<VisualElement> rows = Root.Query<VisualElement>(className: Placard.ItemClass).ToList();
            Assert.That(rows.Select(r => r.ClassListContains(Placard.TickedClass)), Is.EqualTo(new[] { false, true }));
            Assert.That(Root.ClassListContains(Placard.EmptyClass), Is.False, "a list alone shows the panel");
            list.Hide();
            Assert.That(_placard.Items, Is.Empty);
            list.Show();
            Assert.That(_placard.IsTicked("Fuse checked"), Is.False, "switched on again, it starts unticked");
        }

        [Test]
        public void It_comes_up_in_front_of_the_eyes_facing_them_turned_with_their_heading_alone()
        {
            _placard.Show("A01", "Unlock both latches.", null);
            Vector3 expected = _eyes.transform.position + Quaternion.Euler(0f, 90f, 0f) * _placard.offset;
            Assert.That(Vector3.Distance(_placard.transform.position, expected), Is.LessThan(1e-4f), "the eyes' tilt does not move it");
            Vector3 away = (_placard.transform.position - _eyes.transform.position).normalized;
            Assert.That(Vector3.Dot(_placard.transform.forward, away), Is.GreaterThan(0.9999f), "it faces the eyes");

            _placard.follow = false;
            _eyes.transform.position += Vector3.forward * 3f;
            _placard.Show("A02", "Open the door.", null);
            Assert.That(Vector3.Distance(_placard.transform.position, expected), Is.LessThan(1e-4f), "not following, it stays");
        }

        [Test]
        public void Every_window_comes_up_ahead_of_eyes_looking_straight_down_or_up()
        {
            // The debug panel and the picture came up as if headed +Z whatever way such eyes were headed (the placard alone
            // read the way their view's top points, by a sign a straight-down view leaves to rounding).
            var debug = new GameObject("DEBUG (test)", typeof(UIDocument)).AddComponent<DebugPanel>();
            try
            {
                debug.eyes = _eyes.transform;
                foreach (float pitch in new[] { 90f, -90f })
                {
                    _eyes.transform.rotation = Quaternion.Euler(pitch, 90f, 0f);  // headed +X
                    _placard.Place();
                    debug.Place();
                    foreach (Transform window in new[] { _placard.transform, debug.transform })
                    {
                        Vector3 expected = _eyes.transform.position + Quaternion.Euler(0f, 90f, 0f) * (window == debug.transform ? debug.offset : _placard.offset);
                        Assert.That(Vector3.Distance(window.position, expected), Is.LessThan(1e-4f), $"{window.name} ahead, looking {(pitch > 0f ? "down" : "up")}");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(debug.gameObject);
            }
        }

        [Test]
        public void A_page_takes_away_only_what_it_put_there()
        {
            PlacardPage first = PanelsActions.AddPage(_placard.name, "A01", "A01", "First.", null);
            PlacardPage second = PanelsActions.AddPage(_placard.name, "A02", "A02", "Second.", new[] { "Next" });
            Assert.That(first.Placard, Is.SameAs(_placard), "the scene's placard, none being named");
            Assert.That(first.Show() && second.Show(), Is.True);
            Assert.That((_placard.Text, _placard.ShownBy), Is.EqualTo(("Second.", (Object)second)));
            _placard.Clear(first);
            Assert.That(_placard.Text, Is.EqualTo("Second."), "the page replaced takes nothing away");
            _placard.Clear(second);
            Assert.That((_placard.Heading, _placard.Choices.Count), Is.EqualTo(("", 0)));
        }
    }
}
