using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// <see cref="PersistentCalls"/>: an event's saved calls read off the object holding it, each with the argument its
    /// mode passes; an event no serialized field holds, or no event at a path, raised and read as nothing.
    /// </summary>
    public class PersistentCallsTests
    {
        TestEventHolder _holder;

        [SetUp]
        public void SetUp() => _holder = ScriptableObject.CreateInstance<TestEventHolder>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_holder);

        [Test]
        public void Each_call_is_read_with_the_argument_its_mode_passes()
        {
            UnityEventTools.AddStringPersistentListener(_holder.clicked, _holder.Say, "opened");
            UnityEventTools.AddBoolPersistentListener(_holder.clicked, _holder.Switch, true);
            UnityEventTools.AddFloatPersistentListener(_holder.clicked, _holder.Scale, float.NaN);  // a value JSON cannot hold
            UnityEventTools.AddVoidPersistentListener(_holder.clicked, _holder.Ping);
            UnityEventTools.AddPersistentListener(_holder.named, _holder.Say);
            var ctx = new OpContext();
            var calls = PersistentCalls.Of(_holder, _holder.clicked, ctx);
            Assert.That(calls.Select(c => c.Method), Is.EqualTo(new[] { "Say", "Switch", "Scale", "Ping" }), "in the order made");
            Assert.That(calls.All(c => c.Calls(_holder, c.Method)), Is.True);
            Assert.That(calls[0].Mode, Is.EqualTo(PersistentListenerMode.String));
            Assert.That(calls[0].Argument, Is.EqualTo("opened"));
            Assert.That(calls[1].Argument, Is.EqualTo(true));
            Assert.That(calls[2].Argument, Is.NaN);
            Assert.That(calls[3].Argument, Is.Null, "a call passing nothing");
            var named = PersistentCalls.Of(_holder, nameof(TestEventHolder.named), ctx);
            Assert.That(named.Single().Mode, Is.EqualTo(PersistentListenerMode.EventDefined));
            Assert.That(named.Single().Argument, Is.Null, "a call passing the event's own argument");
            Assert.That(ctx.Issues, Is.Empty);
        }

        [Test]
        public void An_event_in_a_private_serialized_field_is_read()
        {
            UnityEventTools.AddVoidPersistentListener(_holder.Kept, _holder.Ping);
            Assert.That(PersistentCalls.Of(_holder, _holder.Kept).Single().Calls(_holder, nameof(TestEventHolder.Ping)), Is.True);
        }

        [Test]
        public void An_event_it_cannot_read_is_raised_and_read_as_nothing()
        {
            var ctx = new OpContext();
            Assert.That(PersistentCalls.Of(_holder, _holder.unsaved, ctx), Is.Null, "no serialized field holds it");
            Assert.That(PersistentCalls.Of(_holder, "nothing", ctx), Is.Null, "no event at that path");
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { PersistentCalls.UnreadableCode, PersistentCalls.UnreadableCode }));
        }

        [Test]
        public void An_event_a_base_class_holds_is_read()
        {
            UnityEventTools.AddVoidPersistentListener(_holder.Inherited, _holder.Ping);
            Assert.That(PersistentCalls.Of(_holder, _holder.Inherited).Single().Calls(_holder, nameof(TestEventHolder.Ping)), Is.True,
                        "its field found up the class chain");
        }

        [Test]
        public void A_call_saved_in_a_mode_unity_does_not_define_is_raised_and_read_as_nothing()
        {
            UnityEventTools.AddVoidPersistentListener(_holder.clicked, _holder.Ping);
            using (var saved = new SerializedObject(_holder))
            {
                saved.FindProperty("clicked.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0).FindPropertyRelative("m_Mode").intValue = 99;
                saved.ApplyModifiedPropertiesWithoutUndo();
            }
            var ctx = new OpContext();
            Assert.That(PersistentCalls.Of(_holder, _holder.clicked, ctx), Is.Null);
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { PersistentCalls.UnreadableCode }));
        }

        // Wired is what every wiring action asks before it wires, and was untested: a number given as an int or a double
        // against a float call read "not wired", so the action wired the call again beside it.
        [Test]
        public void A_call_is_wired_by_its_target_method_and_argument_a_number_as_its_mode_saves_one()
        {
            UnityEventTools.AddFloatPersistentListener(_holder.clicked, _holder.Scale, 1f);
            UnityEventTools.AddIntPersistentListener(_holder.clicked, _holder.Count, 3);
            UnityEventTools.AddStringPersistentListener(_holder.clicked, _holder.Say, "opened");
            var ctx = new OpContext();
            bool Wired(string method, object argument = null) => PersistentCalls.Wired(_holder, _holder.clicked, _holder, method, argument, ctx);
            Assert.That(Wired("Scale"), Is.True, "whatever it passes");
            Assert.That(Wired("Scale", 1f), Is.True);
            Assert.That(Wired("Scale", 1), Is.True, "an int against a float call");
            Assert.That(Wired("Scale", 1.0), Is.True, "a double against a float call");
            Assert.That(Wired("Scale", 2f), Is.False, "another value");
            Assert.That(Wired("Count", 3L), Is.True, "a long against an int call");
            Assert.That(Wired("Count", 3.5), Is.False, "no whole number");
            Assert.That(Wired("Say", "opened"), Is.True);
            Assert.That(Wired("Say", "closed"), Is.False);
            Assert.That(Wired("Say", 1), Is.False, "a number, where it passes a string");
            Assert.That(Wired("Ping"), Is.False, "a method it does not call");
            Assert.That(ctx.Issues, Is.Empty);
            Assert.That(PersistentCalls.Wired(_holder, _holder.unsaved, _holder, "Ping", null, ctx), Is.True,
                        "calls it cannot read: never wired again beside them");
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { PersistentCalls.UnreadableCode }));
        }
    }
}
