using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FluentIcons.Common;

namespace ClassScreenLock.Uninstaller.Converters;

/// <summary>
/// 字符串与布尔值相关的转换器集合
/// </summary>
public static class StringConverters
{
    /// <summary>
    /// 字符串非空/非 null 时返回 true，用于 IsVisible 绑定
    /// </summary>
    public static readonly FuncValueConverter<string?, bool> IsNotNullOrEmpty = new(
        value => !string.IsNullOrEmpty(value));
}

/// <summary>
/// 整数与布尔值相关的转换器集合
/// </summary>
public static class IntConverters
{
    /// <summary>
    /// 整数大于 0 时返回 true，用于集合非空时显示 UI
    /// </summary>
    public static readonly FuncValueConverter<int, bool> GreaterThanZero = new(
        value => value > 0);
}

/// <summary>
/// 字符串图标名到 FluentIcons Symbol 枚举的转换器
/// 用于密码显示/隐藏切换等场景
/// </summary>
public class StringToSymbolConverter : IValueConverter
{
    public static readonly StringToSymbolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string symbolName && !string.IsNullOrEmpty(symbolName))
        {
            if (Enum.TryParse<Symbol>(symbolName, out var symbol))
            {
                return symbol;
            }
        }
        return Symbol.Question;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Symbol symbol)
        {
            return symbol.ToString();
        }
        return string.Empty;
    }
}
