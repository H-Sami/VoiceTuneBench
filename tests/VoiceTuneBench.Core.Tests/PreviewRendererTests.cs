using VoiceTuneBench.Core.Audio;
using VoiceTuneBench.Core.DSP;
using VoiceTuneBench.Core.Presets;

namespace VoiceTuneBench.Core.Tests;

public sealed class PreviewRendererTests
{
    [Fact]
    public void PreviewRenderIsFiniteBoundedAndLengthLimited()
    {
        var preset = PresetLibrary.GetCuratedPresets()[0];
        var sampleRate = VoiceSampleLoader.SampleRate;
        var audio = MakeTone(sampleRate, seconds: 45.5, frequency: 180.0, amplitude: 0.08f);

        var preview = PreviewRenderer.RenderPresetPreview(audio, sampleRate, preset);

        Assert.True(preview.Length <= (int)(VoiceSampleLoader.PreviewMaxSeconds * sampleRate));
        Assert.All(preview, sample => Assert.True(float.IsFinite(sample)));
        Assert.True(preview.Max(sample => Math.Abs(sample)) <= 1.0f);
        Assert.True(preview.Max(sample => Math.Abs(sample)) > 0.0f);
    }

    [Fact]
    public void EqPreviewChangesExpectedSpeechRegions()
    {
        var preset = PresetLibrary.GetCuratedPresets()[0];
        var sampleRate = VoiceSampleLoader.SampleRate;
        var audio = MixTones(sampleRate, seconds: 1.0, (40.0, 0.05f), (3200.0, 0.05f));

        var processed = PreviewRenderer.ApplyEqPreview(audio, sampleRate, preset.EqBands);

        Assert.True(ToneMagnitude(processed, sampleRate, 40.0) < ToneMagnitude(audio, sampleRate, 40.0) * 0.8);
        Assert.True(ToneMagnitude(processed, sampleRate, 3200.0) > ToneMagnitude(audio, sampleRate, 3200.0));
    }

    [Fact]
    public void CompressorPreviewHandlesHotAudioWithoutClipping()
    {
        var preset = PresetLibrary.GetCuratedPresets()[4];
        var sampleRate = VoiceSampleLoader.SampleRate;
        var audio = MakeTone(sampleRate, seconds: 1.0, frequency: 180.0, amplitude: 0.7f);

        var processed = PreviewRenderer.ApplyCompressorPreview(audio, sampleRate, preset.Compressor);

        Assert.All(processed, sample => Assert.True(float.IsFinite(sample)));
        Assert.True(processed.Max(sample => Math.Abs(sample)) <= 0.991f);
    }

    private static float[] MakeTone(int sampleRate, double seconds, double frequency, float amplitude)
    {
        return MixTones(sampleRate, seconds, (frequency, amplitude));
    }

    private static float[] MixTones(int sampleRate, double seconds, params (double Frequency, float Amplitude)[] tones)
    {
        var audio = new float[(int)Math.Round(sampleRate * seconds)];
        for (var index = 0; index < audio.Length; index++)
        {
            var t = index / (double)sampleRate;
            var sample = 0.0;
            foreach (var tone in tones)
            {
                sample += tone.Amplitude * Math.Sin(2.0 * Math.PI * tone.Frequency * t);
            }

            audio[index] = (float)sample;
        }

        return audio;
    }

    private static double ToneMagnitude(float[] audio, int sampleRate, double frequency)
    {
        var real = 0.0;
        var imaginary = 0.0;
        for (var index = 0; index < audio.Length; index++)
        {
            var angle = 2.0 * Math.PI * frequency * index / sampleRate;
            real += audio[index] * Math.Cos(angle);
            imaginary -= audio[index] * Math.Sin(angle);
        }

        return Math.Sqrt(real * real + imaginary * imaginary);
    }
}
