using System.Globalization;
using System.Text;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Writes real Unity assets -- <c>.mat</c> files and the shader's <c>.meta</c> -- rather than only
/// describing the materials in a manifest.
///
/// The trick that makes this work as a drop-in is shipping the shader's .meta with a GUID we
/// choose. Unity would otherwise mint one on import, and a .mat written ahead of time could not
/// possibly reference it; with a fixed GUID on both sides the material-to-shader link resolves the
/// moment the folder lands in Assets.
/// </summary>
public static class UnityMaterialWriter
{
    /// <summary>
    /// Fixed GUID for the bundled shader. Arbitrary but stable: every bundle must use the same one
    /// or materials exported today would not find a shader imported yesterday.
    /// </summary>
    public const string ShaderGuid = "5b1d7e0a4c9f4a2b8e6d3f10a7c25e94";

    private const string ShaderName = "ShipDesign/Voxel Ship URP";

    public sealed record MaterialSpec(
        string Name,
        ShipColor BaseColor,
        float Alpha,
        float Metallic,
        float Smoothness,
        ShipColor? Emission,
        float EmissionStrength);

    /// <summary>The materials a ship exports with, in the same order and with the same names the
    /// mesh's primitives use, so assigning them is a matter of matching names.</summary>
    public static IReadOnlyList<MaterialSpec> SpecsFor(ShipParameters p)
    {
        // Straight from the mesher's own table. The base colour written here has to be the exact
        // one the mesh's vertex colours were computed against -- they encode a multiplier on it.
        var c = VoxelMesher.BaseColours(p);

        return new[]
        {
            new MaterialSpec("voxel_hull", c[VoxelMaterial.Hull], 1f, 0.4f, 0.4f, null, 0f),
            new MaterialSpec("voxel_hull_dark", c[VoxelMaterial.HullDark], 1f, 0.5f, 0.35f, null, 0f),
            new MaterialSpec("voxel_panel", c[VoxelMaterial.Panel], 1f, 0.45f, 0.4f, null, 0f),
            new MaterialSpec("voxel_accent", c[VoxelMaterial.Accent], 1f, 0.4f, 0.45f, null, 0f),
            new MaterialSpec("voxel_window", c[VoxelMaterial.Window], 1f, 0f, 0.5f, c[VoxelMaterial.Window], 1.2f),
            new MaterialSpec("voxel_glow", c[VoxelMaterial.Glow], 1f, 0f, 0.5f, c[VoxelMaterial.Glow], 1.4f),
            new MaterialSpec("voxel_cockpit", c[VoxelMaterial.Cockpit], 0.85f, 0.2f, 0.8f, null, 0f),
        };
    }

    public static IReadOnlyList<string> WriteAll(ShipParameters p, string folder)
    {
        var written = new List<string>();

        var metaPath = Path.Combine(folder, "VoxelShipURP.shader.meta");
        File.WriteAllText(metaPath, ShaderMeta, new UTF8Encoding(false));
        written.Add(Path.GetFileName(metaPath));

        foreach (var spec in SpecsFor(p))
        {
            var path = Path.Combine(folder, $"{spec.Name}.mat");
            File.WriteAllText(path, BuildMaterial(spec), new UTF8Encoding(false));
            written.Add(Path.GetFileName(path));
        }

        return written;
    }

    private static string ShaderMeta => $$"""
        fileFormatVersion: 2
        guid: {{ShaderGuid}}
        ShaderImporter:
          externalObjects: {}
          defaultTextures: []
          nonModifiableTextures: []
          userData:
          assetBundleName:
          assetBundleVariant:
        """;

    private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// One material asset. Emission is only enabled where there is emission to show: leaving the
    /// keyword on with a black colour costs a shader variant and gains nothing, and leaving it
    /// *off* on the engines would mean no bloom at all no matter how the scene is set up.
    /// </summary>
    private static string BuildMaterial(MaterialSpec spec)
    {
        var emission = spec.Emission ?? new ShipColor(0f, 0f, 0f);
        var hasEmission = spec.Emission is not null;

        // Unity's emission colour is HDR: the strength multiplies the colour rather than living in
        // a separate field, which is what pushes it past the bloom threshold.
        var er = F(emission.R * spec.EmissionStrength);
        var eg = F(emission.G * spec.EmissionStrength);
        var eb = F(emission.B * spec.EmissionStrength);

        var keywords = hasEmission ? "_EMISSION" : "";
        var flags = hasEmission ? 2 : 0; // MaterialGlobalIlluminationFlags.RealtimeEmissive / None

        return $$"""
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!21 &2100000
            Material:
              serializedVersion: 6
              m_ObjectHideFlags: 0
              m_CorrespondingSourceObject: {fileID: 0}
              m_PrefabInstance: {fileID: 0}
              m_PrefabAsset: {fileID: 0}
              m_Name: {{spec.Name}}
              m_Shader: {fileID: 4800000, guid: {{ShaderGuid}}, type: 3}
              m_Parent: {fileID: 0}
              m_ModifiedSerializedProperties: 0
              m_ValidKeywords:
              - {{keywords}}
              m_InvalidKeywords: []
              m_LightmapFlags: {{flags}}
              m_EnableInstancingVariants: 0
              m_DoubleSidedGI: 0
              m_CustomRenderQueue: -1
              stringTagMap: {}
              disabledShaderPasses: []
              m_SavedProperties:
                serializedVersion: 3
                m_TexEnvs:
                - _BaseMap:
                    m_Texture: {fileID: 0}
                    m_Scale: {x: 1, y: 1}
                    m_Offset: {x: 0, y: 0}
                m_Ints: []
                m_Floats:
                - _Metallic: {{F(spec.Metallic)}}
                - _Smoothness: {{F(spec.Smoothness)}}
                - _OcclusionStrength: 1
                m_Colors:
                - _BaseColor: {r: {{F(spec.BaseColor.R)}}, g: {{F(spec.BaseColor.G)}}, b: {{F(spec.BaseColor.B)}}, a: {{F(spec.Alpha)}}}
                - _EmissionColor: {r: {{er}}, g: {{eg}}, b: {{eb}}, a: 1}
              m_BuildTextureStacks: []
            """;
    }

    /// <summary>Shader name as Unity will see it, for the readme and for anyone wiring materials
    /// up by hand.</summary>
    public static string ShaderDisplayName => ShaderName;
}
