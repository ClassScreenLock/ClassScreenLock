using Avalonia.Data.Converters;
using System;

namespace ClassScreenLock.Converters;

public class BoolToInverseBoolConverter : IValueConverter
{
    public static readonly BoolToInverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool boolValue ? !boolValue : true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool boolValue ? !boolValue : true;
    }
}
