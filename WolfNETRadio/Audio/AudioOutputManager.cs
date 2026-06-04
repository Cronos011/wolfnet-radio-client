using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WolfNETRadio.Audio;

public class AudioOutputManager : IDisposable
{
    private WaveOutEvent? _waveOut;
    private MixingSampleProvider? _mixer;
    private readonly Dictionary<string, BufferedWaveProvider> _sources = [];

    public IEnumerable<string> GetOutputDevices()
    {
        var devices = new List<string>();
        for (var i = 0; i < WaveOutEvent.DeviceCount; i++)
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

        var deviceIndex = -1;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            for (var i = 0; i < WaveOutEvent.DeviceCount; i++)
            {
                var name = WaveOut.GetCapabilities(i).ProductName;
                if (string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    deviceIndex = i;
                    break;
                }
            }
        }

        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(OpusProcessor.SAMPLE_RATE, OpusProcessor.CHANNELS);
        _mixer = new MixingSampleProvider(waveFormat) { ReadFully = true };
        var outputProvider = _mixer.ToWaveProvider();

        _waveOut = new WaveOutEvent { DeviceNumber = deviceIndex };
        _waveOut.Init(outputProvider);
        _waveOut.Play();
    }

    /// Add or update incoming PCM audio for a source (identified by clientGuid)
    public void PlayAudio(string clientGuid, float[] pcm)
    {
        if (_mixer == null)
            return;

        if (!_sources.TryGetValue(clientGuid, out var buffer))
        {
            var waveFormat = new WaveFormat(OpusProcessor.SAMPLE_RATE, 16, OpusProcessor.CHANNELS);
            buffer = new BufferedWaveProvider(waveFormat)
            {
                DiscardOnBufferOverflow = true
            };
            _sources[clientGuid] = buffer;
            _mixer.AddMixerInput(buffer.ToSampleProvider());
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

        buffer.AddSamples(bytes, 0, bytes.Length);
    }

    public void Stop()
    {
        _waveOut?.Stop();
        _sources.Clear();
    }

    public void Dispose() { _waveOut?.Dispose(); }
}
