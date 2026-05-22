using VoiceTuneBench.Core.Presets;

namespace VoiceTuneBench.Core.Tests;

public sealed class PresetLibraryTests
{
    [Fact]
    public void CuratedPresetsAreSourceAttributedAndSpeechSafe()
    {
        var presets = PresetLibrary.GetCuratedPresets();
        Assert.Equal(6, presets.Count);
        Assert.Equal(presets.Count, presets.Select(preset => preset.Slug).Distinct().Count());

        foreach (var preset in presets)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
            Assert.False(string.IsNullOrWhiteSpace(preset.BestFor));
            Assert.NotEmpty(preset.SourceUrls);
            Assert.True(preset.EqBands.Count <= 6);

            foreach (var band in preset.EqBands)
            {
                Assert.Contains(band.BandType, EqBandTypes.PresetFilterTypes);
                Assert.InRange(band.Frequency, 20.0, 20000.0);
                Assert.InRange(band.Q, 0.2, 10.0);

                if (band.BandType == EqBandType.Band)
                {
                    Assert.True(band.Frequency < 9000.0);
                    Assert.InRange(band.GainDb, -4.0, 3.0);
                }

                if (band.BandType == EqBandType.HighShelf)
                {
                    Assert.True(band.Frequency >= 8000.0);
                    Assert.True(band.GainDb <= 1.0);
                }

                if (band.BandType == EqBandType.LowPass)
                {
                    Assert.True(band.Frequency >= 12000.0);
                }
            }

            var comp = preset.Compressor;
            Assert.InRange(comp.Ratio, 2.0, 4.0);
            Assert.InRange(comp.AttackMs, 8.0, 25.0);
            Assert.InRange(comp.ReleaseMs, 100.0, 200.0);
            Assert.Contains(comp.AA, CompressorSettings.AAOptions);
            Assert.False(comp.AutoMakeup);
            Assert.True(comp.LimitOutput);
        }
    }
}
