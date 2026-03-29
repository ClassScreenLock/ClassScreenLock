using Avalonia.Data.Converters;
using FluentIcons.Avalonia;
using System;

namespace ClassScreenLock.Converters;

public class BoolToIconConverter : IValueConverter
{
    public static readonly BoolToIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? FluentIcons.Common.Symbol.Table : FluentIcons.Common.Symbol.AlignLeft;
        }
        return FluentIcons.Common.Symbol.Table;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
