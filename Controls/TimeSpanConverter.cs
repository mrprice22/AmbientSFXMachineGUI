using System;
using System.Globalization;
using System.Windows.Data;

namespace AmbientSFXMachineGUI.Controls;

[ValueConversion(typeof(TimeSpan), typeof(string))]
public sealed class TimeSpanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TimeSpan ts || ts <= TimeSpan.Zero) return "—";
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss", culture)
            : ts.ToString(@"m\:ss", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
