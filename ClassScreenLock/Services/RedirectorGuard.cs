using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ClassScreenLock.Services;

/// <summary>
/// IFEO 重定向程序（CSL.IfeoRedirector.exe）常驻占坑实例守护器。
///
/// 背景：IFEO 重定向程序每次触发才短暂运行，平时文件无人占用，学生可把
/// 主程序目录（或固定部署目录）下的 CSL.IfeoRedirector.exe 删掉，导致弹窗失效。
/// 解决方案是由看门狗服务 / 主程序 / 看门狗进程共同守护一个常驻占坑实例
/// （CSL.IfeoRedirector.exe --resident）：该实例持有 exe 文件句柄
/// （共享读/写但不共享删除），使删除/重命名被系统拒绝；IFEO 每次触发的新实例
/// 仍能正常读取启动（多实例并存，互不干扰）。
///
/// 多守护者冗余：服务（Session 0，LocalSystem）、主程序（SYSTEM + UIAccess）、
/// 看门狗（SYSTEM）任一存活的守护者发现占坑实例丢失都会立即重新拉起，
/// 全局互斥 Global\CSL.IfeoRedirector.Resident 保证任一时刻只有一个占坑实例。
/// </summary>
public static class RedirectorGuard
{
    private const string ResidentMutexName = @"Global\CSL.IfeoRedirector.Resident";
    private const string ExitEventName = @"Global\CSL.IfeoRedirector.Exit";
    private const string RedirectorFileName = "CSL.IfeoRedirector.exe";
    private const string DeploymentDirName = "ClassScreenLock";

    /// <summary>
    /// 确保常驻占坑实例在运行。幂等、开销极小（一次互斥探测），
    /// 适合在高频监控循环中每周期调用。
    /// </summary>
    public static void EnsureResident()
    {
        try
        {
            // 探测全局互斥：占坑实例存活则无需处理。
            // 主程序为 SYSTEM + UIAccess 进程，其创建的命名对象可访问 Global 命名空间。
            try
            {
                using var existing = Mutex.OpenExisting(ResidentMutexName);
                return; // 占坑实例正在运行
            }
            catch
            {
                // 互斥不存在 → 占坑实例缺失，需要启动
            }

            // 定位重定向程序：主程序实际 exe 目录优先，固定部署目录兜底
            string[] candidates =
            {
                Path.Combine(ProcessDirectory, RedirectorFileName),
                Path.Combine(CommonAppDataDir, DeploymentDirName, RedirectorFileName)
            };

            string? exePath = null;
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    exePath = candidate;
                    break;
                }
            }

            if (exePath == null) return;

            // 直接 Process.Start：占坑实例继承本进程（SYSTEM + UIAccess）令牌，
            // 学生进程（普通用户完整性级别）无法结束它。
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--resident",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            LogService.Instance.Log("Info", "RedirectorGuard", "EnsureResident",
                $"已启动 IFEO 重定向程序常驻占坑实例（{exePath} --resident）");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "RedirectorGuard", "EnsureResident", ex.Message);
        }
    }

    /// <summary>
    /// 主程序退出时调用：置位全局退出事件，通知 IFEO 重定向程序常驻占坑实例
    /// （CSL.IfeoRedirector.exe --resident）释放文件句柄并退出，
    /// 避免主程序退出后占坑实例仍驻留占用资源。
    /// 幂等：事件不存在（无占坑实例）时静默返回。
    /// </summary>
    public static void Shutdown()
    {
        try
        {
            using var exitEvent = EventWaitHandle.OpenExisting(ExitEventName);
            exitEvent.Set();
            LogService.Instance.Log("Info", "RedirectorGuard", "Shutdown",
                "已置位退出事件，IFEO 重定向程序常驻占坑实例将随主程序退出");
        }
        catch
        {
            // 事件不存在：当前没有占坑实例在运行，无需处理
        }
    }

    /// <summary>
    /// 主程序实际 exe 所在目录（单文件发布下 AppContext.BaseDirectory 指向临时解压目录，
    /// 必须优先用 Environment.ProcessPath）。
    /// </summary>
    private static string ProcessDirectory
    {
        get
        {
            try
            {
                return Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            }
            catch
            {
                return AppContext.BaseDirectory;
            }
        }
    }

    private static string CommonAppDataDir
    {
        get
        {
            try
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            }
            catch
            {
                return AppContext.BaseDirectory;
            }
        }
    }
}
