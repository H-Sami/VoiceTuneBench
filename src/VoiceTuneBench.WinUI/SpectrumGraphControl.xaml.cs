using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Color = Windows.UI.Color;

namespace VoiceTuneBench.WinUI;

public sealed partial class SpectrumGraphControl : UserControl
{
    private double[]? _frequencies;
    private double[]? _originalDb;
    private double[]? _processedDb;
    private double[]? _smoothedOriginal;
    private double[]? _smoothedProcessed;
    private string _mode = "original";

    private const float LeftMargin = 48f;
    private const float TopMargin = 24f;
    private const float RightMargin = 18f;
    private const float BottomMargin = 34f;

    public SpectrumGraphControl()
    {
        InitializeComponent();
    }

    public void Reset()
    {
        _frequencies = null;
        _originalDb = null;
        _processedDb = null;
        _smoothedOriginal = null;
        _smoothedProcessed = null;
        Canvas.Invalidate();
    }

    public void SetSpectrumData(
        double[] frequencies,
        double[] originalDb,
        double[] processedDb,
        string mode)
    {
        _frequencies = frequencies;
        _mode = mode;
        _originalDb = Smooth(originalDb, ref _smoothedOriginal);
        _processedDb = Smooth(processedDb, ref _smoothedProcessed);
        Canvas.Invalidate();
    }

    public void UpdateFromPlayback(int index, float[] originalAudio, float[] processedAudio, int sampleRate)
    {
        const int windowSize = 8192;
        var start = Math.Max(0, index - windowSize / 2);
        var end = Math.Min(originalAudio.Length, start + windowSize);
        start = Math.Max(0, end - windowSize);

        var originalSeg = Extract(originalAudio, start, windowSize);
        var processedSeg = Extract(processedAudio, start, windowSize);

        var original = VoiceTuneBench.Core.Visualization.SpectrumAnalyzer.ComputeSpectrum(originalSeg, sampleRate);
        var processed = VoiceTuneBench.Core.Visualization.SpectrumAnalyzer.ComputeSpectrum(processedSeg, sampleRate);

        if (original.Frequencies.Length == 0 || processed.LevelsDb.Length != original.LevelsDb.Length)
        {
            return;
        }

        var reference = Math.Max(original.LevelsDb.Max(), -120.0);
        var origRel = original.LevelsDb.Select(v => Math.Clamp(v - reference, -72.0, 9.0)).ToArray();
        var procRel = processed.LevelsDb.Select(v => Math.Clamp(v - reference, -72.0, 9.0)).ToArray();

        SetSpectrumData(original.Frequencies, origRel, procRel, _mode);
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var session = args.DrawingSession;
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;

        session.Clear(ThemeColors.GraphBackground);

        var plotWidth = Math.Max(1f, width - LeftMargin - RightMargin);
        var plotHeight = Math.Max(1f, height - TopMargin - BottomMargin);

        DrawGrid(session, LeftMargin, TopMargin, plotWidth, plotHeight);

        if (_frequencies == null || _originalDb == null || _processedDb == null)
        {
            session.DrawText(
                "Playback spectrum will appear here",
                LeftMargin + plotWidth / 2f - 108f,
                TopMargin + plotHeight / 2f - 10f,
                ThemeColors.EmptyGraphText);
            return;
        }

        var origWidth = _mode == "original" ? 3.0f : 1.8f;
        var procWidth = _mode == "preset" ? 3.0f : 1.8f;

        DrawSpectrum(session, _frequencies, _originalDb, LeftMargin, TopMargin, plotWidth, plotHeight, ThemeColors.OriginalBlue, origWidth);
        DrawSpectrum(session, _frequencies, _processedDb, LeftMargin, TopMargin, plotWidth, plotHeight, ThemeColors.PresetGreen, procWidth);

        session.DrawText("Original", LeftMargin + 12f, TopMargin + 10f, ThemeColors.OriginalBlue);
        session.DrawText("With preset", LeftMargin + 92f, TopMargin + 10f, ThemeColors.PresetGreen);
    }

    private static void DrawGrid(CanvasDrawingSession session, float left, float top, float width, float height)
    {
        foreach (var db in new[] { -72.0, -54.0, -36.0, -18.0, 0.0 })
        {
            var y = MapDb(db, top, height);
            session.DrawLine(left, y, left + width, y, ThemeColors.GridLine, 1.0f);
            session.DrawText(db.ToString("0"), 6f, y - 9f, ThemeColors.AxisLabel);
        }

        foreach (var freq in new[] { 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0, 2000.0, 5000.0, 10000.0, 20000.0 })
        {
            var x = MapFrequency(freq, left, width);
            session.DrawLine(x, top, x, top + height, ThemeColors.GridLine, 1.0f);
            session.DrawText(FormatFreq(freq), x - 10f, top + height + 8f, ThemeColors.AxisLabel);
        }
    }

    private static void DrawSpectrum(CanvasDrawingSession session, double[] freqs, double[] levelsDb, float left, float top, float width, float height, Color color, float strokeWidth)
    {
        var count = Math.Min(freqs.Length, levelsDb.Length);
        if (count < 2) return;

        var prevX = MapFrequency(freqs[0], left, width);
        var prevY = MapDb(levelsDb[0], top, height);
        for (var i = 1; i < count; i++)
        {
            var x = MapFrequency(freqs[i], left, width);
            var y = MapDb(levelsDb[i], top, height);
            session.DrawLine(prevX, prevY, x, y, color, strokeWidth);
            prevX = x;
            prevY = y;
        }
    }

    private static float MapFrequency(double freq, float left, float width)
    {
        var logMin = Math.Log10(20.0);
        var logMax = Math.Log10(20000.0);
        var value = (Math.Log10(Math.Max(freq, 20.0)) - logMin) / (logMax - logMin);
        return left + (float)Math.Clamp(value, 0.0, 1.0) * width;
    }

    private static float MapDb(double db, float top, float height)
    {
        const double minDb = -72.0, maxDb = 9.0;
        var value = (Math.Clamp(db, minDb, maxDb) - minDb) / (maxDb - minDb);
        return top + (1f - (float)value) * height;
    }

    private static string FormatFreq(double f) => f >= 1000.0 ? $"{f / 1000.0:0.#}k" : f.ToString("0");

    private static float[] Extract(float[] audio, int start, int size)
    {
        var result = new float[size];
        if (start >= audio.Length) return result;
        var count = Math.Min(size, audio.Length - start);
        Array.Copy(audio, start, result, 0, count);
        return result;
    }

    private static double[] Smooth(double[] values, ref double[]? previous)
    {
        if (previous == null || previous.Length != values.Length)
        {
            previous = values;
            return values;
        }

        var smoothed = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            smoothed[i] = previous[i] * 0.62 + values[i] * 0.38;
        }

        previous = smoothed;
        return smoothed;
    }
}