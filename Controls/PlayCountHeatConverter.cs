using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AmbientSFXMachineGUI.Controls;

public sealed class PlayCountHeatConverter : IValueConverter
{
    public static readonly PlayCountHeatConverter Instance = new();

    private const int SaturationCount = 20;
    private static readonly Color ColdColor = Color.FromRgb(0x44, 0x44, 0x44);
    private static readonly Color HotColor  = Color.FromRgb(0xE8, 0x6A, 0x1F);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0,
        };
        if (count <= 0) return new SolidColorBrush(Color.FromArgb(0x40, ColdColor.R, ColdColor.G, ColdColor.B));
        var t = Math.Min(1.0, count / (double)SaturationCount);
        byte r = (byte)(ColdColor.R + (HotColor.R - ColdColor.R) * t);
        byte g = (byte)(ColdColor.G + (HotColor.G - ColdColor.G) * t);
        byte b = (byte)(ColdColor.B + (HotColor.B - ColdColor.B) * t);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
