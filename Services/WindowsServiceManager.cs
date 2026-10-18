using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
using ClassScreenLock.Helpers;

namespace ClassScreenLock.Services;

/// <summary>
/// 管理 Windows 服务的安装、卸载、启动和停止。
/// 使用 sc.exe 命令行工具来操作服务控制管理器（SCM）。
/// 看门狗服务（CSL.WatchdogService）以 LocalSystem 身份运行，
/// 使用 CreateProcessAsUser API 在用户会话中启动并监控主程序。
/// </summary>
public static class WindowsServiceManager
{
    public const string WatchdogServiceName = "CSL.WatchdogService";
    public const string WatchdogServiceDisplayName = "ClassScreenLock 看门狗服务";
    public const string WatchdogServiceDescription = "以 SYSTEM 权限运行的看门狗服务，监控并自动重启 ClassScreenLock 主程序及用户模式看门狗，提供系统级保护。";

    /// <summary>
    /// 获取看门狗服务可执行文件路径（与主程序同目录）
    /// </summary>
    private static string GetWatchdogServicePath()
    {
        return Path.Combine(AppPathHelper.AppDirectory, "CSL.WatchdogService.exe");
    }

    /// <summary>
    /// 检查指定名称的 Windows 服务是否已安装
    /// </summary>
    public static bool IsServiceInstalled(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            var _ = sc.Status;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查指定名称的 Windows 服务是否正在运行
    /// </summary>
    public static bool IsServiceRunning(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 安装/修复并启动看门狗服务。
    /// 每次程序启动时调用此方法：
    ///   - 服务不存在 → 创建
    ///   - 服务存在但 binPath 指向错误位置 → 重建
    ///   - 每次都会重新应用全部配置（描述/恢复策略/启动类型/权限加固），修复被篡改的设置
    /// </summary>
    public static async Task<bool> InstallAndStartServicesAsync()
    {
        var serviceExePath = GetWatchdogServicePath();

        if (!File.Exists(serviceExePath))
        {
            LogService.Instance.Log("Error", "Service", "Install",
                $"看门狗服务可执行文件不存在: {serviceExePath}");
            return false;
        }

        // 检查现有服务：binPath 是否指向当前 exe
        var currentBinPath = GetServiceBinPath(WatchdogServiceName);
        bool serviceExists = IsServiceInstalled(WatchdogServiceName);
        bool pathMismatch = serviceExists &&
                            currentBinPath != null &&
                            !string.Equals(currentBinPath.Trim('"'), serviceExePath.Trim('"'), StringComparison.OrdinalIgnoreCase);

        if (pathMismatch)
        {
            LogService.Instance.Log("Warning", "Service", "Install",
                $"服务 binPath 不匹配（现={currentBinPath}，期望={serviceExePath}），重建服务...");
            await UninstallServicesAsync();
            await Task.Delay(1500);
            serviceExists = false;
        }

        if (!serviceExists)
        {
            // 创建服务
            string createArgs = $"create {WatchdogServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"{WatchdogServiceDisplayName}\"";
            var (createSuccess, createOutput) = RunScCommand(createArgs);
            if (!createSuccess)
            {
                // 常见原因：主程序未以管理员权限运行
                LogService.Instance.Log("Error", "Service", "Install", $"创建服务失败（请以管理员身份运行）: {createOutput}");
                return false;
            }
        }

        // 每次启动都重新应用配置，修复可能被篡改的设置
        ApplyServiceConfiguration();

        // 加固服务权限：仅 SYSTEM 和管理员可操作，普通用户无法停止/更改
        HardenServicePermissions();

        LogService.Instance.Log("Info", "Service", "Install", "服务配置已应用，正在启动...");

        // 确保服务正在运行
        if (!IsServiceRunning(WatchdogServiceName))
        {
            await Task.Delay(500);
            var (startSuccess, startOutput) = RunScCommand($"start {WatchdogServiceName}");
            if (!startSuccess)
            {
                // 启动可能需要一点时间，等待后重试
                await Task.Delay(3000);
                (startSuccess, startOutput) = RunScCommand($"start {WatchdogServiceName}");
            }

            if (startSuccess)
            {
                LogService.Instance.Log("Info", "Service", "Install", "看门狗服务已启动");
            }
            else
            {
                LogService.Instance.Log("Warning", "Service", "Install", $"服务已安装但启动失败: {startOutput}");
            }
        }
        else
        {
            LogService.Instance.Log("Info", "Service", "Install", "看门狗服务已在运行");
        }

        return IsServiceInstalled(WatchdogServiceName);
    }

    /// <summary>
    /// 每次启动时重新应用服务配置，修复被用户手动篡改的设置。
    /// </summary>
    private static void ApplyServiceConfiguration()
    {
        // 设置服务描述
        RunScCommand($"description {WatchdogServiceName} \"{WatchdogServiceDescription}\"");

        // 配置服务恢复策略：服务进程被强制结束后 1 秒内由 SCM 自动重启。
        // 三次失败机会用完后，用户模式看门狗（30 秒周期）与主程序（30 秒周期）
        // 会兜底拉起服务；服务成功启动后 SCM 失败计数自动重置，恢复策略重新生效。
        RunScCommand($"failure {WatchdogServiceName} reset= 86400 actions= restart/1000/restart/1000/restart/1000");

        // 延迟自动启动（减少系统启动时的负担）
        RunScCommand($"config {WatchdogServiceName} start= delayed-auto");
    }

    /// <summary>
    /// 轻量检查并拉起看门狗服务（供主程序 / 用户模式看门狗周期性调用）。
    /// 服务已安装但未运行时通过 sc start 立即启动；相比 InstallAndStartServicesAsync，
    /// 不做重建、权限加固等重操作，适合高频调用。
    /// 服务进程被强制结束后的恢复路径：SCM 恢复策略（1 秒）→ 本方法兜底（约 30 秒周期）。
    /// </summary>
    public static void EnsureWatchdogServiceRunning()
    {
        try
        {
            using var sc = new ServiceController(WatchdogServiceName);
            var status = sc.Status; // 服务未安装时抛异常，直接跳过
            if (status == ServiceControllerStatus.Running || status == ServiceControllerStatus.StartPending)
            {
                return; // 运行中或正在启动，无需处理
            }

            LogService.Instance.Log("Warning", "Service", "Ensure",
                $"看门狗服务未运行（状态: {status}），尝试启动...");
            RunScCommand($"start {WatchdogServiceName}");

            // 服务启动需要一点时间，等待后重试一次
            System.Threading.Thread.Sleep(1500);
            using var scAfter = new ServiceController(WatchdogServiceName);
            if (scAfter.Status == ServiceControllerStatus.Stopped)
            {
                RunScCommand($"start {WatchdogServiceName}");
            }
        }
        catch
        {
            // 服务未安装或查询异常：由 InstallAndStartServicesAsync 负责安装，这里静默跳过
        }
    }

    /// <summary>
    /// 随主程序退出而停止看门狗服务（不卸载，保留下次开机自启）。
    /// 供 App.OnApplicationExit 调用：服务已加固（管理员无停止权限），
    /// 需先 RestoreServicePermissions 恢复权限再 sc stop。
    /// 采用同步 + 有界短等待（最多约 5 秒），避免阻塞主程序退出过久。
    /// 前置条件：调用前应已写入退出标志（Program.CreateExitFlag）并已终止用户模式看门狗进程，
    /// 否则服务停止后可能被三方兜底逻辑重新拉起。
    /// </summary>
    public static void StopServiceForShutdown()
    {
        try
        {
            if (!IsServiceInstalled(WatchdogServiceName))
            {
                return; // 未安装，无需处理
            }

            if (!IsServiceRunning(WatchdogServiceName))
            {
                return; // 已停止，无需处理
            }

            // 服务已加固，管理员无停止权限。先恢复默认权限才能停止
            RestoreServicePermissions();
            System.Threading.Thread.Sleep(300);

            LogService.Instance.Log("Info", "Service", "Shutdown", "主程序退出，正在停止看门狗服务...");
            RunScCommand($"stop {WatchdogServiceName}");

            // 有界等待服务停止（最多约 5 秒），不长时间阻塞退出
            int waitCount = 0;
            while (IsServiceRunning(WatchdogServiceName) && waitCount < 10)
            {
                System.Threading.Thread.Sleep(500);
                waitCount++;
            }

            LogService.Instance.Log("Info", "Service", "Shutdown", "看门狗服务已随主程序停止");
        }
        catch (Exception ex)
        {
            // 停止失败不应阻断主程序退出流程
            try
            {
                LogService.Instance.Log("Warning", "Service", "Shutdown", $"停止看门狗服务异常: {ex.Message}");
            }
            catch { /* 日志服务可能已释放，忽略 */ }
        }
    }

    /// <summary>
    /// 加固后的服务安全描述符（SDDL）：
    ///   - SYSTEM：完全控制
    ///   - Administrators（BA，含管理员身份登录的用户）：保留 查询/启动/配置/改权限，
    ///     但移除 停止(WP) 和 删除(SD) 权限。
    /// 即使用户以管理员身份登录，也无法在服务管理器中停止或删除此服务。
    /// </summary>
    private const string HardenedSddl =
        "D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)" +          // SYSTEM 完全控制
        "(A;;CCDCLCSWRPDTLOCRRCWDWO;;;BA)";                  // BA：无 WP(停止) / SD(删除)

    /// <summary>
    /// Windows 默认的服务安全描述符（用于卸载/重启前临时恢复权限）
    /// </summary>
    private const string DefaultSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)" +
        "(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)" +
        "(A;;CCLCSWLOCRRC;;;IU)" +
        "(A;;CCLCSWLOCRRC;;;SU)";

    /// <summary>
    /// 加固服务安全描述符（SDDL）：
    /// 移除管理员用户的停止(WP)和删除(SD)权限，使管理员身份登录的用户
    /// 也无法在服务管理器中停止或删除此服务（只能查询/启动/改配置）。
    /// </summary>
    private static void HardenServicePermissions()
    {
        var (success, output) = RunScCommand($"sdset {WatchdogServiceName} {HardenedSddl}");
        if (success)
        {
            LogService.Instance.Log("Info", "Service", "Harden", "服务权限已加固（管理员也无法停止/删除服务）");
        }
        else
        {
            LogService.Instance.Log("Warning", "Service", "Harden", $"服务权限加固失败: {output}");
        }
    }

    /// <summary>
    /// 恢复服务默认权限（仅在卸载/重启服务前调用，
    /// 因为加固后管理员没有停止/删除权限，无法操作服务）
    /// </summary>
    private static void RestoreServicePermissions()
    {
        RunScCommand($"sdset {WatchdogServiceName} {DefaultSddl}");
    }

    /// <summary>
    /// 查询已安装服务的 binPath（用于判断是否需要重建服务）
    /// </summary>
    private static string? GetServiceBinPath(string serviceName)
    {
        try
        {
            var (success, output) = RunScCommand($"qc {serviceName}");
            if (!success) return null;

            foreach (var line in output.Split('\n', '\r'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("BINARY_PATH_NAME", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = trimmed.IndexOf(':');
                    if (idx >= 0)
                    {
                        return trimmed.Substring(idx + 1).Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "Service", "Query", $"查询服务 binPath 异常: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 卸载看门狗服务
    /// </summary>
    public static async Task<bool> UninstallServicesAsync()
    {
        if (!IsServiceInstalled(WatchdogServiceName))
        {
            LogService.Instance.Log("Info", "Service", "Uninstall", "服务未安装，无需卸载");
            return true;
        }

        // 服务已加固，管理员无停止/删除权限。先恢复默认权限才能执行卸载操作
        RestoreServicePermissions();
        await Task.Delay(1000);

        // 先停止服务
        if (IsServiceRunning(WatchdogServiceName))
        {
            LogService.Instance.Log("Info", "Service", "Uninstall", "正在停止服务...");
            RunScCommand($"stop {WatchdogServiceName}");

            // 等待服务完全停止
            int waitCount = 0;
            while (IsServiceRunning(WatchdogServiceName) && waitCount < 15)
            {
                await Task.Delay(1000);
                waitCount++;
            }
        }

        await Task.Delay(1000);

        // 删除服务
        var (deleteSuccess, deleteOutput) = RunScCommand($"delete {WatchdogServiceName}");
        if (!deleteSuccess)
        {
            LogService.Instance.Log("Error", "Service", "Uninstall", $"删除服务失败: {deleteOutput}");
            return false;
        }

        LogService.Instance.Log("Info", "Service", "Uninstall", "看门狗服务已卸载");

        // 等待 SCM 完全清理
        await Task.Delay(1000);
        return true;
    }

    /// <summary>
    /// 重启看门狗服务
    /// </summary>
    public static async Task<bool> RestartServicesAsync()
    {
        if (!IsServiceInstalled(WatchdogServiceName))
        {
            // 未安装则直接安装
            return await InstallAndStartServicesAsync();
        }

        // 服务已加固，先恢复权限才能停止
        RestoreServicePermissions();
        await Task.Delay(1000);

        // 停止
        if (IsServiceRunning(WatchdogServiceName))
        {
            RunScCommand($"stop {WatchdogServiceName}");
            int waitCount = 0;
            while (IsServiceRunning(WatchdogServiceName) && waitCount < 15)
            {
                await Task.Delay(1000);
                waitCount++;
            }
        }

        await Task.Delay(2000);

        // 启动
        var (startSuccess, startOutput) = RunScCommand($"start {WatchdogServiceName}");
        if (startSuccess)
        {
            // 启动后重新加固权限
            HardenServicePermissions();
            LogService.Instance.Log("Info", "Service", "Restart", "看门狗服务重启成功");
            return true;
        }
        else
        {
            LogService.Instance.Log("Error", "Service", "Restart", $"服务重启失败: {startOutput}");
            return false;
        }
    }

    /// <summary>
    /// 执行 sc.exe 命令
    /// </summary>
    /// <param name="arguments">sc.exe 的参数（注意 sc.exe 的特殊格式：参数名后需要空格再跟值）</param>
    /// <returns>(是否成功, 输出内容)</returns>
    private static (bool success, string output) RunScCommand(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, "无法启动 sc.exe");
            }

            process.WaitForExit(15000);
            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();

            // sc.exe 退出码：0 表示成功
            bool success = process.ExitCode == 0;

            string fullOutput = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";

            if (!success)
            {
                LogService.Instance.Log("Debug", "Service", "ScCommand", $"sc {arguments} => Exit={process.ExitCode}, Output={fullOutput}");
            }

            return (success, fullOutput);
        }
        catch (Exception ex)
        {
            return (false, $"执行 sc.exe 异常: {ex.Message}");
        }
    }
}
