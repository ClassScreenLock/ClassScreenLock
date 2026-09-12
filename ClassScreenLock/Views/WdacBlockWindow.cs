using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassScreenLock.Services;

namespace ClassScreenLock.Views;

/// <summary>
/// WDAC 风格阻止提示窗口（由主程序显示）。
///
/// 主程序以 SYSTEM + UIAccess 令牌运行，本窗口处于 UIAccess band（ZBID_UIACCESS），
/// Topmost 后即可真正置顶到一切窗口之上（包括锁屏全屏遮罩），类似真实的
/// Windows Defender Application Control 弹窗，学生无法用普通窗口盖住它。
///
/// 视觉规范（对照 WDAC 弹窗 HTML/CSS）：
///   全屏深色遮罩 rgba(0,0,0,0.5) + 中央蓝色卡片（背景 #005A9E、宽 850、圆角 4、
///   box-shadow 0 4px 16px rgba(0,0,0,0.15)）；标题 21px、正文/路径 15px、
///   白色 1px 描边按钮 + hover 半透明白 rgba(255,255,255,0.1)；
///   路径行显示被拦截应用的完整路径。
/// </summary>
public static class WdacBlockWindow
{
    private static readonly Color WindowsBlue = Color.FromRgb(0x00, 0x5A, 0x9E);
    private static readonly object Gate = new();

    private static Window? _window;
    private static Button? _copyButton;
    private static string _appName = "";
    private static string _appPath = "";

    /// <summary>在 UI 线程显示（或激活已存在的）阻止提示窗口。</summary>
    public static void Show(string appName, string appPath)
    {
        _appName = appName;
        _appPath = appPath;

        lock (Gate)
        {
            if (_window is { IsVisible: true })
            {
                _window.Activate();
                return; // 已有弹窗：忽略重复通知
            }

            _window = BuildWindow();
            _window.Closed += (_, _) =>
            {
                lock (Gate) { _window = null; }
            };
            _window.Show();
        }
    }

    private static Window BuildWindow()
    {
        var window = new Window
        {
            Title = "ClassScreenLock",
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            Focusable = true,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        // 铺满主屏幕（含任务栏区域）作为遮罩层。
        // 注意：无边框 + CanResize=false 时 WindowState.Maximized 在 Win32 上不会真正
        // 铺满，窗口保持默认尺寸，透明部分会露出桌面（白边）——必须手动设置尺寸位置。
        var primaryScreen = window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
        if (primaryScreen != null)
        {
            var bounds = primaryScreen.Bounds; // 物理像素
            window.Width = bounds.Width / primaryScreen.Scaling;   // DIP
            window.Height = bounds.Height / primaryScreen.Scaling; // DIP
            window.Position = new PixelPoint(bounds.X, bounds.Y);  // 物理像素
        }
        else
        {
            window.WindowState = WindowState.Maximized; // 兜底
        }

        var root = new Grid();

        // 全屏深色遮罩：rgba(0,0,0,0.5)
        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
        });

        // 中央蓝色卡片（对应 WDAC 弹窗主体：宽 850、圆角 4、阴影 0 4px 16px rgba(0,0,0,0.15)）
        var card = new Border
        {
            Background = new SolidColorBrush(WindowsBlue),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(40, 35, 40, 35),
            Width = 850,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 4,
                Blur = 16,
                Color = Color.FromArgb(38, 0, 0, 0)
            })
        };

        var panel = new StackPanel();

        // 标题两行（21px，白色）
        panel.Children.Add(MakeText("你的组织使用了 ClassScreenLock 应用程序控制", 21, 0));
        panel.Children.Add(MakeText("来阻止此应用", 21, 5));

        // 被拦截路径（15px，白色，上 20 下 25，长路径自动换行）
        panel.Children.Add(new TextBlock
        {
            Text = _appPath,
            FontSize = 15,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 20, 0, 25)
        });

        // 描述（15px，白色，下 35，与按钮区衔接）
        panel.Children.Add(MakeText("有关详细信息，请与你的支持人员或管理人员联系。", 15, 0, 35));

        // 底部按钮：右对齐，间距 12
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };

        _copyButton = MakeButton("复制到剪贴板", 150);
        _copyButton.Click += OnCopyClicked;

        var closeButton = MakeButton("关闭", 78);
        closeButton.Click += (_, _) => window.Close();

        buttons.Children.Add(_copyButton);
        buttons.Children.Add(closeButton);
        panel.Children.Add(buttons);

        card.Child = panel;
        root.Children.Add(card);
        window.Content = root;

        // ESC 等同"关闭"
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
        };

        return window;
    }

    /// <summary>创建白色文字文本块。</summary>
    private static TextBlock MakeText(string text, double fontSize, double marginTop, double marginBottom = 0)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = Brushes.White,
            Margin = new Thickness(0, marginTop, 0, marginBottom)
        };
    }

    /// <summary>创建 WDAC 风格的白色描边按钮（透明底、hover 半透明白）。</summary>
    private static Button MakeButton(string text, double minWidth)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 15,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            MinWidth = minWidth,
            Height = 36,
            Padding = new Thickness(20, 8, 20, 8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        // hover / 离开：rgba(255,255,255,0.1) 半透明白
        button.PointerEntered += (_, _) =>
            button.Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
        button.PointerExited += (_, _) =>
            button.Background = Brushes.Transparent;
        return button;
    }

    private static void OnCopyClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var info = string.Join(Environment.NewLine,
            $"被阻止的应用：{_appName}",
            $"应用路径：{_appPath}",
            $"阻止时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "",
            "你的组织使用了 ClassScreenLock 应用程序控制来阻止此应用。",
            "有关详细信息，请与你的支持人员或管理人员联系。");

        // SYSTEM 账户下 Avalonia OLE 剪贴板可能失败，用 Win32 直写（与通知服务一致）
        if (!SystemClipboard.SetUnicodeText(info) || _copyButton == null) return;

        _copyButton.Content = "已复制";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            if (_copyButton != null) _copyButton.Content = "复制到剪贴板";
            timer.Stop();
        };
        timer.Start();
    }
}
