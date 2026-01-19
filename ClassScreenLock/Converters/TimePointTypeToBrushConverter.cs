using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ClassScreenLock.Models;

namespace ClassScreenLock.Converters;

public class TimePointTypeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TimePointType type)
        {
            try
            {
                return type switch
                {
                    TimePointType.Class => Brush.Parse("#0078D4"), // Fluent Blue
                    TimePointType.Break => Brush.Parse("#107C10"), // Fluent Green
                    TimePointType.Divider => Brush.Parse("#A19F9D"), // Fluent Gray
                    TimePointType.Action => Brush.Parse("#D83B01"), // Fluent Orange/Red
                    _ => Brush.Parse("#000000")
                };
            }
            catch
            {
                return Brushes.Gray;
            }
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
