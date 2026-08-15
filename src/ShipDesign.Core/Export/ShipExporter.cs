using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using ShipDesign.Core.Generation;

namespace ShipDesign.Core.Export;

/// <summary>
/// Flattens a ShipInstance's placed parts into a single glTF scene and writes it as .glb,
/// ready to drop into a Unity project's Assets folder.
/// </summary>
public static class ShipExporter
{
    public static void Export(ShipInstance ship, string outputPath)
    {
        var scene = new SceneBuilder();

        foreach (var placed in ship.Parts)
        {
            foreach (var node in placed.Part.Model.LogicalNodes)
            {
                if (node.Mesh is null)
                    continue;

                var meshBuilder = node.Mesh.ToMeshBuilder();
                var worldTransform = node.WorldMatrix * placed.WorldTransform;
                scene.AddRigidMesh(meshBuilder, worldTransform);
            }
        }

        var model = scene.ToGltf2();
        model.SaveGLB(outputPath);
    }
}
