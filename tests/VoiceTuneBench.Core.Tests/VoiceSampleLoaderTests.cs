using VoiceTuneBench.Core.Audio;

namespace VoiceTuneBench.Core.Tests;

public sealed class VoiceSampleLoaderTests
{
    [Fact]
    public void VoiceSampleLoaderDownmixesResamplesAndCapsDuration()
    {
        var sourceRate = 44100;
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "voice.wav");
        WriteStereoPcm16Wave(path, sourceRate, seconds: 2.0);

        var loaded = VoiceSampleLoader.LoadVoiceSample(path, maxDurationSeconds: 0.75);

        Assert.Equal(VoiceSampleLoader.SampleRate, loaded.SampleRate);
        Assert.InRange(loaded.DurationSeconds, 0.70, 0.80);
        Assert.True(loaded.Audio.Length > VoiceSampleLoader.SampleRate * 0.70);
        Assert.True(loaded.Audio.Max(sample => Math.Abs(sample)) <= 1.0f);
        Assert.Equal("WAV", loaded.Decoder);
    }

    [Fact]
    public void VoiceSampleLoaderResampledToneMaintainsFrequency()
    {
        var sourceRate = 44100;
        var targetRate = 48000;
        var frequency = 1000.0;
        var samples = new float[(int)(sourceRate * 0.5)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(2.0 * Math.PI * frequency * i / sourceRate);
        }

        var resampled = VoiceSampleLoader.PrepareDecodedAudio(
            samples, sourceRate, channels: 1, decoder: "test", targetSampleRate: targetRate);

        Assert.Equal(targetRate, resampled.SampleRate);
        var mag = ToneMagnitude(resampled.Audio, targetRate, frequency);
        Assert.True(mag > 0.01, "Resampled tone should retain its original frequency content");
    }

    [Fact]
    public void VoiceSampleLoaderRejectsSilence()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "silence.wav");
        WriteSilenceWave(path, VoiceSampleLoader.SampleRate, seconds: 1.0);

        var error = Assert.Throws<InvalidOperationException>(() => VoiceSampleLoader.LoadVoiceSample(path));
        Assert.Contains("silence", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteStereoPcm16Wave(string path, int sampleRate, double seconds)
    {
        var frames = (int)Math.Round(sampleRate * seconds);
        var samples = new short[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            var t = frame / (double)sampleRate;
            var left = 0.08 * Math.Sin(2.0 * Math.PI * 1000.0 * t);
            var right = 0.04 * Math.Sin(2.0 * Math.PI * 1000.0 * t);
            samples[frame * 2] = (short)Math.Round(left * short.MaxValue);
            samples[frame * 2 + 1] = (short)Math.Round(right * short.MaxValue);
        }

        WriteWave(path, sampleRate, channels: 2, samples);
    }

    private static void WriteSilenceWave(string path, int sampleRate, double seconds)
    {
        var samples = new short[(int)Math.Round(sampleRate * seconds)];
        WriteWave(path, sampleRate, channels: 1, samples);
    }

    private static void WriteWave(string path, int sampleRate, short channels, short[] samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        var bitsPerSample = (short)16;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;
        var dataBytes = samples.Length * sizeof(short);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }

    private static double ToneMagnitude(float[] audio, int sampleRate, double frequency)
    {
        var real = 0.0;
        var imag = 0.0;
        for (var i = 0; i < audio.Length; i++)
        {
            var angle = 2.0 * Math.PI * frequency * i / sampleRate;
            real += audio[i] * Math.Cos(angle);
            imag -= audio[i] * Math.Sin(angle);
        }
        return Math.Sqrt(real * real + imag * imag);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"voicetunebench-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
