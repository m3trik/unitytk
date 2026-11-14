using System.Collections.Generic;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    public class ProviderRuleTests
    {
        static int Choose(out string problem, params ProviderCandidate[] candidates)
            => ProviderRule.Choose("IRecordSource", new List<ProviderCandidate>(candidates), out problem);

        [Test]
        public void ARealProviderReplacesTheStandIn()
        {
            int chosen = Choose(out string problem,
                new ProviderCandidate("PureRecordSource", true), new ProviderCandidate("CarrierRecords", false));
            Assert.That(chosen, Is.EqualTo(1));
            Assert.That(problem, Is.Null);
        }

        [Test]
        public void TheStandInServesAloneWhenNothingElseProvides()
        {
            Assert.That(Choose(out string problem, new ProviderCandidate("PureRecordSource", true)), Is.EqualTo(0));
            Assert.That(problem, Is.Null);
        }

        [Test]
        public void TwoRealProvidersAreAnErrorNamingBoth()
        {
            int chosen = Choose(out string problem,
                new ProviderCandidate("A", false), new ProviderCandidate("B", false), new ProviderCandidate("Pure", true));
            Assert.That(chosen, Is.EqualTo(-1));
            Assert.That(problem, Does.Contain("A, B").And.Contain("Provided.All"));
        }

        [Test]
        public void NoProviderNamesTheContractAndTheFix()
        {
            Assert.That(Choose(out string problem), Is.EqualTo(-1));
            Assert.That(problem, Does.Contain("nothing provides IRecordSource").And.Contain("pass one"));
        }

        [Test]
        public void TwoStandInsAreAnError()
        {
            Assert.That(Choose(out string problem, new ProviderCandidate("P", true), new ProviderCandidate("Q", true)),
                        Is.EqualTo(-1));
            Assert.That(problem, Does.Contain("stand-ins"));
        }
    }
}
