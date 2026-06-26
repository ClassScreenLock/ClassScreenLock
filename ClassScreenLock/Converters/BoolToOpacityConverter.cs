using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ClassScreenLock.Converters
{
    public class BoolToOpacityConverter : IValueConverter
    {
        public static readonly BoolToOpacityConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isVisible)
            {
                return isVisible ? 1.0 : 0.0;
            }
            return 1.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }

    public class BoolToClassConverter : IValueConverter
    {
        public static readonly BoolToClassConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && parameter is string className)
            {
                return b ? className : string.Empty;
            }

            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }

    public class BoolToThicknessConverter : IValueConverter
    {
        public static readonly BoolToThicknessConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var thickness = new Thickness(1);

            if (parameter is string param)
            {
                var parts = param.Split('|');
                if (parts.Length == 2)
                {
                    var trueValue = TryParseDouble(parts[0], culture);
                    var falseValue = TryParseDouble(parts[1], culture);
                    if (value is bool b)
                    {
                        thickness = new Thickness(b ? trueValue : falseValue);
                    }
                }
            }

            return thickness;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }

        private static double TryParseDouble(string s, CultureInfo culture)
        {
            return double.TryParse(s, NumberStyles.Float, culture, out var d) ? d : 1;
        }
    }

    public class BoolToResourceBrushConverter : IValueConverter
    {
        public static readonly BoolToResourceBrushConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not bool b) return Brushes.Transparent;
            if (parameter is not string param) return Brushes.Transparent;

            var parts = param.Split('|');
            if (parts.Length != 2) return Brushes.Transparent;

            var key = b ? parts[0] : parts[1];
            if (Application.Current?.TryGetResource(key, null, out var resource) != true)
            {
                return Brushes.Transparent;
            }

            return resource switch
            {
                IBrush brush => brush,
                Color color => new SolidColorBrush(color),
                string s => TryParseBrush(s),
                _ => Brushes.Transparent
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }

        private static IBrush TryParseBrush(string value)
        {
            try
            {
                return Brush.Parse(value);
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
    }
}
