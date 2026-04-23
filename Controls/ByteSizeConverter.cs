using System;
using System.Globalization;
using System.Windows.Data;

namespace AmbientSFXMachineGUI.Controls;

[ValueConversion(typeof(long), typeof(string))]
public sealed class ByteSizeConverter : IValueConverter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        double bytes = System.Convert.ToDouble(value, culture);
        if (bytes <= 0) return "—";
        int unit = 0;
        while (bytes >= 1024 && unit < Units.Length - 1) { bytes /= 1024; unit++; }
        return string.Format(culture, unit == 0 ? "{0:0} {1}" : "{0:0.#} {1}", bytes, Units[unit]);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
