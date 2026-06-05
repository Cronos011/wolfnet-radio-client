using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PortAudioSharp;

namespace WolfNETRadio.Audio;

/// <summary>
/// Linux audio playback via PortAudio with per-source stereo panning.
/// </summary>
public class LinuxAudioOutputManager : IDisposable
{
    private Stream?  _stream;
    private readonly object _lock = new();

    // clientGuid -> (ringbuffer, pan -1..+1)
    private readonly Dictionary<string, (Queue<float> buf, float pan)> _sources = [];
    private const int SAMPLE_RATE   = OpusProcessor.SAMPLE_RATE;
    private const int FRAME_SAMPLES = OpusProcessor.FRAME_SAMPLES;

    // Passthrough (mic monitor)
    private readonly Queue<float> _passthrough = new();
    private bool _passthroughActive;

    public IEnumerable<string> GetOutputDevices()
    {
        PortAudio.LoadNativeLibrary();
        PortAudio.Initialize();
        var devices = new List<string>();
        int count = PortAudio.DeviceCount;
        for (int i = 0; i < count; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.maxOutputChannels > 0)
                devices.Add(info.name);
        }
        return devices;
    }

    public void Start(string? deviceName = null)
    {
        Stop();
        PortAudio.LoadNativeLibrary();
        PortAudio.Initialize();

        int deviceIndex = PortAudio.DefaultOutputDevice;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            int count = PortAudio.DeviceCount;
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(PortAudio.GetDeviceInfo(i).name, deviceName, StringComparison.OrdinalIgnoreCase))
                { deviceIndex = i; break; }
            }
        }
        if (deviceIndex < 0) return;

        var outParams = new StreamParameters
        {
            device              = deviceIndex,
            channelCount        = 2, // stereo output for panning
            sampleFormat        = SampleFormat.Float32,
            suggestedLatency    = PortAudio.GetDeviceInfo(deviceIndex).defaultLowOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };

        _stream = new Stream(
            null, outParams,
            SAMPLE_RATE,
            (uint)FRAME_SAMPLES,
            StreamFlags.ClipOff,
            OnAudioOutput,
            null);

        _stream.Start();
    }

    private StreamCallbackResult OnAudioOutput(
        nint input, nint output, uint frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags flags, nint userData)
    {
        int samples = (int)frameCount * 2; // stereo
        var mixed = new float[samples];

        lock (_lock)
        {
            // Mix all sources with their pan
            foreach (var (guid, entry) in _sources)
            {
                var (buf, pan) = entry;
                float gainL = pan <= 0 ? 1.0f : 1.0f - pan;
                float gainR = pan >= 0 ? 1.0f : 1.0f + pan;
                for (int i = 0; i < frameCount && buf.Count > 0; i++)
                {
                    float s = buf.Dequeue();
                    mixed[i * 2]     += s * gainL;
                    mixed[i * 2 + 1] += s * gainR;
                }
            }
            // Passthrough (mono -> both ears)
            if (_passthroughActive)
            {
                for (int i = 0; i < frameCount && _passthrough.Count > 0; i++)
                {
                    float s = _passthrough.Dequeue();
                    mixed[i * 2]     += s;
                    mixed[i * 2 + 1] += s;
                }
            }
        }

        // Soft clamp
        for (int i = 0; i < mixed.Length; i++)
            mixed[i] = Math.Clamp(mixed[i], -1f, 1f);

        Marshal.Copy(mixed, 0, output, samples);
        return StreamCallbackResult.Continue;
    }

    public void PlayAudio(string clientGuid, float pan, float[] pcm)
    {
        lock (_lock)
        {
            if (!_sources.TryGetValue(clientGuid, out var entry))
            {
                entry = (new Queue<float>(), pan);
                _sources[clientGuid] = entry;
            }
            // Update pan
            _sources[clientGuid] = (entry.buf, pan);
            foreach (var s in pcm) entry.buf.Enqueue(s);
        }
    }

    public void StartPassthrough() { lock (_lock) _passthroughActive = true; }
    public void StopPassthrough()  { lock (_lock) { _passthroughActive = false; _passthrough.Clear(); } }
    public bool IsPassthroughActive => _passthroughActive;
    public void PlayPassthrough(float[] pcm) { lock (_lock) { if (_passthroughActive) foreach (var s in pcm) _passthrough.Enqueue(s); } }

    public void Stop()
    {
        try { _stream?.Stop(); } catch { }
        _stream?.Dispose();
        _stream = null;
        lock (_lock) { _sources.Clear(); _passthrough.Clear(); }
    }

    public void Dispose() { Stop(); try { PortAudio.Terminate(); } catch { } }
}
