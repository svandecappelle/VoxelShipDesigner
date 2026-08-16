using System.Globalization;
using System.Text;
using SharpGLTF.Scenes;

namespace ShipDesign.Core.Procedural;

/// <summary>
/// Writes everything Unity needs for one ship into a folder: the mesh, a shader that can read the
/// baked occlusion, a description of the materials, and the import steps.
///
/// The mesh alone is not enough. Occlusion is carried in vertex colours, and no stock URP material
/// reads those -- imported against URP/Lit the ship would come in flat, with the shading present
/// in the file but invisible. Shipping the shader beside the mesh is what makes the export
/// self-contained.
/// </summary>
public static class UnityBundleExporter
{
    public sealed record Result(string Folder, IReadOnlyList<string> Files, int Triangles);

    public static Result Export(ShipParameters p, string folder, string designation)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();

        var grid = ProceduralShipBuilder.BuildVoxels(p);
        var scene = new SceneBuilder();
        VoxelUnityMesher.AddToScene(scene, grid, VoxelShipGrower.VoxelSize, p);
        var model = scene.ToGltf2();

        var meshPath = Path.Combine(folder, $"{designation}.glb");
        model.SaveGLB(meshPath);
        written.Add(Path.GetFileName(meshPath));

        var shaderPath = Path.Combine(folder, "VoxelShipURP.shader");
        File.WriteAllText(shaderPath, ShaderSource, new UTF8Encoding(false));
        written.Add(Path.GetFileName(shaderPath));

        var manifestPath = Path.Combine(folder, "materials.json");
        File.WriteAllText(manifestPath, BuildManifest(p, designation), new UTF8Encoding(false));
        written.Add(Path.GetFileName(manifestPath));

        var readmePath = Path.Combine(folder, "LISEZMOI.md");
        File.WriteAllText(readmePath, BuildReadme(designation), new UTF8Encoding(false));
        written.Add(Path.GetFileName(readmePath));

        return new Result(folder, written, ProceduralShipBuilder.CountTriangles(model));
    }

    private static string Hex(ShipColor c) =>
        $"#{(int)(Math.Clamp(c.R, 0f, 1f) * 255):X2}{(int)(Math.Clamp(c.G, 0f, 1f) * 255):X2}{(int)(Math.Clamp(c.B, 0f, 1f) * 255):X2}";

    private static string BuildManifest(ShipParameters p, string designation)
    {
        var i = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"designation\": \"{designation}\",");
        sb.AppendLine($"  \"voxelSize\": {VoxelShipGrower.VoxelSize.ToString("0.###", i)},");
        sb.AppendLine("  \"ambientOcclusion\": { \"channel\": \"COLOR_0\", \"encoding\": \"greyscale multiplier on albedo\" },");
        sb.AppendLine("  \"materials\": [");

        // Emissive strength is reported because it is the number bloom keys off: below 1 nothing
        // in the scene will ever exceed the threshold and the glow simply will not appear.
        var rows = new (string Name, string Colour, string Notes)[]
        {
            ("voxel_hull", Hex(p.HullColor), "métallicité 0.4, rugosité 0.6"),
            ("voxel_hull_dark", Hex(new ShipColor(p.HullColor.R * 0.46f, p.HullColor.G * 0.46f, p.HullColor.B * 0.46f)), "creux et joints"),
            ("voxel_panel", Hex(new ShipColor(p.HullColor.R * 0.78f, p.HullColor.G * 0.78f, p.HullColor.B * 0.78f)), "plaques en relief"),
            ("voxel_accent", Hex(p.AccentColor), "marquages"),
            ("voxel_window", "#F5BF42", "émissif, intensité 1.2"),
            ("voxel_glow", Hex(p.EngineGlowColor), "émissif, intensité 1.4"),
            ("voxel_cockpit", Hex(p.CockpitTintColor), "translucide, alpha 0.85, double face"),
        };

        for (var r = 0; r < rows.Length; r++)
        {
            var (name, colour, notes) = rows[r];
            var comma = r == rows.Length - 1 ? "" : ",";
            sb.AppendLine($"    {{ \"name\": \"{name}\", \"baseColor\": \"{colour}\", \"notes\": \"{notes}\" }}{comma}");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildReadme(string designation) => $$"""
        # {{designation}} — import Unity

        Contenu du dossier :

        | Fichier | Rôle |
        |---|---|
        | `{{designation}}.glb` | Le maillage, avec l'occlusion ambiante cuite dans les couleurs de sommet (`COLOR_0`) et l'émissif en HDR (`KHR_materials_emissive_strength`). |
        | `VoxelShipURP.shader` | Shader URP qui multiplie l'albédo par la couleur de sommet. Sans lui l'occlusion est présente dans le fichier mais invisible. |
        | `materials.json` | Couleurs et réglages de chaque matériau, pour reconstruire les matériaux à la main si besoin. |

        ## Étapes

        1. Glisser les quatre fichiers dans `Assets/`.
        2. Sur le prefab importé, remplacer le shader de chaque matériau par **ShipDesign/Voxel Ship URP**.
        3. Ajouter un **Volume** avec **Bloom** et **Tonemapping (ACES)**. Sans bloom, les réacteurs et
           les hublots restent des aplats : le halo est un effet d'image, pas une propriété du modèle.
        4. Vérifier que le projet est en espace colorimétrique **Linear**
           (*Project Settings → Player → Color Space*). En Gamma tout paraît délavé.

        ## Points à surveiller

        - **L'occlusion n'apparaît pas** : le matériau utilise encore URP/Lit, qui ignore la couleur
          de sommet. C'est le symptôme le plus courant.
        - **Pas de halo** : le bloom n'est pas dans la scène, ou son seuil est au-dessus de
          l'intensité émissive (1.2 pour les hublots, 1.4 pour les réacteurs).
        - **Métal terne** : les matériaux de coque sont à 0.4 de métallicité et ont besoin de
          réflexions d'environnement. Sans skybox ni reflection probe, descendre la métallicité
          vers 0 donne un meilleur résultat.

        Une unité Unity correspond à une unité du générateur ; l'échelle d'import peut rester à 1.
        """;

    /// <summary>
    /// A URP shader whose only real job is multiplying albedo by vertex colour, so the baked
    /// occlusion shows. Written against URP's standard lit pattern rather than as a Shader Graph:
    /// a .shader file is plain text that survives version differences and diffs, whereas a graph
    /// asset is tied to the package version that produced it.
    /// </summary>
    private const string ShaderSource = """
        Shader "ShipDesign/Voxel Ship URP"
        {
            Properties
            {
                _BaseMap("Base Map", 2D) = "white" {}
                _BaseColor("Base Color", Color) = (1,1,1,1)
                _Metallic("Metallic", Range(0,1)) = 0.0
                _Smoothness("Smoothness", Range(0,1)) = 0.4
                _EmissionColor("Emission Color", Color) = (0,0,0,0)
                _OcclusionStrength("Vertex AO Strength", Range(0,1)) = 1.0
            }

            SubShader
            {
                Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
                LOD 300

                Pass
                {
                    Name "ForwardLit"
                    Tags { "LightMode" = "UniversalForward" }

                    HLSLPROGRAM
                    #pragma vertex Vert
                    #pragma fragment Frag
                    #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
                    #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
                    #pragma multi_compile _ _ADDITIONAL_LIGHTS
                    #pragma multi_compile_fog

                    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
                    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

                    CBUFFER_START(UnityPerMaterial)
                        float4 _BaseMap_ST;
                        half4 _BaseColor;
                        half _Metallic;
                        half _Smoothness;
                        half4 _EmissionColor;
                        half _OcclusionStrength;
                    CBUFFER_END

                    TEXTURE2D(_BaseMap);
                    SAMPLER(sampler_BaseMap);

                    struct Attributes
                    {
                        float4 positionOS : POSITION;
                        float3 normalOS   : NORMAL;
                        float2 uv         : TEXCOORD0;
                        half4  color      : COLOR;
                    };

                    struct Varyings
                    {
                        float4 positionCS : SV_POSITION;
                        float3 positionWS : TEXCOORD0;
                        float3 normalWS   : TEXCOORD1;
                        float2 uv         : TEXCOORD2;
                        half4  color      : COLOR;
                    };

                    Varyings Vert(Attributes input)
                    {
                        Varyings output;
                        VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                        VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);
                        output.positionCS = pos.positionCS;
                        output.positionWS = pos.positionWS;
                        output.normalWS = nrm.normalWS;
                        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                        output.color = input.color;
                        return output;
                    }

                    half4 Frag(Varyings input) : SV_Target
                    {
                        half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                        // This one line is the whole point of the shader: COLOR_0 carries the baked
                        // ambient occlusion, and no stock URP material multiplies it into albedo.
                        half ao = lerp(1.0h, input.color.r, _OcclusionStrength);
                        albedo.rgb *= ao;

                        InputData inputData = (InputData)0;
                        inputData.positionWS = input.positionWS;
                        inputData.normalWS = normalize(input.normalWS);
                        inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                        inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                        inputData.bakedGI = SampleSH(inputData.normalWS);

                        SurfaceData surfaceData = (SurfaceData)0;
                        surfaceData.albedo = albedo.rgb;
                        surfaceData.metallic = _Metallic;
                        surfaceData.smoothness = _Smoothness;
                        surfaceData.emission = _EmissionColor.rgb;
                        surfaceData.occlusion = 1.0h;
                        surfaceData.alpha = 1.0h;

                        return UniversalFragmentPBR(inputData, surfaceData);
                    }
                    ENDHLSL
                }

                UsePass "Universal Render Pipeline/Lit/ShadowCaster"
                UsePass "Universal Render Pipeline/Lit/DepthOnly"
            }

            FallBack "Universal Render Pipeline/Lit"
        }
        """;
}
