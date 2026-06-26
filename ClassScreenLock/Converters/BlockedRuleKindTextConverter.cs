using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ClassScreenLock.Models;

namespace ClassScreenLock.Converters;

/// <summary>
/// BlockedRuleKind -> 显示文本。
/// </summary>
public class BlockedRuleKindTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BlockedRuleKind kind)
        {
            return kind switch
            {
                BlockedRuleKind.Path => "按文件路径匹配",
                BlockedRuleKind.Name => "按进程名匹配",
                _ => string.Empty
            };
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
