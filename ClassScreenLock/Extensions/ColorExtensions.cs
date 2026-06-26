using System;
using Avalonia.Media;

namespace ClassScreenLock.Extensions
{
    /// <summary>
    /// 颜色扩展方法
    /// </summary>
    public static class ColorExtensions
    {
        /// <summary>
        /// 调整颜色亮度
        /// </summary>
        /// <param name="color">原始颜色</param>
        /// <param name="percent">亮度调整百分比，正值为变亮，负值为变暗</param>
        /// <returns>调整后的颜色</returns>
        public static Color LightenPercent(this Color color, float percent)
        {
            float red = color.R;
            float green = color.G;
            float blue = color.B;
            float alpha = color.A;

            // 计算调整因子
            float factor = 1 + percent / 100f;

            // 调整颜色值
            red = Math.Min(255, red * factor);
            green = Math.Min(255, green * factor);
            blue = Math.Min(255, blue * factor);

            return Color.FromArgb((byte)alpha, (byte)red, (byte)green, (byte)blue);
        }

        /// <summary>
        /// 调整颜色亮度（使用绝对值）
        /// </summary>
        /// <param name="color">原始颜色</param>
        /// <param name="amount">亮度调整量，0-255</param>
        /// <returns>调整后的颜色</returns>
        public static Color Lighten(this Color color, int amount)
        {
            int red = Math.Min(255, color.R + amount);
            int green = Math.Min(255, color.G + amount);
            int blue = Math.Min(255, color.B + amount);

            return Color.FromArgb(color.A, (byte)red, (byte)green, (byte)blue);
        }

        /// <summary>
        /// 调整颜色暗度（使用绝对值）
        /// </summary>
        /// <param name="color">原始颜色</param>
        /// <param name="amount">暗度调整量，0-255</param>
        /// <returns>调整后的颜色</returns>
        public static Color Darken(this Color color, int amount)
        {
            int red = Math.Max(0, color.R - amount);
            int green = Math.Max(0, color.G - amount);
            int blue = Math.Max(0, color.B - amount);

            return Color.FromArgb(color.A, (byte)red, (byte)green, (byte)blue);
        }
    }
}