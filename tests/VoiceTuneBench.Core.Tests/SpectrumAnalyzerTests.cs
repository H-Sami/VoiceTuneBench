using VoiceTuneBench.Core.Audio;
using VoiceTuneBench.Core.Visualization;

namespace VoiceTuneBench.Core.Tests;

public sealed class SpectrumAnalyzerTests
{
    [Fact]
    public void SpectrumAnalyzerFindsDominantSpeechTone()
    {
        var sampleRate = VoiceSampleLoader.SampleRate;
        var audio = new float[8192];
        for (var index = 0; index < audio.Length; index++)
        {
            audio[index] = (float)(0.1 * Math.Sin(2.0 * Math.PI * 1000.0 * index / sampleRate));
        }

        var spectrum = SpectrumAnalyzer.ComputeSpectrum(audio, sampleRate);
        var maxIndex = Array.IndexOf(spectrum.LevelsDb, spectrum.LevelsDb.Max());

        Assert.NotEmpty(spectrum.Frequencies);
        Assert.InRange(spectrum.Frequencies[maxIndex], 940.0, 1060.0);
    }
}
