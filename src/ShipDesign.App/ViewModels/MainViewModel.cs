using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using ShipDesign.App.Rendering;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ShipParameters _parameters = new();
    private readonly Random _random = new();

    private SharpGLTF.Schema2.ModelRoot? _currentModel;
    private Model3D? _shipModel;
    private string _statusText = "";
    private string _seedText;
    private string _designation = "";
    private string _massClass = "";
    private int _triangleCount;

    public IReadOnlyList<HullClass> HullClasses { get; } = Enum.GetValues<HullClass>();
    public IReadOnlyList<WingStyle> WingStyles { get; } = Enum.GetValues<WingStyle>();
    public IReadOnlyList<EngineStyle> EngineStyles { get; } = Enum.GetValues<EngineStyle>();
    public IReadOnlyList<CockpitStyle> CockpitStyles { get; } = Enum.GetValues<CockpitStyle>();

    public IReadOnlyList<Color> PresetColors { get; } = new[]
    {
        Color.FromRgb(0xC7, 0xD4, 0xDD), Color.FromRgb(0xF2, 0xA6, 0x5C),
        Color.FromRgb(0x7F, 0xE0, 0xE8), Color.FromRgb(0x5F, 0xB8, 0xD6),
        Color.FromRgb(0xE0, 0x5D, 0x5D), Color.FromRgb(0x6B, 0xC9, 0x6B),
        Color.FromRgb(0xD8, 0xC4, 0x6A), Color.FromRgb(0x8C, 0x8C, 0x9A),
    };

    public HullClass HullClass { get => _parameters.HullClass; set { _parameters.HullClass = value; OnPropertyChanged(); Rebuild(); } }
    public float Length { get => _parameters.Length; set { _parameters.Length = value; OnPropertyChanged(); Rebuild(); } }
    public float Beam { get => _parameters.Beam; set { _parameters.Beam = value; OnPropertyChanged(); Rebuild(); } }
    public float Taper { get => _parameters.Taper; set { _parameters.Taper = value; OnPropertyChanged(); Rebuild(); } }
    public int Decks { get => _parameters.Decks; set { _parameters.Decks = value; OnPropertyChanged(); Rebuild(); } }

    public WingStyle WingStyle { get => _parameters.WingStyle; set { _parameters.WingStyle = value; OnPropertyChanged(); Rebuild(); } }
    public float WingSpan { get => _parameters.WingSpan; set { _parameters.WingSpan = value; OnPropertyChanged(); Rebuild(); } }
    public float WingSweepDegrees { get => _parameters.WingSweepDegrees; set { _parameters.WingSweepDegrees = value; OnPropertyChanged(); Rebuild(); } }

    public int EngineCount { get => _parameters.EngineCount; set { _parameters.EngineCount = value; OnPropertyChanged(); Rebuild(); } }
    public EngineStyle EngineStyle { get => _parameters.EngineStyle; set { _parameters.EngineStyle = value; OnPropertyChanged(); Rebuild(); } }

    public CockpitStyle CockpitStyle { get => _parameters.CockpitStyle; set { _parameters.CockpitStyle = value; OnPropertyChanged(); Rebuild(); } }
    public float CockpitSize { get => _parameters.CockpitSize; set { _parameters.CockpitSize = value; OnPropertyChanged(); Rebuild(); } }

    public bool Greebles { get => _parameters.Greebles; set { _parameters.Greebles = value; OnPropertyChanged(); Rebuild(); } }
    public float GreebleDensity { get => _parameters.GreebleDensity; set { _parameters.GreebleDensity = value; OnPropertyChanged(); Rebuild(); } }
    public int TurretCount { get => _parameters.TurretCount; set { _parameters.TurretCount = value; OnPropertyChanged(); Rebuild(); } }

    public bool Superstructure { get => _parameters.Superstructure; set { _parameters.Superstructure = value; OnPropertyChanged(); Rebuild(); } }
    public float SuperstructureSize { get => _parameters.SuperstructureSize; set { _parameters.SuperstructureSize = value; OnPropertyChanged(); Rebuild(); } }

    public bool Nacelles { get => _parameters.Nacelles; set { _parameters.Nacelles = value; OnPropertyChanged(); Rebuild(); } }
    public float NacelleSize { get => _parameters.NacelleSize; set { _parameters.NacelleSize = value; OnPropertyChanged(); Rebuild(); } }

    public string SeedText { get => _seedText; set { _seedText = value; OnPropertyChanged(); } }

    public Color HullColor { get => ToWpf(_parameters.HullColor); set { _parameters.HullColor = ToShip(value); OnPropertyChanged(); Rebuild(); } }
    public Color AccentColor { get => ToWpf(_parameters.AccentColor); set { _parameters.AccentColor = ToShip(value); OnPropertyChanged(); Rebuild(); } }
    public Color EngineGlowColor { get => ToWpf(_parameters.EngineGlowColor); set { _parameters.EngineGlowColor = ToShip(value); OnPropertyChanged(); Rebuild(); } }
    public Color CockpitTintColor { get => ToWpf(_parameters.CockpitTintColor); set { _parameters.CockpitTintColor = ToShip(value); OnPropertyChanged(); Rebuild(); } }

    public Model3D? ShipModel { get => _shipModel; private set { _shipModel = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    public string Designation { get => _designation; private set { _designation = value; OnPropertyChanged(); } }
    public string MassClass { get => _massClass; private set { _massClass = value; OnPropertyChanged(); } }
    public int TriangleCount { get => _triangleCount; private set { _triangleCount = value; OnPropertyChanged(); } }

    public ICommand ApplySeedCommand { get; }
    public ICommand RerollSeedCommand { get; }
    public ICommand RandomizeShipCommand { get; }
    public ICommand SetHullColorCommand { get; }
    public ICommand SetAccentColorCommand { get; }
    public ICommand SetEngineGlowColorCommand { get; }
    public ICommand SetCockpitTintColorCommand { get; }
    public ICommand ExportCommand { get; }

    public MainViewModel()
    {
        _seedText = _parameters.Seed.ToString();

        ApplySeedCommand = new RelayCommand(_ => ApplySeed());
        RerollSeedCommand = new RelayCommand(_ => RerollSeed());
        RandomizeShipCommand = new RelayCommand(_ => Randomize());
        SetHullColorCommand = new RelayCommand(c => HullColor = (Color)c!);
        SetAccentColorCommand = new RelayCommand(c => AccentColor = (Color)c!);
        SetEngineGlowColorCommand = new RelayCommand(c => EngineGlowColor = (Color)c!);
        SetCockpitTintColorCommand = new RelayCommand(c => CockpitTintColor = (Color)c!);
        ExportCommand = new RelayCommand(_ => Export(), _ => _currentModel is not null);

        Rebuild();
    }

    private void ApplySeed()
    {
        if (int.TryParse(SeedText, out var seed))
        {
            _parameters.Seed = seed;
            Rebuild();
        }
        else
        {
            StatusText = $"Seed invalide : '{SeedText}'";
        }
    }

    private void RerollSeed()
    {
        _parameters.Seed = _random.Next(1000, 9999);
        SeedText = _parameters.Seed.ToString();
        Rebuild();
    }

    private void Randomize()
    {
        _parameters.HullClass = HullClasses[_random.Next(HullClasses.Count)];
        _parameters.Length = 6f + (float)_random.NextDouble() * 30f;
        _parameters.Beam = 1f + (float)_random.NextDouble() * 7f;
        _parameters.Taper = (float)_random.NextDouble();
        _parameters.Decks = 1 + _random.Next(4);
        _parameters.WingStyle = WingStyles[_random.Next(WingStyles.Count)];
        _parameters.WingSpan = 2f + (float)_random.NextDouble() * 10f;
        _parameters.WingSweepDegrees = _random.Next(61);
        _parameters.EngineCount = 1 + _random.Next(4);
        _parameters.EngineStyle = EngineStyles[_random.Next(EngineStyles.Count)];
        _parameters.CockpitStyle = CockpitStyles[_random.Next(CockpitStyles.Count)];
        _parameters.CockpitSize = 0.6f + (float)_random.NextDouble();
        _parameters.Greebles = _random.NextDouble() > 0.15;
        _parameters.GreebleDensity = (float)_random.NextDouble();
        _parameters.TurretCount = _random.Next(9);
        _parameters.Superstructure = _random.NextDouble() > 0.2;
        _parameters.SuperstructureSize = 0.7f + (float)_random.NextDouble() * 0.8f;
        _parameters.Nacelles = _random.NextDouble() > 0.35;
        _parameters.NacelleSize = 0.6f + (float)_random.NextDouble() * 0.9f;
        _parameters.Seed = _random.Next(1000, 9999);
        _parameters.HullColor = ShipColor.RandomHsl(_random, 5, 25, 55, 85);
        _parameters.AccentColor = ShipColor.RandomHsl(_random, 30, 60, 50, 70);
        _parameters.EngineGlowColor = ShipColor.RandomHsl(_random, 40, 80, 55, 75);
        _parameters.CockpitTintColor = ShipColor.RandomHsl(_random, 30, 60, 45, 70);

        SeedText = _parameters.Seed.ToString();
        OnPropertyChanged(string.Empty); // refresh every bound property at once
        Rebuild();
    }

    private void Rebuild()
    {
        try
        {
            _currentModel = ProceduralShipBuilder.Build(_parameters);
            ShipModel = GltfMeshConverter.ToModel3DGroup(_currentModel);
            Designation = ProceduralShipBuilder.Designation(_parameters);
            MassClass = ProceduralShipBuilder.MassClass(_parameters);
            TriangleCount = ProceduralShipBuilder.CountTriangles(_currentModel);
            StatusText = $"Vaisseau '{Designation}' généré ({TriangleCount} triangles).";
        }
        catch (Exception ex)
        {
            StatusText = $"Erreur de génération : {ex.Message}";
        }
    }

    private void Export()
    {
        if (_currentModel is null)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = $"{Designation.ToLowerInvariant()}.glb",
            Filter = "glTF binaire (*.glb)|*.glb"
        };

        if (dialog.ShowDialog() == true)
        {
            _currentModel.SaveGLB(dialog.FileName);
            StatusText = $"Exporté vers {dialog.FileName}";
        }
    }

    private static Color ToWpf(ShipColor c) => Color.FromRgb(
        (byte)(Math.Clamp(c.R, 0f, 1f) * 255),
        (byte)(Math.Clamp(c.G, 0f, 1f) * 255),
        (byte)(Math.Clamp(c.B, 0f, 1f) * 255));

    private static ShipColor ToShip(Color c) => ShipColor.FromBytes(c.R, c.G, c.B);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
