using System.Text.Json;
using SharpGLTF.Schema2;
using ShipDesign.Core.Models;

namespace ShipDesign.Core.Loading;

public static class GltfPartLoader
{
    private const string SocketPrefix = "socket_";

    public static Part Load(string gltfPath)
    {
        var model = ModelRoot.Load(gltfPath);
        var metadata = LoadMetadata(gltfPath);

        return new Part
        {
            Id = Path.GetFileNameWithoutExtension(gltfPath),
            SourcePath = gltfPath,
            Category = Enum.Parse<PartCategory>(metadata.Category, ignoreCase: true),
            SizeClass = Enum.Parse<SizeClass>(metadata.SizeClass, ignoreCase: true),
            Tags = metadata.Tags,
            Sockets = ExtractSockets(model),
            Model = model
        };
    }

    private static PartMetadata LoadMetadata(string gltfPath)
    {
        var jsonPath = Path.ChangeExtension(gltfPath, ".json");
        if (!File.Exists(jsonPath))
            return new PartMetadata();

        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<PartMetadata>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new PartMetadata();
    }

    private static List<Socket> ExtractSockets(ModelRoot model)
    {
        var sockets = new List<Socket>();
        foreach (var node in model.LogicalNodes)
        {
            if (node.Name is null || !node.Name.StartsWith(SocketPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var decomposed = node.LocalTransform.GetDecomposed();
            sockets.Add(new Socket
            {
                Name = node.Name[SocketPrefix.Length..],
                LocalPosition = decomposed.Translation,
                LocalRotation = decomposed.Rotation
            });
        }
        return sockets;
    }
}
