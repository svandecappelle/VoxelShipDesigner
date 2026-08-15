using System.Numerics;
using ShipDesign.Core.Loading;
using ShipDesign.Core.Models;

namespace ShipDesign.Core.Generation;

/// <summary>
/// Combines a hull with parts picked randomly (per seed) for each slot's matching sockets.
/// </summary>
public sealed class ShipAssembler
{
    private readonly PartLibrary _library;

    public ShipAssembler(PartLibrary library) => _library = library;

    public ShipInstance Assemble(ShipTemplate template, int seed)
    {
        var random = new Random(seed);
        var hull = _library.Find(template.HullPartId)
            ?? throw new InvalidOperationException($"Hull part '{template.HullPartId}' not found in library.");

        var placed = new List<PlacedPart>
        {
            new() { Part = hull, WorldTransform = Matrix4x4.Identity }
        };

        foreach (var slot in template.Slots)
        {
            var sockets = hull.Sockets
                .Where(s => s.Name.StartsWith(slot.SocketPattern, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var candidates = _library.ByCategory(slot.PartCategory).ToList();
            if (sockets.Count == 0 || candidates.Count == 0)
                continue;

            var count = Math.Min(random.Next(slot.MinCount, slot.MaxCount + 1), sockets.Count);
            foreach (var socket in sockets.Take(count))
            {
                var chosen = candidates[random.Next(candidates.Count)];
                var transform = Matrix4x4.CreateFromQuaternion(socket.LocalRotation)
                    * Matrix4x4.CreateTranslation(socket.LocalPosition);
                placed.Add(new PlacedPart { Part = chosen, WorldTransform = transform });
            }
        }

        return new ShipInstance { TemplateName = template.Name, Parts = placed };
    }
}
