using System;
using System.IO;
using System.Threading;

namespace CSL.WatchdogService;

/// <summary>
/// 服务端简易文件日志器。由于服务运行在 Session 0，无法使用主程序的 LogService，
/// 因此使用独立的文件日志来记录服务运行状态。
/// </summary>
internal static class ServiceLogger
{
    private static readonly object _lock = new();
    private static readonly string _logDirectory = InitializeLogDirectory();
    private static string _logFilePath = Path.Combine(_logDirectory, "watchdog_service.log");

    // 日志文件最大大小（5 MB），超过后自动轮转
    private const long MaxLogSize = 5 * 1024 * 1024;

    private static string InitializeLogDirectory()
    {
        try
        {
            // 使用系统临时目录而非主程序 Data\Logs，避免污染主程序日志目录
            string logDir = Path.Combine(Path.GetTempPath(), "ClassScreenLock");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            return logDir;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                // 检查日志文件大小，超过则轮转
                try
                {
                    if (File.Exists(_logFilePath))
                    {
                        var fi = new FileInfo(_logFilePath);
                        if (fi.Length > MaxLogSize)
                        {
                            string archivePath = _logFilePath.Replace(".log", $".{DateTime.Now:yyyyMMdd_HHmmss}.log");
                            try { File.Move(_logFilePath, archivePath); }
                            catch { }
                        }
                    }
                }
                catch { }

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
        }
        catch
        {
            // 日志记录失败不应影响服务运行
        }
    }

    public static void Log(string category, string message)
    {
        Log($"[{category}] {message}");
    }

    public static void LogError(string message, Exception? ex = null)
    {
        if (ex != null)
        {
            Log($"[ERROR] {message}: {ex}");
        }
        else
        {
            Log($"[ERROR] {message}");
        }
    }
}
