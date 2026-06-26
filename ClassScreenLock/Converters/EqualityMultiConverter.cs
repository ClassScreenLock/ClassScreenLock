using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters;

public class EqualityMultiConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2) return false;

        var a = values[0];
        var b = values[1];

        if (a == null || b == null) return false;

        if (TryConvertToInt(a, culture, out var ia) && TryConvertToInt(b, culture, out var ib))
        {
            return ia == ib;
        }

        return Equals(a, b);
    }

    public object ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }

    private static bool TryConvertToInt(object? value, CultureInfo culture, out int result)
    {
        try
        {
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case string s when int.TryParse(s, NumberStyles.Integer, culture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    if (value is IConvertible c)
                    {
                        result = System.Convert.ToInt32(c, culture);
                        return true;
                    }

                    result = 0;
                    return false;
            }
        }
        catch
        {
            result = 0;
            return false;
        }
    }
}

