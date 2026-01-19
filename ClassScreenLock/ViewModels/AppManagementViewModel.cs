using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

using System.Text.Json;
using System.Collections.Concurrent;

namespace ClassScreenLock.ViewModels;

public partial class AppManagementViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<AppInfo> _runningApps = new();

    [ObservableProperty]
    private ObservableCollection<string> _allowedApps = new();

    [ObservableProperty]
    private ObservableCollection<string> _blockedRules = new();

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

    [ObservableProperty]
    private ObservableCollection<ProtectionRule> _protectionRules = new();

    [ObservableProperty]
    private int _selectedSubTabIndex = 0; // 0: 运行中, 1: 允许列表, 2: 阻止列表, 3: 基础防护

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
    private DateTime _lastMonitorCpuSampleTime = DateTime.MinValue;
    private TimeSpan _lastMonitorCpuTotalProcessorTime = TimeSpan.Zero;
    private double _lastMonitorCpuUsage;

    private const double MonitorCpuThreshold = 20.0;
    private const double MonitorCpuCriticalThreshold = 40.0;
    private const double MonitorIntervalFastSeconds = 1.0;
    private const double MonitorIntervalSlowSeconds = 2.0;

    private readonly ConcurrentDictionary<int, (long Read, long Write, DateTime Stamp)> _ioRateSamples = new();

    public AppManagementViewModel()
    {
        LoadSettings();
        RefreshAppsCommand.Execute(null);
        // 不再在构造函数中自动启动，由 MainWindowViewModel 控制
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

        _ = OnRefreshTick();
    }

    public void StopRefreshTimer(string reason = "已停止")
    {
        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer = null;
        }
        _ioRateSamples.Clear();
    }

    private async Task OnRefreshTick()
    {
        AdjustMonitorIntervalAndCheckCpu();
        await UpdateProcessStats();
    }

    private void AdjustMonitorIntervalAndCheckCpu()
    {
        try
        {
            lock (_monitorLock)
            {
                var process = Process.GetCurrentProcess();
                var now = DateTime.Now;
                var currentTotal = process.TotalProcessorTime;

                if (_lastMonitorCpuSampleTime != DateTime.MinValue)
                {
                    var wallElapsed = now - _lastMonitorCpuSampleTime;
                    if (wallElapsed.TotalMilliseconds > 0)
                    {
                        var cpuDelta = (currentTotal - _lastMonitorCpuTotalProcessorTime).TotalMilliseconds;
                        var totalDelta = wallElapsed.TotalMilliseconds * Environment.ProcessorCount;
                        var usage = Math.Min(100, Math.Max(0, (cpuDelta / totalDelta) * 100));

                        _lastMonitorCpuUsage = usage;

                        if (_refreshTimer != null)
                        {
                            if (usage > MonitorCpuThreshold)
                            {
                                _refreshTimer.Interval = TimeSpan.FromSeconds(MonitorIntervalSlowSeconds);
                            }
                            else
                            {
                                _refreshTimer.Interval = TimeSpan.FromSeconds(MonitorIntervalFastSeconds);
                            }
                        }

                        if (usage > MonitorCpuCriticalThreshold)
                        {
                            ReleaseMonitoringResources($"性能保护: CPU {usage:F0}%");
                            LogService.Instance.Log("Monitoring", "ForceRelease", "AppManagementViewModel", $"CPU {usage:F1}% > 临界阈值 {MonitorCpuCriticalThreshold}%");
                        }
                    }
                }

                _lastMonitorCpuSampleTime = now;
                _lastMonitorCpuTotalProcessorTime = currentTotal;
            }
        }
        catch
        {
        }
    }

    private void ReleaseMonitoringResources(string reason = "性能保护")
    {
        try
        {
            _ioRateSamples.Clear();
        }
        catch
        {
        }
    }

    private async Task UpdateProcessStats()
    {
        // 快速获取当前 UI 显示的进程引用，避免长时间锁定
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
                try
                {
                    // 内存、句柄等轻量级信息直接从系统 API 获取
                    try
                    {
                        using var process = Process.GetProcessById(app.ProcessId);
                        if (process.HasExited) continue;

                        if (TryGetProcessIoRate(process, out var rate))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                app.NetworkSpeed = $"{FormatBytes((long)rate)}/s";
                            });
                        }

                        long mem = process.WorkingSet64;
                        int tc = process.Threads.Count;
                        int hc = process.HandleCount;

                        Dispatcher.UIThread.Post(() => {
                            app.MemoryUsage = mem;
                            app.MemoryUsageString = FormatBytes(mem);
                            app.ThreadCount = tc;
                            app.HandleCount = hc;
                        });

                        if (TryGetProcessIoTotal(process, out long ioTotal))
                        {
                            string ioTotalStr = FormatBytes(ioTotal);
                            Dispatcher.UIThread.Post(() => {
                                app.TotalNetworkUsage = ioTotalStr;
                            });
                        }
                    }
                    catch { /* 进程可能已关闭或无权访问 */ }
                }
                catch { }
            }
        });
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
        AllowedApps = new ObservableCollection<string>(settings.AllowedApps ?? new List<string>());
        IsBasicProtectionEnabled = settings.IsBasicProtectionEnabled;
        IsAppBlockingEnabled = settings.IsAppBlockingEnabled;
        
        // 初始化统一规则列表
        var rules = new List<string>();
        if (settings.BlockedRules != null && settings.BlockedRules.Any())
        {
            rules.AddRange(settings.BlockedRules);
        }
        
        BlockedRules = new ObservableCollection<string>(rules);

        // 初始化基础防护规则
        if (settings.ProtectionRules == null || !settings.ProtectionRules.Any() || settings.ProtectionRules.Any(r => string.IsNullOrEmpty(r.Name)))
        {
            var defaultRules = GetDefaultProtectionRules();
            // 如果已有规则但名称为空，尝试保留原有的启用状态
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

    private void SaveSettings()
    {
        SettingsService.UpdateBlockage(settings =>
        {
            settings.AllowedApps = AllowedApps.ToList();
            settings.BlockedRules = BlockedRules.ToList();
            settings.IsBasicProtectionEnabled = IsBasicProtectionEnabled;
            settings.IsAppBlockingEnabled = IsAppBlockingEnabled;
            settings.ProtectionRules = ProtectionRules.ToList();
        });
    }

    [RelayCommand]
    private void ToggleAppBlocking()
    {
        // IsAppBlockingEnabled 已经通过绑定自动更新了，所以我们只需要保存
        SaveSettings();
        NotificationService.Instance.ShowSuccess(IsAppBlockingEnabled ? "阻止列表已启用" : "阻止列表已禁用");
    }

    [RelayCommand]
    private async Task ToggleGlobalProtection()
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }
        // IsBasicProtectionEnabled 已经通过绑定自动更新了
        if (IsBasicProtectionEnabled)
        {
            // 启用前执行备份
            var backupSuccess = await ProtectionBackupService.Instance.CreateBackupAsync();
            if (!backupSuccess)
            {
                NotificationService.Instance.ShowWarning("基础防护：备份创建失败，请检查权限或日志");
            }

            // 只要开启了基础防护，就强制开启所有子项
            foreach (var rule in ProtectionRules)
            {
                rule.IsEnabled = true;
            }
        }
        else
        {
            // 解除时执行恢复程序
            NotificationService.Instance.ShowInfo("正在解除基础防护并恢复系统状态...");
            var restoreSuccess = await ProtectionBackupService.Instance.RestoreBackupAsync();
            if (restoreSuccess)
            {
                NotificationService.Instance.ShowSuccess("基础防护解除成功，系统状态已恢复");
                // 强制刷新应用程序界面
                LoadSettings();
                RefreshAppsCommand.Execute(null);
            }
            else
            {
                NotificationService.Instance.ShowError("基础防护解除失败：无法完整恢复备份文件，请查阅错误日志");
            }
        }
        SaveSettings();
        NotificationService.Instance.ShowSuccess(IsBasicProtectionEnabled ? "基础防护已开启 (所有子项已强制开启)" : "基础防护已关闭");
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
            LoadSettings();
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
        if (rule != null)
        {
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
    }

    [RelayCommand]
    private void AddToAllowed(AppInfo app)
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }

        if (app != null && !AllowedApps.Contains(app.ProcessName))
        {
            AllowedApps.Add(app.ProcessName);
            BlockedRules.Remove(app.ProcessName);
            if (!string.IsNullOrEmpty(app.ExecutablePath))
            {
                BlockedRules.Remove(app.ExecutablePath);
            }
            SaveSettings();
            NotificationService.Instance.ShowSuccess($"已添加 {app.ProcessName} 到允许列表");
        }
    }

    [RelayCommand]
    private void AddToBlocked(AppInfo app)
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }

        if (app != null && !BlockedRules.Contains(app.ProcessName))
        {
            BlockedRules.Add(app.ProcessName);
            AllowedApps.Remove(app.ProcessName);
            SaveSettings();
            NotificationService.Instance.ShowSuccess($"已添加 {app.ProcessName} 到阻止列表");
        }
    }

    [RelayCommand]
    private void AddPathToBlocked(string path)
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }
        if (!string.IsNullOrWhiteSpace(path) && !BlockedRules.Contains(path))
        {
            BlockedRules.Add(path);
            SaveSettings();
            NotificationService.Instance.ShowSuccess($"已添加路径到阻止列表");
        }
    }

    [RelayCommand]
    private void RemoveFromAllowed(string processName)
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }
        if (AllowedApps.Remove(processName))
        {
            SaveSettings();
        }
    }

    [RelayCommand]
    private void RemoveFromBlocked(string rule)
    {
        var required = SettingsService.Lock.SidebarAppManagementMinAccountType;
        if (required != null && !(SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value)))
        {
            NotificationService.Instance.ShowWarning("权限不足：访问应用管理需要更高权限");
            return;
        }
        if (BlockedRules.Remove(rule))
        {
            SaveSettings();
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

        // 同步 RunningApps 集合，而不是创建新集合，以保留 DataGrid 的排序状态
        SyncObservableCollection(RunningApps, filteredList);
        UpdateHasRunningApps();
    }

    private void SyncObservableCollection(ObservableCollection<AppInfo> target, List<AppInfo> source)
    {
        // 移除不再存在的项
        var toRemove = target.Where(t => source.All(s => s.ProcessId != t.ProcessId)).ToList();
        foreach (var item in toRemove) target.Remove(item);

        // 添加新项并更新现有项位置
        for (int i = 0; i < source.Count; i++)
        {
            var sourceItem = source[i];
            var targetIndex = -1;
            for (int j = 0; j < target.Count; j++)
            {
                if (target[j].ProcessId == sourceItem.ProcessId)
                {
                    targetIndex = j;
                    break;
                }
            }

            if (targetIndex == -1)
            {
                // 新项，插入到正确位置
                if (i < target.Count) target.Insert(i, sourceItem);
                else target.Add(sourceItem);
            }
            // 如果已经在集合中，DataGrid 会处理排序，我们不需要在这里强制移动位置
            // 否则会干扰用户的手动排序
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
            var processes = Process.GetProcesses()
                .Where(p => 
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
                })
                .ToList();

            var appInfos = processes.Select(p =>
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
                        icon = ExtractIcon(exePath);
                    }
                }
                catch { /* 访问拒绝 */ }

                long mem = 0;
                int threadCount = 0;
                int handleCount = 0;
                try { mem = p.WorkingSet64; } catch { }
                try { threadCount = p.Threads.Count; } catch { }
                try { handleCount = p.HandleCount; } catch { }

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
            })
            .OrderBy(p => p.CategoryOrder) // 先按类别排序（应用在前）
            .ThenBy(p => p.Name)           // 再按名称排序
            .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allRunningApps = appInfos;
                FilterApps();
                IsRefreshing = false;
            });
        });
    }

    private AvaloniaBitmap? ExtractIcon(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using (var icon = SystemDrawingIcon.ExtractAssociatedIcon(filePath))
                {
                    if (icon == null) return null;
                    using (var bitmap = icon.ToBitmap())
                    {
                        using (var stream = new MemoryStream())
                        {
                            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            stream.Position = 0;
                            return new AvaloniaBitmap(stream);
                        }
                    }
                }
            }
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
            var process = Process.GetProcessById(app.ProcessId);
            process.Kill();
            RunningApps.Remove(app);
            NotificationService.Instance.ShowSuccess($"已结束进程: {app.Name}");
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
            ReleaseMonitoringResources();
        }
        base.Dispose(disposing);
    }
}
