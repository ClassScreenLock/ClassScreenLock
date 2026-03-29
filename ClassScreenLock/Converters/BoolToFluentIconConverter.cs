using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using FluentIcons.Common;

namespace ClassScreenLock.Converters;

public class BoolToFluentIconConverter : IValueConverter
{
    public static readonly BoolToFluentIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string paramStr)
        {
            var parts = paramStr.Split('|');
            if (parts.Length == 2)
            {
                var trueIcon = ParseSymbol(parts[0]);
                var falseIcon = ParseSymbol(parts[1]);
                return value is true ? trueIcon : falseIcon;
            }
        }
        
        return Symbol.QuestionCircle;
    }

    private static Symbol ParseSymbol(string symbolName)
    {
        if (Enum.TryParse<Symbol>(symbolName.Trim(), true, out var symbol))
        {
            return symbol;
        }
        return Symbol.QuestionCircle;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
