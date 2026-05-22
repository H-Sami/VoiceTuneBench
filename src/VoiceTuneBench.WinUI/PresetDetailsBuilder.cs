using VoiceTuneBench.Core.Presets;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text;

namespace VoiceTuneBench.WinUI;

public static class PresetDetailsBuilder
{
    public static UIElement Build(CuratedPreset preset)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(BuildOverview(preset));
        panel.Children.Add(BuildEq(preset));
        panel.Children.Add(BuildCompressor(preset.Compressor));
        panel.Children.Add(BuildSources(preset));
        return panel;
    }

    public static string FormatPlainText(CuratedPreset preset)
    {
        var b = new StringBuilder();
        b.AppendLine(preset.DisplayName);
        b.AppendLine(new string('=', preset.DisplayName.Length));
        b.AppendLine();
        b.AppendLine($"Best for: {preset.BestFor}");
        b.AppendLine($"Tone:     {preset.Summary}");
        b.AppendLine();
        b.AppendLine("ReaEQ");
        b.AppendLine("-----");

        for (var i = 0; i < preset.EqBands.Count; i++)
        {
            var band = preset.EqBands[i];
            var setting = band.BandType is EqBandType.HighPass or EqBandType.LowPass
                ? $"{band.Frequency:0} Hz, Q {band.Q:0.00}"
                : $"{band.Frequency:0} Hz, {band.GainDb:+0.0;-0.0;0.0} dB, Q {band.Q:0.00}";
            b.AppendLine($"{i + 1:00}. {band.FilterName}: {setting}");
            if (!string.IsNullOrWhiteSpace(band.Label))
            {
                b.AppendLine($"    {band.Label}");
            }
        }

        b.AppendLine();
        b.AppendLine("ReaComp");
        b.AppendLine("-------");
        var c = preset.Compressor;
        b.AppendLine($"Threshold:      {c.ThresholdDb:0.0} dBFS");
        b.AppendLine($"Ratio:          {c.Ratio:0.0}:1");
        b.AppendLine($"Attack:         {c.AttackMs:0} ms");
        b.AppendLine($"Release:        {c.ReleaseMs:0} ms");
        b.AppendLine($"Knee size:      {c.KneeDb:0.0} dB");
        b.AppendLine($"RMS size:       {c.RmsSizeMs:0} ms");
        b.AppendLine($"Pre-comp:       {c.PrecompMs:0} ms");
        b.AppendLine($"Classic attack: {OnOff(c.ClassicAttack)}");
        b.AppendLine($"Auto release:   {OnOff(c.AutoRelease)}");
        b.AppendLine($"Detector input: {c.DetectorInput}");
        b.AppendLine($"Detector HP:    {c.DetectorHighpassHz:0} Hz");
        b.AppendLine($"Detector LP:    {c.DetectorLowpassHz:0} Hz");
        b.AppendLine($"AA:             {c.AA}");
        b.AppendLine($"Limit output:   {OnOff(c.LimitOutput)}");
        b.AppendLine($"Auto makeup:    {OnOff(c.AutoMakeup)}");
        b.AppendLine($"Manual makeup:  +{c.MakeupGainDb:0.0} dB");
        b.AppendLine($"Preview filter: {OnOff(c.PreviewFilter)}");
        b.AppendLine($"Wet:            {c.WetDb:0.0} dB");
        b.AppendLine($"Dry:            {(double.IsNegativeInfinity(c.DryDb) ? "-inf" : $"{c.DryDb:0.0} dB")}");

        if (!string.IsNullOrWhiteSpace(c.Label))
        {
            b.AppendLine();
            b.AppendLine($"Compressor note: {c.Label}");
        }

        b.AppendLine();
        b.AppendLine("Sources");
        b.AppendLine("-------");
        foreach (var (name, url) in preset.SourceNames.Zip(preset.SourceUrls))
        {
            b.AppendLine($"{name}: {url}");
        }

        b.AppendLine();
        b.AppendLine("Suggested chain: ReaEQ before ReaComp.");
        return b.ToString();
    }

    private static UIElement BuildOverview(CuratedPreset preset)
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
        content.Children.Add(DetailLine("Best for", preset.BestFor));
        content.Children.Add(DetailLine("Tone", preset.Summary));
        content.Children.Add(DetailLine("Chain", "ReaEQ before ReaComp."));
        return Section("Overview", content);
    }

    private static UIElement BuildEq(CuratedPreset preset)
    {
        var rows = new StackPanel { Spacing = 8 };
        for (var i = 0; i < preset.EqBands.Count; i++)
        {
            rows.Children.Add(BuildEqBandRow(i + 1, preset.EqBands[i]));
        }
        return Section("ReaEQ", rows);
    }

    private static UIElement BuildCompressor(CompressorSettings comp)
    {
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
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

        for (var i = 0; i < items.Length; i++)
        {
            var cell = SettingCell(items[i].Label, items[i].Value);
            Grid.SetRow(cell, i / 2);
            Grid.SetColumn(cell, i % 2);
            grid.Children.Add(cell);
        }

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(grid);
        if (!string.IsNullOrWhiteSpace(comp.Label))
        {
            content.Children.Add(NoteText(comp.Label));
        }

        return Section("ReaComp", content);
    }

    private static UIElement BuildSources(CuratedPreset preset)
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
        return Section("Sources", content);
    }

    private static UIElement Section(string title, UIElement content)
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppBrush("VoiceTuneBenchMutedTextBrush"),
        });
        panel.Children.Add(content);
        return panel;
    }

    private static UIElement DetailLine(string label, string value)
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

    private static UIElement BuildEqBandRow(int index, EqBand band)
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
            Text = EqSettingText(band),
            Foreground = AppBrush("VoiceTuneBenchMutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(band.Label))
        {
            content.Children.Add(NoteText(band.Label));
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

    private static Border SettingCell(string label, string value)
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

    private static TextBlock NoteText(string text) => new()
    {
        Text = text,
        Foreground = AppBrush("VoiceTuneBenchMutedTextBrush"),
        TextWrapping = TextWrapping.Wrap,
    };

    private static string EqSettingText(EqBand band) =>
        band.BandType is EqBandType.HighPass or EqBandType.LowPass
            ? $"{band.Frequency:0} Hz, Q {band.Q:0.00}"
            : $"{band.Frequency:0} Hz, {band.GainDb:+0.0;-0.0;0.0} dB, Q {band.Q:0.00}";

    private static Brush AppBrush(string key) =>
        (Brush)Application.Current.Resources[key];

    private static string OnOff(bool value) => value ? "On" : "Off";
}