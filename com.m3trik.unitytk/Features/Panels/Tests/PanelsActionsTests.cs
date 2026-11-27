using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels.Tests
{
    /// <summary>
    /// The Panels actions, held to the kernel's three checks as a chain a builder runs (a
    /// placard made, two pages, the scene's panel input set up): one Ctrl+Z restores the scene,
    /// the dry run's plan names every change the run makes, and a second run plans nothing.
    /// </summary>
    public class PanelsActionsTests
    {
        const string Name = "Placard (actions test)";
        const string DebugName = "Debug (actions test)";

        [TearDown]
        public void TearDown()
        {
            foreach (string name in new[] { Name, DebugName })
            {
                var made = GameObject.Find(name);
                if (made != null) Object.DestroyImmediate(made);
            }
            var input = GameObject.Find(PanelsActions.InputName);
            if (input != null) Object.DestroyImmediate(input);
        }

        static void Chain(OpContext ctx)
        {
            PanelsActions.MakePlacard(Name, ctx);
            PanelsActions.AddPage(Name, "A01", "Open the door", "Select the door to open it.", new[] { "Next" }, ctx: ctx);
            PanelsActions.AddPage(Name, "A02", "Done", "", null, ctx: ctx);
            PanelsActions.SetKeys(Name, "A02", "0123456789", ctx);
            PanelsActions.AddReply(Name, "A02", "Hammer", "Try again.", ctx);
            PanelsActions.AddList(Name, "A01 list", new[] { "Door open", "Lamp on" }, ctx: ctx);
            PanelsActions.AddTick(Name, "A01 list", "Lamp on", ctx: ctx);
            PanelsActions.SetSpot(Name, "A01", new Vector3(0.3f, -0.1f, 0.6f), ctx);
            PanelsActions.MakeDebugPanel(DebugName, ctx);
            PanelsActions.SetUpInput(ctx);
        }

        [Test]
        public void The_chain_plans_what_it_does_in_one_undo_step_and_a_second_run_plans_nothing()
        {
            Assume.That(Object.FindAnyObjectByType<PanelInputConfiguration>(FindObjectsInactive.Include) == null, "the test scene starts with no panel input");
            var dry = new OpContext(dryRun: true);
            Chain(dry);
            Assert.That(GameObject.Find(Name), Is.Null, "a dry run makes nothing");
            Assert.That(dry.Issues, Is.Empty, "a dry run's planned placard takes its pages");
            OpContext.RunMenu("Make the placard", ctx =>
            {
                Chain(ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });

            var placard = GameObject.Find(Name).GetComponent<Placard>();
            var document = placard.GetComponent<UIDocument>();
            Assert.That(AssetDatabase.GetAssetPath(document.panelSettings), Is.EqualTo(PanelsActions.WorldPanelPath));
            Assert.That(AssetDatabase.GetAssetPath(document.visualTreeAsset), Is.EqualTo(PanelsActions.PlacardLayoutPath));
            var settings = new SerializedObject(document.panelSettings);
            SerializedProperty mode = settings.FindProperty("m_RenderMode");
            Assert.That(mode.enumNames[mode.enumValueIndex], Is.EqualTo("WorldSpace"), "the package's panel settings draw in the world");
            SerializedProperty collider = settings.FindProperty("m_ColliderUpdateMode");
            Assert.That(collider.enumNames[collider.enumValueIndex], Is.EqualTo("MatchDocumentRect"), "a collider over the document, for a pointer and a controller");
            PlacardPage a01 = placard.transform.Find("A01").GetComponent<PlacardPage>();
            Assert.That((a01.enabled, a01.heading, a01.text, a01.choices.Single()), Is.EqualTo((false, "Open the door", "Select the door to open it.", "Next")),
                        "made switched off, for what runs the scene to switch on");
            PlacardPage a02 = placard.transform.Find("A02").GetComponent<PlacardPage>();
            Assert.That((a02.choices.Count, a02.keys, a02.replies.Single().text), Is.EqualTo((0, "0123456789", "Try again.")));
            PlacardList list = placard.transform.Find("A01 list").GetComponent<PlacardList>();
            PlacardTick tick = list.transform.Find(PanelsActions.TickName("Lamp on")).GetComponent<PlacardTick>();
            Assert.That((list.enabled, list.items.Count, tick.enabled, tick.list, tick.item), Is.EqualTo((false, 2, false, list, "Lamp on")));
            var input = Object.FindAnyObjectByType<PanelInputConfiguration>(FindObjectsInactive.Include);
            Assert.That((input.processWorldSpaceInput, input.panelInputRedirection),
                        Is.EqualTo((true, PanelInputConfiguration.PanelInputRedirection.Never)));

            var again = new OpContext(dryRun: true);
            Chain(again);
            Assert.That(again.Changes, Is.Empty);
            var reworded = new OpContext(dryRun: true);
            PanelsActions.AddPage(Name, "A01", "Open the door", "Now open it.", new[] { "Next" }, ctx: reworded);
            Assert.That(reworded.Changes.Single().kind, Is.EqualTo("change"), "a page that shows otherwise is changed, not made again");

            Undo.PerformUndo();
            Assert.That(GameObject.Find(Name) == null && Object.FindAnyObjectByType<PanelInputConfiguration>(FindObjectsInactive.Include) == null,
                        "one Ctrl+Z took it all away");
        }

        [Test]
        public void Two_placards_pages_of_one_name_are_planned_apart()
        {
            // A page of one name on two placards: a plan naming a planned page by its name took the second for the first.
            const string Other = "Placard (other actions test)";
            try
            {
                void Run(OpContext ctx)
                {
                    foreach (string placard in new[] { Name, Other })
                    {
                        PanelsActions.MakePlacard(placard, ctx);
                        PanelsActions.AddPage(placard, "A01", "Key it in", "", null, ctx: ctx);
                        PanelsActions.SetKeys(placard, "A01", "0123456789", ctx);
                    }
                }
                var dry = new OpContext(dryRun: true);
                Run(dry);
                var live = new OpContext();
                Run(live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(dry.Changes.Count(c => c.action.StartsWith("show a key pad")), Is.EqualTo(2), "a key pad on each");
            }
            finally
            {
                var other = GameObject.Find(Other);
                if (other != null) Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void A_part_is_made_switched_on_where_asked()
        {
            // Each part was made switched off whatever its caller wanted.
            PanelsActions.MakePlacard(Name, new OpContext());
            Assert.That(PanelsActions.AddPage(Name, "A01", "Shown", "", null).enabled, Is.False, "off by default, for a step to switch on");
            Assert.That(PanelsActions.AddPage(Name, "A02", "Shown", "", null, startEnabled: true).enabled, Is.True);
            Assert.That(PanelsActions.AddList(Name, "list", new[] { "One" }, startEnabled: true).enabled, Is.True);
            Assert.That(PanelsActions.AddTick(Name, "list", "One", startEnabled: true).enabled, Is.True);
            const string Pip = "PiP (switched on test)";
            var part = new GameObject("PART");
            try
            {
                PanelsActions.MakePip(Pip);
                Assert.That(PanelsActions.AddShot(Pip, "B01", part.transform, "The part", startEnabled: true).enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(part);
                var pip = GameObject.Find(Pip);
                if (pip != null) Object.DestroyImmediate(pip);
            }
        }

        [Test]
        public void An_event_of_an_object_a_dry_run_only_planned_is_planned_as_its_live_run_wires_it()
        {
            // A plan named the object holding the event by the name its caller gave, its live run by the object's path.
            const string Room = "Room (actions test)";
            PanelsActions.MakePlacard(Name, new OpContext());
            PanelsActions.AddPage(Name, "A01", "Open", "", new[] { "Next" }, ctx: new OpContext());
            GameObject room = null;
            try
            {
                var dry = new OpContext(dryRun: true);
                var planned = new PlannedObject("LOCK", new PlannedObject(Room));
                Assert.That(PanelsActions.SayOn(Name, planned, "Take the keys first.", dry), Is.True);
                Assert.That(PanelsActions.RevealOn(Name, "A01", planned, dry), Is.True);
                Assert.That(PanelsActions.SayOn(Name, planned, "Take the keys first.", dry), Is.False, "planned once");
                room = new GameObject(Room);
                var holder = new GameObject("LOCK");
                holder.transform.SetParent(room.transform);
                var source = holder.AddComponent<Placard>();  // any component with an event
                var command = holder.AddComponent<DebugCommand>();  // one with an event of no argument
                var live = new OpContext();
                Assert.That(PanelsActions.SayOn(Name, holder, source, source.onChosen, "Take the keys first.", live), Is.True);
                Assert.That(PanelsActions.RevealOn(Name, "A01", holder, command, command.onRun, live), Is.True);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(PanelsActions.RevealOn(Name, "A01", planned, new OpContext()), Is.False, "a live run has nothing to wire on a planned object");
            }
            finally
            {
                if (room != null) Object.DestroyImmediate(room);
            }
        }

        [Test]
        public void An_event_has_the_placard_say_a_line_once_planned_and_undone()
        {
            // A part selected without the tool it needs says so on the placard. Added: 2026-11-22
            PanelsActions.MakePlacard(Name, new OpContext());
            var source = new GameObject("LOCK").AddComponent<Placard>();  // any component with an event
            try
            {
                var dry = new OpContext(dryRun: true);
                Assert.That(PanelsActions.SayOn(Name, source.gameObject, null, null, "Take the keys first.", dry), Is.True);
                Assert.That(PanelsActions.SayOn(Name, source.gameObject, null, null, "Take the keys first.", dry), Is.False, "planned once");
                OpContext.RunMenu("Say it", ctx =>
                {
                    Assert.That(PanelsActions.SayOn(Name, source.gameObject, source, source.onChosen, "Take the keys first.", ctx), Is.True);
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                });
                Placard placard = GameObject.Find(Name).GetComponent<Placard>();
                Assert.That((source.onChosen.GetPersistentTarget(0), source.onChosen.GetPersistentMethodName(0)), Is.EqualTo(((Object)placard, nameof(Placard.Tell))));
                var again = new OpContext(dryRun: true);
                Assert.That(PanelsActions.SayOn(Name, source.gameObject, source, source.onChosen, "Take the keys first.", again), Is.False);
                Assert.That(again.Changes, Is.Empty, "a second run plans nothing");
                placard.Show("Unlock", "Grab the keys.", null);
                placard.Tell("Take the keys first.");
                Assert.That((placard.Heading, placard.Text), Is.EqualTo(("Unlock", "Take the keys first.")), "said in place of the text, the content kept");
                Undo.PerformUndo();
                Assert.That(source.onChosen.GetPersistentEventCount(), Is.Zero, "one Ctrl+Z took it away");
            }
            finally
            {
                Object.DestroyImmediate(source.gameObject);
            }
        }

        // The real task's pictures showed the room from the ceiling: each shot's spot was a child of the picture's card,
        // which moves beside the user as it comes up and carried the spot with it.
        [Test]
        public void A_shots_spot_rides_its_part_and_the_card_never_moves_it()
        {
            const string Pip = "PiP (spot test)";
            var door = new GameObject("DOOR");
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = "PART";
            part.transform.SetParent(door.transform, false);
            part.transform.localPosition = new Vector3(0f, 1f, 2f);
            try
            {
                PipView view = PanelsActions.MakePip(Pip);
                PipShot shot = PanelsActions.AddShot(Pip, "B02", part.transform, "The part");
                // One an earlier build made under the shot goes: the card carried it about.
                var carried = new GameObject("From");
                carried.transform.SetParent(shot.transform, false);
                view.fieldOfView = 24f;
                var dry = new OpContext(dryRun: true);
                PanelsActions.AddShotFrom(shot, dry);
                var live = new OpContext();
                GameObject spot = PanelsActions.AddShotFrom(shot, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.action))), "the plan is the run");
                Assert.That(carried == null, "the spot under the shot is gone");
                Assert.That(spot.transform.parent, Is.SameAs(part.transform), "the spot is the part's");
                // It is a camera, so a person frames the picture looking through it; it never draws to the screen.
                Camera lens = spot.GetComponent<Camera>();
                Assert.That(lens != null && !lens.enabled, "a camera, switched off");
                Assert.That(lens.fieldOfView, Is.EqualTo(24f), "with the view's field of view as it is made");
                lens.fieldOfView = 40f;  // a person's
                var again = new OpContext(dryRun: true);
                PanelsActions.AddShotFrom(shot, again);
                Assert.That(again.Changes, Is.Empty, "a second run changes nothing");
                Assert.That(lens.fieldOfView, Is.EqualTo(40f), "nor a person's field of view");
                // One an earlier build made as a plain object becomes a camera where it stands.
                Object.DestroyImmediate(lens);
                var upgrade = new OpContext(dryRun: true);
                PanelsActions.AddShotFrom(shot, upgrade);
                Assert.That(upgrade.Changes.Select(c => c.action), Has.Some.Contains("make it a camera"));
                PanelsActions.AddShotFrom(shot, new OpContext());
                lens = spot.GetComponent<Camera>();
                Assert.That(lens != null && !lens.enabled && lens.fieldOfView == 24f, "made a camera");
                spot.transform.SetPositionAndRotation(new Vector3(0.5f, 1.2f, 1f), Quaternion.Euler(10f, 30f, 0f));
                Vector3 at = spot.transform.position;
                Quaternion look = spot.transform.rotation;

                shot.Show();
                view.transform.SetPositionAndRotation(new Vector3(3f, 2f, -4f), Quaternion.Euler(0f, 140f, 0f));  // the card comes up beside the user
                view.Frame();
                Assert.That(Vector3.Distance(spot.transform.position, at), Is.LessThan(1e-4f), "the card moved, the spot did not");
                Assert.That(Vector3.Distance(view.PictureCamera.transform.position, at), Is.LessThan(1e-4f), "the picture is taken from the spot");
                Assert.That(Quaternion.Angle(view.PictureCamera.transform.rotation, look), Is.LessThan(0.01f), "the way the spot looks");

                door.transform.Rotate(0f, 90f, 0f);  // a step swings the door open: what is mounted on it goes with it, and its spot
                view.Frame();
                Assert.That(Vector3.Distance(view.PictureCamera.transform.position, spot.transform.position), Is.LessThan(1e-4f));
                Assert.That(Vector3.Distance(spot.transform.position, at), Is.GreaterThan(0.5f), "the spot rode the part");
            }
            finally
            {
                Object.DestroyImmediate(door);
                var pip = GameObject.Find(Pip);
                if (pip != null) Object.DestroyImmediate(pip);
            }
        }

        [Test]
        public void A_picture_leaves_out_the_cues_it_is_given_while_its_own_camera_draws_and_gives_them_back()
        {
            // The real task's picture of the door-number dials was a third arrow: the cue at the part, drawn over it.
            const string Pip = "PiP (hidden test)";
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.name = "Indicator C09";
            var off = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            off.GetComponent<Renderer>().forceRenderingOff = true;  // hidden by another: never given back by the picture
            try
            {
                PipView view = PanelsActions.MakePip(Pip);
                var dry = new OpContext(dryRun: true);
                Assert.That(PanelsActions.SetHidden(view, new[] { arrow, off, arrow }, dry), Is.True);
                Assert.That(view.hidden, Is.Empty, "a dry run plans it");
                Assert.That(PanelsActions.SetHidden(view, new[] { arrow, off, arrow }, new OpContext()), Is.True);
                Assert.That(view.hidden, Is.EqualTo(new[] { arrow, off }), "each once");
                Assert.That(PanelsActions.SetHidden(view, new[] { off, arrow }, new OpContext(dryRun: true)), Is.False, "a second run changes nothing");

                view.Hide();  // what its camera's drawing does
                Assert.That(arrow.GetComponent<Renderer>().forceRenderingOff, Is.True, "left out of the picture");
                view.Unhide();
                Assert.That(arrow.GetComponent<Renderer>().forceRenderingOff, Is.False, "and drawn in the scene again");
                Assert.That(off.GetComponent<Renderer>().forceRenderingOff, Is.True, "what another hid stays hidden");
            }
            finally
            {
                Object.DestroyImmediate(arrow);
                Object.DestroyImmediate(off);
                var pip = GameObject.Find(Pip);
                if (pip != null) Object.DestroyImmediate(pip);
            }
        }

        [Test]
        public void A_shot_given_a_camera_someone_made_uses_it_as_it_stands()
        {
            const string Pip = "PiP (camera test)";
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = "PART";
            var made = new GameObject("CAM_B02");
            made.transform.SetPositionAndRotation(new Vector3(1f, 1.5f, -2f), Quaternion.Euler(5f, -20f, 0f));
            var lens = made.AddComponent<Camera>();
            lens.fieldOfView = 22f;
            void Run(OpContext ctx)
            {
                PanelsActions.MakePip(Pip, ctx: ctx);
                PipShot shot = PanelsActions.AddShot(Pip, "B02", part.transform, "The part", ctx: ctx);
                if (shot != null) PanelsActions.SetShotFrom(shot, made.transform, ctx);
                else PanelsActions.SetShotFrom(PanelsActions.PlannedShot(Pip, "B02"), made.transform, ctx);
            }
            try
            {
                var dry = new OpContext(dryRun: true);
                Run(dry);
                Assert.That(lens.enabled, "a dry run changes nothing");
                OpContext.RunMenu("Shot camera", ctx =>
                {
                    Run(ctx);
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.action)), Is.EquivalentTo(dry.Changes.Select(c => (c.kind, c.action))));
                });
                PipView view = GameObject.Find(Pip).GetComponent<PipView>();
                PipShot shot = view.transform.Find("B02").GetComponent<PipShot>();
                Assert.That(shot.from, Is.SameAs(made.transform));
                Assert.That(lens.enabled, Is.False, "it frames the picture and never draws to the screen");
                Assert.That(part.transform.Find(PanelsActions.SpotName("B02")) == null, "no spot is made beside a named one");
                shot.Show();
                view.Frame();
                Assert.That(Vector3.Distance(view.PictureCamera.transform.position, made.transform.position), Is.LessThan(1e-4f));
                Assert.That(Quaternion.Angle(view.PictureCamera.transform.rotation, made.transform.rotation), Is.LessThan(0.01f));
                Assert.That(view.PictureCamera.fieldOfView, Is.EqualTo(22f).Within(1e-3f), "its own field of view");
                var again = new OpContext(dryRun: true);
                Run(again);
                Assert.That(again.Changes, Is.Empty, "a second run plans nothing");
            }
            finally
            {
                Object.DestroyImmediate(part);
                Object.DestroyImmediate(made);
                var pip = GameObject.Find(Pip);
                if (pip != null) Object.DestroyImmediate(pip);
            }
        }

        // An earlier build made the shot's default spot and the row then named its own camera: the spot was both the
        // default-named one and the one the shot looked from, so a dry run planned its delete twice and the run made it
        // once; and any child of the part the shot looked from was taken for a build's, a person's own locator too.
        [Test]
        public void A_shot_given_its_own_camera_takes_away_the_spot_a_build_made_once_and_never_a_persons()
        {
            const string Pip = "PiP (spot swap test)";
            var part = new GameObject("PART");
            var lens = new GameObject("CAM_B02");
            var locator = new GameObject("LOCATOR");
            locator.transform.SetParent(part.transform, false);
            try
            {
                PanelsActions.MakePip(Pip);
                PipShot shot = PanelsActions.AddShot(Pip, "B02", part.transform, "The part");
                GameObject made = PanelsActions.AddShotFrom(shot);
                Assert.That(made.GetComponent<PipSpot>() != null && shot.from == made.transform, "premise: a build's spot, looked from");

                var dry = new OpContext(dryRun: true);
                PanelsActions.SetShotFrom(shot, lens.transform, dry);
                Assert.That(dry.Changes.Count(c => c.kind == "delete"), Is.EqualTo(1), "its spot planned to go once");
                var live = new OpContext();
                PanelsActions.SetShotFrom(shot, lens.transform, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.action))), "the plan is the run");
                Assert.That(made == null, "the spot a build made is gone");
                Assert.That(shot.from, Is.SameAs(lens.transform));

                PanelsActions.SetShotFrom(shot, locator.transform);  // a person's own locator under the part
                PanelsActions.SetShotFrom(shot, lens.transform);     // and the row names its camera again
                Assert.That(locator != null, "a person's object is never taken away");
                Assert.That(shot.from, Is.SameAs(lens.transform));
            }
            finally
            {
                Object.DestroyImmediate(part);
                Object.DestroyImmediate(lens);
                var pip = GameObject.Find(Pip);
                if (pip != null) Object.DestroyImmediate(pip);
            }
        }

        // A spot's field of view as it is made, and a picture's camera set to one, were covered only by the Tasks kit's tests.
        [Test]
        public void A_spot_frames_with_the_field_of_view_it_is_made_with_and_a_camera_is_set_to_one_once()
        {
            const string Pip = "PiP (field of view test)";
            var part = new GameObject("PART");
            try
            {
                PanelsActions.MakePip(Pip);
                PipShot shot = PanelsActions.AddShot(Pip, "B02", part.transform, "The part");
                Camera lens = PanelsActions.AddShotFrom(shot, null, 18f).GetComponent<Camera>();
                Assert.That(lens.fieldOfView, Is.EqualTo(18f), "the field of view asked, as it is made");

                var dry = new OpContext(dryRun: true);
                Assert.That(PanelsActions.SetFieldOfView(lens, 200f, dry), Is.True);
                Assert.That(lens.fieldOfView, Is.EqualTo(18f), "a dry run changes nothing");
                Assert.That(dry.Changes.Single().action, Does.Contain("179 degree"), "out of range: the nearest in range");
                var live = new OpContext();
                Assert.That(PanelsActions.SetFieldOfView(lens, 200f, live), Is.True);
                Assert.That(live.Changes.Select(c => (c.kind, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.action))));
                Assert.That(lens.fieldOfView, Is.EqualTo(179f));
                Assert.That(PanelsActions.SetFieldOfView(lens, 179f, new OpContext(dryRun: true)), Is.False, "framing so already: nothing");
            }
            finally
            {
                Object.DestroyImmediate(part);
                var pip = GameObject.Find(Pip);
                if (pip != null) Object.DestroyImmediate(pip);
            }
        }

        [Test]
        public void A_shots_own_spot_is_planned_made_once_and_undone()
        {
            const string Pip = "PiP (actions test)";
            var part = new GameObject("PART");
            void Run(OpContext ctx)
            {
                PanelsActions.MakePip(Pip, ctx: ctx);
                PipShot shot = PanelsActions.AddShot(Pip, "B02", part.transform, "The part", ctx: ctx);
                if (shot != null) PanelsActions.AddShotFrom(shot, ctx);
                else PanelsActions.AddShotFrom(PanelsActions.PlannedShot(Pip, "B02"), part.transform, ctx);
            }
            try
            {
                var dry = new OpContext(dryRun: true);
                Run(dry);
                Assert.That(GameObject.Find(Pip), Is.Null, "a dry run makes nothing");
                OpContext.RunMenu("Shot spot", ctx =>
                {
                    Run(ctx);
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                });
                PipShot made = GameObject.Find(Pip).transform.Find("B02").GetComponent<PipShot>();
                Assert.That(made.from, Is.Not.Null.And.SameAs(part.transform.Find(PanelsActions.SpotName("B02"))),
                            "the shot looks from its own spot, under the part it shows");
                var again = new OpContext(dryRun: true);
                Run(again);
                Assert.That(again.Changes, Is.Empty);
                Undo.PerformUndo();
                Assert.That(GameObject.Find(Pip) == null, "one Ctrl+Z took it all away");
            }
            finally
            {
                Object.DestroyImmediate(part);
                var pip = GameObject.Find(Pip);
                if (pip != null) Object.DestroyImmediate(pip);
            }
        }

        [Test]
        public void A_window_added_by_hand_is_given_the_packages_settings_and_look()
        {
            // Add Component gave a debug panel no look and its document no panel settings: nothing showed.
            var node = new GameObject(DebugName);
            DebugPanel panel = ObjectFactory.AddComponent<DebugPanel>(node);
            Assert.That(AssetDatabase.GetAssetPath(node.GetComponent<UIDocument>().panelSettings), Is.EqualTo(PanelsActions.WorldPanelPath));
            Assert.That(node.GetComponent<UIDocument>().worldSpaceSizeMode, Is.EqualTo(UIDocument.WorldSpaceSizeMode.Dynamic),
                        "sized to its content, as a window an action makes (Unity's default: a fixed 1920 by 1080)");
            Assert.That(AssetDatabase.GetDependencies(PanelsActions.WorldPanelPath, true), Does.Contain(PanelsActions.LookPath),
                        "its settings' theme holds the package's look");
            Assert.That(panel.look, Is.Null, "nothing over the theme");
            var placard = new GameObject(Name);
            ObjectFactory.AddComponent<Placard>(placard);
            Assert.That(AssetDatabase.GetAssetPath(placard.GetComponent<UIDocument>().visualTreeAsset), Is.EqualTo(PanelsActions.PlacardLayoutPath),
                        "a placard its layout");
        }

        [Test]
        public void A_window_made_where_a_document_stands_asks_before_putting_it_in_world_space_and_is_undone()
        {
            // The document's own settings were overwritten unasked, so undo left them the package's.
            var node = new GameObject(DebugName);
            UIDocument document = node.AddComponent<UIDocument>();
            Assert.That(document.worldSpaceSizeMode, Is.EqualTo(UIDocument.WorldSpaceSizeMode.Fixed), "Unity's own default");
            var dry = new OpContext(dryRun: true);
            Assert.That(PanelsActions.MakeDebugPanel(DebugName, dry), Is.Null);
            Assert.That(dry.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "change" }), "the panel made and its document changed, both planned");
            OpContext.RunMenu("Make debug panel", ctx =>
            {
                PanelsActions.MakeDebugPanel(DebugName, ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That((AssetDatabase.GetAssetPath(document.panelSettings), document.worldSpaceSizeMode),
                        Is.EqualTo((PanelsActions.WorldPanelPath, UIDocument.WorldSpaceSizeMode.Dynamic)));
            Undo.PerformUndo();
            Assert.That(node.GetComponent<DebugPanel>() == null, "one Ctrl+Z took the panel away");
            Assert.That((document.panelSettings == null, document.worldSpaceSizeMode), Is.EqualTo((true, UIDocument.WorldSpaceSizeMode.Fixed)),
                        "and gave the document its own settings back");
        }

        [Test]
        public void Every_windows_pixels_to_the_metre_are_the_packages_world_panels()
        {
            // Unity keeps a panel settings' scale from scripts (its pixels per unit is internal), so the windows carry it.
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelsActions.WorldPanelPath);
            using var saved = new SerializedObject(settings);
            Assert.That(saved.FindProperty("m_PixelsPerUnit").floatValue, Is.EqualTo(Placard.DefaultPixelsPerMetre));
        }

        [Test]
        public void A_debug_panel_is_taken_away_as_asked_and_undone()
        {
            PanelsActions.MakeDebugPanel(DebugName);
            var dry = new OpContext(dryRun: true);
            Assert.That(PanelsActions.RemoveDebugPanel(DebugName, dry), Is.True);
            Assert.That(dry.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(new[] { ("delete", DebugName) }));
            Assert.That(GameObject.Find(DebugName), Is.Not.Null, "a dry run takes nothing away");
            OpContext.RunMenu("Remove debug panel", ctx => PanelsActions.RemoveDebugPanel(DebugName, ctx));
            Assert.That(GameObject.Find(DebugName) == null);
            Assert.That(PanelsActions.RemoveDebugPanel(DebugName, new OpContext(dryRun: true)), Is.False, "none left: nothing to do");
            Undo.PerformUndo();
            Assert.That(GameObject.Find(DebugName), Is.Not.Null, "one Ctrl+Z brought it back");
        }

        [Test]
        public void A_page_for_a_missing_placard_is_raised_and_makes_nothing()
        {
            var ctx = new OpContext();
            Assert.That(PanelsActions.AddPage("No Such Placard", "A01", "x", "y", null, ctx: ctx), Is.Null);
            Assert.That(ctx.Issues.Single().code, Is.EqualTo(PanelsActions.MissingCode));
            Assert.That(ctx.Changes, Is.Empty);
        }
    }
}
