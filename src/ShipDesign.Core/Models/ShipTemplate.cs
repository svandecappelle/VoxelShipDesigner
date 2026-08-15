namespace ShipDesign.Core.Models;

public sealed class ShipTemplate
{
    public required string Name { get; init; }
    public required string HullPartId { get; init; }
    public IReadOnlyList<SlotDefinition> Slots { get; init; } = Array.Empty<SlotDefinition>();
}
