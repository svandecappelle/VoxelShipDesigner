namespace ShipDesign.Core.Models;

/// <summary>
/// Matches hull sockets whose name starts with <see cref="SocketPattern"/> and fills them
/// with parts of <see cref="PartCategory"/>, up to a random count between Min and Max.
/// </summary>
public sealed class SlotDefinition
{
    public required string SocketPattern { get; init; }
    public required PartCategory PartCategory { get; init; }
    public int MinCount { get; init; } = 1;
    public int MaxCount { get; init; } = 1;
}
