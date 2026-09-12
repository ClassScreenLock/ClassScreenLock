using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using ClassScreenLock.Services;

namespace ClassScreenLock.Converters;

/// <summary>
/// 从当前主题资源解析语义色，失败时回退到 Fluent 规范色。
/// 确保状态色随深浅主题自动切换（F2System* 语义 token）。
/// </summary>
internal static class ThemeBrushResolver
{
    public static IBrush Resolve(string brushKey, string fallbackHex)
    {
        var app = Application.Current;
        if (app is not null)
        {
            var theme = app.ActualThemeVariant;
            if (app.Resources.TryGetResource(brushKey, theme, out var res) && res is IBrush brush)
            {
                return brush;
            }
        }
        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}

public class BooleanToStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isLocked)
        {
            return isLocked
                ? LocalizationService.Instance.GetString("Account_Status_Locked")
                : LocalizationService.Instance.GetString("Account_Status_LoggedIn");
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

public class ServiceStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRunning)
        {
            return isRunning
                ? ThemeBrushResolver.Resolve("F2SystemSuccessBrush", "#0F7B0F")   // Running
                : ThemeBrushResolver.Resolve("F2SystemCriticalBrush", "#C42B1C"); // Stopped
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

public class BooleanToStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isLocked)
        {
            return isLocked
                ? ThemeBrushResolver.Resolve("F2SystemCriticalBrush", "#C42B1C") // 锁定：Critical
                : ThemeBrushResolver.Resolve("F2SystemSuccessBrush", "#0F7B0F");  // 在线：Success
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

public class SuccessStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSuccess)
        {
            return isSuccess
                ? ThemeBrushResolver.Resolve("F2SystemSuccessBrush", "#0F7B0F")   // Success
                : ThemeBrushResolver.Resolve("F2SystemCriticalBrush", "#C42B1C"); // Failed
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}