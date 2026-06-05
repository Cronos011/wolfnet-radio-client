using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WolfNETRadio.Models;

namespace WolfNETRadio.Views.Controls;

public partial class RadioSlotControl : UserControl
{
    private bool _isEditing;
    private int _lastCommittedChannel;

    public RadioSlotControl()
    {
        InitializeComponent();
        _lastCommittedChannel = ChannelCode;
    }

    public static readonly DependencyProperty RadioIdProperty =
        DependencyProperty.Register(nameof(RadioId), typeof(int), typeof(RadioSlotControl), new PropertyMetadata(0));

    public int RadioId
    {
        get => (int)GetValue(RadioIdProperty);
        set => SetValue(RadioIdProperty, value);
    }

    public static readonly DependencyProperty ChannelCodeProperty =
        DependencyProperty.Register(nameof(ChannelCode), typeof(int), typeof(RadioSlotControl), new PropertyMetadata(0, OnChannelCodeChanged));

    public int ChannelCode
    {
        get => (int)GetValue(ChannelCodeProperty);
        set => SetValue(ChannelCodeProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(RadioSlotControl), new PropertyMetadata(false));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty IsTransmittingProperty =
        DependencyProperty.Register(nameof(IsTransmitting), typeof(bool), typeof(RadioSlotControl), new PropertyMetadata(false));

    public bool IsTransmitting
    {
        get => (bool)GetValue(IsTransmittingProperty);
        set => SetValue(IsTransmittingProperty, value);
    }

    public static readonly DependencyProperty SlotLabelTextProperty =
        DependencyProperty.Register(nameof(SlotLabelText), typeof(string), typeof(RadioSlotControl), new PropertyMetadata(string.Empty));

    public string SlotLabelText
    {
        get => (string)GetValue(SlotLabelTextProperty);
        set => SetValue(SlotLabelTextProperty, value);
    }

    public static readonly DependencyProperty VolumeProperty =
        DependencyProperty.Register(nameof(Volume), typeof(double), typeof(RadioSlotControl), new PropertyMetadata(0d));

    public double Volume
    {
        get => (double)GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public static readonly DependencyProperty TunedClientCountProperty =
        DependencyProperty.Register(nameof(TunedClientCount), typeof(int), typeof(RadioSlotControl), new PropertyMetadata(0));

    public int TunedClientCount
    {
        get => (int)GetValue(TunedClientCountProperty);
        set => SetValue(TunedClientCountProperty, value);
    }

    public static readonly DependencyProperty ShowStepButtonsProperty =
        DependencyProperty.Register(nameof(ShowStepButtons), typeof(bool), typeof(RadioSlotControl), new PropertyMetadata(true));

    public bool ShowStepButtons
    {
        get => (bool)GetValue(ShowStepButtonsProperty);
        set => SetValue(ShowStepButtonsProperty, value);
    }

    // ── New DPs: Channel A/B, Mode, Pan, Standby, RX ──────────────────────

    public static readonly DependencyProperty IsChannelAProperty =
        DependencyProperty.Register(nameof(IsChannelA), typeof(bool), typeof(RadioSlotControl), new PropertyMetadata(true));
    public bool IsChannelA
    {
        get => (bool)GetValue(IsChannelAProperty);
        set => SetValue(IsChannelAProperty, value);
    }

    public static readonly DependencyProperty ChannelACodeProperty =
        DependencyProperty.Register(nameof(ChannelACode), typeof(int), typeof(RadioSlotControl), new PropertyMetadata(1000));
    public int ChannelACode
    {
        get => (int)GetValue(ChannelACodeProperty);
        set => SetValue(ChannelACodeProperty, value);
    }

    public static readonly DependencyProperty ChannelBCodeProperty =
        DependencyProperty.Register(nameof(ChannelBCode), typeof(int), typeof(RadioSlotControl), new PropertyMetadata(1000));
    public int ChannelBCode
    {
        get => (int)GetValue(ChannelBCodeProperty);
        set => SetValue(ChannelBCodeProperty, value);
    }

    public static readonly DependencyProperty IsVoxProperty =
        DependencyProperty.Register(nameof(IsVox), typeof(bool), typeof(RadioSlotControl), new PropertyMetadata(false));
    public bool IsVox
    {
        get => (bool)GetValue(IsVoxProperty);
        set => SetValue(IsVoxProperty, value);
    }

    public static readonly DependencyProperty PanModeProperty =
        DependencyProperty.Register(nameof(PanMode), typeof(RadioSlot.RadioPan), typeof(RadioSlotControl),
            new PropertyMetadata(RadioSlot.RadioPan.Both));
    public RadioSlot.RadioPan PanMode
    {
        get => (RadioSlot.RadioPan)GetValue(PanModeProperty);
        set => SetValue(PanModeProperty, value);
    }

    public static readonly DependencyProperty IsSlotActiveProperty =
        DependencyProperty.Register(nameof(IsSlotActive), typeof(bool), typeof(RadioSlotControl), new PropertyMetadata(true));
    public bool IsSlotActive
    {
        get => (bool)GetValue(IsSlotActiveProperty);
        set => SetValue(IsSlotActiveProperty, value);
    }

    public static readonly DependencyProperty ReceivingCallsignProperty =
        DependencyProperty.Register(nameof(ReceivingCallsign), typeof(string), typeof(RadioSlotControl), new PropertyMetadata(string.Empty));
    public string ReceivingCallsign
    {
        get => (string)GetValue(ReceivingCallsignProperty);
        set => SetValue(ReceivingCallsignProperty, value);
    }

    // ── New Routed Events ──────────────────────────────────────────────────

    public static readonly RoutedEvent ChannelSwitchedEvent =
        EventManager.RegisterRoutedEvent(nameof(ChannelSwitched), RoutingStrategy.Bubble,
            typeof(EventHandler<ChannelSwitchedEventArgs>), typeof(RadioSlotControl));
    public event EventHandler<ChannelSwitchedEventArgs> ChannelSwitched
    {
        add => AddHandler(ChannelSwitchedEvent, value);
        remove => RemoveHandler(ChannelSwitchedEvent, value);
    }

    public static readonly RoutedEvent ModeChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ModeChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(RadioSlotControl));
    public event RoutedEventHandler ModeChanged
    {
        add => AddHandler(ModeChangedEvent, value);
        remove => RemoveHandler(ModeChangedEvent, value);
    }

    public static readonly RoutedEvent PanChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(PanChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<PanChangedEventArgs>), typeof(RadioSlotControl));
    public event EventHandler<PanChangedEventArgs> PanChanged
    {
        add => AddHandler(PanChangedEvent, value);
        remove => RemoveHandler(PanChangedEvent, value);
    }

    public static readonly RoutedEvent StandbyChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(StandbyChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(RadioSlotControl));
    public event RoutedEventHandler StandbyChanged
    {
        add => AddHandler(StandbyChangedEvent, value);
        remove => RemoveHandler(StandbyChangedEvent, value);
    }

    public static readonly RoutedEvent ChannelCodeChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ChannelCodeChanged), RoutingStrategy.Bubble, typeof(EventHandler<ChannelCodeChangedEventArgs>), typeof(RadioSlotControl));

    public event EventHandler<ChannelCodeChangedEventArgs> ChannelCodeChanged
    {
        add => AddHandler(ChannelCodeChangedEvent, value);
        remove => RemoveHandler(ChannelCodeChangedEvent, value);
    }

    public static readonly RoutedEvent StepChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(StepChanged), RoutingStrategy.Bubble, typeof(EventHandler<StepChangedEventArgs>), typeof(RadioSlotControl));

    public event EventHandler<StepChangedEventArgs> StepChanged
    {
        add => AddHandler(StepChangedEvent, value);
        remove => RemoveHandler(StepChangedEvent, value);
    }

    public static readonly RoutedEvent RetransmitToggledEvent =
        EventManager.RegisterRoutedEvent(nameof(RetransmitToggled), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(RadioSlotControl));

    public event RoutedEventHandler RetransmitToggled
    {
        add => AddHandler(RetransmitToggledEvent, value);
        remove => RemoveHandler(RetransmitToggledEvent, value);
    }

    public static readonly RoutedEvent RadioSelectedEvent =
        EventManager.RegisterRoutedEvent(nameof(RadioSelected), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(RadioSlotControl));

    public event RoutedEventHandler RadioSelected
    {
        add => AddHandler(RadioSelectedEvent, value);
        remove => RemoveHandler(RadioSelectedEvent, value);
    }

    public static readonly RoutedEvent VolumeChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(VolumeChanged), RoutingStrategy.Bubble, typeof(EventHandler<VolumeChangedEventArgs>), typeof(RadioSlotControl));

    public event EventHandler<VolumeChangedEventArgs> VolumeChanged
    {
        add => AddHandler(VolumeChangedEvent, value);
        remove => RemoveHandler(VolumeChangedEvent, value);
    }

    private static void OnChannelCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (RadioSlotControl)d;
        var newValue = (int)e.NewValue;
        if (!control._isEditing && control.PART_ChannelDisplay != null)
        {
            control.PART_ChannelDisplay.Text = newValue.ToString("D4", CultureInfo.InvariantCulture);
        }
        control._lastCommittedChannel = newValue;
    }

    private void ChannelDisplay_GotFocus(object sender, RoutedEventArgs e)
    {
        _isEditing = true;
        PART_ChannelDisplay.IsReadOnly = false;
        PART_ChannelDisplay.SelectAll();
    }

    private void ChannelDisplay_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitChannelEdit();
    }

    private void ChannelDisplay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitChannelEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RevertChannelEdit();
            e.Handled = true;
        }
    }

    private void CommitChannelEdit()
    {
        if (PART_ChannelDisplay == null)
        {
            return;
        }

        if (int.TryParse(PART_ChannelDisplay.Text, out var value))
        {
            ChannelCode = value;
            RaiseEvent(new ChannelCodeChangedEventArgs(ChannelCodeChangedEvent, this, value));
            _lastCommittedChannel = value;
        }
        else
        {
            PART_ChannelDisplay.Text = _lastCommittedChannel.ToString("D4", CultureInfo.InvariantCulture);
        }

        PART_ChannelDisplay.IsReadOnly = true;
        _isEditing = false;
    }

    private void RevertChannelEdit()
    {
        if (PART_ChannelDisplay == null)
        {
            return;
        }

        PART_ChannelDisplay.Text = _lastCommittedChannel.ToString("D4", CultureInfo.InvariantCulture);
        PART_ChannelDisplay.IsReadOnly = true;
        _isEditing = false;
    }

    private void StepUp1000_Click(object sender, RoutedEventArgs e) => RaiseStepChanged(1000);
    private void StepUp100_Click(object sender, RoutedEventArgs e) => RaiseStepChanged(100);
    private void StepUp10_Click(object sender, RoutedEventArgs e) => RaiseStepChanged(10);
    private void StepUp1_Click(object sender, RoutedEventArgs e) => RaiseStepChanged(1);
    private void StepDown1000_Click(object sender, RoutedEventArgs e) => RaiseStepChanged(-1000);
    private void StepDown100_Click(object sender, RoutedEventArgs e) => RaiseStepChanged(-100);
    private void StepDown10_Click(object sender, RoutedEventArgs e) => RaiseStepChanged(-10);
    private void StepDown1_Click(object sender, RoutedEventArgs e) => RaiseStepChanged(-1);

    private void RaiseStepChanged(int delta)
    {
        RaiseEvent(new StepChangedEventArgs(StepChangedEvent, this, delta));
    }

    private void RetransmitButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(RetransmitToggledEvent, this));
    }

    private void ActiveIndicator_MouseDown(object sender, MouseButtonEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(RadioSelectedEvent, this));
    }

    private void VolumeSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        RaiseEvent(new VolumeChangedEventArgs(VolumeChangedEvent, this, Volume));
    }

    // ── New button handlers ───────────────────────────────────────────────

    private void ChABtn_Click(object sender, RoutedEventArgs e)
    {
        if (!IsChannelA)
            RaiseEvent(new ChannelSwitchedEventArgs(ChannelSwitchedEvent, this, true));
    }

    private void ChBBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsChannelA)
            RaiseEvent(new ChannelSwitchedEventArgs(ChannelSwitchedEvent, this, false));
    }

    private void ModeBtn_Click(object sender, RoutedEventArgs e)
    {
        IsVox = !IsVox;
        RaiseEvent(new RoutedEventArgs(ModeChangedEvent, this));
    }

    private void PanL_Click(object sender, RoutedEventArgs e)
    {
        PanMode = RadioSlot.RadioPan.Left;
        RaiseEvent(new PanChangedEventArgs(PanChangedEvent, this, RadioSlot.RadioPan.Left));
    }
    private void PanB_Click(object sender, RoutedEventArgs e)
    {
        PanMode = RadioSlot.RadioPan.Both;
        RaiseEvent(new PanChangedEventArgs(PanChangedEvent, this, RadioSlot.RadioPan.Both));
    }
    private void PanR_Click(object sender, RoutedEventArgs e)
    {
        PanMode = RadioSlot.RadioPan.Right;
        RaiseEvent(new PanChangedEventArgs(PanChangedEvent, this, RadioSlot.RadioPan.Right));
    }

    private void StbyBtn_Click(object sender, RoutedEventArgs e)
    {
        IsSlotActive = !IsSlotActive;
        RaiseEvent(new RoutedEventArgs(StandbyChangedEvent, this));
    }
}

public class ChannelCodeChangedEventArgs : RoutedEventArgs
{
    public ChannelCodeChangedEventArgs(RoutedEvent routedEvent, object source, int newChannelCode) : base(routedEvent, source)
    {
        NewChannelCode = newChannelCode;
    }

    public int NewChannelCode { get; }
}

public class StepChangedEventArgs : RoutedEventArgs
{
    public StepChangedEventArgs(RoutedEvent routedEvent, object source, int delta) : base(routedEvent, source)
    {
        Delta = delta;
    }

    public int Delta { get; }
}

public class VolumeChangedEventArgs : RoutedEventArgs
{
    public VolumeChangedEventArgs(RoutedEvent routedEvent, object source, double volume) : base(routedEvent, source)
    {
        Volume = volume;
    }

    public double Volume { get; }
}

public class ChannelSwitchedEventArgs : RoutedEventArgs
{
    public ChannelSwitchedEventArgs(RoutedEvent routedEvent, object source, bool isChannelA) : base(routedEvent, source)
    {
        IsChannelA = isChannelA;
    }
    public bool IsChannelA { get; }
}

public class PanChangedEventArgs : RoutedEventArgs
{
    public PanChangedEventArgs(RoutedEvent routedEvent, object source, WolfNETRadio.Models.RadioSlot.RadioPan pan) : base(routedEvent, source)
    {
        Pan = pan;
    }
    public WolfNETRadio.Models.RadioSlot.RadioPan Pan { get; }
}
