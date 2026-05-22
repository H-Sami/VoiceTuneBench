using VoiceTuneBench.Core.Audio;
using VoiceTuneBench.Core.DSP;
using VoiceTuneBench.Core.Presets;
using VoiceTuneBench.WinUI.Audio;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Windows.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VoiceTuneBench.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly IReadOnlyList<CuratedPreset> _presets = PresetLibrary.GetCuratedPresets();
    private readonly Dictionary<string, float[]> _previewCache = [];
    private readonly PlaybackController _playbackController;
    private readonly DispatcherQueueTimer _playbackTimer;
    private readonly DispatcherQueueTimer _recordingTimer;
    private readonly VoiceRecorder _voiceRecorder = new();

    private LoadedAudio? _loadedAudio;
    private string _currentPresetDetailsText = string.Empty;
    private bool _isBusy;
    private bool _isRecording;

    public MainWindow()
    {
        InitializeComponent();
        TryUseMicaBackdrop();
        TryResizeWindow();
        TryApplyDarkTitleBar();

        _playbackTimer = DispatcherQueue.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(40);
        _playbackController = new PlaybackController(_playbackTimer);

        _recordingTimer = DispatcherQueue.CreateTimer();
        _recordingTimer.Interval = TimeSpan.FromMilliseconds(250);
        _recordingTimer.Tick += RecordingTimer_Tick;

        _playbackController.Progress += OnPlaybackProgress;
        _playbackController.Stopped += OnPlaybackStopped;

        PresetCombo.ItemsSource = _presets;
        PresetCombo.DisplayMemberPath = nameof(CuratedPreset.DisplayName);
        PresetCombo.SelectedIndex = 0;

        Closed += (_, _) =>
        {
            _recordingTimer.Stop();
            _voiceRecorder.Dispose();
            _playbackController.Dispose();
        };
        RenderPresetDetails(SelectedPreset);
        UpdateButtons();
    }

    private CuratedPreset SelectedPreset => PresetCombo.SelectedItem as CuratedPreset ?? _presets[0];

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        SpectrumGraph.UpdateFromPlayback(e.Index, e.OriginalAudio, e.ProcessedAudio, e.SampleRate);
        GraphModeText.Text = e.Mode == "original" ? "Original highlighted" : "Preset highlighted";
    }

    private void OnPlaybackStopped(object? sender, PlaybackStoppedEventArgs e)
    {
        if (e.Error == null)
        {
            StatusText.Text = "Playback finished.";
            GraphModeText.Text = "Playback finished";
        }
        else
        {
            StatusText.Text = $"Playback stopped: {e.Error.Message}";
            GraphModeText.Text = "Playback error";
        }

        UpdateButtons();
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            ViewMode = PickerViewMode.List,
        };

        foreach (var ext in VoiceSampleLoader.SupportedExtensions)
        {
            picker.FileTypeFilter.Add(ext);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        try
        {
            SetBusy("Loading sample...");
            var loaded = await Task.Run(() => MediaVoiceSampleLoader.LoadVoiceSample(
                file.Path, maxDurationSeconds: VoiceSampleLoader.PreviewMaxSeconds));

            UseLoadedSample(
                loaded,
                $"{file.Name}  |  {loaded.DurationSeconds:0.0}s",
                $"Loaded sample using {loaded.Decoder}.");
        }
        catch (Exception exc)
        {
            await ShowErrorAsync("Load Failed", exc.Message);
        }
        finally
        {
            ClearBusy();
        }
    }

    private async void PlayOriginalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedAudio == null || _isBusy || _isRecording) return;

        try
        {
            var processed = await RenderPreviewAudioAsync(SelectedPreset);
            StartPlayback(_loadedAudio.Audio, _loadedAudio.Audio, processed, mode: "original",
                label: "Playing original sample.");
        }
        catch (Exception exc)
        {
            await ShowErrorAsync("Playback Failed", exc.Message);
            UpdateButtons();
        }
    }

    private async void PlayPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedAudio == null || _isBusy || _isRecording) return;

        try
        {
            var processed = await RenderPreviewAudioAsync(SelectedPreset);
            StartPlayback(processed, _loadedAudio.Audio, processed, mode: "preset",
                label: $"Playing preset: {SelectedPreset.DisplayName}");
        }
        catch (Exception exc)
        {
            await ShowErrorAsync("Preview Failed", exc.Message);
            UpdateButtons();
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _playbackController.Stop();
        StatusText.Text = "Playback stopped.";
        GraphModeText.Text = "Playback stopped";
        UpdateButtons();
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_isRecording)
        {
            await FinishRecordingAsync();
            return;
        }

        try
        {
            _playbackController.Stop();
            SpectrumGraph.Reset();
            _voiceRecorder.Start(VoiceSampleLoader.PreviewMaxSeconds);
            _isRecording = true;
            _recordingTimer.Start();
            RecordButtonText.Text = "Finish";
            StatusText.Text = "Recording... speak normally, then click Finish.";
            GraphModeText.Text = "Recording sample";
            UpdateButtons();
        }
        catch (Exception exc)
        {
            await ShowErrorAsync("Recording Failed", exc.Message);
            _isRecording = false;
            _recordingTimer.Stop();
            RecordButtonText.Text = "Record";
            UpdateButtons();
        }
    }

    private void CopyPresetDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPresetDetailsText)) return;

        var package = new DataPackage();
        package.SetText(_currentPresetDetailsText);
        Clipboard.SetContent(package);
        StatusText.Text = "Preset details copied.";
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not CuratedPreset preset) return;

        _playbackController.Stop();
        _previewCache.Clear();
        SpectrumGraph.Reset();
        RenderPresetDetails(preset);
        StatusText.Text = $"Selected: {preset.DisplayName}";
        GraphModeText.Text = "Load a sample, then play original or preset";
        UpdateButtons();
    }

    private async void RecordingTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_isRecording)
        {
            return;
        }

        var elapsed = _voiceRecorder.Elapsed;
        RecordButtonText.Text = $"Finish {elapsed:mm\\:ss}";
        StatusText.Text = $"Recording... {elapsed:mm\\:ss}";

        if (elapsed.TotalSeconds >= VoiceSampleLoader.PreviewMaxSeconds)
        {
            await FinishRecordingAsync();
        }
    }

    private async Task<float[]> RenderPreviewAudioAsync(CuratedPreset preset)
    {
        if (_loadedAudio == null)
            throw new InvalidOperationException("Load a voice sample first.");

        if (_previewCache.TryGetValue(preset.Slug, out var cached))
            return cached;

        SetBusy($"Rendering preview: {preset.DisplayName}");
        try
        {
            var rendered = await Task.Run(() =>
                PreviewRenderer.RenderPresetPreview(_loadedAudio.Audio, _loadedAudio.SampleRate, preset));

            var padded = MatchLength(rendered, _loadedAudio.Audio.Length);
            _previewCache[preset.Slug] = padded;
            StatusText.Text = $"Preview ready: {preset.DisplayName}";
            return padded;
        }
        finally
        {
            ClearBusy();
        }
    }

    private void StartPlayback(
        float[] playbackAudio,
        float[] originalAudio,
        float[] processedAudio,
        string mode,
        string label)
    {
        if (_loadedAudio == null)
            throw new InvalidOperationException("Load a voice sample first.");

        var originalComparison = MatchLength(originalAudio, _loadedAudio.Audio.Length);
        var processedComparison = MatchLength(processedAudio, _loadedAudio.Audio.Length);
        _playbackController.Start(
            playbackAudio,
            originalComparison,
            processedComparison,
            _loadedAudio.SampleRate,
            mode);

        StatusText.Text = label;
        GraphModeText.Text = mode == "original" ? "Original highlighted" : "Preset highlighted";
        UpdateButtons();
    }

    private async Task FinishRecordingAsync()
    {
        if (!_isRecording)
        {
            return;
        }

        _recordingTimer.Stop();
        _isRecording = false;
        RecordButtonText.Text = "Record";

        try
        {
            SetBusy("Preparing recording...");
            var loaded = await Task.Run(() => _voiceRecorder.Stop());
            UseLoadedSample(
                loaded,
                $"Recorded sample  |  {loaded.DurationSeconds:0.0}s",
                "Recorded sample is ready.");
        }
        catch (Exception exc)
        {
            await ShowErrorAsync("Recording Failed", exc.Message);
        }
        finally
        {
            ClearBusy();
            UpdateButtons();
        }
    }

    private void UseLoadedSample(LoadedAudio loaded, string sampleText, string status)
    {
        _playbackController.Stop();
        _loadedAudio = loaded;
        _previewCache.Clear();
        SpectrumGraph.Reset();

        SampleText.Text = sampleText;
        StatusText.Text = status;
        GraphModeText.Text = "Load a sample, then play original or preset";
    }

    private static float[] MatchLength(float[] audio, int targetLength)
    {
        if (audio.Length == targetLength) return audio;
        var output = new float[targetLength];
        Array.Copy(audio, output, Math.Min(audio.Length, targetLength));
        return output;
    }

    private void RenderPresetDetails(CuratedPreset preset)
    {
        _currentPresetDetailsText = PresetDetailsBuilder.FormatPlainText(preset);
        CopyPresetDetailsButton.IsEnabled = true;
        PresetDetailsPanel.Children.Clear();
        PresetDetailsPanel.Children.Add(PresetDetailsBuilder.Build(preset));
    }

    private void SetBusy(string status)
    {
        _isBusy = true;
        StatusText.Text = status;
        UpdateButtons();
    }

    private void ClearBusy()
    {
        _isBusy = false;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var sampleLoaded = _loadedAudio != null && _loadedAudio.Audio.Length > 0;
        var isPlaying = _playbackController.IsPlaying;
        PlayOriginalButton.IsEnabled = sampleLoaded && !isPlaying && !_isBusy && !_isRecording;
        PlayPresetButton.IsEnabled = sampleLoaded && !isPlaying && !_isBusy && !_isRecording;
        StopButton.IsEnabled = isPlaying && !_isRecording;
        PresetCombo.IsEnabled = !isPlaying && !_isBusy && !_isRecording;
        LoadButton.IsEnabled = !isPlaying && !_isBusy && !_isRecording;
        RecordButton.IsEnabled = !isPlaying && !_isBusy;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        StatusText.Text = $"{title}: {message}";
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void TryUseMicaBackdrop()
    {
        try { SystemBackdrop = new MicaBackdrop(); }
        catch { }
    }

    private void TryResizeWindow()
    {
        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(1220, 780));
        }
        catch { }
    }

    private void TryApplyDarkTitleBar()
    {
        try
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var tb = appWindow.TitleBar;

            var bg = Color.FromArgb(255, 23, 29, 38);
            var fg = Color.FromArgb(255, 255, 255, 255);
            var hoverBg = Color.FromArgb(255, 37, 48, 64);
            var pressedBg = Color.FromArgb(255, 49, 64, 84);
            var inactiveFg = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

            tb.BackgroundColor = bg;
            tb.ForegroundColor = fg;
            tb.InactiveBackgroundColor = bg;
            tb.InactiveForegroundColor = inactiveFg;
            tb.ButtonBackgroundColor = bg;
            tb.ButtonForegroundColor = fg;
            tb.ButtonHoverBackgroundColor = hoverBg;
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedBackgroundColor = pressedBg;
            tb.ButtonPressedForegroundColor = fg;
            tb.ButtonInactiveBackgroundColor = bg;
            tb.ButtonInactiveForegroundColor = inactiveFg;
        }
        catch { }
    }
}
