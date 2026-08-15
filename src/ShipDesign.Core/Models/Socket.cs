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

    /// <summary>
    /// True for sockets named "*_R" (by convention, parts are authored for the "_L" side
    /// and mirrored on X for their "_R" counterpart), so the assembler can flip the part
    /// instead of requiring a separate mirrored mesh per side.
    /// </summary>
    public bool Mirror { get; init; }
}
