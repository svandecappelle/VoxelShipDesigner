using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.Rendering;

/// <summary>
/// Builds the studio view's geometry straight from the voxel grid, with ambient occlusion baked
/// into the shading.
///
/// It does not go through glTF like the main viewport does, because the occlusion has to be
/// sampled from each face's *neighbourhood* -- information that only exists while the grid does.
/// And because WPF's Media3D has no per-vertex colour, the shading cannot be interpolated across a
/// face either: instead faces are bucketed by (material, occlusion level, tint variant) and each
/// bucket gets its own pre-mixed brush. That is a couple of hundred meshes at worst, which is far
/// cheaper than it sounds and is what makes the crevices and the per-block variation read at all.
/// </summary>
public static class StudioMeshBuilder
{
    private static readonly (int X, int Y, int Z)[] Directions =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    };

    /// <summary>Direction the key light travels, matching the studio and sheet light rigs. A baked
    /// shadow has to agree with the light that is actually in the scene, or parts will be shaded on
    /// the wrong side.</summary>
    public static readonly (float X, float Y, float Z) KeyLightTravel = (-0.55f, -0.72f, -0.42f);

    /// <summary>Materials that emit light. They are kept out of the occlusion bucketing (a lamp
    /// does not get darker for sitting in a corner) and are handed back separately so the window
    /// can render them again into its glow pass.</summary>
    private static bool IsEmissive(VoxelMaterial material) =>
        material is VoxelMaterial.Glow or VoxelMaterial.Window;

    public sealed record Result(Model3DGroup Solid, Model3DGroup Emissive, Rect3D Bounds, StudioPalette Palette);

    public static Result Build(VoxelGrid grid, float voxelSize, ShipParameters p)
    {
        var palette = StudioPalette.For(p);
        var toLight = VoxelShadowCaster.ToLightFromTravel(KeyLightTravel.X, KeyLightTravel.Y, KeyLightTravel.Z);
        var tint = VoxelTint.For(grid);

        // One mesh per bucket, accumulated then committed: adding triangles to a MeshGeometry3D
        // already attached to the visual tree is far slower than building it up first.
        var solidMeshes = new Dictionary<(VoxelMaterial Material, int Level, int Variant), MeshGeometry3D>();
        var emissiveMeshes = new Dictionary<VoxelMaterial, MeshGeometry3D>();

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        foreach (var ((vx, vy, vz), material) in grid.Voxels)
        {
            var emissive = IsEmissive(material);

            // Per voxel, not per face: the six faces of a cube have to agree on their tint or the
            // cube stops reading as one block.
            var variant = emissive ? VoxelTint.Neutral : tint.VariantFor(vx, vy, vz);

            for (var d = 0; d < Directions.Length; d++)
            {
                var n = Directions[d];
                if (grid.IsFilled(vx + n.X, vy + n.Y, vz + n.Z))
                    continue;

                MeshGeometry3D mesh;
                if (emissive)
                {
                    if (!emissiveMeshes.TryGetValue(material, out mesh!))
                        emissiveMeshes[material] = mesh = new MeshGeometry3D();
                }
                else
                {
                    // Occlusion and cast shadow describe different things -- contact darkening and
                    // the key light being blocked -- so they multiply rather than one replacing
                    // the other, and a crevice in shadow ends up darker than either alone.
                    var shade = VoxelAmbientOcclusion.FaceShade(grid, (vx, vy, vz), n)
                              * VoxelShadowCaster.Shade(grid, (vx, vy, vz), n, toLight);
                    var level = StudioPalette.LevelFor(shade);
                    var key = (material, level, variant);
                    if (!solidMeshes.TryGetValue(key, out mesh!))
                        solidMeshes[key] = mesh = new MeshGeometry3D();
                }

                AddFace(mesh, vx, vy, vz, voxelSize, d,
                    ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            }
        }

        var solid = new Model3DGroup();
        foreach (var ((material, level, variant), mesh) in solidMeshes)
            solid.Children.Add(new GeometryModel3D(mesh, palette.SolidMaterial(material, level, variant)));

        var glow = new Model3DGroup();
        foreach (var (material, mesh) in emissiveMeshes)
        {
            var lit = palette.EmissiveMaterial(material);
            solid.Children.Add(new GeometryModel3D(mesh, lit));
            glow.Children.Add(new GeometryModel3D(mesh, lit));
        }

        var bounds = solidMeshes.Count == 0 && emissiveMeshes.Count == 0
            ? new Rect3D(0, 0, 0, 1, 1, 1)
            : new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);

        return new Result(solid, glow, bounds, palette);
    }

    /// <summary>
    /// Emits one cube face. The winding matches the exporter's, so a face that renders front-on
    /// here renders front-on in Unity too -- getting this wrong would make the studio view a
    /// misleading preview rather than a useful one.
    /// </summary>
    private static void AddFace(
        MeshGeometry3D mesh, int vx, int vy, int vz, float size, int direction,
        ref double minX, ref double minY, ref double minZ,
        ref double maxX, ref double maxY, ref double maxZ)
    {
        var h = size / 2.0;
        var cx = vx * (double)size;
        var cy = vy * (double)size;
        var cz = vz * (double)size;

        Point3D C(int sx, int sy, int sz) => new(cx + sx * h, cy + sy * h, cz + sz * h);

        Point3D a, b, c, d;
        Vector3D normal;
        switch (direction)
        {
            case 0: a = C(1, -1, 1); b = C(1, -1, -1); c = C(1, 1, -1); d = C(1, 1, 1); normal = new Vector3D(1, 0, 0); break;
            case 1: a = C(-1, -1, -1); b = C(-1, -1, 1); c = C(-1, 1, 1); d = C(-1, 1, -1); normal = new Vector3D(-1, 0, 0); break;
            case 2: a = C(-1, 1, 1); b = C(1, 1, 1); c = C(1, 1, -1); d = C(-1, 1, -1); normal = new Vector3D(0, 1, 0); break;
            case 3: a = C(-1, -1, -1); b = C(1, -1, -1); c = C(1, -1, 1); d = C(-1, -1, 1); normal = new Vector3D(0, -1, 0); break;
            case 4: a = C(-1, -1, 1); b = C(1, -1, 1); c = C(1, 1, 1); d = C(-1, 1, 1); normal = new Vector3D(0, 0, 1); break;
            default: a = C(1, -1, -1); b = C(-1, -1, -1); c = C(-1, 1, -1); d = C(1, 1, -1); normal = new Vector3D(0, 0, -1); break;
        }

        var i = mesh.Positions.Count;
        mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(d);
        for (var k = 0; k < 4; k++) mesh.Normals.Add(normal);

        mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);

        foreach (var pt in new[] { a, b, c, d })
        {
            if (pt.X < minX) minX = pt.X;
            if (pt.Y < minY) minY = pt.Y;
            if (pt.Z < minZ) minZ = pt.Z;
            if (pt.X > maxX) maxX = pt.X;
            if (pt.Y > maxY) maxY = pt.Y;
            if (pt.Z > maxZ) maxZ = pt.Z;
        }
    }
}
