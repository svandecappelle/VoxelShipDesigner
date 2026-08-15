using System.Text.Json;
using ShipDesign.Core.Models;

namespace ShipDesign.Core.Loading;

public static class ShipTemplateLoader
{
    public static IReadOnlyList<ShipTemplate> LoadFromDirectory(string directory)
    {
        var templates = new List<ShipTemplate>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            templates.Add(Load(file));
        return templates;
    }

    public static ShipTemplate Load(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var data = JsonSerializer.Deserialize<ShipTemplateData>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Template JSON invalide : '{jsonPath}'.");

        return new ShipTemplate
        {
            Name = data.Name,
            HullPartId = data.HullPartId,
            Slots = data.Slots.Select(s => new SlotDefinition
            {
                SocketPattern = s.SocketPattern,
                PartCategory = Enum.Parse<PartCategory>(s.Category, ignoreCase: true),
                MinCount = s.MinCount,
                MaxCount = s.MaxCount
            }).ToList()
        };
    }

    private sealed class ShipTemplateData
    {
        public string Name { get; set; } = "";
        public string HullPartId { get; set; } = "";
        public SlotData[] Slots { get; set; } = Array.Empty<SlotData>();
    }

    private sealed class SlotData
    {
        public string SocketPattern { get; set; } = "";
        public string Category { get; set; } = "";
        public int MinCount { get; set; } = 1;
        public int MaxCount { get; set; } = 1;
    }
}
