using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace ClassScreenLock.Helpers
{
    public static class FontHelper
    {
        public static FontFamily BuildGlobalFontFamily(string? fontFamily)
        {
            var normalized = NormalizeFontName(fontFamily);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                var fallbackString = string.Join(", ", BuildDefaultFallbackChain());
                return new FontFamily(fallbackString);
            }

            if (IsHarmonyOsSelection(normalized))
            {
                var tokens = new[]
                {
                    "avares://ClassScreenLock/Assets/Fonts#HarmonyOS Sans SC",
                    "avares://ClassScreenLock/Assets/Fonts#HarmonyOS Sans SC Light"
                };

                return new FontFamily(string.Join(", ", tokens));
            }

            try
            {
                if (FontManager.Current.SystemFonts.Any(f => f.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    return new FontFamily(normalized);
                }
            }
            catch
            {
            }

            var fallback = string.Join(", ", BuildFontFallbackChain(normalized));
            return new FontFamily(fallback);
        }

        public static FontWeight BuildGlobalFontWeight(string? fontFamily)
        {
            return FontWeight.Normal;
        }

        public static bool IsHarmonyOsSelection(string? fontFamily)
        {
            var normalized = NormalizeFontName(fontFamily);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (normalized.StartsWith("avares://ClassScreenLock/Assets/Fonts#HarmonyOS Sans SC", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.Contains("HarmonyOS", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.Contains("鸿蒙", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static IEnumerable<string> BuildFontFallbackChain(string? fontFamily)
        {
            var raw = new List<string>();
            
            if (string.IsNullOrWhiteSpace(fontFamily))
                return BuildDefaultFallbackChain();

            if (IsHarmonyOsSelection(fontFamily))
            {
                raw.Add("avares://ClassScreenLock/Assets/Fonts#HarmonyOS Sans SC Light");
                raw.Add("avares://ClassScreenLock/Assets/Fonts#HarmonyOS Sans SC");
                raw.Add("Microsoft YaHei UI");
                raw.Add("Microsoft YaHei");
                raw.Add("Segoe UI");
                raw.Add("SimSun");
                raw.Add("sans-serif");

                return raw
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(ToFontFamilyToken);
            }

            // 1. 首先添加原始名称
            raw.Add(fontFamily);

            // 2. 尝试从系统字体库中寻找匹配的家族名 (解决大小写或轻微名称差异)
            try 
            {
                var systemMatch = FontManager.Current.SystemFonts
                    .FirstOrDefault(f => f.Name.Equals(fontFamily, StringComparison.OrdinalIgnoreCase));
                if (systemMatch != null && systemMatch.Name != fontFamily)
                {
                    raw.Add(systemMatch.Name);
                }
            } catch { /* ignore */ }

            var normalized = NormalizeFontName(fontFamily);
            if (!string.IsNullOrWhiteSpace(normalized) && normalized != fontFamily)
            {
                raw.Add(normalized);
            }

            // 3. 添加别名和变体
            foreach (var alias in GetFontAliases(normalized))
                raw.Add(alias);

            // 4. 基础系统回退链
            raw.Add("Microsoft YaHei UI");
            raw.Add("Microsoft YaHei");
            raw.Add("Segoe UI");
            raw.Add("SimSun");
            raw.Add("sans-serif");

            return raw
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(ToFontFamilyToken);
        }

        private static IEnumerable<string> BuildDefaultFallbackChain()
        {
            yield return "Microsoft YaHei UI";
            yield return "Microsoft YaHei";
            yield return "Segoe UI";
            yield return "SimSun";
            yield return "sans-serif";
        }

        public static string NormalizeFontName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var trimmed = name.Trim();
            if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) || (trimmed.StartsWith("'") && trimmed.EndsWith("'")))
                trimmed = trimmed.Substring(1, trimmed.Length - 2);

            return trimmed.Trim();
        }

        public static string ToFontFamilyToken(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            if (name.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                return name;

            if (name.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("serif", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("monospace", StringComparison.OrdinalIgnoreCase))
                return name;

            var safe = name.Replace("\"", string.Empty).Trim();
            
            if (safe.Contains(" ") || safe.Contains(","))
            {
                return $"\"{safe}\"";
            }
            
            return safe;
        }

        private static IEnumerable<string> GetFontAliases(string normalizedFontName)
        {
            if (string.IsNullOrWhiteSpace(normalizedFontName))
                yield break;

            // 基础映射逻辑
            static IEnumerable<string> Pair(string a, string b, string current)
            {
                if (current.Equals(a, StringComparison.OrdinalIgnoreCase)) yield return b;
                else if (current.Equals(b, StringComparison.OrdinalIgnoreCase)) yield return a;
            }

            // 系统内置常用字体映射
            foreach (var v in Pair("SimSun", "宋体", normalizedFontName)) yield return v;
            foreach (var v in Pair("NSimSun", "新宋体", normalizedFontName)) yield return v;
            foreach (var v in Pair("SimHei", "黑体", normalizedFontName)) yield return v;
            foreach (var v in Pair("KaiTi", "楷体", normalizedFontName)) yield return v;
            foreach (var v in Pair("FangSong", "仿宋", normalizedFontName)) yield return v;
            foreach (var v in Pair("Microsoft YaHei", "微软雅黑", normalizedFontName)) yield return v;
            foreach (var v in Pair("Microsoft JhengHei", "微软正黑体", normalizedFontName)) yield return v;
            foreach (var v in Pair("DengXian", "等线", normalizedFontName)) yield return v;
            foreach (var v in Pair("YouYuan", "幼圆", normalizedFontName)) yield return v;

            // Adobe 常用字体映射
            if (normalizedFontName.Contains("Adobe", StringComparison.OrdinalIgnoreCase))
            {
                // 基础对
                foreach (var v in Pair("Adobe Heiti Std", "Adobe 黑体 Std", normalizedFontName)) yield return v;
                foreach (var v in Pair("Adobe Song Std", "Adobe 宋体 Std", normalizedFontName)) yield return v;
                foreach (var v in Pair("Adobe Kaiti Std", "Adobe 楷体 Std", normalizedFontName)) yield return v;
                foreach (var v in Pair("Adobe Fangsong Std", "Adobe 仿宋 Std", normalizedFontName)) yield return v;
                foreach (var v in Pair("Adobe Ming Std", "Adobe 明体 Std", normalizedFontName)) yield return v;
                foreach (var v in Pair("Adobe Myungjo Std", "Adobe 明朝 Std", normalizedFontName)) yield return v;

                // 自动尝试追加常见后缀变体
                var adobeSuffixes = new[] { " Std", " Pro", " Std R", " Std L", " Std M", " Std B", " Pro R", " Pro L" };
                var baseName = normalizedFontName;
                foreach (var s in adobeSuffixes)
                {
                    if (normalizedFontName.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        baseName = normalizedFontName.Substring(0, normalizedFontName.Length - s.Length).Trim();
                        break;
                    }
                }

                if (baseName != normalizedFontName)
                {
                    yield return baseName;
                }

                foreach (var s in adobeSuffixes)
                {
                    var variant = baseName + s;
                    if (!variant.Equals(normalizedFontName, StringComparison.OrdinalIgnoreCase))
                        yield return variant;
                }
            }

            // 处理带字重后缀的小众字体干扰
            // 如果字体名以 " R" " L" " Regular" " Light" 等结尾，尝试添加去掉后缀的版本
            var suffixes = new[] { " Regular", " Light", " Medium", " Bold", " SemiBold", " R", " L", " M", " B" };
            foreach (var suffix in suffixes)
            {
                if (normalizedFontName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return normalizedFontName.Substring(0, normalizedFontName.Length - suffix.Length).Trim();
                }
            }
        }
    }
}
