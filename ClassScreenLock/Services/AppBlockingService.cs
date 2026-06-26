using System;
using System.Collections.Generic;
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
    }

    private AppBlockingService() { }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();
        LogService.Observe(Task.Run(() => MonitorLoop(_cts.Token)), "AppBlocking.MonitorLoop");
        SetupFileWatchers();
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        CleanupFileWatchers();
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
                var settings = SettingsService.Blockage;
                bool lockState = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
                if (settings != null && ((settings.IsBasicProtectionEnabled || settings.IsAppBlockingEnabled) || lockState))
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

        if (isLocked)
        {
            return 300;
        }

        if (isProtectionOnly)
        {
            return 500;
        }

        return 2000;
    }

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

            foreach (var processName in rule.ProcessNames)
            {
                if (!string.IsNullOrEmpty(processName)) result.Add(processName);
            }
        }

        return result;
    }

    /// <summary>
    /// 检查是否是 PowerShell 进程
    /// </summary>
    private static bool IsPowerShellProcess(Process process)
    {
        try
        {
            return PowerShellNames.Contains(process.ProcessName);
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
        bool isManualBlockingActive,
        bool isBasicProtectionActive)
    {
        var name = process.ProcessName;

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

        return false;
    }

    /// <summary>
    /// 获取进程的可执行文件路径
    /// </summary>
    private static string? GetProcessExePath(Process process)
    {
        try
        {
            return NormalizePath(process.MainModule?.FileName);
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
    /// 记录被阻止的进程
    /// </summary>
    private static void LogBlockedProcess(string processName)
    {
        LogService.Instance.Log("Block", "Kill", processName);
    }

    #endregion

    private void CheckAndBlockProcesses()
    {
        var settings = SettingsService.Blockage;
        if (settings == null) return;

        bool lockState = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
        // 注：以下两条逻辑按用户要求保留 (D 不修复)，即锁屏态会强制开启两个开关。
        bool isBasicProtectionActive = settings.IsBasicProtectionEnabled || lockState;
        bool isManualBlockingActive = settings.IsAppBlockingEnabled || lockState;

        if (!isBasicProtectionActive && !isManualBlockingActive) return;

        // 关键修复 C：直接走强类型规则。
        var blockedRules = isManualBlockingActive
            ? settings.GetEffectiveBlockedRules()
            : new List<BlockedRule>();

        var protectionRules = isBasicProtectionActive
            ? (settings.ProtectionRules ?? new List<ProtectionRule>())
            : new List<ProtectionRule>();

        bool isLocked = LockScreenService.Instance.IsLocked;

        if (isBasicProtectionActive && !protectionRules.Any())
        {
            protectionRules = GetDefaultProtectionRules();
            settings.ProtectionRules = protectionRules;
            SettingsService.SaveBlockage(settings);
        }

        if (!blockedRules.Any() && !protectionRules.Any(r => r.IsEnabled || isLocked)) return;

        var blockedProcessNames = GetBlockedProcessNames(blockedRules);
        var blockedExePaths = GetBlockedFilePaths(blockedRules);
        var activeProtectionProcessNames = GetActiveProtectionProcessNames(protectionRules, isBasicProtectionActive, isLocked);

        if (!blockedProcessNames.Any() && !blockedExePaths.Any() && !activeProtectionProcessNames.Any()) return;

        // 关键修复 F：Process.GetProcesses() 返回的 Process 列表，外部不再二次 Dispose，
        // 全部交给 ProcessSingleProcess 的 finally 统一释放。
        var allProcesses = Process.GetProcesses();

        try
        {
            foreach (var process in allProcesses)
            {
                ProcessSingleProcess(process, blockedProcessNames, blockedExePaths,
                    activeProtectionProcessNames, isManualBlockingActive, isBasicProtectionActive);
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

            // 检查是否需要阻止
            if (ShouldBlockProcess(process, blockedProcessNames, blockedExePaths,
                activeProtectionProcessNames, isManualBlockingActive, isBasicProtectionActive))
            {
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
        return new List<ProtectionRule>
        {
            new() {
                Name = "终端防护",
                Description = "防止运行命令提示符 (CMD) 和 PowerShell",
                IsEnabled = true,
                IsSystem = true,
                ProcessNames = new List<string> { "cmd", "powershell", "pwsh" }
            },
            new() {
                Name = "脚本防护",
                Description = "防止运行 Windows 脚本宿主 (VBS/JS)",
                IsEnabled = true,
                IsSystem = true,
                ProcessNames = new List<string> { "wscript", "cscript" }
            },
            new() {
                Name = "系统工具防护",
                Description = "防止运行任务管理器和注册表编辑器",
                IsEnabled = true,
                IsSystem = true,
                ProcessNames = new List<string> { "taskmgr", "regedit" }
            }
        };
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
