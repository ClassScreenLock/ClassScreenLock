using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassScreenLock.Services;
using ClassScreenLock.Views;

namespace ClassScreenLock.Views;

public class TrayPopupWindow : Window
{
    private readonly TrayMenuView _menuView;

    /// <summary>
    /// 抑制失焦关闭（在 ShowAtPosition 后的短时间内避免被 OnDeactivated 误关）
    /// </summary>
    private bool _suppressDeactivated;

    /// <summary>
    /// 抑制计时器（用于在显示后短时间内忽略 Deactivated 事件）
    /// </summary>
    private DispatcherTimer? _suppressTimer;

    public TrayPopupWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        TransparencyLevelHint = new List<WindowTransparencyLevel> { WindowTransparencyLevel.Transparent };
        ExtendClientAreaToDecorationsHint = true;
        ClipToBounds = false;

        if (SettingsService.General.DarkMode)
        {
            Classes.Add("dark");
        }

        _menuView = new TrayMenuView();
        Content = _menuView;

        _menuView.ShowClicked += (s, e) => { Hide(); ShowClicked?.Invoke(this, e); };
        _menuView.LockClicked += (s, e) => { Hide(); LockClicked?.Invoke(this, e); };
        _menuView.AppManagementClicked += (s, e) => { Hide(); AppManagementClicked?.Invoke(this, e); };
        _menuView.NetworkInterceptionClicked += (s, e) => { Hide(); NetworkInterceptionClicked?.Invoke(this, e); };
        _menuView.SecurityLogsClicked += (s, e) => { Hide(); SecurityLogsClicked?.Invoke(this, e); };
        _menuView.SecurityCenterClicked += (s, e) => { Hide(); SecurityCenterClicked?.Invoke(this, e); };
        _menuView.LockSettingsClicked += (s, e) => { Hide(); LockSettingsClicked?.Invoke(this, e); };
        _menuView.ScheduleClicked += (s, e) => { Hide(); ScheduleClicked?.Invoke(this, e); };
        _menuView.ExitClicked += (s, e) => { Hide(); ExitClicked?.Invoke(this, e); };

        Deactivated += OnDeactivated;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // 如果处于抑制期（例如刚刚 Show），不关闭（避免被焦点抢占的瞬态事件误关）
        if (_suppressDeactivated) return;
        Hide();
    }

    public void ShowAtPosition(PixelPoint position)
    {
        // 同步 dark 类
        if (SettingsService.General.DarkMode)
        {
            if (!Classes.Contains("dark"))
                Classes.Add("dark");
        }
        else
        {
            Classes.Remove("dark");
        }

        // 先放到屏幕外（避免布局计算期间用户看到闪烁在 0,0）
        // 屏幕外位置：-32000, -32000 是 Windows 通用做法
        if (!IsVisible)
        {
            Position = new PixelPoint(-32000, -32000);
            Show();
        }

        // 设置抑制标志位（防止刚 Show 时被 Deactivated 误关）
        _suppressDeactivated = true;
        _suppressTimer?.Stop();
        _suppressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _suppressTimer.Tick += (s, e) =>
        {
            _suppressDeactivated = false;
            _suppressTimer?.Stop();
        };
        _suppressTimer.Start();

        // 通过 Dispatcher 在下一帧应用正确位置，确保布局完成
        Dispatcher.UIThread.Post(() =>
        {
            // 再次校验 dark 类（防止主题切换后状态不一致）
            if (SettingsService.General.DarkMode && !Classes.Contains("dark"))
                Classes.Add("dark");
            else if (!SettingsService.General.DarkMode && Classes.Contains("dark"))
                Classes.Remove("dark");

            Position = position;

            // 强制让窗口获得焦点
            Activate();
            Focus();
        }, DispatcherPriority.Render);
    }

    public event EventHandler? ShowClicked;
    public event EventHandler? LockClicked;
    public event EventHandler? AppManagementClicked;
    public event EventHandler? NetworkInterceptionClicked;
    public event EventHandler? SecurityLogsClicked;
    public event EventHandler? SecurityCenterClicked;
    public event EventHandler? LockSettingsClicked;
    public event EventHandler? ScheduleClicked;
    public event EventHandler? ExitClicked;
}
