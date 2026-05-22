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

    private LoadedAudio? _loadedAudio;
    private string _currentPresetDetailsText = string.Empty;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        TryUseMicaBackdrop();
        TryResizeWindow();
        TryApplyDarkTitleBar();

        _playbackTimer = DispatcherQueue.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(40);
        _playbackController = new PlaybackController(_playbackTimer);

        _playbackController.Progress += OnPlaybackProgress;
        _playbackController.Stopped += OnPlaybackStopped;

        PresetCombo.ItemsSource = _presets;
        PresetCombo.DisplayMemberPath = nameof(CuratedPreset.DisplayName);
        PresetCombo.SelectedIndex = 0;

        Closed += (_, _) => _playbackController.Dispose();
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

            _playbackController.Stop();
            _loadedAudio = loaded;
            _previewCache.Clear();
            SpectrumGraph.Reset();

            SampleText.Text = $"{file.Name}  |  {loaded.DurationSeconds:0.0}s";
            StatusText.Text = $"Loaded sample using {loaded.Decoder}.";
            GraphModeText.Text = "Load a sample, then play original or preset";
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
        if (_loadedAudio == null || _isBusy) return;

        try
        {
            var processed = await RenderPreviewAudioAsync(SelectedPreset);
            StartPlayback(_loadedAudio.Audio, processed, mode: "original",
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
        if (_loadedAudio == null || _isBusy) return;

        try
        {
            var processed = await RenderPreviewAudioAsync(SelectedPreset);
            StartPlayback(processed, processed, mode: "preset",
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

    private void StartPlayback(float[] audio, float[] processedAudio, string mode, string label)
    {
        if (_loadedAudio == null)
            throw new InvalidOperationException("Load a voice sample first.");

        var comparison = MatchLength(processedAudio, _loadedAudio.Audio.Length);
        _playbackController.Start(audio, comparison, _loadedAudio.SampleRate, mode);

        StatusText.Text = label;
        GraphModeText.Text = mode == "original" ? "Original highlighted" : "Preset highlighted";
        UpdateButtons();
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
        PlayOriginalButton.IsEnabled = sampleLoaded && !isPlaying && !_isBusy;
        PlayPresetButton.IsEnabled = sampleLoaded && !isPlaying && !_isBusy;
        StopButton.IsEnabled = isPlaying;
        PresetCombo.IsEnabled = !isPlaying && !_isBusy;
        LoadButton.IsEnabled = !isPlaying && !_isBusy;
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

            var bg = Color.FromArgb(0, 0, 0, 0);
            var fg = Color.FromArgb(255, 255, 255, 255);
            var hoverBg = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
            var pressedBg = Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF);
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