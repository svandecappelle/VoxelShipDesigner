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

    /// <summary>Warm gold port lights, matching the reference art style's window glow. Fixed
    /// rather than parameterized: lit windows read as interior lighting, which stays warm
    /// regardless of what the hull is painted -- and the warm/cool contrast against a cool hull
    /// and blue markings is exactly what makes them register as lights.</summary>
    public static readonly ShipColor WindowColor = new(0.96f, 0.75f, 0.26f);

    /// <summary>
    /// The one place a material role's colour is decided. Everything that needs to know -- the two
    /// meshers, the studio palette, the Unity .mat writer -- reads it from here.
    ///
    /// Centralised because the exported tint is a <em>multiplier</em> on this colour: the vertex
    /// colour says "8% lighter than your base", and if the mesher and the .mat disagreed about what
    /// the base was, that instruction would land on the wrong colour. The panel and recess shades
    /// are derived from the hull rather than being separate parameters, so recolouring a ship keeps
    /// its plating detail coherent instead of needing three colour pickers kept in sync by hand.
    /// </summary>
    public static IReadOnlyDictionary<VoxelMaterial, ShipColor> BaseColours(ShipParameters p)
    {
        var hull = p.HullColor;
        static ShipColor Scale(ShipColor c, float f) => new(c.R * f, c.G * f, c.B * f);

        return new Dictionary<VoxelMaterial, ShipColor>
        {
            [VoxelMaterial.Hull] = hull,
            [VoxelMaterial.HullDark] = Scale(hull, 0.46f),
            [VoxelMaterial.Panel] = Scale(hull, 0.78f),
            [VoxelMaterial.Accent] = p.AccentColor,
            [VoxelMaterial.Window] = WindowColor,
            [VoxelMaterial.Glow] = p.EngineGlowColor,
            [VoxelMaterial.Cockpit] = p.CockpitTintColor,
        };
    }

    /// <summary>The material set a ship exports with. Shared with the Unity mesher so both write
    /// the same names and colours -- a bundle whose materials disagreed with the plain export
    /// would be worse than no bundle.</summary>
    internal static Dictionary<VoxelMaterial, MaterialBuilder> BuildMaterials(ShipParameters p)
    {
        var c = BaseColours(p);
        return new Dictionary<VoxelMaterial, MaterialBuilder>
        {
            [VoxelMaterial.Hull] = new MaterialBuilder("voxel_hull").WithMetallicRoughness(0.4f, 0.6f).WithBaseColor(c[VoxelMaterial.Hull].ToVector4()),
            [VoxelMaterial.HullDark] = new MaterialBuilder("voxel_hull_dark").WithMetallicRoughness(0.5f, 0.65f).WithBaseColor(c[VoxelMaterial.HullDark].ToVector4()),
            [VoxelMaterial.Panel] = new MaterialBuilder("voxel_panel").WithMetallicRoughness(0.45f, 0.6f).WithBaseColor(c[VoxelMaterial.Panel].ToVector4()),
            [VoxelMaterial.Accent] = new MaterialBuilder("voxel_accent").WithMetallicRoughness(0.4f, 0.55f).WithBaseColor(c[VoxelMaterial.Accent].ToVector4()),
            [VoxelMaterial.Window] = new MaterialBuilder("voxel_window").WithBaseColor(c[VoxelMaterial.Window].ToVector4()).WithEmissive(c[VoxelMaterial.Window].ToVector3(), 1.2f),
            [VoxelMaterial.Glow] = new MaterialBuilder("voxel_glow").WithBaseColor(c[VoxelMaterial.Glow].ToVector4()).WithEmissive(c[VoxelMaterial.Glow].ToVector3(), 1.4f),
            [VoxelMaterial.Cockpit] = new MaterialBuilder("voxel_cockpit").WithMetallicRoughness(0.2f, 0.2f)
                .WithBaseColor(c[VoxelMaterial.Cockpit].ToVector4(0.85f)).WithAlpha(AlphaMode.BLEND, 0.1f).WithDoubleSide(true),
        };
    }

    public static void AddToScene(SceneBuilder scene, VoxelGrid grid, float voxelSize, ShipParameters p)
    {
        var materials = BuildMaterials(p);

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
