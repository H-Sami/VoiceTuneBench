namespace VoiceTuneBench.Core.Presets;

/// <summary>ReaComp-style compressor parameters used for the preview DSP.</summary>
public sealed record CompressorSettings(
    double ThresholdDb,
    double Ratio,
    double AttackMs,
    double ReleaseMs,
    double KneeDb,
    double MakeupGainDb,
    double PrecompMs = 0.0,
    bool ClassicAttack = false,
    bool AutoRelease = false,
    string DetectorInput = "Main Input",
    double DetectorLowpassHz = 20000.0,
    double DetectorHighpassHz = 80.0,
    double RmsSizeMs = 10.0,
    string AA = "4x",
    bool LimitOutput = true,
    bool AutoMakeup = false,
    bool PreviewFilter = false,
    double WetDb = -0.0,
    double DryDb = double.NegativeInfinity,
    string Label = "")
{
    public static readonly ISet<string> AAOptions = new HashSet<string>
    {
        "2x",
        "4x",
        "8x",
        "16x",
        "32x",
        "64x",
    };
}
