using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters;

/// <summary>
/// 整数相等比较转换器：value 与 parameter（字符串形式的 int）相等时返回 true。
/// 用于 SelectedTabIndex 驱动内容区切换。
/// </summary>
public class IntEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter?.ToString() is string paramStr
            && int.TryParse(paramStr, out var paramInt))
        {
            return intValue == paramInt;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
