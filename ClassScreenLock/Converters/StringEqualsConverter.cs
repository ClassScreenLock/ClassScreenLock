using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters;

public class StringEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            var target = parameter?.ToString() ?? string.Empty;
            return string.Equals(value?.ToString(), target, StringComparison.Ordinal);
        }

        if (parameter is string paramStr)
        {
            var parts = paramStr.Split('|');
            if (parts.Length == 3)
            {
                var target = parts[0];
                var trueVal = parts[1];
                var falseVal = parts[2];

                bool isMatch = false;
                if (value == null) isMatch = target == "null";
                else isMatch = value.ToString() == target;

                return isMatch ? trueVal : falseVal;
            }
            
            if (value is string stringValue)
            {
                return stringValue == paramStr ? "selected-item" : "";
            }
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
