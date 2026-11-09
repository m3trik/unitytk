using System;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// glTF 2.0's frame -- the frame every record value is stated in (metres,
    /// right-handed, +Y up, +Z forward, rotations as unit quaternions (x, y, z, w)) --
    /// as Unity's model import lands a delivery: the one conversion every importer
    /// reads a record's values through.
    /// </summary>
    /// <remarks>
    /// <para><b>Axes.</b> Unity is left-handed with +Y up and +Z forward; its FBX import
    /// converts a right-handed file by mirroring X (measured: a node at (1, 2, 3) lands at
    /// (-1, 2, 3)), so a point or a direction negates its X and a rotation (x, y, z, w)
    /// becomes (x, -y, -z, w). A texture transform's offset moves from glTF's top-left
    /// UV origin to Unity's bottom-left one.</para>
    /// <para><b>Lengths.</b> The import converts the file's unit into every local: with
    /// Convert Units on (<c>useFileScale</c>) a file unit lands as <c>fileScale</c> (the
    /// unit in metres: 0.01 for a centimetre file) times the Scale Factor
    /// (<c>globalScale</c>), so a metre lands as the Scale Factor; with it off a file unit
    /// lands as the Scale Factor alone, and Unity states no file unit then (its
    /// <c>fileScale</c> reads 1: measured, Unity 6000.3). A record states its lengths one
    /// of two ways, each with its reading: in the file's own unit, labelled with that
    /// unit in metres (<see cref="Units"/>: a record's <c>unit_scale</c>, which every
    /// producer's writer states as the unit it writes), or in glTF's frame as it frames
    /// the file (<see cref="Length"/>: a record <c>in_frame</c>, whose FBX lengths are the
    /// file's own values x 0.01 whatever its unit). At Unity's defaults a centimetre
    /// file lands both as metres (<see cref="Default"/>).</para>
    /// <para>Prior art: pythontk's <c>SceneRecords.FRAME</c> declares the frame; Unity
    /// states no conversion of its own for values outside the file (its import
    /// converts the file's nodes and meshes alone), so a reader converts them itself,
    /// here once. Engine-free, held by <c>dotnet test</c>; the editor reads an
    /// importer's three settings into one (the kernel's <c>Imports.Frame</c>).</para>
    /// </remarks>
    public readonly struct GltfFrame
    {
        /// <summary>
        /// The frame of an import whose Convert Units is <paramref name="useFileScale"/>,
        /// its file's unit <paramref name="fileScale"/> metres (read where Convert Units is
        /// on) and its Scale Factor <paramref name="globalScale"/>; a scale that is not a
        /// finite positive number (none Unity states) reads as 1.
        /// </summary>
        public GltfFrame(bool useFileScale, double fileScale, double globalScale)
        {
            _keepsFileUnits = !useFileScale;
            _fileScale = Positive(fileScale);
            _globalScale = Positive(globalScale);
        }

        // Stored so a frame left unset (default(GltfFrame), which skips the constructor)
        // reads as Default: Convert Units on, a centimetre file, Scale Factor 1.
        readonly bool _keepsFileUnits;
        readonly double _fileScale, _globalScale;

        /// <summary>A centimetre file at Unity's defaults (Convert Units on, Scale Factor 1); a frame left unset reads as it.</summary>
        public static GltfFrame Default => new GltfFrame(true, 0.01, 1.0);

        /// <summary>Whether the import converts the file's unit into metres (Convert Units).</summary>
        public bool ConvertsUnits => !_keepsFileUnits;

        /// <summary>The file's unit in metres, as the import states it (read where <see cref="ConvertsUnits"/>).</summary>
        public double FileScale => _fileScale > 0.0 ? _fileScale : 0.01;

        /// <summary>The import's Scale Factor.</summary>
        public double GlobalScale => _globalScale > 0.0 ? _globalScale : 1.0;

        /// <summary>
        /// <paramref name="length"/> in the file's own unit, <paramref name="unit"/> metres
        /// (a record's <c>unit_scale</c>), as the import's locals: a metre as the Scale
        /// Factor where it converts units, else a file unit as the Scale Factor.
        /// </summary>
        public double Units(double length, double unit) => ConvertsUnits ? length * unit * GlobalScale : length * GlobalScale;

        /// <summary>
        /// A length in glTF's frame as it frames the file (an in-frame record's: an FBX's
        /// own value x 0.01) as the import's locals: x 100, then as the import scales a file unit.
        /// </summary>
        public double Length(double length) => length * 100.0 * (ConvertsUnits ? FileScale : 1.0) * GlobalScale;

        /// <summary>A point (x, y, z) in Unity's axes: X mirrored.</summary>
        public static (double x, double y, double z) Point(double x, double y, double z) => (-x, y, z);

        /// <summary>A point stated as three numbers, in Unity's axes: X mirrored.</summary>
        public static double[] Point(double[] p) => new[] { -p[0], p[1], p[2] };

        /// <summary>A direction stated as three numbers, in Unity's axes: X mirrored, as a point's.</summary>
        public static double[] Direction(double[] d) => Point(d);

        /// <summary>A rotation (x, y, z, w) in Unity's axes, mirrored as a point is: (x, -y, -z, w).</summary>
        public static (double x, double y, double z, double w) Rotation(double x, double y, double z, double w)
            => (x, -y, -z, w);

        /// <summary>A rotation stated as four numbers (x, y, z, w), in Unity's axes: (x, -y, -z, w).</summary>
        public static double[] Rotation(double[] q) => new[] { q[0], -q[1], -q[2], q[3] };

        /// <summary>
        /// A texture transform's offset (<paramref name="offsetU"/>, <paramref name="offsetV"/>),
        /// its V scale <paramref name="scaleV"/>, from glTF's top-left UV origin to Unity's
        /// bottom-left one: (u, 1 - scaleV - v).
        /// </summary>
        public static (double u, double v) TextureOffset(double offsetU, double offsetV, double scaleV)
            => (offsetU, 1.0 - scaleV - offsetV);

        static double Positive(double scale) => scale > 0.0 && !double.IsInfinity(scale) ? scale : 1.0;
    }
}
