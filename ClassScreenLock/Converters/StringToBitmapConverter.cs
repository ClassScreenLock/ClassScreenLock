using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ClassScreenLock.Converters;

public class StringToBitmapConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();
    private static readonly ConcurrentDictionary<string, Bitmap> _thumbCache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            try
            {
                if (!File.Exists(path)) return null;

                bool isThumbnail = parameter is string p && p == "thumb";
                var cache = isThumbnail ? _thumbCache : _cache;

                if (cache.TryGetValue(path, out var cached))
                {
                    return cached;
                }

                using var stream = File.OpenRead(path);
                Bitmap bitmap;

                if (isThumbnail)
                {
                    // Decode to tile width to reduce CPU/memory for grids (240px matches thumbnail tile)
                    bitmap = Bitmap.DecodeToWidth(stream, 240);
                }
                else
                {
                    bitmap = new Bitmap(stream);
                }

                if (cache.Count > 500)
                {
                    ClearCache();
                }

                cache[path] = bitmap;
                return bitmap;
            }
            catch { }
        }
        return null;
    }

    public static void ClearCache()
    {
        _cache.Clear();
        _thumbCache.Clear();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
