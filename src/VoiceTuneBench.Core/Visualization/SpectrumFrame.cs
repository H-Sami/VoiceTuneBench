namespace VoiceTuneBench.Core.Visualization;

public sealed record SpectrumFrame(
    double[] Frequencies,
    double[] LevelsDb);
