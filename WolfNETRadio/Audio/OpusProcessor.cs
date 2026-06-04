using Concentus.Enums;
using Concentus.Structs;
using Concentus;

namespace WolfNETRadio.Audio;

public class OpusProcessor : IDisposable
{
    public const int SAMPLE_RATE = 16000;
    public const int CHANNELS = 1;
    public const int FRAME_SAMPLES = 960; // 60ms at 16kHz

    private const int MAX_PACKET_SIZE = 4000;

    private readonly OpusEncoder _encoder;
    private readonly OpusDecoder _decoder;

    public OpusProcessor()
    {
        _encoder = new OpusEncoder(SAMPLE_RATE, CHANNELS, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = 16000;
        _decoder = new OpusDecoder(SAMPLE_RATE, CHANNELS);
    }

    /// Encode PCM float samples to Opus bytes
    public byte[] Encode(float[] pcm)
    {
        if (pcm.Length == 0)
            return [];

        var frameSize = Math.Min(pcm.Length, FRAME_SAMPLES);
        var pcmShorts = new short[frameSize];
        for (var i = 0; i < frameSize; i++)
        {
            var clamped = Math.Clamp(pcm[i], -1.0f, 1.0f);
            pcmShorts[i] = (short)Math.Round(clamped * 32767.0f);
        }

        var output = new byte[MAX_PACKET_SIZE];
        var encoded = _encoder.Encode(pcmShorts, 0, frameSize, output, 0, output.Length);
        if (encoded <= 0)
            return [];

        var trimmed = new byte[encoded];
        Array.Copy(output, trimmed, encoded);
        return trimmed;
    }

    /// Decode Opus bytes to PCM float samples
    public float[] Decode(byte[] opus)
    {
        if (opus.Length == 0)
            return [];

        var pcmShorts = new short[FRAME_SAMPLES];
        var decoded = _decoder.Decode(opus, 0, opus.Length, pcmShorts, 0, FRAME_SAMPLES, false);
        if (decoded <= 0)
            return [];

        var pcm = new float[decoded];
        for (var i = 0; i < decoded; i++)
        {
            pcm[i] = pcmShorts[i] / 32767.0f;
        }

        return pcm;
    }

    public void Dispose() { /* encoders are not IDisposable in Concentus 2.x */ }
}
