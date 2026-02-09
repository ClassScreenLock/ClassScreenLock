using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using ClassScreenLock.Services;

namespace ClassScreenLock.Helpers;

public static class AutoStartHelper
{
    private const string AppName = "ClassScreenLock";
    private const string StartupArgs = "--minimized";

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
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"设置自启动失败: {ex.Message}");
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
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新自启动路径失败: {ex.Message}");
            LogService.Instance.Log("Error", "AutoStart", "UpdatePath", ex.Message);
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
