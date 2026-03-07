using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ClassScreenLock.Services;

/// <summary>
/// 双向守护服务：主进程监控看门狗进程，确保看门狗进程不会被终止
/// </summary>
public class MutualProtectionService : IDisposable
{
    private static readonly Lazy<MutualProtectionService> _instance = new(() => new MutualProtectionService());
    public static MutualProtectionService Instance => _instance.Value;
    
    private Process? _watchdogProcess;
    private Timer? _monitorTimer;
    private readonly object _lock = new();
    private bool _isRunning;
    private int _restartCount;
    private readonly TimeSpan _restartDelay = TimeSpan.FromMilliseconds(500);
    private readonly int _maxRestarts = 5;
    
    private MutualProtectionService()
    {
    }
    
    /// <summary>
    /// 启动互相守护服务
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;
        
        try
        {
            _isRunning = true;
            _restartCount = 0;
            
            StartWatchdogMonitor();
            
            LogService.Instance.Log("MutualProtection", "Started", "System");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MutualProtection.Start", "System", ex.ToString());
        }
    }
    
    /// <summary>
    /// 停止互相守护服务
    /// </summary>
    public void Stop()
    {
        try
        {
            _isRunning = false;
            
            _monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _monitorTimer?.Dispose();
            _monitorTimer = null;
            
            if (_watchdogProcess != null && !_watchdogProcess.HasExited)
            {
                try
                {
                    _watchdogProcess.Kill();
                    // 不等待看门狗进程退出，直接返回
                    // _watchdogProcess.WaitForExit(5000);
                }
                catch { }
                finally
                {
                    _watchdogProcess.Dispose();
                    _watchdogProcess = null;
                }
            }
            
            LogService.Instance.Log("MutualProtection", "Stopped", "System");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MutualProtection.Stop", "System", ex.ToString());
        }
    }
    
    /// <summary>
    /// 启动看门狗进程监控
    /// </summary>
    private void StartWatchdogMonitor()
    {
        try
        {
            StartOrFindWatchdog();
            
            _monitorTimer = new Timer(MonitorWatchdogCallback, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MutualProtection.StartWatchdog", "System", ex.ToString());
        }
    }
    
    /// <summary>
    /// 启动或查找看门狗进程
    /// </summary>
    private void StartOrFindWatchdog()
    {
        try
        {
            var existing = Process.GetProcessesByName("CSL.Watchdog");
            if (existing.Length > 0)
            {
                _watchdogProcess = existing[0];
                LogService.Instance.Log("MutualProtection", "WatchdogFound", $"PID: {existing[0].Id}");
                return;
            }
            
            var baseDir = AppContext.BaseDirectory;
            string watchdogExe = Path.Combine(baseDir, "CSL.Watchdog.exe");
            
            if (File.Exists(watchdogExe))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" \"{watchdogExe}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                Process.Start(startInfo);
                LogService.Instance.Log("MutualProtection", "WatchdogStarted", "Detached");
            }
            else
            {
                LogService.Instance.Log("Error", "MutualProtection", "System", $"Watchdog executable not found: {watchdogExe}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MutualProtection.StartOrFind", "System", ex.ToString());
        }
    }
    
    /// <summary>
    /// 监控看门狗进程的回调
    /// </summary>
    private void MonitorWatchdogCallback(object? state)
    {
        if (!_isRunning) return;
        
        try
        {
            lock (_lock)
            {
                var existing = Process.GetProcessesByName("CSL.Watchdog");
                if (existing.Length == 0)
                {
                    if (_restartCount < _maxRestarts)
                    {
                        LogService.Instance.Log("MutualProtection", "WatchdogExited", $"Restarting... (Count: {_restartCount + 1})");
                        _restartCount++;
                        Task.Delay(_restartDelay).ContinueWith(_ => StartOrFindWatchdog());
                    }
                    else
                    {
                        LogService.Instance.Log("Error", "MutualProtection", "System", $"Watchdog restart limit reached ({_maxRestarts})");
                    }
                }
                else
                {
                    _watchdogProcess = existing[0];
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MutualProtection.Monitor", "System", ex.ToString());
        }
    }
    
    /// <summary>
    /// 重置重启计数器
    /// </summary>
    public void ResetRestartCounter()
    {
        lock (_lock)
        {
            _restartCount = 0;
        }
    }
    
    /// <summary>
    /// 获取看门狗进程状态
    /// </summary>
    public bool IsWatchdogRunning
    {
        get
        {
            try
            {
                return _watchdogProcess != null && !_watchdogProcess.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }
    
    public void Dispose()
    {
        Stop();
    }
}
