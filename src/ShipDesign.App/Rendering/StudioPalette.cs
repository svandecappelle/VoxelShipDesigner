using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.Rendering;

/// <summary>
/// Materials for the studio view, one per (voxel material, occlusion level).
///
/// Shading happens in HSL rather than by multiplying RGB. Scaling RGB drags a colour toward black
/// in a straight line, which desaturates it and is why plainly-multiplied shadows look muddy;
/// dropping lightness while *raising* saturation and easing the hue toward blue is what gives
/// shaded voxel art its depth. Highlights get the opposite nudge, toward warm.
/// </summary>
public sealed class StudioPalette
{
    private readonly Dictionary<(VoxelMaterial, int), Material> _solid = new();
    private readonly Dictionary<VoxelMaterial, Material> _emissive = new();
    private readonly List<(string Name, Color Colour)> _swatches = new();

    private StudioPalette() { }

    /// <summary>The colours actually in use, for the presentation sheet's palette strip. Taken
    /// from the same computation that shades the model rather than re-derived alongside it, so the
    /// swatch and the hull it claims to describe cannot drift apart.</summary>
    public IReadOnlyList<(string Name, Color Colour)> Swatches => _swatches;

    public static int LevelFor(float shade) =>
        Math.Clamp((int)MathF.Round(shade * (VoxelAmbientOcclusion.Levels - 1)), 0, VoxelAmbientOcclusion.Levels - 1);

    public static StudioPalette For(ShipParameters p)
    {
        var palette = new StudioPalette();

        var hull = p.HullColor;
        var baseColours = new Dictionary<VoxelMaterial, ShipColor>
        {
            [VoxelMaterial.Hull] = hull,
            [VoxelMaterial.HullDark] = Scale(hull, 0.46f),
            [VoxelMaterial.Panel] = Scale(hull, 0.78f),
            [VoxelMaterial.Accent] = p.AccentColor,
            [VoxelMaterial.Cockpit] = p.CockpitTintColor,
        };

        foreach (var (material, colour) in baseColours)
            for (var level = 0; level < VoxelAmbientOcclusion.Levels; level++)
            {
                var shaded = ShadeHsl(colour, VoxelAmbientOcclusion.Shade(level));
                var brush = new SolidColorBrush(ToWpf(shaded));
                var group = new MaterialGroup();
                group.Children.Add(new DiffuseMaterial(brush));

                // A touch of specular on the brighter faces only: it reads as a sheen catching the
                // key light, and applying it in the crevices too would flatten the occlusion again.
                if (level >= VoxelAmbientOcclusion.Levels - 2)
                    group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(40, 46, 54)), 22));

                group.Freeze();
                palette._solid[(material, level)] = group;
            }

        var windowColour = new ShipColor(0.96f, 0.75f, 0.26f);
        palette._emissive[VoxelMaterial.Glow] = Emissive(p.EngineGlowColor);
        palette._emissive[VoxelMaterial.Window] = Emissive(windowColour);

        // Two shades per plating material -- lit and shaded -- because the pair is what the eye
        // actually reads off the model; a single flat swatch would not match anything on screen.
        foreach (var (label, colour) in new[]
        {
            ("Coque", hull),
            ("Plaques", Scale(hull, 0.78f)),
            ("Creux", Scale(hull, 0.46f)),
            ("Accent", p.AccentColor),
        })
        {
            palette._swatches.Add((label, ToWpf(ShadeHsl(colour, 1f))));
            palette._swatches.Add(($"{label} ombré", ToWpf(ShadeHsl(colour, VoxelAmbientOcclusion.Shade(0)))));
        }

        palette._swatches.Add(("Hublots", ToWpf(windowColour)));
        palette._swatches.Add(("Réacteurs", ToWpf(p.EngineGlowColor)));
        palette._swatches.Add(("Verrière", ToWpf(p.CockpitTintColor)));

        return palette;
    }

    public Material SolidMaterial(VoxelMaterial material, int level) =>
        _solid.TryGetValue((material, level), out var m) ? m : _solid[(VoxelMaterial.Hull, level)];

    public Material EmissiveMaterial(VoxelMaterial material) =>
        _emissive.TryGetValue(material, out var m) ? m : _emissive[VoxelMaterial.Glow];

    private static Material Emissive(ShipColor colour)
    {
        var group = new MaterialGroup();
        // Diffuse under the emissive so the lit surface still has a body colour rather than
        // reading as a flat sticker when the glow pass is composited over it.
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(ToWpf(Scale(colour, 0.55f)))));
        group.Children.Add(new EmissiveMaterial(new SolidColorBrush(ToWpf(colour))));
        group.Freeze();
        return group;
    }

    private static ShipColor Scale(ShipColor c, float f) => new(c.R * f, c.G * f, c.B * f);

    /// <summary>Darkens in HSL, cooling and saturating as it goes, warming as it brightens.</summary>
    private static ShipColor ShadeHsl(ShipColor c, float shade)
    {
        var (h, s, l) = ToHsl(c);

        l *= shade;

        // Shaded surfaces read as cooler and slightly richer; lit ones as warmer and calmer.
        var depth = 1f - shade;
        s = Math.Clamp(s + depth * 0.22f - (1f - depth) * 0.04f, 0f, 1f);
        h = (h + depth * 14f - (1f - depth) * 4f + 360f) % 360f;

        return FromHsl(h, s, Math.Clamp(l, 0f, 1f));
    }

    private static (float H, float S, float L) ToHsl(ShipColor c)
    {
        float r = c.R, g = c.G, b = c.B;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var l = (max + min) / 2f;
        var d = max - min;

        if (d < 1e-6f) return (210f, 0f, l);

        var s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        float h;
        if (max == r) h = (g - b) / d + (g < b ? 6f : 0f);
        else if (max == g) h = (b - r) / d + 2f;
        else h = (r - g) / d + 4f;

        return (h * 60f, s, l);
    }

    private static ShipColor FromHsl(float h, float s, float l)
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

    private static Color ToWpf(ShipColor c) => Color.FromRgb(
        (byte)(Math.Clamp(c.R, 0f, 1f) * 255),
        (byte)(Math.Clamp(c.G, 0f, 1f) * 255),
        (byte)(Math.Clamp(c.B, 0f, 1f) * 255));
}
