using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters
{
    public class BoolToMarginConverter : IValueConverter
    {
        public static readonly BoolToMarginConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isExpanded && parameter is string param)
            {
                var parts = param.Split('|');
                if (parts.Length == 2)
                {
                    return isExpanded ? parts[0] : parts[1];
                }
            }
            return "0";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}
