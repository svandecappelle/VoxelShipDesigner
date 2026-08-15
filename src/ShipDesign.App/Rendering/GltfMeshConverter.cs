using System;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ShipDesign.App.Rendering;

/// <summary>
/// Converts a SharpGLTF ModelRoot (System.Numerics based) into WPF's Media3D types so the
/// procedurally-built ship can be shown in a HelixToolkit viewport, carrying over each
/// primitive's base color and emissive tint so the livery choices are actually visible.
/// </summary>
public static class GltfMeshConverter
{
    public static Model3DGroup ToModel3DGroup(SharpGLTF.Schema2.ModelRoot model)
    {
        var group = new Model3DGroup();

        foreach (var node in model.LogicalNodes)
        {
            if (node.Mesh is null)
                continue;

            var worldMatrix = node.WorldMatrix;
            // Correct normal transform under non-uniform scale (used by the greeble details)
            // is the inverse-transpose of the world matrix, not the world matrix itself.
            var hasInverse = Matrix4x4.Invert(worldMatrix, out var inverse);
            var normalMatrix = hasInverse ? Matrix4x4.Transpose(inverse) : Matrix4x4.Identity;

            foreach (var primitive in node.Mesh.Primitives)
            {
                var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                if (positions is null)
                    continue;

                var geometry = new MeshGeometry3D();
                foreach (var p in positions)
                    geometry.Positions.Add(new Point3D(p.X, p.Y, p.Z));

                var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                if (normals is not null)
                    foreach (var n in normals)
                    {
                        var transformed = Vector3.TransformNormal(n, normalMatrix);
                        geometry.Normals.Add(new Vector3D(transformed.X, transformed.Y, transformed.Z));
                    }

                foreach (var index in primitive.GetIndices())
                    geometry.TriangleIndices.Add((int)index);

                var material = BuildMaterial(primitive.Material);
                group.Children.Add(new GeometryModel3D(geometry, material)
                {
                    // Mirrored parts (negative-determinant transform) flip triangle winding,
                    // which would otherwise cull the now-inverted front face.
                    BackMaterial = material,
                    Transform = ToTransform(worldMatrix)
                });
            }
        }

        return group;
    }

    private static Material BuildMaterial(SharpGLTF.Schema2.Material? gltfMaterial)
    {
        var baseColor = gltfMaterial?.FindChannel("BaseColor")?.Color ?? new Vector4(0.7f, 0.7f, 0.7f, 1f);
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(Math.Clamp(baseColor.W, 0f, 1f) * 255),
            (byte)(Math.Clamp(baseColor.X, 0f, 1f) * 255),
            (byte)(Math.Clamp(baseColor.Y, 0f, 1f) * 255),
            (byte)(Math.Clamp(baseColor.Z, 0f, 1f) * 255)));

        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(brush));

        var emissive = gltfMaterial?.FindChannel("Emissive")?.Color;
        if (emissive is { } e && e.X + e.Y + e.Z > 0.05f)
        {
            var emissiveBrush = new SolidColorBrush(Color.FromRgb(
                (byte)(Math.Clamp(e.X, 0f, 1f) * 255),
                (byte)(Math.Clamp(e.Y, 0f, 1f) * 255),
                (byte)(Math.Clamp(e.Z, 0f, 1f) * 255)));
            group.Children.Add(new EmissiveMaterial(emissiveBrush));
        }

        return group;
    }

    private static Transform3D ToTransform(Matrix4x4 m) =>
        new MatrixTransform3D(new Matrix3D(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44));
}
