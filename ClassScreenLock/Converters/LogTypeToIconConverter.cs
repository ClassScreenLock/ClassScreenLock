using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using FluentIcons.Common;

namespace ClassScreenLock.Converters;

public class LogTypeToIconConverter : IValueConverter
{
    public static readonly LogTypeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "error" => Symbol.DismissCircle,
            "warning" => Symbol.Warning,
            "info" => Symbol.Info,
            "debug" => Symbol.Bug,
            "security" => Symbol.Shield,
            "network" => Symbol.Building,
            "account" => Symbol.Person,
            "ipc" => Symbol.ArrowSwap,
            "ui" => Symbol.Desktop,
            "monitoring" => Symbol.Line,
            "init" => Symbol.Settings,
            "navigation" => Symbol.SignOut,
            "backup" => Symbol.Database,
            "restore" => Symbol.ArrowUndo,
            _ => Symbol.Document
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
