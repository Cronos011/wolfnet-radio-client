using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WolfNETRadio.Models;
using WolfNETRadio.Network;

namespace WolfNETRadio.ViewModels;

public partial class RadioSlotViewModel : ObservableObject
{
    private readonly RadioSlot _slot;
    private readonly SRSControlClient _control;

    public RadioSlotViewModel(RadioSlot slot, SRSControlClient control)
    {
        _slot = slot;
        _control = control;
        // Mirror slot properties
        _slot.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }

    public int RadioId => _slot.RadioId;
    public string SlotLabel => _slot.IsIntercom ? "INTERCOM" : $"CH-{_slot.RadioId}";
    public string DisplayCode => _slot.DisplayCode;

    /// <summary>
    /// Two-way bindable channel code. Setting this updates the underlying slot
    /// and sends an SRS update, allowing overlay XAMLs to bind TwoWay.
    /// </summary>
    public int ChannelCode
    {
        get => _slot.ChannelCode;
        set
        {
            var clamped = Math.Clamp(value, 0, 9999);
            if (_slot.ChannelCode == clamped) return;
            _slot.ChannelCode = clamped;
            OnPropertyChanged();
            _ = _control.SendUpdateAsync();
        }
    }

    public int ChannelACode
    {
        get => _slot.ChannelACode;
        set { _slot.ChannelACode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChannelCode)); }
    }

    public int ChannelBCode
    {
        get => _slot.ChannelBCode;
        set { _slot.ChannelBCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChannelCode)); }
    }

    public bool IsChannelA => _slot.IsChannelA;

    public RadioSlot.RadioMode Mode
    {
        get => _slot.Mode;
        set { _slot.Mode = value; OnPropertyChanged(); }
    }

    public RadioSlot.RadioPan Pan
    {
        get => _slot.Pan;
        set { _slot.Pan = value; OnPropertyChanged(); }
    }

    /// <summary>True when this radio is in VOX mode (bindable bool for XAML).</summary>
    public bool IsVox
    {
        get => _slot.Mode == RadioSlot.RadioMode.VOX;
        set
        {
            _slot.Mode = value ? RadioSlot.RadioMode.VOX : RadioSlot.RadioMode.PTT;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Mode));
        }
    }

    public RadioSlot.RadioMode Mode
    {
        get => _slot.Mode;
        set { _slot.Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsVox)); }
    }

    public bool IsSlotActive
    {
        get => _slot.IsActive;
        set { _slot.IsActive = value; OnPropertyChanged(); _ = _control.SendUpdateAsync(); }
    }

    public string ReceivingCallsign => _slot.ReceivingCallsign;

    public bool IsSelected => _slot.IsSelected;
    public bool IsTransmitting => _slot.IsTransmitting;
    public bool IsReceiving => _slot.IsReceiving;
    public bool IsActive => _slot.IsSelected || _slot.IsTransmitting || _slot.IsReceiving;
    public int TunedClientCount => _slot.TunedClientCount;
    public double Volume
    {
        get => _slot.Volume * 100.0;
        set { _slot.Volume = (float)(value / 100.0); OnPropertyChanged(); }
    }
    public bool IsRetransmit => _slot.IsRetransmit;

    [RelayCommand]
    public void SelectRadio() => _slot.IsSelected = true;

    [RelayCommand]
    public void ToggleRetransmit()
    {
        _slot.IsRetransmit = !_slot.IsRetransmit;
        _ = _control.SendUpdateAsync();
    }

    [RelayCommand]
    public void StepChannel(int delta)
    {
        _slot.ChannelCode = Math.Clamp(_slot.ChannelCode + delta, 0, 9999);
        _ = _control.SendUpdateAsync();
    }

    [RelayCommand]
    public void SwitchChannel()
    {
        _slot.SwitchChannel();
        OnPropertyChanged(nameof(ChannelCode));
        OnPropertyChanged(nameof(IsChannelA));
        OnPropertyChanged(nameof(DisplayCode));
        _ = _control.SendUpdateAsync();
    }

    public void CommitChannelCode(string input)
    {
        if (int.TryParse(input, out int code))
        {
            _slot.ChannelCode = Math.Clamp(code, 0, 9999);
            _ = _control.SendUpdateAsync();
        }
    }

    public void SetPreset(PresetChannel preset)
    {
        _slot.ChannelCode = preset.ChannelCode;
        _ = _control.SendUpdateAsync();
    }

    public IEnumerable<PresetChannel> Presets => _slot.Presets;
}
