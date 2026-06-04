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
