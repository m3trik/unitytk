using System.Linq;
using NUnit.Framework;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>A contract the registry tests provide, with its stand-in and one real provider.</summary>
    public interface IGreeting
    {
        /// <summary>What the provider says.</summary>
        string Say();
    }

    /// <summary>The stand-in: what a need gets when nothing real provides it.</summary>
    [Provides(typeof(IGreeting), StandIn = true)]
    public sealed class PureGreeting : IGreeting
    {
        /// <inheritdoc/>
        public string Say() => "stand-in";
    }

    /// <summary>The real provider, which replaces the stand-in.</summary>
    [Provides(typeof(IGreeting))]
    public sealed class RealGreeting : IGreeting
    {
        /// <inheritdoc/>
        public string Say() => "real";
    }

    /// <summary>The provider registry over the attribute, as TypeCache finds it.</summary>
    public class ProvidedTests
    {
        [Test]
        public void A_need_is_served_by_the_real_provider_over_the_stand_in()
        {
            Assert.That(Provided.One<IGreeting>().Say(), Is.EqualTo("real"));
            Assert.That(Provided.One<IGreeting>(), Is.SameAs(Provided.One<IGreeting>()), "one instance per domain");
        }

        [Test]
        public void All_lists_the_real_providers_and_no_stand_in()
        {
            Assert.That(Provided.All<IGreeting>().Select(g => g.Say()), Is.EqualTo(new[] { "real" }));
        }

        [Test]
        public void A_contract_nothing_provides_fails_naming_it()
        {
            var e = Assert.Throws<ProviderException>(() => Provided.One<System.IDisposable>());
            Assert.That(e.Message, Does.Contain("nothing provides System.IDisposable"));
        }

        [Test]
        public void The_doctor_sees_no_problem_in_a_well_declared_contract()
        {
            Assert.That(Provided.Problems().Where(p => p.Contains(nameof(IGreeting))), Is.Empty);
        }
    }
}
