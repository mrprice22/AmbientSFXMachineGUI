using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AmbientSFXMachineGUI.Controls;

public sealed class ZeroToVisibleConverter : IValueConverter
{
    public static readonly ZeroToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 1,
        };
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
