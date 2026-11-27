using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// The editor's icon set (the shared SVGs, copied by <c>sync_unity_icons.py</c>): every icon
    /// imports as a vector image, the manifests' status icons are there, an unknown one is
    /// none, and an icon element draws its icon in its tint.
    /// </summary>
    public class VectorIconsTests
    {
        [Test]
        public void Every_copied_icon_imports_as_a_vector_image()
        {
            var names = VectorIcons.Names;
            Assert.That(names.Count, Is.GreaterThan(100), "the shared set, copied");
            foreach (string name in names)
                Assert.That(VectorIcons.Load(name), Is.Not.Null, $"{name}: no vector image");
        }

        [Test]
        public void The_manifests_status_icons_are_there_and_an_unknown_one_is_none()
        {
            foreach (string name in new[] { "error", "warning", "info", "check", "lock", "close", "edit" })
                Assert.That(VectorIcons.Has(name), Is.True, name);
            Assert.That(VectorIcons.Has("no_such_icon"), Is.False);
        }

        [Test]
        public void An_icon_element_draws_its_icon_in_its_tint()
        {
            var element = new VectorIcon("warning", Color.yellow);
            Assert.That(element.vectorImage, Is.SameAs(VectorIcons.Load("warning")));
            Assert.That(element.tint, Is.EqualTo(Color.yellow));
            element.icon = "no_such_icon";
            Assert.That(element.vectorImage, Is.Null, "an unknown icon draws nothing");
        }
    }
}
