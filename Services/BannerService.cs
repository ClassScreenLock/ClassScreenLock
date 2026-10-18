using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ClassScreenLock.Services;

/// <summary>
/// 横幅尺寸
/// </summary>
public enum BannerSize
{
    Small = 0,   // 小：宽度 360-560px
    Medium = 1,  // 中：宽度 480-720px
    Large = 2,   // 大：宽度 640-960px
    XLarge = 3   // 超大：宽度 960-1280px（小尺寸的两倍）
}

/// <summary>
/// 文字大小（独立于尺寸，可单独调节）
/// </summary>
public enum BannerFontSize
{
    Small = 0,   // 小
    Medium = 1,  // 中（默认）
    Large = 2,   // 大
    XLarge = 3   // 超大
}

/// <summary>
/// 持续时间模式
/// </summary>
public enum BannerDurationMode
{
    Auto = 0,       // 自动按消息长度
    Short = 1,      // 短：3 秒
    Medium = 2,     // 中：6 秒
    Long = 3,       // 长：10 秒
    Custom = 4,     // 自定义（5-60 秒）
    Persistent = 5  // 持久：不自动关闭，60 秒后可手动关闭（可被 lockWindow 覆盖）
}

/// <summary>
/// 横幅通知服务 - 显示半透明大横幅消息
/// 独立于 NotificationService，专用于集控端消息
/// </summary>
public class BannerService
{
    private static BannerService? _instance;
    public static BannerService Instance => _instance ??= new BannerService();

    private Window? _currentBanner;
    private CancellationTokenSource? _autoCloseCts;
    private bool _isLockWindow;       // 当前横幅是否启用了"禁止关闭窗口"
    private bool _isPersistent;       // 当前横幅是否处于持久模式
    private bool _pointerCloseRegistered; // 当前横幅的点击关闭事件是否已注册（用于持久模式延时启用）
    private DateTime _bannerShownAt;  // 当前横幅显示时间
    private DispatcherTimer? _closeButtonRefreshTimer; // 持久模式下定时刷新关闭按钮可用状态

    private const int PERSISTENT_CLOSE_UNLOCK_SECONDS = 60;

    private BannerService() { }

    /// <summary>
    /// 显示集控消息横幅
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="sender">发送者</param>
    /// <param name="size">横幅尺寸（默认 Small）</param>
    /// <param name="fontSize">文字大小（默认 Medium，独立于尺寸）</param>
    /// <param name="durationMode">持续时间模式</param>
    /// <param name="customDurationSeconds">自定义持续秒数（仅当 durationMode=Custom 时使用）</param>
    /// <param name="lockWindow">是否在通知时段禁止关闭窗口（开启后关闭按钮和点击关闭均失效）</param>
    public void ShowBanner(
        string title,
        string message,
        string sender,
        BannerSize size = BannerSize.Small,
        BannerFontSize fontSize = BannerFontSize.Medium,
        BannerDurationMode durationMode = BannerDurationMode.Auto,
        int customDurationSeconds = 10,
        bool lockWindow = false)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 关闭已有横幅
            CloseCurrentBanner();

            _isLockWindow = lockWindow;
            _isPersistent = durationMode == BannerDurationMode.Persistent;
            _pointerCloseRegistered = false;
            _bannerShownAt = DateTime.Now;

            var banner = CreateBannerWindow(title, message, sender, size, fontSize, lockWindow, _isPersistent);
            _currentBanner = banner;
            _autoCloseCts = new CancellationTokenSource();

            // 计算位置（屏幕顶部居中，使用 MinWidth 近似）
            CalculateAndSetPosition(banner, size);

            // 显示并播放淡入动画
            banner.Opacity = 0;
            banner.Show();

            // 窗口布局完成后（实际尺寸已知），按 Bounds 重新居中
            // 解决 SizeToContent 模式下不同尺寸不居中的问题
            banner.Opened += (_, _) => CenterBannerAtTop(banner);

            _ = PlayFadeInAsync(banner);

            // 持久模式：启动定时器，每秒检查是否到达 60 秒可关闭时间
            if (_isPersistent)
            {
                _closeButtonRefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _closeButtonRefreshTimer.Tick += (_, _) => RefreshCloseButtonState();
                _closeButtonRefreshTimer.Start();
            }

            // 计算并启动自动关闭
            // 持久模式虽然返回 24 小时（>0），但用户通常会提前点击关闭按钮
            int durationMs = CalculateDuration(message, durationMode, customDurationSeconds);
            if (durationMs > 0)
            {
                _ = AutoCloseAsync(durationMs, _autoCloseCts.Token);
            }
        });
    }

    /// <summary>
    /// 计算持续时间（毫秒）
    /// 持久模式：使用一个非常大的时间（24 小时），由用户手动关闭或 60 秒后通过关闭按钮关闭
    /// </summary>
    private int CalculateDuration(string message, BannerDurationMode mode, int customSeconds)
    {
        return mode switch
        {
            BannerDurationMode.Short => 3000,
            BannerDurationMode.Medium => 6000,
            BannerDurationMode.Long => 10000,
            BannerDurationMode.Custom => Math.Clamp(customSeconds, 5, 60) * 1000,
            BannerDurationMode.Persistent => 10 * 60 * 1000, // 持久：10 分钟（用户通常会提前关闭）
            BannerDurationMode.Auto => Math.Min(30000, Math.Max(4000, message.Length * 100)),
            _ => 6000
        };
    }

    /// <summary>
    /// 刷新关闭按钮的可用状态
    /// 持久模式下，每秒检查：到达 60 秒后启用关闭按钮（除非 lockWindow 为 true）
    /// </summary>
    private void RefreshCloseButtonState()
    {
        if (_currentBanner == null || !_isPersistent) return;

        var elapsed = (DateTime.Now - _bannerShownAt).TotalSeconds;
        var canClose = !_isLockWindow && elapsed >= PERSISTENT_CLOSE_UNLOCK_SECONDS;

        var closeButton = FindCloseButton(_currentBanner);
        if (closeButton != null)
        {
            closeButton.IsEnabled = canClose;
            // 更新提示文字
            if (!canClose && !_isLockWindow)
            {
                var remaining = Math.Max(0, PERSISTENT_CLOSE_UNLOCK_SECONDS - (int)elapsed);
                ToolTip.SetTip(closeButton, $"{remaining} 秒后可关闭");
            }
            else
            {
                ToolTip.SetTip(closeButton, null);
            }
        }

        // 持久模式 + 60 秒到达 + 关闭可用的瞬间：注册点击关闭事件
        if (canClose && !_pointerCloseRegistered)
        {
            _currentBanner.PointerPressed += (_, _) => CloseCurrentBanner();
            _pointerCloseRegistered = true;
            _closeButtonRefreshTimer?.Stop();
        }
    }

    /// <summary>
    /// 在视觉树中查找关闭按钮
    /// </summary>
    private Button? FindCloseButton(Window window)
    {
        if (window.Content is Border border && border.Child is StackPanel stack)
        {
            foreach (var child in stack.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var gc in grid.Children)
                    {
                        if (gc is Button btn && btn.Content?.ToString() == "✕")
                        {
                            return btn;
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 获取尺寸参数（仅影响宽度/图标/内边距，不影响文字字号）
    /// </summary>
    private (double minWidth, double maxWidth, double iconSize, Thickness padding, CornerRadius cornerRadius, int boxBlur, double titleFontSize, double senderFontSize) GetSizeParams(BannerSize size)
    {
        return size switch
        {
            // 小：当前默认
            BannerSize.Small => (360, 560, 32, new Thickness(20, 16, 20, 16), new CornerRadius(12), 24, 14, 11),
            // 中
            BannerSize.Medium => (480, 720, 40, new Thickness(28, 22, 28, 22), new CornerRadius(16), 30, 16, 12),
            // 大
            BannerSize.Large => (640, 960, 52, new Thickness(36, 28, 36, 28), new CornerRadius(20), 40, 20, 14),
            // 超大：原"小"的两倍
            BannerSize.XLarge => (720, 1280, 64, new Thickness(40, 30, 40, 30), new CornerRadius(24), 50, 22, 15),
            _ => (360, 560, 32, new Thickness(20, 16, 20, 16), new CornerRadius(12), 24, 14, 11)
        };
    }

    /// <summary>
    /// 获取消息正文字号（独立于尺寸）
    /// </summary>
    private (double messageFontSize, double lineHeight) GetFontSizeParams(BannerFontSize fontSize)
    {
        return fontSize switch
        {
            BannerFontSize.Small => (16.0, 22.0),
            BannerFontSize.Medium => (20.0, 28.0),
            BannerFontSize.Large => (26.0, 36.0),
            BannerFontSize.XLarge => (32.0, 44.0),
            _ => (20.0, 28.0)
        };
    }

    /// <summary>
    /// 创建横幅窗口
    /// </summary>
    private Window CreateBannerWindow(string title, string message, string sender, BannerSize size, BannerFontSize fontSize, bool lockWindow, bool isPersistent)
    {
        var (minW, maxW, iconSz, padding, cornerRadius, boxBlur, titleFs, senderFs) = GetSizeParams(size);
        var (msgFs, lineHeight) = GetFontSizeParams(fontSize);

        var window = new Window
        {
            Title = "集控消息",
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowActivated = false,
            Focusable = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        // 主容器 - 半透明白色磨砂背景
        var mainBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 30, 41, 59)),  // 深蓝色半透明
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 96, 165, 250)),
            BorderThickness = new Thickness(2),
            CornerRadius = cornerRadius,
            Padding = padding,
            MinWidth = minW,
            MaxWidth = maxW,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 6,
                Blur = boxBlur,
                Color = Color.FromArgb(150, 0, 0, 0)
            })
        };

        // 顶部标题栏
        var titleBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 0, 0, 8)
        };

        // 图标
        var iconBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 59, 130, 246)),
            CornerRadius = new CornerRadius(iconSz / 2),
            Width = iconSz,
            Height = iconSz,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = "💬",
                FontSize = iconSz * 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconBorder, 0);
        titleBar.Children.Add(iconBorder);

        // 标题文字
        var titleStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2
        };

        var titleText = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = titleFs,
            FontWeight = FontWeight.Bold
        };

        var senderText = new TextBlock
        {
            Text = string.IsNullOrEmpty(sender) ? "集控端" : $"来自 {sender}",
            Foreground = new SolidColorBrush(Color.FromArgb(200, 147, 197, 253)),
            FontSize = senderFs
        };

        titleStack.Children.Add(titleText);
        titleStack.Children.Add(senderText);
        Grid.SetColumn(titleStack, 1);
        titleBar.Children.Add(titleStack);

        // 关闭按钮
        // 持久模式下：默认禁用，60 秒后启用（若 lockWindow=false）
        // 非持久 + lockWindow=true：禁用
        // 其他情况：可用
        bool closeButtonEnabled;
        string? closeButtonTooltip = null;
        if (isPersistent && lockWindow)
        {
            closeButtonEnabled = false;
            closeButtonTooltip = "通知期间不可关闭";
        }
        else if (isPersistent)
        {
            closeButtonEnabled = false;
            closeButtonTooltip = $"{PERSISTENT_CLOSE_UNLOCK_SECONDS} 秒后可关闭";
        }
        else if (lockWindow)
        {
            closeButtonEnabled = false;
            closeButtonTooltip = "通知期间不可关闭";
        }
        else
        {
            closeButtonEnabled = true;
        }

        var closeButton = new Button
        {
            Content = "✕",
            FontSize = titleFs,
            FontWeight = FontWeight.Bold,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 200, 200, 200)),
            BorderThickness = new Thickness(0),
            Width = iconSz,
            Height = iconSz,
            CornerRadius = new CornerRadius(iconSz / 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            IsEnabled = closeButtonEnabled
        };
        if (closeButtonTooltip != null)
        {
            ToolTip.SetTip(closeButton, closeButtonTooltip);
        }
        closeButton.Click += (_, _) => CloseCurrentBanner();
        // 悬停效果
        closeButton.PointerEntered += (_, _) =>
        {
            if (closeButton.IsEnabled)
                closeButton.Foreground = new SolidColorBrush(Colors.White);
        };
        closeButton.PointerExited += (_, _) =>
            closeButton.Foreground = new SolidColorBrush(Color.FromArgb(200, 200, 200, 200));
        Grid.SetColumn(closeButton, 2);
        titleBar.Children.Add(closeButton);

        // 消息内容
        var messageText = new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = msgFs,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = lineHeight,
            Margin = new Thickness(0, 4, 0, 0)
        };

        // 持久模式或 lockWindow 模式下，附加一个状态提示条
        if (isPersistent || lockWindow)
        {
            var statusStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var statusIcon = new TextBlock
            {
                Text = "🔒",
                FontSize = senderFs,
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromArgb(200, 252, 211, 77)),
                FontSize = senderFs,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (isPersistent && lockWindow)
            {
                statusText.Text = "持久通知 · 通知期间不可关闭";
            }
            else if (isPersistent)
            {
                statusText.Text = $"持久通知 · {PERSISTENT_CLOSE_UNLOCK_SECONDS} 秒后可手动关闭";
            }
            else
            {
                statusText.Text = "通知期间不可关闭";
            }
            statusStack.Children.Add(statusIcon);
            statusStack.Children.Add(statusText);

            // 内容布局
            var mainStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 0
            };
            mainStack.Children.Add(titleBar);
            mainStack.Children.Add(messageText);
            mainStack.Children.Add(statusStack);

            mainBorder.Child = mainStack;
            window.Content = mainBorder;

            // 非持久 + lockWindow=true：始终不响应点击关闭
            // 持久模式：不在创建时注册，由 RefreshCloseButtonState 在 60 秒后注册
            if (!isPersistent && !lockWindow)
            {
                window.PointerPressed += (_, _) => CloseCurrentBanner();
            }
        }
        else
        {
            // 内容布局
            var mainStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 0
            };
            mainStack.Children.Add(titleBar);
            mainStack.Children.Add(messageText);

            mainBorder.Child = mainStack;
            window.Content = mainBorder;

            // 点击窗口任意位置关闭
            window.PointerPressed += (_, _) => CloseCurrentBanner();
        }

        return window;
    }

    /// <summary>
    /// 计算并设置横幅位置（屏幕顶部居中，使用 MinWidth 近似）
    /// 在 Show() 之前调用作为初始位置；真正的居中由 CenterBannerAtTop 在 Opened 事件中完成
    /// </summary>
    private void CalculateAndSetPosition(Window window, BannerSize size)
    {
        Screen? screen = ResolvePrimaryScreen();
        if (screen == null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var wa = screen.WorkingArea;
        var (minW, _, _, _, _, _, _, _) = GetSizeParams(size);
        window.Position = new PixelPoint(
            wa.X + (int)((wa.Width - minW) / 2),
            wa.Y + 40
        );
    }

    /// <summary>
    /// 在 Opened 事件中按窗口实际 Bounds 重新居中（顶部居中）
    /// 解决 SizeToContent + 不同尺寸/最大宽度 不居中的问题
    /// </summary>
    private void CenterBannerAtTop(Window window)
    {
        try
        {
            var screen = ResolvePrimaryScreen();
            if (screen == null) return;

            var wa = screen.WorkingArea;
            var bounds = window.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            window.Position = new PixelPoint(
                wa.X + (int)((wa.Width - bounds.Width) / 2),
                wa.Y + 40
            );
        }
        catch { }
    }

    /// <summary>
    /// 获取主屏幕（优先使用主窗口所在屏幕）
    /// </summary>
    private Screen? ResolvePrimaryScreen()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            var mainWindow = desktop.MainWindow;
            return mainWindow.Screens?.ScreenFromPoint(mainWindow.Position)
                   ?? mainWindow.Screens?.Primary;
        }
        return null;
    }

    /// <summary>
    /// 淡入动画
    /// </summary>
    private async Task PlayFadeInAsync(Window window)
    {
        try
        {
            // 加上滑入效果
            var startY = window.Position.Y - 20;
            var targetY = window.Position.Y;
            window.Position = new PixelPoint(window.Position.X, startY);

            var duration = TimeSpan.FromMilliseconds(300);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < duration)
            {
                var progress = (DateTime.UtcNow - startTime).TotalMilliseconds / duration.TotalMilliseconds;
                var eased = 1 - Math.Pow(1 - progress, 3); // easeOutCubic

                window.Opacity = eased;
                window.Position = new PixelPoint(
                    window.Position.X,
                    (int)(startY + (targetY - startY) * eased)
                );
                await Task.Delay(16);
            }

            window.Opacity = 1;
            window.Position = new PixelPoint(window.Position.X, targetY);
        }
        catch { }
    }

    /// <summary>
    /// 自动关闭
    /// </summary>
    private async Task AutoCloseAsync(int duration, CancellationToken token)
    {
        try
        {
            await Task.Delay(duration, token);
            if (!token.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(CloseCurrentBanner);
            }
        }
        catch (TaskCanceledException) { }
    }

    /// <summary>
    /// 关闭当前横幅
    /// </summary>
    public void CloseCurrentBanner()
    {
        try
        {
            _autoCloseCts?.Cancel();
            _autoCloseCts?.Dispose();
            _autoCloseCts = null;

            // 停止持久模式的关闭按钮刷新定时器
            if (_closeButtonRefreshTimer != null)
            {
                _closeButtonRefreshTimer.Stop();
                _closeButtonRefreshTimer = null;
            }

            _isLockWindow = false;
            _isPersistent = false;

            if (_currentBanner != null)
            {
                _ = PlayFadeOutAndCloseAsync(_currentBanner);
                _currentBanner = null;
            }
        }
        catch { }
    }

    /// <summary>
    /// 淡出动画并关闭
    /// </summary>
    private async Task PlayFadeOutAndCloseAsync(Window window)
    {
        try
        {
            var duration = TimeSpan.FromMilliseconds(200);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < duration && window.IsVisible)
            {
                var progress = (DateTime.UtcNow - startTime).TotalMilliseconds / duration.TotalMilliseconds;
                window.Opacity = 1 - progress;
                await Task.Delay(16);
            }

            window.Close();
        }
        catch { }
    }
}
