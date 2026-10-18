using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClassScreenLock.Services;

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class LogService : IDisposable
{
    private static readonly LogService _instance = new();
    public static LogService Instance => _instance;

    private static readonly string LogDirectory = Path.Combine(
        Helpers.AppPathHelper.AppDirectory,
        "Data",
        "Logs");

    private readonly object _lock = new();
    private Dictionary<string, List<LogEntry>> _cachedLogs = new();
    private readonly HashSet<string> _dirtyDates = new(StringComparer.Ordinal);
    private Timer? _flushTimer;
    private bool _disposed;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private const int MaxUnflushedEntriesPerDay = 80;

    private LogService()
    {
        _flushTimer = new Timer(OnFlushTimer, null, FlushInterval, FlushInterval);
    }

    public void FlushNow()
    {
        OnFlushTimer(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer?.Dispose();
        _flushTimer = null;
        FlushDirty();
    }

    private void OnFlushTimer(object? state)
    {
        FlushDirty();
    }

    private string GetLogFilePath(DateTime date)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        return Path.Combine(LogDirectory, $"logs_{dateStr}.json");
    }

    private string GetDateKey(DateTime date)
    {
        return date.ToString("yyyy-MM-dd");
    }

    public void Log(string type, string action, string target, string details = "")
    {
        lock (_lock)
        {
            try
            {
                var now = DateTime.Now;
                var dateKey = GetDateKey(now);

                if (!_cachedLogs.TryGetValue(dateKey, out var dayLogs))
                {
                    dayLogs = LoadDayLogs(now);
                    _cachedLogs[dateKey] = dayLogs;
                }

                dayLogs.Insert(0, new LogEntry
                {
                    Timestamp = now,
                    Type = type,
                    Action = action,
                    Target = target,
                    Details = details
                });

                // 标记脏数据，延迟批量写入
                _dirtyDates.Add(dateKey);

                // 如果当日累积条目过多，立即刷盘
                if (dayLogs.Count % MaxUnflushedEntriesPerDay == 0)
                {
                    FlushDateLocked(dateKey, dayLogs);
                    _dirtyDates.Remove(dateKey);
                }

                System.Diagnostics.Debug.WriteLine($"[LOG][{type}][{action}] {target}: {details}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to log: {ex.Message}");
            }
        }
    }

    private void FlushDirty()
    {
        List<string> datesToFlush;
        lock (_lock)
        {
            if (_dirtyDates.Count == 0) return;
            datesToFlush = _dirtyDates.ToList();
        }

        foreach (var dateKey in datesToFlush)
        {
            lock (_lock)
            {
                if (_cachedLogs.TryGetValue(dateKey, out var dayLogs))
                {
                    FlushDateLocked(dateKey, dayLogs);
                    _dirtyDates.Remove(dateKey);
                }
            }
        }
    }

    private static void FlushDateLocked(string dateKey, List<LogEntry> logs)
    {
        // 必须在 _lock 内调用
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            var filePath = Path.Combine(LogDirectory, $"logs_{dateKey}.json");
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(logs, options);
            File.WriteAllText(filePath, json);
        }
        catch { }
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            try
            {
                _cachedLogs.Clear();
                _dirtyDates.Clear();
                if (Directory.Exists(LogDirectory))
                {
                    foreach (var file in Directory.GetFiles(LogDirectory, "logs_*.json"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }
    }

    public void ClearDayLogs(DateTime date)
    {
        lock (_lock)
        {
            try
            {
                var dateKey = GetDateKey(date);
                _cachedLogs.Remove(dateKey);
                _dirtyDates.Remove(dateKey);

                var filePath = GetLogFilePath(date);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch { }
        }
    }

    public List<LogEntry> LoadLogs()
    {
        lock (_lock)
        {
            // 加载前先刷盘，确保磁盘数据是最新的
            var datesToFlush = _dirtyDates.ToList();
            foreach (var dk in datesToFlush)
            {
                if (_cachedLogs.TryGetValue(dk, out var dayLogs))
                {
                    FlushDateLocked(dk, dayLogs);
                    _dirtyDates.Remove(dk);
                }
            }

            var allLogs = new List<LogEntry>();

            try
            {
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                    return allLogs;
                }

                var files = Directory.GetFiles(LogDirectory, "logs_*.json");
                var sortedFiles = files.OrderByDescending(f => f);

                foreach (var file in sortedFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var dateKey = fileName.Replace("logs_", "");

                    if (!_cachedLogs.TryGetValue(dateKey, out var dayLogs))
                    {
                        dayLogs = LoadDayLogsFromFile(file);
                        _cachedLogs[dateKey] = dayLogs;
                    }

                    allLogs.AddRange(dayLogs);
                }
            }
            catch { }

            return allLogs;
        }
    }

    public List<LogEntry> LoadDayLogs(DateTime date)
    {
        var filePath = GetLogFilePath(date);
        return LoadDayLogsFromFile(filePath);
    }

    private List<LogEntry> LoadDayLogsFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new List<LogEntry>();
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<LogEntry>>(json) ?? new List<LogEntry>();
        }
        catch
        {
            return new List<LogEntry>();
        }
    }

    public List<string> GetAvailableDates()
    {
        var dates = new List<string>();

        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                return dates;
            }

            var files = Directory.GetFiles(LogDirectory, "logs_*.json");
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var dateStr = fileName.Replace("logs_", "");
                dates.Add(dateStr);
            }

            dates.Sort();
            dates.Reverse();
        }
        catch { }

        return dates;
    }

    public static void Observe(Task? task, string source)
    {
        if (task == null) return;
        task.ContinueWith(t =>
        {
            try
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    var ex = t.Exception.Flatten();
                    Instance.Log("Error", "TaskFault", source, ex.ToString());
                }
            }
            catch { }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
