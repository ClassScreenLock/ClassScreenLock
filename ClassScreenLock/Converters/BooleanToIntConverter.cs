using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters
{
    public class BooleanToIntConverter : IValueConverter
    {
        public static readonly BooleanToIntConverter Instance = new();
        
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                if (parameter is string { Length: > 0 } mode && string.Equals(mode, "Invert", StringComparison.OrdinalIgnoreCase))
                {
                    boolValue = !boolValue;
                }
                return boolValue ? 0 : 1;
            }
            return 1;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                var boolValue = intValue == 0;
                if (parameter is string { Length: > 0 } mode && string.Equals(mode, "Invert", StringComparison.OrdinalIgnoreCase))
                {
                    boolValue = !boolValue;
                }
                return boolValue;
            }
            return false;
        }
    }
}
