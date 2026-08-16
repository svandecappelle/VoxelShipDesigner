namespace ShipDesign.Core.Procedural;

/// <summary>
/// Colour arithmetic in HSL, shared by the studio view and the exporter.
///
/// It lives in Core rather than beside the WPF palette because the tint a voxel receives has to be
/// computed identically on both sides: the studio bakes it into a brush, the exporter bakes it into
/// vertex colour, and if the two derived it separately the preview would stop predicting the export.
///
/// Everything here works in HSL rather than by scaling RGB. Scaling RGB drags a colour toward black
/// in a straight line, which desaturates it -- that is why naively multiplied shadows look muddy.
/// Dropping lightness while <em>raising</em> saturation and easing the hue toward blue is what gives
/// shaded voxel art its depth; highlights get the opposite nudge, toward warm.
/// </summary>
public static class ColorMath
{
    /// <summary>Darkens by <paramref name="shade"/>, cooling and saturating as it goes, warming as
    /// it brightens.</summary>
    public static ShipColor Shaded(ShipColor c, float shade)
    {
        var (h, s, l) = ToHsl(c);

        l *= shade;

        var depth = 1f - shade;
        s = Math.Clamp(s + depth * 0.22f - (1f - depth) * 0.04f, 0f, 1f);
        h = (h + depth * 14f - (1f - depth) * 4f + 360f) % 360f;

        return FromHsl(h, s, Math.Clamp(l, 0f, 1f));
    }

    /// <summary>
    /// Applies a tint offset. Lightness is scaled rather than added: an absolute offset that reads
    /// as a subtle variation on the near-white hull would swamp the near-black recess material,
    /// where the whole colour only spans a few percent of the range.
    /// </summary>
    public static ShipColor Tinted(ShipColor c, (float Lightness, float Saturation, float Hue) offset)
    {
        var (h, s, l) = ToHsl(c);

        l = Math.Clamp(l * (1f + offset.Lightness), 0f, 1f);
        s = Math.Clamp(s + offset.Saturation, 0f, 1f);
        h = (h + offset.Hue + 360f) % 360f;

        return FromHsl(h, s, l);
    }

    public static (float H, float S, float L) ToHsl(ShipColor c)
    {
        float r = c.R, g = c.G, b = c.B;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var l = (max + min) / 2f;
        var d = max - min;

        // A grey has no meaningful hue. Reporting a cool one rather than 0 (red) keeps the tint and
        // shade nudges pushing a grey hull toward steel rather than toward pink.
        if (d < 1e-6f) return (210f, 0f, l);

        var s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        float h;
        if (max == r) h = (g - b) / d + (g < b ? 6f : 0f);
        else if (max == g) h = (b - r) / d + 2f;
        else h = (r - g) / d + 4f;

        return (h * 60f, s, l);
    }

    public static ShipColor FromHsl(float h, float s, float l)
    {
        if (s < 1e-6f) return new ShipColor(l, l, l);

        var c = (1f - MathF.Abs(2f * l - 1f)) * s;
        var hp = h / 60f;
        var x = c * (1f - MathF.Abs(hp % 2f - 1f));
        var m = l - c / 2f;

        var (r, g, b) = hp switch
        {
            < 1f => (c, x, 0f),
            < 2f => (x, c, 0f),
            < 3f => (0f, c, x),
            < 4f => (0f, x, c),
            < 5f => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return new ShipColor(r + m, g + m, b + m);
    }
}
