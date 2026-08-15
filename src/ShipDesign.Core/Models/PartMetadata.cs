namespace ShipDesign.Core.Models;

/// <summary>
/// Sidecar JSON metadata for a part (same filename as the .gltf/.glb, extension ".json").
/// Sockets are not listed here — they come from glTF nodes named "socket_&lt;name&gt;".
/// </summary>
public sealed class PartMetadata
{
    public string Category { get; set; } = nameof(PartCategory.Hull);
    public string SizeClass { get; set; } = nameof(Models.SizeClass.Medium);
    // note: "Models." prefix avoids clashing with the SizeClass property name above
    public string[] Tags { get; set; } = Array.Empty<string>();
}
