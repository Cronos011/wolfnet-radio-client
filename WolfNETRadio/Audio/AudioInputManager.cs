using System.Collections.Generic;
using NAudio.Wave;

namespace WolfNETRadio.Audio;

public class AudioInputManager : IDisposable
{
    private WaveInEvent? _waveIn;
    private readonly List<float> _buffer = [];
    private const int FRAME_SAMPLES = OpusProcessor.FRAME_SAMPLES;

    public event Action<float[]>? FrameReady; // fires with 960-sample frames
    public event Action<float>? VuLevel;       // 0.0-1.0 RMS level

    public IEnumerable<string> GetInputDevices()
    {
        var devices = new List<string>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            devices.Add(WaveIn.GetCapabilities(i).ProductName);
        }

        return devices;
    }

    public void StartCapture(string? deviceName = null, float volumeMultiplier = 1.0f)
    {
        StopCapture();
        _waveIn?.Dispose();
        _waveIn = null;

        var deviceIndex = -1;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {
                var name = WaveIn.GetCapabilities(i).ProductName;
                if (string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    deviceIndex = i;
                    break;
                }
            }
        }

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceIndex,
            WaveFormat = new WaveFormat(OpusProcessor.SAMPLE_RATE, 16, OpusProcessor.CHANNELS),
            BufferMilliseconds = 60
        };

        _waveIn.DataAvailable += (_, e) =>
        {
            var samples = e.BytesRecorded / 2;
            if (samples <= 0)
                return;

            var floats = new float[samples];
            var sumSquares = 0.0f;
            for (var i = 0; i < samples; i++)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i * 2);
                var normalized = (sample / 32767.0f) * volumeMultiplier;
                normalized = Math.Clamp(normalized, -1.0f, 1.0f);
                floats[i] = normalized;
                sumSquares += normalized * normalized;
            }

            var rms = MathF.Sqrt(sumSquares / samples);
            VuLevel?.Invoke(Math.Clamp(rms, 0.0f, 1.0f));

            _buffer.AddRange(floats);
            while (_buffer.Count >= FRAME_SAMPLES)
            {
                var frame = _buffer.GetRange(0, FRAME_SAMPLES).ToArray();
                _buffer.RemoveRange(0, FRAME_SAMPLES);
                FrameReady?.Invoke(frame);
            }
        };

        _waveIn.StartRecording();
    }

    public void StopCapture()
    {
        _waveIn?.StopRecording();
    }

    public void Dispose() { _waveIn?.Dispose(); }
}
