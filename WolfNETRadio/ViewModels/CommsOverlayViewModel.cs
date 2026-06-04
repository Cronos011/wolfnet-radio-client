using CommunityToolkit.Mvvm.ComponentModel;
using WolfNETRadio.Models;
using WolfNETRadio.Network;

namespace WolfNETRadio.ViewModels;

public partial class CommsOverlayViewModel : ObservableObject
{
    private readonly ClientState _state;

    public CommsOverlayViewModel(ClientState state, SRSControlClient control)
    {
        _state = state;
        // Radio slots 1-3 + intercom (0)
        Radio1 = new RadioSlotViewModel(state.Radios[1], control);
        Radio2 = new RadioSlotViewModel(state.Radios[2], control);
        Radio3 = new RadioSlotViewModel(state.Radios[3], control);
        Intercom = new RadioSlotViewModel(state.Radios[0], control);
    }

    public RadioSlotViewModel Radio1 { get; }
    public RadioSlotViewModel Radio2 { get; }
    public RadioSlotViewModel Radio3 { get; }
    public RadioSlotViewModel Intercom { get; }

    [ObservableProperty] private double _opacity = 1.0;
}
