using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>Builds the cockpit canopy: a partial sphere ("bubble") or a tilted box
/// ("flat canopy"), sat on top of the hull near the nose with a transparent tinted material.</summary>
public static class CockpitBuilder
{
    private const int DomeWidthSegments = 16;
    private const int DomeHeightSteps = 8;

    public static (IMeshBuilder<MaterialBuilder> Mesh, Matrix4x4 Transform)? Build(ShipParameters p, HullClassPreset preset)
    {
        if (p.CockpitStyle == CockpitStyle.None)
            return null;

        // Sit within the nose taper (scaled to how long that taper is for this hull class)
        // rather than a fixed U, so it lands somewhere already "developed" instead of right at
        // the tip for long-nosed classes like the wedge cruiser.
        var attachU = preset.NoseFraction * 0.6f;
        var z = HullBuilder.ZAt(attachU, p.Length);
        var r = HullBuilder.RadiusAt(attachU, MathF.PI / 2f, p, preset); // theta=pi/2: the hull's top
        var size = p.CockpitSize;

        var material = new MaterialBuilder("cockpit")
            .WithMetallicRoughness(0.2f, 0.15f)
            .WithBaseColor(p.CockpitTintColor.ToVector4(0.55f))
            .WithAlpha(AlphaMode.BLEND, 0.1f)
            .WithDoubleSide(true);

        var mesh = p.CockpitStyle == CockpitStyle.Bubble
            ? BuildDome(r * 0.55f * size, material)
            : BuildTiltedBox(r * 0.7f * size, r * 0.4f * size, r * 1.1f * size, material);

        var transform = Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, -0.2f)
            * Matrix4x4.CreateTranslation(0, r * 0.55f, z);

        return (mesh, transform);
    }

    /// <summary>A partial sphere: apex at the top (+Y), opening downward past the equator
    /// (thetaMax &gt; 90°) for a rounded, slightly overhanging bubble canopy silhouette.</summary>
    private static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> BuildDome(
        float radius, MaterialBuilder material)
    {
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("cockpit_bubble");
        var prim = mesh.UsePrimitive(material);

        const float thetaMax = MathF.PI / 1.6f;

        VertexPositionNormal[] Ring(float theta)
        {
            var y = radius * MathF.Cos(theta);
            var ringR = radius * MathF.Sin(theta);
            var ring = new VertexPositionNormal[DomeWidthSegments];
            for (var k = 0; k < DomeWidthSegments; k++)
            {
                // Sin/cos swapped (vs. the hull's Z-axis ring) because revolving around Y using
                // the same cos/sin-on-X/Z pattern flips handedness relative to X/Y-around-Z --
                // this keeps winding consistent with MeshUtil.StitchBand's assumptions.
                var phi = (float)k / DomeWidthSegments * MathF.PI * 2f;
                var position = new Vector3(ringR * MathF.Sin(phi), y, ringR * MathF.Cos(phi));
                ring[k] = new VertexPositionNormal(position, Vector3.Normalize(position));
            }
            return ring;
        }

        var rings = new VertexPositionNormal[DomeHeightSteps + 1][];
        for (var i = 1; i <= DomeHeightSteps; i++)
            rings[i] = Ring(i / (float)DomeHeightSteps * thetaMax);

        var apex = new VertexPositionNormal(new Vector3(0, radius, 0), Vector3.UnitY);
        MeshUtil.StitchFan(prim, apex, rings[1], flip: true);
        for (var i = 1; i < DomeHeightSteps; i++)
            MeshUtil.StitchBand(prim, rings[i], rings[i + 1]);

        return mesh;
    }

    private static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> BuildTiltedBox(
        float width, float height, float depth, MaterialBuilder material)
    {
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("cockpit_canopy");
        var prim = mesh.UsePrimitive(material);
        var h = new Vector3(width, height, depth) / 2f;

        var corners = new[]
        {
            new Vector3(-h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, -h.Z),
            new Vector3(h.X, h.Y, -h.Z), new Vector3(-h.X, h.Y, -h.Z),
            new Vector3(-h.X, -h.Y, h.Z), new Vector3(h.X, -h.Y, h.Z),
            new Vector3(h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z),
        };

        void Quad(int a, int b, int c, int d, Vector3 normal)
        {
            var va = new VertexPositionNormal(corners[a], normal);
            var vb = new VertexPositionNormal(corners[b], normal);
            var vc = new VertexPositionNormal(corners[c], normal);
            var vd = new VertexPositionNormal(corners[d], normal);
            prim.AddTriangle(Wrap(va), Wrap(vb), Wrap(vc));
            prim.AddTriangle(Wrap(va), Wrap(vc), Wrap(vd));
        }

        Quad(4, 5, 6, 7, Vector3.UnitZ);
        Quad(1, 0, 3, 2, -Vector3.UnitZ);
        Quad(0, 4, 7, 3, -Vector3.UnitX);
        Quad(5, 1, 2, 6, Vector3.UnitX);
        Quad(3, 7, 6, 2, Vector3.UnitY);
        Quad(0, 1, 5, 4, -Vector3.UnitY);

        return mesh;
    }

    private static IVertexBuilder Wrap(VertexPositionNormal v) => new VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>(v);
}
