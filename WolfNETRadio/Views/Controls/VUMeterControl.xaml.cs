using System.Windows;
using System.Windows.Controls;

namespace WolfNETRadio.Views.Controls;

public partial class VUMeterControl : UserControl
{
    public VUMeterControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(double), typeof(VUMeterControl), new PropertyMetadata(0d));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }
}
