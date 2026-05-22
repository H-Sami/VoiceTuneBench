namespace VoiceTuneBench.Core.Audio;

/// <summary>
/// Loads voice samples from WAV files in the portable core.
/// The WinUI app layer uses NAudio/Media Foundation for compressed formats
/// via <see cref="MediaVoiceSampleLoader.LoadVoiceSample"/> in the VoiceTuneBench.WinUI assembly.
/// </summary>
public static class VoiceSampleLoader
{
    /// <summary>Target sample rate for all loaded audio (48000 Hz).</summary>
    public const int SampleRate = 48000;

    /// <summary>Maximum duration of a preview in seconds (45s).</summary>
    public const double PreviewMaxSeconds = 45.0;

    /// <summary>File extensions supported by the WinUI layer's NAudio/Media Foundation loader.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
    [
        ".wav", ".wave", ".flac", ".aiff", ".aif", ".caf",
        ".ogg", ".oga", ".opus", ".mp3", ".aac", ".m4a", ".m4b",
        ".mp4", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".wma", ".3gp",
    ];

    /// <summary>Loads a WAV file, decodes it, and returns a prepared mono 48kHz voice sample.</summary>
    public static LoadedAudio LoadVoiceSample(
        string path,
        int targetSampleRate = SampleRate,
        double? maxDurationSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose an audio file first.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Audio file not found: {path}", path);
        }

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".wave", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Compressed media decoding is provided by the WinUI app through NAudio / Media Foundation.");
        }

        var decoded = ReadWaveFile(path);
        return PrepareDecodedAudio(
            decoded.Audio,
            decoded.SampleRate,
            decoded.Channels,
            decoded.Decoder,
            targetSampleRate,
            maxDurationSeconds);
    }

    /// <summary>
    /// Applies standard preparation to decoded PCM audio: downmix to mono,
    /// resample to 48kHz, DC offset removal, peak normalization, silence
    /// rejection, and minimum-duration check.
    /// </summary>
    public static LoadedAudio PrepareDecodedAudio(
        float[] interleavedAudio,
        int sourceSampleRate,
        int channels,
        string decoder,
        int targetSampleRate = SampleRate,
        double? maxDurationSeconds = null)
    {
        if (sourceSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSampleRate), "Sample rate must be positive.");
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), "Channel count must be positive.");
        }

        if (interleavedAudio.Length == 0)
        {
            throw new InvalidOperationException("Audio file is empty.");
        }

        var availableFrames = interleavedAudio.Length / channels;
        var maxFrames = maxDurationSeconds is null
            ? availableFrames
            : Math.Min(availableFrames, Math.Max(1, (int)Math.Ceiling(maxDurationSeconds.Value * sourceSampleRate)));

        var mono = DownmixToMono(interleavedAudio, channels, maxFrames);
        mono = ResampleIfNeeded(mono, sourceSampleRate, targetSampleRate);

        if (mono.Length == 0)
        {
            throw new InvalidOperationException("Audio file is empty.");
        }

        RemoveDcOffset(mono);

        var peak = Peak(mono);
        if (peak <= 1e-8)
        {
            throw new InvalidOperationException("Audio file contains only silence.");
        }

        if (peak > 1.0)
        {
            for (var index = 0; index < mono.Length; index++)
            {
                mono[index] = (float)(mono[index] / peak);
            }
        }

        var minimumSamples = (int)Math.Round(targetSampleRate * 0.25);
        if (mono.Length < minimumSamples)
        {
            throw new InvalidOperationException("Audio file must be at least 0.25 seconds long.");
        }

        return new LoadedAudio(
            Audio: mono,
            SampleRate: targetSampleRate,
            DurationSeconds: mono.Length / (double)targetSampleRate,
            Decoder: decoder);
    }

    private static float[] DownmixToMono(float[] interleavedAudio, int channels, int frames)
    {
        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0.0;
            var offset = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += Sanitize(interleavedAudio[offset + channel]);
            }

            mono[frame] = (float)(sum / channels);
        }

        return mono;
    }

    private static float[] ResampleIfNeeded(float[] audio, int sourceSampleRate, int targetSampleRate)
    {
        if (sourceSampleRate == targetSampleRate) return audio;

        var targetLength = Math.Max(1, (int)Math.Round(audio.Length * targetSampleRate / (double)sourceSampleRate));
        var output = new float[targetLength];
        var scale = sourceSampleRate / (double)targetSampleRate;

        for (var index = 0; index < targetLength; index++)
        {
            var sourcePosition = index * scale;
            var i = (int)Math.Floor(sourcePosition);
            var t = (float)(sourcePosition - i);

            if (i <= 0)
            {
                output[index] = audio[0];
                continue;
            }

            if (i + 1 >= audio.Length)
            {
                output[index] = audio[^1];
                continue;
            }

            var p0 = audio[Math.Max(0, i - 1)];
            var p1 = audio[i];
            var p2 = audio[i + 1];
            var p3 = audio[Math.Min(audio.Length - 1, i + 2)];

            output[index] = CatmullRom(p0, p1, p2, p3, t);
        }

        return output;
    }

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static DecodedAudio ReadWaveFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (ReadAscii(reader, 4) != "RIFF")
        {
            throw new InvalidOperationException("WAV file is missing the RIFF header.");
        }

        _ = reader.ReadInt32();
        if (ReadAscii(reader, 4) != "WAVE")
        {
            throw new InvalidOperationException("WAV file is missing the WAVE header.");
        }

        short formatTag = 0;
        short channels = 0;
        var sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadAscii(reader, 4);
            var chunkSize = reader.ReadInt32();
            var nextChunk = stream.Position + chunkSize;

            if (chunkId == "fmt ")
            {
                formatTag = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }

            stream.Position = nextChunk + (chunkSize % 2);
        }

        if (data is null || data.Length == 0)
        {
            throw new InvalidOperationException("WAV file has no audio data.");
        }

        if (channels <= 0 || sampleRate <= 0)
        {
            throw new InvalidOperationException("WAV file has invalid format metadata.");
        }

        var audio = ConvertWaveData(data, formatTag, bitsPerSample);
        return new DecodedAudio(audio, sampleRate, channels, "WAV");
    }

    private static float[] ConvertWaveData(byte[] data, short formatTag, short bitsPerSample)
    {
        if (formatTag == 1 && bitsPerSample == 16)
        {
            var samples = new float[data.Length / 2];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = BitConverter.ToInt16(data, index * 2) / 32768.0f;
            }

            return samples;
        }

        if (formatTag == 1 && bitsPerSample == 24)
        {
            var samples = new float[data.Length / 3];
            for (var index = 0; index < samples.Length; index++)
            {
                var offset = index * 3;
                var value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
                if ((value & 0x800000) != 0)
                {
                    value |= unchecked((int)0xFF000000);
                }

                samples[index] = value / 8388608.0f;
            }

            return samples;
        }

        if (formatTag == 1 && bitsPerSample == 32)
        {
            var samples = new float[data.Length / 4];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = BitConverter.ToInt32(data, index * 4) / 2147483648.0f;
            }

            return samples;
        }

        if (formatTag == 3 && bitsPerSample == 32)
        {
            var samples = new float[data.Length / 4];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = BitConverter.ToSingle(data, index * 4);
            }

            return samples;
        }

        throw new InvalidOperationException($"Unsupported WAV format: format {formatTag}, {bitsPerSample} bits.");
    }

    private static string ReadAscii(BinaryReader reader, int count)
    {
        return System.Text.Encoding.ASCII.GetString(reader.ReadBytes(count));
    }

    private static float Sanitize(float value)
    {
        return float.IsFinite(value) ? value : 0.0f;
    }

    private static void RemoveDcOffset(float[] audio)
    {
        if (audio.Length == 0)
        {
            return;
        }

        var sum = 0.0;
        foreach (var sample in audio)
        {
            sum += sample;
        }

        var mean = sum / audio.Length;
        for (var index = 0; index < audio.Length; index++)
        {
            audio[index] = (float)(audio[index] - mean);
        }
    }

    private static double Peak(float[] audio)
    {
        var peak = 0.0;
        foreach (var sample in audio)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    private sealed record DecodedAudio(float[] Audio, int SampleRate, int Channels, string Decoder);
}
