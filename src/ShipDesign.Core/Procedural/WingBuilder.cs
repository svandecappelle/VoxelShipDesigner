using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Builds a mirrored pair of flat trapezoidal wing panels attached to the hull. The panel is
/// authored once (spanning local +X, root at the origin) and instanced twice: once as-is for
/// the right side, once with an X-mirrored transform for the left -- the same technique used
/// for kitbashed socket parts, just applied to procedurally-built geometry instead.
/// </summary>
public static class WingBuilder
{
    private sealed record WingConfig(float AttachU, float ChordRootFactor, float ChordTipFactor, float DihedralDegrees);

    private static readonly Dictionary<WingStyle, WingConfig> Configs = new()
    {
        [WingStyle.Swept] = new WingConfig(0.55f, 0.85f, 0.35f, 6f),
        [WingStyle.Delta] = new WingConfig(0.60f, 1.30f, 0.08f, 2f),
        [WingStyle.TwinFin] = new WingConfig(0.82f, 0.50f, 0.25f, 55f),
    };

    public static IReadOnlyList<(IMeshBuilder<MaterialBuilder> Mesh, Matrix4x4 Transform)> Build(ShipParameters p, HullClassPreset preset)
    {
        if (p.WingStyle == WingStyle.None || !Configs.TryGetValue(p.WingStyle, out var cfg))
            return Array.Empty<(IMeshBuilder<MaterialBuilder>, Matrix4x4)>();

        var attachZ = HullBuilder.ZAt(cfg.AttachU, p.Length);
        var rootR = HullBuilder.RadiusAt(cfg.AttachU, 0f, p, preset); // theta=0: the hull's side
        var span = MathF.Max(p.WingSpan, 0.1f);
        var sweep = p.WingSweepDegrees * MathF.PI / 180f;
        var chordRoot = cfg.ChordRootFactor * (p.Length * 0.18f);
        var chordTip = cfg.ChordTipFactor * (p.Length * 0.18f);
        var thickness = 0.12f + p.Beam * 0.01f;
        var dihedral = cfg.DihedralDegrees * MathF.PI / 180f;

        var mesh = BuildPrism(chordRoot, chordTip, span, sweep, thickness, p.AccentColor);

        var rootOffset = rootR * 0.6f;
        var rightTransform = Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, dihedral)
            * Matrix4x4.CreateTranslation(rootOffset, 0, attachZ);
        // Mirroring the fully-composed world transform (rather than mirroring the local
        // geometry before rotating) keeps the dihedral lift the same sign on both sides --
        // mirror-then-rotate would tip one wing up and the other down.
        var leftTransform = rightTransform * Matrix4x4.CreateScale(-1, 1, 1);

        return new (IMeshBuilder<MaterialBuilder>, Matrix4x4)[]
        {
            (mesh, rightTransform),
            (mesh, leftTransform),
        };
    }

    private static MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> BuildPrism(
        float chordRoot, float chordTip, float span, float sweep, float thickness, ShipColor color)
    {
        var material = new MaterialBuilder("wing").WithMetallicRoughness(0.5f, 0.45f).WithBaseColor(color.ToVector4());
        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("wing");
        var prim = mesh.UsePrimitive(material);

        // Outline in the (spanwise X, chordwise Z) plane: root leading -> tip leading ->
        // tip trailing -> root trailing. Positive sweep drags the tip aft (-Z).
        var tanSweep = MathF.Tan(sweep);
        var p0 = new Vector2(0, chordRoot / 2f);
        var p1 = new Vector2(span, chordRoot / 2f - span * tanSweep);
        var p2 = new Vector2(span, chordRoot / 2f - span * tanSweep - chordTip);
        var p3 = new Vector2(0, -chordRoot / 2f);

        var halfT = thickness / 2f;
        Vector3 Top(Vector2 p) => new(p.X, halfT, p.Y);
        Vector3 Bottom(Vector2 p) => new(p.X, -halfT, p.Y);

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            var va = new VertexPositionNormal(a, normal);
            var vb = new VertexPositionNormal(b, normal);
            var vc = new VertexPositionNormal(c, normal);
            var vd = new VertexPositionNormal(d, normal);
            prim.AddTriangle(Wrap(va), Wrap(vb), Wrap(vc));
            prim.AddTriangle(Wrap(va), Wrap(vc), Wrap(vd));
        }

        Quad(Top(p0), Top(p1), Top(p2), Top(p3), Vector3.UnitY);
        Quad(Bottom(p3), Bottom(p2), Bottom(p1), Bottom(p0), -Vector3.UnitY);

        void Side(Vector2 a, Vector2 b)
        {
            var edge = b - a;
            var outward = Vector3.Normalize(new Vector3(-edge.Y, 0, edge.X));
            Quad(Top(a), Bottom(a), Bottom(b), Top(b), outward);
        }
        Side(p0, p1);
        Side(p1, p2);
        Side(p2, p3);
        Side(p3, p0);

        return mesh;
    }

    private static IVertexBuilder Wrap(VertexPositionNormal v) => new VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>(v);
}
