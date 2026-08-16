using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.Rendering;

/// <summary>
/// Materials for the studio view, one per (voxel material, occlusion level, tint variant).
///
/// The third axis is what stops the hull reading as plastic. Occlusion and shadow describe the
/// relief; <see cref="VoxelTint"/> describes the block, so two unoccluded voxels side by side are
/// no longer pixel-identical. It is a third axis rather than a continuous colour because WPF batches
/// geometry by material -- a per-voxel colour would mean one draw call per cube.
///
/// The colour arithmetic itself lives in <see cref="ColorMath"/>, in Core, so the studio and the
/// exporter cannot drift apart on what a given tint actually looks like.
/// </summary>
public sealed class StudioPalette
{
    private readonly Dictionary<(VoxelMaterial, int, int), Material> _solid = new();
    private readonly Dictionary<VoxelMaterial, Material> _emissive = new();
    private readonly List<(string Name, Color Colour)> _swatches = new();

    private StudioPalette() { }

    /// <summary>The colours actually in use, for the presentation sheet's palette strip. Taken
    /// from the same computation that shades the model rather than re-derived alongside it, so the
    /// swatch and the hull it claims to describe cannot drift apart.</summary>
    public IReadOnlyList<(string Name, Color Colour)> Swatches => _swatches;

    /// <summary>
    /// Shade levels the palette quantises to. More than ambient occlusion alone needs: occlusion
    /// and cast shadow multiply together, so the darkest a surface can get is well below the
    /// darkest occlusion, and four levels calibrated on occlusion would have rendered shadowed
    /// faces *lighter* than deep crevices.
    /// </summary>
    public const int Levels = 6;

    /// <summary>Brightness of the darkest level: occlusion floor times shadow floor.</summary>
    private const float MinShade = 0.42f * VoxelShadowCaster.ShadowShade;

    private static float ShadeForLevel(int level) =>
        MinShade + (1f - MinShade) * (Math.Clamp(level, 0, Levels - 1) / (float)(Levels - 1));

    public static int LevelFor(float shade)
    {
        var t = (shade - MinShade) / (1f - MinShade);
        return Math.Clamp((int)MathF.Round(t * (Levels - 1)), 0, Levels - 1);
    }

    public static StudioPalette For(ShipParameters p)
    {
        var palette = new StudioPalette();

        // The same table the meshers and the .mat writer read, so the studio is showing the colour
        // the export will actually carry rather than a lookalike maintained in parallel.
        var baseColours = VoxelMesher.BaseColours(p);

        foreach (var material in new[]
        {
            VoxelMaterial.Hull, VoxelMaterial.HullDark, VoxelMaterial.Panel,
            VoxelMaterial.Accent, VoxelMaterial.Cockpit,
        })
        {
            var colour = baseColours[material];

            for (var variant = 0; variant < VoxelTint.Variants; variant++)
            {
                var tinted = ColorMath.Tinted(colour, VoxelTint.Offsets(variant));

                for (var level = 0; level < Levels; level++)
                {
                    var shaded = ColorMath.Shaded(tinted, ShadeForLevel(level));
                    var group = new MaterialGroup();
                    group.Children.Add(new DiffuseMaterial(new SolidColorBrush(ToWpf(shaded))));

                    // A touch of specular on the brighter faces only: it reads as a sheen catching
                    // the key light, and applying it in the crevices too would flatten the
                    // occlusion again.
                    if (level >= Levels - 2)
                        group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(40, 46, 54)), 22));

                    group.Freeze();
                    palette._solid[(material, level, variant)] = group;
                }
            }
        }

        palette._emissive[VoxelMaterial.Glow] = Emissive(baseColours[VoxelMaterial.Glow]);
        palette._emissive[VoxelMaterial.Window] = Emissive(baseColours[VoxelMaterial.Window]);

        // Three samples per plating material -- the tint extremes at full light, plus the shaded
        // middle -- because that trio is what the eye actually reads off the model. A single flat
        // swatch would not match anything on screen.
        foreach (var (label, material) in new[]
        {
            ("Coque", VoxelMaterial.Hull),
            ("Plaques", VoxelMaterial.Panel),
            ("Creux", VoxelMaterial.HullDark),
            ("Accent", VoxelMaterial.Accent),
        })
        {
            var colour = baseColours[material];
            ShipColor Variant(int v) => ColorMath.Tinted(colour, VoxelTint.Offsets(v));

            palette._swatches.Add(($"{label} clair", ToWpf(ColorMath.Shaded(Variant(VoxelTint.Variants - 1), 1f))));
            palette._swatches.Add((label, ToWpf(ColorMath.Shaded(Variant(VoxelTint.Neutral), 1f))));
            palette._swatches.Add(($"{label} ombré", ToWpf(ColorMath.Shaded(Variant(0), ShadeForLevel(0)))));
        }

        palette._swatches.Add(("Hublots", ToWpf(baseColours[VoxelMaterial.Window])));
        palette._swatches.Add(("Réacteurs", ToWpf(baseColours[VoxelMaterial.Glow])));
        palette._swatches.Add(("Verrière", ToWpf(baseColours[VoxelMaterial.Cockpit])));

        return palette;
    }

    public Material SolidMaterial(VoxelMaterial material, int level, int variant) =>
        _solid.TryGetValue((material, level, variant), out var m)
            ? m
            : _solid[(VoxelMaterial.Hull, level, variant)];

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

    private static Color ToWpf(ShipColor c) => Color.FromRgb(
        (byte)(Math.Clamp(c.R, 0f, 1f) * 255),
        (byte)(Math.Clamp(c.G, 0f, 1f) * 255),
        (byte)(Math.Clamp(c.B, 0f, 1f) * 255));
}
