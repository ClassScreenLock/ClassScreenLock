using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace ClassScreenLock.Services;

public class InterceptedContent
{
    public DateTime Timestamp { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public float Confidence { get; set; }
}

public class InterceptionDatabase
{
    private static readonly InterceptionDatabase _instance = new();
    public static InterceptionDatabase Instance => _instance;

    private static readonly string DbFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Data",
        "violation_logs.json");

    private readonly object _lock = new();
    private List<InterceptedContent> _cache = new();

    private InterceptionDatabase()
    {
        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(DbFilePath))
                {
                    var json = File.ReadAllText(DbFilePath);
                    _cache = JsonSerializer.Deserialize<List<InterceptedContent>>(json) ?? new();
                }
            }
            catch { _cache = new(); }
        }
    }

    public void Add(InterceptedContent entry)
    {
        lock (_lock)
        {
            try
            {
                _cache.Insert(0, entry);
                if (_cache.Count > 1000) _cache = _cache.Take(1000).ToList();
                Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to add interception entry: {ex.Message}");
            }
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(DbFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DbFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save interception database: {ex.Message}");
        }
    }

    public List<InterceptedContent> GetHistory()
    {
        lock (_lock) return _cache.ToList();
    }
}
