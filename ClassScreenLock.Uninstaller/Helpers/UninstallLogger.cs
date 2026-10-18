using System;
using System.IO;
using System.Text;

namespace ClassScreenLock.Uninstaller.Helpers;

/// <summary>
/// 卸载日志记录器
/// 将卸载过程写入日志文件，便于排查问题
/// </summary>
public static class UninstallLogger
{
    private static readonly object _lock = new();
    private static string? _logPath;
    private static bool _initialized;

    /// <summary>
    /// 日志文件路径（默认为系统临时目录下的 CSL_Uninstall_时间戳.log）
    /// </summary>
    public static string LogPath
    {
        get
        {
            if (_logPath == null)
            {
                var tempDir = Path.GetTempPath();
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logPath = Path.Combine(tempDir, $"CSL_Uninstall_{timestamp}.log");
            }
            return _logPath;
        }
    }

    public static void Init()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;

            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sb = new StringBuilder();
                sb.AppendLine("=== ClassScreenLock 卸载日志 ===");
                sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"卸载程序: {Environment.ProcessPath}");
                sb.AppendLine($"操作系统: {Environment.OSVersion}");
                sb.AppendLine($"用户: {Environment.UserName}");
                sb.AppendLine($"64位系统: {Environment.Is64BitOperatingSystem}");
                sb.AppendLine();
                sb.AppendLine("--- 卸载流程开始 ---");
                sb.AppendLine();

                File.WriteAllText(LogPath, sb.ToString());
                _initialized = true;
            }
            catch
            {
                // 日志初始化失败不影响卸载流程
            }
        }
    }

    public static void Info(string message)
    {
        WriteLog("INFO", message);
    }

    public static void Warning(string message)
    {
        WriteLog("WARN", message);
    }

    public static void Error(string message)
    {
        WriteLog("ERROR", message);
    }

    public static void Step(string stepName, string description)
    {
        WriteLog("STEP", $"[{stepName}] {description}");
    }

    private static void WriteLog(string level, string message)
    {
        try
        {
            if (!_initialized) Init();

            var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";

            lock (_lock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志写入失败不影响主流程
        }
    }
}
