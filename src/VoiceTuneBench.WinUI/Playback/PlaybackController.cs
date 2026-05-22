using System.Diagnostics;
using NAudio.Wave;
using Microsoft.UI.Dispatching;
using VoiceTuneBench.Core.Audio;
using VoiceTuneBench.WinUI.Playback;

namespace VoiceTuneBench.WinUI;

public sealed class PlaybackController : IDisposable
{
    private readonly DispatcherQueueTimer _spectrumTimer;
    private readonly DispatcherQueue _dispatcher;

    private WaveOutEvent? _waveOut;
    private FloatArrayWaveProvider? _waveProvider;
    private int _playbackLength;
    private int _sampleRate = VoiceSampleLoader.SampleRate;
    private float[]? _originalAudio;
    private float[]? _processedAudio;
    private string? _activeMode;
    private readonly Stopwatch _clock = new();
    private bool _isDisposed;

    public event EventHandler<PlaybackProgressEventArgs>? Progress;
    public event EventHandler<PlaybackStoppedEventArgs>? Stopped;

    public PlaybackController(DispatcherQueueTimer timer)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _spectrumTimer = timer;
        _spectrumTimer.Interval = TimeSpan.FromMilliseconds(40);
        _spectrumTimer.Tick += OnTimerTick;
    }

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

    public float[]? OriginalAudio => _originalAudio;
    public float[]? ProcessedAudio => _processedAudio;
    public string? ActiveMode => _activeMode;

    public void Start(
        float[] playbackAudio,
        float[] originalAudio,
        float[] processedAudio,
        int sampleRate,
        string mode)
    {
        Stop();

        _playbackLength = playbackAudio.Length;
        _sampleRate = sampleRate;
        _originalAudio = originalAudio;
        _processedAudio = processedAudio;
        _activeMode = mode;

        _waveProvider = new FloatArrayWaveProvider(playbackAudio, sampleRate);
        _waveOut = new WaveOutEvent();
        _waveOut.PlaybackStopped += OnPlaybackStopped;

        try
        {
            _waveOut.Init(_waveProvider);
            _waveOut.Play();
        }
        catch
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
            _waveProvider = null;
            _playbackLength = 0;
            _originalAudio = null;
            _processedAudio = null;
            _activeMode = null;
            throw;
        }

        _clock.Restart();
        _spectrumTimer.Start();
    }

    public void Stop()
    {
        _spectrumTimer.Stop();
        _clock.Reset();

        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                _waveOut.Stop();
            }
            catch
            {
            }

            _waveOut.Dispose();
            _waveOut = null;
        }

        _waveProvider = null;
        _playbackLength = 0;
        _originalAudio = null;
        _processedAudio = null;
        _activeMode = null;
    }

    private void OnTimerTick(object? sender, object e)
    {
        if (_waveOut == null || _originalAudio == null || _processedAudio == null)
        {
            return;
        }

        if (_waveOut.PlaybackState == PlaybackState.Stopped)
        {
            Finish(null);
            return;
        }

        var index = (int)Math.Round(_clock.Elapsed.TotalSeconds * _sampleRate);
        if (index >= _playbackLength)
        {
            Finish(null);
            return;
        }

        Progress?.Invoke(this, new PlaybackProgressEventArgs(
            index,
            _originalAudio,
            _processedAudio,
            _sampleRate,
            _activeMode ?? "original"));
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        _dispatcher.TryEnqueue(() => Finish(e.Exception));
    }

    private void Finish(Exception? error)
    {
        _spectrumTimer.Stop();
        _clock.Reset();

        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
        }

        var original = _originalAudio;
        var processed = _processedAudio;
        var mode = _activeMode;

        _waveProvider = null;
        _playbackLength = 0;
        _originalAudio = null;
        _processedAudio = null;
        _activeMode = null;

        Stopped?.Invoke(this, new PlaybackStoppedEventArgs(original, processed, mode, error));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
        _spectrumTimer.Stop();
        _spectrumTimer.Tick -= OnTimerTick;
    }
}

public sealed class PlaybackProgressEventArgs(
    int index,
    float[] originalAudio,
    float[] processedAudio,
    int sampleRate,
    string mode) : EventArgs
{
    public int Index { get; } = index;
    public float[] OriginalAudio { get; } = originalAudio;
    public float[] ProcessedAudio { get; } = processedAudio;
    public int SampleRate { get; } = sampleRate;
    public string Mode { get; } = mode;
}

public sealed class PlaybackStoppedEventArgs : EventArgs
{
    public PlaybackStoppedEventArgs(float[]? original, float[]? processed, string? mode, Exception? error)
    {
        OriginalAudio = original;
        ProcessedAudio = processed;
        Mode = mode;
        Error = error;
    }

    public float[]? OriginalAudio { get; }
    public float[]? ProcessedAudio { get; }
    public string? Mode { get; }
    public Exception? Error { get; }
}
