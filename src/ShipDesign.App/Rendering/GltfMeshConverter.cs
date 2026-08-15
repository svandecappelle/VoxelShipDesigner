using System.Windows.Media;
using System.Windows.Media.Media3D;
using ShipDesign.Core.Models;

namespace ShipDesign.App.Rendering;

/// <summary>
/// Converts SharpGLTF mesh data (System.Numerics based) into WPF's Media3D types
/// so parts can be shown in a HelixToolkit viewport.
/// </summary>
public static class GltfMeshConverter
{
    public static Model3DGroup ToModel3DGroup(Part part)
    {
        var group = new Model3DGroup();

        foreach (var node in part.Model.LogicalNodes)
        {
            if (node.Mesh is null)
                continue;

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
                        geometry.Normals.Add(new Vector3D(n.X, n.Y, n.Z));

                foreach (var index in primitive.GetIndices())
                    geometry.TriangleIndices.Add((int)index);

                var material = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));
                group.Children.Add(new GeometryModel3D(geometry, material)
                {
                    Transform = ToTransform(node.WorldMatrix)
                });
            }
        }

        return group;
    }

    private static Transform3D ToTransform(System.Numerics.Matrix4x4 m) =>
        new MatrixTransform3D(new Matrix3D(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44));
}
