using System.Numerics;
using ShipDesign.Core.Models;

namespace ShipDesign.Core.Generation;

public sealed class PlacedPart
{
    public required Part Part { get; init; }
    public required Matrix4x4 WorldTransform { get; init; }
}

public sealed class ShipInstance
{
    public required string TemplateName { get; init; }
    public required IReadOnlyList<PlacedPart> Parts { get; init; }
}
