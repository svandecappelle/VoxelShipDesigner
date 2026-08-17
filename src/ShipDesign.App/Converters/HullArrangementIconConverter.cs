using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.Converters;

/// <summary>
/// Turns a <see cref="HullArrangement"/> into a side-view schematic.
///
/// Side view, unlike the hull-shape icons, because the arrangement is entirely about the vertical
/// axis: seen from above, a composite ship and a single-hull one look the same. The distinction the
/// picker is asking about only exists in profile.
/// </summary>
public sealed class HullArrangementIconConverter : IValueConverter
{
    // Bow at the left, drawn in a 44x22 box.
    private static readonly Dictionary<HullArrangement, string> Outlines = new()
    {
        // One hull on the axis, with a modest dorsal rise: the conventional layout.
        [HullArrangement.Parallel] =
            "M 2,14 L 12,9 L 20,8 L 22,5 L 30,5 L 32,8 L 41,10 L 41,16 L 30,18 L 10,18 Z",

        // A disc up front and high, an engineering hull aft and low, and the neck between them --
        // three strokes, which is the whole idea of the arrangement.
        [HullArrangement.Composite] =
            "M 4,7 L 26,4 L 30,6 L 26,8 L 4,7 Z " +          // saucer, seen edge-on
            "M 17,7 L 22,7 L 25,15 L 20,15 Z " +             // neck, raked aft
            "M 14,15 L 34,13 L 41,15 L 41,19 L 34,20 L 16,19 Z", // engineering hull
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is HullArrangement arrangement && Outlines.TryGetValue(arrangement, out var path)
            ? Geometry.Parse(path)
            : Geometry.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
