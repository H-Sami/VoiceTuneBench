using System.Diagnostics;
using System.Text;
using VoiceTuneBench.Core.Audio;
using VoiceTuneBench.Core.DSP;
using VoiceTuneBench.Core.Presets;
using VoiceTuneBench.Core.Visualization;
using VoiceTuneBench.WinUI.Audio;
using VoiceTuneBench.WinUI.Playback;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NAudio.Wave;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VoiceTuneBench.WinUI;

public sealed partial class MainWindow : Window
{
    private const int SpectrumWindowSize = 8192;

    private readonly IReadOnlyList<CuratedPreset> _presets = PresetLibrary.GetCuratedPresets();
    private readonly Dictionary<string, float[]> _previewCache = [];
    private readonly Stopwatch _playbackClock = new();
    private readonly DispatcherQueueTimer _playbackTimer;

    private LoadedAudio? _loadedAudio;
    private WaveOutEvent? _waveOut;
    private float[]? _playbackAudio;
    private float[]? _comparisonProcessedAudio;
    private string? _activePlayback;
    private bool _isBusy;

    private double[]? _lastFrequencies;
    private double[]? _lastOriginalDb;
    private double[]? _lastProcessedDb;
    private double[]? _smoothedOriginal;
    private double[]? _smoothedProcessed;
    private string _currentPresetDetailsText = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        TryUseMicaBackdrop();
        TryResizeWindow();

        _playbackTimer = DispatcherQueue.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(40);
        _playbackTimer.Tick += (_, _) => UpdatePlaybackSpectrum();

        PresetCombo.ItemsSource = _presets;
        PresetCombo.DisplayMemberPath = nameof(CuratedPreset.DisplayName);
        PresetCombo.SelectedIndex = 0;

        Closed += (_, _) => StopPlayback(updateStatus: false);
        RenderPresetDetails(SelectedPreset);
        UpdateButtons();
    }

    private CuratedPreset SelectedPreset => PresetCombo.SelectedItem as CuratedPreset ?? _presets[0];

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            ViewMode = PickerViewMode.List,
        };

        foreach (var extension in VoiceSampleLoader.SupportedExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            SetBusy("Loading sample...");
            var loaded = await Task.Run(() => MediaVoiceSampleLoader.LoadVoiceSample(
                file.Path,
                maxDurationSeconds: VoiceSampleLoader.PreviewMaxSeconds));

            StopPlayback(updateStatus: false);
            _loadedAudio = loaded;
            _previewCache.Clear();
            ClearGraph();

            SampleText.Text = $"{file.Name}  |  {loaded.DurationSeconds:0.0}s";
            StatusText.Text = $"Loaded sample using {loaded.Decoder}.";
            GraphModeText.Text = "Spectrum starts when playback starts";
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
        if (_loadedAudio is null)
        {
            return;
        }

        try
        {
            var processed = await RenderPreviewAudioAsync(SelectedPreset);
            StartPlayback(
                audio: _loadedAudio.Audio,
                processedAudio: processed,
                mode: "original",
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
        if (_loadedAudio is null)
        {
            return;
        }

        try
        {
            var processed = await RenderPreviewAudioAsync(SelectedPreset);
            StartPlayback(
                audio: processed,
                processedAudio: processed,
                mode: "preset",
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
        StopPlayback(updateStatus: true);
    }

    private void CopyPresetDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPresetDetailsText))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(_currentPresetDetailsText);
        Clipboard.SetContent(package);
        StatusText.Text = "Preset details copied.";
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not CuratedPreset preset)
        {
            return;
        }

        StopPlayback(updateStatus: false);
        _previewCache.Clear();
        ClearGraph();
        RenderPresetDetails(preset);
        StatusText.Text = $"Selected: {preset.DisplayName}";
        GraphModeText.Text = "Load a sample, then play original or preset";
        UpdateButtons();
    }

    private async Task<float[]> RenderPreviewAudioAsync(CuratedPreset preset)
    {
        if (_loadedAudio is null)
        {
            throw new InvalidOperationException("Load a voice sample first.");
        }

        if (_previewCache.TryGetValue(preset.Slug, out var cached))
        {
            return cached;
        }

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
        if (_loadedAudio is null)
        {
            throw new InvalidOperationException("Load a voice sample first.");
        }

        StopPlayback(updateStatus: false);

        var playbackAudio = audio.ToArray();
        var comparisonAudio = MatchLength(processedAudio, _loadedAudio.Audio.Length);
        var provider = new FloatArrayWaveProvider(playbackAudio, _loadedAudio.SampleRate);
        var waveOut = new WaveOutEvent();

        try
        {
            waveOut.Init(provider);
            waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
            waveOut.Play();
        }
        catch
        {
            waveOut.Dispose();
            _waveOut = null;
            _playbackAudio = null;
            _comparisonProcessedAudio = null;
            _activePlayback = null;
            _playbackClock.Reset();
            throw;
        }

        _waveOut = waveOut;
        _playbackAudio = playbackAudio;
        _comparisonProcessedAudio = comparisonAudio;
        _activePlayback = mode;
        _smoothedOriginal = null;
        _smoothedProcessed = null;
        _playbackClock.Restart();

        StatusText.Text = label;
        GraphModeText.Text = mode == "original" ? "Original highlighted" : "Preset highlighted";
        UpdateButtons();
        _playbackTimer.Start();
        UpdatePlaybackSpectrum();
    }

    private void StopPlayback(bool updateStatus)
    {
        var waveOut = _waveOut;
        _waveOut = null;
        if (waveOut is not null)
        {
            waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
            try
            {
                waveOut.Stop();
            }
            catch
            {
                // Stopping is best-effort because output devices can disappear.
            }

            waveOut.Dispose();
        }

        _playbackTimer.Stop();
        _playbackClock.Reset();
        _activePlayback = null;
        _playbackAudio = null;
        _comparisonProcessedAudio = null;
        _smoothedOriginal = null;
        _smoothedProcessed = null;

        if (updateStatus)
        {
            StatusText.Text = "Playback stopped.";
            GraphModeText.Text = "Playback stopped";
        }

        UpdateButtons();
    }

    private void FinishPlayback(Exception? playbackError = null)
    {
        var waveOut = _waveOut;
        _waveOut = null;
        if (waveOut is not null)
        {
            waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
            waveOut.Dispose();
        }

        _playbackTimer.Stop();
        _playbackClock.Reset();
        _activePlayback = null;
        _playbackAudio = null;
        _comparisonProcessedAudio = null;
        _smoothedOriginal = null;
        _smoothedProcessed = null;

        if (playbackError is null)
        {
            StatusText.Text = "Playback finished.";
            GraphModeText.Text = "Playback finished";
        }
        else
        {
            StatusText.Text = $"Playback stopped: {playbackError.Message}";
            GraphModeText.Text = "Playback error";
        }

        UpdateButtons();
    }

    private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => FinishPlayback(e.Exception));
    }

    private void UpdatePlaybackSpectrum()
    {
        if (_loadedAudio is null
            || _playbackAudio is null
            || _comparisonProcessedAudio is null
            || _activePlayback is null)
        {
            return;
        }

        var sampleRate = _loadedAudio.SampleRate;
        var index = (int)Math.Round(_playbackClock.Elapsed.TotalSeconds * sampleRate);
        if (index >= _playbackAudio.Length)
        {
            FinishPlayback();
            return;
        }

        var start = Math.Max(0, index - SpectrumWindowSize / 2);
        var end = Math.Min(_loadedAudio.Audio.Length, start + SpectrumWindowSize);
        start = Math.Max(0, end - SpectrumWindowSize);

        var originalSegment = ExtractSegment(_loadedAudio.Audio, start, SpectrumWindowSize);
        var processedSegment = ExtractSegment(_comparisonProcessedAudio, start, SpectrumWindowSize);

        var original = SpectrumAnalyzer.ComputeSpectrum(originalSegment, sampleRate);
        var processed = SpectrumAnalyzer.ComputeSpectrum(processedSegment, sampleRate);
        if (original.Frequencies.Length == 0 || processed.LevelsDb.Length != original.LevelsDb.Length)
        {
            return;
        }

        var reference = Math.Max(original.LevelsDb.Max(), -120.0);
        var originalRelative = original.LevelsDb
            .Select(value => Math.Clamp(value - reference, -72.0, 9.0))
            .ToArray();
        var processedRelative = processed.LevelsDb
            .Select(value => Math.Clamp(value - reference, -72.0, 9.0))
            .ToArray();

        _lastFrequencies = original.Frequencies;
        _lastOriginalDb = SmoothSpectrum(originalRelative, ref _smoothedOriginal);
        _lastProcessedDb = SmoothSpectrum(processedRelative, ref _smoothedProcessed);
        SpectrumCanvas.Invalidate();
    }

    private void SpectrumCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var session = args.DrawingSession;
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        session.Clear(ThemeColors.GraphBackground);

        const float left = 48.0f;
        const float top = 24.0f;
        const float right = 18.0f;
        const float bottom = 34.0f;
        var plotWidth = Math.Max(1.0f, width - left - right);
        var plotHeight = Math.Max(1.0f, height - top - bottom);

        DrawGrid(session, left, top, plotWidth, plotHeight);

        if (_lastFrequencies is null || _lastOriginalDb is null || _lastProcessedDb is null)
        {
            session.DrawText(
                "Playback spectrum will appear here",
                left + plotWidth / 2.0f - 108.0f,
                top + plotHeight / 2.0f - 10.0f,
                ThemeColors.EmptyGraphText);
            return;
        }

        var originalWidth = _activePlayback == "original" ? 3.0f : 1.8f;
        var processedWidth = _activePlayback == "preset" ? 3.0f : 1.8f;

        DrawSpectrum(session, _lastFrequencies, _lastOriginalDb, left, top, plotWidth, plotHeight, ThemeColors.OriginalBlue, originalWidth);
        DrawSpectrum(session, _lastFrequencies, _lastProcessedDb, left, top, plotWidth, plotHeight, ThemeColors.PresetGreen, processedWidth);

        session.DrawText("Original", left + 12.0f, top + 10.0f, ThemeColors.OriginalBlue);
        session.DrawText("With preset", left + 92.0f, top + 10.0f, ThemeColors.PresetGreen);
    }

    private static void DrawGrid(
        Microsoft.Graphics.Canvas.CanvasDrawingSession session,
        float left,
        float top,
        float width,
        float height)
    {
        foreach (var db in new[] { -72.0, -54.0, -36.0, -18.0, 0.0 })
        {
            var y = MapDb(db, top, height);
            session.DrawLine(left, y, left + width, y, ThemeColors.GridLine, 1.0f);
            session.DrawText(db.ToString("0"), 6.0f, y - 9.0f, ThemeColors.AxisLabel);
        }

        foreach (var frequency in new[] { 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0, 2000.0, 5000.0, 10000.0, 20000.0 })
        {
            var x = MapFrequency(frequency, left, width);
            session.DrawLine(x, top, x, top + height, ThemeColors.GridLine, 1.0f);
            session.DrawText(FormatFrequency(frequency), x - 10.0f, top + height + 8.0f, ThemeColors.AxisLabel);
        }
    }

    private static void DrawSpectrum(
        Microsoft.Graphics.Canvas.CanvasDrawingSession session,
        double[] frequencies,
        double[] levelsDb,
        float left,
        float top,
        float width,
        float height,
        Windows.UI.Color color,
        float strokeWidth)
    {
        var count = Math.Min(frequencies.Length, levelsDb.Length);
        if (count < 2)
        {
            return;
        }

        var previousX = MapFrequency(frequencies[0], left, width);
        var previousY = MapDb(levelsDb[0], top, height);
        for (var index = 1; index < count; index++)
        {
            var x = MapFrequency(frequencies[index], left, width);
            var y = MapDb(levelsDb[index], top, height);
            session.DrawLine(previousX, previousY, x, y, color, strokeWidth);
            previousX = x;
            previousY = y;
        }
    }

    private void RenderPresetDetails(CuratedPreset preset)
    {
        _currentPresetDetailsText = FormatPresetDetailsText(preset);
        CopyPresetDetailsButton.IsEnabled = true;
        PresetDetailsPanel.Children.Clear();
        PresetDetailsPanel.Children.Add(BuildOverviewSection(preset));
        PresetDetailsPanel.Children.Add(BuildEqSection(preset));
        PresetDetailsPanel.Children.Add(BuildCompressorSection(preset.Compressor));
        PresetDetailsPanel.Children.Add(BuildSourcesSection(preset));
    }

    private UIElement BuildOverviewSection(CuratedPreset preset)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = preset.DisplayName,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppBrush("VoiceTuneBenchPrimaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(CreateDetailLine("Best for", preset.BestFor));
        content.Children.Add(CreateDetailLine("Tone", preset.Summary));
        content.Children.Add(CreateDetailLine("Chain", "ReaEQ before ReaComp."));
        return CreateSection("Overview", content);
    }

    private UIElement BuildEqSection(CuratedPreset preset)
    {
        var rows = new StackPanel { Spacing = 8 };
        for (var index = 0; index < preset.EqBands.Count; index++)
        {
            rows.Children.Add(CreateEqBandRow(index + 1, preset.EqBands[index]));
        }

        return CreateSection("ReaEQ", rows);
    }

    private UIElement BuildCompressorSection(CompressorSettings comp)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var items = new (string Label, string Value)[]
        {
            ("Threshold", $"{comp.ThresholdDb:0.0} dBFS"),
            ("Ratio", $"{comp.Ratio:0.0}:1"),
            ("Attack", $"{comp.AttackMs:0} ms"),
            ("Release", $"{comp.ReleaseMs:0} ms"),
            ("Knee", $"{comp.KneeDb:0.0} dB"),
            ("RMS", $"{comp.RmsSizeMs:0} ms"),
            ("Pre-comp", $"{comp.PrecompMs:0} ms"),
            ("Detector", comp.DetectorInput),
            ("Detector HP", $"{comp.DetectorHighpassHz:0} Hz"),
            ("Detector LP", $"{comp.DetectorLowpassHz:0} Hz"),
            ("AA", comp.AA),
            ("Makeup", $"+{comp.MakeupGainDb:0.0} dB"),
            ("Classic attack", OnOff(comp.ClassicAttack)),
            ("Auto release", OnOff(comp.AutoRelease)),
            ("Limit output", OnOff(comp.LimitOutput)),
            ("Auto makeup", OnOff(comp.AutoMakeup)),
            ("Preview filter", OnOff(comp.PreviewFilter)),
            ("Wet / Dry", $"{comp.WetDb:0.0} dB / {(double.IsNegativeInfinity(comp.DryDb) ? "-inf" : $"{comp.DryDb:0.0} dB")}"),
        };

        for (var row = 0; row < (int)Math.Ceiling(items.Length / 2.0); row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < items.Length; index++)
        {
            var cell = CreateSettingCell(items[index].Label, items[index].Value);
            Grid.SetRow(cell, index / 2);
            Grid.SetColumn(cell, index % 2);
            grid.Children.Add(cell);
        }

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(grid);
        if (!string.IsNullOrWhiteSpace(comp.Label))
        {
            content.Children.Add(CreateNoteText(comp.Label));
        }

        return CreateSection("ReaComp", content);
    }

    private UIElement BuildSourcesSection(CuratedPreset preset)
    {
        var content = new StackPanel { Spacing = 4 };
        foreach (var (name, url) in preset.SourceNames.Zip(preset.SourceUrls))
        {
            content.Children.Add(new HyperlinkButton
            {
                Content = name,
                NavigateUri = new Uri(url),
                Padding = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = AppBrush("VoiceTuneBenchAccentBlueBrush"),
            });
        }

        return CreateSection("Sources", content);
    }

    private static UIElement CreateSection(string title, UIElement content)
    {
        var section = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 4),
        };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppBrush("VoiceTuneBenchMutedTextBrush"),
        });
        section.Children.Add(content);
        return section;
    }

    private static UIElement CreateDetailLine(string label, string value)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppBrush("VoiceTuneBenchSubtleTextBrush"),
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = AppBrush("VoiceTuneBenchPrimaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private static UIElement CreateEqBandRow(int index, EqBand band)
    {
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = $"{index:00}. {band.FilterName}",
            FontWeight = FontWeights.SemiBold,
            Foreground = AppBrush("VoiceTuneBenchPrimaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = FormatEqSetting(band),
            Foreground = AppBrush("VoiceTuneBenchMutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(band.Label))
        {
            content.Children.Add(CreateNoteText(band.Label));
        }

        return new Border
        {
            Background = AppBrush("VoiceTuneBenchRaisedSurfaceBrush"),
            BorderBrush = AppBrush("VoiceTuneBenchBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = content,
        };
    }

    private static FrameworkElement CreateSettingCell(string label, string value)
    {
        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppBrush("VoiceTuneBenchSubtleTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = AppBrush("VoiceTuneBenchPrimaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border
        {
            Background = AppBrush("VoiceTuneBenchRaisedSurfaceBrush"),
            BorderBrush = AppBrush("VoiceTuneBenchBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 7, 9, 7),
            Child = content,
        };
    }

    private static TextBlock CreateNoteText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = AppBrush("VoiceTuneBenchMutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private static string FormatEqSetting(EqBand band)
    {
        return band.BandType is EqBandType.HighPass or EqBandType.LowPass
            ? $"{band.Frequency:0} Hz, Q {band.Q:0.00}"
            : $"{band.Frequency:0} Hz, {band.GainDb:+0.0;-0.0;0.0} dB, Q {band.Q:0.00}";
    }

    private static Brush AppBrush(string key)
    {
        return (Brush)Application.Current.Resources[key];
    }

    private static string FormatPresetDetailsText(CuratedPreset preset)
    {
        var builder = new StringBuilder();
        builder.AppendLine(preset.DisplayName);
        builder.AppendLine(new string('=', preset.DisplayName.Length));
        builder.AppendLine();
        builder.AppendLine($"Best for: {preset.BestFor}");
        builder.AppendLine($"Tone:     {preset.Summary}");
        builder.AppendLine();
        builder.AppendLine("ReaEQ");
        builder.AppendLine("-----");

        for (var index = 0; index < preset.EqBands.Count; index++)
        {
            var band = preset.EqBands[index];
            var setting = band.BandType is EqBandType.HighPass or EqBandType.LowPass
                ? $"{band.Frequency:0} Hz, Q {band.Q:0.00}"
                : $"{band.Frequency:0} Hz, {band.GainDb:+0.0;-0.0;0.0} dB, Q {band.Q:0.00}";

            builder.AppendLine($"{index + 1:00}. {band.FilterName}: {setting}");
            if (!string.IsNullOrWhiteSpace(band.Label))
            {
                builder.AppendLine($"    {band.Label}");
            }
        }

        var comp = preset.Compressor;
        builder.AppendLine();
        builder.AppendLine("ReaComp");
        builder.AppendLine("-------");
        builder.AppendLine($"Threshold:      {comp.ThresholdDb:0.0} dBFS");
        builder.AppendLine($"Ratio:          {comp.Ratio:0.0}:1");
        builder.AppendLine($"Attack:         {comp.AttackMs:0} ms");
        builder.AppendLine($"Release:        {comp.ReleaseMs:0} ms");
        builder.AppendLine($"Knee size:      {comp.KneeDb:0.0} dB");
        builder.AppendLine($"RMS size:       {comp.RmsSizeMs:0} ms");
        builder.AppendLine($"Pre-comp:       {comp.PrecompMs:0} ms");
        builder.AppendLine($"Classic attack: {OnOff(comp.ClassicAttack)}");
        builder.AppendLine($"Auto release:   {OnOff(comp.AutoRelease)}");
        builder.AppendLine($"Detector input: {comp.DetectorInput}");
        builder.AppendLine($"Detector HP:    {comp.DetectorHighpassHz:0} Hz");
        builder.AppendLine($"Detector LP:    {comp.DetectorLowpassHz:0} Hz");
        builder.AppendLine($"AA:             {comp.AA}");
        builder.AppendLine($"Limit output:   {OnOff(comp.LimitOutput)}");
        builder.AppendLine($"Auto makeup:    {OnOff(comp.AutoMakeup)}");
        builder.AppendLine($"Manual makeup:  +{comp.MakeupGainDb:0.0} dB");
        builder.AppendLine($"Preview filter: {OnOff(comp.PreviewFilter)}");
        builder.AppendLine($"Wet:            {comp.WetDb:0.0} dB");
        builder.AppendLine($"Dry:            {(double.IsNegativeInfinity(comp.DryDb) ? "-inf" : $"{comp.DryDb:0.0} dB")}");

        if (!string.IsNullOrWhiteSpace(comp.Label))
        {
            builder.AppendLine();
            builder.AppendLine($"Compressor note: {comp.Label}");
        }

        builder.AppendLine();
        builder.AppendLine("Sources");
        builder.AppendLine("-------");
        foreach (var (name, url) in preset.SourceNames.Zip(preset.SourceUrls))
        {
            builder.AppendLine($"{name}: {url}");
        }

        builder.AppendLine();
        builder.AppendLine("Suggested chain: ReaEQ before ReaComp.");
        return builder.ToString();
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
        var sampleLoaded = _loadedAudio is not null && _loadedAudio.Audio.Length > 0;
        var isPlaying = _activePlayback is not null;
        PlayOriginalButton.IsEnabled = sampleLoaded && !isPlaying && !_isBusy;
        PlayPresetButton.IsEnabled = sampleLoaded && !isPlaying && !_isBusy;
        StopButton.IsEnabled = isPlaying;
        PresetCombo.IsEnabled = !isPlaying && !_isBusy;
        LoadButton.IsEnabled = !isPlaying && !_isBusy;
    }

    private void ClearGraph()
    {
        _lastFrequencies = null;
        _lastOriginalDb = null;
        _lastProcessedDb = null;
        _smoothedOriginal = null;
        _smoothedProcessed = null;
        SpectrumCanvas.Invalidate();
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
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Older Windows builds simply fall back to the solid app background.
        }
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
        catch
        {
            // Window sizing is cosmetic; the app remains fully usable without it.
        }
    }

    private static float[] ExtractSegment(float[] audio, int start, int size)
    {
        var segment = new float[size];
        if (start >= audio.Length)
        {
            return segment;
        }

        var count = Math.Min(size, audio.Length - start);
        Array.Copy(audio, start, segment, 0, count);
        return segment;
    }

    private static double[] SmoothSpectrum(double[] values, ref double[]? previous)
    {
        if (previous is null || previous.Length != values.Length)
        {
            previous = values;
            return values;
        }

        var smoothed = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            smoothed[index] = previous[index] * 0.62 + values[index] * 0.38;
        }

        previous = smoothed;
        return smoothed;
    }

    private static float[] MatchLength(float[] audio, int targetLength)
    {
        if (audio.Length == targetLength)
        {
            return audio;
        }

        var output = new float[targetLength];
        Array.Copy(audio, output, Math.Min(audio.Length, targetLength));
        return output;
    }

    private static float MapFrequency(double frequency, float left, float width)
    {
        var logMin = Math.Log10(20.0);
        var logMax = Math.Log10(20000.0);
        var value = (Math.Log10(Math.Max(frequency, 20.0)) - logMin) / (logMax - logMin);
        return left + (float)Math.Clamp(value, 0.0, 1.0) * width;
    }

    private static float MapDb(double db, float top, float height)
    {
        const double minDb = -72.0;
        const double maxDb = 9.0;
        var value = (Math.Clamp(db, minDb, maxDb) - minDb) / (maxDb - minDb);
        return top + (1.0f - (float)value) * height;
    }

    private static string FormatFrequency(double frequency)
    {
        return frequency >= 1000.0 ? $"{frequency / 1000.0:0.#}k" : frequency.ToString("0");
    }

    private static string OnOff(bool value)
    {
        return value ? "On" : "Off";
    }
}
