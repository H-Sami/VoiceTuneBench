namespace VoiceTuneBench.Core.Presets;

public enum EqBandType
{
    LowPass = 0,
    HighPass = 1,
    BandPass = 2,
    LowShelf = 3,
    HighShelf = 4,
    Band = 5,
    Notch = 6,
}

public sealed record EqBand(
    EqBandType BandType,
    double Frequency,
    double GainDb,
    double Q,
    bool Enabled = true,
    string Label = "")
{
    public string FilterName => BandType switch
    {
        EqBandType.LowShelf => "Low Shelf",
        EqBandType.HighShelf => "High Shelf",
        EqBandType.Band => "Band",
        EqBandType.LowPass => "Low Pass",
        EqBandType.HighPass => "High Pass",
        EqBandType.Notch => "Notch",
        EqBandType.BandPass => "Band Pass",
        _ => "Unknown",
    };
}

public static class EqBandTypes
{
    public static readonly ISet<EqBandType> PresetFilterTypes = new HashSet<EqBandType>
    {
        EqBandType.LowShelf,
        EqBandType.HighShelf,
        EqBandType.Band,
        EqBandType.LowPass,
        EqBandType.HighPass,
    };
}
