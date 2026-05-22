namespace VoiceTuneBench.Core.Audio;

/// <summary>
/// Audio that has been decoded, downmixed to mono, resampled to 48kHz,
/// DC-offset corrected, and validated for silence and minimum duration.
/// </summary>
/// <param name="Audio">Mono float samples at 48kHz.</param>
/// <param name="SampleRate">Always 48000.</param>
/// <param name="DurationSeconds">Actual duration in seconds.</param>
/// <param name="Decoder">Describes how the audio was decoded (e.g. "WAV" or "NAudio / Media Foundation").</param>
public sealed record LoadedAudio(
    float[] Audio,
    int SampleRate,
    double DurationSeconds,
    string Decoder);
