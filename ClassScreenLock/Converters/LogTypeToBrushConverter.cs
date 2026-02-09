using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ClassScreenLock.Converters;

public class LogTypeToBrushConverter : IValueConverter
{
    public static readonly LogTypeToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? string.Empty).Trim();
        var opacity = 1.0;
        if (parameter is string p && double.TryParse(p, out var o))
        {
            opacity = o;
        }

        try
        {
            var brush = s.ToLowerInvariant() switch
            {
                "error" => Brush.Parse("#E81123"),
                "warning" => Brush.Parse("#D83B01"),
                "info" => Brush.Parse("#0078D4"),
                "debug" => Brush.Parse("#A19F9D"),
                "security" => Brush.Parse("#5C2D91"),
                "network" => Brush.Parse("#0099BC"),
                "account" => Brush.Parse("#5A64B1"),
                "ipc" => Brush.Parse("#0078D4"),
                "ui" => Brush.Parse("#107C10"),
                "monitoring" => Brush.Parse("#8A8886"),
                "init" => Brush.Parse("#605E5C"),
                "navigation" => Brush.Parse("#6B69D6"),
                "backup" => Brush.Parse("#107C10"),
                "restore" => Brush.Parse("#107C10"),
                _ => Brush.Parse("#A19F9D")
            };

            if (opacity < 1.0 && brush is ISolidColorBrush scb)
            {
                return new SolidColorBrush(scb.Color, opacity);
            }

            return brush;
        }
        catch
        {
            return Brushes.Gray;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
