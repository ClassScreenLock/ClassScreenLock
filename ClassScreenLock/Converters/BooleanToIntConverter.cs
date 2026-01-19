using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters
{
    public class BooleanToIntConverter : IValueConverter
    {
        public static readonly BooleanToIntConverter Instance = new();
        
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? 0 : 1; // true=系统强调色(索引0)，false=自定义颜色(索引1)
            }
            return 1; // 默认返回自定义颜色
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue == 0; // 索引0为true(系统强调色)，其他为false(自定义颜色)
            }
            return false;
        }
    }
}