namespace VoiceTuneBench.Core.Presets;

public sealed record CuratedPreset(
    string Slug,
    string DisplayName,
    string Summary,
    string BestFor,
    IReadOnlyList<EqBand> EqBands,
    CompressorSettings Compressor,
    IReadOnlyList<string> SourceNames)
{
    public IReadOnlyList<string> SourceUrls =>
        SourceNames.Select(name => PresetLibrary.SourceLinks[name]).ToArray();
}
