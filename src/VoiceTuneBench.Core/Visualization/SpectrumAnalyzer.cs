using System.Numerics;

namespace VoiceTuneBench.Core.Visualization;

/// <summary>Computes a short-time Fourier transform (STFT) magnitude spectrum.</summary>
public static class SpectrumAnalyzer
{
    /// <summary>Computes FFT magnitude spectrum for the given audio segment.</summary>
    public static SpectrumFrame ComputeSpectrum(
        float[] audio,
        int sampleRate,
        double minHz = 20.0,
        double maxHz = 20000.0)
    {
        if (audio.Length == 0 || sampleRate <= 0)
        {
            return new SpectrumFrame([], []);
        }

        var fftSize = PreviousPowerOfTwo(audio.Length);
        if (fftSize < 2)
        {
            return new SpectrumFrame([], []);
        }

        var bins = new Complex[fftSize];
        var windowSum = 0.0;
        for (var index = 0; index < fftSize; index++)
        {
            var window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * index / (fftSize - 1));
            windowSum += window;
            var sample = float.IsFinite(audio[index]) ? audio[index] : 0.0f;
            bins[index] = new Complex(sample * window, 0.0);
        }

        FastFourierTransform(bins);

        var frequencyStep = sampleRate / (double)fftSize;
        var upperHz = Math.Min(maxHz, sampleRate / 2.0);
        var scale = Math.Max(windowSum / 2.0, 1e-9);
        var frequencies = new List<double>();
        var levels = new List<double>();

        for (var bin = 0; bin <= fftSize / 2; bin++)
        {
            var frequency = bin * frequencyStep;
            if (frequency < minHz || frequency > upperHz)
            {
                continue;
            }

            frequencies.Add(frequency);
            levels.Add(20.0 * Math.Log10((bins[bin].Magnitude / scale) + 1e-10));
        }

        return new SpectrumFrame(frequencies.ToArray(), levels.ToArray());
    }

    private static void FastFourierTransform(Complex[] values)
    {
        var count = values.Length;
        for (int index = 1, swap = 0; index < count; index++)
        {
            var bit = count >> 1;
            for (; (swap & bit) != 0; bit >>= 1)
            {
                swap ^= bit;
            }

            swap ^= bit;
            if (index < swap)
            {
                (values[index], values[swap]) = (values[swap], values[index]);
            }
        }

        for (var length = 2; length <= count; length <<= 1)
        {
            var angle = -2.0 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var start = 0; start < count; start += length)
            {
                var factor = Complex.One;
                var halfLength = length / 2;
                for (var offset = 0; offset < halfLength; offset++)
                {
                    var even = values[start + offset];
                    var odd = values[start + offset + halfLength] * factor;
                    values[start + offset] = even + odd;
                    values[start + offset + halfLength] = even - odd;
                    factor *= step;
                }
            }
        }
    }

    private static int PreviousPowerOfTwo(int value)
    {
        var power = 1;
        while (power <= value / 2)
        {
            power <<= 1;
        }

        return power;
    }
}
