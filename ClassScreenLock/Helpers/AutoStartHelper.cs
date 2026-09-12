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
    private static readonly TimeSpan _normalCheckInterval = TimeSpan.FromMinutes(30); // 正常检查间隔：30 分钟
    private static readonly TimeSpan _abnormalCheckInterval = TimeSpan.FromMinutes(2); // 异常检查间隔：2 分钟
    private const int MAX_FAILURES_BEFORE_REPAIR = 3; // 连续失败 3 次才触发修复

    // 修复防抖：最小修复间隔 5 分钟，防止异常环境（如启动文件夹路径获取失败）导致
    // 自启动检查永远失败、周期性反复启动 PowerShell 修复进程（表现为每隔几分钟闪现一个 ps 进程）。
    private static readonly TimeSpan _minRepairInterval = TimeSpan.FromMinutes(5);
    private static DateTime _lastRepairTime = DateTime.MinValue;
    private static DateTime _lastWatchdogRepairTime = DateTime.MinValue;

    /// <summary>
    /// 是否运行在 SYSTEM / 非交互式上下文。
    /// 主程序经 UIAccess 提权为 SYSTEM 后（UiAccessService 复制 winlogon 令牌重启），
    /// Registry.CurrentUser 指向系统配置文件（.DEFAULT）、SpecialFolder.Startup 指向
    /// systemprofile——在此上下文中维护自启动只会写入无效垃圾项（用户看不到、删不掉，
    /// 反被周期性检查反复重建）。自启动应在交互用户会话中维护，SYSTEM 上下文一律跳过。
    /// </summary>
    private static bool IsSystemContext()
    {
        try
        {
            if (!Environment.UserInteractive) return true;
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (identity.User != null &&
                identity.User.IsWellKnown(System.Security.Principal.WellKnownSidType.LocalSystemSid))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(identity.Name) &&
                (identity.Name.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase) ||
                 identity.Name.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    /// <summary>
    /// 解析当前用户"启动"文件夹（带 USERPROFILE / UserProfile 兜底）。
    /// SYSTEM 上下文或全部解析失败时返回空字符串，调用方应跳过快捷方式相关操作，
    /// 不得误判为不健康或创建到 systemprofile。
    /// </summary>
    private static string GetStartupFolderPath()
    {
        if (IsSystemContext()) return string.Empty;
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                return folder;
            }
        }
        catch
        {
        }
        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile,
                @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
        }
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                return Path.Combine(profile,
                    @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
            }
        }
        catch
        {
        }
        return string.Empty;
    }

    public static void SetAutoStart(bool enable)
    {
        if (!OperatingSystem.IsWindows()) return;

        // SYSTEM 上下文（UIAccess 提权后）：HKCU / 计划任务指向系统配置文件，
        // 写入只会产生无效垃圾项；自启动由交互用户会话中的组件负责维护。
        if (IsSystemContext())
        {
            LogService.Instance.Log("Debug", "AutoStart", "SetAutoStart",
                $"SYSTEM 上下文，跳过自启动{(enable ? "启用" : "禁用")}（应由交互用户会话维护）");
            return;
        }

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

            // 2. 任务计划程序 (无 UAC 提示自启动)
            ManageTaskScheduler(enable, appPath);

            LogService.Instance.Log("Info", "AutoStart", "SetAutoStart", $"自启动已{(enable ? "启用" : "禁用")} (注册表 + 任务计划)");
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

        // SYSTEM 上下文：跳过（见 SetAutoStart 注释）
        if (IsSystemContext())
        {
            LogService.Instance.Log("Debug", "AutoStart", "UpdatePath", "SYSTEM 上下文，跳过自启动路径更新");
            return;
        }

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

        // SYSTEM 上下文：不检查也不修复（见 SetAutoStart 注释），避免向系统配置文件
        // 写入无效注册表项/计划任务并被反复重建。
        if (IsSystemContext())
        {
            LogService.Instance.Log("Debug", "AutoStart", "Repair", "SYSTEM 上下文，跳过自启动检查修复");
            return;
        }

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

            // 跳过任务计划程序检查以减少资源占用
            // 任务计划程序检查会在定时检查中异步执行

            // 如果需要修复，重新启用所有自启动方式
            // 防抖：5 分钟内不重复修复。防止因环境异常（如启动文件夹路径获取失败）导致
            // 检查永远失败、周期性地反复启动 PowerShell 修复进程（表现为每隔几分钟闪现一个 ps 进程）。
            if (needRepair)
            {
                if ((DateTime.Now - _lastRepairTime) < _minRepairInterval)
                {
                    LogService.Instance.Log("Warning", "AutoStart", "Repair", $"自启动修复被防抖跳过（{repairReason.Trim()}），5 分钟内已修复过");
                    return;
                }
                _lastRepairTime = DateTime.Now;
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
    /// 使用 Task Scheduler COM API（不再启动 PowerShell 子进程），
    /// 避免反复唤起"服务主机: Task Scheduler"导致高 CPU。
    /// </summary>
    private static bool IsTaskSchedulerEnabled()
    {
        try
        {
            var taskName = $"{AppName}AutoStart";

            // 使用 COM API 查询任务状态，不启动新进程
            dynamic scheduler = Activator.CreateInstance(
                Type.GetTypeFromProgID("Schedule.Service")!)!;
            try
            {
                scheduler.Connect();
                dynamic folder = scheduler.GetFolder("\\");
                dynamic task = folder.GetTask(taskName);
                var state = (int)task.State; // 3 = Ready, 4 = Running
                return state == 3 || state == 4;
            }
            finally
            {
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(scheduler); } catch { }
            }
        }
        catch
        {
            // 任务不存在或 COM 不可用
            return false;
        }
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

        // SYSTEM 上下文：无需维护自启动，直接视为健康（配合 StartPeriodicCheck 不再触发修复）
        if (IsSystemContext()) return true;

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

    private static void ManageTaskScheduler(bool enable, string appPath)
    {
        try
        {
            var taskName = $"{AppName}AutoStart";
            if (enable)
            {
                var script = $@"
#__CSL_ALLOW__
$action = New-ScheduledTaskAction -Execute '{appPath}' -Argument '{StartupArgs}'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Days 365)
Register-ScheduledTask -TaskName '{taskName}' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force";

                // 软件自身合法调用：临时放行 PowerShell，避免被实时拦截误杀
                AppBlockingService.AllowPowerShellTemporarily(15);
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
                var script = $"#__CSL_ALLOW__; Unregister-ScheduledTask -TaskName '{taskName}' -Confirm:$false -ErrorAction SilentlyContinue";
                // 软件自身合法调用：临时放行 PowerShell，避免被实时拦截误杀
                AppBlockingService.AllowPowerShellTemporarily(15);
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

    private const string WatchdogName = "CSL.Watchdog";
    private const string WatchdogStartupArgs = "";

    public static void SetWatchdogAutoStart(bool enable)
    {
        if (!OperatingSystem.IsWindows()) return;

        // SYSTEM 上下文：跳过（见 SetAutoStart 注释）
        if (IsSystemContext())
        {
            LogService.Instance.Log("Debug", "AutoStart", "Watchdog", "SYSTEM 上下文，跳过看门狗自启动设置");
            return;
        }

        try
        {
#if WINDOWS
            var baseDir = Helpers.AppPathHelper.AppDirectory;
            var watchdogPath = Path.Combine(baseDir, $"{WatchdogName}.exe");
            
            if (!File.Exists(watchdogPath))
            {
                LogService.Instance.Log("Warning", "AutoStart", "Watchdog", $"看门狗程序不存在: {watchdogPath}");
                return;
            }

            using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (enable)
                {
                    key?.SetValue(WatchdogName, $"\"{watchdogPath}\" {WatchdogStartupArgs}".TrimEnd());
                }
                else
                {
                    key?.DeleteValue(WatchdogName, false);
                }
            }

            ManageWatchdogTaskScheduler(enable, watchdogPath);

            LogService.Instance.Log("Info", "AutoStart", "Watchdog", $"看门狗自启动已{(enable ? "启用" : "禁用")} (注册表 + 任务计划)");
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"设置看门狗自启动失败：{ex.Message}");
            LogService.Instance.Log("Error", "AutoStart", "Watchdog", ex.Message);
        }
    }

    public static bool CheckWatchdogAutoStartStatus()
    {
        if (!OperatingSystem.IsWindows()) return true;

        // SYSTEM 上下文：无需维护，直接视为健康
        if (IsSystemContext()) return true;

        try
        {
#if WINDOWS
            var baseDir = Helpers.AppPathHelper.AppDirectory;
            var watchdogPath = Path.Combine(baseDir, $"{WatchdogName}.exe");
            
            if (!File.Exists(watchdogPath))
            {
                LogService.Instance.Log("Warning", "AutoStart", "Watchdog", $"看门狗程序不存在: {watchdogPath}");
                return false;
            }

            var expectedValue = $"\"{watchdogPath}\" {WatchdogStartupArgs}".TrimEnd();
            bool allHealthy = true;

            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
            {
                if (key != null)
                {
                    var value = key.GetValue(WatchdogName) as string;
                    if (value != expectedValue)
                    {
                        allHealthy = false;
                        LogService.Instance.Log("Debug", "AutoStart", "Watchdog", "注册表自启动项不正确");
                    }
                }
                else
                {
                    allHealthy = false;
                    LogService.Instance.Log("Debug", "AutoStart", "Watchdog", "注册表自启动项缺失");
                }
            }

            if (!IsWatchdogTaskSchedulerEnabled())
            {
                allHealthy = false;
                LogService.Instance.Log("Debug", "AutoStart", "Watchdog", "任务计划任务缺失或未启用");
            }

            return allHealthy;
#endif
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "AutoStart", "Watchdog", $"检查看门狗自启动状态失败：{ex.Message}");
            return false;
        }
    }

    public static void CheckAndRepairWatchdogAutoStart()
    {
        if (!OperatingSystem.IsWindows()) return;

        // SYSTEM 上下文：不检查也不修复（见 SetAutoStart 注释）
        if (IsSystemContext())
        {
            LogService.Instance.Log("Debug", "AutoStart", "Watchdog", "SYSTEM 上下文，跳过看门狗自启动检查修复");
            return;
        }

        try
        {
#if WINDOWS
            var baseDir = Helpers.AppPathHelper.AppDirectory;
            var watchdogPath = Path.Combine(baseDir, $"{WatchdogName}.exe");
            
            if (!File.Exists(watchdogPath))
            {
                return;
            }

            bool needRepair = false;
            string repairReason = string.Empty;

            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
            {
                if (key != null)
                {
                    var value = key.GetValue(WatchdogName) as string;
                    var expectedValue = $"\"{watchdogPath}\" {WatchdogStartupArgs}".TrimEnd();
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

            if (needRepair)
            {
                // 防抖：5 分钟内不重复修复，避免周期性反复启动 PowerShell 修复进程
                if ((DateTime.Now - _lastWatchdogRepairTime) < _minRepairInterval)
                {
                    LogService.Instance.Log("Warning", "AutoStart", "Watchdog", $"看门狗自启动修复被防抖跳过（{repairReason.Trim()}），5 分钟内已修复过");
                    return;
                }
                _lastWatchdogRepairTime = DateTime.Now;
                SetWatchdogAutoStart(true);
                LogService.Instance.Log("Info", "AutoStart", "Watchdog", $"看门狗自启动已修复: {repairReason}");
            }
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检查看门狗自启动状态失败：{ex.Message}");
        }
    }

    private static bool IsWatchdogTaskSchedulerEnabled()
    {
        try
        {
            var taskName = $"{WatchdogName}AutoStart";
            var script = $"#__CSL_ALLOW__; Get-ScheduledTask -TaskName '{taskName}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty State";
            // 软件自身合法调用：临时放行 PowerShell，避免被实时拦截误杀
            AppBlockingService.AllowPowerShellTemporarily(15);
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
                    process.WaitForExit(2000);
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

    private static void ManageWatchdogTaskScheduler(bool enable, string watchdogPath)
    {
        try
        {
            var taskName = $"{WatchdogName}AutoStart";
            if (enable)
            {
                var script = $@"
#__CSL_ALLOW__
$action = New-ScheduledTaskAction -Execute '{watchdogPath}' -Argument '{WatchdogStartupArgs}'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Days 365)
Register-ScheduledTask -TaskName '{taskName}' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force";

                // 软件自身合法调用：临时放行 PowerShell，避免被实时拦截误杀
                AppBlockingService.AllowPowerShellTemporarily(15);
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
                var script = $"#__CSL_ALLOW__; Unregister-ScheduledTask -TaskName '{taskName}' -Confirm:$false -ErrorAction SilentlyContinue";
                // 软件自身合法调用：临时放行 PowerShell，避免被实时拦截误杀
                AppBlockingService.AllowPowerShellTemporarily(15);
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
            Debug.WriteLine($"管理看门狗任务计划程序失败: {ex.Message}");
        }
    }
}
