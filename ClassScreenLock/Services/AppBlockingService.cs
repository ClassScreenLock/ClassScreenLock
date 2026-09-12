using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using ClassScreenLock.Views;
using Microsoft.Win32;

namespace ClassScreenLock.Services;

public class AppBlockingService
{
    private static readonly AppBlockingService _instance = new();
    public static AppBlockingService Instance => _instance;

    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private readonly List<FileSystemWatcher> _fileWatchers = new();
    private readonly HashSet<string> _watchedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _watcherLock = new();

    // 架构升级：主拦截从"轮询扫描"改为"WMI 进程创建事件订阅"（消除空窗期），
    // 轮询降级为低频兜底（保险丝），组策略 DisableCMD 提供系统级硬拦截（仅锁屏时）。
    private ProcessStartWatcher? _processWatcher;
    private bool _watcherActive;      // WMI 订阅应处于激活状态（防护开启时）
    private bool _watcherRunning;     // WMI 订阅实际在运行

    // 关键修复 B：原代码在访问进程 exe path 失败时把 PID 标记为"已知"，
    // 下一次直接跳过。但 PID 会被回收，会造成"前一个被拦截的进程的 PID
    // 恰好被一个合法进程继承，该合法进程被永久放行"。
    // 这里改成"按进程路径字符串集合"做命中判定，不再依赖"已知进程"缓存。
    // 若出现读不到路径的情况，按"既不安全也不危险"处理：跳过本次，下一周期重试。

    // 关键修复 A：原 AllowedPowerShellMarkers 与 OwnProcessNames 完全一致，
    // 且 IsPowerShellCommandAllowed 错误地读了 MainModule.FileName 而不是命令行，
    // 导致命令行 allow-marker 永远是 false，PowerShell 几乎必定被杀。
    // 修复后：
    //   1) Allow-marker 改为只通过 ProcessCommandLineReader 读到的真实命令行匹配；
    //   2) OwnProcessNames 通过 ProcessConstants 统一引用，避免双份定义。

    // PowerShell 进程名集合（注意大小写不敏感）
    private static readonly HashSet<string> PowerShellNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "pwsh"
    };

    private static DateTime _allowPowerShellUntil = DateTime.MinValue;
    private static readonly object _allowPowerShellLock = new();

    public static void AllowPowerShellTemporarily(int seconds = 10)
    {
        lock (_allowPowerShellLock)
        {
            _allowPowerShellUntil = DateTime.Now.AddSeconds(seconds);
        }

        // 跨进程同步：写共享注册表标记（Unix 毫秒时间戳），供 IFEO 重定向程序
        // CSL.IfeoRedirector.exe 读取。IFEO 劫持在 CreateProcess 层生效，软件自身
        // 发起的 PowerShell/CMD 调用（自动启动修复、计划任务注册等）会被 Redirector
        // 接管，必须靠这个标记放行，否则会把自身功能一并拦截。
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\ClassScreenLock", true);
            key?.SetValue("AllowPowerShellUntil",
                DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeMilliseconds(),
                RegistryValueKind.QWord);
        }
        catch
        {
            // 标记写失败不影响本进程内的放行判断，仅 IFEO 通道可能误拦自身调用
        }
    }

    private AppBlockingService() { }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();
        LogService.Observe(Task.Run(() => MonitorLoop(_cts.Token)), "AppBlocking.MonitorLoop");
        SetupFileWatchers();

        // 同步注册表现状，避免上次异常退出留下 DisableCMD / IFEO 策略残留
        try { GroupPolicyBlocker.SyncFromRegistry(); } catch { }
        try { IfeoBlocker.SyncFromRegistry(); } catch { }

        // 订阅锁屏状态变化，实时联动 WMI 订阅与组策略启停
        LockScreenService.Instance.PropertyChanged += OnLockScreenStateChanged;

        // 立即按当前状态刷新（启动时防护/锁屏可能已激活）
        RefreshDynamicState();
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        LockScreenService.Instance.PropertyChanged -= OnLockScreenStateChanged;
        CleanupFileWatchers();
        StopProcessWatcher();

        // 退出时恢复组策略与 IFEO，避免策略残留
        try
        {
            if (GroupPolicyBlocker.IsDisableCmdActive) GroupPolicyBlocker.DisableDisableCmd();
        }
        catch { }
        try { IfeoBlocker.Disable(); } catch { }
    }

    public void RefreshFileWatchers()
    {
        SetupFileWatchers();
    }

    private void SetupFileWatchers()
    {
        lock (_watcherLock)
        {
            CleanupFileWatchers();

            var settings = SettingsService.Blockage;
            if (settings == null) return;

            // 仅监控 Path 类型规则的目录（Name 规则与文件系统无关）
            var effectiveRules = settings.GetEffectiveBlockedRules();
            foreach (var rule in effectiveRules)
            {
                if (rule.Kind != BlockedRuleKind.Path) continue;
                if (string.IsNullOrWhiteSpace(rule.Value)) continue;
                if (!LooksLikePath(rule.Value)) continue;
                var path = NormalizePath(rule.Value);
                if (string.IsNullOrWhiteSpace(path)) continue;

                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

                if (_watchedDirectories.Contains(dir)) continue;

                try
                {
                    var watcher = new FileSystemWatcher(dir)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnFileCreated;
                    watcher.Renamed += OnFileRenamed;

                    _fileWatchers.Add(watcher);
                    _watchedDirectories.Add(dir);
                    LogService.Instance.Log("Info", "FileWatcher", "Setup", $"监控目录: {dir}");
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log("Error", "FileWatcher", "Setup", $"设置目录监控失败 {dir}: {ex.Message}");
                }
            }
        }
    }

    private void CleanupFileWatchers()
    {
        lock (_watcherLock)
        {
            foreach (var watcher in _fileWatchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Created -= OnFileCreated;
                    watcher.Renamed -= OnFileRenamed;
                    watcher.Dispose();
                }
                catch { }
            }
            _fileWatchers.Clear();
            _watchedDirectories.Clear();
        }
    }

    private static string? ComputeFileHash(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[Math.Min(64 * 1024, stream.Length)];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) return null;
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(buffer, 0, bytesRead);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return null;
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            var newFile = e.FullPath;
            if (!File.Exists(newFile)) return;

            var settings = SettingsService.Blockage;
            if (settings?.BlockedFileHashes == null || settings.BlockedFileHashes.Count == 0) return;

            var newFileHash = ComputeFileHash(newFile);
            if (string.IsNullOrWhiteSpace(newFileHash)) return;

            foreach (var kv in settings.BlockedFileHashes)
            {
                if (string.Equals(kv.Value, newFileHash, StringComparison.OrdinalIgnoreCase))
                {
                    LogService.Instance.Log("Warning", "FileWatcher", "Created", $"检测到新文件与阻止文件哈希匹配: {newFile}");
                    ApplyFileAcl(newFile, settings);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileWatcher", "Created", ex.Message);
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            var newFile = e.FullPath;
            if (!File.Exists(newFile)) return;

            var settings = SettingsService.Blockage;
            if (settings?.BlockedFileHashes == null || settings.BlockedFileHashes.Count == 0) return;

            var newFileHash = ComputeFileHash(newFile);
            if (string.IsNullOrWhiteSpace(newFileHash)) return;

            foreach (var kv in settings.BlockedFileHashes)
            {
                if (string.Equals(kv.Value, newFileHash, StringComparison.OrdinalIgnoreCase))
                {
                    LogService.Instance.Log("Warning", "FileWatcher", "Renamed", $"检测到重命名文件与阻止文件哈希匹配: {newFile}");
                    ApplyFileAcl(newFile, settings);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileWatcher", "Renamed", ex.Message);
        }
    }

    private static void ApplyFileAcl(string path, SoftwareBlockageModel settings)
    {
        if (!File.Exists(path)) return;

        settings.BlockedFileAclBackup ??= new Dictionary<string, string>();

        if (!settings.BlockedFileAclBackup.ContainsKey(path))
        {
            try
            {
                var current = new FileInfo(path).GetAccessControl(AccessControlSections.All);
                var sddl = current.GetSecurityDescriptorSddlForm(AccessControlSections.All);
                if (string.IsNullOrWhiteSpace(sddl))
                {
                    LogService.Instance.Log("Error", "FileAcl", "Backup", $"备份文件 ACL 失败（SDDL 为空）: {path}");
                    return;
                }
                settings.BlockedFileAclBackup[path] = sddl;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "FileAcl", "Backup", $"备份文件 ACL 失败 {path}: {ex.Message}");
                return;
            }
        }

        try
        {
            var fileSecurity = new FileSecurity();
            fileSecurity.SetAccessRuleProtection(true, false);

            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            fileSecurity.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));

            var currentUser = WindowsIdentity.GetCurrent();
            if (currentUser != null)
            {
                var userSid = currentUser.User;
                if (userSid != null)
                {
                    fileSecurity.AddAccessRule(new FileSystemAccessRule(userSid, FileSystemRights.FullControl, AccessControlType.Deny));
                }
            }

            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            fileSecurity.AddAccessRule(new FileSystemAccessRule(everyoneSid, FileSystemRights.FullControl, AccessControlType.Deny));

            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            fileSecurity.AddAccessRule(new FileSystemAccessRule(usersSid, FileSystemRights.FullControl, AccessControlType.Deny));

            new FileInfo(path).SetAccessControl(fileSecurity);
            LogService.Instance.Log("Info", "FileAcl", "Apply", $"已限制文件访问: {path}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileAcl", "Apply", $"设置文件 ACL 失败 {path}: {ex.Message}");
        }
    }

    private async Task MonitorLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // 架构升级：实时拦截已由 WMI 进程创建事件负责，这里只做
                // 1) 动态状态刷新（启停 WMI 订阅 / 组策略 DisableCMD）；
                // 2) 低频全量扫描兜底（防 WMI 通道静默失效、补杀漏网进程）。
                RefreshDynamicState();

                // 3) 守护 IFEO 重定向程序常驻占坑实例（防学生删除弹窗 exe）。
                //    周期跟随锁屏 300ms / 防护 500ms / 普通 2000ms，与看门狗服务、
                //    看门狗进程共同冗余守护，丢失即拉起。
                RedirectorGuard.EnsureResident();

                // 4) 刷新"安全中心登录账户满足最低允许权限"的放行心跳标记
                //    （HKLM，供 IfeoRedirector 跨进程读取；登出或崩溃后 60s 内自动恢复拦截）。
                ProcessBypassService.RefreshHeartbeat();

                var settings = SettingsService.Blockage;
                bool lockState = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
                // 关键修复：已启用子项规则时也进入扫描（子项独立于总开关生效）。
                bool hasEnabledProtectionRules = settings?.ProtectionRules?.Any(r => r.IsEnabled) ?? false;
                if (settings != null && (lockState || settings.IsBasicProtectionEnabled || settings.IsAppBlockingEnabled || hasEnabledProtectionRules))
                {
                    CheckAndBlockProcesses();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppBlockingService Error: {ex.Message}");
                LogService.Instance.Log("Error", "MonitorLoop", "AppBlocking", ex.Message);
            }

            int delay = CalculateCheckInterval();
            try { await Task.Delay(delay, token); } catch { break; }
        }
    }

    private int CalculateCheckInterval()
    {
        bool isLocked = LockScreenService.Instance.IsLocked;
        bool isProtectionOnly = LockScreenService.Instance.IsProtectionOnlyActive;

        // WMI 进程创建事件提供毫秒级实时拦截，本轮询仅为兜底（防 WMI 通道
        // 静默失效、补杀漏网进程），无需高频：锁屏 1000ms、防护 1500ms、普通 2000ms。
        // 原先 300/500ms 的高频全量进程枚举是 CPU 占用偏高的主因之一。
        if (isLocked)
        {
            return 1000;
        }

        if (isProtectionOnly)
        {
            return 1500;
        }

        return 2000;
    }

    #region 动态状态管理（WMI 订阅 + 组策略）

    private void OnLockScreenStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LockScreenService.IsLocked) or nameof(LockScreenService.IsProtectionOnlyActive))
        {
            RefreshDynamicState();
        }
    }

    /// <summary>
    /// 按当前防护/锁屏状态动态启停：
    ///   1) WMI 进程创建事件订阅 —— 任一防护激活（锁屏/总开关/子项/手动阻止）即启动；
    ///   2) 组策略 DisableCMD —— 仅锁屏（Full/ProtectionOnly）时启用，
    ///      避免拦截软件自身对 cmd/批处理的合法调用（看门狗、自举脚本等）。
    /// </summary>
    private void RefreshDynamicState()
    {
        try
        {
            var settings = SettingsService.Blockage;
            bool lockState = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
            bool hasEnabledProtectionRules = settings?.ProtectionRules?.Any(r => r.IsEnabled) ?? false;
            bool protectionActive = lockState ||
                (settings != null && (settings.IsBasicProtectionEnabled || settings.IsAppBlockingEnabled || hasEnabledProtectionRules));

            // 1) WMI 订阅
            if (protectionActive)
            {
                if (!_watcherActive)
                {
                    _watcherActive = true;
                    StartProcessWatcher();
                }
                else if (!_watcherRunning)
                {
                    // 上次启动失败：下一次刷新时重试
                    StartProcessWatcher();
                }
            }
            else if (_watcherActive)
            {
                _watcherActive = false;
                StopProcessWatcher();
            }

            // 2) 组策略 DisableCMD（仅锁屏）
            if (lockState)
            {
                if (!GroupPolicyBlocker.IsDisableCmdActive)
                {
                    GroupPolicyBlocker.EnableDisableCmd();
                }
            }
            else if (GroupPolicyBlocker.IsDisableCmdActive)
            {
                GroupPolicyBlocker.DisableDisableCmd();
            }

            // 3) IFEO 映像劫持（防护激活时对生效规则进程名写 Debugger 键，CreateProcess 层硬拦截）
            //    满足最低放行权限（老师登录且未锁屏）时自动撤销劫持（还原管控），登出/锁屏后恢复
            if (ProcessBypassService.IsBypassActive())
            {
                if (IfeoBlocker.IsActive)
                {
                    IfeoBlocker.Disable();
                }
            }
            else if (protectionActive)
            {
                IfeoBlocker.Enable(GetIfeoTargetNames());
            }
            else if (IfeoBlocker.IsActive)
            {
                IfeoBlocker.Disable();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "RefreshState", "AppBlocking", ex.Message);
        }
    }

    /// <summary>
    /// 计算当前需要被 IFEO 劫持的进程名集合：
    ///   手动 Name 阻止规则 + 激活的子项防护规则（含锁屏时自动补全的默认规则）。
    /// 与 ShouldBlockProcess 的身份识别不同，IFEO 只能按"文件名"精确劫持，
    /// 复制/改名变体不在其中（由 WMI 实时拦截 + 高频轮询兜底覆盖）。
    /// </summary>
    private HashSet<string> GetIfeoTargetNames()
    {
        var settings = SettingsService.Blockage;
        bool lockState = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
        bool hasEnabledProtectionRules = settings?.ProtectionRules?.Any(r => r.IsEnabled) ?? false;
        bool isManualBlockingActive = settings?.IsAppBlockingEnabled == true || lockState;
        bool isBasicProtectionActive = lockState ||
            (settings != null && (settings.IsBasicProtectionEnabled || hasEnabledProtectionRules));

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (isManualBlockingActive && settings != null)
        {
            foreach (var n in GetBlockedProcessNames(settings.GetEffectiveBlockedRules()))
            {
                if (!string.IsNullOrWhiteSpace(n)) result.Add(n);
            }
        }

        if (isBasicProtectionActive)
        {
            var rules = settings?.ProtectionRules ?? new List<ProtectionRule>();
            if (!rules.Any()) rules = GetDefaultProtectionRules();

            foreach (var rule in rules)
            {
                if (!rule.IsEnabled && !lockState) continue;
                if (rule.ProcessNames == null) continue;
                // 关键：带 CommandLineKeywords 的规则（如 mmc 承载的 services.msc/gpedit.msc）
                // 只能靠命令行精确匹配，绝不能进 IFEO 名单（否则会劫持所有 MMC 窗口）。
                if (rule.CommandLineKeywords != null && rule.CommandLineKeywords.Count > 0) continue;
                foreach (var pn in rule.ProcessNames)
                {
                    if (!string.IsNullOrWhiteSpace(pn)) result.Add(pn.Trim());
                }
            }
        }

        return result;
    }

    private void StartProcessWatcher()
    {
        try
        {
            _processWatcher ??= new ProcessStartWatcher();
            _processWatcher.ProcessStarted -= OnProcessStarted;
            _processWatcher.ProcessStarted += OnProcessStarted;
            _watcherRunning = _processWatcher.Start();
        }
        catch (Exception ex)
        {
            _watcherRunning = false;
            LogService.Instance.Log("Error", "StartWatcher", "AppBlocking", $"启动 WMI 订阅失败: {ex.Message}");
        }
    }

    private void StopProcessWatcher()
    {
        _watcherRunning = false;
        if (_processWatcher == null) return;
        try
        {
            _processWatcher.ProcessStarted -= OnProcessStarted;
            _processWatcher.Stop();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "StopWatcher", "AppBlocking", $"停止 WMI 订阅失败: {ex.Message}");
        }
    }

    /// <summary>
    /// WMI 进程创建事件回调：进程启动瞬间立即评估并拦截（实时，无空窗期）。
    /// 运行在 WMI 事件线程，全程 try/catch。
    /// 进程刚创建时 EXE 路径可能短暂不可读，做少量重试；仍失败则交给兜底轮询。
    /// </summary>
    private void OnProcessStarted(int pid)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process == null || process.Id <= 4) return;
                if (ProcessConstants.IsOwnProcess(process.ProcessName)) return;

                var (names, paths, activeRules, cmdLineRules, manual, basic) = BuildDecisionContext();
                if (!manual && !basic) return;

                ProcessSingleProcess(process, names, paths, activeRules, cmdLineRules, manual, basic);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= 2)
                {
                    // 进程刚创建时 EXE 路径可能短暂不可读，此处静默失败并交给兜底轮询，不再输出评估日志避免刷屏。
                    _ = ex;
                    return;
                }
                try { Thread.Sleep(120); } catch { return; }
            }
        }
    }

    /// <summary>
    /// 从当前设置构建一次决策上下文（阻止名单 / 子项规则 / 开关状态）。
    /// 供 WMI 实时回调与兜底轮询共用，保证两条通道判定一致。
    /// 内部带 1 秒缓存：WMI 事件回调在系统繁忙时每秒可达数十次，
    /// 规则集合重建（LINQ + 多个 HashSet）无需每次执行。
    /// </summary>
    private (HashSet<string> names, HashSet<string> paths, HashSet<string> activeRules, List<ProtectionRule> cmdLineRules, bool manual, bool basic) BuildDecisionContext()
    {
        lock (_ctxLock)
        {
            if ((DateTime.UtcNow - _ctxStamp).TotalSeconds < 1 && _ctxCache.HasValue)
            {
                return _ctxCache!.Value;
            }

            var ctx = BuildDecisionContextCore();
            _ctxCache = ctx;
            _ctxStamp = DateTime.UtcNow;
            return ctx;
        }
    }

    private readonly object _ctxLock = new();
    private DateTime _ctxStamp = DateTime.MinValue;
    private (HashSet<string> names, HashSet<string> paths, HashSet<string> activeRules, List<ProtectionRule> cmdLineRules, bool manual, bool basic)? _ctxCache;

    private (HashSet<string> names, HashSet<string> paths, HashSet<string> activeRules, List<ProtectionRule> cmdLineRules, bool manual, bool basic) BuildDecisionContextCore()
    {
        var settings = SettingsService.Blockage;
        if (settings == null)
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new List<ProtectionRule>(),
                false, false);
        }

        bool lockState = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
        bool hasEnabledProtectionRules = settings.ProtectionRules?.Any(r => r.IsEnabled) ?? false;
        bool isBasicProtectionActive = settings.IsBasicProtectionEnabled || hasEnabledProtectionRules || lockState;
        bool isManualBlockingActive = settings.IsAppBlockingEnabled || lockState;

        var blockedRules = isManualBlockingActive
            ? settings.GetEffectiveBlockedRules()
            : new List<BlockedRule>();

        var protectionRules = isBasicProtectionActive
            ? (settings.ProtectionRules ?? new List<ProtectionRule>())
            : new List<ProtectionRule>();

        if (isBasicProtectionActive && !protectionRules.Any())
        {
            protectionRules = GetDefaultProtectionRules();
            settings.ProtectionRules = protectionRules;
            SettingsService.SaveBlockage(settings);
        }

        bool isLocked = LockScreenService.Instance.IsLocked;

        return (GetBlockedProcessNames(blockedRules),
            GetBlockedFilePaths(blockedRules),
            GetActiveProtectionProcessNames(protectionRules, isBasicProtectionActive, isLocked),
            GetActiveCommandLineRules(protectionRules, isBasicProtectionActive, isLocked),
            isManualBlockingActive,
            isBasicProtectionActive);
    }

    #endregion

    #region CheckAndBlockProcesses 辅助方法

    /// <summary>
    /// 从阻止规则中提取进程名称集合（仅 Name 类型）。
    /// </summary>
    private static HashSet<string> GetBlockedProcessNames(IEnumerable<BlockedRule> rules)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            if (rule == null || rule.Kind != BlockedRuleKind.Name) continue;
            var r = rule.Value?.Trim();
            if (string.IsNullOrWhiteSpace(r)) continue;

            if (r.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                r = Path.GetFileNameWithoutExtension(r);
            }
            if (!string.IsNullOrWhiteSpace(r)) result.Add(r);
        }

        return result;
    }

    /// <summary>
    /// 从阻止规则中提取阻止的文件路径集合（仅 Path 类型，已规范化）。
    /// </summary>
    private static HashSet<string> GetBlockedFilePaths(IEnumerable<BlockedRule> rules)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            if (rule == null || rule.Kind != BlockedRuleKind.Path) continue;
            var r = rule.Value?.Trim();
            if (string.IsNullOrWhiteSpace(r)) continue;

            if (!LooksLikePath(r)) continue;

            var normalized = NormalizePath(r);
            if (!string.IsNullOrWhiteSpace(normalized)) result.Add(normalized);
        }

        return result;
    }

    /// <summary>
    /// 获取活跃的保护进程名称集合
    /// </summary>
    private static HashSet<string> GetActiveProtectionProcessNames(IEnumerable<ProtectionRule> protectionRules, bool isBasicProtectionActive, bool isLocked)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!isBasicProtectionActive) return result;

        foreach (var rule in protectionRules)
        {
            if (!rule.IsEnabled && !isLocked) continue;
            if (rule.ProcessNames == null) continue;
            // 带 CommandLineKeywords 的规则走命令行精确匹配，不能仅凭进程名命中
            //（如 mmc 承载多个管理单元，仅按进程名会把磁盘管理等全部拦截）。
            if (rule.CommandLineKeywords != null && rule.CommandLineKeywords.Count > 0) continue;

            foreach (var processName in rule.ProcessNames)
            {
                if (!string.IsNullOrEmpty(processName)) result.Add(processName);
            }
        }

        return result;
    }

    /// <summary>
    /// 获取激活的"命令行关键词规则"列表。
    /// 这类规则（如 services.msc / gpedit.msc / secpol.msc 由 mmc.exe 承载）必须
    /// 进程名命中且命令行含关键词才拦截，不能仅凭进程名（会误伤磁盘管理等 MMC 窗口）。
    /// </summary>
    private static List<ProtectionRule> GetActiveCommandLineRules(
        IEnumerable<ProtectionRule> protectionRules, bool isBasicProtectionActive, bool isLocked)
    {
        var result = new List<ProtectionRule>();
        if (!isBasicProtectionActive) return result;

        foreach (var rule in protectionRules)
        {
            if (!rule.IsEnabled && !isLocked) continue;
            if (rule.ProcessNames == null || rule.CommandLineKeywords == null) continue;
            if (rule.CommandLineKeywords.Count == 0) continue;
            if (rule.ProcessNames.All(string.IsNullOrWhiteSpace)) continue;

            result.Add(rule);
        }

        return result;
    }

    /// <summary>
    /// 检查是否是 PowerShell 进程。
    /// 关键修复（复制+改名绕过）：改名后进程名不再是 powershell/pwsh，
    /// 这里通过文件身份识别（PE 资源 OriginalFilename / FileDescription / SHA256）判断，
    /// 官方原版与复制/改名伪装均可命中。
    /// </summary>
    private static bool IsPowerShellProcess(Process process)
    {
        try
        {
            if (PowerShellNames.Contains(process.ProcessName)) return true;

            var exePath = GetProcessExePath(process);
            return !string.IsNullOrWhiteSpace(exePath) && ProcessIdentityDetector.IsPowerShellIdentity(exePath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查 PowerShell 是否在临时允许时间内
    /// </summary>
    private static bool IsPowerShellTemporarilyAllowed()
    {
        lock (_allowPowerShellLock)
        {
            return DateTime.Now < _allowPowerShellUntil;
        }
    }

    /// <summary>
    /// 检查 PowerShell 进程的命令行是否包含允许的标记。
    /// 关键修复 A：原代码读 MainModule.FileName（EXE 路径），永远不包含 marker。
    /// 这里改用 WMI 读取真实命令行（带 TTL 缓存，避免每次都 WMI）。
    /// </summary>
    private static bool IsPowerShellCommandAllowed(Process process)
    {
        try
        {
            var cmdLine = ProcessCommandLineReader.GetCommandLine(process);
            if (string.IsNullOrEmpty(cmdLine)) return false;

            // 显式放行标记（软件自身所有 PowerShell 调用统一嵌入，与 IFEO 侧 AllowMarkers 一致）
            // 优先匹配；回退到进程名隐式匹配，兼容历史调用。
            if (cmdLine.IndexOf(ProcessConstants.PowerShellAllowMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return ProcessConstants.OwnProcessNames.Any(marker =>
                cmdLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// PowerShell 进程是否被"暂时放行"：时间窗内 OR 命令行包含 allow-marker。
    /// </summary>
    private static bool IsPowerShellAllowed(Process process)
    {
        if (!IsPowerShellProcess(process)) return false;
        if (IsPowerShellTemporarilyAllowed()) return true;
        return IsPowerShellCommandAllowed(process);
    }

    /// <summary>
    /// 判断是否应该阻止该进程
    /// </summary>
    private static bool ShouldBlockProcess(Process process,
        HashSet<string> blockedProcessNames,
        HashSet<string> blockedExePaths,
        HashSet<string> activeProtectionProcessNames,
        List<ProtectionRule> cmdLineRules,
        bool isManualBlockingActive,
        bool isBasicProtectionActive,
        out string? disguisedIdentity)
    {
        disguisedIdentity = null;
        var name = process.ProcessName;

        // 关键修复（复制+改名绕过 + 官方目录改名）：只要防护开启，先按文件身份识别"系统敏感工具"。
        // 复制/改名不改变 PE 资源（OriginalFilename / FileDescription）与 SHA256，身份识别不受进程名影响。
        // 一视同仁：官方目录内的官方工具与复制出去的伪装工具统一按同一套规则判断——
        //   从身份推导规范进程名（如 POWERSHELL.EXE → powershell），匹配已启用的子项防护规则。
        //   不依赖改名后的 ProcessName（官方目录内改名后进程名已不是 powershell）。
        if (isManualBlockingActive || isBasicProtectionActive)
        {
            var exePath = GetProcessExePath(process);
            if (!string.IsNullOrWhiteSpace(exePath) &&
                ProcessIdentityDetector.IdentifySensitiveTool(exePath, out var identity, out _))
            {
                disguisedIdentity = identity;
                var canonicalName = ToCanonicalProcessName(identity);
                if (canonicalName != null && activeProtectionProcessNames.Contains(canonicalName))
                {
                    if (IsPowerShellProcess(process)) return !IsPowerShellAllowed(process);
                    return true;
                }
            }
        }

        // 检查手动阻止列表 - 进程名
        if (isManualBlockingActive && blockedProcessNames.Contains(name))
        {
            // 关键修复：PowerShell 仅当 IsPowerShellAllowed 时放行（不再是裸 !IsAllowedPowerShellProcess）
            if (IsPowerShellProcess(process)) return !IsPowerShellAllowed(process);
            return true;
        }

        // 检查手动阻止列表 - 文件路径
        if (isManualBlockingActive && blockedExePaths.Count > 0)
        {
            // 关键修复 B：不再"读不到路径就放行"。读不到路径时直接跳过本次判定，
            // 下一周期重试。这样 PID 复用也不会绕过。
            var exePath = GetProcessExePath(process);
            if (!string.IsNullOrWhiteSpace(exePath) && blockedExePaths.Contains(exePath))
            {
                if (IsPowerShellProcess(process)) return !IsPowerShellAllowed(process);
                return true;
            }
        }

        // 检查基础保护列表
        if (isBasicProtectionActive && activeProtectionProcessNames.Contains(name))
        {
            if (IsPowerShellProcess(process)) return !IsPowerShellAllowed(process);
            return true;
        }

        // 检查命令行关键词规则（MMC 共享宿主：services.msc / gpedit.msc / secpol.msc）
        // 进程名命中且命令行含任一关键词才拦截，避免误伤磁盘管理等其它 MMC 窗口。
        if (cmdLineRules.Count > 0)
        {
            foreach (var rule in cmdLineRules)
            {
                if (!rule.ProcessNames.Any(p => string.Equals(p.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var cmdLine = ProcessCommandLineReader.GetCommandLine(process);
                if (string.IsNullOrWhiteSpace(cmdLine)) continue;

                foreach (var kw in rule.CommandLineKeywords)
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    if (cmdLine.IndexOf(kw.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (IsPowerShellProcess(process)) return !IsPowerShellAllowed(process);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 从敏感工具身份推导规范进程名（如 POWERSHELL.EXE → powershell）。
    /// 官方目录内的官方工具即使被改名（进程名变为 x），拦截判定也需用规范名
    /// 匹配子项防护规则（ProtectionRule.ProcessNames），而非改名后的 ProcessName。
    /// </summary>
    private static string? ToCanonicalProcessName(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity)) return null;
        try
        {
            var name = Path.GetFileNameWithoutExtension(identity);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取进程的可执行文件路径。
    /// 关键修复（复制+改名绕过）：优先使用 QueryFullProcessImageName 读取真实路径，
    /// 避免 32/64 位路径重定向导致与合法目录比对失准；失败时回退 MainModule。
    /// </summary>
    private static string? GetProcessExePath(Process process)
    {
        try
        {
            var path = ProcessIdentityDetector.QueryProcessImageName(process);
            if (string.IsNullOrWhiteSpace(path))
            {
                path = process.MainModule?.FileName;
            }
            return NormalizePath(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 终止进程
    /// </summary>
    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(true);
            LogBlockedProcess(process.ProcessName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to kill process {process.ProcessName}: {ex.Message}");
        }
    }

    /// <summary>
    /// 拦截提示冷却：同一进程名在冷却期内不重复弹窗，避免刷屏。
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime> _blockNotifyCooldown = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan BlockNotifyCooldownWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 被拦截进程的用户提示：显示与 IFEO 劫持一致的 WDAC 风格弹窗
    /// （"你的组织使用了 ClassScreenLock 应用程序控制来阻止此应用"）。
    /// 主程序以 SYSTEM + UIAccess 身份显示，可覆盖锁屏遮罩等一切窗口。
    /// </summary>
    private static void NotifyProcessBlocked(Process process, string processName)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (_blockNotifyCooldown.TryGetValue(processName, out var last) &&
                (now - last) < BlockNotifyCooldownWindow) return;
            _blockNotifyCooldown[processName] = now;

            var exePath = GetProcessExePath(process) ?? processName;
            var displayName = GetBlockedDisplayName(process, processName);

            // 切到 UI 线程显示弹窗（与 BlockNoticeService 收到 IFEO 通知时的做法一致）
            Dispatcher.UIThread.Post(() =>
            {
                try { WdacBlockWindow.Show(displayName, exePath); }
                catch { }
            });
        }
        catch { }
    }

    /// <summary>
    /// 计算弹窗中显示的应用名：命令行命中 MMC 管理单元时用友好名称，
    /// 其余情况回退到进程名。
    /// </summary>
    private static string GetBlockedDisplayName(Process process, string fallback)
    {
        try
        {
            var cmdLine = ProcessCommandLineReader.GetCommandLine(process);
            if (string.IsNullOrWhiteSpace(cmdLine)) return fallback;

            if (cmdLine.IndexOf("services.msc", StringComparison.OrdinalIgnoreCase) >= 0)
                return "服务管理 (services.msc)";
            if (cmdLine.IndexOf("gpedit.msc", StringComparison.OrdinalIgnoreCase) >= 0)
                return "组策略编辑器 (gpedit.msc)";
            if (cmdLine.IndexOf("secpol.msc", StringComparison.OrdinalIgnoreCase) >= 0)
                return "本地安全策略 (secpol.msc)";
            return fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// 记录被阻止的进程
    /// </summary>
    private static void LogBlockedProcess(string processName)
    {
        LogService.Instance.Log("Block", "Kill", processName);
    }

    #endregion

    private void CheckAndBlockProcesses()
    {
        // 放行激活（老师账户且未锁屏）：整轮跳过，避免无谓的全量进程枚举
        if (ProcessBypassService.IsBypassActive()) return;

        var (blockedProcessNames, blockedExePaths, activeProtectionProcessNames, cmdLineRules, isManualBlockingActive, isBasicProtectionActive) = BuildDecisionContext();

        if (!isBasicProtectionActive && !isManualBlockingActive) return;

        // 关键修复（复制+改名绕过）：不再因"规则为空"提前返回。
        // 即使没有任何名称/路径规则，只要防护开关开启，也必须继续扫描进程，
        // 识别"伪装的系统敏感工具"（复制+改名的 powershell/cmd 等不依赖用户配置的规则）。

        // 关键修复 F：Process.GetProcesses() 返回的 Process 列表，外部不再二次 Dispose，
        // 全部交给 ProcessSingleProcess 的 finally 统一释放。
        var allProcesses = Process.GetProcesses();

        try
        {
            foreach (var process in allProcesses)
            {
                ProcessSingleProcess(process, blockedProcessNames, blockedExePaths,
                    activeProtectionProcessNames, cmdLineRules, isManualBlockingActive, isBasicProtectionActive);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "CheckAndBlockProcesses", "Loop", ex.Message);
        }
    }

    /// <summary>
    /// 处理单个进程
    /// </summary>
    private void ProcessSingleProcess(Process process,
        HashSet<string> blockedProcessNames,
        HashSet<string> blockedExePaths,
        HashSet<string> activeProtectionProcessNames,
        List<ProtectionRule> cmdLineRules,
        bool isManualBlockingActive,
        bool isBasicProtectionActive)
    {
        try
        {
            // 跳过系统进程
            if (process.Id <= 4) return;

            string name = process.ProcessName;

            // 跳过自身进程
            if (ProcessConstants.IsOwnProcess(name)) return;

            // 安全中心登录账户满足最低允许权限：直接放行所有被拦截进程
            //（WMI 实时与轮询兜底共用本入口，一处放行两路都生效；
            //  IFEO 劫持链路由 IfeoRedirector 读取心跳标记自行放行，与此保持一致）。
            if (ProcessBypassService.IsBypassActive()) return;

            // 检查是否需要阻止
            if (ShouldBlockProcess(process, blockedProcessNames, blockedExePaths,
                activeProtectionProcessNames, cmdLineRules, isManualBlockingActive, isBasicProtectionActive,
                out var disguisedIdentity))
            {
                if (!string.IsNullOrWhiteSpace(disguisedIdentity))
                {
                    LogService.Instance.Log("Warning", "AppBlocking", "SensitiveToolKill",
                        $"检测到敏感工具 {disguisedIdentity}（进程名 {name}），已拦截");
                }
                NotifyProcessBlocked(process, name);
                KillProcess(process);
            }
        }
        catch { }
        finally
        {
            // 关键修复 F：统一在这里 Dispose，外部不再 Dispose。
            try { process.Dispose(); } catch { }
        }
    }

    private List<ProtectionRule> GetDefaultProtectionRules()
    {
        // 统一走 Model 的权威规则集，避免多处默认定义不一致（历史上曾出现 6 条 vs 4 条）。
        return Models.SoftwareBlockageModel.CreateDefaultProtectionRules();
    }

    private static bool LooksLikePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.IndexOf(Path.DirectorySeparatorChar) >= 0) return true;
        if (value.IndexOf(Path.AltDirectorySeparatorChar) >= 0) return true;
        if (value.Contains(":\\", StringComparison.Ordinal)) return true;
        return false;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var trimmed = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed)) return null;
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return null;
        }
    }
}
