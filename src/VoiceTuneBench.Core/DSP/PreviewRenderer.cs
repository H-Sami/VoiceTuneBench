using VoiceTuneBench.Core.Audio;
using VoiceTuneBench.Core.Presets;

namespace VoiceTuneBench.Core.DSP;

/// <summary>Renders a preview of how a preset's EQ and compressor settings will sound.</summary>
public static class PreviewRenderer
{
    /// <summary>Applies ReaEQ-style biquad filters to audio.</summary>
    public static float[] ApplyEqPreview(float[] audio, int sampleRate, IReadOnlyList<EqBand> bands)
    {
        return BiquadFilter.ApplyEq(audio, sampleRate, bands);
    }

    /// <summary>Applies ReaComp-style compressor preview: RMS detection, attack/release, makeup, limiting.</summary>
    public static float[] ApplyCompressorPreview(float[] audio, int sampleRate, CompressorSettings settings)
    {
        var dry = Sanitize(audio);
        if (dry.Length == 0)
        {
            return dry;
        }

        var detector = ApplyDetectorFilters(dry, sampleRate, settings);
        var lookahead = Math.Max(0, (int)Math.Round(settings.PrecompMs * sampleRate / 1000.0));
        if (lookahead > 0 && lookahead < detector.Length)
        {
            var shifted = new float[detector.Length];
            Array.Copy(detector, lookahead, shifted, 0, detector.Length - lookahead);
            detector = shifted;
        }

        var rms = MovingRms(detector, sampleRate, settings.RmsSizeMs);
        var reductionDb = new double[dry.Length];
        for (var index = 0; index < dry.Length; index++)
        {
            var levelDb = 20.0 * Math.Log10(rms[index] + 1e-10);
            reductionDb[index] = CompressionReductionDb(levelDb, settings);
        }

        var smoothedReduction = SmoothGainReduction(
            reductionDb,
            sampleRate,
            settings.AttackMs,
            settings.ReleaseMs);

        var wetGain = DbToGain(settings.WetDb);
        var dryGain = DbToGain(settings.DryDb);
        var output = new float[dry.Length];

        for (var index = 0; index < dry.Length; index++)
        {
            var gain = Math.Pow(10.0, (smoothedReduction[index] + settings.MakeupGainDb) / 20.0);
            var wet = dry[index] * gain;
            var sample = wet * wetGain + dry[index] * dryGain;
            output[index] = settings.LimitOutput
                ? (float)Math.Clamp(sample, -0.99, 0.99)
                : (float)sample;
        }

        return output;
    }

    /// <summary>Renders a full preset preview: EQ then compressor, capped at 45 seconds, peak-normalized.</summary>
    public static float[] RenderPresetPreview(float[] audio, int sampleRate, CuratedPreset preset)
    {
        var clean = Sanitize(audio);
        var maxSamples = (int)Math.Round(VoiceSampleLoader.PreviewMaxSeconds * sampleRate);
        if (clean.Length > maxSamples)
        {
            Array.Resize(ref clean, maxSamples);
        }

        var processed = ApplyEqPreview(clean, sampleRate, preset.EqBands);
        processed = ApplyCompressorPreview(processed, sampleRate, preset.Compressor);

        var peak = Peak(processed);
        if (peak > 0.99)
        {
            for (var index = 0; index < processed.Length; index++)
            {
                processed[index] = (float)(processed[index] / peak * 0.99);
            }
        }

        return processed;
    }

    private static float[] ApplyDetectorFilters(float[] audio, int sampleRate, CompressorSettings settings)
    {
        var detector = audio;
        var nyquist = sampleRate / 2.0;
        if (settings.DetectorHighpassHz >= 20.0 && settings.DetectorHighpassHz < nyquist * 0.95)
        {
            detector = BiquadFilter.Apply(detector, BiquadFilter.HighPass(sampleRate, settings.DetectorHighpassHz, 0.707));
        }

        if (settings.DetectorLowpassHz >= 20.0 && settings.DetectorLowpassHz < nyquist * 0.95)
        {
            detector = BiquadFilter.Apply(detector, BiquadFilter.LowPass(sampleRate, settings.DetectorLowpassHz, 0.707));
        }

        return detector;
    }

    private static double[] MovingRms(float[] audio, int sampleRate, double rmsSizeMs)
    {
        var rms = new double[audio.Length];
        if (audio.Length == 0)
        {
            return rms;
        }

        var window = Math.Clamp((int)Math.Round(rmsSizeMs * sampleRate / 1000.0), 1, audio.Length);
        var sum = 0.0;
        var squared = new double[audio.Length];

        for (var index = 0; index < audio.Length; index++)
        {
            squared[index] = audio[index] * audio[index];
            sum += squared[index];
            if (index >= window)
            {
                sum -= squared[index - window];
            }

            var denominator = Math.Min(index + 1, window);
            rms[index] = Math.Sqrt(Math.Max(sum / denominator, 1e-18));
        }

        return rms;
    }

    private static double CompressionReductionDb(double levelDb, CompressorSettings settings)
    {
        var threshold = settings.ThresholdDb;
        var ratio = Math.Max(settings.Ratio, 1.0);
        var knee = Math.Max(settings.KneeDb, 0.0);
        var over = levelDb - threshold;
        double compressedOver;

        if (knee <= 0.0)
        {
            compressedOver = over > 0.0 ? over / ratio : over;
        }
        else
        {
            var halfKnee = knee / 2.0;
            if (over <= -halfKnee)
            {
                compressedOver = over;
            }
            else if (over >= halfKnee)
            {
                compressedOver = over / ratio;
            }
            else
            {
                var x = over + halfKnee;
                compressedOver = over + ((1.0 / ratio - 1.0) * x * x) / (2.0 * knee);
            }
        }

        return Math.Min(compressedOver - over, 0.0);
    }

    private static double[] SmoothGainReduction(
        double[] reductionDb,
        int sampleRate,
        double attackMs,
        double releaseMs)
    {
        var attackCoeff = Math.Exp(-1.0 / Math.Max(1.0, attackMs * sampleRate / 1000.0));
        var releaseCoeff = Math.Exp(-1.0 / Math.Max(1.0, releaseMs * sampleRate / 1000.0));
        var smoothed = new double[reductionDb.Length];
        var current = 0.0;

        for (var index = 0; index < reductionDb.Length; index++)
        {
            var desired = reductionDb[index];
            var coeff = desired < current ? attackCoeff : releaseCoeff;
            current = coeff * current + (1.0 - coeff) * desired;
            smoothed[index] = current;
        }

        return smoothed;
    }

    private static double DbToGain(double db)
    {
        return double.IsNegativeInfinity(db) ? 0.0 : Math.Pow(10.0, db / 20.0);
    }

    private static float[] Sanitize(float[] audio)
    {
        var output = new float[audio.Length];
        for (var index = 0; index < audio.Length; index++)
        {
            output[index] = float.IsFinite(audio[index]) ? audio[index] : 0.0f;
        }

        return output;
    }

    private static double Peak(float[] audio)
    {
        var peak = 0.0;
        foreach (var sample in audio)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }
}
