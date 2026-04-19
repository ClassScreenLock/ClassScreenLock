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

    private static readonly HashSet<string> OwnProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ClassScreenLock",
        "CSL.Watchdog",
        "MonitorProcess",
        "BreakButtonProcess"
    };

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
            if (settings?.BlockedRules == null) return;

            foreach (var rule in settings.BlockedRules)
            {
                if (!LooksLikePath(rule)) continue;
                var path = NormalizePath(rule);
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
                settings.BlockedFileAclBackup[path] = current.GetSecurityDescriptorSddlForm(AccessControlSections.All);
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

    private bool IsAdministrator()
    {
        try
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }
        catch { return false; }
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
            
            // 锁屏时显著提高检查频率到 100ms，以确保任务管理器等无法开启
            // 非锁屏状态下维持 2000ms 以节省 CPU
            int delay = LockScreenService.Instance.IsLocked ? 100 : 2000;
            try { await Task.Delay(delay, token); } catch { break; }
        }
    }

    private void CheckAndBlockProcesses()
    {
        var settings = SettingsService.Blockage;
        if (settings == null) return;

        bool lockState = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
        bool isBasicProtectionActive = settings.IsBasicProtectionEnabled || lockState;
        bool isManualBlockingActive = settings.IsAppBlockingEnabled || lockState;

        if (!isBasicProtectionActive && !isManualBlockingActive) return;

        var blockedRules = isManualBlockingActive ? (settings.BlockedRules ?? new List<string>()) : new List<string>();
        var protectionRules = isBasicProtectionActive ? (settings.ProtectionRules ?? new List<ProtectionRule>()) : new List<ProtectionRule>();

        // 确保基础防护的核心规则始终存在且在锁屏时强制开启
        bool isLocked = LockScreenService.Instance.IsLocked;
        
        if (isBasicProtectionActive && (protectionRules == null || !protectionRules.Any()))
        {
            protectionRules = GetDefaultProtectionRules();
            settings.ProtectionRules = protectionRules;
            SettingsService.SaveBlockage(settings);
        }

        if (!blockedRules.Any() && !protectionRules.Any(r => r.IsEnabled || isLocked)) return;

        // 获取所有运行中的进程
        var allProcesses = Process.GetProcesses();
        try
        {
            // 预处理规则
            var activeProtectionProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (isBasicProtectionActive)
            {
                foreach (var rule in protectionRules)
                {
                    // 如果开启了基础防护，则强制启用所有子项，或者在锁屏状态，强制启用系统规则
                    if (isBasicProtectionActive || rule.IsEnabled || (isLocked && rule.IsSystem))
                    {
                        if (rule.ProcessNames == null) continue;
                        foreach (var processName in rule.ProcessNames)
                        {
                            if (!string.IsNullOrEmpty(processName)) activeProtectionProcessNames.Add(processName);
                        }
                    }
                }
            }

            var blockedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var blockedExePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in blockedRules)
            {
                var r = rule?.Trim();
                if (string.IsNullOrWhiteSpace(r)) continue;

                if (LooksLikePath(r))
                {
                    var normalized = NormalizePath(r);
                    if (!string.IsNullOrWhiteSpace(normalized)) blockedExePaths.Add(normalized);
                }
                else
                {
                    if (r.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        r = Path.GetFileNameWithoutExtension(r);
                    }
                    if (!string.IsNullOrWhiteSpace(r)) blockedProcessNames.Add(r);
                }
            }

            foreach (var process in allProcesses)
            {
                try
                {
                    if (process.Id <= 4) continue;

                    string name = process.ProcessName;
                    
                    if (OwnProcessNames.Contains(name))
                    {
                        continue;
                    }

                    bool shouldKill = false;

                    if (isManualBlockingActive && blockedProcessNames.Contains(name))
                    {
                        shouldKill = true;
                    }
                    else if (isManualBlockingActive && blockedExePaths.Count > 0)
                    {
                        try
                        {
                            var exePath = process.MainModule?.FileName;
                            var normalizedExePath = NormalizePath(exePath);
                            if (!string.IsNullOrWhiteSpace(normalizedExePath) && blockedExePaths.Contains(normalizedExePath))
                            {
                                shouldKill = true;
                            }
                        }
                        catch
                        {
                        }
                    }
                    else if (isBasicProtectionActive && activeProtectionProcessNames.Contains(name))
                    {
                        shouldKill = true;
                    }

                    if (shouldKill)
                    {
                        // 尝试停止进程
                        try 
                        {
                            process.Kill(true);
                            LogService.Instance.Log("Block", "Kill", name);
                        }
                        catch (Exception killEx)
                        {
                            Debug.WriteLine($"Failed to kill process {name}: {killEx.Message}");
                        }
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
        finally
        {
            foreach (var p in allProcesses) { try { p.Dispose(); } catch { } }
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
            },
            new() { 
                Name = "远程辅助防护", 
                Description = "防止运行远程桌面和远程协助工具", 
                IsEnabled = false, 
                IsSystem = true,
                ProcessNames = new List<string> { "mstsc", "msra" } 
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
