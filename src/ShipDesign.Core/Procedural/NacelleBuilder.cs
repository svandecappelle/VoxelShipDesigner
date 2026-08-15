using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Builds a mirrored pair of secondary hull pods ("nacelles") mounted on pylons out to the
/// sides of the main hull, parallel to its length axis -- a Star Trek warp-nacelle style
/// silhouette. This is the piece that turns the ship from "one hull with things bolted on" into
/// "several distinct volumes joined by structure", which a single greebled/angular hull still
/// doesn't achieve on its own.
/// </summary>
public static class NacelleBuilder
{
    // Small tube-like profile: nose taper, near-constant body, tapered tail -- independent of
    // the main hull's own profile (see HullClassPreset), reusing HullBuilder.BuildVolume's
    // generic chamfered-octagon sweep for a family resemblance without being identical.
    private static readonly HullProfilePoint[] Profile =
    {
        new(0.00f, 0f, 0f),
        new(0.14f, 0.85f, 0.85f),
        new(0.70f, 1f, 1f),
        new(1.00f, 0.55f, 0.55f),
    };

    public static IReadOnlyList<(IMeshBuilder<MaterialBuilder> Mesh, Matrix4x4 Transform)> Build(ShipParameters p, HullClassPreset preset)
    {
        if (!p.Nacelles)
            return Array.Empty<(IMeshBuilder<MaterialBuilder>, Matrix4x4)>();

        var size = MathF.Max(p.NacelleSize, 0.2f);
        var nacelleLength = p.Length * 0.55f * size;
        var nacelleBeam = p.Beam * 0.30f * size;

        var attachU = MathF.Min(preset.TailFraction, 0.75f);
        var hullHalfWidth = HullBuilder.RadiusAt(attachU, 0f, p, preset);
        var nacelleZCenter = HullBuilder.ZAt(attachU, p.Length) - nacelleLength * 0.1f;
        var nacelleCenterX = hullHalfWidth + nacelleBeam * 1.4f;

        var nacelleMaterial = new MaterialBuilder("nacelle")
            .WithMetallicRoughness(0.5f, 0.4f)
            .WithBaseColor(p.AccentColor.ToVector4());
        var nacelleMesh = HullBuilder.BuildVolume(Profile, nacelleLength, nacelleBeam, 0.35f, nacelleMaterial, "nacelle");

        var pylonMaterial = new MaterialBuilder("pylon")
            .WithMetallicRoughness(0.5f, 0.5f)
            .WithBaseColor(p.HullColor.ToVector4());
        var pylonMesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty>("pylon");
        var pylonPrim = pylonMesh.UsePrimitive(pylonMaterial);
        // Overlap slightly into both the hull and the nacelle so there's no visible gap at
        // either join, rather than trying to land exactly on both surfaces.
        var pylonStartX = hullHalfWidth * 0.7f;
        var pylonEndX = nacelleCenterX - nacelleBeam * 0.35f;
        var pylonCenterX = (pylonStartX + pylonEndX) / 2f;
        var pylonHalfX = MathF.Max((pylonEndX - pylonStartX) / 2f, 0.05f);
        MeshUtil.AddBox(pylonPrim, new Vector3(pylonCenterX, -nacelleBeam * 0.1f, 0f),
            new Vector3(pylonHalfX, nacelleBeam * 0.18f, nacelleLength * 0.22f));

        var rightNacelleTransform = Matrix4x4.CreateTranslation(nacelleCenterX, 0, nacelleZCenter);
        var leftNacelleTransform = rightNacelleTransform * Matrix4x4.CreateScale(-1, 1, 1);
        var rightPylonTransform = Matrix4x4.CreateTranslation(0, 0, nacelleZCenter);
        var leftPylonTransform = rightPylonTransform * Matrix4x4.CreateScale(-1, 1, 1);

        return new (IMeshBuilder<MaterialBuilder>, Matrix4x4)[]
        {
            (nacelleMesh, rightNacelleTransform),
            (nacelleMesh, leftNacelleTransform),
            (pylonMesh, rightPylonTransform),
            (pylonMesh, leftPylonTransform),
        };
    }
}
