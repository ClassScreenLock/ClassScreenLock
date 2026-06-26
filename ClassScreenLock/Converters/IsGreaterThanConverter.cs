using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters;

public class IsGreaterThanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue && parameter != null)
        {
            if (double.TryParse(parameter.ToString(), out double compareValue))
            {
                return doubleValue > compareValue;
            }
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
