using System;
using System.Windows;
using System.Windows.Controls;

namespace AmbientSFXMachineGUI.Controls;

public partial class PanIndicator : UserControl
{
    // Arc geometry constants matching the XAML path "M 2,42 A 38,38 0 0 0 78,42"
    private const double Cx = 40.0;   // arc center x
    private const double Cy = 42.0;   // arc center y (bottom of canvas)
    private const double R  = 38.0;   // arc radius
    private const double DotHalf = 5.0; // half of PanDot Width/Height

    public static readonly DependencyProperty PanProperty =
        DependencyProperty.Register(
            nameof(Pan), typeof(double), typeof(PanIndicator),
            new PropertyMetadata(0.0, OnPanChanged));

    public double Pan
    {
        get => (double)GetValue(PanProperty);
        set => SetValue(PanProperty, value);
    }

    public PanIndicator()
    {
        InitializeComponent();
        Loaded += (_, _) => PlaceDot(Pan);
    }

    private static void OnPanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PanIndicator)d).PlaceDot((double)e.NewValue);

    private void PlaceDot(double pan)
    {
        // pan is -1 (full left) to +1 (full right); 0 = top of arc (center)
        double theta = Math.Clamp(pan, -1.0, 1.0) * (Math.PI / 2.0);
        double x = Cx + R * Math.Sin(theta);
        double y = Cy - R * Math.Cos(theta);

        Canvas.SetLeft(PanDot, x - DotHalf);
        Canvas.SetTop(PanDot,  y - DotHalf);
    }
}
