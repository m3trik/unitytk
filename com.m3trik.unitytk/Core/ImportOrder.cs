using System;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// Declares a contract that importers read during an import, and its rank
    /// among such contracts: the one place the import order is declared. An
    /// importer that provides the contract runs before every importer that reads
    /// it (<see cref="ImportOrder"/>): a component one feature's importer attaches
    /// that another's reads in the same import.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, Inherited = false)]
    public sealed class ImportRankAttribute : Attribute
    {
        /// <param name="rank">0 for a contract no other import-time contract's provider reads; one more than the highest it reads otherwise; at most <see cref="ImportOrder.MaxRank"/>.</param>
        public ImportRankAttribute(int rank)
        {
            Rank = rank;
        }

        /// <summary>The contract's rank: lower ranks import first.</summary>
        public int Rank { get; }
    }

    /// <summary>
    /// The import-order table: an <c>AssetPostprocessor</c>'s
    /// <c>GetPostprocessOrder</c>, from the contract it provides or reads, so no
    /// importer spells a bare number and the order is declared once, on the
    /// contracts (<see cref="ImportRankAttribute"/>).
    /// </summary>
    /// <remarks>
    /// Prior art: Unity orders postprocessors by <c>GetPostprocessOrder</c> alone
    /// (lower first), with no notion of a dependency; a table of numbers per
    /// feature would name every feature in the foundation. Ranks on the contracts
    /// keep the foundation feature-free.
    /// </remarks>
    public static class ImportOrder
    {
        /// <summary>The order of an importer that provides <paramref name="contract"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="contract"/> is null.</exception>
        /// <exception cref="ArgumentException">The contract declares no <see cref="ImportRankAttribute"/>, or
        /// a rank outside 0 to <see cref="MaxRank"/>.</exception>
        public static int Provider(Type contract) => Rank(contract) * 2;

        /// <summary>The order of an importer that reads <paramref name="contract"/>: after every provider of it.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="contract"/> is null.</exception>
        /// <exception cref="ArgumentException">The contract declares no <see cref="ImportRankAttribute"/>, or
        /// a rank outside 0 to <see cref="MaxRank"/>.</exception>
        public static int Reader(Type contract) => Rank(contract) * 2 + 1;

        /// <summary>
        /// The order of an importer that rewrites the materials Unity's own material
        /// description preprocessors made of a model (<c>OnPreprocessMaterialDescription</c>):
        /// after every one of them -- Built-in's at -990, URP's at -980, -970 and -960
        /// (measured, Unity 6000.3) -- and after every importer a contract's rank orders
        /// (<see cref="Provider"/>, <see cref="Reader"/>; ranks stay at or below <see cref="MaxRank"/>), so its
        /// material is the one the import keeps.
        /// </summary>
        public const int AfterMaterialDescriptions = 100;

        /// <summary>
        /// The highest rank a contract may declare: its readers' order stays below
        /// <see cref="AfterMaterialDescriptions"/>, so the material rewriter runs after them all.
        /// </summary>
        public const int MaxRank = (AfterMaterialDescriptions - 1) / 2;

        static int Rank(Type contract)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            var rank = (ImportRankAttribute)Attribute.GetCustomAttribute(contract, typeof(ImportRankAttribute));
            if (rank == null)
                throw new ArgumentException(
                    $"{contract.FullName} declares no [ImportRank]: a contract read during an import declares its rank");
            if (rank.Rank < 0 || rank.Rank > MaxRank)
                throw new ArgumentException(
                    $"{contract.FullName} declares [ImportRank({rank.Rank})]: a rank runs from 0 to {MaxRank}, so its " +
                    $"readers import before the material rewriter ({nameof(AfterMaterialDescriptions)}, {AfterMaterialDescriptions})");
            return rank.Rank;
        }
    }
}
