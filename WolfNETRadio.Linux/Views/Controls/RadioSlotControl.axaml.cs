using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WolfNETRadio.Models;

namespace WolfNETRadio.Views.Controls;

/// <summary>
/// Radio slot control for the Linux client. Binds to a RadioSlot model object via DataContext.
/// </summary>
public partial class RadioSlotControl : UserControl
{
    private RadioSlot? _slot;
    private static readonly SolidColorBrush Green  = new(Avalonia.Media.Color.Parse("#43E59A"));
    private static readonly SolidColorBrush Cyan   = new(Avalonia.Media.Color.Parse("#4FC3F7"));
    private static readonly SolidColorBrush Gold   = new(Avalonia.Media.Color.Parse("#D4A017"));
    private static readonly SolidColorBrush Red    = new(Avalonia.Media.Color.Parse("#FF4444"));
    private static readonly SolidColorBrush Muted  = new(Avalonia.Media.Color.Parse("#8A96A8"));

    public RadioSlotControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bind();
    }

    private void Bind()
    {
        _slot = DataContext as RadioSlot;
        if (_slot == null) return;

        Refresh();
        _slot.PropertyChanged += (_, e) =>
        {
            Dispatcher.UIThread.Post(Refresh);
        };
    }

    private void Refresh()
    {
        if (_slot == null) return;
        var s = _slot;

        // Label
        PART_Label.Text = s.IsIntercom ? "INTERCOM" : $"RADIO {s.RadioId}";

        // Code
        PART_Code.Text      = s.DisplayCode;
        PART_Code.Foreground = s.IsTransmitting ? Red : (s.IsReceiving ? Green : Cyan);

        // TX / RX
        PART_TxLabel.IsVisible    = s.IsTransmitting;
        PART_RxCallsign.IsVisible = s.IsReceiving && !string.IsNullOrEmpty(s.ReceivingCallsign);
        PART_RxCallsign.Text      = $"▼ {s.ReceivingCallsign}";

        // Channel A/B
        PART_ChA.Foreground = s.IsChannelA  ? Green : Muted;
        PART_ChB.Foreground = !s.IsChannelA ? Cyan  : Muted;

        // Note
        PART_Note.Text = s.Mode == RadioSlot.RadioMode.VOX ? "VOX" : "";

        // Mode button
        PART_Mode.Content   = s.Mode == RadioSlot.RadioMode.VOX ? "VOX" : "PTT";
        PART_Mode.Foreground = s.Mode == RadioSlot.RadioMode.VOX ? Gold : Muted;

        // Pan buttons
        PART_PanL.Foreground = s.Pan == RadioSlot.RadioPan.Left  ? Cyan : Muted;
        PART_PanC.Foreground = s.Pan == RadioSlot.RadioPan.Both  ? Cyan : Muted;
        PART_PanR.Foreground = s.Pan == RadioSlot.RadioPan.Right ? Cyan : Muted;

        // Standby
        PART_Stby.Content    = s.IsActive ? "ACT" : "STBY";
        PART_Stby.Foreground = s.IsActive ? Green : Muted;
    }

    private void ChA_Click(object? s, RoutedEventArgs e)  { if (_slot?.IsChannelA == false) { _slot.SwitchChannel(); } }
    private void ChB_Click(object? s, RoutedEventArgs e)  { if (_slot?.IsChannelA == true)  { _slot.SwitchChannel(); } }
    private void Mode_Click(object? s, RoutedEventArgs e) { if (_slot != null) _slot.Mode = _slot.Mode == RadioSlot.RadioMode.PTT ? RadioSlot.RadioMode.VOX : RadioSlot.RadioMode.PTT; }
    private void PanL_Click(object? s, RoutedEventArgs e) { if (_slot != null) _slot.Pan = RadioSlot.RadioPan.Left; }
    private void PanC_Click(object? s, RoutedEventArgs e) { if (_slot != null) _slot.Pan = RadioSlot.RadioPan.Both; }
    private void PanR_Click(object? s, RoutedEventArgs e) { if (_slot != null) _slot.Pan = RadioSlot.RadioPan.Right; }
    private void Stby_Click(object? s, RoutedEventArgs e) { if (_slot != null) _slot.IsActive = !_slot.IsActive; }
}
