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

    public static readonly ShipColor HullDefault = FromBytes(0xC7, 0xD4, 0xDD);
    public static readonly ShipColor AccentDefault = FromBytes(0xF2, 0xA6, 0x5C);
    public static readonly ShipColor EngineGlowDefault = FromBytes(0x7F, 0xE0, 0xE8);
    public static readonly ShipColor CockpitTintDefault = FromBytes(0x5F, 0xB8, 0xD6);

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
