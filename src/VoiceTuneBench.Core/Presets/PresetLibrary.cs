namespace VoiceTuneBench.Core.Presets;

/// <summary>Provides the six curated speech/podcast presets and their source attribution links.</summary>
public static class PresetLibrary
{
    /// <summary>Maps preset source names to their public reference URLs.</summary>
    public static readonly IReadOnlyDictionary<string, string> SourceLinks = new Dictionary<string, string>
    {
        ["PodRewind EQ Guide"] = "https://podrewind.com/blog/podcast-eq-settings-guide",
        ["PodRewind Compression Guide"] = "https://podrewind.com/blog/podcast-compression-best-practices",
        ["Music Guy Mixing Voice EQ"] = "https://www.musicguymixing.com/voice-eq/",
        ["Voiceovers.com Compression Guide"] = "https://www.voiceovers.com/blog/best-compression-settings-for-voice-over",
        ["ReaComp Accessibility Wiki"] = "https://reaperaccessibility.com/index.php/The_fast_and_easy_way_to_understand_compression_and_reaComp_accessibility",
        ["Podcast Audio Quality Tips"] = "https://mp3-ai.com/blog/podcast-audio-quality-tips/",
        ["DeckReady Podcast Optimization"] = "https://deckready.club/ja/blog/podcast-audio-optimization-en",
        ["StreamGeeks Podcast EQ"] = "https://streamgeeks.us/how-to-eq-a-podcast/",
    };

    /// <summary>Returns all six curated presets.</summary>
    public static IReadOnlyList<CuratedPreset> GetCuratedPresets() => Presets;

    /// <summary>Returns the preset with the given slug, throwing if not found.</summary>
    /// <exception cref="KeyNotFoundException">Thrown when no preset matches the slug.</exception>
    public static CuratedPreset GetCuratedPreset(string slug)
    {
        return Presets.FirstOrDefault(preset => preset.Slug == slug)
            ?? throw new KeyNotFoundException($"Unknown curated preset: {slug}");
    }

    private static CompressorSettings Comp(
        double threshold,
        double ratio,
        double attack,
        double release,
        double knee,
        double makeup,
        double rms = 10.0,
        double detectorHp = 80.0,
        string label = "")
    {
        return new CompressorSettings(
            ThresholdDb: threshold,
            Ratio: ratio,
            AttackMs: attack,
            ReleaseMs: release,
            KneeDb: knee,
            MakeupGainDb: makeup,
            PrecompMs: 0.0,
            ClassicAttack: false,
            AutoRelease: false,
            DetectorInput: "Main Input",
            DetectorLowpassHz: 20000.0,
            DetectorHighpassHz: detectorHp,
            RmsSizeMs: rms,
            AA: "4x",
            LimitOutput: true,
            AutoMakeup: false,
            PreviewFilter: false,
            WetDb: -0.0,
            DryDb: double.NegativeInfinity,
            Label: label);
    }

    private static readonly CuratedPreset[] Presets =
    [
        new(
            Slug: "clean-podcast",
            DisplayName: "Clean Podcast",
            Summary: "A low-risk cleanup preset that keeps the voice natural.",
            BestFor: "Most podcast, chat, and voiceover recordings that already sound decent.",
            EqBands:
            [
                new(EqBandType.HighPass, 85.0, 0.0, 0.707, Label: "Removes rumble while preserving normal voice weight."),
                new(EqBandType.Band, 285.0, -2.0, 1.25, Label: "Gentle mud cleanup in the common 200-400Hz problem area."),
                new(EqBandType.Band, 420.0, -1.0, 1.10, Label: "Small boxiness cut for close desk recordings."),
                new(EqBandType.Band, 3200.0, 1.6, 0.90, Label: "Broad presence lift for clearer words."),
                new(EqBandType.HighShelf, 11000.0, -0.8, 0.70, Label: "Keeps top-end brightness from getting brittle."),
            ],
            Compressor: Comp(
                -18.0, 2.8, 12.0, 140.0, 3.5, 2.5,
                label: "Natural podcast compression, intended for roughly 3-5dB gain reduction."),
            SourceNames:
            [
                "PodRewind EQ Guide",
                "PodRewind Compression Guide",
                "Podcast Audio Quality Tips",
            ]),
        new(
            Slug: "clear-dialogue",
            DisplayName: "Clear Dialogue",
            Summary: "More articulation without pushing the top end too hard.",
            BestFor: "Darker voices, dynamic microphones, or recordings that feel slightly veiled.",
            EqBands:
            [
                new(EqBandType.HighPass, 90.0, 0.0, 0.707, Label: "Standard speech high-pass for rumble control."),
                new(EqBandType.Band, 250.0, -2.2, 1.20, Label: "Reduces proximity boom and low-mid buildup."),
                new(EqBandType.Band, 850.0, -1.2, 1.20, Label: "Tames nasal/honky buildup without hollowing the voice."),
                new(EqBandType.Band, 3600.0, 2.1, 0.85, Label: "Presence lift for consonants and intelligibility."),
                new(EqBandType.Band, 6800.0, -1.2, 1.80, Label: "Light sibilance safety cut."),
                new(EqBandType.HighShelf, 10500.0, 0.6, 0.70, Label: "Tiny air lift for dull recordings."),
            ],
            Compressor: Comp(
                -18.0, 3.2, 10.0, 125.0, 3.5, 3.0,
                label: "Clear but still natural speech compression."),
            SourceNames:
            [
                "PodRewind EQ Guide",
                "Podcast Audio Quality Tips",
                "DeckReady Podcast Optimization",
            ]),
        new(
            Slug: "broadcast-warmth",
            DisplayName: "Broadcast Warmth",
            Summary: "Fuller, smoother speech without the fake radio scoop.",
            BestFor: "Thin voices, small capsules, or recordings that need more body.",
            EqBands:
            [
                new(EqBandType.HighPass, 75.0, 0.0, 0.707, Label: "Lower high-pass keeps natural low voice weight."),
                new(EqBandType.LowShelf, 175.0, 1.0, 0.70, Label: "Subtle warmth for thin sources."),
                new(EqBandType.Band, 360.0, -1.8, 1.10, Label: "Prevents the added body from turning boxy."),
                new(EqBandType.Band, 2700.0, 1.1, 0.90, Label: "Gentle intelligibility lift."),
                new(EqBandType.HighShelf, 9500.0, -1.2, 0.70, Label: "Smoother long-listening top end."),
            ],
            Compressor: Comp(
                -20.0, 3.0, 18.0, 165.0, 4.0, 3.0,
                label: "Smoother compression for warm dialogue."),
            SourceNames:
            [
                "PodRewind EQ Guide",
                "Music Guy Mixing Voice EQ",
                "DeckReady Podcast Optimization",
            ]),
        new(
            Slug: "bright-mic-control",
            DisplayName: "Bright Mic Control",
            Summary: "Tames sharp condensers, sibilance, and fatiguing room edge.",
            BestFor: "Bright condenser mics, reflective rooms, and sharp S/T sounds.",
            EqBands:
            [
                new(EqBandType.HighPass, 100.0, 0.0, 0.707, Label: "Higher high-pass for sensitive mics and desk rumble."),
                new(EqBandType.Band, 320.0, -2.0, 1.20, Label: "Cuts boxiness if the room is audible."),
                new(EqBandType.Band, 5400.0, -2.4, 1.70, Label: "Controls harsh presence and sibilant bite."),
                new(EqBandType.HighShelf, 8800.0, -2.0, 0.70, Label: "Broad high-frequency fatigue reduction."),
                new(EqBandType.LowPass, 15000.0, 0.0, 0.707, Label: "Gentle high cut for hiss-prone bright recordings."),
            ],
            Compressor: Comp(
                -16.0, 2.5, 20.0, 160.0, 4.0, 1.8,
                detectorHp: 100.0,
                label: "Lighter compression so bright mics do not jump forward too hard."),
            SourceNames:
            [
                "PodRewind EQ Guide",
                "Music Guy Mixing Voice EQ",
                "Voiceovers.com Compression Guide",
            ]),
        new(
            Slug: "dynamic-mic-lift",
            DisplayName: "Dynamic Mic Lift",
            Summary: "Adds level and intelligibility for quieter dynamic microphones.",
            BestFor: "SM58-style, broadcast dynamic, or low-output USB/XLR dynamic mics.",
            EqBands:
            [
                new(EqBandType.HighPass, 80.0, 0.0, 0.707, Label: "Rumble cleanup while preserving voice weight."),
                new(EqBandType.Band, 250.0, -1.8, 1.15, Label: "Reduces proximity mud common on close dynamics."),
                new(EqBandType.Band, 3800.0, 2.3, 0.85, Label: "Presence lift for speech clarity."),
                new(EqBandType.Band, 6600.0, 0.8, 1.00, Label: "Small consonant lift without making S sounds harsh."),
                new(EqBandType.HighShelf, 10500.0, 0.6, 0.70, Label: "Tiny top lift for darker dynamics."),
            ],
            Compressor: Comp(
                -24.0, 3.4, 18.0, 180.0, 3.5, 4.5,
                label: "Quiet-speaker compression with conservative manual makeup."),
            SourceNames:
            [
                "PodRewind EQ Guide",
                "Podcast Audio Quality Tips",
                "ReaComp Accessibility Wiki",
            ]),
        new(
            Slug: "room-cleanup",
            DisplayName: "Room Cleanup",
            Summary: "Reduces room mud and fatigue without making speech thin.",
            BestFor: "Desk setups, reflective bedrooms, and close-talk chat recordings.",
            EqBands:
            [
                new(EqBandType.HighPass, 105.0, 0.0, 0.707, Label: "Higher high-pass helps rumble and plosives."),
                new(EqBandType.Band, 230.0, -3.0, 1.15, Label: "Cuts boom and room thickness."),
                new(EqBandType.Band, 520.0, -2.1, 1.05, Label: "Reduces boxy reflections."),
                new(EqBandType.Band, 1200.0, -1.0, 1.20, Label: "Small hollow-room control."),
                new(EqBandType.Band, 3300.0, 1.3, 0.90, Label: "Restores clarity after cleanup cuts."),
                new(EqBandType.HighShelf, 10000.0, -1.5, 0.70, Label: "Avoids lifting room hiss and harshness."),
            ],
            Compressor: Comp(
                -18.0, 2.8, 18.0, 175.0, 4.0, 2.2,
                detectorHp: 100.0,
                label: "Moderate compression that avoids dragging room tone forward too much."),
            SourceNames:
            [
                "PodRewind EQ Guide",
                "StreamGeeks Podcast EQ",
                "DeckReady Podcast Optimization",
            ]),
    ];
}
