using System;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// Marks a class as a provider of a contract, so a need for that contract
    /// (<see cref="Provided.One{T}"/>) is served by it. The class implements the
    /// contract and has a public constructor with no parameters; a contract's
    /// stand-in is marked <see cref="StandIn"/>, and any real provider replaces it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ProvidesAttribute : Attribute
    {
        /// <param name="contract">The contract (an interface) this class provides.</param>
        public ProvidesAttribute(Type contract)
        {
            Contract = contract;
        }

        /// <summary>The contract this class provides.</summary>
        public Type Contract { get; }

        /// <summary>Whether this is the contract's stand-in, which a real provider replaces.</summary>
        public bool StandIn { get; set; }
    }
}
