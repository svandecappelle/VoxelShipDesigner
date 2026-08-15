using ShipDesign.Core.Models;

namespace ShipDesign.Core.Loading;

public sealed class PartLibrary
{
    private readonly List<Part> _parts = new();

    public IReadOnlyList<Part> Parts => _parts;

    public static PartLibrary LoadFromDirectory(string directory)
    {
        var library = new PartLibrary();
        var files = Directory.EnumerateFiles(directory, "*.glb", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(directory, "*.gltf", SearchOption.AllDirectories));

        foreach (var file in files)
            library._parts.Add(GltfPartLoader.Load(file));

        return library;
    }

    public Part? Find(string id) => _parts.Find(p => p.Id == id);

    public IEnumerable<Part> ByCategory(PartCategory category) =>
        _parts.Where(p => p.Category == category);
}
