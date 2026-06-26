using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace ClassScreenLock.Services;

/// <summary>
/// 进程相关的全局常量与辅助方法。
/// 统一 OwnProcessNames 的定义，避免 AppBlockingService / AppManagementViewModel 各定义一份导致不同步。
/// </summary>
public static class ProcessConstants
{
    /// <summary>
    /// 自身进程名集合：黑名单匹配 / 阻止列表保护 / PowerShell 命令行 allow-marker 共同使用。
    /// </summary>
    public static readonly System.Collections.Generic.HashSet<string> OwnProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ClassScreenLock",
            "CSL.Watchdog",
            "MonitorProcess",
            "BreakButtonProcess"
        };

    /// <summary>
    /// 判断是否是自身进程。
    /// </summary>
    public static bool IsOwnProcess(string? processName)
    {
        return !string.IsNullOrEmpty(processName) && OwnProcessNames.Contains(processName);
    }
}

/// <summary>
/// 进程命令行获取器。.NET 9 的 Process 没有公开 CommandLine，
/// 这里使用 WMI Win32_Process.CommandLine。注意：
/// 1. 必须 STA 线程访问（首次访问会触发 CoInitializeSecurity）；
/// 2. WMI 较慢，需要做短 TTL 缓存；
/// 3. 受保护进程可能读不到，对应情况视为"未知"，调用方应按需降级。
/// </summary>
public static class ProcessCommandLineReader
{
    private struct CacheEntry
    {
        public string CommandLine;
        public DateTime Stamp;
    }

    private static readonly ConcurrentDictionary<uint, CacheEntry> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);
    private static readonly object _initLock = new();
    private static bool _initialized;

    public static void Invalidate(uint pid) => _cache.TryRemove(pid, out _);

    public static string GetCommandLine(Process process)
    {
        if (process == null) return string.Empty;
        return GetCommandLine((uint)process.Id);
    }

    public static string GetCommandLine(uint pid)
    {
        if (pid == 0) return string.Empty;

        if (_cache.TryGetValue(pid, out var hit) && (DateTime.UtcNow - hit.Stamp) < CacheTtl)
        {
            return hit.CommandLine;
        }

        var cmd = QueryWmi(pid);
        _cache[pid] = new CacheEntry { CommandLine = cmd, Stamp = DateTime.UtcNow };

        // 简单回收：超过 256 项时全清空
        if (_cache.Count > 256) _cache.Clear();

        return cmd;
    }

    private static string QueryWmi(uint pid)
    {
        try
        {
            EnsureInitialized();
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var v = mo["CommandLine"]?.ToString();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
        }
        catch
        {
            // WMI 失败：受保护进程 / 权限不足 / WMI 服务异常。
            // 返回空字符串，由调用方根据业务规则处理（不要默认"安全"，也不要默认"危险"）。
        }
        return string.Empty;
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            try
            {
                // 部分环境下首次 WMI 调用会非常慢且偶发失败，
                // 这里预热一次（使用 SELECT 1 这种零成本查询）。
                using var warmup = new ManagementObjectSearcher("SELECT 1");
                _ = warmup.Get().Count;
            }
            catch
            {
                // 预热失败不影响后续调用
            }
            _initialized = true;
        }
    }
}
