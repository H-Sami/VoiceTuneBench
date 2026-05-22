using NAudio.Wave;

namespace VoiceTuneBench.WinUI.Playback;

internal sealed class FloatArrayWaveProvider : IWaveProvider
{
    private readonly byte[] _bytes;
    private int _position;

    public FloatArrayWaveProvider(float[] audio, int sampleRate)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels: 1);
        _bytes = new byte[audio.Length * sizeof(float)];
        Buffer.BlockCopy(audio, 0, _bytes, 0, _bytes.Length);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var available = _bytes.Length - _position;
        if (available <= 0)
        {
            return 0;
        }

        var bytesToCopy = Math.Min(available, count);
        Buffer.BlockCopy(_bytes, _position, buffer, offset, bytesToCopy);
        _position += bytesToCopy;
        return bytesToCopy;
    }
}
