using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WolfNETRadio.Models;

/// <summary>Represents a single radio channel slot (0=intercom, 1-10=ops)</summary>
public partial class RadioSlot : ObservableObject
{
    // ── Enums ───────────────────────────────────────────────────────────
    public enum RadioMode { PTT, VOX }
    public enum RadioPan { Both, Left, Right }

    // ── Core properties ─────────────────────────────────────────────────
    [ObservableProperty] private int _radioId;
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

    // ── Channel A/B memory ──────────────────────────────────────────────
    [ObservableProperty] private int _channelACode = 1000;
    [ObservableProperty] private int _channelBCode = 1000;
    [ObservableProperty] private bool _isChannelA = true;  // true=A active, false=B active

    // ── Mode ─────────────────────────────────────────────────────────────
    [ObservableProperty] private RadioMode _mode = RadioMode.PTT;

    // ── Stereo pan ──────────────────────────────────────────────────────
    [ObservableProperty] private RadioPan _pan = RadioPan.Both;

    // ── Active/Standby ──────────────────────────────────────────────────
    [ObservableProperty] private bool _isActive = true;

    // ── RX info (cleared after 500ms silence) ───────────────────────────
    [ObservableProperty] private string _receivingCallsign = string.Empty;

    // ── Computed properties ─────────────────────────────────────────────────────

    /// <summary>Active channel code (computed from A/B memory)</summary>
    public int ChannelCode
    {
        get => _isChannelA ? _channelACode : _channelBCode;
        set
        {
            // Sets the ACTIVE channel's code
            if (_isChannelA)
                ChannelACode = value;
            else
                ChannelBCode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FrequencyHz));
            OnPropertyChanged(nameof(DisplayCode));
        }
    }

    /// <summary>Internal SRS frequency in Hz (1MHz per code unit offset from 1MHz base)</summary>
    public double FrequencyHz => 1_000_000.0 + (ChannelCode * 1_000.0);

    public string DisplayCode => ChannelCode.ToString("D4");

    public bool IsIntercom => RadioId == 0;

    public static int FrequencyToCode(double hz) =>
        Math.Clamp((int)Math.Round((hz - 1_000_000.0) / 1_000.0), 0, 9999);

    public static double CodeToFrequency(int code) =>
        1_000_000.0 + (Math.Clamp(code, 0, 9999) * 1_000.0);

    // ── Channel switching ────────────────────────────────────────────────────

    public void SwitchChannel()
    {
        IsChannelA = !IsChannelA;
        OnPropertyChanged(nameof(ChannelCode));
        OnPropertyChanged(nameof(DisplayCode));
        OnPropertyChanged(nameof(FrequencyHz));
    }
}

public record PresetChannel(int Index, string Name, int ChannelCode);
