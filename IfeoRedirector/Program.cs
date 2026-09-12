using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CSL.IfeoRedirector;

/// <summary>
/// IFEO 重定向程序（映像劫持的接收端）。
///
/// 系统启动被劫持的进程（powershell.exe / cmd.exe 等）时，实际启动的是本程序，
/// 被劫持 exe 的完整路径与原始参数作为命令行传入：
///   CSL.IfeoRedirector.exe "C:\...\powershell.exe" 原始参数...
///
/// 职责：
///   1. 放行判定 —— 软件自身合法调用（共享临时放行标记有效 或 命令行含 allow-marker）→ 放行；
///      学生 / 攻击者调用 → 拒绝：弹出 WDAC 风格提示框（"你的组织使用了 ClassScreenLock
///      应用程序控制来阻止此应用"），关闭提示后进程退出，目标进程始终无法启动。
///   2. 放行时删除本进程名的 IFEO Debugger 键（避免递归劫持），用真实 EXE 重新拉起进程；
///      Debugger 键由主程序 AppBlockingService 的周期刷新（锁屏 300ms）自动写回。
///   3. 拒绝时显示 WDAC 风格提示框后退出（退出码 1）；Session 0 / 无交互桌面等异常
///      环境静默降级为直接退出，拦截结果不受影响。
///
/// 权限模型：放行场景的调用方都是管理员（主程序 / 看门狗，manifest 声明管理员），
/// 本进程继承管理员令牌，可删除 HKLM Debugger 键；非管理员上下文调用必然走"拒绝"
/// 路径（无标记 / 删键失败 → 保守拒绝），攻击者无法借伪造 marker 绕过——
/// 没有管理员权限就删不掉键，也就启动不了真实 EXE。
/// </summary>
internal static class Program
{
    // 软件自身进程名（与主程序 ProcessConstants.OwnProcessNames 保持一致）：
    // 命令行中出现这些标记视为软件自身发起的调用，放行。
    // "__CSL_ALLOW__" 为显式放行标记：软件内部所有 PowerShell 调用统一嵌入，
    // 不依赖脚本恰好包含进程名，避免内部调用被本程序误拦。
    private static readonly string[] AllowMarkers =
    {
        "ClassScreenLock", "CSL.Watchdog", "MonitorProcess", "BreakButtonProcess", "__CSL_ALLOW__"
    };

    private const string IfeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string AllowKeyPath = @"Software\ClassScreenLock";
    private const string AllowMarkerValue = "AllowPowerShellUntil";

    // 常驻占坑模式参数与全局互斥名（看门狗服务用 OpenExisting 探测占坑实例是否存活）
    private const string ResidentArg = "--resident";
    private const string ResidentMutexName = @"Global\CSL.IfeoRedirector.Resident";
    // 主程序退出时通过该全局事件通知占坑实例退出，随主程序一起结束、不再占用资源
    private const string ExitEventName = @"Global\CSL.IfeoRedirector.Exit";
    private const string RedirectorFileName = "CSL.IfeoRedirector.exe";
    private const string DeploymentDirName = "ClassScreenLock";

    // 安全中心账户权限放行心跳（机器级，由主程序 SYSTEM 进程在监控循环中持续刷新，
    // 见主程序 ProcessBypassService：HKLM\SOFTWARE\ClassScreenLock\AllowBlockedProcesses\Until）
    private const string BypassKeyPath = @"SOFTWARE\ClassScreenLock\AllowBlockedProcesses";
    private const string BypassValueName = "Until";

    // 放行 / 拒绝 / 故障均追加写日志（尽力而为），便于验证与排障。
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassScreenLock", "IfeoRedirector.log");

    [STAThread]
    private static int Main(string[] args)
    {
        // 常驻占坑模式（看门狗服务以 SYSTEM 身份启动，参数 --resident）：
        // 打开自身 exe 文件句柄（共享读/写但不共享删除），使文件删除/重命名被系统拒绝
        //（提示"文件被占用"），从而保住 IFEO 弹窗可执行文件不被学生删掉。
        // 该实例常驻不做事，与 IFEO 每次触发拉起的短命实例互不干扰（多实例并存）。
        if (args.Length > 0 && args[0].Equals(ResidentArg, StringComparison.OrdinalIgnoreCase))
        {
            return RunResident(args);
        }

        // args[0] = 被劫持 exe 的完整路径（由 IFEO 系统附加）
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return 0; // 没有目标进程，无事可做
        }

        var targetPath = args[0].Trim().Trim('"');
        string targetName;
        try
        {
            targetName = Path.GetFileNameWithoutExtension(targetPath);
        }
        catch
        {
            return 1;
        }
        if (string.IsNullOrWhiteSpace(targetName)) return 1;

        var originalArgs = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "";

        // 1) 放行判定：安全中心账户权限放行心跳有效 OR 共享临时放行标记有效（仅限 PowerShell 本体）
        //    OR 命令行含 allow-marker
        bool allowed = IsBlockBypassActive() || CommandLineContainsMarker(originalArgs) || IsTemporarilyAllowedFor(targetName);

        if (!allowed)
        {
            AppendLog("BLOCK", targetPath, originalArgs);
            ShowBlockedNotice(targetName, targetPath); // 拒绝：弹出 WDAC 风格提示框
            return 1;
        }

        // 2) 放行：删除本进程名的 Debugger 键（防递归劫持）→ 启动真实 EXE
        if (!RemoveIfeoDebugger(targetName))
        {
            AppendLog("FAIL_REMOVE_KEY", targetPath, originalArgs);
            return 2; // 非管理员上下文无法删键：保守拒绝，不放行
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = false
            };
            if (!string.IsNullOrEmpty(originalArgs))
            {
                psi.Arguments = originalArgs;
            }

            using var proc = Process.Start(psi);
            AppendLog("ALLOW", targetPath, originalArgs);
            return 0;
        }
        catch (Exception ex)
        {
            AppendLog("FAIL_START", targetPath, $"{originalArgs} | {ex.Message}");
            return 3;
        }
    }

    /// <summary>
    /// 常驻占坑模式（看门狗服务以 SYSTEM 身份启动，学生无法终止）：
    ///   1. 创建全局互斥 Global\CSL.IfeoRedirector.Resident 标记自己，供服务探测；
    ///   2. 对自身 exe（主程序目录副本）与固定部署目录（%ProgramData%\ClassScreenLock\）副本
    ///      各打开一个 FileStream 句柄，共享模式为"允许读/写但不允许删除"——
    ///      IFEO 每次触发的新实例仍能正常读取并启动（CreateProcess 只需读映像），
    ///      但任何删除/重命名操作都会因共享冲突失败，Windows 提示"文件正在被使用"；
    ///   3. 无限等待，进程保持存活并持续占用句柄，直至被系统终止。
    /// 多实例兼容：占坑实例不参与 IFEO 拦截逻辑、不显示弹窗、不连管道，
    /// 与每次触发拉起的短命实例完全互不干扰。
    /// </summary>
    private static int RunResident(string[] args)
    {
        try
        {
            using var residentMutex = new Mutex(true, ResidentMutexName, out var createdNew);
            if (!createdNew)
            {
                // 已有占坑实例在运行（竞态窗口），本实例直接退出
                AppendLog("RESIDENT_SKIP", "", "已有常驻占坑实例在运行");
                return 0;
            }

            // 收集需要保护的 CSL.IfeoRedirector.exe 路径：
            //   自身所在目录（主程序目录副本）+ 固定部署目录副本 + 服务显式传入的路径
            var targets = new List<string>();
            try
            {
                var selfDir = Path.GetDirectoryName(Environment.ProcessPath);
                if (!string.IsNullOrEmpty(selfDir))
                    targets.Add(Path.Combine(selfDir, RedirectorFileName));
            }
            catch { }

            try
            {
                targets.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    DeploymentDirName, RedirectorFileName));
            }
            catch { }

            foreach (var arg in args)
            {
                var t = arg?.Trim().Trim('"') ?? "";
                if (string.IsNullOrWhiteSpace(t) ||
                    t.Equals(ResidentArg, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) targets.Add(t);
            }

            var opened = new List<FileStream>();
            foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    // 关键：FileShare.Read | FileShare.Write（不含 Delete）。
                    // 读取/执行/覆盖写都允许，唯独删除与重命名被拒绝。
                    var fs = new FileStream(target, FileMode.Open, FileAccess.Read,
                        FileShare.Read | FileShare.Write);
                    opened.Add(fs);
                    AppendLog("RESIDENT_LOCK", target, "已占用文件句柄，禁止删除/重命名");
                }
                catch (Exception ex)
                {
                    AppendLog("RESIDENT_FAIL", target, ex.Message);
                }
            }

            if (opened.Count == 0)
            {
                AppendLog("RESIDENT_EMPTY", "", "未找到可占坑的重定向程序副本");
                return 0;
            }

            AppendLog("RESIDENT_START", "", $"常驻占坑实例启动，占用 {opened.Count} 个文件句柄");
            // 等待主程序退出信号：主程序退出时会置位全局事件 CSL.IfeoRedirector.Exit，
            // 本实例随即释放文件句柄并退出，不再残留占用资源；
            // 未被通知（主程序崩溃 / 系统注销）时保持常驻，由系统终止。
            try
            {
                using var exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
                // 若事件已由其它实例创建，此处打开的是同一个全局事件；等待它被置位
                WaitHandle.WaitAny(new[] { exitEvent });
                AppendLog("RESIDENT_EXIT", "", "收到主程序退出信号，常驻占坑实例退出");
            }
            catch
            {
                // 事件创建失败（极端权限场景）：回退为无限等待，保持原常驻行为
                System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
            }
            return 0;
        }
        catch (Exception ex)
        {
            AppendLog("RESIDENT_ERROR", "", ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// 安全中心登录账户满足最低允许权限时的放行心跳（60 秒窗口）。
    /// 主程序（SYSTEM）在监控循环中持续刷新 HKLM\SOFTWARE\ClassScreenLock\AllowBlockedProcesses\Until
    /// （QWORD Unix 毫秒时间戳）；此处读到未过期的时间戳即放行被拦截进程。
    /// 心跳保证无残留：老师登出（清值）或主程序崩溃（停止刷新）后 60 秒内自动恢复拦截。
    /// 普通学生对 HKLM 该键无写权限，无法伪造放行。
    /// </summary>
    private static bool IsBlockBypassActive()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BypassKeyPath);
            var raw = key?.GetValue(BypassValueName);
            if (raw == null) return false;
            long untilMs = raw switch
            {
                long l => l,
                int i => i,
                _ => 0
            };
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < untilMs;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 共享临时放行标记是否对当前目标生效。
    /// 标记（AllowPowerShellUntil）由主程序/看门狗在调用 powershell.exe / pwsh.exe
    /// 做自动启动修复、网络配置等合法操作前写入；仅对 PowerShell 本体生效——
    /// ISE（powershell_ise）不属于软件自身调用场景，学生借临时放行窗口启动 ISE
    /// 也会被拦截，杜绝绕过。
    /// </summary>
    private static bool IsTemporarilyAllowedFor(string targetName)
    {
        if (!string.Equals(targetName, "powershell", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetName, "pwsh", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return IsTemporarilyAllowed();
    }

    /// <summary>
    /// 读取共享临时放行标记（HKCU\Software\ClassScreenLock\AllowPowerShellUntil，Unix 毫秒时间戳）。
    /// 由主程序 AppBlockingService.AllowPowerShellTemporarily 与看门狗在调用 powershell 前写入。
    /// </summary>
    private static bool IsTemporarilyAllowed()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AllowKeyPath, false);
            var value = key?.GetValue(AllowMarkerValue);

            long until;
            if (value is long l) until = l;
            else if (value is int i) until = i * 1000L;                     // 兼容 32 位写入
            else if (value is string s && long.TryParse(s, out var parsed)) until = parsed;
            else return false;

            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < until;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>命令行是否包含软件自身进程名标记（软件自身发起的调用）。</summary>
    private static bool CommandLineContainsMarker(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return false;
        foreach (var marker in AllowMarkers)
        {
            if (commandLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    /// <summary>删除本进程名对应的 IFEO Debugger 值。无管理员权限时返回 false。</summary>
    private static bool RemoveIfeoDebugger(string processName)
    {
        try
        {
            var keyPath = $@"{IfeoRoot}\{processName}.exe";
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
            if (key == null) return true; // 键不存在：无需处理
            key.DeleteValue("Debugger", false);
            return true;
        }
        catch
        {
            return false; // 无权限（非管理员上下文）或键被锁定
        }
    }

    /// <summary>
    /// 显示 WDAC 风格的阻止提示。
    /// 优先级：1) 通知主程序显示（SYSTEM + UIAccess 置顶弹窗，可覆盖锁屏遮罩）；
    ///         2) 主程序不可达时本地 WinForms 弹窗（TopMost）降级；
    ///         3) Session 0 / 无交互桌面 / 弹窗异常 → 静默直接退出。
    /// "拒绝"这一核心结果在任何情况下都不受影响。
    /// </summary>
    private static void ShowBlockedNotice(string appName, string appPath)
    {
        try
        {
            // 服务会话（Session 0）没有交互桌面，弹窗没有意义且会失败，直接跳过
            if (Process.GetCurrentProcess().SessionId == 0) return;

            // 优先通知主程序：由主程序以 UIAccess 置顶显示，避免学生用其他窗口盖住提示
            if (BlockNoticeClient.TryNotifyMainApp(appName, appPath)) return;

            // 主程序不可达（未运行 / 重启中）→ 本地弹窗降级
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            WdacBlockDialog.ShowBlockDialog(appName, appPath);
        }
        catch
        {
            // 弹窗失败不改变拦截结果（目标进程依然无法启动）
        }
    }

    private static void AppendLog(string action, string targetPath, string detail)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {action} | {targetPath} | {detail}";
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // 日志失败不影响拦截主流程
        }
    }
}
