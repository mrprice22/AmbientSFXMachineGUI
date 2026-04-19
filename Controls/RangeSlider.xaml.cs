using System;
using System.Windows;
using System.Windows.Controls;

namespace AmbientSFXMachineGUI.Controls;

public partial class RangeSlider : UserControl
{
    private bool _updating;

    public int LowValue  => (int)LowSlider.Value;
    public int HighValue => (int)HighSlider.Value;

    public RangeSlider()
    {
        InitializeComponent();
    }

    public void SetValues(int low, int high, int maximum = 1800)
    {
        _updating = true;
        LowSlider.Maximum  = maximum;
        HighSlider.Maximum = maximum;
        LowSlider.Value    = Math.Clamp(low,  0, maximum);
        HighSlider.Value   = Math.Clamp(high, 0, maximum);
        _updating = false;

        UpdateLabels();
        UpdateFill();
    }

    private void OnLowChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        // Ensure low never exceeds high.
        if (LowSlider.Value > HighSlider.Value)
        {
            _updating = true;
            LowSlider.Value = HighSlider.Value;
            _updating = false;
        }
        UpdateLabels();
        UpdateFill();
    }

    private void OnHighChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        // Ensure high never falls below low.
        if (HighSlider.Value < LowSlider.Value)
        {
            _updating = true;
            HighSlider.Value = LowSlider.Value;
            _updating = false;
        }
        UpdateLabels();
        UpdateFill();
    }

    private void OnTrackSizeChanged(object s, SizeChangedEventArgs e) => UpdateFill();

    private void UpdateLabels()
    {
        LowLabel.Text  = FormatSeconds((int)LowSlider.Value);
        HighLabel.Text = FormatSeconds((int)HighSlider.Value);
    }

    private void UpdateFill()
    {
        double width = TrackArea.ActualWidth;
        if (width <= 0) return;

        const double thumbHalf = 7.0;
        double trackWidth = width - thumbHalf * 2;
        double maximum = HighSlider.Maximum;
        if (maximum <= 0) return;

        double leftPct  = LowSlider.Value  / maximum;
        double rightPct = HighSlider.Value / maximum;

        RangeFill.Margin = new Thickness(
            thumbHalf + leftPct  * trackWidth,
            0,
            thumbHalf + (1 - rightPct) * trackWidth,
            0);
    }

    private static string FormatSeconds(int totalSeconds)
    {
        int m = totalSeconds / 60;
        int s = totalSeconds % 60;
        return s == 0 ? $"{m}m" : $"{m}m {s:00}s";
    }
}
