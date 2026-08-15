namespace ShipDesign.App.ViewModels;

/// <summary>Display row for a placed part in the side panel's list, with its index into ShipInstance.Parts.</summary>
public sealed class PartListEntry
{
    public required int Index { get; init; }
    public required string Text { get; init; }
}
