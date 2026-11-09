namespace M3trik.Unitytk.Materials
{
    /// <summary>What a map's texels hold, which decides its import settings.</summary>
    internal enum TextureRole
    {
        /// <summary>sRGB colour: a base colour or emission map (Unity's default).</summary>
        Colour,
        /// <summary>Linear data: metal, roughness, occlusion, a packed map.</summary>
        Data,
        /// <summary>A tangent-space normal map.</summary>
        Normal,
    }
}
