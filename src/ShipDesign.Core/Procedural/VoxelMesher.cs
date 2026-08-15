using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Converts a VoxelGrid into glTF meshes, one per material role present, using face culling:
/// a voxel only emits the faces that border an empty neighbor, so touching voxels (even across
/// different materials) never produce hidden internal geometry. This is the standard
/// Minecraft-style "culled meshing" technique -- not full greedy meshing (adjacent same-normal
/// faces aren't merged into larger quads), which keeps the algorithm simple at the cost of a
/// higher triangle count than a fully optimized voxel mesher would produce.
/// </summary>
public static class VoxelMesher
{
    private static readonly (int dx, int dy, int dz)[] Directions =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    };

    /// <summary>Scales RGB while leaving alpha alone -- the panel/recess shades are derived from
    /// the hull color rather than being separate parameters, so recoloring a ship keeps its
    /// plating detail coherent instead of needing three color pickers kept in sync by hand.</summary>
    private static Vector4 Shade(Vector4 color, float factor) =>
        new(color.X * factor, color.Y * factor, color.Z * factor, color.W);

    /// <summary>Warm amber port lights. Fixed rather than parameterized: lit windows read as
    /// interior lighting, which stays warm regardless of what the hull is painted.</summary>
    private static readonly Vector3 WindowColor = new(1f, 0.68f, 0.22f);

    public static void AddToScene(SceneBuilder scene, VoxelGrid grid, float voxelSize, ShipParameters p)
    {
        var hull = p.HullColor.ToVector4();
        var materials = new Dictionary<VoxelMaterial, MaterialBuilder>
        {
            [VoxelMaterial.Hull] = new MaterialBuilder("voxel_hull").WithMetallicRoughness(0.4f, 0.6f).WithBaseColor(hull),
            [VoxelMaterial.HullDark] = new MaterialBuilder("voxel_hull_dark").WithMetallicRoughness(0.5f, 0.65f).WithBaseColor(Shade(hull, 0.5f)),
            [VoxelMaterial.Panel] = new MaterialBuilder("voxel_panel").WithMetallicRoughness(0.45f, 0.6f).WithBaseColor(Shade(hull, 0.78f)),
            [VoxelMaterial.Accent] = new MaterialBuilder("voxel_accent").WithMetallicRoughness(0.4f, 0.55f).WithBaseColor(p.AccentColor.ToVector4()),
            [VoxelMaterial.Window] = new MaterialBuilder("voxel_window").WithBaseColor(new Vector4(WindowColor, 1f)).WithEmissive(WindowColor, 1.2f),
            [VoxelMaterial.Glow] = new MaterialBuilder("voxel_glow").WithBaseColor(p.EngineGlowColor.ToVector4()).WithEmissive(p.EngineGlowColor.ToVector3(), 1.4f),
            [VoxelMaterial.Cockpit] = new MaterialBuilder("voxel_cockpit").WithMetallicRoughness(0.2f, 0.2f)
                .WithBaseColor(p.CockpitTintColor.ToVector4(0.85f)).WithAlpha(AlphaMode.BLEND, 0.1f).WithDoubleSide(true),
        };

        var meshesByMaterial = new Dictionary<VoxelMaterial, MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>>();
        var primsByMaterial = new Dictionary<VoxelMaterial, IPrimitiveBuilder>();

        foreach (var ((x, y, z), material) in grid.Voxels)
        {
            if (!meshesByMaterial.TryGetValue(material, out var mesh))
            {
                mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>($"voxels_{material}");
                meshesByMaterial[material] = mesh;
                primsByMaterial[material] = mesh.UsePrimitive(materials[material]);
            }

            var exposed = new bool[6];
            var anyExposed = false;
            for (var i = 0; i < 6; i++)
            {
                var (dx, dy, dz) = Directions[i];
                exposed[i] = !grid.IsFilled(x + dx, y + dy, z + dz);
                anyExposed |= exposed[i];
            }
            if (!anyExposed)
                continue;

            var center = new Vector3(x, y, z) * voxelSize;
            var half = new Vector3(voxelSize / 2f);
            MeshUtil.AddBoxFaces(primsByMaterial[material], center, half,
                posX: exposed[0], negX: exposed[1], posY: exposed[2], negY: exposed[3], posZ: exposed[4], negZ: exposed[5]);
        }

        foreach (var mesh in meshesByMaterial.Values)
            scene.AddRigidMesh(mesh, Matrix4x4.Identity);
    }
}
