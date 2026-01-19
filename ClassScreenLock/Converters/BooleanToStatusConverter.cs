using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ClassScreenLock.Services;

namespace ClassScreenLock.Converters;

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
                ? new SolidColorBrush(Color.Parse("#107C10")) // Running: Green
                : new SolidColorBrush(Color.Parse("#E81123")); // Stopped: Red
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
            try
            {
                return isLocked 
                    ? new SolidColorBrush(Color.Parse("#E81123")) // Fluent Red
                    : new SolidColorBrush(Color.Parse("#107C10")); // Fluent Green
            }
            catch
            {
                return isLocked ? Brushes.Red : Brushes.Green;
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
