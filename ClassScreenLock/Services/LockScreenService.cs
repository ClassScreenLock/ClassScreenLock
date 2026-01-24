using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Media;
using ClassScreenLock.Models;
using ClassScreenLock.Views;
using ClassScreenLock.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClassScreenLock.Services;

public class LockScreenService : INotifyPropertyChanged
{
    private static readonly LockScreenService _instance = new();
    public static LockScreenService Instance => _instance;

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
    private bool _isManualLock;
    private string? _lastAutoLockedBreakId;
    private bool _wasManuallyUnlockedInBreak;
    private (TimePoint? current, TimePoint? next) _lastLockScheduleSnapshot;

    public LockScreenService()
    {        _scheduleTimer = new Timer(_ => CheckSchedule(), null, 0, 5000);
    }

    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private LowLevelHookProc? _mouseProc;
    private LowLevelHookProc? _keyboardProc;

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

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
        var settings = SettingsService.Lock;
        var now = DateTime.Now.TimeOfDay;
        var (current, next) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);

        // 不再在课间自动弹出锁屏，只通过按钮/托盘/菜单手动触发

        // 1. 控制下课按钮进程（BreakButtonProcess）只在课间可见，上课或空档结束进程
        // 为避免按钮一闪一闪，只在状态发生变化时才调用 Show/Hide；如果本课间已手动解锁，则不再显示按钮
        bool shouldShowWidget = settings.EnableBreakTimeLock && !IsLocked && !IsProtectionOnlyActive && current != null && current.Type == TimePointType.Break && !_wasManuallyUnlockedInBreak;
        if (shouldShowWidget)
        {
            if (_lastAutoLockedBreakId != "__BREAK_WIDGET_ON__")
            {
                FloatingWidgetService.Instance.ShowWidget();
                _lastAutoLockedBreakId = "__BREAK_WIDGET_ON__";
            }
        }
        else
        {
            if (_lastAutoLockedBreakId != "__BREAK_WIDGET_OFF__")
            {
                FloatingWidgetService.Instance.HideWidget();
                _lastAutoLockedBreakId = "__BREAK_WIDGET_OFF__";
            }
            // 离开课间后，允许下一课间重新显示按钮
            if (current == null || current.Type != TimePointType.Break)
            {
                _wasManuallyUnlockedInBreak = false;
            }
        }

        if (IsLocked || IsProtectionOnlyActive)
        {
            TimePoint? unlockPoint = null;

            if (_lastLockScheduleSnapshot.current != null && _lastLockScheduleSnapshot.current.Type == TimePointType.Break)
            {
                unlockPoint = _lastLockScheduleSnapshot.current;
            }
            else if (next != null && next.Type == TimePointType.Class)
            {
                unlockPoint = next;
            }

            if (unlockPoint != null)
            {
                var unlockTime = unlockPoint.Type == TimePointType.Break
                    ? unlockPoint.EndTime
                    : unlockPoint.StartTime.Subtract(TimeSpan.FromMinutes(settings.AutoUnlockBeforeClassMinutes));

                var minutesToUnlock = (unlockTime - now).TotalMinutes;

                if (minutesToUnlock <= 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        DeactivateLock();
                        if (unlockPoint.Type == TimePointType.Break)
                        {
                            NotificationService.Instance.ShowInfo("本次课间已结束，已自动解除锁定/防护");
                        }
                        else
                        {
                            NotificationService.Instance.ShowInfo("即将上课，已自动提前解除锁定/防护");
                        }
                    });
                }
            }
        }
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
            StopScreenLock();
            ShowProtectionOnlyInfo();
            ShowProtectionInfoWindow();
            return;
        }

        IsProtectionOnlyActive = false;
        CloseProtectionInfoWindow();

        if (mode == LockMode.Full)
        {
            EnableProtections();
        }

        StartScreenLock();
        _lastLockScheduleSnapshot = ScheduleService.Instance.GetCurrentAndNextTimePoint(DateTime.Now.TimeOfDay);
    }

    public void DeactivateLock()
    {
        IsLocked = false;
        IsProtectionOnlyActive = false;
        _isManualLock = false;
        StopScreenLock();
        CloseProtectionInfoWindow();
        // 解锁后刷新网络拦截规则，确保在关闭总开关时清理远端规则
        _ = NetworkBlockingService.Instance.ApplyRulesAsync();
    }

    public void ManualDeactivateLock()
    {
        var now = DateTime.Now.TimeOfDay;
        var (current, _) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);
        if (current != null && current.Type == TimePointType.Break)
        {
            // 标记当前课间已经手动解锁，本课间不再显示按钮
            _wasManuallyUnlockedInBreak = true;
        }
        DeactivateLock();
    }

    public void RefreshBreakWidgetVisibility()
    {
        CheckSchedule();
    }

    private void ShowProtectionOnlyInfo()
    {
        string message;

        var now = DateTime.Now.TimeOfDay;
        var (current, next) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);

        if (current != null && current.Type == TimePointType.Break)
        {
            var endTime = DateTime.Today.Add(current.EndTime);
            message = $"已启动仅防护模式，本次课间预计 {endTime:HH:mm} 结束。如需提前结束，请在应用管理中关闭基础防护或应用拦截。";
        }
        else
        {
            message = "已启动仅防护模式。如需结束，请在应用管理中关闭基础防护或应用拦截。";
        }

        NotificationService.Instance.ShowInfo(message);
    }

    private void ShowProtectionInfoWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_protectionWindow != null)
            {
                if (!_protectionWindow.IsVisible)
                {
                    _protectionWindow.Show();
                }
                _protectionWindow.Topmost = true;
                return;
            }

            var window = new FloatingLockWidget
            {
                DataContext = new FloatingLockWidgetViewModel()
            };

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
        // 尊重用户的拦截开关，不再强制修改持久化设置。
        // 仅在用户已开启网络拦截时，应用一次规则（确保锁定期间规则已生效）。
        if (SettingsService.Blockage.IsNetworkLockEnabled)
        {
            _ = NetworkBlockingService.Instance.ApplyRulesAsync();
        }
        // 应用拦截服务在应用启动或初始化完成后已启动，这里不再强制开启或修改设置。
    }

    private void StartScreenLock()
    {
        if (IsLocked)
        {
            return;
        }

        IsLocked = true;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StartHooks();
        CreateOrShowLockWindow();

        _topmostTimer?.Dispose();
        _topmostTimer = new Timer(_ => EnsureLockWindowState(), null, 0, 1000);
    }

    private void StopScreenLock()
    {
        StopHooks();

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
                    _lockWindow.Show();
                }
                _lockWindow.Topmost = true;
                return;
            }

            var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            var lockWindow = new LockWindow
            {
                DataContext = new LockWindowViewModel()
            };

            if (mainWindow != null)
            {
                lockWindow.Icon = mainWindow.Icon;
            }

            _lockWindow = lockWindow;
            if (mainWindow != null && mainWindow.IsVisible)
                lockWindow.Show(mainWindow);
            else
                lockWindow.Show();
        });
    }

    private void EnsureLockWindowState()
    {
        if (!IsLocked)
        {
            return;
        }

        // 处理强制置顶的程序
        HandleForcedTopmostApps();

        Dispatcher.UIThread.Post(() =>
        {
            if (_lockWindow == null) return;

            IntPtr foregroundHwnd = GetForegroundWindow();
            IntPtr? lockHwnd = _lockWindow.TryGetPlatformHandle()?.Handle;

            // 如果当前前台就是锁屏窗口，什么都不做，直接返回
            if (lockHwnd != null && foregroundHwnd == lockHwnd.Value)
            {
                if (!_lockWindow.IsVisible) _lockWindow.Show();
                return;
            }

            // 获取前台进程名
            string foregroundProcessName = string.Empty;
            try
            {                GetWindowThreadProcessId(foregroundHwnd, out uint pid);
                using var process = Process.GetProcessById((int)pid);
                foregroundProcessName = process.ProcessName;
            }
            catch { }

            bool isAllowed = IsProcessAllowed(foregroundProcessName);

            if (isAllowed)
            {                // 如果是允许的第三方程序在前台，隐藏锁屏窗口以让出视野
                if (_lockWindow.IsVisible)
                {                    _lockWindow.Hide();
                }
            }
            else
            {                // 如果是不允许的程序在前台，或者没有前台窗口，显示并强制置顶锁屏窗口
                if (!_lockWindow.IsVisible)
                {                    _lockWindow.Show();
                }

                if (!NotificationService.Instance.IsShowingNotification)
                {                    _lockWindow.Topmost = false;
                    _lockWindow.Topmost = true;
                    _lockWindow.Activate();
                }
            }
        });
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
        if (nCode >= 0 && IsLocked && !IsAllowedForegroundProcess())
        {
            return new IntPtr(1);
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
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
}
