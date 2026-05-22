using VoiceTuneBench.Core.Presets;

namespace VoiceTuneBench.Core.DSP;

internal readonly record struct BiquadCoefficients(
    double B0,
    double B1,
    double B2,
    double A1,
    double A2);

internal static class BiquadFilter
{
    public static float[] ApplyEq(float[] audio, int sampleRate, IReadOnlyList<EqBand> bands)
    {
        var processed = Sanitize(audio);
        var nyquist = sampleRate / 2.0;

        foreach (var band in bands.Where(band => band.Enabled))
        {
            var frequency = Math.Clamp(band.Frequency, 20.0, nyquist * 0.95);
            var q = Math.Clamp(band.Q, 0.2, 10.0);

            processed = band.BandType switch
            {
                EqBandType.HighPass => Apply(processed, HighPass(sampleRate, frequency, q)),
                EqBandType.LowPass => Apply(processed, LowPass(sampleRate, frequency, q)),
                EqBandType.Band when Math.Abs(band.GainDb) > 0.01 => Apply(processed, Peaking(sampleRate, frequency, band.GainDb, q)),
                EqBandType.LowShelf when Math.Abs(band.GainDb) > 0.01 => Apply(processed, Shelf(sampleRate, frequency, band.GainDb, q, high: false)),
                EqBandType.HighShelf when Math.Abs(band.GainDb) > 0.01 => Apply(processed, Shelf(sampleRate, frequency, band.GainDb, q, high: true)),
                _ => processed,
            };
        }

        return processed;
    }

    public static float[] Apply(float[] audio, BiquadCoefficients coefficients)
    {
        var output = new float[audio.Length];
        var x1 = 0.0;
        var x2 = 0.0;
        var y1 = 0.0;
        var y2 = 0.0;

        for (var index = 0; index < audio.Length; index++)
        {
            var x0 = Sanitize(audio[index]);
            var y0 = coefficients.B0 * x0
                + coefficients.B1 * x1
                + coefficients.B2 * x2
                - coefficients.A1 * y1
                - coefficients.A2 * y2;

            output[index] = (float)y0;
            x2 = x1;
            x1 = x0;
            y2 = y1;
            y1 = y0;
        }

        return output;
    }

    public static BiquadCoefficients HighPass(int sampleRate, double frequency, double q)
    {
        var omega = 2.0 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(omega) / (2.0 * q);
        var cos = Math.Cos(omega);

        return Normalize(
            (1.0 + cos) / 2.0,
            -(1.0 + cos),
            (1.0 + cos) / 2.0,
            1.0 + alpha,
            -2.0 * cos,
            1.0 - alpha);
    }

    public static BiquadCoefficients LowPass(int sampleRate, double frequency, double q)
    {
        var omega = 2.0 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(omega) / (2.0 * q);
        var cos = Math.Cos(omega);

        return Normalize(
            (1.0 - cos) / 2.0,
            1.0 - cos,
            (1.0 - cos) / 2.0,
            1.0 + alpha,
            -2.0 * cos,
            1.0 - alpha);
    }

    private static BiquadCoefficients Peaking(int sampleRate, double frequency, double gainDb, double q)
    {
        var a = Math.Pow(10.0, gainDb / 40.0);
        var omega = 2.0 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(omega) / (2.0 * q);
        var cos = Math.Cos(omega);

        return Normalize(
            1.0 + alpha * a,
            -2.0 * cos,
            1.0 - alpha * a,
            1.0 + alpha / a,
            -2.0 * cos,
            1.0 - alpha / a);
    }

    private static BiquadCoefficients Shelf(int sampleRate, double frequency, double gainDb, double q, bool high)
    {
        var a = Math.Pow(10.0, gainDb / 40.0);
        var omega = 2.0 * Math.PI * frequency / sampleRate;
        var sin = Math.Sin(omega);
        var cos = Math.Cos(omega);
        var alpha = sin / (2.0 * q);
        var beta = 2.0 * Math.Sqrt(a) * alpha;

        if (high)
        {
            return Normalize(
                a * ((a + 1.0) + (a - 1.0) * cos + beta),
                -2.0 * a * ((a - 1.0) + (a + 1.0) * cos),
                a * ((a + 1.0) + (a - 1.0) * cos - beta),
                (a + 1.0) - (a - 1.0) * cos + beta,
                2.0 * ((a - 1.0) - (a + 1.0) * cos),
                (a + 1.0) - (a - 1.0) * cos - beta);
        }

        return Normalize(
            a * ((a + 1.0) - (a - 1.0) * cos + beta),
            2.0 * a * ((a - 1.0) - (a + 1.0) * cos),
            a * ((a + 1.0) - (a - 1.0) * cos - beta),
            (a + 1.0) + (a - 1.0) * cos + beta,
            -2.0 * ((a - 1.0) + (a + 1.0) * cos),
            (a + 1.0) + (a - 1.0) * cos - beta);
    }

    private static BiquadCoefficients Normalize(
        double b0,
        double b1,
        double b2,
        double a0,
        double a1,
        double a2)
    {
        return new BiquadCoefficients(
            B0: b0 / a0,
            B1: b1 / a0,
            B2: b2 / a0,
            A1: a1 / a0,
            A2: a2 / a0);
    }

    private static float[] Sanitize(float[] audio)
    {
        var output = new float[audio.Length];
        for (var index = 0; index < audio.Length; index++)
        {
            output[index] = Sanitize(audio[index]);
        }

        return output;
    }

    private static float Sanitize(float sample)
    {
        return float.IsFinite(sample) ? sample : 0.0f;
    }
}
