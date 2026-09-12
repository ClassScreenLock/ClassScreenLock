using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters;

public class StringFormatConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string format)
        {
            return string.Format(format, values.ToArray());
        }
        return string.Join(" ", values.Where(v => v != null));
    }
}
