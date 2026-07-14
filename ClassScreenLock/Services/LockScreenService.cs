using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Media;
using ClassScreenLock.Models;
using ClassScreenLock.Views;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Helpers;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassScreenLock.Services;

public class LockScreenService : INotifyPropertyChanged
{
    private static readonly LockScreenService _instance = new();
    public static LockScreenService Instance => _instance;

    private static readonly string LockStateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "ClassScreenLock");
    private static readonly string LockStateFile = Path.Combine(LockStateDirectory, "lock_state.dat");

    private LockMode _currentMode = LockMode.ProtectionOnly;
    private bool _isLocked;
    public bool IsLocked 
    { 
        get => _isLocked;
        private set
        {
            if (_isLocked != value)
            {
                _isLocked = value;
                OnPropertyChanged();
            }
        }
    }
    private bool _isProtectionOnlyActive;
    public bool IsProtectionOnlyActive 
    { 
        get => _isProtectionOnlyActive;
        private set
        {
            if (_isProtectionOnlyActive != value)
            {
                _isProtectionOnlyActive = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    private Window? _lockWindow;
    private Window? _protectionWindow;
    private Timer? _topmostTimer;
    private Timer? _scheduleTimer;
    private Timer? _lockStateCheckTimer;
    private Timer? _maxLockDurationTimer;
    private bool _isManualLock;
    private string? _lastAutoLockedBreakId;
    private bool _wasManuallyUnlockedInBreak;
    private (TimePoint? current, TimePoint? next) _lastLockScheduleSnapshot;
    private bool _isRestoringFromStateFile = false;
#pragma warning disable CS0414 // 字段已被赋值但从未使用过它的值
    private bool _initializationCompleted = false;
#pragma warning restore CS0414 // 字段已被赋值但从未使用过它的值
    private DateTime? _lockStartTime;
    private FileStream? _lockStateFileStream;
    private bool? _previousNetworkLockState;

    public LockScreenService()
    {
        _scheduleTimer = new Timer(_ => CheckSchedule(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
    }

    public void StartLockStateFileCheck()
    {
        var settings = SettingsService.Lock;
        if (!settings.EnableLockStateFileCheck)
        {
            return;
        }

        var interval = Math.Max(1, settings.LockStateFileCheckIntervalSeconds) * 1000;
        _lockStateCheckTimer?.Dispose();

        // 延迟启动定时器，确保 RestoreLockStateOnStartup 先执行
        var initialDelay = _initializationCompleted ? 0 : 2000;
        _lockStateCheckTimer = new Timer(_ => CheckLockStateFile(), null, initialDelay, interval);
        LogService.Instance.Log("Info", "LockState", "Check", $"已启动锁屏状态文件检查，间隔: {settings.LockStateFileCheckIntervalSeconds} 秒");
    }

    /// <summary>
    /// 注册远程锁屏/解锁的 WebSocket 事件（由 App 启动时调用）。
    /// </summary>
    public void InitializeRemoteControl()
    {
        WebSocketService.Instance.OnRemoteLock += () =>
        {
            LogService.Instance.Log("Info", "LockScreen", "Remote", "集控端触发远程锁屏");
            ActivateLock(LockMode.Full, false);
        };
        WebSocketService.Instance.OnRemoteUnlock += () =>
        {
            LogService.Instance.Log("Info", "LockScreen", "Remote", "集控端触发远程解锁");
            DeactivateLock();
        };

        // WebSocket 连接后延迟发送当前锁屏状态
        Task.Run(async () =>
        {
            await Task.Delay(3000);
            _ = WebSocketService.Instance.SendLockStateAsync(IsLocked);
        });

        LogService.Instance.Log("Info", "LockScreen", "Remote", "远程锁屏控制已注册");
    }

    public void RestoreLockStateOnStartup()
    {
        // 在后台线程执行文件读取和状态检查，避免阻塞UI线程
        _ = Task.Run(async () =>
        {
            _isRestoringFromStateFile = true;

            try
            {
                if (CannotRestoreLockState())
                {
                    _initializationCompleted = true;
                    return;
                }

                var lockStateData = await LoadSavedLockStateAsync();
                if (!ValidateLockState(lockStateData))
                {
                    _initializationCompleted = true;
                    return;
                }

                ApplyLockState(lockStateData!);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "LockState", "Startup", $"检查锁屏状态文件失败: {ex.Message}");
                _initializationCompleted = true;
            }
        });
    }

    private bool CannotRestoreLockState()
    {
        return IsLocked || IsProtectionOnlyActive || _isRestoringFromStateFile;
    }

    private LockStateData? LoadSavedLockState()
    {
        return GetLockStateData();
    }

    private async Task<LockStateData?> LoadSavedLockStateAsync()
    {
        return await GetLockStateDataAsync();
    }

    private bool ValidateLockState(LockStateData? lockStateData)
    {
        return lockStateData != null && lockStateData.IsLocked;
    }

    private void ApplyLockState(LockStateData lockStateData)
    {
        LogService.Instance.Log("Info", "LockState", "Startup", "检测到锁屏状态文件，正在恢复锁定状态...");
        _isRestoringFromStateFile = true;
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                ExecuteLockStateRestore(lockStateData);
                ShowRestoreNotification(lockStateData);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "LockState", "Startup", $"恢复锁定状态失败: {ex.Message}");
            }
            finally
            {
                _isRestoringFromStateFile = false;
            }
        });
    }

    private void ExecuteLockStateRestore(LockStateData lockStateData)
    {
        ActivateLock(lockStateData.LockMode, false);
        LogService.Instance.Log("Info", "LockState", "Startup", "已根据状态文件恢复锁定状态");
    }

    private void ShowRestoreNotification(LockStateData lockStateData)
    {
        if (lockStateData.LockMode == LockMode.ProtectionOnly)
        {
            NotificationService.Instance.ShowInfo("已从上次锁定状态恢复仅防护模式");
            return;
        }

        ShowFullLockRestoreNotification();
    }

    private void ShowFullLockRestoreNotification()
    {
        var settings = SettingsService.Lock;
        var (nextClassPoint, nextClassDateTime) = ScheduleService.Instance.GetNextClassPoint();
        
        if (nextClassDateTime == null)
        {
            NotificationService.Instance.ShowInfo("已从上次锁定状态恢复");
            return;
        }

        var unlockTime = nextClassDateTime.Value.Subtract(TimeSpan.FromMinutes(settings.AutoUnlockBeforeClassMinutes));
        var remaining = unlockTime - DateTime.Now;
        
        var message = GetRestoreNotificationMessage(remaining);
        NotificationService.Instance.ShowInfo(message);
    }

    private string GetRestoreNotificationMessage(TimeSpan remaining)
    {
        if (remaining.TotalMinutes > 0)
        {
            var minutes = (int)remaining.TotalMinutes;
            return $"已从上次锁定状态恢复，将在 {minutes} 分钟后自动解锁";
        }
        
        return "已从上次锁定状态恢复，即将自动解锁";
    }

    public void StopLockStateFileCheck()
    {
        _lockStateCheckTimer?.Dispose();
        _lockStateCheckTimer = null;
    }

    public void Stop()
    {
        _scheduleTimer?.Dispose();
        _scheduleTimer = null;
        
        _topmostTimer?.Dispose();
        _topmostTimer = null;
        
        StopLockStateFileCheck();
    }

    private void CheckLockStateFile()
    {
        if (IsLocked || IsProtectionOnlyActive || _isRestoringFromStateFile)
        {
            return;
        }

        try
        {
            var lockStateData = GetLockStateData();
            if (lockStateData != null && lockStateData.IsLocked)
            {
                LogService.Instance.Log("Info", "LockState", "Check", "检测到锁屏状态文件，正在触发锁屏...");
                _isRestoringFromStateFile = true;
                
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        ActivateLock(lockStateData.LockMode, false);
                        LogService.Instance.Log("Info", "LockState", "Check", "已根据状态文件触发锁屏");
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Error", "LockState", "Check", $"触发锁屏失败: {ex.Message}");
                    }
                    finally
                    {
                        _isRestoringFromStateFile = false;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "LockState", "Check", $"检查锁屏状态文件失败: {ex.Message}");
        }
    }

    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private LowLevelHookProc? _mouseProc;
    private LowLevelHookProc? _keyboardProc;

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_DELETE = 0x2E;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private void CheckSchedule()
    {
        var (current, next) = GetCurrentSchedule();
        HandleBreakWidgetVisibility(current);
        
        if (IsLocked || IsProtectionOnlyActive)
        {
            HandleScheduleDeactivation(current, next);
        }
    }

    private (TimePoint? current, TimePoint? next) GetCurrentSchedule()
    {
        var currentTime = DateTime.Now.TimeOfDay;
        return ScheduleService.Instance.GetCurrentAndNextTimePoint(currentTime);
    }

    private void HandleBreakWidgetVisibility(TimePoint? current)
    {
        var settings = SettingsService.Lock;
        bool shouldShowWidget = EvaluateShouldShowWidget(settings, current);
        
        if (shouldShowWidget)
        {
            ShowBreakWidget();
        }
        else
        {
            HideBreakWidget(current);
        }
    }

    private bool EvaluateShouldShowWidget(LockSettingsModel settings, TimePoint? current)
    {
        return settings.EnableBreakTimeLock 
               && !IsLocked 
               && !IsProtectionOnlyActive 
               && current != null 
               && current.Type == TimePointType.Break 
               && !_wasManuallyUnlockedInBreak;
    }

    private void ShowBreakWidget()
    {
        if (_lastAutoLockedBreakId != "__BREAK_WIDGET_ON__")
        {
            FloatingWidgetService.Instance.ShowWidget();
            _lastAutoLockedBreakId = "__BREAK_WIDGET_ON__";
        }
    }

    private void HideBreakWidget(TimePoint? current)
    {
        if (_lastAutoLockedBreakId != "__BREAK_WIDGET_OFF__")
        {
            FloatingWidgetService.Instance.HideWidget();
            _lastAutoLockedBreakId = "__BREAK_WIDGET_OFF__";
        }
        
        if (current == null || current.Type != TimePointType.Break)
        {
            _wasManuallyUnlockedInBreak = false;
        }
    }

    private void HandleScheduleDeactivation(TimePoint? current, TimePoint? next)
    {
        var settings = SettingsService.Lock;
        var now = DateTime.Now;
        
        var breakEnd = GetBreakEndTime(current);
        var targetUnlockDateTime = CalculateTargetUnlockDateTime(now, settings, next, breakEnd);
        
        if (targetUnlockDateTime != null && ShouldAutoUnlock(now, targetUnlockDateTime.Value))
        {
            PerformAutoUnlock(breakEnd);
        }
    }

    private TimeSpan? GetBreakEndTime(TimePoint? current)
    {
        return current != null && current.Type == TimePointType.Break ? current.EndTime : null;
    }

    private DateTime? CalculateTargetUnlockDateTime(DateTime now, LockSettingsModel settings, TimePoint? next, TimeSpan? breakEnd)
    {
        DateTime? targetUnlockDateTime = null;

        targetUnlockDateTime = CalculateNextClassUnlockTime(now, settings, next);

        if (breakEnd != null)
        {
            var breakEndDateTime = CalculateBreakEndDateTime(now, breakEnd.Value);
            if (targetUnlockDateTime == null || breakEndDateTime < targetUnlockDateTime.Value)
            {
                targetUnlockDateTime = breakEndDateTime;
            }
        }

        return targetUnlockDateTime;
    }

    private DateTime? CalculateNextClassUnlockTime(DateTime now, LockSettingsModel settings, TimePoint? next)
    {
        if (next != null && next.Type == TimePointType.Class)
        {
            var nextClassToday = now.Date.Add(next.StartTime);
            return nextClassToday.Subtract(TimeSpan.FromMinutes(settings.AutoUnlockBeforeClassMinutes));
        }

        var (nextClassPoint, nextClassDateTime) = ScheduleService.Instance.GetNextClassPoint();
        if (nextClassDateTime != null)
        {
            return nextClassDateTime.Value.Subtract(TimeSpan.FromMinutes(settings.AutoUnlockBeforeClassMinutes));
        }

        return null;
    }

    private DateTime CalculateBreakEndDateTime(DateTime now, TimeSpan breakEnd)
    {
        var breakEndDateTime = now.Date.Add(breakEnd);
        if (breakEndDateTime < now)
        {
            breakEndDateTime = breakEndDateTime.AddDays(1);
        }
        return breakEndDateTime;
    }

    private bool ShouldAutoUnlock(DateTime now, DateTime targetUnlockDateTime)
    {
        var timeToUnlock = targetUnlockDateTime - now;
        return timeToUnlock.TotalMinutes <= 0;
    }

    private void PerformAutoUnlock(TimeSpan? breakEnd)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                DeactivateLock();
                var message = GetAutoUnlockMessage(breakEnd);
                NotificationService.Instance.ShowInfo(message);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "Schedule", "AutoUnlock", $"自动解锁失败：{ex.Message}\n{ex.StackTrace}");
            }
        });
    }

    private string GetAutoUnlockMessage(TimeSpan? breakEnd)
    {
        return breakEnd != null 
            ? "本次课间已结束，已自动解除锁定/防护" 
            : "即将上课，已自动提前解除锁定/防护";
    }

    public void ActivateLock(LockMode mode)
    {
        ActivateLock(mode, true);
    }

    public void ActivateLock(LockMode mode, bool isManual)
    {
        _currentMode = mode;
        _isManualLock = isManual;
        FloatingWidgetService.Instance.HideWidget();
        _lastAutoLockedBreakId = "__BREAK_WIDGET_OFF__";

        if (mode == LockMode.ProtectionOnly)
        {
            IsProtectionOnlyActive = true;
            EnableProtections();
            StartProtectionHooks();
            CreateLockStateFile();
            ShowProtectionOnlyInfo();
            ShowProtectionInfoWindow();
            return;
        }

        IsProtectionOnlyActive = false;
        CloseProtectionInfoWindow();
        StopProtectionHooks();

        if (mode == LockMode.Full)
        {
            EnableProtections();
        }

        StartScreenLock();
        _lastLockScheduleSnapshot = ScheduleService.Instance.GetCurrentAndNextTimePoint(DateTime.Now.TimeOfDay);
        
        ShowAutoUnlockNotification();

        // 通知集控端锁屏状态
        _ = WebSocketService.Instance.SendLockStateAsync(true);
    }

    public void DeactivateLock()
    {
        IsLocked = false;
        IsProtectionOnlyActive = false;
        _isManualLock = false;
        StopScreenLock();
        StopProtectionHooks();
        CloseProtectionInfoWindow();
        DisableProtections();
        DeleteLockStateFile();

        // 通知集控端解锁状态
        _ = WebSocketService.Instance.SendLockStateAsync(false);
    }

    public void ManualDeactivateLock()
    {
        var now = DateTime.Now.TimeOfDay;
        var (current, _) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);
        if (current != null && current.Type == TimePointType.Break)
        {
            _wasManuallyUnlockedInBreak = true;
        }
        DeactivateLock();
    }

    public void Relock()
    {
        if (IsLocked || IsProtectionOnlyActive)
        {
            return;
        }

        var now = DateTime.Now.TimeOfDay;
        var (current, _) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);
        if (current == null || current.Type != TimePointType.Break)
        {
            NotificationService.Instance.ShowWarning("当前不是课间休息时间，无法重新锁定");
            return;
        }

        ActivateLock(LockMode.Full, true);
        NotificationService.Instance.ShowInfo("已重新锁定");
    }

    public bool CanRelock()
    {
        if (IsLocked || IsProtectionOnlyActive)
        {
            return false;
        }

        var now = DateTime.Now.TimeOfDay;
        var (current, _) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);
        return current != null && current.Type == TimePointType.Break;
    }

    public void RefreshBreakWidgetVisibility()
    {
        CheckSchedule();
    }

    private void ShowProtectionOnlyInfo()
    {
        NotificationService.Instance.ShowInfo("已启动仅防护模式");
    }

    private void ShowAutoUnlockNotification()
    {
        var settings = SettingsService.Lock;
        if (!settings.EnableBreakTimeLock)
        {
            return;
        }

        var (nextClassPoint, nextClassDateTime) = ScheduleService.Instance.GetNextClassPoint();
        if (nextClassDateTime == null)
        {
            return;
        }

        var unlockTime = nextClassDateTime.Value.Subtract(TimeSpan.FromMinutes(settings.AutoUnlockBeforeClassMinutes));
        var remaining = unlockTime - DateTime.Now;
        
        if (remaining.TotalMinutes > 0)
        {
            var minutes = (int)remaining.TotalMinutes;
            var classTimeStr = nextClassDateTime.Value.ToString("HH:mm");
            NotificationService.Instance.ShowInfo($"已锁定，将在 {minutes} 分钟后（{classTimeStr} 上课前）自动解锁");
        }
        else
        {
            var classTimeStr = nextClassDateTime.Value.ToString("HH:mm");
            NotificationService.Instance.ShowInfo($"已锁定，将在 {classTimeStr} 上课前自动解锁");
        }
    }

    private void ShowProtectionInfoWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_protectionWindow != null)
            {
                if (!_protectionWindow.IsVisible)
                {
                    ShowWindowSafely(ref _protectionWindow, () => new FloatingLockWidget
                    {
                        DataContext = new FloatingLockWidgetViewModel()
                    }, true);
                }
                _protectionWindow.Topmost = true;
                return;
            }

            var window = new FloatingLockWidget
            {
                DataContext = new FloatingLockWidgetViewModel()
            };

            // 根据设置应用 dark 类
            if (SettingsService.General.DarkMode)
            {
                window.Classes.Add("dark");
            }

            _protectionWindow = window;
            window.Show();
        });
    }

    private void CloseProtectionInfoWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_protectionWindow != null)
            {
                try
                {
                    _protectionWindow.Close();
                }
                catch
                {
                }
                finally
                {
                    _protectionWindow = null;
                }
            }
        });
    }

    private void EnableProtections()
    {
        _previousNetworkLockState = SettingsService.Blockage.IsNetworkLockEnabled;
        
        if (!_previousNetworkLockState.Value)
        {
            SettingsService.UpdateBlockage(settings => settings.IsNetworkLockEnabled = true);
            LogService.Instance.Log("Info", "LockScreen", "EnableProtections", "已强制开启网络拦截");
        }
        
        _ = NetworkBlockingService.Instance.ApplyRulesAsync("EnableProtections");
    }

    private void DisableProtections()
    {
        if (_previousNetworkLockState.HasValue && !_previousNetworkLockState.Value)
        {
            SettingsService.UpdateBlockage(settings => settings.IsNetworkLockEnabled = false);
            LogService.Instance.Log("Info", "LockScreen", "DisableProtections", "已恢复网络拦截设置");
        }
        
        _previousNetworkLockState = null;
        _ = NetworkBlockingService.Instance.ApplyRulesAsync("DisableProtections");
    }

    private void StartScreenLock()
    {
        if (IsLocked)
        {
            return;
        }

        IsLocked = true;
        _lockStartTime = DateTime.Now;
        StartMaxLockDurationTimer();

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StartHooks();
        CreateOrShowLockWindow();
        CreateLockStateFile();

        _topmostTimer?.Dispose();
        _topmostTimer = new Timer(_ => EnsureLockWindowState(), null, 0, 1000);
    }

    private void StopScreenLock()
    {
        StopMaxLockDurationTimer();
        _lockStartTime = null;
        StopHooks();
        DeleteLockStateFile();

        _topmostTimer?.Dispose();
        _topmostTimer = null;

        if (_lockWindow != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _lockWindow.Close();
                }
                catch
                {
                }
                finally
                {
                    _lockWindow = null;
                }
            });
        }
    }

    private void CreateOrShowLockWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_lockWindow != null)
            {
                if (!_lockWindow.IsVisible)
                {
                    ShowWindowSafely(ref _lockWindow, () => CreateLockWindow(), false);
                }
                _lockWindow.Topmost = true;
                return;
            }

            _lockWindow = CreateLockWindow();
            ShowWindowSafely(ref _lockWindow, () => CreateLockWindow(), false);
        });
    }

    private Window CreateLockWindow()
    {
        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        var lockWindow = new LockWindow
        {
            DataContext = new LockWindowViewModel()
        };

        // 根据设置应用 dark 类
        if (SettingsService.General.DarkMode)
        {
            lockWindow.Classes.Add("dark");
        }

        if (mainWindow != null)
        {
            lockWindow.Icon = mainWindow.Icon;
        }

        return lockWindow;
    }

    private void ShowWindowSafely(ref Window window, Func<Window> createWindowFunc, bool isProtectionWindow)
    {
        try
        {
            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null && mainWindow.IsVisible)
            {
                window.Show(mainWindow);
            }
            else
            {
                window.Show();
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Cannot re-show a closed window"))
        {
            LogService.Instance.Log("Warning", "Window", "Show", $"窗口已关闭，正在释放旧窗口并重建: {ex.Message}");
            
            try
            {
                window.Close();
            }
            catch
            {
            }
            
            window = createWindowFunc();

            // 根据设置应用 dark 类
            if (SettingsService.General.DarkMode)
            {
                window.Classes.Add("dark");
            }

            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null && mainWindow.IsVisible)
            {
                window.Show(mainWindow);
            }
            else
            {
                window.Show();
            }
        }
    }

    private void EnsureLockWindowState()
    {
        if (!IsLocked)
        {
            return;
        }

        HandleForcedTopmostApps();

        Dispatcher.UIThread.Post(() =>
        {
            if (!CheckLockWindowExists())
            {
                return;
            }

            UpdateLockWindowState();
        });
    }

    private bool CheckLockWindowExists()
    {
        return _lockWindow != null;
    }

    private void UpdateLockWindowState()
    {
        var foregroundHwnd = GetForegroundWindow();
        var lockHwnd = _lockWindow!.TryGetPlatformHandle()?.Handle;

        if (IsLockWindowForeground(lockHwnd, foregroundHwnd))
        {
            EnsureLockWindowVisible();
            return;
        }

        var foregroundProcessName = GetForegroundProcessName(foregroundHwnd);
        var isAllowed = IsProcessAllowed(foregroundProcessName);

        if (isAllowed)
        {
            HideLockWindowForAllowedProcess();
        }
        else
        {
            ShowAndActivateLockWindow();
        }
    }

    private bool IsLockWindowForeground(IntPtr? lockHwnd, IntPtr foregroundHwnd)
    {
        return lockHwnd != null && foregroundHwnd == lockHwnd.Value;
    }

    private void EnsureLockWindowVisible()
    {
        if (!_lockWindow!.IsVisible)
        {
            ShowWindowSafely(ref _lockWindow, () => CreateLockWindow(), false);
        }
    }

    private string GetForegroundProcessName(IntPtr foregroundHwnd)
    {
        try
        {
            GetWindowThreadProcessId(foregroundHwnd, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void HideLockWindowForAllowedProcess()
    {
        if (_lockWindow!.IsVisible)
        {
            _lockWindow.Hide();
        }
    }

    private void ShowAndActivateLockWindow()
    {
        if (!_lockWindow!.IsVisible)
        {
            ShowWindowSafely(ref _lockWindow, () => CreateLockWindow(), false);
        }

        if (!NotificationService.Instance.IsShowingNotification)
        {
            ActivateLockWindow();
        }
    }

    private void ActivateLockWindow()
    {
        _lockWindow!.Topmost = false;
        _lockWindow.Topmost = true;
        _lockWindow.Activate();
    }

    private void HandleForcedTopmostApps()
    {
        var settings = SettingsService.Lock;
        if (settings.ForcedTopmostApps.Count == 0) return;

        foreach (var appName in settings.ForcedTopmostApps)
        {
            try
            {
                var processes = Process.GetProcessesByName(appName);
                foreach (var process in processes)
                {
                    IntPtr hwnd = process.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        // 如果不是当前前台窗口，则尝试置顶
                        if (GetForegroundWindow() != hwnd)
                        {
                            ShowWindow(hwnd, SW_RESTORE);
                            SetForegroundWindow(hwnd);
                        }
                    }
                }
            }
            catch
            {
                // 忽略错误
            }
        }
    }

    private void StartHooks()
    {
        if (_mouseHookId != IntPtr.Zero || _keyboardHookId != IntPtr.Zero)
        {
            return;
        }

        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;

        IntPtr moduleHandle = currentModule != null ? GetModuleHandle(currentModule.ModuleName) : IntPtr.Zero;

        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
    }

    private void StopHooks()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }

        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
    }

    private void StartProtectionHooks()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            return;
        }

        _keyboardProc = KeyboardHookCallback;

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;

        IntPtr moduleHandle = currentModule != null ? GetModuleHandle(currentModule.ModuleName) : IntPtr.Zero;

        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
    }

    private void StopProtectionHooks()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsLocked && !IsAllowedForegroundProcess())
        {
            return new IntPtr(1);
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        if (!IsLocked && !IsProtectionOnlyActive)
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        bool isKeyDown = IsKeyDownEvent(wParam);
        if (isKeyDown)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool shouldBlock = ShouldBlockKey(hookStruct.vkCode, IsLocked);
            
            if (shouldBlock)
            {
                return new IntPtr(1);
            }
        }

        if (IsLocked && !IsAllowedForegroundProcess())
        {
            return new IntPtr(1);
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private bool IsKeyDownEvent(IntPtr wParam)
    {
        return wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
    }

    private bool ShouldBlockKey(int vkCode, bool isFullLockMode)
    {
        return EvaluateKeyBlocking(vkCode, isFullLockMode);
    }

    private bool EvaluateKeyBlocking(int vkCode, bool isFullLockMode)
    {
        // 使用 switch 表达式判断按键类型
        var keyType = GetKeyType(vkCode);
        
        return keyType switch
        {
            KeyType.AlwaysBlock => true,
            KeyType.FullLockOnly => isFullLockMode,
            KeyType.Allow => false,
            _ => false
        };
    }

    private KeyType GetKeyType(int vkCode)
    {
        return vkCode switch
        {
            VK_LWIN or VK_RWIN => KeyType.AlwaysBlock,
            VK_LMENU or VK_RMENU => KeyType.AlwaysBlock,
            VK_TAB => KeyType.AlwaysBlock,
            VK_ESCAPE => KeyType.AlwaysBlock,
            VK_F4 => KeyType.AlwaysBlock,
            VK_DELETE => KeyType.AlwaysBlock,
            VK_LCONTROL or VK_RCONTROL => KeyType.FullLockOnly,
            _ => KeyType.Allow
        };
    }

    private enum KeyType
    {
        AlwaysBlock,
        FullLockOnly,
        Allow
    }

    private bool IsAllowedForegroundProcess()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return IsProcessAllowed(process.ProcessName);
        }
        catch { return false; }
    }

    private bool IsProcessAllowed(string processName)
    {        if (string.IsNullOrWhiteSpace(processName)) return false;
        
        // 总是允许自身
        if (string.Equals(processName, "ClassScreenLock", StringComparison.OrdinalIgnoreCase))
        {            return true;
        }

        var settings = SettingsService.Lock;
        
        // 检查允许置顶的程序
        if (settings.AllowedTopmostApps.Any(allowed => string.Equals(processName, allowed, StringComparison.OrdinalIgnoreCase)))
        {            return true;
        }

        // 检查强制置顶的程序
        if (settings.ForcedTopmostApps.Any(forced => string.Equals(processName, forced, StringComparison.OrdinalIgnoreCase)))
        {            return true;
        }

        return false;
    }

    private void CreateLockStateFile()
    {
        try
        {
            if (!Directory.Exists(LockStateDirectory))
            {
                Directory.CreateDirectory(LockStateDirectory);
            }

            var stateData = new LockStateData
            {
                IsLocked = true,
                LockMode = _currentMode,
                Timestamp = DateTime.Now,
                ProcessId = Environment.ProcessId
            };

            var json = JsonSerializer.Serialize(stateData, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            ReleaseLockStateFile();
            
            _lockStateFileStream = new FileStream(
                LockStateFile,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None);
            
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            _lockStateFileStream.Write(bytes, 0, bytes.Length);
            _lockStateFileStream.Flush();

            var fileInfo = new FileInfo(LockStateFile);
            fileInfo.Attributes = FileAttributes.Hidden | FileAttributes.System;

            LogService.Instance.Log("Info", "LockState", "File", $"锁屏状态文件已创建并锁定: {LockStateFile}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "LockState", "File", $"创建锁屏状态文件失败: {ex.Message}");
        }
    }

    private void ReleaseLockStateFile()
    {
        try
        {
            _lockStateFileStream?.Flush();
            _lockStateFileStream?.Close();
            _lockStateFileStream?.Dispose();
            _lockStateFileStream = null;
        }
        catch
        {
        }
    }

    private void DeleteLockStateFile()
    {
        try
        {
            ReleaseLockStateFile();
            
            if (File.Exists(LockStateFile))
            {
                File.Delete(LockStateFile);
                LogService.Instance.Log("Info", "LockState", "File", "锁屏状态文件已解锁并删除");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "LockState", "File", $"删除锁屏状态文件失败: {ex.Message}");
        }
    }

    public LockStateData? GetLockStateData()
    {
        try
        {
            if (!File.Exists(LockStateFile))
            {
                return null;
            }

            var json = File.ReadAllText(LockStateFile);
            var stateData = JsonSerializer.Deserialize<LockStateData>(json);

            if (stateData == null)
            {
                return null;
            }

            if (stateData.ProcessId > 0)
            {
                try
                {
                    var process = Process.GetProcessById(stateData.ProcessId);
                    // 检查进程名是否匹配
                    if (process.ProcessName.Equals("ClassScreenLock", StringComparison.OrdinalIgnoreCase))
                    {
                        return null; // 自己的进程，不恢复
                    }
                    // 如果进程名不匹配，说明进程 ID 可能被其他进程复用
                    // 继续返回 stateData，允许恢复（因为这不是当前实例）
                }
                catch (ArgumentException)
                {
                    // 进程不存在，允许恢复
                }
                catch (InvalidOperationException)
                {
                    // 进程不存在，允许恢复
                }
            }

            return stateData;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "LockState", "Get", $"读取锁屏状态文件失败: {ex.Message}");
            return null;
        }
    }

    public async Task<LockStateData?> GetLockStateDataAsync()
    {
        try
        {
            if (!File.Exists(LockStateFile))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(LockStateFile);
            var stateData = JsonSerializer.Deserialize<LockStateData>(json);

            if (stateData == null)
            {
                return null;
            }

            if (stateData.ProcessId > 0)
            {
                try
                {
                    var process = Process.GetProcessById(stateData.ProcessId);
                    // 检查进程名是否匹配
                    if (process.ProcessName.Equals("ClassScreenLock", StringComparison.OrdinalIgnoreCase))
                    {
                        return null; // 自己的进程，不恢复
                    }
                    // 如果进程名不匹配，说明进程 ID 可能被其他进程复用
                    // 继续返回 stateData，允许恢复（因为这不是当前实例）
                }
                catch (ArgumentException)
                {
                    // 进程不存在，允许恢复
                }
                catch (InvalidOperationException)
                {
                    // 进程不存在，允许恢复
                }
            }

            return stateData;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "LockState", "File", $"读取锁屏状态文件失败: {ex.Message}");
            return null;
        }
    }

    public void CleanupLockStateFile()
    {
        if (IsProtectionOnlyActive && !IsLocked)
        {
            ReleaseLockStateFile();
            return;
        }

        DeleteLockStateFile();
    }

    private void StartMaxLockDurationTimer()
    {
        StopMaxLockDurationTimer();

        var maxHours = SettingsService.General.MaxLockDurationHours;
        
        if (maxHours <= 0)
        {
            return;
        }

        var checkInterval = TimeSpan.FromMinutes(1);
        _maxLockDurationTimer = new Timer(_ => CheckMaxLockDuration(), null, checkInterval, checkInterval);
        LogService.Instance.Log("Info", "LockDuration", "Timer", $"已启动最大锁定时间检查，最大时长: {maxHours} 小时");
    }

    private void StopMaxLockDurationTimer()
    {
        _maxLockDurationTimer?.Dispose();
        _maxLockDurationTimer = null;
    }

    private void CheckMaxLockDuration()
    {
        try
        {
            if (!IsLocked || !_lockStartTime.HasValue)
            {
                return;
            }

            var maxHours = SettingsService.General.MaxLockDurationHours;
            
            if (maxHours <= 0)
            {
                return;
            }

            var elapsed = DateTime.Now - _lockStartTime.Value;
            var maxDuration = TimeSpan.FromHours(maxHours);

            if (elapsed >= maxDuration)
            {
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        LogService.Instance.Log("Info", "LockDuration", "AutoUnlock", $"已达到最大锁定时间 {maxHours} 小时，自动解锁");
                        NotificationService.Instance.ShowSuccess(LocalizationService.Instance.GetString("Notify_MaxLockDurationReached") ?? "已达到最大锁定时间，自动解锁");
                        DeactivateLock();
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Error", "LockDuration", "AutoUnlock", $"自动解锁失败：{ex.Message}\n{ex.StackTrace}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "LockDuration", "Check", $"检查最大锁定时间失败：{ex.Message}\n{ex.StackTrace}");
        }
    }
}

public class LockStateData
{
    public bool IsLocked { get; set; }
    public LockMode LockMode { get; set; }
    public DateTime Timestamp { get; set; }
    public int ProcessId { get; set; }
}
