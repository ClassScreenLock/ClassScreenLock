using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ClassScreenLock.Converters
{
    public class SidebarIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isExpanded)
            {
                if (parameter?.ToString() == "Tooltip")
                {
                    return isExpanded ? "收起侧边栏" : "展开侧边栏";
                }
                return isExpanded ? "fas fa-chevron-left" : "fas fa-chevron-right";
            }
            return "fas fa-chevron-left";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return AvaloniaProperty.UnsetValue;
        }
    }
}
