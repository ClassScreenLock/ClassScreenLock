using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClassScreenLock.Services;

namespace ClassScreenLock.Views;

public partial class FloatingLockWidget : Window
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int HWND_TOPMOST = -1;
    private const int HWND_TOP = 0;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private DispatcherTimer? _protectionTimer;
    private IntPtr _hwnd;

    public FloatingLockWidget()
    {
        InitializeComponent();

        Opened += OnOpened;
        PropertyChanged += OnWindowPropertyChanged;

        var screens = Screens;
        if (screens != null)
        {
            var primaryScreen = screens.Primary;
            if (primaryScreen != null)
            {
                var workingArea = primaryScreen.WorkingArea;
                int x = workingArea.X + 20;
                int y = workingArea.Y + workingArea.Height - 220;
                Position = new PixelPoint(x, y);
            }
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            LogService.Instance.Log("UI", "FloatingWidgetOpened", "FloatingLockWidget", "Floating lock widget opened");

            var platformHandle = this.TryGetPlatformHandle();
            if (platformHandle != null)
            {
                _hwnd = platformHandle.Handle;

                var style = GetWindowLong(_hwnd, GWL_STYLE);
                style &= ~WS_MINIMIZEBOX;
                style &= ~WS_MAXIMIZEBOX;
                SetWindowLong(_hwnd, GWL_STYLE, style);

                var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOPMOST;
                SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

                LogService.Instance.Log("Debug", "FloatingLockWidget", "WndProc",
                    "已移除最小化/最大化按钮样式，设置置顶扩展样式");

                StartProtectionTimer();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "FloatingLockWidget", "WndProcHook",
                $"无法修改窗口样式: {ex.Message}");
        }
    }

    private void StartProtectionTimer()
    {
        if (_protectionTimer != null) return;

        var settings = Services.SettingsService.Lock;
        var interval = Math.Max(0.01, Math.Min(500, settings.TopmostRefreshInterval));

        _protectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(interval)
        };
        _protectionTimer.Tick += ProtectionTimer_Tick;
        _protectionTimer.Start();

        LogService.Instance.Log("Debug", "FloatingLockWidget", "ProtectionTimer", $"已启动窗口保护定时器 ({interval}ms)");
    }

    private void StopProtectionTimer()
    {
        if (_protectionTimer != null)
        {
            _protectionTimer.Stop();
            _protectionTimer.Tick -= ProtectionTimer_Tick;
            _protectionTimer = null;
        }
    }

    private void ProtectionTimer_Tick(object? sender, EventArgs e)
    {
        if (_hwnd == IntPtr.Zero) return;

        try
        {
            if (IsIconic(_hwnd))
            {
                LogService.Instance.Log("Warning", "FloatingLockWidget", "ProtectionTimer",
                    "检测到窗口被最小化 (IsIconic)，立即恢复");

                ShowWindow(_hwnd, 9);
                SetWindowPos(_hwnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            if (!IsWindowVisible(_hwnd))
            {
                LogService.Instance.Log("Warning", "FloatingLockWidget", "ProtectionTimer",
                    "检测到窗口被隐藏，立即恢复");

                ShowWindow(_hwnd, 9);
                SetWindowPos(_hwnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            SetWindowPos(_hwnd, new IntPtr(HWND_TOP), 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            SetWindowPos(_hwnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FloatingLockWidget", "ProtectionTimer",
                $"保护定时器异常: {ex.Message}");
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            if (WindowState == WindowState.Minimized)
            {
                LogService.Instance.Log("Warning", "FloatingLockWidget", "StateChanged",
                    "检测到窗口被最小化，立即恢复");

                WindowState = WindowState.Normal;

                if (_hwnd != IntPtr.Zero)
                {
                    SetWindowPos(_hwnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0,
                        SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
            }
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopProtectionTimer();

        try
        {
            LogService.Instance.Log("UI", "FloatingWidgetClosed", "FloatingLockWidget", "Floating lock widget closed");
        }
        catch
        {
        }
        base.OnClosed(e);
    }
}
