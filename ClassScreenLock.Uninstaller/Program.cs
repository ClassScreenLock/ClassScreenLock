using System;
using System.Linq;
using System.Threading;
using System.Security.Principal;
using Avalonia;
using ClassScreenLock.Uninstaller.Services;
using ClassScreenLock.Uninstaller.Helpers;

namespace ClassScreenLock.Uninstaller;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 1. 初始化日志
        try
        {
            UninstallLogger.Init();
        }
        catch
        {
            // 日志初始化失败不影响主流程
        }

        // 2. 检查管理员权限（manifest 声明了 requireAdministrator，
        //    正常情况下会有 UAC 弹窗，但如果被策略阻止则需要提示）
        if (!IsRunningAsAdmin())
        {
            UninstallLogger.Warning("当前未以管理员身份运行，卸载程序需要管理员权限");
            // 尝试以管理员身份重启自身（触发 UAC 弹窗）
            if (TryRestartAsAdmin(args))
            {
                return 0; // 重启成功，当前进程退出
            }

            Console.WriteLine("错误：卸载程序需要管理员权限才能运行。");
            Console.WriteLine("请右键点击程序，选择\"以管理员身份运行\"。");
            return 1;
        }

        // 3. UIAccess 提权（与主程序一致的提权方式：复制 winlogon 令牌 → SYSTEM + UIAccess）
        //    如果当前没有 UIAccess，会创建提权后的新进程并退出当前进程
        try
        {
            UninstallLogger.Info("开始 UIAccess 提权检测...");
            UiAccessService.Instance.CheckAndElevate();
            // 如果提权成功，上面这行调用内部会 Environment.Exit(0) 退出当前进程，
            // 不会继续往下走。如果继续往下走，说明：
            //   a) 已经有 UIAccess 了，或者
            //   b) 提权失败但继续运行（有管理员权限也能完成大部分卸载操作）
        }
        catch (Exception ex)
        {
            UninstallLogger.Warning($"UIAccess 提权异常，继续以管理员权限运行: {ex.Message}");
        }

        // 4. 解析命令行参数
        bool isSilent = args.Any(a =>
            string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase));

        bool isQuiet = args.Any(a =>
            string.Equals(a, "/quiet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase));

        // 5. 静默模式：无 UI，直接执行卸载
        if (isSilent || isQuiet)
        {
            return RunSilentUninstall();
        }

        // 6. 正常 GUI 模式
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        return 0;
    }

    /// <summary>
    /// 检查当前进程是否以管理员身份运行
    /// </summary>
    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 尝试以管理员身份重启自身（触发 UAC 弹窗）
    /// </summary>
    private static bool TryRestartAsAdmin(string[] args)
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return false;

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                UseShellExecute = true,
                Verb = "runas" // 触发 UAC 提权
            };

            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — 用户拒绝了 UAC 弹窗
            UninstallLogger.Warning("用户拒绝了 UAC 提权请求");
            return false;
        }
        catch (Exception ex)
        {
            UninstallLogger.Warning($"以管理员身份重启失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 静默卸载模式（无 GUI）
    /// 返回 0 表示成功，非 0 表示失败
    /// </summary>
    private static int RunSilentUninstall()
    {
        try
        {
            UninstallLogger.Info("静默卸载模式启动");

            // 检查是否已安装
            if (!InstallationDetector.IsInstalled)
            {
                UninstallLogger.Warning("未检测到 ClassScreenLock 安装，无需卸载");
                return 0;
            }

            // 执行卸载
            var service = new UninstallService();
            var progress = new Progress<UninstallProgressReport>(report =>
            {
                UninstallLogger.Info($"{report.Progress:P0} - {report.StepDescription}");
            });

            var result = service.RunUninstallAsync(progress, CancellationToken.None).GetAwaiter().GetResult();

            if (result.Success)
            {
                UninstallLogger.Info("静默卸载成功");
                UninstallService.ScheduleSelfDestruct();
                return 0;
            }
            else
            {
                UninstallLogger.Error($"静默卸载失败: {result.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            try
            {
                UninstallLogger.Error($"静默卸载异常: {ex}");
            }
            catch { }
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
