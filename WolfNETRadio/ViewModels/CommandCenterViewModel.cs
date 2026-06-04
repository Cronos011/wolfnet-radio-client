using CommunityToolkit.Mvvm.ComponentModel;
using WolfNETRadio.Models;
using WolfNETRadio.Network;

namespace WolfNETRadio.ViewModels;

public partial class CommandCenterViewModel : ObservableObject
{
    public CommandCenterViewModel(ClientState state, SRSControlClient control)
    {
        // Radios 1-10 + intercom
        RadioSlots = state.Radios.Skip(1).Take(10)
            .Select(r => new RadioSlotViewModel(r, control))
            .ToArray();
        Intercom = new RadioSlotViewModel(state.Radios[0], control);
    }

    public RadioSlotViewModel[] RadioSlots { get; }
    public RadioSlotViewModel Intercom { get; }

    [ObservableProperty] private double _opacity = 1.0;
}
