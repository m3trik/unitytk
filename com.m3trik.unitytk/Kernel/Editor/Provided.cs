using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using M3trik.Unitytk.Core;
using UnityEditor;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The provider registry: what serves each contract in this editor. An action's
    /// need is a parameter of a contract type that defaults to its provider
    /// (<c>if (records == null) records = Provided.One&lt;IRecordSource&gt;();</c>),
    /// so a test passes a stand-in and every other caller passes nothing.
    /// </summary>
    /// <remarks>
    /// Prior art: <c>TypeCache.GetTypesWithAttribute</c> is Unity's own index of
    /// attributed types, built at compile time and refreshed with every domain
    /// reload, so the registry needs no assembly scan. Container libraries
    /// (Zenject, VContainer) were weighed and not adopted: a runtime wiring
    /// framework, where Unity's components are the grain. Instances live for the
    /// domain: the one kind of mutable static state the C# rules allow, a registry
    /// rebuilt after a reload.
    /// </remarks>
    public static class Provided
    {
        static readonly Dictionary<Type, object> _instances = new Dictionary<Type, object>();

        /// <summary>The one provider of <typeparamref name="T"/>.</summary>
        /// <exception cref="ProviderException">Nothing provides it, or two real providers do: the message names the fix.</exception>
        public static T One<T>() where T : class
        {
            // A real provider that cannot serve (no public constructor, not the
            // contract's type) is an error, never a silent fall back to the stand-in.
            foreach (Type type in TypeCache.GetTypesWithAttribute<ProvidesAttribute>())
                foreach (ProvidesAttribute provides in type.GetCustomAttributes<ProvidesAttribute>())
                    if (provides.Contract == typeof(T) && !provides.StandIn && Invalid(type, typeof(T)) is string invalid)
                        throw new ProviderException(invalid + ": fix it, or the stand-in would serve in its place");
            List<(Type type, ProvidesAttribute provides)> candidates = Candidates(typeof(T));
            int chosen = ProviderRule.Choose(Name(typeof(T)), Describe(candidates), out string problem);
            if (chosen < 0) throw new ProviderException(problem);
            return (T)Instance(candidates[chosen].type);
        }

        /// <summary>Every real provider of <typeparamref name="T"/> (no stand-in), for a need that takes them all.</summary>
        public static IReadOnlyList<T> All<T>() where T : class
            => Candidates(typeof(T)).Where(c => !c.provides.StandIn).Select(c => (T)Instance(c.type)).ToList();

        /// <summary>
        /// What is wrong with the providers in this editor, for the project doctor: a
        /// contract two real providers serve, or a provider that does not implement its
        /// contract or has no public constructor without parameters.
        /// </summary>
        public static IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            var byContract = new Dictionary<Type, List<(Type type, ProvidesAttribute provides)>>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<ProvidesAttribute>())
                foreach (ProvidesAttribute provides in type.GetCustomAttributes<ProvidesAttribute>())
                {
                    string invalid = Invalid(type, provides.Contract);
                    if (invalid != null)
                    {
                        problems.Add(invalid);
                        continue;
                    }
                    if (!byContract.TryGetValue(provides.Contract, out var list))
                        byContract[provides.Contract] = list = new List<(Type type, ProvidesAttribute provides)>();
                    list.Add((type, provides));
                }
            foreach (var entry in byContract.OrderBy(e => Name(e.Key), StringComparer.Ordinal))
                if (ProviderRule.Choose(Name(entry.Key), Describe(entry.Value), out string problem) < 0)
                    problems.Add(problem);
            return problems;
        }

        static List<(Type type, ProvidesAttribute provides)> Candidates(Type contract)
        {
            var found = new List<(Type type, ProvidesAttribute provides)>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<ProvidesAttribute>())
                foreach (ProvidesAttribute provides in type.GetCustomAttributes<ProvidesAttribute>())
                    if (provides.Contract == contract && Invalid(type, contract) == null)
                        found.Add((type, provides));
            return found.OrderBy(c => c.type.FullName, StringComparer.Ordinal).ToList();
        }

        static List<ProviderCandidate> Describe(IEnumerable<(Type type, ProvidesAttribute provides)> candidates)
            => candidates.Select(c => new ProviderCandidate(c.type.FullName, c.provides.StandIn)).ToList();

        static string Invalid(Type type, Type contract)
        {
            if (contract == null) return $"{type.FullName}: [Provides] names no contract";
            if (!contract.IsAssignableFrom(type))
                return $"{type.FullName} provides {Name(contract)} but does not implement it";
            if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                return $"{type.FullName} provides {Name(contract)} but has no public constructor without parameters";
            return null;
        }

        static object Instance(Type type)
        {
            if (!_instances.TryGetValue(type, out object instance))
                _instances[type] = instance = Activator.CreateInstance(type);
            return instance;
        }

        static string Name(Type contract) => contract.FullName ?? contract.Name;
    }

    /// <summary>A need no provider can serve: its message names the missing or doubled provider and the fix.</summary>
    public sealed class ProviderException : InvalidOperationException
    {
        /// <param name="message">What is wrong and what to do.</param>
        public ProviderException(string message) : base(message) { }
    }
}
