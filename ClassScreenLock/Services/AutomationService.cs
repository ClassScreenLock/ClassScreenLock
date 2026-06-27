using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Media;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class AutomationService
{
    private static AutomationService? _instance;
    public static AutomationService Instance => _instance ??= new AutomationService();

    private Timer? _timer;
    private bool _startupProcessed;
    private readonly object _lock = new();
    private bool _isEvaluating;
    private readonly Dictionary<string, DateTime> _lastBuiltInActionDates = new();
    private const int BuiltInActionCooldownSeconds = 60;

    public void Start()
    {
        _startupProcessed = false;
        _timer = new Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_timer != null)
            {
                var waitHandle = new ManualResetEvent(false);
                _timer.Dispose(waitHandle);
                waitHandle.WaitOne(TimeSpan.FromSeconds(5));
                _timer = null;
            }
        }
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
        catch (Exception ex)
        {
            LogService.Instance.Log("自动化", "错误", "TimerTick", ex.Message);
        }
    }

    private void EvaluateWorkflows()
    {
        lock (_lock)
        {
            if (_isEvaluating) return;
            _isEvaluating = true;
        }

        try
        {
            EvaluateWorkflowsInternal();
        }
        finally
        {
            lock (_lock)
            {
                _isEvaluating = false;
            }
        }
    }

    private void EvaluateWorkflowsInternal()
    {
        var settings = SettingsService.Automation;
        if (settings == null)
        {
            LogService.Instance.Log("自动化", "警告", "配置", "自动化设置为空");
            _startupProcessed = true;
            return;
        }

        var now = DateTime.Now;
        var currentTime = now.TimeOfDay;
        var currentScheme = settings.CurrentScheme ?? "Default";

        if (settings.IsAutomationEnabled && settings.Workflows != null && settings.Workflows.Any())
        {
            foreach (var workflow in settings.Workflows.Where(w => w.IsEnabled))
            {
                EvaluateWorkflow(workflow, currentScheme, now, currentTime);
            }
        }

        _startupProcessed = true;
        EvaluateBuiltInActions(settings, now, currentTime);
    }

    private void EvaluateWorkflow(AutomationWorkflow workflow, string currentScheme, DateTime now, TimeSpan currentTime)
    {
        if (!string.Equals(workflow.Scheme, currentScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var triggerMatched = EvaluateWorkflowTriggers(workflow.Triggers, now, currentTime);
        var conditionsOk = EvaluateWorkflowConditions(workflow, now, currentTime);
        HandleWorkflowStateChange(workflow, triggerMatched, conditionsOk, now);
    }

    private bool EvaluateWorkflowTriggers(System.Collections.ObjectModel.ObservableCollection<AutomationTrigger> triggers, DateTime now, TimeSpan currentTime)
    {
        foreach (var trigger in triggers)
        {
            if (EvaluateTrigger(trigger, now, currentTime))
            {
                return true;
            }
        }
        return false;
    }

    private bool EvaluateTrigger(AutomationTrigger trigger, DateTime now, TimeSpan currentTime)
    {
        return trigger.Type switch
        {
            "DailyTime" => EvaluateDailyTimeTrigger(trigger, currentTime),
            "Interval" => EvaluateIntervalTrigger(trigger, now),
            "OnStartup" => EvaluateOnStartupTrigger(),
            "ProcessRunning" => EvaluateProcessRunningTrigger(trigger, now),
            "ProcessNotRunning" => EvaluateProcessNotRunningTrigger(trigger, now),
            "NetworkAvailable" => EvaluateNetworkAvailableTrigger(trigger, now),
            "NetworkUnavailable" => EvaluateNetworkUnavailableTrigger(trigger, now),
            "FileExists" => EvaluateFileExistsTrigger(trigger, now),
            _ => false
        };
    }

    private bool EvaluateDailyTimeTrigger(AutomationTrigger trigger, TimeSpan currentTime)
    {
        if (!trigger.Time.HasValue)
        {
            return false;
        }

        return Math.Abs((currentTime - trigger.Time.Value).TotalSeconds) < 30;
    }

    private bool EvaluateIntervalTrigger(AutomationTrigger trigger, DateTime now)
    {
        if (!trigger.IntervalMinutes.HasValue)
        {
            return false;
        }

        var intervalMinutes = Math.Max(1, trigger.IntervalMinutes.Value);
        var lastTrigger = trigger.TriggerLastTriggeredAt;

        return lastTrigger == null || (now - lastTrigger.Value).TotalMinutes >= intervalMinutes;
    }

    private bool EvaluateOnStartupTrigger()
    {
        return !_startupProcessed;
    }

    private bool EvaluateProcessRunningTrigger(AutomationTrigger trigger, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(trigger.ProcessName) && string.IsNullOrWhiteSpace(trigger.FilePath))
        {
            return false;
        }

        if (!ShouldCheckTrigger(trigger, now))
        {
            return false;
        }

        trigger.LastCheckedAt = now;
        return CheckProcessExists(trigger.ProcessName, trigger.FilePath);
    }

    private bool EvaluateProcessNotRunningTrigger(AutomationTrigger trigger, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(trigger.ProcessName) && string.IsNullOrWhiteSpace(trigger.FilePath))
        {
            return false;
        }

        if (!ShouldCheckTrigger(trigger, now))
        {
            return false;
        }

        trigger.LastCheckedAt = now;
        return !CheckProcessExists(trigger.ProcessName, trigger.FilePath);
    }

    private bool EvaluateNetworkAvailableTrigger(AutomationTrigger trigger, DateTime now)
    {
        if (!ShouldCheckTrigger(trigger, now))
        {
            return false;
        }

        var networkAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        trigger.LastCheckedAt = now;

        if (networkAvailable && trigger.LastNetworkStatus != true)
        {
            trigger.LastNetworkStatus = true;
            return true;
        }

        trigger.LastNetworkStatus = networkAvailable;
        return false;
    }

    private bool EvaluateNetworkUnavailableTrigger(AutomationTrigger trigger, DateTime now)
    {
        if (!ShouldCheckTrigger(trigger, now))
        {
            return false;
        }

        var networkAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        trigger.LastCheckedAt = now;

        if (!networkAvailable && trigger.LastNetworkStatus != false)
        {
            trigger.LastNetworkStatus = false;
            return true;
        }

        trigger.LastNetworkStatus = networkAvailable;
        return false;
    }

    private bool EvaluateFileExistsTrigger(AutomationTrigger trigger, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(trigger.FilePath))
        {
            return false;
        }

        if (!ShouldCheckTrigger(trigger, now))
        {
            return false;
        }

        var exists = File.Exists(trigger.FilePath);
        trigger.LastCheckedAt = now;

        if (exists && trigger.LastFileExistsStatus != true)
        {
            trigger.LastFileExistsStatus = true;
            return true;
        }

        trigger.LastFileExistsStatus = exists;
        return false;
    }

    private bool ShouldCheckTrigger(AutomationTrigger trigger, DateTime now)
    {
        var interval = Math.Clamp(trigger.CheckIntervalSeconds ?? 5, 1, 120);
        return !trigger.LastCheckedAt.HasValue || (now - trigger.LastCheckedAt.Value).TotalSeconds >= interval;
    }

    private bool EvaluateWorkflowConditions(AutomationWorkflow workflow, DateTime now, TimeSpan currentTime)
    {
        if (!workflow.ConditionsEnabled)
        {
            return true;
        }

        foreach (var condition in workflow.Conditions)
        {
            if (!EvaluateCondition(condition, now, currentTime))
            {
                return false;
            }
        }

        return true;
    }

    private bool EvaluateCondition(AutomationCondition condition, DateTime now, TimeSpan currentTime)
    {
        return condition.Type switch
        {
            "IsLocked" => EvaluateIsLockedCondition(condition),
            "DayOfWeek" => EvaluateDayOfWeekCondition(condition, now),
            "TimeRange" => EvaluateTimeRangeCondition(condition, currentTime),
            "AppBlockingEnabled" => EvaluateAppBlockingEnabledCondition(condition),
            "NetworkLockEnabled" => EvaluateNetworkLockEnabledCondition(condition),
            "ProcessRunning" => EvaluateProcessRunningCondition(condition, now),
            "FileExists" => EvaluateFileExistsCondition(condition),
            _ => true
        };
    }

    private bool EvaluateIsLockedCondition(AutomationCondition condition)
    {
        if (!condition.Bool.HasValue)
        {
            return true;
        }

        var locked = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
        return locked == condition.Bool.Value;
    }

    private bool EvaluateDayOfWeekCondition(AutomationCondition condition, DateTime now)
    {
        if (condition.Days == null || condition.Days.Length == 0)
        {
            return true;
        }

        var currentDay = now.DayOfWeek.ToString();
        return condition.Days.Contains(currentDay);
    }

    private bool EvaluateTimeRangeCondition(AutomationCondition condition, TimeSpan currentTime)
    {
        if (!condition.Start.HasValue || !condition.End.HasValue)
        {
            return true;
        }

        return currentTime >= condition.Start.Value && currentTime <= condition.End.Value;
    }

    private bool EvaluateAppBlockingEnabledCondition(AutomationCondition condition)
    {
        if (!condition.Bool.HasValue)
        {
            return true;
        }

        return SettingsService.Blockage.IsAppBlockingEnabled == condition.Bool.Value;
    }

    private bool EvaluateNetworkLockEnabledCondition(AutomationCondition condition)
    {
        if (!condition.Bool.HasValue)
        {
            return true;
        }

        return SettingsService.Blockage.IsNetworkLockEnabled == condition.Bool.Value;
    }

    private bool EvaluateProcessRunningCondition(AutomationCondition condition, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(condition.ProcessName) && string.IsNullOrWhiteSpace(condition.FilePath))
        {
            return true;
        }

        var interval = Math.Clamp(condition.CheckIntervalSeconds ?? 5, 1, 120);
        if (condition.LastCheckedAt.HasValue && (now - condition.LastCheckedAt.Value).TotalSeconds < interval)
        {
            return true;
        }

        condition.LastCheckedAt = now;
        return CheckProcessExists(condition.ProcessName, condition.FilePath);
    }

    private bool EvaluateFileExistsCondition(AutomationCondition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.FilePath))
        {
            return true;
        }

        return File.Exists(condition.FilePath);
    }

    private void HandleWorkflowStateChange(AutomationWorkflow workflow, bool triggerMatched, bool conditionsOk, DateTime now)
    {
        var satisfiedNow = triggerMatched && conditionsOk;

        if (satisfiedNow && !workflow.PreviouslySatisfied)
        {
            ExecuteWorkflowActivation(workflow, now);
        }
        else if (!satisfiedNow && workflow.RecoveryEnabled && workflow.PreviouslySatisfied)
        {
            ExecuteWorkflowRecovery(workflow);
        }
    }

    private void ExecuteWorkflowActivation(AutomationWorkflow workflow, DateTime now)
    {
        LogService.Instance.Log("自动化", "触发工作流", "系统", $"工作流 [{workflow.Name}] 已触发");

        if (workflow.Actions == null || workflow.Actions.Count == 0)
        {
            NotificationService.Instance.ShowWarning("工作流已匹配，但未配置任何行动");
        }
        else
        {
            ExecuteWorkflowActions(workflow.Actions);
        }

        workflow.LastTriggeredAt = now;
        workflow.PreviouslySatisfied = true;
        workflow.TriggerCount++;

        foreach (var trigger in workflow.Triggers)
        {
            trigger.TriggerLastTriggeredAt = now;
        }

        SaveAutomationSettings();
    }

    private void ExecuteWorkflowRecovery(AutomationWorkflow workflow)
    {
        LogService.Instance.Log("自动化", "执行恢复行动", "系统", $"工作流 [{workflow.Name}] 已触发恢复");
        ExecuteWorkflowActions(workflow.RecoveryActions);
        workflow.PreviouslySatisfied = false;

        // 立即保存，确保状态持久化
        SaveAutomationSettings();
    }

    private void ExecuteWorkflowActions(System.Collections.ObjectModel.ObservableCollection<AutomationAction> actions)
    {
        if (actions == null)
        {
            return;
        }

        foreach (var action in actions)
        {
            ExecuteWorkflowAction(action);
        }
    }

    private void SaveAutomationSettings()
    {
        try
        {
            SettingsService.SaveAutomation(SettingsService.Automation);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("自动化", "保存失败", "错误", ex.Message);
        }
    }

    /// <summary>
    /// 重置所有工作流的状态（用于数据恢复后重新评估）
    /// </summary>
    public void ResetWorkflowStates()
    {
        var settings = SettingsService.Automation;
        if (settings?.Workflows != null)
        {
            foreach (var workflow in settings.Workflows)
            {
                // 重置 PreviouslySatisfied，让工作流重新评估
                workflow.PreviouslySatisfied = false;
            }
            SettingsService.SaveAutomation(settings);
            LogService.Instance.Log("自动化", "状态重置", "系统", "工作流状态已重置");
        }
    }

    private void EvaluateBuiltInActions(AutomationSettingsModel settings, DateTime now, TimeSpan currentTime)
    {
        if (settings.EnableAutoShutdown)
        {
            TryExecuteBuiltInAction("AutoShutdown", now, currentTime, settings.AutoShutdownTime, ExecuteShutdown);
        }

        if (settings.EnableAutoRestart)
        {
            TryExecuteBuiltInAction("AutoRestart", now, currentTime, settings.AutoRestartTime, ExecuteRestart);
        }

        if (settings.EnableAutoLock)
        {
            TryExecuteBuiltInAction("AutoLock", now, currentTime, settings.AutoLockTime, ExecuteLock);
        }

        if (settings.EnableAutoNetworkLockOn)
        {
            TryExecuteBuiltInAction("AutoNetworkLockOn", now, currentTime, settings.AutoNetworkLockOnTime, () => ExecuteNetworkLockAsync(true).Wait());
        }

        if (settings.EnableAutoNetworkLockOff)
        {
            TryExecuteBuiltInAction("AutoNetworkLockOff", now, currentTime, settings.AutoNetworkLockOffTime, () => ExecuteNetworkLockAsync(false).Wait());
        }

        if (settings.EnableAutoAppBlockOn)
        {
            TryExecuteBuiltInAction("AutoAppBlockOn", now, currentTime, settings.AutoAppBlockOnTime, () => ExecuteAppBlocking(true));
        }

        if (settings.EnableAutoAppBlockOff)
        {
            TryExecuteBuiltInAction("AutoAppBlockOff", now, currentTime, settings.AutoAppBlockOffTime, () => ExecuteAppBlocking(false));
        }

        if (settings.EnableAutoWebcamCapture)
        {
            TryExecuteBuiltInAction("AutoWebcamCapture", now, currentTime, settings.AutoWebcamCaptureTime, () => ExecuteWebcamCapture(null));
        }
    }

    private void TryExecuteBuiltInAction(string actionKey, DateTime now, TimeSpan currentTime, TimeSpan targetTime, Action action)
    {
        if (Math.Abs((currentTime - targetTime).TotalSeconds) < 30)
        {
            var todayKey = $"{actionKey}_{now.Date:yyyyMMdd}";
            if (!_lastBuiltInActionDates.TryGetValue(todayKey, out var lastTriggered) ||
                (now - lastTriggered).TotalSeconds >= BuiltInActionCooldownSeconds)
            {
                _lastBuiltInActionDates[todayKey] = now;
                CleanupOldBuiltInActionDates(now);
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log("自动化", actionKey, "错误", ex.Message);
                }
            }
        }
    }

    private void CleanupOldBuiltInActionDates(DateTime now)
    {
        var cutoffDate = now.Date.AddDays(-1).ToString("yyyyMMdd");
        var keysToRemove = _lastBuiltInActionDates.Keys
            .Where(k => !k.EndsWith(now.Date.ToString("yyyyMMdd")) && !k.EndsWith(cutoffDate))
            .ToList();
        foreach (var key in keysToRemove)
        {
            _lastBuiltInActionDates.Remove(key);
        }
    }

    private bool CheckProcessExists(string? processName, string? filePath)
    {
        bool exists = false;

        if (!string.IsNullOrWhiteSpace(processName))
        {
            var name = processName.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }
            try
            {
                exists = System.Diagnostics.Process.GetProcesses()
                    .Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { }
        }

        if (!exists && !string.IsNullOrWhiteSpace(filePath))
        {
            var target = filePath.Trim();
            try
            {
                exists = System.Diagnostics.Process.GetProcesses()
                    .Any(p =>
                    {
                        try
                        {
                            return string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    });
            }
            catch { }
        }

        return exists;
    }

    private void ExecuteShutdown()
    {
        LogService.Instance.Log("自动化", "关机", "系统", "将在 60 秒后关机");
        NotificationService.Instance.ShowWarning("将在 60 秒后关机，可运行 'shutdown /a' 取消", true);
        System.Diagnostics.Process.Start("shutdown", "/s /t 60 /c \"ClassScreenLock: Scheduled shutdown in 60 seconds. Run 'shutdown /a' to cancel.\"");
    }

    private void ExecuteRestart()
    {
        LogService.Instance.Log("自动化", "重启", "系统", "将在 60 秒后重启");
        NotificationService.Instance.ShowWarning("将在 60 秒后重启，可运行 'shutdown /a' 取消", true);
        System.Diagnostics.Process.Start("shutdown", "/r /t 60 /c \"ClassScreenLock: Scheduled restart in 60 seconds. Run 'shutdown /a' to cancel.\"");
    }

    private void ExecuteLock()
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            LogService.Instance.Log("警告", "自动化", "锁定", "无法锁定：需要初始化");
            return;
        }

        LogService.Instance.Log("自动化", "锁定", "屏幕", "激活全屏锁定模式");
        LockScreenService.Instance.ActivateLock(LockMode.Full);
    }

    private async Task ExecuteNetworkLockAsync(bool enable)
    {
        try
        {
            LogService.Instance.Log("自动化", enable ? "网络锁定开启" : "网络锁定关闭", "网络", "切换网络锁定");
            SettingsService.UpdateBlockage(s => { s.IsNetworkLockEnabled = enable; });
            await NetworkBlockingService.Instance.ApplyRulesAsync("Automation");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("自动化", "网络锁定错误", "错误", ex.Message);
        }
    }

    private void ExecuteAppBlocking(bool enable)
    {
        LogService.Instance.Log("自动化", enable ? "应用拦截开启" : "应用拦截关闭", "应用拦截", "切换应用拦截");
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
            LogService.Observe(Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(delay));
                doCapture();
            }), "AutomationService.WebcamCapture");
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
            LogService.Observe(Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(delay));
                doShot();
            }), "AutomationService.ScreenShot");
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
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
            else
            {
                LogService.Instance.Log("自动化", "打开URL", "警告", $"无效的URL: {url}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("自动化", "打开URL失败", "错误", ex.Message);
        }
    }

    private void ExecuteRunProcess(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (!File.Exists(path))
            {
                LogService.Instance.Log("自动化", "运行进程", "警告", $"文件不存在: {path}");
                return;
            }
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".exe" && extension != ".bat" && extension != ".cmd" && extension != ".lnk")
            {
                LogService.Instance.Log("自动化", "运行进程", "警告", $"不支持的文件类型: {extension}");
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("自动化", "运行进程失败", "错误", ex.Message);
        }
    }

    private void ExecutePlaySound()
    {
        try
        {
            SystemSounds.Beep.Play();
            LogService.Instance.Log("自动化", "播放声音", "系统", "已播放系统提示音");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("自动化", "播放声音失败", "错误", ex.Message);
            NotificationService.Instance.ShowInfo("提示");
        }
    }

    private void ExecuteWorkflowAction(AutomationAction action)
    {
        switch (action.Type)
        {
            case "Shutdown":
                ExecuteShutdown();
                break;
            case "Restart":
                ExecuteRestart();
                break;
            case "LockFull":
                ExecuteLock();
                break;
            case "NetworkLockOn":
                _ = ExecuteNetworkLockAsync(true);
                break;
            case "NetworkLockOff":
                _ = ExecuteNetworkLockAsync(false);
                break;
            case "AppBlockOn":
                ExecuteAppBlocking(true);
                break;
            case "AppBlockOff":
                ExecuteAppBlocking(false);
                break;
            case "WebcamCapture":
                ExecuteWebcamCapture(action);
                break;
            case "Notify":
                NotificationService.Instance.ShowInfo(action.Text ?? string.Empty);
                break;
            case "OpenUrl":
                ExecuteOpenUrl(action.Text);
                break;
            case "RunProcess":
                ExecuteRunProcess(action.Text);
                break;
            case "PlaySound":
                ExecutePlaySound();
                break;
            case "ScreenShot":
                ExecuteScreenShot(action);
                break;
            case "BasicProtectionOn":
                SettingsService.UpdateBlockage(s => s.IsBasicProtectionEnabled = true);
                break;
            case "BasicProtectionOff":
                SettingsService.UpdateBlockage(s => s.IsBasicProtectionEnabled = false);
                break;
        }
    }
}
