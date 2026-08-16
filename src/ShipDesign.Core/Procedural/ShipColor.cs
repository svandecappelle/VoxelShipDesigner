using System.Numerics;

namespace ShipDesign.Core.Procedural;

/// <summary>Linear-ish 0..1 RGB color, independent of any UI framework's color type.</summary>
public readonly struct ShipColor
{
    public float R { get; }
    public float G { get; }
    public float B { get; }

    public ShipColor(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
    }

    public static ShipColor FromBytes(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f);

    public Vector4 ToVector4(float alpha = 1f) => new(R, G, B, alpha);
    public Vector3 ToVector3() => new(R, G, B);

    // Defaults follow the reference voxel-ship art style: a near-white, slightly cool hull with
    // deep blue squadron markings, vivid cyan exhaust, and near-black canopy glass. Note the
    // accent is blue, not orange -- in that style warm tones are reserved for the lit ports
    // (see VoxelMesher.WindowColor), which is what makes them read as lights against the hull.
    public static readonly ShipColor HullDefault = FromBytes(0xD2, 0xD9, 0xDE);
    public static readonly ShipColor AccentDefault = FromBytes(0x2F, 0x66, 0xAD);
    public static readonly ShipColor EngineGlowDefault = FromBytes(0x62, 0xD0, 0xFA);
    public static readonly ShipColor CockpitTintDefault = FromBytes(0x22, 0x30, 0x3D);

    /// <summary>Port of vessel-forge.html's hslToHex, used by the "random ship" button.</summary>
    public static ShipColor FromHsl(float h, float s, float l)
    {
        s /= 100f;
        l /= 100f;
        float K(float n) => (n + h / 30f) % 12f;
        var a = s * MathF.Min(l, 1f - l);
        float F(float n) => l - a * MathF.Max(-1f, MathF.Min(MathF.Min(K(n) - 3f, 9f - K(n)), 1f));
        return new ShipColor(F(0), F(8), F(4));
    }

    public static ShipColor RandomHsl(Random random, float sMin = 20, float sMax = 55, float lMin = 50, float lMax = 78)
    {
        var h = (float)random.NextDouble() * 360f;
        var s = sMin + (float)random.NextDouble() * (sMax - sMin);
        var l = lMin + (float)random.NextDouble() * (lMax - lMin);
        return FromHsl(h, s, l);
    }
}
