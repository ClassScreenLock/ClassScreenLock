using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters;

public class LogTypeToIconConverter : IValueConverter
{
    public static readonly LogTypeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "error" => "fas fa-circle-xmark",
            "warning" => "fas fa-triangle-exclamation",
            "info" => "fas fa-info-circle",
            "debug" => "fas fa-bug",
            "security" => "fas fa-shield",
            "network" => "fas fa-network-wired",
            "account" => "fas fa-user",
            "ipc" => "fas fa-right-left",
            "ui" => "fas fa-desktop",
            "monitoring" => "fas fa-chart-line",
            "init" => "fas fa-gears",
            "navigation" => "fas fa-signs-post",
            "backup" => "fas fa-database",
            "restore" => "fas fa-undo",
            _ => "fas fa-file-lines"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
