using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters
{
    public class MBToGBConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int mbValue)
            {
                return mbValue / 1024.0;
            }
            if (value is double dValue)
            {
                return dValue / 1024.0;
            }
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double gbValue)
            {
                return (int)(gbValue * 1024);
            }
            return 0;
        }
    }
}