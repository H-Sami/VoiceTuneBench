using VoiceTuneBench.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceTuneBench.WinUI.Audio;

internal static class MediaVoiceSampleLoader
{
    public static LoadedAudio LoadVoiceSample(
        string path,
        int targetSampleRate = VoiceSampleLoader.SampleRate,
        double? maxDurationSeconds = null)
    {
        try
        {
            return LoadWithNAudio(path, targetSampleRate, maxDurationSeconds);
        }
        catch (Exception exc)
        {
            throw new InvalidOperationException(
                $"Could not read {Path.GetFileName(path)} as audio. {exc.Message}",
                exc);
        }
    }

    private static LoadedAudio LoadWithNAudio(
        string path,
        int targetSampleRate,
        double? maxDurationSeconds)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider provider = reader;
        if (reader.WaveFormat.SampleRate != targetSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, targetSampleRate);
        }

        var channels = provider.WaveFormat.Channels;
        var maxFrames = maxDurationSeconds is null
            ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(maxDurationSeconds.Value * targetSampleRate));

        var interleaved = new List<float>(Math.Min(maxFrames * channels, targetSampleRate * channels * 4));
        var bufferFrames = Math.Min(targetSampleRate, 16384);
        var buffer = new float[bufferFrames * channels];

        while (interleaved.Count / channels < maxFrames)
        {
            var framesNeeded = Math.Min(bufferFrames, maxFrames - interleaved.Count / channels);
            var samplesNeeded = framesNeeded * channels;
            var samplesRead = provider.Read(buffer, 0, samplesNeeded);
            if (samplesRead <= 0)
            {
                break;
            }

            for (var index = 0; index < samplesRead; index++)
            {
                interleaved.Add(float.IsFinite(buffer[index]) ? buffer[index] : 0.0f);
            }
        }

        return VoiceSampleLoader.PrepareDecodedAudio(
            interleaved.ToArray(),
            targetSampleRate,
            channels,
            "NAudio / Media Foundation",
            targetSampleRate,
            maxDurationSeconds: null);
    }
}
