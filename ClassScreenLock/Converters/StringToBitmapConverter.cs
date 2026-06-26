using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ClassScreenLock.Converters;

public class StringToBitmapConverter : IValueConverter
{
    private const int MaxCacheSize = 100;
    private const int MaxThumbnailCacheSize = 150;
    
    private static readonly LinkedList<CacheEntry> _cacheList = new();
    private static readonly LinkedList<CacheEntry> _thumbCacheList = new();
    private static readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheIndex = new();
    private static readonly Dictionary<string, LinkedListNode<CacheEntry>> _thumbCacheIndex = new();
    private static readonly object _cacheLock = new();

    private class CacheEntry : IDisposable
    {
        public string Path { get; set; } = string.Empty;
        public Bitmap? Bitmap { get; set; }
        public DateTime LastAccess { get; set; }

        public void Dispose()
        {
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            bool isThumbnail = parameter is string p && p == "thumb";

            lock (_cacheLock)
            {
                var cacheIndex = isThumbnail ? _thumbCacheIndex : _cacheIndex;
                var cacheList = isThumbnail ? _thumbCacheList : _cacheList;

                if (cacheIndex.TryGetValue(path, out var node))
                {
                    node.Value.LastAccess = DateTime.Now;
                    cacheList.Remove(node);
                    cacheList.AddFirst(node);
                    return node.Value.Bitmap;
                }
            }

            using var stream = File.OpenRead(path);
            Bitmap bitmap;

            if (isThumbnail)
            {
                bitmap = Bitmap.DecodeToWidth(stream, 240);
            }
            else
            {
                bitmap = new Bitmap(stream);
            }

            lock (_cacheLock)
            {
                var cacheIndex = isThumbnail ? _thumbCacheIndex : _cacheIndex;
                var cacheList = isThumbnail ? _thumbCacheList : _cacheList;
                var maxSize = isThumbnail ? MaxThumbnailCacheSize : MaxCacheSize;

                if (cacheIndex.ContainsKey(path))
                {
                    bitmap.Dispose();
                    return null;
                }

                var entry = new CacheEntry
                {
                    Path = path,
                    Bitmap = bitmap,
                    LastAccess = DateTime.Now
                };

                var newNode = cacheList.AddFirst(entry);
                cacheIndex[path] = newNode;

                while (cacheList.Count > maxSize)
                {
                    var last = cacheList.Last;
                    if (last != null)
                    {
                        cacheIndex.Remove(last.Value.Path);
                        last.Value.Dispose();
                        cacheList.RemoveLast();
                    }
                }
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            foreach (var entry in _cacheList)
            {
                entry.Dispose();
            }
            foreach (var entry in _thumbCacheList)
            {
                entry.Dispose();
            }
            _cacheList.Clear();
            _thumbCacheList.Clear();
            _cacheIndex.Clear();
            _thumbCacheIndex.Clear();
        }
    }

    public static void RemoveFromCache(string path)
    {
        lock (_cacheLock)
        {
            if (_cacheIndex.TryGetValue(path, out var node))
            {
                node.Value.Dispose();
                _cacheList.Remove(node);
                _cacheIndex.Remove(path);
            }
            if (_thumbCacheIndex.TryGetValue(path, out var thumbNode))
            {
                thumbNode.Value.Dispose();
                _thumbCacheList.Remove(thumbNode);
                _thumbCacheIndex.Remove(path);
            }
        }
    }

    public static int GetCacheSize()
    {
        lock (_cacheLock)
        {
            return _cacheList.Count + _thumbCacheList.Count;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
