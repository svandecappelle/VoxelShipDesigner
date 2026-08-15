using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
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
    private sealed class PartOverride
    {
        public Part? ReplacementPart;
        public float Scale = 1f;
    }

    private readonly ShipAssembler? _assembler;
    private readonly PartLibrary? _library;
    private readonly Random _random = new();
    private readonly Dictionary<int, PartOverride> _overrides = new();

    private ShipInstance? _baseShip;
    private ShipInstance? _currentShip;

    private ShipTemplate _selectedTemplate;
    private Model3D? _shipModel;
    private string _statusText = "";
    private int _seed;
    private string _seedInput = "";
    private PartListEntry? _selectedPartEntry;
    private float _selectedPartScale = 1f;

    public IReadOnlyList<ShipTemplate> AvailableTemplates { get; } = ShipTemplateCatalog.All;

    public ShipTemplate SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (ReferenceEquals(_selectedTemplate, value))
                return;
            _selectedTemplate = value;
            OnPropertyChanged();
            Regenerate();
        }
    }

    public Model3D? ShipModel { get => _shipModel; private set { _shipModel = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    public int Seed { get => _seed; private set { _seed = value; OnPropertyChanged(); SeedInput = value.ToString(); } }
    public string SeedInput { get => _seedInput; set { _seedInput = value; OnPropertyChanged(); } }
    public IReadOnlyList<PartListEntry> PartsSummary { get; private set; } = Array.Empty<PartListEntry>();

    public PartListEntry? SelectedPartEntry
    {
        get => _selectedPartEntry;
        set
        {
            _selectedPartEntry = value;
            OnPropertyChanged();
            SelectedPartScale = value is null ? 1f : GetOverrideScale(value.Index);
        }
    }

    public float SelectedPartScale
    {
        get => _selectedPartScale;
        set
        {
            if (Math.Abs(_selectedPartScale - value) < 0.001f)
                return;
            _selectedPartScale = value;
            OnPropertyChanged();
            SetSelectedPartScale(value);
        }
    }

    public ICommand RegenerateCommand { get; }
    public ICommand GenerateWithSeedCommand { get; }
    public ICommand ReplacePartCommand { get; }
    public ICommand ExportCommand { get; }

    public MainViewModel()
    {
        RegenerateCommand = new RelayCommand(_ => Regenerate(), _ => _assembler is not null);
        GenerateWithSeedCommand = new RelayCommand(_ => GenerateWithSeed(), _ => _assembler is not null);
        ReplacePartCommand = new RelayCommand(_ => ReplaceSelectedPart(), _ => _selectedPartEntry is not null && _library is not null);
        ExportCommand = new RelayCommand(_ => Export(), _ => _currentShip is not null);

        _selectedTemplate = AvailableTemplates[0];

        var partsDirectory = FindPartsDirectory();
        if (partsDirectory is null)
        {
            StatusText = "Dossier Assets/Parts introuvable.";
            return;
        }

        _library = PartLibrary.LoadFromDirectory(partsDirectory);
        if (_library.Parts.Count == 0)
        {
            StatusText = $"Aucune pièce dans {partsDirectory}. Lance tools/PlaceholderPartGenerator pour générer des pièces de test.";
            return;
        }

        _assembler = new ShipAssembler(_library);
        Regenerate();
    }

    private void Regenerate() => Assemble(Environment.TickCount);

    private void GenerateWithSeed()
    {
        if (int.TryParse(SeedInput, out var seed))
            Assemble(seed);
        else
            StatusText = $"Seed invalide : '{SeedInput}'";
    }

    private void Assemble(int seed)
    {
        if (_assembler is null)
            return;

        Seed = seed;
        _overrides.Clear();
        SelectedPartEntry = null;

        try
        {
            _baseShip = _assembler.Assemble(SelectedTemplate, seed);
            _currentShip = _baseShip;
            ShipModel = BuildModel(_currentShip);
            RefreshPartsSummary();
            StatusText = $"Vaisseau '{SelectedTemplate.Name}' généré (seed {seed}, {_currentShip.Parts.Count} pièce(s)).";
        }
        catch (InvalidOperationException ex)
        {
            StatusText = ex.Message;
        }
    }

    private void ReplaceSelectedPart()
    {
        if (_selectedPartEntry is null || _library is null || _baseShip is null)
            return;

        var index = _selectedPartEntry.Index;
        var category = _baseShip.Parts[index].Part.Category;
        var currentPartId = GetOverride(index).ReplacementPart?.Id ?? _baseShip.Parts[index].Part.Id;

        var candidates = _library.ByCategory(category).Where(p => p.Id != currentPartId).ToList();
        if (candidates.Count == 0)
        {
            StatusText = $"Aucune autre pièce disponible pour la catégorie {category}.";
            return;
        }

        var chosen = candidates[_random.Next(candidates.Count)];
        GetOverride(index).ReplacementPart = chosen;

        RebuildEffectiveShip();
        StatusText = $"Pièce {index} remplacée par {chosen.Id}.";
    }

    private void SetSelectedPartScale(float scale)
    {
        if (_selectedPartEntry is null)
            return;

        GetOverride(_selectedPartEntry.Index).Scale = scale;
        RebuildEffectiveShip();
    }

    private PartOverride GetOverride(int index)
    {
        if (!_overrides.TryGetValue(index, out var over))
        {
            over = new PartOverride();
            _overrides[index] = over;
        }
        return over;
    }

    private float GetOverrideScale(int index) =>
        _overrides.TryGetValue(index, out var over) ? over.Scale : 1f;

    private void RebuildEffectiveShip()
    {
        if (_baseShip is null)
            return;

        var parts = new List<PlacedPart>(_baseShip.Parts.Count);
        for (var i = 0; i < _baseShip.Parts.Count; i++)
        {
            var basePlaced = _baseShip.Parts[i];
            _overrides.TryGetValue(i, out var over);
            var part = over?.ReplacementPart ?? basePlaced.Part;
            var scale = over?.Scale ?? 1f;
            var transform = Math.Abs(scale - 1f) < 0.001f
                ? basePlaced.WorldTransform
                : Matrix4x4.CreateScale(scale) * basePlaced.WorldTransform;

            parts.Add(new PlacedPart { Part = part, WorldTransform = transform });
        }

        _currentShip = new ShipInstance { TemplateName = _baseShip.TemplateName, Parts = parts };
        ShipModel = BuildModel(_currentShip);
        RefreshPartsSummary();
    }

    private void RefreshPartsSummary()
    {
        var previousIndex = _selectedPartEntry?.Index;

        if (_currentShip is null)
        {
            PartsSummary = Array.Empty<PartListEntry>();
        }
        else
        {
            PartsSummary = _currentShip.Parts.Select((placed, index) =>
            {
                var edited = _overrides.TryGetValue(index, out var over)
                    && (over.ReplacementPart is not null || Math.Abs(over.Scale - 1f) > 0.001f);
                var text = $"{placed.Part.Category} — {placed.Part.Id}" + (edited ? " (modifié)" : "");
                return new PartListEntry { Index = index, Text = text };
            }).ToList();
        }

        OnPropertyChanged(nameof(PartsSummary));

        // Rebuilding PartsSummary creates new PartListEntry instances, which would otherwise
        // silently drop the ListBox's SelectedItem (and with it, the scale slider's display).
        if (previousIndex is int index)
            SelectedPartEntry = PartsSummary.FirstOrDefault(e => e.Index == index);
    }

    private void Export()
    {
        if (_currentShip is null)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = $"{SelectedTemplate.Name.ToLowerInvariant()}_{Seed}.glb",
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
