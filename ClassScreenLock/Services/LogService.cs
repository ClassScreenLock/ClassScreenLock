using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ClassScreenLock.Services;

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty; // "App" or "Network"
    public string Action { get; set; } = string.Empty; // "Blocked"
    public string Target { get; set; } = string.Empty; // Process name or Domain
    public string Details { get; set; } = string.Empty;
}

public class LogService
{
    private static readonly LogService _instance = new();
    public static LogService Instance => _instance;

    private static readonly string LogFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Data",
        "logs.json");

    private const int MaxLogEntries = 500;
    private readonly object _lock = new();
    private List<LogEntry>? _cachedLogs;

    private LogService() { }

    public void Log(string type, string action, string target, string details = "")
    {
        lock (_lock)
        {
            try
            {
                if (_cachedLogs == null)
                {
                    _cachedLogs = LoadLogs();
                }

                _cachedLogs.Insert(0, new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Type = type,
                    Action = action,
                    Target = target,
                    Details = details
                });

                // Keep only the latest MaxLogEntries
                if (_cachedLogs.Count > MaxLogEntries)
                {
                    _cachedLogs = _cachedLogs.Take(MaxLogEntries).ToList();
                }

                SaveLogs(_cachedLogs);
                System.Diagnostics.Debug.WriteLine($"[LOG][{type}][{action}] {target}: {details}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to log: {ex.Message}");
            }
        }
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            try
            {
                _cachedLogs = new List<LogEntry>();
                if (File.Exists(LogFilePath))
                {
                    File.Delete(LogFilePath);
                }
            }
            catch { }
        }
    }

    public List<LogEntry> LoadLogs()
    {
        lock (_lock)
        {
            if (_cachedLogs != null) return new List<LogEntry>(_cachedLogs);

            try
            {
                var directory = Path.GetDirectoryName(LogFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                if (!File.Exists(LogFilePath))
                {
                    _cachedLogs = new List<LogEntry>();
                    return _cachedLogs;
                }

                var json = File.ReadAllText(LogFilePath);
                _cachedLogs = JsonSerializer.Deserialize<List<LogEntry>>(json) ?? new List<LogEntry>();
                return new List<LogEntry>(_cachedLogs);
            }
            catch
            {
                _cachedLogs = new List<LogEntry>();
                return _cachedLogs;
            }
        }
    }

    private void SaveLogs(List<LogEntry> logs)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(logs, options);
            File.WriteAllText(LogFilePath, json);
        }
        catch { }
    }
}
