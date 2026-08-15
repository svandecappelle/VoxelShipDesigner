using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using ShipDesign.App.Rendering;
using ShipDesign.Core.Export;
using ShipDesign.Core.Generation;
using ShipDesign.Core.Loading;
using ShipDesign.Core.Models;

namespace ShipDesign.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ShipAssembler? _assembler;
    private readonly ShipTemplate _template;
    private ShipInstance? _currentShip;

    private Model3D? _shipModel;
    private string _statusText = "";
    private int _seed;

    public Model3D? ShipModel { get => _shipModel; private set { _shipModel = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    public int Seed { get => _seed; private set { _seed = value; OnPropertyChanged(); } }

    public ICommand RegenerateCommand { get; }
    public ICommand ExportCommand { get; }

    public MainViewModel()
    {
        RegenerateCommand = new RelayCommand(_ => Regenerate(), _ => _assembler is not null);
        ExportCommand = new RelayCommand(_ => Export(), _ => _currentShip is not null);

        _template = new ShipTemplate
        {
            Name = "Fighter",
            HullPartId = "hull_fighter_01",
            Slots = new[]
            {
                new SlotDefinition { SocketPattern = "wing_", PartCategory = PartCategory.Wing, MinCount = 2, MaxCount = 2 },
                new SlotDefinition { SocketPattern = "engine_", PartCategory = PartCategory.Engine, MinCount = 2, MaxCount = 2 },
            }
        };

        var partsDirectory = FindPartsDirectory();
        if (partsDirectory is null)
        {
            StatusText = "Dossier Assets/Parts introuvable.";
            return;
        }

        var library = PartLibrary.LoadFromDirectory(partsDirectory);
        if (library.Parts.Count == 0)
        {
            StatusText = $"Aucune pièce dans {partsDirectory}. Lance tools/PlaceholderPartGenerator pour générer des pièces de test.";
            return;
        }

        _assembler = new ShipAssembler(library);
        Regenerate();
    }

    private void Regenerate()
    {
        if (_assembler is null)
            return;

        Seed = Environment.TickCount;
        try
        {
            _currentShip = _assembler.Assemble(_template, Seed);
            ShipModel = BuildModel(_currentShip);
            StatusText = $"Vaisseau '{_template.Name}' généré (seed {Seed}, {_currentShip.Parts.Count} pièce(s)).";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;
        }
    }

    private void Export()
    {
        if (_currentShip is null)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = $"{_template.Name.ToLowerInvariant()}_{Seed}.glb",
            Filter = "glTF binaire (*.glb)|*.glb"
        };

        if (dialog.ShowDialog() == true)
        {
            ShipExporter.Export(_currentShip, dialog.FileName);
            StatusText = $"Exporté vers {dialog.FileName}";
        }
    }

    private static Model3DGroup BuildModel(ShipInstance ship)
    {
        var group = new Model3DGroup();
        foreach (var placed in ship.Parts)
            group.Children.Add(GltfMeshConverter.ToModel3DGroup(placed.Part, placed.WorldTransform));
        return group;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
