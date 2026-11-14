using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// The import order over the package's real contracts (none, while no import reads
    /// another's result): each one that declares an
    /// <see cref="ImportRankAttribute"/> ranks within 0 to <see cref="ImportOrder.MaxRank"/>,
    /// so a bad declaration fails here, not at an import, where the order refuses it
    /// only once an importer asks. The table's rules are held engine-free, over stand-in
    /// contracts (<c>tests/core_dotnet</c>).
    /// </summary>
    public class ImportOrderTests
    {
        [Test]
        public void Every_ranked_contract_in_the_package_ranks_within_the_table()
        {
            List<Type> ranked = TypeCache.GetTypesWithAttribute<ImportRankAttribute>()
                .Where(t => IsPackageCode(t.Assembly)).ToList();
            foreach (Type contract in ranked)
            {
                Assert.That(contract.GetCustomAttribute<ImportRankAttribute>().Rank, Is.InRange(0, ImportOrder.MaxRank),
                            contract.FullName);
                Assert.That(ImportOrder.Provider(contract), Is.LessThan(ImportOrder.Reader(contract)), contract.FullName);
                Assert.That(ImportOrder.Reader(contract), Is.LessThan(ImportOrder.AfterMaterialDescriptions), contract.FullName);
            }
        }

        // The package's own assemblies, its tests excepted: a test may declare a bad rank to see it refused.
        static bool IsPackageCode(Assembly assembly)
        {
            string name = assembly.GetName().Name;
            return name.StartsWith("M3trik.Unitytk", StringComparison.Ordinal)
                   && !name.EndsWith(".Tests", StringComparison.Ordinal);
        }
    }
}
