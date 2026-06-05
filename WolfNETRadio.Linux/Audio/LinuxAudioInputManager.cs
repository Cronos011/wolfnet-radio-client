using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PortAudioSharp;

namespace WolfNETRadio.Audio;

/// <summary>
/// Linux audio capture via PortAudio (ALSA/PipeWire/PulseAudio — whatever the user's system uses).
/// PortAudio auto-detects the best available backend on Linux.
/// </summary>
public class LinuxAudioInputManager : IDisposable
{
    private Stream? _stream;
    private float _volumeMultiplier = 1.0f;
    private const int FRAME_SAMPLES = OpusProcessor.FRAME_SAMPLES; // 960 @ 16kHz
    private const int SAMPLE_RATE   = OpusProcessor.SAMPLE_RATE;
    private readonly List<float> _buffer = [];

    public event Action<float[]>? FrameReady;
    public event Action<float>?   VuLevel;

    public IEnumerable<string> GetInputDevices()
    {
        PortAudio.LoadNativeLibrary();
        PortAudio.Initialize();
        var devices = new List<string>();
        int count = PortAudio.DeviceCount;
        for (int i = 0; i < count; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.maxInputChannels > 0)
                devices.Add(info.name);
        }
        return devices;
    }

    public void StartCapture(string? deviceName = null, float volumeMultiplier = 1.0f)
    {
        StopCapture();
        _volumeMultiplier = volumeMultiplier;

        PortAudio.LoadNativeLibrary();
        PortAudio.Initialize();

        int deviceIndex = PortAudio.DefaultInputDevice;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            int count = PortAudio.DeviceCount;
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(PortAudio.GetDeviceInfo(i).name, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    deviceIndex = i;
                    break;
                }
            }
        }

        if (deviceIndex < 0) return; // no input device available

        var inParams = new StreamParameters
        {
            device              = deviceIndex,
            channelCount        = 1, // mono
            sampleFormat        = SampleFormat.Float32,
            suggestedLatency    = PortAudio.GetDeviceInfo(deviceIndex).defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };

        _stream = new Stream(
            inParams, null,
            SAMPLE_RATE,
            (uint)FRAME_SAMPLES,
            StreamFlags.ClipOff,
            OnAudioInput,
            null);

        _stream.Start();
    }

    private StreamCallbackResult OnAudioInput(
        nint input, nint output, uint frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags flags, nint userData)
    {
        if (input == nint.Zero) return StreamCallbackResult.Continue;

        var pcm = new float[frameCount];
        Marshal.Copy(input, pcm, 0, (int)frameCount);

        float sumSq = 0;
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = Math.Clamp(pcm[i] * _volumeMultiplier, -1f, 1f);
            sumSq += pcm[i] * pcm[i];
        }
        VuLevel?.Invoke(MathF.Sqrt(sumSq / pcm.Length));

        _buffer.AddRange(pcm);
        while (_buffer.Count >= FRAME_SAMPLES)
        {
            var frame = _buffer.GetRange(0, FRAME_SAMPLES).ToArray();
            _buffer.RemoveRange(0, FRAME_SAMPLES);
            FrameReady?.Invoke(frame);
        }
        return StreamCallbackResult.Continue;
    }

    public void StopCapture()
    {
        try { _stream?.Stop(); } catch { }
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() { StopCapture(); PortAudio.Terminate(); }
}
