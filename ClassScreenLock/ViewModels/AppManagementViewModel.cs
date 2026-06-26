using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using Avalonia;
using Avalonia.Threading;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using SystemDrawingIcon = System.Drawing.Icon;
using SystemDrawingBitmap = System.Drawing.Bitmap;

namespace ClassScreenLock.ViewModels;

public partial class AppManagementViewModel : ViewModelBase
{
    // 不再各自维护一份 OwnProcessNames，统一使用 ProcessConstants。

    [ObservableProperty]
    private ObservableCollection<AppInfo> _runningApps = new();

    /// <summary>
    /// 强类型阻止规则集合（区分 Name / Path）。
    /// 旧字段 BlockedRules: List<string> 在加载时一次性迁移到这里。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BlockedRule> _blockedRules = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showAllProcesses = false;

    private List<AppInfo> _allRunningApps = new();

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _hasRunningApps;

    partial void OnRunningAppsChanged(ObservableCollection<AppInfo> value)
    {
        UpdateHasRunningApps();
    }

    private void UpdateHasRunningApps()
    {
        HasRunningApps = RunningApps?.Count > 0;
    }

    [ObservableProperty]
    private bool _isAppBlockingEnabled = true;

    [ObservableProperty]
    private bool _isBasicProtectionEnabled = true;

    private bool _isInitializing = true;

    [ObservableProperty]
    private ObservableCollection<ProtectionRule> _protectionRules = new();

    [ObservableProperty]
    private int _selectedSubTabIndex = 0; // 0: 运行中, 1: 阻止列表, 2: 基础防护

    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private double _sidebarWidth = 220;

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
        SidebarWidth = IsSidebarExpanded ? 220 : 64;
    }

    private DispatcherTimer? _refreshTimer;
    private readonly object _monitorLock = new();

    // 注意：旧的 GetCurrentProcess().TotalProcessorTime 只能反映 ClassScreenLock.exe 自己，
    // 不能反映 MonitorProcess / Watchdog 进程。原先的"自适应节流"语义错误，已移除。
    private const double MonitorIntervalFastSeconds = 1.0;
    private const double MonitorIntervalSlowSeconds = 2.0;
    private DispatcherTimer? _statsTimer;

    private readonly ConcurrentDictionary<int, (long Read, long Write, DateTime Stamp)> _ioRateSamples = new();
    private readonly ConcurrentDictionary<int, (TimeSpan Cpu, DateTime Stamp)> _cpuSamples = new();

    public AppManagementViewModel()
    {
        _isInitializing = true;
        LoadSettings();
        _isInitializing = false;
    }

    public void StartRefreshTimer()
    {
        if (_refreshTimer != null) return;

        // 启动时立即刷新一次进程列表
        _ = RefreshApps();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(MonitorIntervalFastSeconds)
        };
        _refreshTimer.Tick += async (s, e) => await OnRefreshTick();
        _refreshTimer.Start();

        // 独立定时器拉取统计信息（CPU/IO/内存）。原代码在 RefreshTimer 中
        // 触发 UpdateProcessStats 后又自己 Sleep 调节间隔，造成无效自反馈，已重构。
        _statsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(MonitorIntervalFastSeconds)
        };
        _statsTimer.Tick += async (s, e) => await UpdateProcessStats();
        _statsTimer.Start();

        _ = OnRefreshTick();
    }

    public void StopRefreshTimer(string reason = "已停止")
    {
        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer = null;
        }
        if (_statsTimer != null)
        {
            _statsTimer.Stop();
            _statsTimer = null;
        }
        _ioRateSamples.Clear();
        _cpuSamples.Clear();
    }

    private async Task OnRefreshTick()
    {
        await RefreshApps();
    }

    private async Task UpdateProcessStats()
    {
        List<AppInfo> appsToUpdate;
        lock (_allRunningApps)
        {
            appsToUpdate = RunningApps.ToList();
        }

        if (!appsToUpdate.Any()) return;

        await Task.Run(() =>
        {
            foreach (var app in appsToUpdate)
            {
                UpdateSingleProcessStats(app);
            }
        });
    }

    private void UpdateSingleProcessStats(AppInfo app)
    {
        try
        {
            using var process = Process.GetProcessById(app.ProcessId);
            if (process.HasExited) return;

            UpdateProcessIoStats(app, process);
            UpdateProcessMemoryStats(app, process);
            UpdateProcessCpuStats(app, process);
            UpdateProcessIoTotalStats(app, process);
        }
        catch { /* 进程可能已关闭或无权访问 */ }
    }

    private void UpdateProcessIoStats(AppInfo app, Process process)
    {
        if (TryGetProcessIoRate(process, out var rate))
        {
            Dispatcher.UIThread.Post(() =>
            {
                app.IoRate = $"{FormatBytes((long)rate)}/s";
            });
        }
    }

    private void UpdateProcessMemoryStats(AppInfo app, Process process)
    {
        long mem = 0;
        try { mem = process.PrivateMemorySize64; } catch { }
        if (mem <= 0)
        {
            try { mem = process.WorkingSet64; } catch { }
        }
        int tc = process.Threads.Count;
        int hc = process.HandleCount;

        Dispatcher.UIThread.Post(() =>
        {
            app.MemoryUsage = mem;
            app.MemoryUsageString = FormatBytes(mem);
            app.ThreadCount = tc;
            app.HandleCount = hc;
        });
    }

    private void UpdateProcessCpuStats(AppInfo app, Process process)
    {
        if (TryGetProcessCpuUsage(process, out double cpu))
        {
            Dispatcher.UIThread.Post(() =>
            {
                app.CpuUsage = cpu;
                app.CpuUsageString = $"{cpu:0.#}%";
            });
        }
    }

    private void UpdateProcessIoTotalStats(AppInfo app, Process process)
    {
        if (TryGetProcessIoTotal(process, out long ioTotal))
        {
            string ioTotalStr = FormatBytes(ioTotal);
            Dispatcher.UIThread.Post(() =>
            {
                app.TotalIoUsage = ioTotalStr;
            });
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS ioCounters);

    private bool TryGetProcessIoTotal(Process p, out long total)
    {
        total = 0;
        try
        {
            if (GetProcessIoCounters(p.Handle, out var counters))
            {
                var t = counters.ReadTransferCount + counters.WriteTransferCount;
                if (t > long.MaxValue) t = (ulong)long.MaxValue;
                total = (long)t;
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private bool TryGetProcessIoRate(Process p, out double bytesPerSecond)
    {
        bytesPerSecond = 0;
        try
        {
            if (!GetProcessIoCounters(p.Handle, out var counters))
            {
                _ioRateSamples.TryRemove(p.Id, out _);
                return false;
            }

            var now = DateTime.UtcNow;
            long currentRead = (long)Math.Min(counters.ReadTransferCount, (ulong)long.MaxValue);
            long currentWrite = (long)Math.Min(counters.WriteTransferCount, (ulong)long.MaxValue);

            if (_ioRateSamples.TryGetValue(p.Id, out var last))
            {
                var sec = (now - last.Stamp).TotalSeconds;
                _ioRateSamples[p.Id] = (currentRead, currentWrite, now);
                if (sec > 0.1)
                {
                    var readDelta = Math.Max(0, currentRead - last.Read);
                    var writeDelta = Math.Max(0, currentWrite - last.Write);
                    bytesPerSecond = (readDelta + writeDelta) / sec;
                    return true;
                }
                return false;
            }

            _ioRateSamples[p.Id] = (currentRead, currentWrite, now);
            return false;
        }
        catch
        {
            _ioRateSamples.TryRemove(p.Id, out _);
            return false;
        }
    }

    private bool TryGetProcessCpuUsage(Process p, out double percent)
    {
        percent = 0;
        try
        {
            var now = DateTime.UtcNow;
            var total = p.TotalProcessorTime;
            if (_cpuSamples.TryGetValue(p.Id, out var last))
            {
                var sec = (now - last.Stamp).TotalSeconds;
                _cpuSamples[p.Id] = (total, now);
                if (sec > 0.1)
                {
                    var cpuDeltaMs = (total - last.Cpu).TotalMilliseconds;
                    var denom = sec * Environment.ProcessorCount * 1000.0;
                    percent = Math.Min(100, Math.Max(0, (cpuDeltaMs / denom) * 100));
                    return true;
                }
                return false;
            }
            _cpuSamples[p.Id] = (total, now);
            return false;
        }
        catch
        {
            _cpuSamples.TryRemove(p.Id, out _);
            return false;
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
        double dblSByte = bytes;
        int i = 0;
        while (dblSByte >= 1024 && i < Suffix.Length - 1)
        {
            dblSByte /= 1024;
            i++;
        }
        return $"{dblSByte:0.##} {Suffix[i]}";
    }

    private void LoadSettings()
    {
        var settings = SettingsService.Blockage;
        IsBasicProtectionEnabled = settings.IsBasicProtectionEnabled;
        IsAppBlockingEnabled = settings.IsAppBlockingEnabled;

        // 迁移旧的 List<string> BlockedRules 到强类型 BlockedRule
        MigrateLegacyBlockedRules(settings);

        // 把强类型规则投影到 UI ObservableCollection
        BlockedRules = new ObservableCollection<BlockedRule>(settings.GetEffectiveBlockedRules());

        // 初始化基础防护规则
        if (settings.ProtectionRules == null || !settings.ProtectionRules.Any() || settings.ProtectionRules.Any(r => string.IsNullOrEmpty(r.Name)))
        {
            var defaultRules = GetDefaultProtectionRules();
            if (settings.ProtectionRules != null && settings.ProtectionRules.Any())
            {
                for (int i = 0; i < Math.Min(defaultRules.Count, settings.ProtectionRules.Count); i++)
                {
                    defaultRules[i].IsEnabled = settings.ProtectionRules[i].IsEnabled;
                }
            }
            ProtectionRules = new ObservableCollection<ProtectionRule>(defaultRules);
            settings.ProtectionRules = defaultRules;
            SaveSettings();
        }
        else
        {
            ProtectionRules = new ObservableCollection<ProtectionRule>(settings.ProtectionRules);
        }

        if (IsAppBlockingEnabled)
        {
            ApplyBlockedFilePermissions();
            SaveSettings();
        }
    }

    /// <summary>
    /// 把遗留的 List&lt;string&gt; BlockedRules 一次性迁移到 BlockedRulesTyped，
    /// 并清空遗留列表避免下次重复迁移。
    /// </summary>
    private void MigrateLegacyBlockedRules(SoftwareBlockageModel settings)
    {
        if (settings.BlockedRules == null || settings.BlockedRules.Count == 0) return;
        if (settings.BlockedRulesTyped == null) settings.BlockedRulesTyped = new List<BlockedRule>();

        foreach (var raw in settings.BlockedRules)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var trimmed = raw.Trim();
            var kind = LooksLikePathRule(trimmed) ? BlockedRuleKind.Path : BlockedRuleKind.Name;
            var value = kind == BlockedRuleKind.Path ? BlockedRule.Normalize(trimmed) : trimmed;
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (settings.BlockedRulesTyped.Any(r => r.Kind == kind && string.Equals(r.Value, value, StringComparison.OrdinalIgnoreCase)))
                continue;
            settings.BlockedRulesTyped.Add(new BlockedRule(kind, value));
        }
        settings.BlockedRules.Clear();
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

    private void SaveSettings()
    {
        SettingsService.UpdateBlockage(settings =>
        {
            settings.BlockedRulesTyped = BlockedRules.ToList();
            settings.BlockedRules = new List<string>(); // 已迁移完成
            settings.IsBasicProtectionEnabled = IsBasicProtectionEnabled;
            settings.IsAppBlockingEnabled = IsAppBlockingEnabled;
            settings.ProtectionRules = ProtectionRules.ToList();
        });
    }

    private static bool ShouldApplyFileAcl(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext)) return true;
            return !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                   !ext.Equals(".com", StringComparison.OrdinalIgnoreCase) &&
                   !ext.Equals(".msi", StringComparison.OrdinalIgnoreCase) &&
                   !ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) &&
                   !ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) &&
                   !ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikePathRule(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.IndexOf(Path.DirectorySeparatorChar) >= 0) return true;
        if (value.IndexOf(Path.AltDirectorySeparatorChar) >= 0) return true;
        if (value.Contains(":\\", StringComparison.Ordinal)) return true;
        return false;
    }

    private void ApplyBlockedFilePermissions()
    {
        try
        {
            var settings = SettingsService.Blockage;
            if (settings == null) return;
            settings.BlockedFileAclBackup ??= new Dictionary<string, string>();

            // 关键：不要再用遗留的 BlockedRules 字符串列表，必须走强类型规则。
            // 仅 Path 类型才会触发文件 ACL。
            foreach (var rule in BlockedRules.ToList())
            {
                if (rule.Kind != BlockedRuleKind.Path) continue;
                if (string.IsNullOrWhiteSpace(rule.Value)) continue;
                if (!ShouldApplyFileAcl(rule.Value)) continue;
                ApplyBlockedFilePermissionForPath(rule.Value, settings);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileAcl", "ApplyAll", $"应用所有文件 ACL 失败: {ex.Message}");
        }
    }

    private static bool RestoreFileAclWithElevation(string filePath, string? sddl)
    {
        try
        {
            var escapedPath = filePath.Replace("'", "''");
            string psScript;

            if (!string.IsNullOrWhiteSpace(sddl))
            {
                var escapedSddl = sddl.Replace("'", "''");
                psScript = $@"
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetSecurityDescriptorSddlForm('{escapedSddl}')
Set-Acl -Path '{escapedPath}' -AclObject $acl
";
            }
            else
            {
                psScript = $@"
$acl = Get-Acl -Path '{escapedPath}'
$acl.SetAccessRuleProtection($false, $true)
Set-Acl -Path '{escapedPath}' -AclObject $acl
";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript.Replace("\"", "\\\"")}\"",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            AppBlockingService.AllowPowerShellTemporarily(15);
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(10000);
                if (process.ExitCode == 0)
                {
                    LogService.Instance.Log("Info", "FileAcl", "ElevatedRestore", $"已通过提权恢复文件权限: {filePath}");
                    return true;
                }
                else
                {
                    LogService.Instance.Log("Error", "FileAcl", "ElevatedRestore", $"提权恢复失败，退出码: {process.ExitCode}");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileAcl", "ElevatedRestore", $"提权恢复异常: {ex.Message}");
        }
        return false;
    }

    private static bool RestoreMultipleFileAclsWithElevation(List<(string filePath, string? sddl)> files)
    {
        if (files.Count == 0) return true;

        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (filePath, sddl) in files)
            {
                var escapedPath = filePath.Replace("'", "''");
                if (!string.IsNullOrWhiteSpace(sddl))
                {
                    var escapedSddl = sddl.Replace("'", "''");
                    sb.AppendLine($@"
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetSecurityDescriptorSddlForm('{escapedSddl}')
Set-Acl -Path '{escapedPath}' -AclObject $acl -ErrorAction SilentlyContinue
");
                }
                else
                {
                    // 关键修复：原代码会把"无备份 SDDL"的文件硬重置为继承父级 ACL。
                    // 这在原本是显式拒绝的文件上可能造成权限被完全打开。
                    // 没有备份时直接跳过（不再 SetAccessRuleProtection）。
                    sb.AppendLine($@"
# 无备份 SDDL，跳过（避免破坏原显式权限）
Write-Host 'Skip: no backup SDDL for {escapedPath}'
");
                }
            }

            var psScript = sb.ToString();
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript.Replace("\"", "\\\"")}\"",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            AppBlockingService.AllowPowerShellTemporarily(30);
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(30000);
                if (process.ExitCode == 0)
                {
                    LogService.Instance.Log("Info", "FileAcl", "BatchElevatedRestore", $"已批量恢复 {files.Count} 个文件权限");
                    return true;
                }
                else
                {
                    LogService.Instance.Log("Error", "FileAcl", "BatchElevatedRestore", $"批量恢复失败，退出码: {process.ExitCode}");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileAcl", "BatchElevatedRestore", $"批量恢复异常: {ex.Message}");
        }
        return false;
    }

    private void ApplyBlockedFilePermissionForPath(string rule, SoftwareBlockageModel settings)
    {
        if (string.IsNullOrWhiteSpace(rule)) return;
        if (!ShouldApplyFileAcl(rule)) return;
        if (!File.Exists(rule))
        {
            LogService.Instance.Log("Warning", "FileAcl", "Apply", $"文件不存在: {rule}");
            return;
        }

        if (!BackupFileAcl(rule, settings)) return;
        ComputeAndStoreFileHash(rule, settings);
        ApplyDenyAccessRules(rule);
    }

    private bool BackupFileAcl(string path, SoftwareBlockageModel settings)
    {
        if (settings.BlockedFileAclBackup.ContainsKey(path))
        {
            return true;
        }

        try
        {
            var current = new FileInfo(path).GetAccessControl(AccessControlSections.All);
            var sddl = current.GetSecurityDescriptorSddlForm(AccessControlSections.All);
            // 关键修复：原代码在读不到 SDDL 时仍会把 null 写进备份字典，
            // 之后恢复时走 "SetAccessRuleProtection($false,$true)" 把权限敞开。
            // 这里如果 SDDL 为空/读取失败，就拒绝备份并提示用户。
            if (string.IsNullOrWhiteSpace(sddl))
            {
                LogService.Instance.Log("Error", "FileAcl", "Backup", $"备份文件 ACL 失败（SDDL 为空）: {path}");
                return false;
            }
            settings.BlockedFileAclBackup[path] = sddl;
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileAcl", "Backup", $"备份文件 ACL 失败 {path}: {ex.Message}");
            return false;
        }
    }

    private void ComputeAndStoreFileHash(string path, SoftwareBlockageModel settings)
    {
        settings.BlockedFileHashes ??= new Dictionary<string, string>();
        var hash = ComputeFileHash(path);
        if (!string.IsNullOrWhiteSpace(hash))
        {
            settings.BlockedFileHashes[path] = hash;
            LogService.Instance.Log("Info", "FileAcl", "Hash", $"已记录文件哈希: {path}");
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

    private void ApplyDenyAccessRules(string path)
    {
        try
        {
            var fileSecurity = new FileSecurity();
            fileSecurity.SetAccessRuleProtection(true, false);

            // 允许 SYSTEM 完全控制
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            fileSecurity.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));

            // 拒绝当前用户访问
            var currentUser = WindowsIdentity.GetCurrent();
            if (currentUser?.User != null)
            {
                fileSecurity.AddAccessRule(new FileSystemAccessRule(currentUser.User, FileSystemRights.FullControl, AccessControlType.Deny));
            }

            // 拒绝 Everyone 和 Users 组访问
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

    private void RestoreBlockedFilePermissions()
    {
        try
        {
            var settings = SettingsService.Blockage;
            if (settings?.BlockedFileAclBackup == null || settings.BlockedFileAclBackup.Count == 0) return;

            var filesToRestoreWithElevation = new List<(string filePath, string? sddl)>();
            var pathsToRemove = new List<string>();

            foreach (var kv in settings.BlockedFileAclBackup.ToList())
            {
                var path = kv.Key;
                var sddl = kv.Value;

                if (string.IsNullOrWhiteSpace(path))
                {
                    settings.BlockedFileAclBackup.Remove(path);
                    continue;
                }

                CollectFilesForElevation(path, sddl, settings, filesToRestoreWithElevation);
                pathsToRemove.Add(path);
            }

            if (filesToRestoreWithElevation.Count > 0)
            {
                LogService.Instance.Log("Info", "FileAcl", "BatchRestore", $"需要提权恢复 {filesToRestoreWithElevation.Count} 个文件");
                RestoreMultipleFileAclsWithElevation(filesToRestoreWithElevation);
            }

            foreach (var path in pathsToRemove)
            {
                settings.BlockedFileAclBackup.Remove(path);
            }
            settings.BlockedFileHashes?.Clear();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileAcl", "RestoreAll", $"恢复所有文件 ACL 失败: {ex.Message}");
        }
    }

    private void CollectFilesForElevation(string path, string? sddl, SoftwareBlockageModel settings,
        List<(string filePath, string? sddl)> filesToRestoreWithElevation)
    {
        if (File.Exists(path))
        {
            CollectExistingFileForElevation(path, sddl, filesToRestoreWithElevation);
        }
        else
        {
            CollectMissingFileByHashForElevation(path, sddl, settings, filesToRestoreWithElevation);
        }
    }

    private void CollectExistingFileForElevation(string path, string? sddl,
        List<(string filePath, string? sddl)> filesToRestoreWithElevation)
    {
        // 关键修复：原代码在 sddl 为 null 时仍把 path 加入"用空 sddl 提权恢复"列表，
        // 然后用 SetAccessRuleProtection($false, $true) 显式把文件 ACL 切到"继承父级"，
        // 在原本是显式拒绝的文件上等于把权限打开。这里直接跳过，保留原 ACL。
        if (string.IsNullOrWhiteSpace(sddl))
        {
            LogService.Instance.Log("Warning", "FileAcl", "Restore", $"跳过（无 SDDL 备份）: {path}");
            return;
        }

        var restoreResult = RestoreSingleFileAcl(path, sddl);
        if (!restoreResult.Success)
        {
            filesToRestoreWithElevation.Add((path, sddl));
        }
    }

    private (bool Success, bool NeedsElevation) RestoreSingleFileAcl(string path, string sddl)
    {
        try
        {
            var original = new FileSecurity();
            original.SetSecurityDescriptorSddlForm(sddl);
            new FileInfo(path).SetAccessControl(original);
            LogService.Instance.Log("Info", "FileAcl", "Restore", $"已恢复文件访问: {path}");
            return (true, false);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, true);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "FileAcl", "Restore", $"普通恢复失败，将批量提权: {ex.Message}");
            return (false, true);
        }
    }

    private void CollectMissingFileByHashForElevation(string path, string? sddl, SoftwareBlockageModel settings,
        List<(string filePath, string? sddl)> filesToRestoreWithElevation)
    {
        if (string.IsNullOrWhiteSpace(sddl))
        {
            LogService.Instance.Log("Warning", "FileAcl", "Restore", $"跳过（无 SDDL 备份）: {path}");
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        var storedHash = settings.BlockedFileHashes?.GetValueOrDefault(path);
        if (string.IsNullOrWhiteSpace(storedHash)) return;

        var foundFile = FindFileByHash(dir, storedHash);
        if (foundFile == null) return;

        var restoreResult = RestoreSingleFileAcl(foundFile, sddl);
        if (!restoreResult.Success)
        {
            filesToRestoreWithElevation.Add((foundFile, sddl));
        }
    }

    private string? FindFileByHash(string directory, string targetHash)
    {
        try
        {
            var files = Directory.GetFiles(directory);
            foreach (var file in files)
            {
                try
                {
                    var fileHash = ComputeFileHash(file);
                    if (string.Equals(fileHash, targetHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileAcl", "Scan", $"扫描目录查找文件失败: {ex.Message}");
        }
        return null;
    }

    private void RestoreBlockedFilePermissionForPath(string rule)
    {
        try
        {
            var settings = SettingsService.Blockage;
            if (settings?.BlockedFileAclBackup == null) return;

            var path = BlockedRule.Normalize(rule);
            if (string.IsNullOrWhiteSpace(path)) return;

            var sddl = settings.BlockedFileAclBackup.TryGetValue(path, out var backupSddl) ? backupSddl : null;

            if (File.Exists(path))
            {
                RestoreExistingFileAcl(path, sddl);
            }
            else
            {
                RestoreMissingFileByHash(path, sddl, settings);
            }

            CleanupBackupEntries(path, settings);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FileAcl", "Restore", $"恢复文件 ACL 失败: {ex.Message}");
        }
    }

    private void RestoreExistingFileAcl(string path, string? sddl)
    {
        if (string.IsNullOrWhiteSpace(sddl))
        {
            // 关键修复：没有 SDDL 备份时不要用提权"重置为继承"，因为可能是受保护程序。
            // 原行为：直接通过 PowerShell 把权限敞开。
            LogService.Instance.Log("Warning", "FileAcl", "Restore", $"无 SDDL 备份，跳过（避免敞开权限）: {path}");
            return;
        }

        try
        {
            var original = new FileSecurity();
            original.SetSecurityDescriptorSddlForm(sddl);
            new FileInfo(path).SetAccessControl(original);
            LogService.Instance.Log("Info", "FileAcl", "Restore", $"已恢复文件访问: {path}");
        }
        catch (UnauthorizedAccessException)
        {
            LogService.Instance.Log("Warning", "FileAcl", "Restore", $"无权限恢复 ACL，尝试提权: {path}");
            RestoreFileAclWithElevation(path, sddl);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "FileAcl", "Restore", $"普通恢复失败，尝试提权: {ex.Message}");
            RestoreFileAclWithElevation(path, sddl);
        }
    }

    private void RestoreMissingFileByHash(string path, string? sddl, SoftwareBlockageModel settings)
    {
        if (string.IsNullOrWhiteSpace(sddl)) return;

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        var storedHash = settings.BlockedFileHashes?.GetValueOrDefault(path);
        if (string.IsNullOrWhiteSpace(storedHash)) return;

        var foundFile = FindFileByHash(dir, storedHash);
        if (foundFile == null) return;

        RestoreExistingFileAcl(foundFile, sddl);
    }

    private void CleanupBackupEntries(string path, SoftwareBlockageModel settings)
    {
        if (settings.BlockedFileAclBackup.ContainsKey(path))
        {
            settings.BlockedFileAclBackup.Remove(path);
        }
        if (settings.BlockedFileHashes?.ContainsKey(path) == true)
        {
            settings.BlockedFileHashes.Remove(path);
        }
    }

    // === 修复 G: 删除冗余的 RelayCommand 入口，全部由 partial OnChanged 驱动。 ===
    // 原代码 ToggleAppBlocking() 与 OnIsAppBlockingEnabledChanged() 都会跑一遍
    // Apply/Restore + SaveSettings，导致文件 ACL 操作被执行两次、SaveSettings 两次、
    // 通知弹两次。改用 partial OnChanged 单一入口。

    partial void OnIsAppBlockingEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            // 初始化阶段不要触发任何副作用
            return;
        }

        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            // 回滚 UI 状态
            _isInitializing = true;
            IsAppBlockingEnabled = !value;
            _isInitializing = false;
            return;
        }

        try
        {
            if (value)
            {
                ApplyBlockedFilePermissions();
            }
            else
            {
                RestoreBlockedFilePermissions();
            }
            SaveSettings();
            NotificationService.Instance.ShowSuccess(value ? "阻止列表已启用" : "阻止列表已禁用");
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"切换阻止列表失败: {ex.Message}");
        }
    }

    partial void OnIsBasicProtectionEnabledChanged(bool value)
    {
        if (_isInitializing) return;

        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            _isInitializing = true;
            IsBasicProtectionEnabled = !value;
            _isInitializing = false;
            return;
        }

        // 同步切换所有子项
        foreach (var rule in ProtectionRules)
        {
            rule.IsEnabled = value;
        }

        // 异步执行备份/恢复，避免阻塞 UI 线程。
        // 关键修复：捕获"切换时刻"的 value，UI 后续被切换不再影响本次操作，
        // 避免出现"开启 -> 关闭"两个异步任务竞态导致状态错乱。
        var snapshot = value;
        _ = Task.Run(async () =>
        {
            try
            {
                if (snapshot)
                {
                    var backupSuccess = await ProtectionBackupService.Instance.CreateBackupAsync();
                    if (!backupSuccess)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            NotificationService.Instance.ShowWarning("基础防护：备份创建失败，请检查权限或日志");
                        });
                    }
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        NotificationService.Instance.ShowInfo("正在解除基础防护并恢复系统状态...");
                    });

                    var restoreSuccess = await ProtectionBackupService.Instance.RestoreBackupAsync();

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (restoreSuccess)
                        {
                            NotificationService.Instance.ShowSuccess("基础防护解除成功，系统状态已恢复");
                            _isInitializing = true;
                            LoadSettings();
                            _isInitializing = false;
                            RefreshAppsCommand.Execute(null);
                        }
                        else
                        {
                            NotificationService.Instance.ShowError("基础防护解除失败：无法完整恢复备份文件，请查阅错误日志");
                        }
                    });
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SaveSettings();
                    NotificationService.Instance.ShowSuccess(snapshot ? "基础防护已开启 (所有子项已强制开启)" : "基础防护已关闭");
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    NotificationService.Instance.ShowError($"基础防护操作失败: {ex.Message}");
                });
            }
        });
    }

    [RelayCommand]
    private async Task ManualRestore()
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }
        var result = await ProtectionBackupService.Instance.RestoreBackupAsync();
        if (result)
        {
            NotificationService.Instance.ShowSuccess("手动恢复成功");
            _isInitializing = true;
            LoadSettings();
            _isInitializing = false;
            RefreshAppsCommand.Execute(null);
        }
        else
        {
            NotificationService.Instance.ShowError("手动恢复失败，请检查备份文件和日志");
        }
    }

    [RelayCommand]
    private void ToggleProtectionRule(ProtectionRule rule)
    {
        if (rule == null) return;
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }
        // 如果全局开关开启，则不允许关闭子项
        if (IsBasicProtectionEnabled && !rule.IsEnabled)
        {
            rule.IsEnabled = true;
            NotificationService.Instance.ShowWarning("基础防护开启时，必须开启所有子项功能");
        }
        SaveSettings();
    }

    /// <summary>
    /// 从运行中应用添加（默认按可执行文件路径，更精确）。
    /// </summary>
    [RelayCommand]
    private void AddToBlocked(AppInfo app)
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }

        if (app == null) return;

        if (ProcessConstants.IsOwnProcess(app.ProcessName))
        {
            NotificationService.Instance.ShowError("无法添加：不能将本程序自身加入黑名单");
            return;
        }

        // 关键修复：优先按可执行文件路径添加，避免"按名匹配"误杀同名其他路径的程序。
        // 如果确实没有可执行文件路径（受保护进程），退回到按进程名添加。
        BlockedRuleKind kind;
        string value;
        if (!string.IsNullOrWhiteSpace(app.ExecutablePath) && File.Exists(app.ExecutablePath))
        {
            kind = BlockedRuleKind.Path;
            value = BlockedRule.Normalize(app.ExecutablePath);
        }
        else
        {
            kind = BlockedRuleKind.Name;
            value = app.ProcessName ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            NotificationService.Instance.ShowError("无法添加：该应用没有可用的标识");
            return;
        }

        if (BlockedRules.Any(r => r.Kind == kind && string.Equals(r.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            NotificationService.Instance.ShowInfo("该规则已存在");
            return;
        }

        var rule = new BlockedRule(kind, value);
        BlockedRules.Add(rule);
        ApplyAddedRule(rule);
        SaveSettings();
        AppBlockingService.Instance.RefreshFileWatchers();
        NotificationService.Instance.ShowSuccess($"已添加 {value} 到阻止列表");
    }

    /// <summary>
    /// 从文件选择器 / 拖拽添加（始终按路径）。
    /// </summary>
    [RelayCommand]
    private void AddPathToBlocked(string path)
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }

        if (string.IsNullOrWhiteSpace(path)) return;

        var normalized = BlockedRule.Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            NotificationService.Instance.ShowError("路径无效");
            return;
        }

        if (IsOwnProgramPath(normalized))
        {
            NotificationService.Instance.ShowError("无法添加：不能将本程序自身加入黑名单");
            return;
        }

        if (BlockedRules.Any(r => r.Kind == BlockedRuleKind.Path &&
                                  string.Equals(r.Value, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var rule = new BlockedRule(BlockedRuleKind.Path, normalized);
        BlockedRules.Add(rule);
        ApplyAddedRule(rule);
        SaveSettings();
        AppBlockingService.Instance.RefreshFileWatchers();
        NotificationService.Instance.ShowSuccess($"已添加路径到阻止列表");
    }

    private void ApplyAddedRule(BlockedRule rule)
    {
        if (!IsAppBlockingEnabled) return;
        if (rule.Kind != BlockedRuleKind.Path) return;

        var settings = SettingsService.Blockage;
        if (settings == null) return;
        settings.BlockedFileAclBackup ??= new Dictionary<string, string>();
        ApplyBlockedFilePermissionForPath(rule.Value, settings);
    }

    private static bool IsOwnProgramPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(fileName) && ProcessConstants.OwnProcessNames.Contains(fileName))
            {
                return true;
            }

            var normalizedPath = BlockedRule.Normalize(path);
            if (string.IsNullOrEmpty(normalizedPath)) return false;

            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

            foreach (var ownName in ProcessConstants.OwnProcessNames)
            {
                var ownExePath = Path.Combine(baseDir, $"{ownName}.exe");
                if (string.Equals(normalizedPath, ownExePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var ext = Path.GetExtension(normalizedPath);
            if (string.Equals(ext, ".dll", StringComparison.OrdinalIgnoreCase) &&
                normalizedPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var dataPath = Path.Combine(baseDir, "Data").TrimEnd(Path.DirectorySeparatorChar);
            var normalizedPathTrimmed = normalizedPath.TrimEnd(Path.DirectorySeparatorChar);

            if (normalizedPathTrimmed.StartsWith(dataPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPathTrimmed, dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    [RelayCommand]
    private void RemoveFromBlocked(BlockedRule rule)
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }
        if (rule == null) return;

        if (rule.Kind == BlockedRuleKind.Path)
        {
            RestoreBlockedFilePermissionForPath(rule.Value);
        }

        if (BlockedRules.Remove(rule))
        {
            SaveSettings();
            AppBlockingService.Instance.RefreshFileWatchers();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterApps();
    }

    partial void OnShowAllProcessesChanged(bool value)
    {
        RefreshAppsCommand.Execute(null);
    }

    private void FilterApps()
    {
        var filtered = _allRunningApps.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(a =>
                (a.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                a.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();
        // O(n) 同步 + 不破坏 DataGrid 用户排序：
        // 1) 把已存在的项按目标顺序加入新列表（保留对象引用，更新绑定的字段）
        // 2) 把不再存在的项从原集合移除
        // 3) 不在 UI 端按业务排序插入，避免与用户手动排序冲突
        SyncRunningApps(filteredList);
        UpdateHasRunningApps();
    }

    private void SyncRunningApps(List<AppInfo> source)
    {
        var sourceSet = new HashSet<int>(source.Select(a => a.ProcessId));
        // 1) 移除已退出的项
        for (int i = RunningApps.Count - 1; i >= 0; i--)
        {
            if (!sourceSet.Contains(RunningApps[i].ProcessId))
            {
                RunningApps.RemoveAt(i);
            }
        }

        // 2) 把新出现的项按 source 顺序追加到末尾（不强行 Insert 到中间位置，
        //    保留 DataGrid 用户的当前排序状态）
        var existingIds = new HashSet<int>(RunningApps.Select(a => a.ProcessId));
        foreach (var item in source)
        {
            if (!existingIds.Contains(item.ProcessId))
            {
                RunningApps.Add(item);
            }
        }
    }

    [RelayCommand]
    private async Task RefreshApps()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;

        await Task.Run(() =>
        {
            var currentProcess = Process.GetCurrentProcess();
            var processes = FilterProcesses(currentProcess);

            var appInfos = processes.Select(p => CreateAppInfo(p))
                .OrderBy(p => p.CategoryOrder)
                .ThenBy(p => p.Name)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allRunningApps = appInfos;
                FilterApps();
                IsRefreshing = false;
            });
        });
    }

    private List<Process> FilterProcesses(Process currentProcess)
    {
        return Process.GetProcesses()
            .Where(p => ShouldIncludeProcess(p, currentProcess))
            .ToList();
    }

    private bool ShouldIncludeProcess(Process p, Process currentProcess)
    {
        try
        {
            // 排除自己
            if (p.Id == currentProcess.Id) return false;

            // 排除系统进程和空闲进程
            if (p.Id <= 4) return false;

            // 过滤 Windows 服务和系统组件 (通常在 Session 0)
            if (p.SessionId == 0) return false;

            // 如果不显示所有进程，则只显示有窗口标题的（应用）
            if (!ShowAllProcesses)
            {
                return !string.IsNullOrEmpty(p.MainWindowTitle);
            }

            return true;
        }
        catch { return false; }
    }

    private AppInfo CreateAppInfo(Process p)
    {
        string exePath = string.Empty;
        string name = p.MainWindowTitle;
        AvaloniaBitmap? icon = null;
        bool isApp = !string.IsNullOrEmpty(p.MainWindowTitle);

        try
        {
            exePath = p.MainModule?.FileName ?? string.Empty;

            // 如果是后台进程或窗口标题为空，使用进程名
            if (string.IsNullOrEmpty(name))
            {
                name = p.ProcessName;
            }

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                icon = ExtractProcessIcon(exePath);
            }
        }
        catch { /* 访问拒绝 */ }

        var (mem, threadCount, handleCount) = GetProcessResourceUsage(p);

        return new AppInfo
        {
            Name = name,
            ProcessName = p.ProcessName,
            ProcessId = p.Id,
            ExecutablePath = exePath,
            IsRunning = true,
            Icon = icon,
            MemoryUsage = mem,
            MemoryUsageString = FormatBytes(mem),
            ThreadCount = threadCount,
            HandleCount = handleCount,
            Category = isApp ? "应用" : "后台进程",
            CategoryOrder = isApp ? 0 : 1
        };
    }

    private (long Memory, int ThreadCount, int HandleCount) GetProcessResourceUsage(Process p)
    {
        long mem = 0;
        int threadCount = 0;
        int handleCount = 0;

        try { mem = p.WorkingSet64; } catch { }
        try { threadCount = p.Threads.Count; } catch { }
        try { handleCount = p.HandleCount; } catch { }

        return (mem, threadCount, handleCount);
    }

    private AvaloniaBitmap? ExtractProcessIcon(string filePath)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;

            using var icon = SystemDrawingIcon.ExtractAssociatedIcon(filePath);
            if (icon == null) return null;

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            return new AvaloniaBitmap(stream);
        }
        catch { }
        return null;
    }

    [RelayCommand]
    private void KillProcess(AppInfo app)
    {
        if (app == null) return;

        try
        {
            using var process = Process.GetProcessById(app.ProcessId);
            process.Kill();
            NotificationService.Instance.ShowSuccess($"已结束进程: {app.Name}");
            // 不在 UI 端立即 Remove；下一次 RefreshApps 会自然清掉它
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"无法结束进程: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFileLocation(AppInfo app)
    {
        if (app == null) return;

        try
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath) || !File.Exists(app.ExecutablePath))
            {
                NotificationService.Instance.ShowWarning("当前进程没有可用的可执行文件路径");
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                var argument = $"/select,\"{app.ExecutablePath}\"";
                var startInfo = new ProcessStartInfo("explorer.exe", argument)
                {
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            else
            {
                var directory = Path.GetDirectoryName(app.ExecutablePath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
                else
                {
                    NotificationService.Instance.ShowWarning("无法打开文件所在目录");
                }
            }
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"无法打开文件位置: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CopyPath(AppInfo app)
    {
        if (app == null) return;

        try
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath))
            {
                NotificationService.Instance.ShowWarning("当前进程没有可用的路径");
                return;
            }

            var success = await NotificationService.Instance.TrySetClipboardTextAsync(app.ExecutablePath);
            if (success)
            {
                NotificationService.Instance.ShowSuccess("已复制路径到剪贴板");
            }
            else
            {
                NotificationService.Instance.ShowError("无法访问系统剪贴板");
            }
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"复制路径失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CopyProcessName(AppInfo app)
    {
        if (app == null) return;

        try
        {
            if (string.IsNullOrWhiteSpace(app.ProcessName))
            {
                NotificationService.Instance.ShowWarning("当前进程没有可用的名称");
                return;
            }

            var success = await NotificationService.Instance.TrySetClipboardTextAsync(app.ProcessName);
            if (success)
            {
                NotificationService.Instance.ShowSuccess("已复制进程名到剪贴板");
            }
            else
            {
                NotificationService.Instance.ShowError("无法访问系统剪贴板");
            }
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"复制进程名失败: {ex.Message}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopRefreshTimer();
        }
        base.Dispose(disposing);
    }
}
