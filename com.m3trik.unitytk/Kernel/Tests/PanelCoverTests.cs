using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>What a panel in front of the eyes hides: a point behind it inside its rectangle, nothing beside or before it.</summary>
    public class PanelCoverTests
    {
        static readonly Vector3 Eyes = Vector3.zero;
        static readonly Vector3 At = new Vector3(0f, 0f, 1f);
        static readonly Vector2 Half = new Vector2(0.3f, 0.2f);

        [Test]
        public void A_point_behind_the_panel_is_hidden_and_one_beside_or_before_it_is_not()
        {
            Quaternion facing = Quaternion.LookRotation(At - Eyes);
            Assert.That(PanelCover.Hides(Eyes, At, facing, Half, new Vector3(0f, 0f, 3f)), Is.True, "straight behind");
            Assert.That(PanelCover.Hides(Eyes, At, facing, Half, new Vector3(1.5f, 0f, 3f)), Is.False, "seen past its edge");
            Assert.That(PanelCover.Hides(Eyes, At, facing, Half, new Vector3(0f, 0f, 0.5f)), Is.False, "nearer than the panel");
            Assert.That(PanelCover.Hides(Eyes, At, facing, Half, new Vector3(0f, 0f, -3f)), Is.False, "behind the eyes");
        }

        [Test]
        public void It_counts_the_points_it_hides()
        {
            Quaternion facing = Quaternion.LookRotation(At - Eyes);
            var points = new[] { new Vector3(0f, 0f, 3f), new Vector3(0.2f, 0.1f, 2f), new Vector3(2f, 0f, 3f) };
            Assert.That(PanelCover.Count(Eyes, At, facing, Half, points), Is.EqualTo(2));
            Assert.That(PanelCover.Count(Eyes, At, facing, Half, null), Is.EqualTo(0));
        }
    }
}
