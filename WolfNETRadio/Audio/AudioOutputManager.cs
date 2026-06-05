using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WolfNETRadio.Audio;

public class AudioOutputManager : IDisposable
{
    private WaveOutEvent? _waveOut;
    private MixingSampleProvider? _mixer;
    private readonly Dictionary<string, (BufferedWaveProvider buffer, PanningSampleProvider panner)> _sources = [];
    private string? _passthroughGuid;

    public IEnumerable<string> GetOutputDevices()
    {
        var devices = new List<string>();
        for (var i = 0; i < WaveOut.DeviceCount; i++)
        {
            devices.Add(WaveOut.GetCapabilities(i).ProductName);
        }

        return devices;
    }

    public void Start(string? deviceName = null)
    {
        Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _mixer = null;
        _sources.Clear();

        var deviceIndex = -1;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                var name = WaveOut.GetCapabilities(i).ProductName;
                if (string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    deviceIndex = i;
                    break;
                }
            }
        }

        // Switch to STEREO output
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(OpusProcessor.SAMPLE_RATE, 2);  // 2 channels for stereo
        _mixer = new MixingSampleProvider(waveFormat) { ReadFully = true };
        var outputProvider = _mixer.ToWaveProvider();

        _waveOut = new WaveOutEvent { DeviceNumber = deviceIndex };
        _waveOut.Init(outputProvider);
        _waveOut.Play();
    }

    /// <summary>Add or update incoming PCM audio for a source with panning</summary>
    /// <param name="pan">-1.0=Left, 0.0=Both, +1.0=Right</param>
    public void PlayAudio(string clientGuid, float pan, float[] pcm)
    {
        if (_mixer == null)
            return;

        if (!_sources.TryGetValue(clientGuid, out var entry))
        {
            var monoFmt = new WaveFormat(OpusProcessor.SAMPLE_RATE, 16, 1);  // mono input
            var buffer = new BufferedWaveProvider(monoFmt) { DiscardOnBufferOverflow = true };
            var panner = new PanningSampleProvider(buffer.ToSampleProvider()) { Pan = pan };
            _sources[clientGuid] = (buffer, panner);
            _mixer.AddMixerInput(panner);
            entry = (buffer, panner);
        }
        else
        {
            entry.panner.Pan = pan;  // update pan if radio changed
        }

        if (pcm.Length == 0)
            return;

        var bytes = new byte[pcm.Length * 2];
        for (var i = 0; i < pcm.Length; i++)
        {
            var clamped = Math.Clamp(pcm[i], -1.0f, 1.0f);
            var sample = (short)Math.Round(clamped * 32767.0f);
            BitConverter.GetBytes(sample).CopyTo(bytes, i * 2);
        }

        entry.buffer.AddSamples(bytes, 0, bytes.Length);
    }

    /// <summary>Passthrough test mode: route mic input directly to output</summary>
    public void StartPassthrough()
    {
        _passthroughGuid = "__passthrough__";
    }

    public void StopPassthrough()
    {
        _passthroughGuid = null;
        if (_sources.TryGetValue("__passthrough__", out _))
            _sources.Remove("__passthrough__");
    }

    public bool IsPassthroughActive => _passthroughGuid != null;

    public void PlayPassthrough(float[] pcm)
    {
        if (_passthroughGuid != null)
            PlayAudio("__passthrough__", 0.0f, pcm);
    }

    public void Stop()
    {
        _waveOut?.Stop();
        _sources.Clear();
    }

    public void Dispose() { _waveOut?.Dispose(); }
}
