using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ClassScreenLock.Models;
using FluentIcons.Common;

namespace ClassScreenLock.Converters;

/// <summary>
/// BlockedRuleKind -> 图标。
/// Name  -> Person (进程名)
/// Path  -> File (文件路径)
/// </summary>
public class BlockedRuleKindIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BlockedRuleKind kind)
        {
            return kind switch
            {
                BlockedRuleKind.Path => Symbol.Document,
                BlockedRuleKind.Name => Symbol.Person,
                _ => Symbol.Question
            };
        }
        return Symbol.Question;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
