using CommunityToolkit.Mvvm.ComponentModel;

namespace WolfNETRadio.Models;

/// <summary>Represents a single radio channel slot (0=intercom, 1-10=ops)</summary>
public partial class RadioSlot : ObservableObject
{
    [ObservableProperty] private int _radioId;
    [ObservableProperty] private int _channelCode = 1000;   // 0000–9999
    [ObservableProperty] private float _volume = 1.0f;       // 0.0–1.0
    [ObservableProperty] private bool _isSelected;           // this radio is the active PTT target
    [ObservableProperty] private bool _isTransmitting;       // currently sending audio
    [ObservableProperty] private bool _isReceiving;          // currently receiving audio
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isRetransmit;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private int _tunedClientCount;
    [ObservableProperty] private string _label = string.Empty;  // server-set callsign/label
    [ObservableProperty] private List<PresetChannel> _presets = [];

    /// <summary>Internal SRS frequency in Hz (1MHz per code unit offset from 1MHz base)</summary>
    public double FrequencyHz => 1_000_000.0 + (ChannelCode * 1_000.0);

    public static int FrequencyToCode(double hz) =>
        Math.Clamp((int)Math.Round((hz - 1_000_000.0) / 1_000.0), 0, 9999);

    public static double CodeToFrequency(int code) =>
        1_000_000.0 + (Math.Clamp(code, 0, 9999) * 1_000.0);

    public string DisplayCode => ChannelCode.ToString("D4");

    public bool IsIntercom => RadioId == 0;
}

public record PresetChannel(int Index, string Name, int ChannelCode);
