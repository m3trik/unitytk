using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using M3trik.Unitytk.Core;
using UnityEditor;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// Every <see cref="OperationAttribute"/> method the editor's assemblies hold,
    /// by id: how each is described (its catalog entry, with the argument schema
    /// its parameters are) and called (its JSON arguments bound to those
    /// parameters, as pythontk's host binds a Python function's).
    /// </summary>
    /// <remarks>
    /// Prior art: <c>TypeCache.GetMethodsWithAttribute</c> is Unity's own index of
    /// attributed methods, built at compile time (no assembly scan), and refreshed
    /// with every domain reload, so a registry over it is rebuilt for free.
    /// </remarks>
    internal static class OperationRegistry
    {
        /// <summary>One registered operation.</summary>
        public sealed class Operation
        {
            internal Operation(OperationAttribute attribute, MethodInfo method)
            {
                Attribute = attribute;
                Method = method;
                TakesContext = method.GetParameters().Any(p => p.ParameterType == typeof(OpContext));
            }

            /// <summary>Its id.</summary>
            public string Id => Attribute.Id;

            /// <summary>Its declaration.</summary>
            public OperationAttribute Attribute { get; }

            /// <summary>The static method it calls.</summary>
            public MethodInfo Method { get; }

            /// <summary>Whether it takes an <see cref="OpContext"/>: it can plan (a dry run).</summary>
            public bool TakesContext { get; }
        }

        /// <summary>The largest id length: Claude caps a tool name at 64 characters.</summary>
        public const int IdMax = 34;

        static Dictionary<string, Operation> _byId;
        static List<string> _problems;

        /// <summary>Every operation, by id (built once per domain).</summary>
        public static IReadOnlyDictionary<string, Operation> All
        {
            get
            {
                Build();
                return _byId;
            }
        }

        /// <summary>Why a declaration was left out (a duplicate id, an instance method, a bad id).</summary>
        public static IReadOnlyList<string> Problems
        {
            get
            {
                Build();
                return _problems;
            }
        }

        /// <summary>The operation <paramref name="id"/> names, or null.</summary>
        public static Operation Find(string id)
        {
            Build();
            return id != null && _byId.TryGetValue(id, out Operation op) ? op : null;
        }

        static void Build()
        {
            if (_byId != null) return;
            var byId = new Dictionary<string, Operation>();
            var problems = new List<string>();
            foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<OperationAttribute>())
            {
                var attribute = method.GetCustomAttribute<OperationAttribute>();
                string where = $"{method.DeclaringType?.FullName}.{method.Name}";
                if (!method.IsStatic)
                    problems.Add($"{where}: an operation is a static method");
                else if (!IdIsValid(attribute.Id))
                    problems.Add($"{where}: id '{attribute.Id}' is not 'unity.<area>.<action>' in lower case, at most {IdMax} characters");
                else if (byId.TryGetValue(attribute.Id, out Operation first))
                    problems.Add($"{where}: id '{attribute.Id}' is declared by {first.Method.DeclaringType?.FullName}.{first.Method.Name} too; the first is kept");
                else
                    byId[attribute.Id] = new Operation(attribute, method);
            }
            _byId = byId;
            _problems = problems;
        }

        static bool IdIsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > IdMax || !id.StartsWith("unity.", StringComparison.Ordinal))
                return false;
            string[] parts = id.Split('.');
            return parts.Length >= 3 && parts.All(p => p.Length > 0 && char.IsLower(p[0])
                && p.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'));
        }

        // -- calling ------------------------------------------------------------------

        /// <summary>
        /// Call <paramref name="op"/> with <paramref name="args"/> (a JSON object's tree)
        /// bound to its parameters by name, and <paramref name="ctx"/> where it takes one.
        /// </summary>
        /// <exception cref="ArgumentException">An unknown or missing argument, or a value of another type.</exception>
        public static object Invoke(Operation op, IDictionary<string, object> args, OpContext ctx)
        {
            args = args ?? new Dictionary<string, object>();
            ParameterInfo[] parameters = op.Method.GetParameters();
            var known = new HashSet<string>(parameters.Where(p => p.ParameterType != typeof(OpContext)).Select(p => p.Name));
            foreach (string name in args.Keys)
                if (!known.Contains(name))
                    throw new ArgumentException($"{op.Id} takes no argument '{name}' (it takes: {string.Join(", ", known)})");
            var values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo p = parameters[i];
                if (p.ParameterType == typeof(OpContext))
                    values[i] = ctx;
                else if (args.TryGetValue(p.Name, out object value))
                    values[i] = Convert(value, p.ParameterType, p.Name);
                else if (p.HasDefaultValue)
                    values[i] = p.DefaultValue;
                else
                    throw new ArgumentException($"{op.Id} needs the argument '{p.Name}'");
            }
            try
            {
                return op.Method.Invoke(null, values);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        static object Convert(object value, Type type, string name)
        {
            Type underlying = Nullable.GetUnderlyingType(type);
            if (value == null)
            {
                if (!type.IsValueType || underlying != null) return null;
                throw new ArgumentException($"'{name}' takes a {Describe(type)}, not null");
            }
            type = underlying ?? type;
            if (type == typeof(object) || type.IsInstanceOfType(value) && !(value is IList<object>)) return value;
            if (type == typeof(string)) return value as string ?? throw Wrong(name, type, value);
            if (type == typeof(bool)) return value is bool b ? b : throw Wrong(name, type, value);
            if (type.IsEnum)
            {
                if (value is string text && Enum.GetNames(type).Contains(text)) return Enum.Parse(type, text);
                throw new ArgumentException($"'{name}' is one of {string.Join(", ", Enum.GetNames(type))}, not {value}");
            }
            if (type == typeof(double) || type == typeof(float))
            {
                if (!(value is double || value is long || value is decimal)) throw Wrong(name, type, value);
                double d = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (type == typeof(double)) return d;
                if (Math.Abs(d) > float.MaxValue && !double.IsInfinity(d))
                    throw new ArgumentException($"'{name}' takes a number a float holds, not {Json.Write(value)}");
                return (float)d;
            }
            if (type == typeof(int) || type == typeof(long))
            {
                // Never cast: a fraction, or a value past the parameter's range, is refused.
                long whole;
                if (value is long l) whole = l;
                else if (value is decimal m && m == decimal.Truncate(m) && m >= long.MinValue && m <= long.MaxValue) whole = (long)m;
                else if (value is double d && d == Math.Floor(d) && Math.Abs(d) <= 9007199254740992d) whole = (long)d;
                else throw Wrong(name, type, value);
                if (type == typeof(int) && (whole < int.MinValue || whole > int.MaxValue))
                    throw new ArgumentException($"'{name}' takes a whole number an int holds, not {whole}");
                return type == typeof(int) ? (object)(int)whole : whole;
            }
            if (typeof(IDictionary<string, object>).IsAssignableFrom(type))
                return value as IDictionary<string, object> ?? throw Wrong(name, type, value);
            Type item = ItemType(type);
            if (item != null && value is IList<object> list)
            {
                IList items = type.IsArray
                    ? (IList)Array.CreateInstance(item, list.Count)
                    : (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
                for (int i = 0; i < list.Count; i++)
                {
                    object converted = Convert(list[i], item, $"{name}[{i}]");
                    if (type.IsArray) items[i] = converted;
                    else items.Add(converted);
                }
                return items;
            }
            throw Wrong(name, type, value);
        }

        static ArgumentException Wrong(string name, Type type, object value)
            => new ArgumentException($"'{name}' takes a {Describe(type)}, not {Json.Write(JsonValue.From(value))}");

        static string Describe(Type type)
        {
            Type item = ItemType(type);
            if (item != null) return $"list of {Describe(item)}";
            if (type == typeof(int) || type == typeof(long)) return "whole number";
            if (type == typeof(double) || type == typeof(float)) return "number";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(string)) return "string";
            return type.Name;
        }

        static Type ItemType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>)
                                       || type.GetGenericTypeDefinition() == typeof(IList<>)
                                       || type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)))
                return type.GetGenericArguments()[0];
            return null;
        }

        // -- describing ---------------------------------------------------------------

        /// <summary><paramref name="op"/>'s catalog entry: pythontk's <c>OperationEntry</c>.</summary>
        public static OperationEntry Describe(Operation op)
        {
            OperationAttribute a = op.Attribute;
            string summary = a.Summary ?? "";
            var properties = new Dictionary<string, object>();
            var required = new List<object>();
            foreach (ParameterInfo p in op.Method.GetParameters())
            {
                if (p.ParameterType == typeof(OpContext)) continue;
                Dictionary<string, object> schema = Schema(p.ParameterType, p.HasDefaultValue && p.DefaultValue == null);
                string description = p.GetCustomAttribute<DescriptionAttribute>()?.Description;
                if (!string.IsNullOrEmpty(description)) schema["description"] = description;
                if (p.HasDefaultValue) schema["default"] = JsonValue.From(p.DefaultValue);
                else required.Add(p.Name);
                properties[p.Name] = schema;
            }
            // In the order pythontk's schema writes them, so both catalogs read alike.
            var args = new Dictionary<string, object>
            {
                ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false,
            };
            if (required.Count > 0) args["required"] = required;
            Type returns = op.Method.ReturnType;
            return new OperationEntry
            {
                id = a.Id,
                title = string.IsNullOrEmpty(a.Title) ? summary.TrimEnd('.') : a.Title,
                summary = summary,
                effect = a.Effect.ToString().ToLowerInvariant(),
                host = "unity",
                target = $"{op.Method.DeclaringType?.FullName}.{op.Method.Name}",
                dry_run = a.Effect == Effect.Read || op.TakesContext,
                args = args,
                result = returns == typeof(void) ? null : Schema(returns, false),
            };
        }

        static Dictionary<string, object> Schema(Type type, bool nullable)
        {
            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) return Schema(underlying, true);
            Dictionary<string, object> schema;
            Type item = ItemType(type);
            if (type == typeof(string)) schema = Typed("string");
            else if (type == typeof(bool)) schema = Typed("boolean");
            else if (type == typeof(int) || type == typeof(long)) schema = Typed("integer");
            else if (type == typeof(double) || type == typeof(float)) schema = Typed("number");
            else if (type.IsEnum) schema = new Dictionary<string, object> { ["type"] = "string", ["enum"] = Enum.GetNames(type).Cast<object>().ToList() };
            else if (item != null) schema = new Dictionary<string, object> { ["type"] = "array", ["items"] = Schema(item, false) };
            else schema = Typed("object");
            if (!nullable) return schema;
            return new Dictionary<string, object>
            {
                ["anyOf"] = new List<object> { schema, Typed("null") },
            };
        }

        static Dictionary<string, object> Typed(string type) => new Dictionary<string, object> { ["type"] = type };
    }
}
