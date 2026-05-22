using System.Diagnostics;
using VoiceTuneBench.Core.Audio;
using NAudio.Wave;

namespace VoiceTuneBench.WinUI.Audio;

internal sealed class VoiceRecorder : IDisposable
{
    private const int CaptureSampleRate = VoiceSampleLoader.SampleRate;
    private const int CaptureChannels = 1;
    private const int CaptureBitsPerSample = 16;

    private readonly List<float> _samples = [];
    private readonly object _gate = new();
    private readonly Stopwatch _clock = new();
    private WaveInEvent? _waveIn;
    private int _maxSamples;

    public bool IsRecording => _waveIn != null;

    public TimeSpan Elapsed => _clock.Elapsed;

    public void Start(double maxDurationSeconds)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("Recording is already active.");
        }

        if (WaveIn.DeviceCount <= 0)
        {
            throw new InvalidOperationException("No microphone input device was found.");
        }

        lock (_gate)
        {
            _samples.Clear();
        }

        _maxSamples = Math.Max(1, (int)Math.Round(maxDurationSeconds * CaptureSampleRate));

        _waveIn = new WaveInEvent
        {
            DeviceNumber = 0,
            WaveFormat = new WaveFormat(CaptureSampleRate, CaptureBitsPerSample, CaptureChannels),
            BufferMilliseconds = 40,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
        _clock.Restart();
    }

    public LoadedAudio Stop()
    {
        if (_waveIn == null)
        {
            throw new InvalidOperationException("Recording is not active.");
        }

        var waveIn = _waveIn;
        _waveIn = null;
        _clock.Stop();

        try
        {
            waveIn.StopRecording();
        }
        finally
        {
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.Dispose();
        }

        float[] samples;
        lock (_gate)
        {
            samples = _samples.ToArray();
        }

        return VoiceSampleLoader.PrepareDecodedAudio(
            samples,
            CaptureSampleRate,
            CaptureChannels,
            "Microphone recording",
            VoiceSampleLoader.SampleRate,
            maxDurationSeconds: null);
    }

    public void Dispose()
    {
        if (_waveIn == null)
        {
            return;
        }

        try
        {
            Stop();
        }
        catch
        {
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            var availableSlots = _maxSamples - _samples.Count;
            if (availableSlots <= 0)
            {
                return;
            }

            var samplesAvailable = Math.Min(e.BytesRecorded / sizeof(short), availableSlots);
            for (var index = 0; index < samplesAvailable; index++)
            {
                var sample = BitConverter.ToInt16(e.Buffer, index * sizeof(short)) / 32768.0f;
                _samples.Add(sample);
            }
        }
    }
}
