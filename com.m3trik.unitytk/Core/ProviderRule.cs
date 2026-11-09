using System;
using System.Collections.Generic;
using System.Linq;

namespace M3trik.Unitytk.Core
{
    /// <summary>One type that offers to serve a contract: its name and whether it is the contract's stand-in.</summary>
    public readonly struct ProviderCandidate
    {
        /// <param name="name">The provider type's full name.</param>
        /// <param name="standIn">Whether it is the contract's stand-in, which any real provider replaces.</param>
        public ProviderCandidate(string name, bool standIn)
        {
            Name = name;
            StandIn = standIn;
        }

        /// <summary>The provider type's full name.</summary>
        public string Name { get; }

        /// <summary>Whether it is the contract's stand-in.</summary>
        public bool StandIn { get; }
    }

    /// <summary>
    /// Which provider serves a need, by the kernel's one rule: exactly one real
    /// provider serves it, else the stand-in; a contract with two real providers,
    /// or two stand-ins, is an error that names them, unless the need asks for all
    /// of them as a list. Held here, with no engine, so <c>dotnet test</c> proves it.
    /// </summary>
    public static class ProviderRule
    {
        /// <summary>The index of the provider that serves <paramref name="contract"/>, or -1 with the problem.</summary>
        /// <param name="contract">The contract's name, for the problem's message.</param>
        /// <param name="candidates">Every type that provides it.</param>
        /// <param name="problem">Why none serves it (and the fix), or null.</param>
        public static int Choose(string contract, IReadOnlyList<ProviderCandidate> candidates, out string problem)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            int[] real = Indexes(candidates, false);
            int[] standIns = Indexes(candidates, true);
            problem = null;
            if (real.Length == 1) return real[0];
            if (real.Length > 1)
            {
                problem = $"{contract} has {real.Length} providers ({Names(candidates, real)}): keep one, " +
                          "or take them all as a list (Provided.All)";
                return -1;
            }
            if (standIns.Length == 1) return standIns[0];
            problem = standIns.Length > 1
                ? $"{contract} has {standIns.Length} stand-ins ({Names(candidates, standIns)}): a contract ships one"
                : $"nothing provides {contract}: install the feature that provides it, or pass one to the call";
            return -1;
        }

        static int[] Indexes(IReadOnlyList<ProviderCandidate> candidates, bool standIn)
            => Enumerable.Range(0, candidates.Count).Where(i => candidates[i].StandIn == standIn).ToArray();

        static string Names(IReadOnlyList<ProviderCandidate> candidates, int[] indexes)
            => string.Join(", ", indexes.Select(i => candidates[i].Name));
    }
}
