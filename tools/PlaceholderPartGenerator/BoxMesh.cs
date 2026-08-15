using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace PlaceholderPartGenerator;

/// <summary>
/// Builds a simple axis-aligned box mesh, flat-shaded, centered on the local origin.
/// Good enough for greybox test parts; real parts will come from Blender later.
/// </summary>
public static class BoxMesh
{
    public static MeshBuilder<MaterialBuilder, VertexPosition, VertexEmpty, VertexEmpty> Create(
        string name, Vector3 size, Vector4 color)
    {
        var material = new MaterialBuilder(name + "_mat").WithBaseColor(color).WithDoubleSide(false);
        var mesh = new MeshBuilder<MaterialBuilder, VertexPosition, VertexEmpty, VertexEmpty>(name);
        var prim = mesh.UsePrimitive(material);

        var h = size / 2f;
        var p = new[]
        {
            new Vector3(-h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, -h.Z),
            new Vector3(h.X, h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z),
            new Vector3(-h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, h.Z),
            new Vector3(h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z),
        };

        void Quad(int a, int b, int c, int d) => prim.AddQuadrangle(
            new VertexPosition(p[a]), new VertexPosition(p[b]),
            new VertexPosition(p[c]), new VertexPosition(p[d]));

        Quad(4, 5, 6, 7); // +Z
        Quad(1, 0, 3, 2); // -Z
        Quad(0, 4, 7, 3); // -X
        Quad(5, 1, 2, 6); // +X
        Quad(3, 7, 6, 2); // +Y
        Quad(0, 1, 5, 4); // -Y

        return mesh;
    }
}
