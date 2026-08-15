using SharpGLTF.Schema2;

namespace ShipDesign.Core.Models;

public sealed class Part
{
    public required string Id { get; init; }
    public required string SourcePath { get; init; }
    public required PartCategory Category { get; init; }
    public SizeClass SizeClass { get; init; } = SizeClass.Medium;
    public string[] Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Socket> Sockets { get; init; } = Array.Empty<Socket>();
    public required ModelRoot Model { get; init; }
}
