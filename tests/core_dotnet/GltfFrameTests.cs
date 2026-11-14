using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>
    /// glTF's frame as Unity's model import lands a delivery (<see cref="GltfFrame"/>):
    /// the X mirror of every point, direction and rotation, the texture transform's
    /// origin, and what the import's scale makes of a record's lengths.
    /// </summary>
    public class GltfFrameTests
    {
        const double Tol = 1e-12;

        [Test]
        public void A_centimetre_file_at_the_defaults_lands_a_metre_as_one()
        {
            GltfFrame f = GltfFrame.Default;
            Assert.That(f.Units(100.0, 0.01), Is.EqualTo(1.0).Within(Tol), "100 cm of a record labelled 0.01 m");
            Assert.That(f.Length(1.0), Is.EqualTo(1.0).Within(Tol), "an in-frame length: the file's value x 0.01");
        }

        [Test]
        public void The_scale_factor_scales_every_length()
        {
            var f = new GltfFrame(true, 0.01, 2.0);
            Assert.That(f.Units(100.0, 0.01), Is.EqualTo(2.0).Within(Tol));
            Assert.That(f.Length(1.0), Is.EqualTo(2.0).Within(Tol));
        }

        [Test]
        public void With_convert_units_off_a_length_lands_in_the_files_own_units()
        {
            // Unity states no file unit then (fileScale reads 1): the record's label is the unit.
            var cm = new GltfFrame(false, 1.0, 1.0);
            Assert.That(cm.Units(100.0, 0.01), Is.EqualTo(100.0).Within(Tol), "a metre is 100 of a centimetre file's units");
            Assert.That(cm.Length(1.0), Is.EqualTo(100.0).Within(Tol));
            var scaled = new GltfFrame(false, 1.0, 3.0);
            Assert.That(scaled.Units(2.0, 1.0), Is.EqualTo(6.0).Within(Tol));
        }

        [Test]
        public void An_in_frame_length_is_the_files_own_value_read_as_centimetres()
        {
            // A metre file: glTF's frame reads its value 1 as 0.01, which Unity lands as 1.
            var m = new GltfFrame(true, 1.0, 1.0);
            Assert.That(m.Length(0.01), Is.EqualTo(1.0).Within(Tol));
            Assert.That(m.Units(1.0, 1.0), Is.EqualTo(1.0).Within(Tol), "a scene metre is a metre");
        }

        [Test]
        public void A_frame_left_unset_is_the_default_frame()
        {
            // A struct's default skips its constructor: it must not land every length at 0.
            GltfFrame unset = default;
            Assert.That(unset.ConvertsUnits, Is.True);
            Assert.That(unset.FileScale, Is.EqualTo(0.01));
            Assert.That(unset.GlobalScale, Is.EqualTo(1.0));
            Assert.That(unset.Units(100.0, 0.01), Is.EqualTo(GltfFrame.Default.Units(100.0, 0.01)).Within(Tol));
            Assert.That(unset.Length(1.0), Is.EqualTo(1.0).Within(Tol));
        }

        [Test]
        public void A_scale_unity_never_states_reads_as_one()
        {
            var f = new GltfFrame(true, 0.0, double.NaN);
            Assert.That(f.FileScale, Is.EqualTo(1.0));
            Assert.That(f.GlobalScale, Is.EqualTo(1.0));
            Assert.That(f.Length(1.0), Is.EqualTo(100.0).Within(Tol));
        }

        [Test]
        public void Points_directions_and_rotations_mirror_x()
        {
            Assert.That(GltfFrame.Point(new[] { 1.0, 2.0, 3.0 }), Is.EqualTo(new[] { -1.0, 2.0, 3.0 }));
            Assert.That(GltfFrame.Direction(new[] { 0.6, -0.8, 0.0 }), Is.EqualTo(new[] { -0.6, -0.8, 0.0 }));
            Assert.That(GltfFrame.Rotation(new[] { 0.1, 0.2, 0.3, 0.9 }), Is.EqualTo(new[] { 0.1, -0.2, -0.3, 0.9 }));
            var (x, y, z) = GltfFrame.Point(1.0, 2.0, 3.0);
            Assert.That((x, y, z), Is.EqualTo((-1.0, 2.0, 3.0)));
            var (qx, qy, qz, qw) = GltfFrame.Rotation(0.1, 0.2, 0.3, 0.9);
            Assert.That((qx, qy, qz, qw), Is.EqualTo((0.1, -0.2, -0.3, 0.9)));
        }

        [Test]
        public void A_texture_transforms_offset_moves_to_unitys_bottom_left_origin()
        {
            // glTF's UV origin is the image's top left, Unity's its bottom left.
            Assert.That(GltfFrame.TextureOffset(0.25, 0.5, 0.5), Is.EqualTo((0.25, 0.0)));
            Assert.That(GltfFrame.TextureOffset(0.25, 0.0, 0.5), Is.EqualTo((0.25, 0.5)), "the top half lands on top");
            Assert.That(GltfFrame.TextureOffset(0.0, 0.0, 1.0), Is.EqualTo((0.0, 0.0)), "the identity stays");
        }
    }
}
