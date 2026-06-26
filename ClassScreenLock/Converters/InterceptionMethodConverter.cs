using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using ClassScreenLock.Models;

namespace ClassScreenLock.Converters;

public class InterceptionMethodConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is InterceptionMethod method)
        {
            var key = method switch
            {
                InterceptionMethod.App => "Network_Method_App",
                InterceptionMethod.Hosts => "Network_Method_Hosts",
                InterceptionMethod.Both => "Network_Method_Both",
                _ => "Network_Method_App"
            };

            if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true && resource is string localized)
            {
                return localized;
            }
            return method.ToString();
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
