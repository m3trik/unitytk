using NUnit.Framework;

namespace M3trik.Unitytk.Materials.Tests
{
    /// <summary>
    /// A mask's source: its name and its equality agree (the import adds a mask under
    /// its name, so two sources one name holds are one mask), factors read to the name's
    /// 3 decimals, and an image other than a PNG keeps its extension in the name.
    /// </summary>
    public class MaskSourceTests
    {
        [Test]
        public void Two_sources_are_one_mask_exactly_when_their_names_agree()
        {
            var a = new MaskSource { metallicRoughness = "Ball_ORM.png", metallic = 0.12341f, roughness = 0.5f };
            var b = new MaskSource { metallicRoughness = "Ball_ORM.png", metallic = 0.12339f, roughness = 0.5f };
            Assert.That(a.Name, Is.EqualTo(b.Name));
            Assert.That(a, Is.EqualTo(b), "one name: one mask");
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.Name, Is.EqualTo("Ball_ORM_MetallicSmoothness_m0.123_r0.5"));

            var c = new MaskSource { metallicRoughness = "Ball_ORM.png", metallic = 0.125f, roughness = 0.5f };
            Assert.That(c, Is.Not.EqualTo(a));
            Assert.That(c.Name, Is.Not.EqualTo(a.Name));
        }

        [Test]
        public void A_png_and_a_jpeg_of_one_stem_name_two_masks()
        {
            var png = new MaskSource { metallicRoughness = "Ball_ORM.png" };
            var jpg = new MaskSource { metallicRoughness = "Ball_ORM.jpg" };
            Assert.That(png.Name, Is.EqualTo("Ball_ORM_MetallicSmoothness"));
            Assert.That(jpg.Name, Is.EqualTo("Ball_ORM_jpg_MetallicSmoothness"));
            Assert.That(png, Is.Not.EqualTo(jpg));
        }

        [Test]
        public void A_factor_is_baked_at_the_names_precision()
        {
            Assert.That(MaskSource.Quantized(0.12349f), Is.EqualTo(0.123f));
            Assert.That(MaskSource.Quantized(1f), Is.EqualTo(1f));
        }
    }
}
