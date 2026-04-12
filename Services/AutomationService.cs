using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class AutomationService
{
    private static AutomationService? _instance;
    public static AutomationService Instance => _instance ??= new AutomationService();

    private Timer? _timer;
    private bool _startupProcessed;

    public void Start()
    {
        _timer = new Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, 0);
        _timer?.Dispose();
        _timer = null;
    }

    public void ForceCheck()
    {
        EvaluateWorkflows();
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            EvaluateWorkflows();
        }
        catch { }
    }

    private void EvaluateWorkflows()
    {
        var settings = SettingsService.Automation;
        var now = DateTime.Now;
        var currentTime = now.TimeOfDay;

        if (settings.IsAutomationEnabled && settings.Workflows != null && settings.Workflows.Any())
        {
            foreach (var wf in settings.Workflows.Where(w => w.IsEnabled))
            {
                bool triggerMatched = false;
                foreach (var t in wf.Triggers)
                {
                    if (t.Type == "DailyTime" && t.Time.HasValue)
                    {
                        if (Math.Abs((currentTime - t.Time.Value).TotalSeconds) < 30) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "Interval" && t.IntervalMinutes.HasValue)
                    {
                        if (wf.LastTriggeredAt == null || (now - wf.LastTriggeredAt.Value).TotalMinutes >= t.IntervalMinutes.Value) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "OnStartup")
                    {
                        if (!_startupProcessed) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "ProcessRunning" && (!string.IsNullOrWhiteSpace(t.ProcessName) || !string.IsNullOrWhiteSpace(t.FilePath)))
                    {
                        var interval = Math.Clamp(t.CheckIntervalSeconds ?? 5, 1, 120);
                        if (t.LastCheckedAt.HasValue && (now - t.LastCheckedAt.Value).TotalSeconds < interval)
                        {
                            continue;
                        }
                        bool exists = false;
                        var name = string.IsNullOrWhiteSpace(t.ProcessName) ? null : Path.GetFileNameWithoutExtension(t.ProcessName).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            try { exists = System.Diagnostics.Process.GetProcesses().Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)); } catch { }
                        }
                        if (!exists && !string.IsNullOrWhiteSpace(t.FilePath))
                        {
                            var target = t.FilePath.Trim();
                            try { exists = System.Diagnostics.Process.GetProcesses().Any(p => { try { return string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase); } catch { return false; } }); } catch { }
                        }
                        t.LastCheckedAt = now;
                        if (exists) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "ProcessNotRunning" && (!string.IsNullOrWhiteSpace(t.ProcessName) || !string.IsNullOrWhiteSpace(t.FilePath)))
                    {
                        var interval = Math.Clamp(t.CheckIntervalSeconds ?? 5, 1, 120);
                        if (t.LastCheckedAt.HasValue && (now - t.LastCheckedAt.Value).TotalSeconds < interval)
                        {
                            continue;
                        }
                        bool exists = false;
                        var name = string.IsNullOrWhiteSpace(t.ProcessName) ? null : Path.GetFileNameWithoutExtension(t.ProcessName).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            try { exists = System.Diagnostics.Process.GetProcesses().Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)); } catch { }
                        }
                        if (!exists && !string.IsNullOrWhiteSpace(t.FilePath))
                        {
                            var target = t.FilePath.Trim();
                            try { exists = System.Diagnostics.Process.GetProcesses().Any(p => { try { return string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase); } catch { return false; } }); } catch { }
                        }
                        t.LastCheckedAt = now;
                        if (!exists) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "NetworkAvailable")
                    {
                        if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "NetworkUnavailable")
                    {
                        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "FileExists" && !string.IsNullOrWhiteSpace(t.FilePath))
                    {
                        if (File.Exists(t.FilePath)) { triggerMatched = true; break; }
                    }
                }

                bool conditionsOk = !wf.ConditionsEnabled ? true : true;
                if (wf.ConditionsEnabled)
                {
                    foreach (var c in wf.Conditions)
                    {
                        if (c.Type == "IsLocked" && c.Bool.HasValue)
                        {
                            bool locked = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
                            if (locked != c.Bool.Value) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "DayOfWeek" && c.Days != null && c.Days.Length > 0)
                        {
                            var d = now.DayOfWeek.ToString();
                            if (!c.Days.Contains(d)) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "TimeRange" && c.Start.HasValue && c.End.HasValue)
                        {
                            if (!(currentTime >= c.Start.Value && currentTime <= c.End.Value)) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "AppBlockingEnabled" && c.Bool.HasValue)
                        {
                            var enabled = SettingsService.Blockage.IsAppBlockingEnabled;
                            if (enabled != c.Bool.Value) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "NetworkLockEnabled" && c.Bool.HasValue)
                        {
                            var enabled = SettingsService.Blockage.IsNetworkLockEnabled;
                            if (enabled != c.Bool.Value) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "ProcessRunning" && (!string.IsNullOrWhiteSpace(c.ProcessName) || !string.IsNullOrWhiteSpace(c.FilePath)))
                        {
                            bool exists = false;
                            var name = string.IsNullOrWhiteSpace(c.ProcessName) ? null : Path.GetFileNameWithoutExtension(c.ProcessName).Trim();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                try { exists = System.Diagnostics.Process.GetProcesses().Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)); } catch { }
                            }
                            if (!exists && !string.IsNullOrWhiteSpace(c.FilePath))
                            {
                                var target = c.FilePath.Trim();
                                try { exists = System.Diagnostics.Process.GetProcesses().Any(p => { try { return string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase); } catch { return false; } }); } catch { }
                            }
                            if (!exists) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "FileExists" && !string.IsNullOrWhiteSpace(c.FilePath))
                        {
                            if (!File.Exists(c.FilePath)) { conditionsOk = false; break; }
                        }
                    }
                }

                var satisfiedNow = triggerMatched && conditionsOk;
                if (satisfiedNow && !wf.PreviouslySatisfied)
                {
                    LogService.Instance.Log("自动化", "触发工作流", "系统", $"工作流 [{wf.Name}] 已触发");
                    if (wf.Actions == null || wf.Actions.Count == 0)
                    {
                        NotificationService.Instance.ShowWarning("工作流已匹配，但未配置任何行动");
                    }
                    else
                    {
                        foreach (var a in wf.Actions)
                        {
                            ExecuteWorkflowAction(a);
                        }
                    }
                    wf.LastTriggeredAt = now;
                    wf.PreviouslySatisfied = true;
                    wf.TriggerCount++;
                    try { SettingsService.SaveAutomation(SettingsService.Automation); } catch { }
                }
                else if (!satisfiedNow && wf.RecoveryEnabled && wf.PreviouslySatisfied)
                {
                    LogService.Instance.Log("自动化", "执行恢复行动", "系统", $"工作流 [{wf.Name}] 已触发恢复");
                    foreach (var a in wf.RecoveryActions)
                    {
                        ExecuteWorkflowAction(a);
                    }
                    wf.PreviouslySatisfied = false;
                }
            }
            _startupProcessed = true;
        }

        if (settings.EnableAutoShutdown)
        {
            var shutdownTime = settings.AutoShutdownTime;
            if (Math.Abs((currentTime - shutdownTime).TotalSeconds) < 30)
            {
                ExecuteShutdown();
            }
        }

        if (settings.EnableAutoRestart)
        {
            var restartTime = settings.AutoRestartTime;
            if (Math.Abs((currentTime - restartTime).TotalSeconds) < 30)
            {
                ExecuteRestart();
            }
        }

        if (settings.EnableAutoLock)
        {
            var lockTime = settings.AutoLockTime;
            if (Math.Abs((currentTime - lockTime).TotalSeconds) < 30)
            {
                ExecuteLock();
            }
        }

        if (settings.EnableAutoNetworkLockOn)
        {
            var t = settings.AutoNetworkLockOnTime;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteNetworkLock(true);
            }
        }

        if (settings.EnableAutoNetworkLockOff)
        {
            var t = settings.AutoNetworkLockOffTime;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteNetworkLock(false);
            }
        }

        if (settings.EnableAutoAppBlockOn)
        {
            var t = settings.AutoAppBlockOnTime;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteAppBlocking(true);
            }
        }

        if (settings.EnableAutoAppBlockOff)
        {
            var t = settings.AutoAppBlockOffTime;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteAppBlocking(false);
            }
        }

        if (settings.EnableAutoWebcamCapture)
        {
            var t = settings.AutoWebcamCaptureTime;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteWebcamCapture();
            }
        }
    }

    private void ExecuteShutdown()
    {
        LogService.Instance.Log("自动化", "关机", "系统", "将在 60 秒后关机");
        NotificationService.Instance.ShowWarning("将在 60 秒后关机", true);
        System.Diagnostics.Process.Start("shutdown", "/s /t 60 /c \"ClassScreenLock: Scheduled shutdown in 60 seconds\"");
    }

    private void ExecuteRestart()
    {
        LogService.Instance.Log("自动化", "重启", "系统", "将在 60 秒后重启");
        NotificationService.Instance.ShowWarning("将在 60 秒后重启", true);
        System.Diagnostics.Process.Start("shutdown", "/r /t 60 /c \"ClassScreenLock: Scheduled restart in 60 seconds\"");
    }

    private void ExecuteLock()
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            LogService.Instance.Log("Warning", "Automation", "Lock", "Cannot lock: initialization required");
            return;
        }
        
        LogService.Instance.Log("Automation", "Lock", "Screen", "Activating full lock mode");
        LockScreenService.Instance.ActivateLock(LockMode.Full);
    }

    private async void ExecuteNetworkLock(bool enable)
    {
        LogService.Instance.Log("Automation", enable ? "NetworkLockOn" : "NetworkLockOff", "Network", "Toggling network lock");
        SettingsService.UpdateBlockage(s => { s.IsNetworkLockEnabled = enable; });
        await NetworkBlockingService.Instance.ApplyRulesAsync("Automation");
    }

    private void ExecuteAppBlocking(bool enable)
    {
        LogService.Instance.Log("Automation", enable ? "AppBlockOn" : "AppBlockOff", "AppBlocking", "Toggling app blocking");
        SettingsService.UpdateBlockage(s => { s.IsAppBlockingEnabled = enable; });
    }

    private void ExecuteWebcamCapture(AutomationAction? action = null)
    {
        var delay = Math.Clamp(action?.DelaySeconds ?? 0, 0, 60);
        Action doCapture = () =>
        {
            try
            {
                LogService.Instance.Log("自动化", "拍照", "摄像头", "开始拍照...");
                var s = SettingsService.Screenshot;
                var moniker = s.SelectedCameraMoniker;
                if (string.IsNullOrEmpty(moniker))
                {
                    try { moniker = WebcamService.Instance.GetAvailableCameras()?.FirstOrDefault() ?? string.Empty; } catch { moniker = string.Empty; }
                }
                if (string.IsNullOrEmpty(moniker))
                {
                    LogService.Instance.Log("自动化", "拍照失败", "摄像头", "未检测到可用摄像头");
                    return;
                }
                WebcamService.Instance.CaptureOnce(moniker);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("自动化", "拍照失败", "错误", ex.Message);
            }
        };
        if (delay > 0)
        {
            LogService.Observe(Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(delay)); doCapture(); }), "AutomationService.WebcamCapture");
        }
        else
        {
            LogService.Observe(Task.Run(() => doCapture()), "AutomationService.WebcamCapture");
        }
    }

    private void ExecuteScreenShot(AutomationAction? action = null)
    {
        var delay = Math.Clamp(action?.DelaySeconds ?? 0, 0, 60);
        Action doShot = () =>
        {
            try
            {
                LogService.Instance.Log("自动化", "截屏", "屏幕", "开始截屏...");
                ScreenshotService.Instance.CaptureOnce();
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("自动化", "截屏失败", "错误", ex.Message);
            }
        };
        if (delay > 0)
        {
            LogService.Observe(Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(delay)); doShot(); }), "AutomationService.ScreenShot");
        }
        else
        {
            LogService.Observe(Task.Run(() => doShot()), "AutomationService.ScreenShot");
        }
    }

    private void ExecuteOpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void ExecuteRunProcess(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { System.Diagnostics.Process.Start(path); } catch { }
    }

    private void ExecutePlaySound()
    {
        NotificationService.Instance.ShowInfo("提示");
    }

    private void ExecuteWorkflowAction(AutomationAction action)
    {
        var t = action.Type;
        if (t == "Shutdown") ExecuteShutdown();
        else if (t == "Restart") ExecuteRestart();
        else if (t == "LockFull") ExecuteLock();
        else if (t == "NetworkLockOn") ExecuteNetworkLock(true);
        else if (t == "NetworkLockOff") ExecuteNetworkLock(false);
        else if (t == "AppBlockOn") ExecuteAppBlocking(true);
        else if (t == "AppBlockOff") ExecuteAppBlocking(false);
        else if (t == "WebcamCapture") ExecuteWebcamCapture(action);
        else if (t == "Notify") NotificationService.Instance.ShowInfo(action.Text ?? string.Empty);
        else if (t == "OpenUrl") ExecuteOpenUrl(action.Text);
        else if (t == "RunProcess") ExecuteRunProcess(action.Text);
        else if (t == "PlaySound") ExecutePlaySound();
        else if (t == "ScreenShot") ExecuteScreenShot(action);
        else if (t == "BasicProtectionOn") SettingsService.UpdateBlockage(s => s.IsBasicProtectionEnabled = true);
        else if (t == "BasicProtectionOff") SettingsService.UpdateBlockage(s => s.IsBasicProtectionEnabled = false);
    }
}
