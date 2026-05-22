namespace VoiceTuneBench.Core.Audio;

public sealed record LoadedAudio(
    float[] Audio,
    int SampleRate,
    double DurationSeconds,
    string Decoder);
