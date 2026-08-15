using System.Numerics;

namespace ShipDesign.Core.Models;

/// <summary>
/// An attachment point on a part, sourced from a glTF node named "socket_&lt;name&gt;".
/// </summary>
public sealed class Socket
{
    public required string Name { get; init; }
    public Vector3 LocalPosition { get; init; }
    public Quaternion LocalRotation { get; init; } = Quaternion.Identity;
}
