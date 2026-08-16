using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.Converters;

/// <summary>
/// Turns a <see cref="HullShape"/> into a top-view silhouette, so the picker shows the planform
/// each option produces instead of just naming it. Names like "wedge" or "spindle" mean little
/// until you have seen the hull they make; the outline is the thing being chosen.
/// </summary>
public sealed class HullShapeIconConverter : IValueConverter
{
    // Bow at the left, stern at the right, drawn in a 44x22 box and mirrored about y=11.
    // Each path traces the starboard edge forward-to-aft, then returns along the port edge.
    private static readonly Dictionary<HullShape, string> Outlines = new()
    {
        // Long point opening into a broad after-body.
        [HullShape.Dart] = "M 1,11 L 15,5 L 34,3 L 41,6 L 41,16 L 34,19 L 15,17 Z",

        // Narrow bow widening steadily to the widest point at the stern.
        [HullShape.Wedge] = "M 1,11 L 40,1 L 42,3 L 42,19 L 40,21 Z",

        // Closes at both ends, fullest amidships.
        [HullShape.Spindle] = "M 2,11 L 12,4 L 30,4 L 41,11 L 30,18 L 12,18 Z",

        // Near-parallel sides with blunt, slightly clipped ends.
        [HullShape.Slab] = "M 4,4 L 39,4 L 42,7 L 42,15 L 39,18 L 4,18 L 2,15 L 2,7 Z",

        // Wide bow, pinched waist, moderate after-body.
        [HullShape.Hammerhead] = "M 2,11 L 7,2 L 14,2 L 17,8 L 30,7 L 41,5 L 41,17 L 30,15 L 17,14 L 14,20 L 7,20 Z",

        // A disc: wider across than it is long, so it is drawn as a full ellipse.
        [HullShape.Saucer] = "M 22,1 A 20,10 0 1 1 21.9,1 Z",

        // The same disc with the middle cut out. The second sub-path runs the opposite way, which
        // is what makes the even-odd fill leave a hole rather than a smaller filled disc.
        [HullShape.Ring] = "M 22,1 A 20,10 0 1 1 21.9,1 Z M 22,5 A 11,5.5 0 1 0 22.1,5 Z",

        // Two prongs at the bow merging into a common after-body.
        [HullShape.Fork] = "M 2,2 L 16,5 L 30,6 L 41,7 L 41,15 L 30,16 L 16,17 L 2,20 L 2,15 L 13,13 L 13,9 L 2,7 Z",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HullShape shape || !Outlines.TryGetValue(shape, out var data))
            return Geometry.Empty;

        var geometry = Geometry.Parse(data);
        // EvenOdd is what turns the ring's inner sub-path into an actual hole; the default
        // Nonzero rule would fill straight over it and draw a plain disc.
        if (geometry is PathGeometry path)
            path.FillRule = FillRule.EvenOdd;
        return geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
