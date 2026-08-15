using System.Collections.Generic;
using ShipDesign.Core.Models;

namespace ShipDesign.App.ViewModels;

/// <summary>
/// Hardcoded ship templates for now. Once there's more than a handful, this should
/// become data (JSON next to the parts) instead of code.
/// </summary>
public static class ShipTemplateCatalog
{
    public static IReadOnlyList<ShipTemplate> All { get; } = new[]
    {
        new ShipTemplate
        {
            Name = "Chasseur",
            HullPartId = "hull_fighter_01",
            Slots = new[]
            {
                new SlotDefinition { SocketPattern = "wing_", PartCategory = PartCategory.Wing, MinCount = 2, MaxCount = 2 },
                new SlotDefinition { SocketPattern = "engine_", PartCategory = PartCategory.Engine, MinCount = 2, MaxCount = 2 },
            }
        },
        new ShipTemplate
        {
            Name = "Éclaireur",
            HullPartId = "hull_scout_01",
            Slots = new[]
            {
                new SlotDefinition { SocketPattern = "wing_", PartCategory = PartCategory.Wing, MinCount = 2, MaxCount = 2 },
                new SlotDefinition { SocketPattern = "engine_", PartCategory = PartCategory.Engine, MinCount = 1, MaxCount = 1 },
            }
        },
    };
}
