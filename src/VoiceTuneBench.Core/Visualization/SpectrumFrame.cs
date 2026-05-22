namespace VoiceTuneBench.Core.Visualization;

/// <summary>Frequency bin centers and their dB magnitudes from an FFT.</summary>
/// <param name="Frequencies">Center frequency of each FFT bin in Hz.</param>
/// <param name="LevelsDb">Magnitude in dB for each frequency bin.</param>
public sealed record SpectrumFrame(
    double[] Frequencies,
    double[] LevelsDb);
