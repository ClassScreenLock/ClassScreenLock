using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ClassScreenLock.Uninstaller.Helpers;

namespace ClassScreenLock.Uninstaller.Services;

/// <summary>
/// 卸载步骤枚举，用于进度展示
/// </summary>
public enum UninstallStep
{
    NotStarted,
    StoppingProcesses,
    StoppingWatchdogService,
    UninstallingWatchdogService,
    CleaningIfeoRegistry,
    CleaningGroupPolicy,
    CleaningAutoStart,
    CleaningProgramData,
    CleaningAppData,
    CleaningUserRegistry,
    CleaningUninstallEntry,
    DeletingFiles,
    SelfDestruct,
    Completed,
    Failed
}

/// <summary>
/// 卸载进度报告
/// </summary>
public class UninstallProgressReport
{
    public UninstallStep CurrentStep { get; set; }
    public string StepDescription { get; set; } = string.Empty;
    public double Progress { get; set; } // 0.0 - 1.0
    public string? Detail { get; set; }
}

/// <summary>
/// 单个卸载步骤的结果
/// </summary>
public class UninstallStepResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// 核心卸载服务。
/// 
/// 卸载顺序（按依赖关系从外到内，确保不会因为防护机制导致卸载被阻断）：
/// 1. 通知占坑实例退出（释放 IFEO 重定向程序文件句柄）
/// 2. 终止主程序与看门狗用户态进程
/// 3. 恢复服务权限 → 停止 → 卸载看门狗 Windows 服务
/// 4. 清理 IFEO 注册表（进程映像劫持）
/// 5. 清理组策略 DisableCMD
/// 6. 清理自启动（注册表 Run + 任务计划程序）
/// 7. 清理 ProgramData 固定部署目录
/// 8. 清理用户注册表项
/// 9. 删除程序文件（最后一步，因为前面的步骤都依赖程序文件）
/// 10. 自删除（卸载程序自身）
/// </summary>
public class UninstallService
{
    // === 常量定义（与主程序保持一致） ===
    private const string WatchdogServiceName = "CSL.WatchdogService";
    private const string IfeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string DebuggerValueName = "Debugger";
    private const string SystemPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string DisableCmdValue = "DisableCMD";
    private const string AppRunKeyName = "ClassScreenLock";
    private const string WatchdogRunKeyName = "CSL.Watchdog";
    private const string UserRegistryPath = @"Software\ClassScreenLock";
    private const string ExitEventName = @"Global\CSL.IfeoRedirector.Exit";

    /// <summary>
    /// 主程序 IPC 管道名（与主程序 IpcService 一致）
    /// </summary>
    private const string IpcPipeName = "ClassScreenLock_IPC";

    /// <summary>
    /// 主进程名（用于查找 PID / 判断是否已退出）
    /// </summary>
    private const string MainProcessName = "ClassScreenLock";

    // 需要检查并清理的 IFEO 目标进程名（与主程序 KnownTargetNames 一致）
    private static readonly string[] IfeoTargetNames =
    {
        "cmd", "powershell", "pwsh", "powershell_ise", "wscript", "cscript", "taskmgr", "resmon", "regedit"
    };

    // 需要终止的进程名（覆盖主程序、服务、用户态看门狗、占坑实例及其子进程）
    private static readonly string[] ProcessNamesToKill =
    {
        "ClassScreenLock",
        "CSL.WatchdogService",
        "CSL.Watchdog",
        "CSL.IfeoRedirector",
        "MonitorProcess",
        "BreakButtonProcess"
    };

    // 默认服务安全描述符（用于恢复加固后的服务权限）
    private const string DefaultServiceSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)" +
        "(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)" +
        "(A;;CCLCSWLOCRRC;;;IU)" +
        "(A;;CCLCSWLOCRRC;;;SU)";

    private readonly List<string> _warnings = new();
    private CancellationTokenSource? _cts;

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// 执行完整卸载流程
    /// </summary>
    /// <param name="progress">进度回调</param>
    /// <param name="cancellationToken">取消令牌（用户取消时直接退出）</param>
    public async Task<UninstallStepResult> RunUninstallAsync(
        IProgress<UninstallProgressReport> progress,
        CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        var result = new UninstallStepResult { Success = true };

        // 初始化卸载日志
        UninstallLogger.Init();
        UninstallLogger.Info("开始卸载 ClassScreenLock");
        UninstallLogger.Info($"安装目录: {AppPathHelper.AppDirectory}");

        try
        {
            // 步骤总数（用于计算进度）
            int totalSteps = 13;
            int currentStepIndex = 0;

            // 1. 通过主程序 IPC 接口请求正常退出
            // 主程序收到 EXIT 后走 desktop.Shutdown() → OnApplicationExit 完整清理流程，
            // 并由 Program.Main 的 finally 用「主程序自己的 PID」写退出标志，
            // 所有守护进程都能识别为正常退出，不再重新拉起。
            // 若 IPC 不可用，则回退为写退出标志 + 强制终止（PID 使用主程序 PID）。
            UninstallLogger.Step("退出主程序", "正在通过 IPC 接口通知主程序退出...");
            ReportProgress(progress, UninstallStep.StoppingProcesses, "正在停止主程序...", currentStepIndex, totalSteps);
            int mainPid = await StopMainProcessAsync(token);
            currentStepIndex++;

            // 2. 等待用户态看门狗检测退出标志并自行退出
            // 用户态看门狗（3 个 CSL.Watchdog.exe）同时守护主程序、服务和占坑实例，
            // 必须先让它们自己退出，否则停服务后会被立刻重新拉起。
            // 等待期间持续刷新退出标志（主程序退出后标志有效期仅 5 秒）。
            UninstallLogger.Step("等待看门狗退出", "正在等待看门狗进程退出...");
            ReportProgress(progress, UninstallStep.StoppingProcesses, "正在等待看门狗退出...", currentStepIndex, totalSteps);
            // 主程序未运行时用本进程 PID 占位写入标志（看门狗在主程序不存在时只看时间戳）
            await WaitForWatchdogsExitAsync(mainPid != 0 ? mainPid : Environment.ProcessId, token);
            currentStepIndex++;

            // 3. 停止看门狗服务（先禁用故障恢复动作，防止 SCM 自动重启）
            UninstallLogger.Step("停止服务", "正在停止看门狗 Windows 服务...");
            ReportProgress(progress, UninstallStep.StoppingWatchdogService, "正在停止看门狗服务...", currentStepIndex, totalSteps);
            await StopWatchdogServiceAsync(token);
            currentStepIndex++;

            // 4. 卸载看门狗服务
            UninstallLogger.Step("卸载服务", "正在卸载看门狗 Windows 服务...");
            ReportProgress(progress, UninstallStep.UninstallingWatchdogService, "正在卸载看门狗服务...", currentStepIndex, totalSteps);
            await UninstallWatchdogServiceAsync(token);
            currentStepIndex++;

            // 5. 强制清理所有残留进程（IFEO 占坑实例、残留看门狗、监控子进程等）
            UninstallLogger.Step("清理进程", "正在清理所有残留进程...");
            ReportProgress(progress, UninstallStep.StoppingProcesses, "正在清理残留进程...", currentStepIndex, totalSteps);
            await StopRemainingProcessesAsync(token);
            currentStepIndex++;

            // 6. 清理 IFEO 注册表
            UninstallLogger.Step("清理IFEO", "正在清理 IFEO 映像劫持注册表...");
            ReportProgress(progress, UninstallStep.CleaningIfeoRegistry, "正在清理进程保护规则...", currentStepIndex, totalSteps);
            CleanIfeoRegistry();
            currentStepIndex++;

            // 8. 清理组策略
            UninstallLogger.Step("清理组策略", "正在恢复 DisableCMD 组策略...");
            ReportProgress(progress, UninstallStep.CleaningGroupPolicy, "正在恢复系统组策略设置...", currentStepIndex, totalSteps);
            CleanGroupPolicy();
            currentStepIndex++;

            // 6. 清理自启动
            UninstallLogger.Step("清理自启动", "正在清理注册表和任务计划自启动项...");
            ReportProgress(progress, UninstallStep.CleaningAutoStart, "正在清理自启动项...", currentStepIndex, totalSteps);
            CleanAutoStart();
            currentStepIndex++;

            // 7. 清理用户注册表
            UninstallLogger.Step("清理用户注册表", "正在清理 HKCU 用户配置...");
            ReportProgress(progress, UninstallStep.CleaningUserRegistry, "正在清理用户配置...", currentStepIndex, totalSteps);
            CleanUserRegistry();
            currentStepIndex++;

            // 8. 清理卸载注册表项（添加/删除程序）
            UninstallLogger.Step("清理卸载项", "正在清理添加/删除程序注册表项...");
            ReportProgress(progress, UninstallStep.CleaningUninstallEntry, "正在清理卸载注册表项...", currentStepIndex, totalSteps);
            InstallationDetector.CleanUninstallRegistry();
            currentStepIndex++;

            // 9. 清理 ProgramData 目录
            UninstallLogger.Step("清理ProgramData", "正在清理公共部署目录...");
            ReportProgress(progress, UninstallStep.CleaningProgramData, "正在清理公共部署文件...", currentStepIndex, totalSteps);
            CleanProgramData();
            currentStepIndex++;

            // 10. 清理各账户 AppData 残留
            // 主程序以 SYSTEM 身份运行时，备份包与日志写入
            // C:\Windows\System32\config\systemprofile\AppData\{Roaming,Local}\ClassScreenLock；
            // 普通账户运行时则写入各自用户目录。这里统一遍历清理。
            UninstallLogger.Step("清理AppData", "正在清理账户 AppData 残留...");
            ReportProgress(progress, UninstallStep.CleaningAppData, "正在清理账户数据残留...", currentStepIndex, totalSteps);
            CleanAppDataResidue();
            currentStepIndex++;

            // 11. 删除程序文件
            UninstallLogger.Step("删除文件", "正在删除程序文件...");
            ReportProgress(progress, UninstallStep.DeletingFiles, "正在删除程序文件...", currentStepIndex, totalSteps);
            await DeleteAppFilesAsync(token);
            currentStepIndex++;

            // 完成
            UninstallLogger.Info("卸载完成");
            ReportProgress(progress, UninstallStep.Completed, "卸载完成", 1.0);
            result.Success = true;
            result.Message = "ClassScreenLock 已成功卸载";
        }
        catch (OperationCanceledException)
        {
            // 用户取消：直接退出（需求 #3：删除文件时正在进行直接退出）
            result.Success = false;
            result.Message = "卸载已取消";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"卸载失败: {ex.Message}";
            ReportProgress(progress, UninstallStep.Failed, $"卸载失败: {ex.Message}", 0);
        }

        result.Warnings.AddRange(_warnings);
        return result;
    }

    private static void ReportProgress(
        IProgress<UninstallProgressReport> progress,
        UninstallStep step,
        string description,
        int currentStep,
        int totalSteps)
    {
        var p = totalSteps > 0 ? (double)currentStep / totalSteps : 0;
        progress.Report(new UninstallProgressReport
        {
            CurrentStep = step,
            StepDescription = description,
            Progress = Math.Clamp(p, 0, 1)
        });
    }

    private static void ReportProgress(
        IProgress<UninstallProgressReport> progress,
        UninstallStep step,
        string description,
        double progressValue)
    {
        progress.Report(new UninstallProgressReport
        {
            CurrentStep = step,
            StepDescription = description,
            Progress = Math.Clamp(progressValue, 0, 1)
        });
    }

    #region 退出标志

    /// <summary>
    /// 写入退出标志文件（exit.dat），格式与主程序 Program.CreateExitFlag 完全一致：
    /// [16字节随机Key][16字节随机IV][AES-CBC加密的 "PID|时间戳"]
    ///
    /// 关键：PID 必须是「主程序 ClassScreenLock.exe 的 PID」，不能是卸载程序自己的 PID。
    /// 用户态看门狗 CheckExitFlag() 会校验：
    ///   - 若主程序仍在运行，标志中的 PID 必须与某个主程序实例匹配，否则判为无效；
    ///   - 若主程序已退出，标志时间戳必须不超过 5 秒，否则判为无效。
    /// 因此本方法需要在等待看门狗退出的过程中被反复调用（刷新时间戳）。
    /// </summary>
    /// <param name="mainProcessId">主程序 PID</param>
    /// <param name="quiet">是否抑制日志（刷新时使用）</param>
    private bool CreateExitFlag(int mainProcessId, bool quiet = false)
    {
        try
        {
            var exitFlagPath = Path.Combine(AppPathHelper.AppDirectory, "exit.dat");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var content = $"{mainProcessId}|{timestamp}";
            var contentBytes = Encoding.UTF8.GetBytes(content);

            var key = new byte[16];
            var iv = new byte[16];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
                rng.GetBytes(iv);
            }

            byte[] encryptedPayload;
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor();
                encryptedPayload = encryptor.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
            }

            var finalData = new byte[key.Length + iv.Length + encryptedPayload.Length];
            Array.Copy(key, 0, finalData, 0, key.Length);
            Array.Copy(iv, 0, finalData, key.Length, iv.Length);
            Array.Copy(encryptedPayload, 0, finalData, key.Length + iv.Length, encryptedPayload.Length);

            // 覆盖写入（主程序可能刚写过同名文件，先确保可写）
            using (var fs = new FileStream(exitFlagPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                fs.Write(finalData, 0, finalData.Length);
                fs.Flush(true);
            }

            if (!quiet)
            {
                UninstallLogger.Info($"已写入退出标志（主程序 PID={mainProcessId}）: {exitFlagPath}");
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                UninstallLogger.Warning($"写入退出标志失败: {ex.Message}");
                _warnings.Add($"写入退出标志失败：{ex.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// 删除退出标志文件。必须在确认所有守护进程都已退出后调用，
    /// 否则守护进程会认为主程序异常退出并重新拉起。
    /// </summary>
    private void DeleteExitFlag()
    {
        try
        {
            var exitFlagPath = Path.Combine(AppPathHelper.AppDirectory, "exit.dat");
            if (File.Exists(exitFlagPath))
            {
                File.Delete(exitFlagPath);
                UninstallLogger.Info("已删除退出标志 exit.dat");
            }
        }
        catch (Exception ex)
        {
            UninstallLogger.Warning($"删除退出标志失败: {ex.Message}");
        }
    }

    #endregion

    #region 进程终止

    /// <summary>
    /// 等待用户态看门狗进程（CSL.Watchdog）检测到退出标志并自行退出。
    /// 用户态看门狗检测到有效 exit.dat 后会立即退出（不再守护主程序/服务/占坑实例）。
    ///
    /// 注意：主程序退出后，看门狗要求退出标志时间戳不超过 5 秒，
    /// 所以等待期间必须持续刷新标志，否则看门狗会重新开始拉起进程。
    /// </summary>
    private async Task WaitForWatchdogsExitAsync(int mainPid, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        const string watchdogProcessName = "CSL.Watchdog";
        const int maxWaitMs = 20000;
        const int checkIntervalMs = 500;

        int waitedMs = 0;
        UninstallLogger.Info("等待用户态看门狗进程检测退出标志并自行退出...");

        while (waitedMs < maxWaitMs)
        {
            token.ThrowIfCancellationRequested();

            // 持续刷新退出标志（同时覆盖主程序已退出、标志需保持 5 秒有效期的场景）
            CreateExitFlag(mainPid, quiet: true);

            await Task.Delay(checkIntervalMs, token);
            waitedMs += checkIntervalMs;

            var procs = Process.GetProcessesByName(watchdogProcessName);
            try
            {
                if (procs.Length == 0)
                {
                    UninstallLogger.Info($"所有看门狗进程已退出（耗时 {waitedMs}ms）");
                    return;
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }

        var remaining = Process.GetProcessesByName(watchdogProcessName);
        try
        {
            UninstallLogger.Warning($"看门狗进程在 {maxWaitMs}ms 内未完全退出，剩余 {remaining.Length} 个，将强制终止");
        }
        finally
        {
            foreach (var p in remaining) p.Dispose();
        }
    }

    /// <summary>
    /// 获取主程序进程 PID 列表
    /// </summary>
    private static int[] GetMainProcessIds()
    {
        try
        {
            var procs = Process.GetProcessesByName(MainProcessName);
            try
            {
                return procs.Select(p => p.Id).ToArray();
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// 主程序是否仍在运行
    /// </summary>
    private static bool IsMainProcessRunning() => GetMainProcessIds().Length > 0;

    /// <summary>
    /// 通过主程序 IPC 接口请求其正常退出。
    /// 主程序收到 EXIT 后走 desktop.Shutdown() → OnApplicationExit 完整清理流程，
    /// 并由 Program.Main 的 finally 写入带自身 PID 的退出标志，
    /// 因此所有守护进程都能识别为「正常退出」而不再拉起。
    /// </summary>
    private async Task<bool> RequestMainExitViaIpcAsync(CancellationToken token)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", IpcPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await client.ConnectAsync(2000, token);

            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            await writer.WriteLineAsync("EXIT");

            UninstallLogger.Info("已通过 IPC 接口向主程序发送 EXIT 命令");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UninstallLogger.Warning($"IPC 发送 EXIT 失败（将回退到强制退出）: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 强制终止指定进程名的所有实例（SeDebugPrivilege 下可终止 SYSTEM 进程），
    /// 失败时回退到 taskkill /F /T。
    /// </summary>
    private void ForceKillProcesses(string[] processNames, int waitMsPerProcess = 3000)
    {
        foreach (var processName in processNames)
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (var proc in procs)
            {
                int pid = -1;
                try
                {
                    if (proc.HasExited) continue;

                    pid = proc.Id;
                    // 先尝试直接结束（管理员 + SeDebugPrivilege 可结束 SYSTEM 进程）
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(waitMsPerProcess);
                }
                catch
                {
                    // 直接结束失败，回退 taskkill
                    if (pid > 0)
                    {
                        try
                        {
                            RunProcessHidden("taskkill.exe", $"/F /T /PID {pid}");
                        }
                        catch { }
                    }
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }

        // 对仍残留的实例统一走 taskkill（按映像名，大小写不敏感）
        foreach (var processName in processNames)
        {
            if (Process.GetProcessesByName(processName).Length == 0) continue;
            try
            {
                RunProcessHidden("taskkill.exe", $"/F /T /IM {processName}.exe");
            }
            catch { }
        }
    }

    /// <summary>
    /// 静默执行外部命令
    /// </summary>
    private static void RunProcessHidden(string fileName, string arguments, int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            p?.WaitForExit(timeoutMs);
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    /// 通知主程序退出：
    /// 1) 优先通过 IPC 的 EXIT 接口让主程序自己走正常退出流程（会自行写退出标志）；
    /// 2) IPC 不可用或超时未退出时，用「主程序 PID」写入退出标志并强制终止，
    ///    使看门狗/服务认定为正常退出而不再拉起。
    ///
    /// 返回用于写入退出标志的主程序 PID（调用方在等待看门狗期间继续刷新该标志）。
    /// </summary>
    private async Task<int> StopMainProcessAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var mainPids = GetMainProcessIds();
        if (mainPids.Length == 0)
        {
            UninstallLogger.Info("主程序未在运行，跳过退出通知");
            return 0;
        }

        int mainPid = mainPids[0];
        UninstallLogger.Info($"检测到 {mainPids.Length} 个主程序实例，PID={mainPid}");

        // ---- 1. 先通过 IPC 接口请求正常退出 ----
        bool ipcAccepted = await RequestMainExitViaIpcAsync(token);

        if (ipcAccepted)
        {
            // 主程序退出需要时间：停服务、写标志、清理资源等，最多等 15 秒
            const int maxWaitMs = 15000;
            int waitedMs = 0;

            while (waitedMs < maxWaitMs)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(500, token);
                waitedMs += 500;

                if (!IsMainProcessRunning())
                {
                    UninstallLogger.Info($"主程序已通过 IPC 正常退出（耗时 {waitedMs}ms）");
                    return mainPid;
                }
            }

            UninstallLogger.Warning("主程序在 15 秒内未响应 IPC 退出请求，将强制终止");
        }

        // ---- 2. 回退：用主程序 PID 写退出标志，然后强制终止 ----
        // PID 必须是主程序 PID，否则用户态看门狗会判定标志无效并重新拉起主程序
        CreateExitFlag(mainPid);
        await Task.Delay(300, token);

        ForceKillProcesses(new[] { MainProcessName });

        // 等待主程序进程完全消失（最多 8 秒）
        for (int i = 0; i < 16; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!IsMainProcessRunning())
            {
                UninstallLogger.Info("主程序已强制退出");
                break;
            }
            await Task.Delay(500, token);
        }

        return mainPid;
    }

    /// <summary>
    /// 强制清理所有残留的 ClassScreenLock 相关进程。
    /// 包括：IFEO 占坑实例、残留看门狗、监控子进程、主程序残留实例等。
    /// 此时各类守护机制均已停止，强制终止后不会被重新拉起。
    /// </summary>
    private async Task StopRemainingProcessesAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // 先通知 IFEO 占坑实例主动退出（释放文件句柄）
        try
        {
            using var exitEvent = EventWaitHandle.OpenExisting(ExitEventName);
            exitEvent.Set();
        }
        catch
        {
            // 事件不存在说明没有占坑实例，正常
        }

        await Task.Delay(500, token);

        // 强制终止所有相关进程
        ForceKillProcesses(ProcessNamesToKill);
        await Task.Delay(800, token);

        // 复核：把仍然存活（可能是被重新拉起）的进程再杀一遍
        for (int round = 0; round < 3; round++)
        {
            token.ThrowIfCancellationRequested();

            var alive = ProcessNamesToKill
                .Where(name =>
                {
                    try
                    {
                        var ps = Process.GetProcessesByName(name);
                        try { return ps.Length > 0; }
                        finally { foreach (var p in ps) p.Dispose(); }
                    }
                    catch { return false; }
                })
                .ToArray();

            if (alive.Length == 0)
            {
                UninstallLogger.Info("所有相关进程已全部退出");
                break;
            }

            UninstallLogger.Warning($"第 {round + 1} 轮清理，仍有进程存活: {string.Join(", ", alive)}");
            ForceKillProcesses(alive);
            await Task.Delay(800, token);
        }

        // 确认主程序与看门狗均已停止后，删除退出标志，避免残留标志影响后续安装
        var stillAlive = ProcessNamesToKill
            .Where(name =>
            {
                try
                {
                    var ps = Process.GetProcessesByName(name);
                    try { return ps.Length > 0; }
                    finally { foreach (var p in ps) p.Dispose(); }
                }
                catch { return false; }
            })
            .ToArray();

        if (stillAlive.Length == 0)
        {
            DeleteExitFlag();
        }
        else
        {
            UninstallLogger.Warning($"以下进程未能终止，保留退出标志以防被重新拉起: {string.Join(", ", stillAlive)}");
            _warnings.Add($"以下进程未能终止：{string.Join(", ", stillAlive)}");
        }
    }

    #endregion

    #region 看门狗服务

    /// <summary>
    /// 停止看门狗 Windows 服务
    /// </summary>
    private async Task StopWatchdogServiceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (!IsServiceInstalled(WatchdogServiceName))
        {
            return; // 服务未安装，无需处理
        }

        try
        {
            // 服务可能已被加固（管理员无停止权限），先恢复默认权限
            RestoreServicePermissions(WatchdogServiceName);
            await Task.Delay(500, token);

            // 关闭 SCM 故障恢复动作与自动启动，防止服务被停止后又被自动拉起
            var (failOk, failOut) = RunScCommand($"failure {WatchdogServiceName} reset= 0 actions= \"\"");
            if (!failOk)
            {
                UninstallLogger.Warning($"关闭服务故障恢复动作失败: {failOut}");
            }

            var (cfgOk, cfgOut) = RunScCommand($"config {WatchdogServiceName} start= disabled");
            if (!cfgOk)
            {
                UninstallLogger.Warning($"禁用服务自动启动失败: {cfgOut}");
            }

            // 停止服务
            RunScCommand($"stop {WatchdogServiceName}");

            // 等待服务停止（最多 15 秒）
            int waitCount = 0;
            while (IsServiceRunning(WatchdogServiceName) && waitCount < 15)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(1000, token);
                waitCount++;
            }

            if (IsServiceRunning(WatchdogServiceName))
            {
                // 仍有异常时强制结束服务进程
                var (_, killOut) = RunScCommand($"queryex {WatchdogServiceName}");
                UninstallLogger.Warning($"服务未在超时内停止，尝试强制结束服务进程: {killOut}");
                ForceKillProcesses(new[] { "CSL.WatchdogService" });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _warnings.Add($"停止看门狗服务时出现警告: {ex.Message}");
        }
    }

    /// <summary>
    /// 卸载看门狗 Windows 服务
    /// </summary>
    private async Task UninstallWatchdogServiceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (!IsServiceInstalled(WatchdogServiceName))
        {
            return;
        }

        try
        {
            // 确保服务已停止
            if (IsServiceRunning(WatchdogServiceName))
            {
                await StopWatchdogServiceAsync(token);
            }

            token.ThrowIfCancellationRequested();

            // 确保权限已恢复
            RestoreServicePermissions(WatchdogServiceName);
            await Task.Delay(500, token);

            // 删除服务（服务可能处于 "marked for deletion" 状态，重试几次）
            bool deleted = false;
            string lastOutput = string.Empty;
            for (int attempt = 0; attempt < 3 && !deleted; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var (success, output) = RunScCommand($"delete {WatchdogServiceName}");
                lastOutput = output;
                deleted = success;
                if (!deleted)
                {
                    await Task.Delay(2000, token);
                }
            }

            if (!deleted)
            {
                _warnings.Add($"删除看门狗服务失败: {lastOutput}");
                UninstallLogger.Warning($"删除看门狗服务失败: {lastOutput}");
            }
            else
            {
                UninstallLogger.Info("看门狗服务已删除");
            }

            await Task.Delay(1000, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _warnings.Add($"卸载看门狗服务时出现警告: {ex.Message}");
        }
    }

    private static bool IsServiceInstalled(string serviceName)
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

    private static bool IsServiceRunning(string serviceName)
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
    /// 恢复服务默认权限（服务被加固后管理员无法停止/删除，需要先恢复权限）
    /// </summary>
    private static void RestoreServicePermissions(string serviceName)
    {
        try
        {
            RunScCommand($"sdset {serviceName} {DefaultServiceSddl}");
        }
        catch
        {
            // 权限恢复失败，继续尝试其他操作
        }
    }

    /// <summary>
    /// 执行 sc.exe 命令
    /// </summary>
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
            if (process == null) return (false, "无法启动 sc.exe");

            process.WaitForExit(15000);
            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();

            bool success = process.ExitCode == 0;
            string fullOutput = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";

            return (success, fullOutput);
        }
        catch (Exception ex)
        {
            return (false, $"执行 sc.exe 异常: {ex.Message}");
        }
    }

    #endregion

    #region IFEO 注册表清理

    /// <summary>
    /// 清理 IFEO 映像劫持注册表项
    /// 只删除指向我们重定向程序的 Debugger 值，不删除键本身
    /// </summary>
    private void CleanIfeoRegistry()
    {
        string? redirectorPath = null;

        try
        {
            // 尝试定位重定向程序路径（用于精确匹配）
            var programDataExe = Path.Combine(AppPathHelper.ProgramDataDir, "CSL.IfeoRedirector.exe");
            if (File.Exists(programDataExe))
            {
                redirectorPath = programDataExe;
            }
            else
            {
                var appDirExe = Path.Combine(AppPathHelper.AppDirectory, "CSL.IfeoRedirector.exe");
                if (File.Exists(appDirExe))
                {
                    redirectorPath = appDirExe;
                }
            }
        }
        catch { }

        foreach (var name in IfeoTargetNames)
        {
            try
            {
                var keyPath = $@"{IfeoRoot}\{name}.exe";
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                if (key == null) continue;

                var value = key.GetValue(DebuggerValueName) as string;
                if (string.IsNullOrWhiteSpace(value)) continue;

                // 判断是否是我们写入的：
                // 1. 包含 CSL.IfeoRedirector 字样
                // 2. 或指向我们的重定向程序路径
                bool isOurs = value.Contains("CSL.IfeoRedirector", StringComparison.OrdinalIgnoreCase);
                if (!isOurs && redirectorPath != null)
                {
                    isOurs = value.Contains(redirectorPath, StringComparison.OrdinalIgnoreCase);
                }

                if (isOurs)
                {
                    key.DeleteValue(DebuggerValueName, false);
                }
            }
            catch (Exception ex)
            {
                _warnings.Add($"清理 IFEO 规则（{name}.exe）失败: {ex.Message}");
            }
        }
    }

    #endregion

    #region 组策略清理

    /// <summary>
    /// 清理组策略 DisableCMD 注册表项
    /// </summary>
    private void CleanGroupPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SystemPolicyPath, true);
            if (key == null) return;

            var value = key.GetValue(DisableCmdValue);
            if (value is int i && i != 0)
            {
                key.DeleteValue(DisableCmdValue, false);
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"清理组策略 DisableCMD 失败: {ex.Message}");
        }
    }

    #endregion

    #region 自启动清理

    /// <summary>
    /// 清理所有自启动方式：
    /// 1. HKCU\...\Run 注册表项（主程序 + 看门狗）
    /// 2. 任务计划程序（主程序 + 看门狗）
    /// </summary>
    private void CleanAutoStart()
    {
        // 1. 注册表 Run 项
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                // 清理主程序自启动
                if (key.GetValue(AppRunKeyName) != null)
                {
                    key.DeleteValue(AppRunKeyName, false);
                }
                // 清理看门狗自启动
                if (key.GetValue(WatchdogRunKeyName) != null)
                {
                    key.DeleteValue(WatchdogRunKeyName, false);
                }
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"清理注册表自启动项失败: {ex.Message}");
        }

        // 2. 任务计划程序
        try
        {
            var taskNames = new[] { $"{AppRunKeyName}AutoStart", $"{WatchdogRunKeyName}AutoStart" };
            foreach (var taskName in taskNames)
            {
                try
                {
                    // 使用 schtasks.exe 删除任务（比 PowerShell 更轻量）
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/Delete /TN \"{taskName}\" /F",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(5000);
                }
                catch
                {
                    // 单个任务删除失败不影响整体
                }
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"清理任务计划程序失败: {ex.Message}");
        }
    }

    #endregion

    #region ProgramData 清理

    /// <summary>
    /// 清理 ProgramData\ClassScreenLock 目录（IFEO 重定向程序固定部署位置）
    /// </summary>
    private void CleanProgramData()
    {
        try
        {
            var dir = AppPathHelper.ProgramDataDir;
            if (!Directory.Exists(dir))
            {
                return;
            }

            ClearFileAttributes(dir);

            if (TryDeleteDirectory(dir, out var error))
            {
                UninstallLogger.Info($"已清理 ProgramData 目录: {dir}");
            }
            else
            {
                _warnings.Add($"清理 ProgramData 目录失败: {error}");
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"清理 ProgramData 目录失败: {ex.Message}");
        }
    }

    #endregion

    #region AppData 残留清理

    /// <summary>
    /// 清理所有账户下的 ClassScreenLock AppData 残留目录。
    ///
    /// 这些目录由主程序与 IFEO 重定向器写入：
    ///   - Roaming\ClassScreenLock：主程序 DataProtectionService 的加密备份包与同步日志
    ///     （ClassScreenLock_backup.dat / ClassScreenLock_sync_log.json）
    ///   - Local\ClassScreenLock：IFEO 重定向器的 IfeoRedirector.log
    ///
    /// 关键：主程序以 SYSTEM 身份运行，其 `SpecialFolder.ApplicationData` 实际解析到
    ///   C:\Windows\System32\config\systemprofile\AppData\Roaming
    /// 因此除了各用户目录，还必须单独处理 systemprofile（含 SysWOW64 重定向路径）。
    ///
    /// 这些目录被设置为 Hidden + System 属性，删除前需要先清除属性，
    /// 否则递归删除可能因只读/系统属性失败。
    /// </summary>
    private void CleanAppDataResidue()
    {
        var targets = new List<string>();

        // 1. SYSTEM 账户配置文件（主程序以 SYSTEM 运行，残留主要集中在这里）
        var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(systemDrive))
        {
            systemDrive = @"C:\Windows";
        }

        var systemProfiles = new[]
        {
            Path.Combine(systemDrive, @"System32\config\systemprofile"),
            Path.Combine(systemDrive, @"SysWOW64\config\systemprofile")
        };

        foreach (var profile in systemProfiles)
        {
            if (Directory.Exists(profile))
            {
                targets.Add(profile);
            }
        }

        // 2. 当前进程所在账户的用户目录
        try
        {
            var currentProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(currentProfile) && Directory.Exists(currentProfile))
            {
                targets.Add(currentProfile);
            }
        }
        catch { }

        // 3. 所有本地用户目录（C:\Users\*），兼容教师账户/管理员账户曾运行过的情况
        try
        {
            var usersRoot = Path.Combine(Path.GetPathRoot(systemDrive) ?? @"C:\", "Users");
            if (Directory.Exists(usersRoot))
            {
                foreach (var dir in Directory.GetDirectories(usersRoot))
                {
                    var name = Path.GetFileName(dir);

                    // 跳过符号链接式的公共目录与系统伪目录
                    if (name.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Default User", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("All Users", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    targets.Add(dir);
                }
            }
        }
        catch (Exception ex)
        {
            UninstallLogger.Warning($"枚举用户目录失败: {ex.Message}");
        }

        // 4. 逐个清理 Local / Roaming 下的 ClassScreenLock 目录
        int removed = 0;
        foreach (var profile in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var appDataFolder in new[] { "AppData\\Local", "AppData\\Roaming" })
            {
                var targetDir = Path.Combine(profile, appDataFolder, "ClassScreenLock");

                try
                {
                    if (!Directory.Exists(targetDir))
                    {
                        continue;
                    }

                    ClearFileAttributes(targetDir);

                    if (TryDeleteDirectory(targetDir, out var error))
                    {
                        removed++;
                        UninstallLogger.Info($"已清理 AppData 残留: {targetDir}");
                    }
                    else
                    {
                        UninstallLogger.Warning($"清理 AppData 残留失败: {targetDir} ({error})");
                        _warnings.Add($"无法删除目录: {targetDir}（{error}）");
                    }
                }
                catch (Exception ex)
                {
                    UninstallLogger.Warning($"清理 AppData 残留异常: {targetDir} ({ex.Message})");
                    _warnings.Add($"清理目录失败: {targetDir}（{ex.Message}）");
                }
            }
        }

        if (removed == 0)
        {
            UninstallLogger.Info("未发现 AppData 残留目录");
        }
        else
        {
            UninstallLogger.Info($"AppData 残留清理完成，共删除 {removed} 个目录");
        }
    }

    /// <summary>
    /// 递归清除目录内所有文件和子目录的只读/隐藏/系统属性，
    /// 否则 Directory.Delete 可能因属性限制失败。
    /// </summary>
    private static void ClearFileAttributes(string directory)
    {
        try
        {
            var dirInfo = new DirectoryInfo(directory);

            // 先清目录本身（去掉 ReadOnly 才能正常枚举删除）
            try
            {
                if ((dirInfo.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                }
                dirInfo.Attributes = FileAttributes.Directory;
            }
            catch { }

            foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0 ||
                        (file.Attributes & FileAttributes.System) != 0 ||
                        (file.Attributes & FileAttributes.Hidden) != 0)
                    {
                        file.Attributes = FileAttributes.Normal;
                    }
                }
                catch { }
            }

            foreach (var dir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
            {
                try
                {
                    if ((dir.Attributes & FileAttributes.ReadOnly) != 0 ||
                        (dir.Attributes & FileAttributes.System) != 0 ||
                        (dir.Attributes & FileAttributes.Hidden) != 0)
                    {
                        dir.Attributes = FileAttributes.Directory;
                    }
                }
                catch { }
            }
        }
        catch
        {
            // 属性清理失败时仍继续尝试删除
        }
    }

    /// <summary>
    /// 尝试删除目录，失败时重试并返回详细错误信息。
    /// </summary>
    private static bool TryDeleteDirectory(string path, out string error, int attempts = 3)
    {
        error = string.Empty;

        for (int i = 0; i < attempts; i++)
        {
            try
            {
                Directory.Delete(path, true);
                return !Directory.Exists(path);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Thread.Sleep(300);
            }
        }

        return false;
    }

    #endregion

    #region 用户注册表清理

    /// <summary>
    /// 清理 ClassScreenLock 的用户级注册表项：
    /// 1. HKCU\Software\ClassScreenLock（当前账户）
    /// 2. HKU\{每个已加载用户SID}\Software\ClassScreenLock（其他曾运行过的账户，
    ///    包括 SYSTEM 的 S-1-5-18，主程序以 SYSTEM 运行时写入此处）
    /// 3. HKLM\SOFTWARE\ClassScreenLock（进程放行心跳等机器级状态）
    /// </summary>
    private void CleanUserRegistry()
    {
        // 1. 当前账户
        try
        {
            using var software = Registry.CurrentUser.OpenSubKey("Software", true);
            if (software?.OpenSubKey("ClassScreenLock") != null)
            {
                software.DeleteSubKeyTree("ClassScreenLock", false);
                UninstallLogger.Info(@"已清理 HKCU\Software\ClassScreenLock");
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"清理用户注册表失败: {ex.Message}");
        }

        // 2. 其他已加载用户配置单元（含 SYSTEM）
        try
        {
            using var users = Registry.Users;
            foreach (var sidName in users.GetSubKeyNames())
            {
                // 跳过 .DEFAULT 与各类 _Classes 挂载点
                if (sidName.Equals(".DEFAULT", StringComparison.OrdinalIgnoreCase) ||
                    sidName.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var software = users.OpenSubKey($@"{sidName}\Software", true);
                    if (software?.OpenSubKey("ClassScreenLock") != null)
                    {
                        software.DeleteSubKeyTree("ClassScreenLock", false);
                        UninstallLogger.Info($@"已清理 HKU\{sidName}\Software\ClassScreenLock");
                    }
                }
                catch (Exception ex)
                {
                    UninstallLogger.Warning($@"清理 HKU\{sidName}\Software\ClassScreenLock 失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            UninstallLogger.Warning($"枚举 HKU 配置单元失败: {ex.Message}");
        }

        // 3. 机器级键
        try
        {
            using var hklmSoftware = Registry.LocalMachine.OpenSubKey("SOFTWARE", true);
            if (hklmSoftware?.OpenSubKey("ClassScreenLock") != null)
            {
                hklmSoftware.DeleteSubKeyTree("ClassScreenLock", false);
                UninstallLogger.Info(@"已清理 HKLM\SOFTWARE\ClassScreenLock");
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"清理机器级注册表失败: {ex.Message}");
        }
    }

    #endregion

    #region 文件删除

    /// <summary>
    /// 删除主程序目录下的所有文件（不包括卸载程序自身，自删除单独处理）
    /// </summary>
    private async Task DeleteAppFilesAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var appDir = AppPathHelper.AppDirectory;
        var uninstallerExe = Path.GetFileName(Environment.ProcessPath) ?? "CSL.Uninstaller.exe";

        if (!Directory.Exists(appDir))
        {
            return;
        }

        // 先删文件（排除卸载程序自身）
        await Task.Run(() =>
        {
            try
            {
                // 删除 Data 目录
                var dataDir = Path.Combine(appDir, "Data");
                if (Directory.Exists(dataDir))
                {
                    SafeDeleteDirectory(dataDir, token);
                }

                // 删除所有文件（排除卸载程序 exe 及其依赖）
                foreach (var file in Directory.GetFiles(appDir))
                {
                    token.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileName(file);
                    // 跳过卸载程序自身和可能正在运行的文件
                    if (fileName.Equals(uninstallerExe, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // 文件被占用等情况，加入待自删除列表
                        _warnings.Add($"无法立即删除文件: {fileName}（将在系统重启时自动清理）");
                    }
                }

                // 删除子目录
                foreach (var dir in Directory.GetDirectories(appDir))
                {
                    token.ThrowIfCancellationRequested();
                    var dirName = Path.GetFileName(dir);
                    if (string.Equals(dirName, "Data", StringComparison.OrdinalIgnoreCase))
                        continue; // 已处理

                    try
                    {
                        SafeDeleteDirectory(dir, token);
                    }
                    catch
                    {
                        _warnings.Add($"无法删除目录: {dirName}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _warnings.Add($"删除程序文件时出现警告: {ex.Message}");
            }
        }, token);
    }

    /// <summary>
    /// 安全删除目录（递归），遇到删除失败的文件继续处理其他文件
    /// </summary>
    private static void SafeDeleteDirectory(string dirPath, CancellationToken token)
    {
        if (!Directory.Exists(dirPath)) return;

        token.ThrowIfCancellationRequested();

        // 清除只读/隐藏/系统属性，避免删除失败（Data 目录可能被设为隐藏）
        ClearFileAttributes(dirPath);

        // 先删文件
        foreach (var file in Directory.GetFiles(dirPath))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
            }
            catch { }
        }

        // 再删子目录
        foreach (var dir in Directory.GetDirectories(dirPath))
        {
            SafeDeleteDirectory(dir, token);
        }

        // 最后删目录本身
        try
        {
            Directory.Delete(dirPath, false);
        }
        catch { }
    }

    #endregion

    #region 自删除

    /// <summary>
    /// 计划卸载程序自身的删除。
    /// 使用 cmd.exe 延迟删除：启动一个独立的 cmd 进程，等待卸载程序退出后删除自身。
    /// 
    /// 注意：此方法应在卸载流程最后、主窗口关闭后调用。
    /// </summary>
    public static void ScheduleSelfDestruct()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            var exeDir = Path.GetDirectoryName(exePath);
            var exeName = Path.GetFileName(exePath);

            // 使用 cmd /c ping -n 3 127.0.0.1 >nul & del /f /q "path" & rmdir "dir"
            // ping 等待 2 秒，让卸载程序完全退出后再删除
            var batchCommands = string.IsNullOrEmpty(exeDir)
                ? $"ping -n 3 127.0.0.1 >nul & del /f /q \"{exePath}\""
                : $"ping -n 3 127.0.0.1 >nul & del /f /q \"{exePath}\" & (rmdir \"{exeDir}\" 2>nul || exit 0)";

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchCommands}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
        }
        catch
        {
            // 自删除失败不影响卸载结果
        }
    }

    #endregion
}
