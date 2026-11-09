using System;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    public class ImportOrderTests
    {
        [ImportRank(0)]
        interface IFirst { }

        [ImportRank(ImportOrder.MaxRank)]
        interface ILast { }

        [ImportRank(ImportOrder.MaxRank + 1)]
        interface ITooLate { }

        [ImportRank(-1)]
        interface INegative { }

        interface IUnranked { }

        [Test]
        public void AProviderImportsBeforeEveryReaderOfItsContract()
        {
            Assert.That(ImportOrder.Provider(typeof(IFirst)), Is.LessThan(ImportOrder.Reader(typeof(IFirst))));
            Assert.That(ImportOrder.Reader(typeof(IFirst)), Is.LessThan(ImportOrder.Provider(typeof(ILast))));
        }

        [Test]
        public void TheMaterialRewriterImportsAfterUnitysDescriptionPreprocessorsAndEveryRankedImporter()
        {
            // Unity's own: Built-in's at -990, URP's at -980, -970 and -960.
            Assert.That(ImportOrder.AfterMaterialDescriptions, Is.GreaterThan(-960));
            Assert.That(ImportOrder.AfterMaterialDescriptions, Is.GreaterThan(ImportOrder.Reader(typeof(ILast))));
        }

        [Test]
        public void ARankThatWouldReachTheMaterialRewriterIsRefused()
        {
            Assert.That(ImportOrder.MaxRank, Is.EqualTo(49));
            Assert.That(ImportOrder.Reader(typeof(ILast)), Is.LessThan(ImportOrder.AfterMaterialDescriptions));
            var e = Assert.Throws<ArgumentException>(() => ImportOrder.Reader(typeof(ITooLate)));
            Assert.That(e.Message, Does.Contain("a rank runs from 0 to 49"));
            Assert.Throws<ArgumentException>(() => ImportOrder.Provider(typeof(ITooLate)));
            Assert.Throws<ArgumentException>(() => ImportOrder.Provider(typeof(INegative)));
        }

        [Test]
        public void AContractWithNoRankIsRefusedNamingTheFix()
        {
            var e = Assert.Throws<ArgumentException>(() => ImportOrder.Reader(typeof(IUnranked)));
            Assert.That(e.Message, Does.Contain("declares no [ImportRank]"));
        }
    }
}
