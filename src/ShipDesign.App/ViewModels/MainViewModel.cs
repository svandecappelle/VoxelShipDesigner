using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;
using ShipDesign.App.Rendering;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ShipParameters _parameters = new();
    private readonly Random _random = new();

    /// <summary>Long enough to swallow a slider drag, short enough that letting go feels immediate.</summary>
    private readonly DispatcherTimer _rebuildDebounce = new() { Interval = TimeSpan.FromMilliseconds(140) };

    /// <summary>
    /// Ceiling on the bounding volume a ship may occupy. Set from measurement rather than taste:
    /// the previous sliders' far corner already reached about 530k and took nine tenths of a
    /// second, so this leaves close to twice that headroom while stopping short of the multi-second,
    /// multi-gigabyte builds the widened ranges can otherwise ask for.
    /// </summary>
    private const long MaxBoundingVoxels = 1_000_000;

    private SharpGLTF.Schema2.ModelRoot? _currentModel;
    private Model3D? _shipModel;
    private string _statusText = "";
    private string _seedText;
    private string _designation = "";
    private string _massClass = "";
    private int _triangleCount;

    /// <summary>The live parameter set, for views that build their own geometry from it rather
    /// than reusing the main viewport's model -- the studio view needs the voxel grid, which the
    /// meshed model no longer carries.</summary>
    public ShipParameters Parameters => _parameters;

    public IReadOnlyList<HullClass> HullClasses { get; } = Enum.GetValues<HullClass>();
    public IReadOnlyList<HullShape> HullShapes { get; } = Enum.GetValues<HullShape>();
    public IReadOnlyList<WingStyle> WingStyles { get; } = Enum.GetValues<WingStyle>();
    public IReadOnlyList<EngineStyle> EngineStyles { get; } = Enum.GetValues<EngineStyle>();
    public IReadOnlyList<CockpitStyle> CockpitStyles { get; } = Enum.GetValues<CockpitStyle>();
    public IReadOnlyList<ShipSilhouette> Silhouettes { get; } = ShipSilhouette.All;
    public IReadOnlyList<HullArrangement> HullArrangements { get; } = Enum.GetValues<HullArrangement>();
    public IReadOnlyList<NacelleMount> NacelleMounts { get; } = Enum.GetValues<NacelleMount>();
    public IReadOnlyList<NacelleStyle> NacelleStyles { get; } = Enum.GetValues<NacelleStyle>();

    /// <summary>Swatches taken from the reference voxel-ship art style: a cool greyscale ramp for
    /// hull and plating, deep blue for markings, warm amber/gold for lit ports, and cyan for
    /// exhaust. Ordered light-to-dark then warm-to-cool so the row reads as a coherent palette.</summary>
    public IReadOnlyList<Color> PresetColors { get; } = new[]
    {
        Color.FromRgb(0xE4, 0xE9, 0xEC), Color.FromRgb(0xD2, 0xD9, 0xDE),
        Color.FromRgb(0x9B, 0xA6, 0xAE), Color.FromRgb(0x6E, 0x7B, 0x86),
        Color.FromRgb(0x4A, 0x55, 0x60), Color.FromRgb(0x22, 0x30, 0x3D),
        Color.FromRgb(0x2F, 0x66, 0xAD), Color.FromRgb(0x1B, 0x41, 0x74),
        Color.FromRgb(0xE8, 0x9A, 0x3C), Color.FromRgb(0xF5, 0xC5, 0x42),
        Color.FromRgb(0x62, 0xD0, 0xFA), Color.FromRgb(0x2B, 0x9E, 0xD6),
    };

    public HullClass HullClass { get => _parameters.HullClass; set { _parameters.HullClass = value; OnPropertyChanged(); Rebuild(); } }
    public HullShape HullShape { get => _parameters.HullShape; set { _parameters.HullShape = value; OnPropertyChanged(); Rebuild(); } }
    public HullShape SecondaryHullShape { get => _parameters.SecondaryHullShape; set { _parameters.SecondaryHullShape = value; OnPropertyChanged(); Rebuild(); } }

    public HullArrangement HullArrangement
    {
        get => _parameters.HullArrangement;
        set
        {
            _parameters.HullArrangement = value;
            OnPropertyChanged();
            // Half the hull panel applies to one arrangement and half to the other, so every
            // visibility flag has to be re-evaluated when it changes.
            OnPropertyChanged(nameof(HasSecondaryHulls));
            OnPropertyChanged(nameof(IsComposite));
            OnPropertyChanged(nameof(IsParallel));
            Rebuild();
        }
    }

    public bool IsComposite => _parameters.HullArrangement == HullArrangement.Composite;
    public bool IsParallel => _parameters.HullArrangement == HullArrangement.Parallel;

    public int HullCount
    {
        get => _parameters.HullCount;
        set
        {
            _parameters.HullCount = value;
            OnPropertyChanged();
            // The outrigger shape picker only applies to a trimaran, so its visibility follows
            // this property and has to be re-evaluated whenever the hull count changes.
            OnPropertyChanged(nameof(HasSecondaryHulls));
            Rebuild();
        }
    }

    /// <summary>Whether the ship has a second hull whose shape can differ from the main one. A
    /// catamaran is two copies of the primary hull, so only a trimaran qualifies -- but a composite
    /// ship always has one, since its engineering hull is a distinct volume.</summary>
    public bool HasSecondaryHulls => IsComposite || _parameters.HullCount >= 3;

    public float PrimaryHullFraction { get => _parameters.PrimaryHullFraction; set { _parameters.PrimaryHullFraction = value; OnPropertyChanged(); Rebuild(); } }
    public float SecondaryHullDrop { get => _parameters.SecondaryHullDrop; set { _parameters.SecondaryHullDrop = value; OnPropertyChanged(); Rebuild(); } }
    public float HullSpacing { get => _parameters.HullSpacing; set { _parameters.HullSpacing = value; OnPropertyChanged(); Rebuild(); } }
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
    public NacelleMount NacelleMount { get => _parameters.NacelleMount; set { _parameters.NacelleMount = value; OnPropertyChanged(); Rebuild(); } }
    public NacelleStyle NacelleStyle { get => _parameters.NacelleStyle; set { _parameters.NacelleStyle = value; OnPropertyChanged(); Rebuild(); } }
    public float PylonChord { get => _parameters.PylonChord; set { _parameters.PylonChord = value; OnPropertyChanged(); Rebuild(); } }
    public bool Deflector { get => _parameters.Deflector; set { _parameters.Deflector = value; OnPropertyChanged(); Rebuild(); } }
    public float NacelleWidth { get => _parameters.NacelleWidth; set { _parameters.NacelleWidth = value; OnPropertyChanged(); Rebuild(); } }
    public float NacelleLength { get => _parameters.NacelleLength; set { _parameters.NacelleLength = value; OnPropertyChanged(); Rebuild(); } }
    public float NacelleSpacing { get => _parameters.NacelleSpacing; set { _parameters.NacelleSpacing = value; OnPropertyChanged(); Rebuild(); } }
    public float NacelleRise { get => _parameters.NacelleRise; set { _parameters.NacelleRise = value; OnPropertyChanged(); Rebuild(); } }
    public float NacelleSweep { get => _parameters.NacelleSweep; set { _parameters.NacelleSweep = value; OnPropertyChanged(); Rebuild(); } }

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
    public ICommand ApplySilhouetteCommand { get; }
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
        ApplySilhouetteCommand = new RelayCommand(s => ApplySilhouette((ShipSilhouette)s!));
        SetHullColorCommand = new RelayCommand(c => HullColor = (Color)c!);
        SetAccentColorCommand = new RelayCommand(c => AccentColor = (Color)c!);
        SetEngineGlowColorCommand = new RelayCommand(c => EngineGlowColor = (Color)c!);
        SetCockpitTintColorCommand = new RelayCommand(c => CockpitTintColor = (Color)c!);
        ExportCommand = new RelayCommand(_ => Export(), _ => _currentModel is not null);

        _rebuildDebounce.Tick += (_, _) => { _rebuildDebounce.Stop(); RebuildNow(); };

        RebuildNow();
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

    /// <summary>Applies a named silhouette and refreshes the whole panel, since it moves most of
    /// the parameters at once.</summary>
    private void ApplySilhouette(ShipSilhouette silhouette)
    {
        silhouette.ApplyTo(_parameters);
        OnPropertyChanged(string.Empty);
        StatusText = $"Silhouette « {silhouette.Name} » appliquée — {silhouette.Summary}.";
        Rebuild();
    }

    private void Randomize()
    {
        _parameters.HullClass = HullClasses[_random.Next(HullClasses.Count)];

        // The composite arrangement is a distinctive read, so it turns up sometimes rather than
        // half the time -- but it does turn up. Leaving it out entirely, which this method did when
        // the arrangement was added, made a whole half of the generator unreachable by the one
        // button most likely to be pressed first.
        _parameters.HullArrangement = _random.NextDouble() < 0.25
            ? HullArrangement.Composite
            : HullArrangement.Parallel;
        _parameters.PrimaryHullFraction = 0.32f + (float)_random.NextDouble() * 0.26f;
        _parameters.SecondaryHullDrop = 0.9f + (float)_random.NextDouble() * 1.6f;
        _parameters.Deflector = _random.NextDouble() > 0.2;

        // Single hull most of the time: catamarans and trimarans are a distinctive silhouette,
        // and making them as common as the conventional layout would dilute that.
        _parameters.HullCount = _random.NextDouble() switch { < 0.65 => 1, < 0.85 => 2, _ => 3 };
        _parameters.HullSpacing = 0.6f + (float)_random.NextDouble() * 1.1f;

        _parameters.HullShape = HullShapes[_random.Next(HullShapes.Count)];
        _parameters.SecondaryHullShape = HullShapes[_random.Next(HullShapes.Count)];
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
        // Drawn independently, so random ships get stubby and elongated pods rather than only
        // uniformly-scaled ones -- the reason the knob was split in the first place.
        _parameters.NacelleWidth = 0.6f + (float)_random.NextDouble() * 0.9f;
        _parameters.NacelleLength = 0.6f + (float)_random.NextDouble() * 0.9f;
        _parameters.NacelleSpacing = 0.5f + (float)_random.NextDouble() * 1.3f;
        // Straddle both mounting idioms: slung underneath, or raised and swept back.
        _parameters.NacelleRise = -1.2f + (float)_random.NextDouble() * 2.6f;
        _parameters.NacelleSweep = (float)_random.NextDouble() * 0.4f;
        _parameters.NacelleMount = NacelleMounts[_random.Next(NacelleMounts.Count)];
        _parameters.NacelleStyle = _random.NextDouble() < 0.4 ? NacelleStyle.Warp : NacelleStyle.Thruster;
        _parameters.PylonChord = 0.6f + (float)_random.NextDouble() * 2.4f;

        // Composite ships get their shape choices narrowed, last so nothing above overrides them.
        // Two hulls drawn independently from the full set would as often as not hang a slab under a
        // slab, which is not a composite ship so much as two ships sharing a neck; and a saucer
        // ship with wings on it stops reading as either thing.
        if (_parameters.HullArrangement == HullArrangement.Composite)
        {
            _parameters.HullShape = _random.NextDouble() < 0.7 ? HullShape.Saucer : HullShape.Hammerhead;
            _parameters.SecondaryHullShape = _random.NextDouble() < 0.6 ? HullShape.Spindle : HullShape.Dart;
            _parameters.WingStyle = WingStyle.None;
            _parameters.Nacelles = true;
            _parameters.NacelleMount = NacelleMount.Secondary;
            _parameters.NacelleStyle = NacelleStyle.Warp;
            _parameters.NacelleRise = 0.8f + (float)_random.NextDouble() * 1.6f;
            _parameters.PylonChord = 2f + (float)_random.NextDouble() * 2.5f;

            // A composite ship is two hulls deep, so the same beam that suits one hull makes a
            // gaunt pair. Kept off the bottom of the range rather than widened outright, which
            // would push the far corner of length x beam past what the budget allows.
            _parameters.Beam = MathF.Max(_parameters.Beam, 4f);
        }

        // The far corner of length x beam is over budget, and a composite ship stacks two hulls so
        // it gets there sooner. Shrunk until it fits rather than drawn from ranges narrow enough to
        // be safe everywhere: the button's whole job is to produce a ship, and one that lands on
        // "gabarit trop grand" has produced nothing at all.
        while (VoxelShipGrower.EstimateBoundingVoxels(_parameters) > MaxBoundingVoxels
               && _parameters.Length > 5f)
        {
            _parameters.Length *= 0.9f;
            _parameters.Beam *= 0.93f;
        }

        _parameters.Seed = _random.Next(1000, 9999);
        // Ranges keep a random ship inside the reference art style rather than letting it land on
        // any hue at any lightness: a pale desaturated hull, a saturated mid-dark accent that
        // still reads as a marking, a bright exhaust, and near-black canopy glass.
        _parameters.HullColor = ShipColor.RandomHsl(_random, 4, 18, 72, 90);
        _parameters.AccentColor = ShipColor.RandomHsl(_random, 45, 75, 32, 52);
        _parameters.EngineGlowColor = ShipColor.RandomHsl(_random, 60, 95, 58, 76);
        _parameters.CockpitTintColor = ShipColor.RandomHsl(_random, 15, 40, 12, 24);

        SeedText = _parameters.Seed.ToString();
        OnPropertyChanged(string.Empty); // refresh every bound property at once
        Rebuild();
    }

    /// <summary>
    /// Asks for a rebuild soon rather than now. A slider raises its value on every mouse move, and
    /// generation is a whole-volume rebuild that runs to a second or more at the wide end of the
    /// ranges -- rebuilding per tick would mean dragging a slider queues dozens of them and the
    /// window stops responding. Waiting for the drag to settle gives one rebuild per gesture.
    /// </summary>
    private void Rebuild()
    {
        _rebuildDebounce.Stop();
        _rebuildDebounce.Start();
    }

    private void RebuildNow()
    {
        // Length and beam multiply, so the far corner of the two is far more expensive than either
        // slider suggests on its own -- enough to spend seconds and gigabytes. Refused rather than
        // silently clamped: a slider that moves while the ship ignores it is worse than being told
        // why nothing happened, and the previous ship stays on screen meanwhile.
        var estimate = VoxelShipGrower.EstimateBoundingVoxels(_parameters);
        if (estimate > MaxBoundingVoxels)
        {
            StatusText = $"Gabarit trop grand ({estimate / 1000:N0}k voxels d'encombrement, " +
                         $"maximum {MaxBoundingVoxels / 1000:N0}k) — réduire la longueur ou le maître-bau.";
            return;
        }

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
