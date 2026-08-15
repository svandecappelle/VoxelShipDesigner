using System.Numerics;
using System.Text.Json;
using PlaceholderPartGenerator;
using SharpGLTF.Scenes;

var outputDir = Path.Combine(FindRepoRoot(), "Assets", "Parts");
Directory.CreateDirectory(outputDir);

ExportPart(
    outputDir, "hull_fighter_01",
    BoxMesh.Create("hull", new Vector3(1.2f, 0.6f, 4f), new Vector4(0.55f, 0.55f, 0.6f, 1f)),
    category: "Hull", sizeClass: "Medium", tags: new[] { "fighter" },
    sockets: new (string, Vector3)[]
    {
        ("wing_L", new Vector3(-0.7f, 0f, 0.3f)),
        ("wing_R", new Vector3(0.7f, 0f, 0.3f)),
        ("engine_L", new Vector3(-0.35f, 0f, -2f)),
        ("engine_R", new Vector3(0.35f, 0f, -2f)),
    });

ExportPart(
    outputDir, "wing_basic_01",
    BoxMesh.Create("wing", new Vector3(1.4f, 0.08f, 0.6f), new Vector4(0.35f, 0.4f, 0.45f, 1f)),
    category: "Wing", sizeClass: "Medium", tags: new[] { "fighter" },
    sockets: Array.Empty<(string, Vector3)>());

ExportPart(
    outputDir, "engine_basic_01",
    BoxMesh.Create("engine", new Vector3(0.35f, 0.35f, 0.9f), new Vector4(0.2f, 0.2f, 0.22f, 1f)),
    category: "Engine", sizeClass: "Medium", tags: new[] { "fighter" },
    sockets: Array.Empty<(string, Vector3)>());

Console.WriteLine($"Pieces generees dans {outputDir}");

static void ExportPart(
    string outputDir, string id,
    SharpGLTF.Geometry.IMeshBuilder<SharpGLTF.Materials.MaterialBuilder> mesh,
    string category, string sizeClass, string[] tags,
    (string Name, Vector3 Position)[] sockets)
{
    var scene = new SceneBuilder();
    scene.AddRigidMesh(mesh, new NodeBuilder(id));

    foreach (var socket in sockets)
        scene.AddNode(new NodeBuilder("socket_" + socket.Name).WithLocalTranslation(socket.Position));

    var model = scene.ToGltf2();
    model.SaveGLB(Path.Combine(outputDir, id + ".glb"));

    var metadata = new { category, sizeClass, tags };
    File.WriteAllText(
        Path.Combine(outputDir, id + ".json"),
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"  {id}.glb ({sockets.Length} socket(s))");
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ShipDesign.sln")))
        dir = dir.Parent;

    return dir?.FullName ?? throw new InvalidOperationException("ShipDesign.sln introuvable en remontant depuis " + AppContext.BaseDirectory);
}
