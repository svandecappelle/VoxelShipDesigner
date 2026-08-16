namespace ShipDesign.Core.Procedural;

/// <summary>
/// Cast shadows for a directional light, traced through the voxel grid.
///
/// On voxels this needs no shadow map and no depth buffer: walk from a face toward the light one
/// step at a time and see whether anything solid is in the way. It is exact, it costs a hash lookup
/// per step, and it gives the one thing ambient occlusion cannot -- a wing darkening the hull
/// beneath it, which is where most of a lit render's sense of depth comes from.
///
/// This is a *display* concern only. The Unity bundle deliberately does not carry baked shadows:
/// Unity computes its own, and a baked one underneath would double up and follow the wrong light.
/// </summary>
public static class VoxelShadowCaster
{
    /// <summary>How far a ray travels before giving up, in voxels. A ship is a few hundred voxels
    /// across at most, and stopping early costs only a missed shadow from a very distant part.</summary>
    private const int MaxSteps = 220;

    /// <summary>Brightness of a fully shadowed face. Well above zero because a shadow here is only
    /// blocking the key light -- fill and ambient still reach the surface, and driving it to black
    /// would read as a hole rather than as shade.</summary>
    public const float ShadowShade = 0.62f;

    /// <summary>
    /// Whether the face of <paramref name="voxel"/> pointing along <paramref name="normal"/> can
    /// see the light. <paramref name="toLight"/> points from the surface *toward* the light.
    /// </summary>
    public static bool IsLit(
        VoxelGrid grid,
        (int X, int Y, int Z) voxel,
        (int X, int Y, int Z) normal,
        (float X, float Y, float Z) toLight)
    {
        // A face turned away from the light is unlit regardless of what is in front of it, and
        // tracing it would be wasted work.
        var facing = normal.X * toLight.X + normal.Y * toLight.Y + normal.Z * toLight.Z;
        if (facing <= 0f)
            return false;

        // Start one voxel out along the normal, or the ray immediately hits the face's own voxel.
        float x = voxel.X + normal.X + 0.5f;
        float y = voxel.Y + normal.Y + 0.5f;
        float z = voxel.Z + normal.Z + 0.5f;

        // Step at half a voxel so a ray crossing a corner diagonally cannot slip between two
        // solid voxels -- at a full step it would tunnel through thin structures.
        const float step = 0.5f;
        var dx = toLight.X * step;
        var dy = toLight.Y * step;
        var dz = toLight.Z * step;

        for (var i = 0; i < MaxSteps; i++)
        {
            x += dx;
            y += dy;
            z += dz;

            if (grid.IsFilled((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z)))
                return false;
        }

        return true;
    }

    /// <summary>Brightness multiplier from the key light alone: 1 in the light, ShadowShade in
    /// shadow. Multiplied with ambient occlusion rather than replacing it -- the two describe
    /// different things, and a crevice in shadow should be darker than either alone.</summary>
    public static float Shade(
        VoxelGrid grid,
        (int X, int Y, int Z) voxel,
        (int X, int Y, int Z) normal,
        (float X, float Y, float Z) toLight) =>
        IsLit(grid, voxel, normal, toLight) ? 1f : ShadowShade;

    /// <summary>Normalises a light direction into the unit vector pointing from a surface toward
    /// the light, which is the opposite of the direction the light travels.</summary>
    public static (float X, float Y, float Z) ToLightFromTravel(float dx, float dy, float dz)
    {
        var length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length < 1e-6f) return (0f, 1f, 0f);
        return (-dx / length, -dy / length, -dz / length);
    }
}
