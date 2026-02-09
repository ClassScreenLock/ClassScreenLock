using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    private AppBlockingService() { }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();
        LogService.Observe(Task.Run(() => MonitorLoop(_cts.Token)), "AppBlocking.MonitorLoop");
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
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
                    // 跳过空进程和系统进程 (PID <= 4)
                    if (process.Id <= 4) continue;

                    string name = process.ProcessName;
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
