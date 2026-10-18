using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using ClassScreenLock.Uninstaller.Helpers;

namespace ClassScreenLock.Uninstaller.Services;

/// <summary>
/// 安装状态检测器
/// 用于判断 ClassScreenLock 是否已安装，以及安装位置、版本等信息
/// </summary>
public static class InstallationDetector
{
    private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClassScreenLock";
    private const string UninstallRegistryPath6432 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\ClassScreenLock";

    /// <summary>
    /// 是否已安装（基于多种检测方式）
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            // 1. 检查主程序 exe 是否存在
            var mainExe = Path.Combine(AppPathHelper.AppDirectory, "ClassScreenLock.exe");
            if (File.Exists(mainExe)) return true;

            // 2. 检查看门狗服务
            try
            {
                using var sc = new System.ServiceProcess.ServiceController("CSL.WatchdogService");
                var _ = sc.Status;
                return true;
            }
            catch { }

            // 3. 检查卸载注册表项
            if (HasUninstallRegistryEntry()) return true;

            // 4. 检查 IFEO 规则
            if (HasIfeoRules()) return true;

            // 5. 检查 ProgramData 目录
            if (Directory.Exists(AppPathHelper.ProgramDataDir))
            {
                try
                {
                    var redirector = Path.Combine(AppPathHelper.ProgramDataDir, "CSL.IfeoRedirector.exe");
                    if (File.Exists(redirector)) return true;
                }
                catch { }
            }

            return false;
        }
    }

    /// <summary>
    /// 获取安装版本
    /// </summary>
    public static string? GetInstalledVersion()
    {
        try
        {
            // 从卸载注册表读取
            using var key = Registry.LocalMachine.OpenSubKey(UninstallRegistryPath, false);
            if (key != null)
            {
                var version = key.GetValue("DisplayVersion") as string;
                if (!string.IsNullOrEmpty(version)) return version;
            }

            // 从主程序 exe 读取
            var mainExe = Path.Combine(AppPathHelper.AppDirectory, "ClassScreenLock.exe");
            if (File.Exists(mainExe))
            {
                var info = FileVersionInfo.GetVersionInfo(mainExe);
                if (!string.IsNullOrEmpty(info.ProductVersion))
                {
                    return info.ProductVersion;
                }
                if (!string.IsNullOrEmpty(info.FileVersion))
                {
                    return info.FileVersion;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 获取安装日期
    /// </summary>
    public static DateTime? GetInstallDate()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallRegistryPath, false);
            if (key != null)
            {
                var dateStr = key.GetValue("InstallDate") as string;
                if (!string.IsNullOrEmpty(dateStr) && dateStr.Length == 8)
                {
                    if (int.TryParse(dateStr.Substring(0, 4), out int year) &&
                        int.TryParse(dateStr.Substring(4, 2), out int month) &&
                        int.TryParse(dateStr.Substring(6, 2), out int day))
                    {
                        return new DateTime(year, month, day);
                    }
                }
            }

            // 从主程序目录创建时间推断
            if (Directory.Exists(AppPathHelper.AppDirectory))
            {
                return Directory.GetCreationTime(AppPathHelper.AppDirectory);
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 是否有卸载注册表项（添加/删除程序中的条目）
    /// </summary>
    private static bool HasUninstallRegistryEntry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallRegistryPath, false);
            if (key != null) return true;

            // 32 位注册表视图
            using var key6432 = Registry.LocalMachine.OpenSubKey(UninstallRegistryPath6432, false);
            return key6432 != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 是否存在 IFEO 规则（说明安装过程中写过注册表）
    /// </summary>
    private static bool HasIfeoRules()
    {
        try
        {
            var ifeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            var targets = new[] { "cmd.exe", "powershell.exe", "taskmgr.exe", "regedit.exe" };

            foreach (var target in targets)
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"{ifeoRoot}\{target}", false);
                var debugger = key?.GetValue("Debugger") as string;
                if (!string.IsNullOrWhiteSpace(debugger) &&
                    debugger.Contains("CSL.IfeoRedirector", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// 清理卸载注册表项（添加/删除程序中的条目）
    /// </summary>
    public static void CleanUninstallRegistry()
    {
        try
        {
            // 清理 64 位
            using var parent = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true);
            parent?.DeleteSubKeyTree("ClassScreenLock", false);
        }
        catch { }

        try
        {
            // 清理 32 位视图
            using var parent6432 = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", true);
            parent6432?.DeleteSubKeyTree("ClassScreenLock", false);
        }
        catch { }
    }
}
