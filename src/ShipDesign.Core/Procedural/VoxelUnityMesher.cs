using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Meshes a voxel grid with ambient occlusion and per-voxel colour variation baked into vertex
/// colours (glTF COLOR_0).
///
/// Kept separate from <see cref="VoxelMesher"/> rather than folded into it: vertex colours are
/// invisible under a stock URP/Lit material, so a plain .glb carrying them would look no different
/// while quietly costing every vertex an extra attribute. They are only worth exporting alongside
/// the shader that reads them, which is what the Unity bundle ships.
///
/// COLOR_0 is RGB rather than greyscale, because occlusion is a brightness and the tint is a hue
/// shift, and one channel cannot carry both. The two multiply: occlusion says how deep in shadow a
/// corner is, the tint says what shade of the material that particular block is made of.
///
/// Occlusion is per *corner* here, unlike the studio view which has to average it over a face --
/// glTF can interpolate a value across a triangle, and WPF's Media3D cannot. The tint is per voxel
/// on both sides, since varying it within a cube would break the cube up.
/// </summary>
public static class VoxelUnityMesher
{
    private static readonly (int X, int Y, int Z)[] Directions =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    };

    /// <summary>
    /// The four corners of each face, as sign triples, in the same order
    /// <see cref="MeshUtil.AddBoxFaces"/> emits them. The order matters: it is what keeps the
    /// winding identical to the plain exporter, so both files agree on which way a face points.
    /// </summary>
    private static readonly (int X, int Y, int Z)[][] FaceCorners =
    {
        new[] { (1, -1, 1), (1, -1, -1), (1, 1, -1), (1, 1, 1) },       // +X
        new[] { (-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1) },   // -X
        new[] { (-1, 1, 1), (1, 1, 1), (1, 1, -1), (-1, 1, -1) },       // +Y
        new[] { (-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1) },   // -Y
        new[] { (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1) },       // +Z
        new[] { (1, -1, -1), (-1, -1, -1), (-1, 1, -1), (1, 1, -1) },   // -Z
    };

    public static void AddToScene(SceneBuilder scene, VoxelGrid grid, float voxelSize, ShipParameters p)
    {
        var materials = VoxelMesher.BuildMaterials(p);
        var baseColours = VoxelMesher.BaseColours(p);
        var tint = VoxelTint.For(grid);
        var meshes = new Dictionary<VoxelMaterial, MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexColor1, VertexEmpty>>();
        var prims = new Dictionary<VoxelMaterial, IPrimitiveBuilder>();

        foreach (var ((x, y, z), material) in grid.Voxels)
        {
            // Computed once per voxel and handed to every face of it. Lamps are left alone: a light
            // source is not made of plating, and mottling one would show as a patchy glow.
            var variation = IsLit(material)
                ? (1f, 1f, 1f)
                : VoxelTint.Multiplier(baseColours[material], tint.VariantFor(x, y, z));

            for (var d = 0; d < Directions.Length; d++)
            {
                var n = Directions[d];
                if (grid.IsFilled(x + n.X, y + n.Y, z + n.Z))
                    continue;

                if (!meshes.TryGetValue(material, out var mesh))
                {
                    mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexColor1, VertexEmpty>($"voxels_{material}");
                    meshes[material] = mesh;
                    prims[material] = mesh.UsePrimitive(materials[material]);
                }

                AddFace(prims[material], grid, (x, y, z), d, voxelSize, material, variation);
            }
        }

        foreach (var mesh in meshes.Values)
            scene.AddRigidMesh(mesh, Matrix4x4.Identity);
    }

    /// <summary>Lamps do not get darker for sitting in a corner, nor mottled for being made of
    /// plating -- occluding or tinting them would fight the bloom they exist to feed.</summary>
    private static bool IsLit(VoxelMaterial material) =>
        material is VoxelMaterial.Glow or VoxelMaterial.Window;

    private static void AddFace(
        IPrimitiveBuilder prim, VoxelGrid grid, (int X, int Y, int Z) voxel, int direction, float voxelSize,
        VoxelMaterial material, (float R, float G, float B) variation)
    {
        var n = Directions[direction];
        var normal = new Vector3(n.X, n.Y, n.Z);
        var (ta, tb) = VoxelAmbientOcclusion.TangentsFor(n);

        var lit = IsLit(material);

        var half = voxelSize / 2f;
        var centre = new Vector3(voxel.X, voxel.Y, voxel.Z) * voxelSize;

        var corners = FaceCorners[direction];
        var built = new VertexBuilder<VertexPositionNormal, VertexColor1, VertexEmpty>[4];

        for (var i = 0; i < 4; i++)
        {
            var s = corners[i];
            var position = centre + new Vector3(s.X * half, s.Y * half, s.Z * half);

            var shade = 1f;
            if (!lit)
            {
                // The corner's tangent directions are just its own sign components along the
                // face's two in-plane axes.
                var signA = s.X * ta.X + s.Y * ta.Y + s.Z * ta.Z;
                var signB = s.X * tb.X + s.Y * tb.Y + s.Z * tb.Z;
                var level = VoxelAmbientOcclusion.CornerLevel(grid, voxel, n,
                    (ta.X * signA, ta.Y * signA, ta.Z * signA),
                    (tb.X * signB, tb.Y * signB, tb.Z * signB));
                shade = VoxelAmbientOcclusion.Shade(level);
            }

            built[i] = new VertexBuilder<VertexPositionNormal, VertexColor1, VertexEmpty>(
                new VertexPositionNormal(position, normal),
                new VertexColor1(new Vector4(
                    shade * variation.R, shade * variation.G, shade * variation.B, 1f)));
        }

        prim.AddTriangle(built[0], built[1], built[2]);
        prim.AddTriangle(built[0], built[2], built[3]);
    }
}
