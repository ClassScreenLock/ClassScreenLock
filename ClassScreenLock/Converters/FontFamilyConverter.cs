using Avalonia.Data.Converters;
using Avalonia.Media;
using ClassScreenLock.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassScreenLock.Converters
{
    public class FontFamilyConverter : IValueConverter
    {
        public static FontFamilyConverter Instance { get; } = new FontFamilyConverter();

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string fontName && !string.IsNullOrWhiteSpace(fontName))
            {
                try
                {
                    // 对于预览，我们希望尽可能准确。
                    // 只要系统中有这个字体，我们直接创建一个只包含该名字的 FontFamily 对象。
                    // 这样可以完全避免 fallback 链中其他字体的干扰。
                    if (FontManager.Current.SystemFonts.Any(f => f.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase)))
                    {
                        // 在预览时，如果只是单个字体名，不加引号在 Avalonia 中渲染最准确。
                        return new FontFamily(fontName);
                    }

                    // 如果系统没找到（可能是别名），则使用回退链构建
                    var fallbackString = string.Join(", ", FontHelper.BuildFontFallbackChain(fontName));
                    return new FontFamily(fallbackString);
                }
                catch
                {
                    return new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");
                }
            }
            return new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is FontFamily fontFamily)
            {
                return fontFamily.Name;
            }
            return string.Empty;
        }
    }
}
