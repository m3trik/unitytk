using System;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>glTF's metal, roughness and occlusion repacked into Unity's one mask layout.</summary>
    public class MaskMapTests
    {
        static Color32[] Fill(int count, Color32 texel)
        {
            var texels = new Color32[count];
            for (int i = 0; i < count; i++) texels[i] = texel;
            return texels;
        }

        [Test]
        public void Metal_comes_from_b_smoothness_from_g_with_the_factors_baked_in()
        {
            // glTF: R occlusion 64, G roughness 102 (0.4), B metal 255.
            var orm = Fill(4, new Color32(64, 102, 255, 255));
            var mask = MaskMap.Pack(orm, 2, 2, null, 0, 0, 0.5f, 1f, out int w, out int h);
            Assert.That((w, h), Is.EqualTo((2, 2)));
            Assert.That(mask[0].r, Is.EqualTo(128), "metal = factor 0.5 x B 1");
            Assert.That(mask[0].g, Is.EqualTo(255), "no occlusion: white");
            Assert.That(mask[0].b, Is.EqualTo(255), "HDRP's detail mask: the whole detail");
            Assert.That(mask[0].a, Is.EqualTo(153), "smoothness = 1 - roughness 0.4");
        }

        [Test]
        public void Occlusion_packed_with_them_is_read_from_r_into_g()
        {
            var orm = Fill(1, new Color32(64, 0, 0, 255));
            var mask = MaskMap.Pack(orm, 1, 1, orm, 1, 1, 1f, 1f, out _, out _);
            Assert.That(mask[0].g, Is.EqualTo(64));
            Assert.That(mask[0].r, Is.EqualTo(0), "metal B 0");
            Assert.That(mask[0].a, Is.EqualTo(255), "roughness G 0: a mirror");
        }

        [Test]
        public void An_occlusion_image_alone_carries_the_factors_as_constants_at_its_own_size()
        {
            var occlusion = new[] { new Color32(10, 0, 0, 255), new Color32(20, 0, 0, 255) };
            var mask = MaskMap.Pack(null, 0, 0, occlusion, 2, 1, 0.2f, 0.7f, out int w, out int h);
            Assert.That((w, h), Is.EqualTo((2, 1)));
            Assert.That(new[] { mask[0].g, mask[1].g }, Is.EqualTo(new byte[] { 10, 20 }));
            Assert.That(mask[1].r, Is.EqualTo(51), "metal 0.2");
            Assert.That(mask[1].a, Is.EqualTo(77), "smoothness 0.3");
        }

        [Test]
        public void An_occlusion_image_of_another_size_is_sampled_to_the_masks()
        {
            var orm = Fill(16, new Color32(0, 0, 0, 255));
            // 2 x 2: left column 0, right column 200; row 0 (bottom) and row 1 alike.
            var occlusion = new[] { new Color32(0, 0, 0, 255), new Color32(200, 0, 0, 255),
                                    new Color32(0, 0, 0, 255), new Color32(200, 0, 0, 255) };
            var mask = MaskMap.Pack(orm, 4, 4, occlusion, 2, 2, 1f, 1f, out _, out _);
            Assert.That(new[] { mask[0].g, mask[1].g, mask[2].g, mask[3].g }, Is.EqualTo(new byte[] { 0, 0, 200, 200 }));
        }

        [Test]
        public void Neither_image_or_a_short_one_is_refused()
        {
            Assert.Throws<ArgumentException>(() => MaskMap.Pack(null, 0, 0, null, 0, 0, 1f, 1f, out _, out _));
            Assert.Throws<ArgumentException>(() => MaskMap.Pack(new Color32[3], 2, 2, null, 0, 0, 1f, 1f, out _, out _));
        }
    }
}
