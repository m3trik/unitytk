using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The persistent calls an event saves, read off the object holding it through its
    /// <see cref="SerializedObject"/>: each call's target, method, mode and the argument it passes, which the
    /// event itself has no getter for. What an action reads before it wires a call, so a second run adds none.
    /// </summary>
    /// <remarks>
    /// Prior art: <c>UnityEventTools</c> adds and removes calls but reads none back;
    /// <see cref="UnityEventBase.GetPersistentTarget"/> and <see cref="UnityEventBase.GetPersistentMethodName"/>
    /// say what a call calls, not what it passes; the event's JSON (<c>JsonUtility.ToJson</c>) is a text to parse
    /// again. The owner's <see cref="SerializedObject"/> is Unity's own typed view of what it saves.
    /// </remarks>
    public static class PersistentCalls
    {
        /// <summary>An event's saved calls cannot be read: no serialized field of its owner holds it, or a call's mode is none Unity defines.</summary>
        public const string UnreadableCode = "unity.events.unreadable";

        /// <summary>One saved call: what it calls, how, and the argument it passes.</summary>
        public readonly struct Call
        {
            /// <summary>A call of <paramref name="method"/> on <paramref name="target"/>, passing <paramref name="argument"/> as <paramref name="mode"/> says.</summary>
            /// <param name="target">The object it calls.</param>
            /// <param name="method">The method's name.</param>
            /// <param name="mode">What it passes.</param>
            /// <param name="argument">The argument it passes (<see cref="Argument"/>).</param>
            public Call(Object target, string method, PersistentListenerMode mode, object argument)
            {
                Target = target;
                Method = method;
                Mode = mode;
                Argument = argument;
            }

            /// <summary>The object it calls.</summary>
            public Object Target { get; }

            /// <summary>The method's name (a property's setter: <c>set_&lt;name&gt;</c>).</summary>
            public string Method { get; }

            /// <summary>What it passes: the event's own arguments, none, or one value of its own.</summary>
            public PersistentListenerMode Mode { get; }

            /// <summary>
            /// The value it passes, as its <see cref="Mode"/> says: a string, bool, int, float or Unity object; null where it
            /// passes the event's own arguments or none.
            /// </summary>
            public object Argument { get; }

            /// <summary>Whether it calls <paramref name="method"/> on <paramref name="target"/>.</summary>
            /// <param name="target">The object called.</param>
            /// <param name="method">The method's name.</param>
            public bool Calls(Object target, string method) => Target == target && Method == method;
        }

        /// <summary>
        /// The calls <paramref name="when"/> saves, an event in one of <paramref name="owner"/>'s own serialized fields;
        /// null, raised (<see cref="UnreadableCode"/>), where none of them holds it.
        /// </summary>
        /// <param name="owner">The object holding the event (a component, an asset).</param>
        /// <param name="when">The event.</param>
        /// <param name="ctx">The run's context, which a problem is raised on.</param>
        /// <returns>The calls, in the order the event makes them.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="when"/> is null.</exception>
        public static IReadOnlyList<Call> Of(Object owner, UnityEventBase when, OpContext ctx = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner), "name the object holding the event");
            if (when == null) throw new ArgumentNullException(nameof(when), "name the event");
            string field = FieldOf(owner, when);
            if (field != null) return Of(owner, field, ctx);
            ctx.Raise(UnreadableCode, $"'{owner.name}' holds the event in none of its own serialized fields: pass the object whose field it is");
            return null;
        }

        /// <summary>
        /// Whether <paramref name="when"/>, an event in one of <paramref name="owner"/>'s own serialized fields, already
        /// calls <paramref name="method"/> on <paramref name="to"/> (passing <paramref name="argument"/>, where one is given;
        /// null: whatever it passes); true too where its calls cannot be read (raised: <see cref="UnreadableCode"/>), so an
        /// action never wires a call it cannot see beside the ones there. What an action asks before it wires a call. A
        /// number is compared as the call's mode saves one (a float call passing 1 is passed 1, 1.0 and 1f alike; an int
        /// call, a whole number of any type), anything else as itself.
        /// </summary>
        /// <param name="owner">The object holding the event (a component, an asset).</param>
        /// <param name="when">The event.</param>
        /// <param name="to">The object the call calls.</param>
        /// <param name="method">The method's name (a property's setter: <c>set_&lt;name&gt;</c>).</param>
        /// <param name="argument">The value the call passes; null: any.</param>
        /// <param name="ctx">The run's context, which a problem is raised on.</param>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="when"/> is null.</exception>
        public static bool Wired(Object owner, UnityEventBase when, Object to, string method, object argument = null, OpContext ctx = null)
        {
            IReadOnlyList<Call> calls = Of(owner, when, ctx);
            if (calls == null) return true;
            foreach (Call call in calls)
                if (call.Calls(to, method) && (argument == null || Passes(call, argument))) return true;
            return false;
        }

        // Whether call passes argument: a number as the call's mode saves one, anything else as itself.
        static bool Passes(Call call, object argument)
        {
            if (!Number(argument)) return Equals(call.Argument, argument);
            double value = Convert.ToDouble(argument, CultureInfo.InvariantCulture);
            switch (call.Mode)
            {
                case PersistentListenerMode.Float: return ((float)call.Argument).Equals((float)value);
                case PersistentListenerMode.Int: return (int)call.Argument == value;
                default: return false;  // a number, and the call passes none
            }
        }

        static bool Number(object value)
        {
            if (!(value is IConvertible convertible)) return false;
            TypeCode code = convertible.GetTypeCode();
            return code >= TypeCode.SByte && code <= TypeCode.Decimal;
        }

        /// <summary>
        /// The calls the event <paramref name="owner"/> saves at <paramref name="path"/> (a field's name, or a
        /// <see cref="SerializedProperty.propertyPath"/>); null, raised (<see cref="UnreadableCode"/>), where it holds no
        /// event there or a call's mode is none Unity defines.
        /// </summary>
        /// <param name="owner">The object holding the event (a component, an asset).</param>
        /// <param name="path">Where it holds it.</param>
        /// <param name="ctx">The run's context, which a problem is raised on.</param>
        /// <returns>The calls, in the order the event makes them.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
        public static IReadOnlyList<Call> Of(Object owner, string path, OpContext ctx = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner), "name the object holding the event");
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("name where it holds the event", nameof(path));
            using var saved = new SerializedObject(owner);
            SerializedProperty list = saved.FindProperty($"{path}.m_PersistentCalls.m_Calls");
            if (list == null || !list.isArray)
            {
                ctx.Raise(UnreadableCode, $"'{owner.name}' holds no event at '{path}': name a UnityEvent field of it");
                return null;
            }
            var calls = new List<Call>(list.arraySize);
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty call = list.GetArrayElementAtIndex(i);
                var mode = (PersistentListenerMode)call.FindPropertyRelative("m_Mode").intValue;
                if (!Enum.IsDefined(typeof(PersistentListenerMode), mode))
                {
                    ctx.Raise(UnreadableCode, $"'{owner.name}''s event at '{path}' saves call {i} in mode {(int)mode}, which Unity does not define: set it again in its inspector");
                    return null;
                }
                calls.Add(new Call(call.FindPropertyRelative("m_Target").objectReferenceValue,
                                   call.FindPropertyRelative("m_MethodName").stringValue, mode,
                                   Argument(call.FindPropertyRelative("m_Arguments"), mode)));
            }
            return calls;
        }

        // The value a call in mode passes, off its saved arguments.
        static object Argument(SerializedProperty arguments, PersistentListenerMode mode)
        {
            switch (mode)
            {
                case PersistentListenerMode.String: return arguments.FindPropertyRelative("m_StringArgument").stringValue;
                case PersistentListenerMode.Bool: return arguments.FindPropertyRelative("m_BoolArgument").boolValue;
                case PersistentListenerMode.Int: return arguments.FindPropertyRelative("m_IntArgument").intValue;
                case PersistentListenerMode.Float: return arguments.FindPropertyRelative("m_FloatArgument").floatValue;
                case PersistentListenerMode.Object: return arguments.FindPropertyRelative("m_ObjectArgument").objectReferenceValue;
                default: return null;  // the event's own arguments, or none
            }
        }

        // The name of owner's own serialized field (its class's or a base class's) holding when; null where none does.
        static string FieldOf(Object owner, UnityEventBase when)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = owner.GetType(); type != null && type != typeof(Object); type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(fields))
                    if ((field.IsPublic || field.IsDefined(typeof(SerializeField), false)) && !field.IsNotSerialized
                        && ReferenceEquals(field.GetValue(owner), when))
                        return field.Name;
            return null;
        }
    }
}
