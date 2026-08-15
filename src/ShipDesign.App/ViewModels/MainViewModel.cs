using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;
using ShipDesign.App.Rendering;
using ShipDesign.Core.Loading;

namespace ShipDesign.App.ViewModels;

public sealed class MainViewModel
{
    public Model3D? ShipModel { get; }
    public string StatusText { get; }

    public MainViewModel()
    {
        var partsDirectory = FindPartsDirectory();
        if (partsDirectory is null)
        {
            StatusText = "Dossier Assets/Parts introuvable.";
            return;
        }

        var library = PartLibrary.LoadFromDirectory(partsDirectory);
        var firstPart = library.Parts.FirstOrDefault();
        if (firstPart is null)
        {
            StatusText = $"Aucune pièce dans {partsDirectory}. Ajoute un .glb/.gltf pour le voir ici.";
            return;
        }

        ShipModel = GltfMeshConverter.ToModel3DGroup(firstPart);
        StatusText = $"Pièce chargée : {firstPart.Id} ({library.Parts.Count} pièce(s) dans la bibliothèque)";
    }

    private static string? FindPartsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Assets", "Parts");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
