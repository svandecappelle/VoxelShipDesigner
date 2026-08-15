namespace ShipDesign.Core.Procedural;

/// <summary>All the knobs the procedural ship builder reads. Mutable, plain data — the UI
/// layer owns one instance and rebuilds the ship whenever a field changes.</summary>
public sealed class ShipParameters
{
    public HullClass HullClass { get; set; } = HullClass.Fighter;
    public float Length { get; set; } = 14f;
    public float Beam { get; set; } = 3.2f;
    public float Taper { get; set; } = 0.5f;
    public int Decks { get; set; } = 2;

    public WingStyle WingStyle { get; set; } = WingStyle.Swept;
    public float WingSpan { get; set; } = 6f;
    public float WingSweepDegrees { get; set; } = 30f;

    public int EngineCount { get; set; } = 2;
    public EngineStyle EngineStyle { get; set; } = EngineStyle.Standard;

    public CockpitStyle CockpitStyle { get; set; } = CockpitStyle.Bubble;
    public float CockpitSize { get; set; } = 1f;

    public bool Greebles { get; set; } = true;
    public float GreebleDensity { get; set; } = 0.5f;
    public int TurretCount { get; set; } = 4;
    public int Seed { get; set; } = 2291;

    public bool Superstructure { get; set; } = true;
    public float SuperstructureSize { get; set; } = 1f;

    public bool Nacelles { get; set; } = true;
    public float NacelleSize { get; set; } = 1f;

    public ShipColor HullColor { get; set; } = ShipColor.HullDefault;
    public ShipColor AccentColor { get; set; } = ShipColor.AccentDefault;
    public ShipColor EngineGlowColor { get; set; } = ShipColor.EngineGlowDefault;
    public ShipColor CockpitTintColor { get; set; } = ShipColor.CockpitTintDefault;
}
