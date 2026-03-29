using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ClassScreenLock.Services;

namespace ClassScreenLock.Helpers;

public static class AutoStartHelper
{
    private const string AppName = "ClassScreenLock";
    private const string StartupArgs = "--minimized";
    
    // 定时检查机制
    private static Timer? _autoStartCheckTimer;
    private static bool _isChecking = false;
    private static int _consecutiveFailures = 0; // 连续失败次数
    private static readonly TimeSpan _normalCheckInterval = TimeSpan.FromMinutes(10); // 正常检查间隔：10 分钟
    private static readonly TimeSpan _abnormalCheckInterval = TimeSpan.FromSeconds(30); // 异常检查间隔：30 秒
    private const int MAX_FAILURES_BEFORE_REPAIR = 1; // 失败 1 次立即触发修复

    public static void SetAutoStart(bool enable)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
#if WINDOWS
            var appPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(appPath))
            {
                appPath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(appPath)) return;

            // 1. 注册表
            using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (enable)
                {
                    key?.SetValue(AppName, $"\"{appPath}\" {StartupArgs}");
                }
                else
                {
                    key?.DeleteValue(AppName, false);
                }
            }

            // 2. 启动文件夹快捷方式 (双重保险)
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupFolder, $"{AppName}.lnk");

            if (enable)
            {
                CreateShortcut(appPath, shortcutPath, StartupArgs);
            }
            else
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }

            // 3. 任务计划程序 (无 UAC 提示自启动)
            ManageTaskScheduler(enable, appPath);

            LogService.Instance.Log("Info", "AutoStart", "SetAutoStart", $"自启动已{(enable ? "启用" : "禁用")} (注册表 + 启动文件夹 + 任务计划)");
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"设置自启动失败：{ex.Message}");
            LogService.Instance.Log("Error", "AutoStart", "SetAutoStart", ex.Message);
        }
    }

    public static void UpdateAutoStartPath()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
#if WINDOWS
            var appPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(appPath))
            {
                appPath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            if (string.IsNullOrWhiteSpace(appPath)) return;

            // 更新注册表
            using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key != null)
                {
                    var expectedValue = $"\"{appPath}\" {StartupArgs}";
                    var currentValue = key.GetValue(AppName) as string;
                    if (currentValue != expectedValue)
                    {
                        key.SetValue(AppName, expectedValue);
                    }
                }
            }

            // 更新启动文件夹快捷方式
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupFolder, $"{AppName}.lnk");
            if (File.Exists(shortcutPath))
            {
                // 重新创建以确保路径和参数正确
                CreateShortcut(appPath, shortcutPath, StartupArgs);
            }

            // 更新任务计划程序
            ManageTaskScheduler(true, appPath);

            LogService.Instance.Log("Info", "AutoStart", "UpdatePath", "自启动路径已更新完成");
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新自启动路径失败：{ex.Message}");
            LogService.Instance.Log("Error", "AutoStart", "UpdatePath", ex.Message);
        }
    }

    /// <summary>
    /// 检查并修复所有自启动方式，确保它们都处于启用状态
    /// 用于解决用户在任务管理器禁用后状态不一致的问题
    /// </summary>
    public static void CheckAndRepairAutoStart()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
#if WINDOWS
            var appPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(appPath))
            {
                appPath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            if (string.IsNullOrWhiteSpace(appPath)) return;

            bool needRepair = false;
            string repairReason = string.Empty;

            // 检查注册表
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
            {
                if (key != null)
                {
                    var value = key.GetValue(AppName) as string;
                    var expectedValue = $"\"{appPath}\" {StartupArgs}";
                    if (value != expectedValue)
                    {
                        needRepair = true;
                        repairReason += "注册表 ";
                    }
                }
                else
                {
                    needRepair = true;
                    repairReason += "注册表 ";
                }
            }

            // 检查启动文件夹快捷方式
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupFolder, $"{AppName}.lnk");
            if (!File.Exists(shortcutPath))
            {
                needRepair = true;
                repairReason += "启动文件夹 ";
            }

            // 跳过任务计划程序检查以减少资源占用
            // 任务计划程序检查会在定时检查中异步执行

            // 如果需要修复，重新启用所有自启动方式
            if (needRepair)
            {
                SetAutoStart(true);
            }
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检查自启动状态失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 检查任务计划程序中的自启动任务是否存在并启用
    /// </summary>
    private static bool IsTaskSchedulerEnabled()
    {
        try
        {
            var taskName = $"{AppName}AutoStart";
            var script = $"Get-ScheduledTask -TaskName '{taskName}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty State";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                {
                    process.WaitForExit(2000); // 减少等待时间从 5 秒到 2 秒
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    return output.Equals("Ready", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// 启动定时检查任务
    /// </summary>
    public static void StartPeriodicCheck()
    {
        if (_autoStartCheckTimer != null) return;

        try
        {
            // 初始延迟 10 秒后开始检查
            _autoStartCheckTimer = new Timer(
                callback: CheckAutoStartCallback,
                state: null,
                dueTime: TimeSpan.FromSeconds(10),
                period: Timeout.InfiniteTimeSpan // 不设置固定周期，由我们动态控制
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动定时检查失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 停止定时检查任务
    /// </summary>
    public static void StopPeriodicCheck()
    {
        try
        {
            _autoStartCheckTimer?.Dispose();
            _autoStartCheckTimer = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"停止定时检查失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 定时检查回调函数
    /// </summary>
    private static async void CheckAutoStartCallback(object? state)
    {
        if (_isChecking) return; // 避免重入

        try
        {
            _isChecking = true;
            await Task.Run(() =>
            {
                var isHealthy = CheckAllAutoStartMethods();
                
                if (isHealthy)
                {
                    // 检查成功，重置失败计数
                    _consecutiveFailures = 0;
                    
                    // 使用正常间隔
                    _autoStartCheckTimer?.Change(_normalCheckInterval, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    // 检查失败，增加失败计数
                    _consecutiveFailures++;
                    
                    if (_consecutiveFailures >= MAX_FAILURES_BEFORE_REPAIR)
                    {
                        // 失败 1 次，立即触发修复
                        CheckAndRepairAutoStart();
                        _consecutiveFailures = 0;
                        
                        // 修复完成后，立即恢复正常检查间隔
                        _autoStartCheckTimer?.Change(_normalCheckInterval, Timeout.InfiniteTimeSpan);
                    }
                    else
                    {
                        // 使用快速检查间隔（未达到修复阈值时）
                        _autoStartCheckTimer?.Change(_abnormalCheckInterval, Timeout.InfiniteTimeSpan);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"定时检查出错：{ex.Message}");
            _consecutiveFailures++;
            _autoStartCheckTimer?.Change(_abnormalCheckInterval, Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _isChecking = false;
        }
    }

    /// <summary>
    /// 检查所有自启动方式（不修复，只返回状态）
    /// </summary>
    private static bool CheckAllAutoStartMethods()
    {
        if (!OperatingSystem.IsWindows()) return true;

        try
        {
            var appPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(appPath))
            {
                appPath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            if (string.IsNullOrWhiteSpace(appPath)) return false;

            bool allHealthy = true;

            // 检查注册表
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
            {
                if (key != null)
                {
                    var value = key.GetValue(AppName) as string;
                    var expectedValue = $"\"{appPath}\" {StartupArgs}";
                    if (value != expectedValue)
                    {
                        allHealthy = false;
                        LogService.Instance.Log("Debug", "AutoStart", "Check", "注册表自启动项不正确");
                    }
                }
                else
                {
                    allHealthy = false;
                    LogService.Instance.Log("Debug", "AutoStart", "Check", "注册表自启动项缺失");
                }
            }

            // 检查启动文件夹快捷方式
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupFolder, $"{AppName}.lnk");
            if (!File.Exists(shortcutPath))
            {
                allHealthy = false;
                LogService.Instance.Log("Debug", "AutoStart", "Check", "启动文件夹快捷方式缺失");
            }

            // 检查任务计划程序
            if (!IsTaskSchedulerEnabled())
            {
                allHealthy = false;
                LogService.Instance.Log("Debug", "AutoStart", "Check", "任务计划任务缺失或未启用");
            }

            return allHealthy;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "AutoStart", "Check", $"检查自启动状态失败：{ex.Message}");
            return false;
        }
    }

    private static void CreateShortcut(string targetPath, string shortcutPath, string arguments)
    {
        try
        {
            // 使用 PowerShell 创建快捷方式，避免引入额外的 COM 引用
            // 确保路径中的引号被正确处理
            var script = $"$s=(New-Object -COM WScript.Shell).CreateShortcut('{shortcutPath}');$s.TargetPath='{targetPath}';$s.Arguments='{arguments}';$s.Save()";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"创建快捷方式失败: {ex.Message}");
        }
    }

    private static void ManageTaskScheduler(bool enable, string appPath)
    {
        try
        {
            var taskName = $"{AppName}AutoStart";
            if (enable)
            {
                // 使用 PowerShell 创建任务计划，设置为最高权限运行以跳过 UAC
                // 触发器：登录时；操作：启动程序；设置：允许按需运行，不在交流电时停止，允许在电池模式启动
                var script = $@"
$action = New-ScheduledTaskAction -Execute '{appPath}' -Argument '{StartupArgs}'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Days 365)
Register-ScheduledTask -TaskName '{taskName}' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var process = Process.Start(psi);
                process?.WaitForExit(10000);
            }
            else
            {
                // 移除任务
                var script = $"Unregister-ScheduledTask -TaskName '{taskName}' -Confirm:$false -ErrorAction SilentlyContinue";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var process = Process.Start(psi);
                process?.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"管理任务计划程序失败: {ex.Message}");
        }
    }
}
