using M3trik.Unitytk.Shots;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.ShotListNaming.Tests
{
    /// <summary>
    /// A reader in any other <c>M3trik.Unitytk.*</c> namespace names the component as
    /// it names any type. Named <c>Shots</c>, like its namespace, it resolved to the
    /// namespace there (CS0118: 'Shots' is a namespace but is used like a type).
    /// </summary>
    public class TheComponentIsNamedPlainlyTests
    {
        [Test]
        public void A_reader_in_another_unitytk_namespace_names_the_component()
        {
            var root = new GameObject("Model");
            try
            {
                Assert.That(root.AddComponent<ShotList>().entries, Is.Empty);
                Assert.That(typeof(ShotList).Namespace, Is.EqualTo("M3trik.Unitytk.Shots"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
