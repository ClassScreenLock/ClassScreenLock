using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassScreenLock.Services;

namespace ClassScreenLock.Views;

/// <summary>
/// 系统托盘菜单弹出窗口。
///
/// 定位策略（彻底解决高 DPI / 低分辨率下菜单被遮挡的问题）：
/// 1. Avalonia 的 Bounds / 布局尺寸是 DIP，而 Window.Position 与 GetCursorPos 是物理像素。
///    两者不能直接相减，统一按「目标屏幕 Scaling」把 DIP 换算为物理像素后再定位。
/// 2. 窗口四周保留 <see cref="ShadowMargin"/> 透明内边距，使阴影绘制在窗口内部，
///    避免 SizeToContent 窗口把阴影边缘裁掉。
/// 3. 显示前按目标屏幕可用工作区反推菜单最大高度，超出时改为内部滚动，
///    任何分辨率 / 缩放比例都不会出现「退出项被截断」。
/// 4. 定位后按工作区四边钳制，任务栏位于上 / 下 / 左 / 右时都能正确避让。
/// </summary>
public class TrayPopupWindow : Window
{
    /// <summary>透明阴影边距（DIP）：为 BoxShadow 预留绘制空间。</summary>
    private const double ShadowMargin = 18;

    /// <summary>菜单面板与鼠标 / 屏幕边缘的安全间距（DIP）。</summary>
    private const double EdgeGap = 8;

    /// <summary>菜单可被压缩的最小高度（DIP），极端小屏时直接贴边显示。</summary>
    private const double MinMenuHeight = 200;

    /// <summary>菜单面板自身的内边距 + 描边总高度（DIP），用于换算内容区可用高度。</summary>
    private const double ShellChrome = 10;

    private readonly TrayMenuView _menuView;

    /// <summary>抑制失焦关闭（Show 之后短时间内忽略 Deactivated）。</summary>
    private bool _suppressDeactivated;

    private DispatcherTimer? _suppressTimer;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    public TrayPopupWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = new List<WindowTransparencyLevel> { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ClipToBounds = false;

        if (SettingsService.General.DarkMode)
        {
            Classes.Add("dark");
        }

        _menuView = new TrayMenuView();

        // 只保留透明阴影边距；滚动容器位于菜单面板内部（TrayMenuView）
        Content = new Border
        {
            Padding = new Thickness(ShadowMargin),
            Child = _menuView
        };

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
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// 在鼠标光标附近显示菜单，并保证完整落在当前屏幕的可用工作区内。
    /// </summary>
    public void ShowNearCursor()
    {
        SyncTheme();

        if (!IsVisible)
        {
            // 先放到屏幕外，等布局完成后再计算真实位置，避免闪现在左上角
            Position = new PixelPoint(-32000, -32000);
            Show();
        }

        BeginSuppressDeactivated();

        // 等 SizeToContent 完成一次真实布局后再测量，杜绝使用硬编码兜底尺寸
        Dispatcher.UIThread.Post(PlaceNearCursor, DispatcherPriority.Loaded);
    }

    private void PlaceNearCursor()
    {
        var screen = ResolveScreenAtCursor(out var cursor);
        if (screen == null)
        {
            _suppressDeactivated = false;
            return;
        }

        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var work = screen.WorkingArea;

        // 1) 先按目标屏幕可用高度限制菜单高度（DIP），从根源避免超高被截断
        var maxShellHeight = work.Height / scale - (ShadowMargin + EdgeGap) * 2;
        var maxContentHeight = Math.Max(MinMenuHeight, maxShellHeight) - ShellChrome;
        _menuView.SetMaxContentHeight(maxContentHeight);
        InvalidateMeasure();
        UpdateLayout();

        // 2) DIP -> 物理像素（Bounds 是 DIP，Position 是物理像素）
        var designWindowWidth = _menuView.Width + ShadowMargin * 2;
        var designMaxWindowHeight = maxContentHeight + ShellChrome + ShadowMargin * 2;

        var dipWidth = Bounds.Width > 0 ? Math.Min(Bounds.Width, designWindowWidth) : designWindowWidth;
        var dipHeight = Bounds.Height > 0 ? Math.Min(Bounds.Height, designMaxWindowHeight) : 0;

        if (dipHeight <= 0)
        {
            // 极端兜底：Bounds 尚未就绪时直接测量内容
            _menuView.Measure(new Size(Math.Max(0, dipWidth - ShadowMargin * 2), double.PositiveInfinity));
            dipHeight = Math.Min(_menuView.DesiredSize.Height, maxContentHeight + ShellChrome) + ShadowMargin * 2;
        }

        var windowWidth = (int)Math.Ceiling(dipWidth * scale);
        var windowHeight = (int)Math.Ceiling(dipHeight * scale);
        var marginPx = (int)Math.Round(ShadowMargin * scale);
        var gapPx = (int)Math.Round(EdgeGap * scale);

        var shellWidth = Math.Max(1, windowWidth - marginPx * 2);
        var shellHeight = Math.Max(1, windowHeight - marginPx * 2);

        var workLeft = work.X;
        var workTop = work.Y;
        var workRight = work.X + work.Width;
        var workBottom = work.Y + work.Height;

        // 3) 默认：面板右边缘贴光标（略微右溢），整体位于光标上方
        var shellLeft = cursor.X + gapPx - shellWidth;
        var shellTop = cursor.Y - gapPx - shellHeight;

        // 4) 上方空间不足 -> 翻到光标下方
        if (shellTop < workTop + gapPx)
        {
            shellTop = cursor.Y + gapPx;
        }

        // 5) 下方仍超出 -> 贴住底部安全区（高度已被 MaxHeight 限制，不会截断内容）
        if (shellTop + shellHeight > workBottom - gapPx)
        {
            shellTop = workBottom - gapPx - shellHeight;
        }

        if (shellTop < workTop + gapPx)
        {
            shellTop = workTop + gapPx;
        }

        // 6) 水平钳制
        if (shellLeft < workLeft + gapPx)
        {
            shellLeft = workLeft + gapPx;
        }

        if (shellLeft + shellWidth > workRight - gapPx)
        {
            shellLeft = workRight - gapPx - shellWidth;
        }

        // 7) 窗口位置 = 面板位置 - 透明阴影边距
        Position = new PixelPoint(shellLeft - marginPx, shellTop - marginPx);

        Activate();
        Focus();
    }

    private Screen? ResolveScreenAtCursor(out PixelPoint cursor)
    {
        if (GetCursorPos(out var point))
        {
            cursor = new PixelPoint(point.X, point.Y);
            return Screens.ScreenFromPoint(cursor)
                   ?? Screens.Primary
                   ?? Screens.All.FirstOrDefault();
        }

        var primary = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (primary != null)
        {
            var area = primary.WorkingArea;
            cursor = new PixelPoint(area.X + area.Width - 24, area.Y + area.Height - 24);
        }
        else
        {
            cursor = default;
        }

        return primary;
    }

    private void SyncTheme()
    {
        if (SettingsService.General.DarkMode)
        {
            if (!Classes.Contains("dark"))
            {
                Classes.Add("dark");
            }
        }
        else
        {
            Classes.Remove("dark");
        }
    }

    private void BeginSuppressDeactivated()
    {
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
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_suppressDeactivated)
        {
            return;
        }

        Hide();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
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
