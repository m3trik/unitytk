using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Looks.Tests
{
    /// <summary>
    /// The Looks actions, each held to the kernel's three checks: one Ctrl+Z after
    /// a run restores the scene, the dry run's plan names every change the run makes,
    /// and a second run plans nothing.
    /// </summary>
    public class LooksActionsTests
    {
        GameObject _model;

        [SetUp]
        public void SetUp() => _model = new GameObject("MODEL");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_model);

        [Test]
        public void A_look_is_planned_made_once_set_again_and_undone()
        {
            var dry = new OpContext(dryRun: true);
            Assert.That(LooksActions.SetLook(_model, LookCurve.AcesFilmic, 1f, ctx: dry), Is.Null);
            Assert.That(LooksActions.SetLook(_model, LookCurve.AcesFilmic, 1f, ctx: dry), Is.Null);
            Assert.That(dry.Changes.Select(c => c.target), Is.EqualTo(new[] { "MODEL/SceneLook" }), "planned once");
            Assert.That(_model.GetComponent<SceneLook>(), Is.Null, "a dry run makes nothing");
            SceneLook made = null;
            OpContext.RunMenu("Give the model its look", ctx =>
            {
                made = LooksActions.SetLook(_model, LookCurve.AcesFilmic, 1f, ctx: ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(made != null && made.toneMapping == LookCurve.AcesFilmic && made.exposure == 1f);
            var again = new OpContext(dryRun: true);
            Assert.That(LooksActions.SetLook(_model, LookCurve.AcesFilmic, 1f, ctx: again), Is.SameAs(made));
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");

            var brighter = new OpContext();
            Undo.IncrementCurrentGroup();  // a step of its own, as a menu's run is
            LooksActions.SetLook(_model, LookCurve.AcesFilmic, 1.5f, ctx: brighter);
            Assert.That((brighter.Changes.Count, made.exposure), Is.EqualTo((1, 1.5f)), "another exposure: one change");
            Assert.Throws<System.ArgumentException>(() => LooksActions.SetLook(_model, "AgX", 1f));
            Undo.PerformUndo();
            Undo.PerformUndo();
            Assert.That(_model.GetComponent<SceneLook>(), Is.Null, "undone");
        }

        [Test]
        public void A_look_takes_its_table_span_cameras_and_weight_and_its_table_wins()
        {
            var table = new Texture2D(256, 16) { name = "Grade" };
            var camera = new GameObject("Eyes").AddComponent<Camera>();
            try
            {
                var options = new DisplayOptions { priority = 5f, bloomOff = false, vignetteOff = false, cameras = new[] { camera } };
                var plan = new OpContext(dryRun: true);
                LooksActions.SetLook(_model, LookCurve.AcesFilmic, 2f, table, 8f, options, plan);
                Assert.That(plan.Changes.Single().action, Does.Contain("its table 'Grade' (in place of ACESFilmic)"),
                            "the plan says the table wins");
                SceneLook look = LooksActions.SetLook(_model, LookCurve.AcesFilmic, 2f, table, 8f, options, new OpContext());
                Assert.That((look.table, look.span, look.priority, look.bloomOff, look.vignetteOff),
                            Is.EqualTo((table, 8f, 5f, false, false)));
                Assert.That(look.cameras, Is.EqualTo(new[] { camera }));
                Assert.That(look.toneMapping, Is.EqualTo(LookCurve.AcesFilmic), "the tone mapping kept behind the table");

                var again = new OpContext(dryRun: true);
                LooksActions.SetLook(_model, LookCurve.AcesFilmic, 2f, table, 8f,
                                     new DisplayOptions { priority = 5f, bloomOff = false, vignetteOff = false, cameras = new[] { camera } }, again);
                Assert.That(again.Changes, Is.Empty, "the same values: nothing to change");
                LooksActions.SetLook(_model, LookCurve.AcesFilmic, 2f, ctx: new OpContext());
                Assert.That((look.table, look.span, look.priority, look.cameras.Length), Is.EqualTo(((Texture2D)null, LookCurve.Brightest, float.MaxValue, 0)),
                            "left out: a look's defaults");

                LooksActions.SetLook(_model, LookCurve.Linear, 1f, ctx: new OpContext());
                var tabled = new OpContext();
                LooksActions.SetLook(_model, null, 1f, table, ctx: tabled);
                Assert.That((look.table, look.toneMapping), Is.EqualTo((table, LookCurve.Linear)), "a table wins: no tone mapping asked, its own kept");
                Assert.That(tabled.Changes.Single().action, Does.Contain("its table 'Grade'").And.Not.Contain("in place of"));
                Assert.Throws<System.ArgumentException>(() => LooksActions.SetLook(_model, null, 1f), "no table: a tone mapping is asked");
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}
