using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.Platform;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

/// <summary>
/// 通知服务
/// </summary>
public class NotificationService : IDisposable
{
    private static NotificationService? _instance;
    private readonly LocalizationService _localizationService;
    private bool _notificationsEnabled = true;
    private bool _disposed = false;
    private readonly object _notificationGate = new();
    private CancellationTokenSource? _activeNotificationCts;
    private Task _activeNotificationTask = Task.CompletedTask;
    private string? _lastNotificationKey;
    private DateTimeOffset _lastNotificationAt;

    /// <summary>
    /// 获取通知服务实例
    /// </summary>
    public static NotificationService Instance => _instance ??= new NotificationService();

    /// <summary>
    /// 启用或禁用通知
    /// </summary>
    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => _notificationsEnabled = value;
    }

    /// <summary>
    /// 是否正在显示通知
    /// </summary>
    public bool IsShowingNotification
    {
        get
        {
            lock (_notificationGate)
            {
                return _activeNotificationTask != null && !_activeNotificationTask.IsCompleted;
            }
        }
    }

    private NotificationService()
    {
        _localizationService = LocalizationService.Instance;
        
        // 从设置中初始化通知状态
        try
        {
            var settings = SettingsService.General;
            _notificationsEnabled = settings?.ShowNotifications ?? true;
        }
        catch
        {
            _notificationsEnabled = true;
        }
    }

    /// <summary>
    /// 尝试将文本复制到系统剪贴板
    /// </summary>
    /// <param name="text">要复制的文本</param>
    /// <returns>是否复制成功</returns>
    public async Task<bool> TrySetClipboardTextAsync(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                var clipboard = mainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// 获取本地化字符串
    /// </summary>
    /// <param name="key">资源键</param>
    /// <returns>本地化字符串</returns>
    private string GetLocalizedString(string key)
    {
        return LocalizationService.Instance.GetString(key);
    }

    /// <summary>
    /// 显示信息通知
    /// </summary>
    /// <param name="message">通知消息</param>
    public void ShowInfo(string message)
    {
        ShowNotification("Info", _localizationService.GetString("Notification_Info"), message);
    }

    /// <summary>
    /// 显示成功通知
    /// </summary>
    /// <param name="message">通知消息</param>
    public void ShowSuccess(string message)
    {
        ShowNotification("Success", _localizationService.GetString("Notification_Success"), message);
    }

    /// <summary>
    /// 显示警告通知
    /// </summary>
    /// <param name="message">通知消息</param>
    /// <param name="force">是否强制显示（忽略设置中的开关）</param>
    public void ShowWarning(string message, bool force = false)
    {
        ShowNotification("Warning", _localizationService.GetString("Notification_Warning"), message, force);
    }

    /// <summary>
    /// 显示错误通知
    /// </summary>
    /// <param name="message">通知消息</param>
    /// <param name="force">是否强制显示（忽略设置中的开关）</param>
    public void ShowError(string message, bool force = false)
    {
        ShowNotification("Error", _localizationService.GetString("Notification_Error"), message, force);
    }

    /// <summary>
    /// 显示信息通知
    /// </summary>
    /// <param name="title">通知标题或资源键</param>
    /// <param name="message">通知消息或资源键</param>
    /// <param name="duration">显示持续时间（毫秒），默认为3000ms</param>
    public async Task ShowInfoAsync(string title, string message, int duration = 3000)
    {
        if (!_notificationsEnabled)
        {
            // 临时启用通知，确保集控消息一定能显示
            _notificationsEnabled = true;
        }

        var localizedTitle = GetLocalizedString(title);
        var localizedMessage = GetLocalizedString(message);

        await ShowNotificationAsync(localizedTitle, localizedMessage, "Info", duration, force: true);
    }
    
    /// <summary>
    /// 显示信息通知（仅标题）
    /// </summary>
    /// <param name="title">通知标题或资源键</param>
    /// <param name="duration">显示持续时间（毫秒），默认为3000ms</param>
    public async Task ShowInfoAsync(string title, int duration = 3000)
    {
        await ShowInfoAsync(title, string.Empty, duration);
    }
    
    /// <summary>
    /// 显示成功通知
    /// </summary>
    /// <param name="title">通知标题或资源键</param>
    /// <param name="message">通知消息或资源键</param>
    /// <param name="duration">显示持续时间（毫秒），默认为3000ms</param>
    public async Task ShowSuccessAsync(string title, string message, int duration = 3000)
    {
        if (!_notificationsEnabled) return;
        
        var localizedTitle = GetLocalizedString(title);
        var localizedMessage = GetLocalizedString(message);
        
        await ShowNotificationAsync(localizedTitle, localizedMessage, "Success", duration);
    }
    
    /// <summary>
    /// 显示成功通知（仅标题）
    /// </summary>
    /// <param name="title">通知标题或资源键</param>
    /// <param name="duration">显示持续时间（毫秒），默认为3000ms</param>
    public async Task ShowSuccessAsync(string title, int duration = 3000)
    {
        await ShowSuccessAsync(title, string.Empty, duration);
    }
    
    /// <summary>
    /// 显示警告通知
    /// </summary>
    /// <param name="title">通知标题或资源键</param>
    /// <param name="message">通知消息或资源键</param>
    /// <param name="duration">显示持续时间（毫秒），默认为5000ms</param>
    public async Task ShowWarningAsync(string title, string message, int duration = 5000)
    {
        if (!_notificationsEnabled) return;
        
        var localizedTitle = GetLocalizedString(title);
        var localizedMessage = GetLocalizedString(message);
        
        await ShowNotificationAsync(localizedTitle, localizedMessage, "Warning", duration);
    }
    
    /// <summary>
    /// 显示警告通知（仅标题）
    /// </summary>
    /// <param name="title">通知标题或资源键</param>
    /// <param name="duration">显示持续时间（毫秒），默认为5000ms</param>
    public async Task ShowWarningAsync(string title, int duration = 5000)
    {
        await ShowWarningAsync(title, string.Empty, duration);
    }
    
    /// <summary>
    /// 显示错误通知
    /// </summary>
    /// <param name="title">通知标题或资源键</param>
    /// <param name="message">通知消息或资源键</param>
    /// <param name="duration">显示持续时间（毫秒），默认为5000ms</param>
    public async Task ShowErrorAsync(string title, string message, int duration = 5000)
    {
        if (!_notificationsEnabled) return;
        
        var localizedTitle = GetLocalizedString(title);
        var localizedMessage = GetLocalizedString(message);
        
        await ShowNotificationAsync(localizedTitle, localizedMessage, "Error", duration);
    }
    
    /// <summary>
    /// 显示错误通知（仅标题）
    /// </summary>
    /// <param name="title">通知标题或资源键</param>
    /// <param name="duration">显示持续时间（毫秒），默认为5000ms</param>
    public async Task ShowErrorAsync(string title, int duration = 5000)
    {
        await ShowErrorAsync(title, string.Empty, duration);
    }

    #region ShowConfirmAsync 重构方法

    /// <summary>
    /// 显示确认对话框
    /// </summary>
    /// <param name="message">确认消息</param>
    /// <param name="title">对话框标题</param>
    /// <returns>用户是否确认</returns>
    public async Task<bool> ShowConfirmAsync(string message, string title)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();
        var owner = desktop.MainWindow;
        var isDarkMode = SettingsService.General.DarkMode;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = CreateConfirmWindow(title, owner, isDarkMode);
            var grid = CreateConfirmGrid();
            var textBlock = CreateConfirmTextBlock(message, isDarkMode);
            var buttonPanel = CreateConfirmButtonPanel();
            SetupConfirmButtons(buttonPanel, tcs, window, isDarkMode);

            Grid.SetRow(textBlock, 0);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(textBlock);
            grid.Children.Add(buttonPanel);
            window.Content = grid;

            window.Closed += (_, _) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(false);
                }
            };

            if (owner != null)
                window.Show(owner);
            else
                window.Show();
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 创建确认窗口
    /// </summary>
    private Window CreateConfirmWindow(string title, Window? owner, bool isDarkMode)
    {
        var window = new Window
        {
            Title = title,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            SystemDecorations = SystemDecorations.BorderOnly,
            Background = isDarkMode ? new SolidColorBrush(Color.Parse("#252525")) : new SolidColorBrush(Colors.White)
        };

        if (isDarkMode)
        {
            window.Classes.Add("dark");
        }

        if (owner != null)
        {
            window.Icon = owner.Icon;
        }

        return window;
    }

    /// <summary>
    /// 创建确认对话框的网格布局
    /// </summary>
    private Grid CreateConfirmGrid()
    {
        return new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };
    }

    /// <summary>
    /// 创建确认对话框的文本块
    /// </summary>
    private TextBlock CreateConfirmTextBlock(string message, bool isDarkMode)
    {
        return new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap,
            Foreground = isDarkMode ? Brushes.White : Brushes.Black
        };
    }

    /// <summary>
    /// 创建确认对话框的按钮面板
    /// </summary>
    private StackPanel CreateConfirmButtonPanel()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
    }

    /// <summary>
    /// 设置确认对话框的按钮
    /// </summary>
    private void SetupConfirmButtons(StackPanel buttonPanel, TaskCompletionSource<bool> tcs, Window window, bool isDarkMode)
    {
        var cancelButton = new Button
        {
            Content = GetLocalizedString("Btn_Cancel"),
            MinWidth = 80,
            Background = isDarkMode ? new SolidColorBrush(Color.Parse("#3A3A3A")) : new SolidColorBrush(Color.Parse("#E0E0E0")),
            Foreground = isDarkMode ? Brushes.White : Brushes.Black,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8)
        };

        var okButton = new Button
        {
            Content = GetLocalizedString("Btn_Save"),
            MinWidth = 80,
            Background = new SolidColorBrush(Color.Parse("#0078D4")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8)
        };

        cancelButton.Click += (_, _) =>
        {
            tcs.TrySetResult(false);
            window.Close();
        };

        okButton.Click += (_, _) =>
        {
            tcs.TrySetResult(true);
            window.Close();
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);
    }

    #endregion

    /// <summary>
    /// 显示通知
    /// </summary>
    /// <param name="type">通知类型</param>
    /// <param name="title">通知标题</param>
    /// <param name="message">通知消息</param>
    /// <param name="force">是否强制显示</param>
    private void ShowNotification(string type, string title, string message, bool force = false)
    {
        // 检查通知设置
        if (!force && !SettingsService.General.ShowNotifications)
            return;

        // 使用默认持续时间（3000ms）
        var duration = 3000;

        // 显示通知
        _ = ShowNotificationAsync(title, message, type, duration, force);
    }

    /// <summary>
    /// 显示通知
    /// </summary>
    /// <param name="title">通知标题</param>
    /// <param name="message">通知消息</param>
    /// <param name="type">通知类型</param>
    /// <param name="duration">显示持续时间（毫秒）</param>
    /// <param name="force">是否强制显示</param>
    private async Task ShowNotificationAsync(string title, string message, string type, int duration, bool force = false)
    {
        if (_disposed) return;
        if (!force && !_notificationsEnabled) return;

        var key = $"{type}\n{title}\n{message}";

        NotificationWindowContext? context = null;
        CancellationTokenSource? ctsToDispose = null;
        Task taskToWait = Task.CompletedTask;

        lock (_notificationGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastNotificationKey == key && (now - _lastNotificationAt) < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            _lastNotificationKey = key;
            _lastNotificationAt = now;

            if (_activeNotificationCts != null)
            {
                _activeNotificationCts.Cancel();
                ctsToDispose = _activeNotificationCts;
                taskToWait = _activeNotificationTask;
            }

            _activeNotificationCts = new CancellationTokenSource();
            context = new NotificationWindowContext();
            context.Cts = _activeNotificationCts;

            _activeNotificationTask = ShowNotificationCoreAsync(
                title, message, type, duration, _activeNotificationCts.Token, context);
        }

        if (ctsToDispose != null)
        {
            _ = taskToWait.ContinueWith(_ => ctsToDispose.Dispose(), TaskScheduler.Default);
        }

        try
        {
            await _activeNotificationTask;
        }
        catch
        {
            // 忽略通知任务中的异常
        }
    }

    #region ShowNotificationCoreAsync 重构方法

    /// <summary>
    /// 通知窗口创建结果
    /// </summary>
    private class NotificationWindowContext
    {
        public Window? Window { get; set; }
        public Button? CloseButton { get; set; }
        public Border? RootBorder { get; set; }
        public EventHandler<RoutedEventArgs>? CloseHandler { get; set; }
        public EventHandler<PointerEventArgs>? PointerEnteredHandler { get; set; }
        public EventHandler<PointerEventArgs>? PointerExitedHandler { get; set; }
        public bool IsDarkMode { get; set; }
        public NotificationColors? Colors { get; set; }
        public CancellationTokenSource? Cts { get; set; }
    }

    /// <summary>
    /// 通知颜色配置
    /// </summary>
    private class NotificationColors
    {
        // 强调色 - 用于左侧条、图标、标题点缀
        public Color AccentColor { get; set; } = Color.FromRgb(33, 150, 243);
        public Color AccentColorLight { get; set; } = Color.FromRgb(100, 180, 255);

        // 背景色 - 渐变
        public Color BackgroundStart { get; set; } = Color.FromRgb(255, 255, 255);
        public Color BackgroundEnd { get; set; } = Color.FromRgb(248, 250, 252);

        // 文字色
        public Color TitleColor { get; set; } = Color.FromRgb(15, 23, 42);
        public Color MessageColor { get; set; } = Color.FromRgb(71, 85, 105);
        public Color CloseButtonColor { get; set; } = Color.FromRgb(148, 163, 184);
        public Color CloseButtonHoverColor { get; set; } = Color.FromRgb(239, 68, 68);

        // 边框/分隔线
        public Color BorderColor { get; set; } = Color.FromRgb(226, 232, 240);

        // 图标容器背景
        public Color IconBackgroundStart { get; set; } = Color.FromRgb(240, 245, 255);
        public Color IconBackgroundEnd { get; set; } = Color.FromRgb(224, 239, 255);

        // 阴影颜色
        public Color ShadowColor { get; set; } = Color.FromArgb(60, 0, 0, 0);
    }

    /// <summary>
    /// 显示通知核心逻辑
    /// </summary>
    private async Task ShowNotificationCoreAsync(string title, string message, string type, int duration, CancellationToken cancellationToken, NotificationWindowContext context)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;

                context.IsDarkMode = IsDarkMode();
                var colors = CreateNotificationColors(type, context.IsDarkMode);
                context.Colors = colors;

                context.Window = CreateNotificationWindow(title, colors);
                var content = CreateNotificationContent(title, message, type, colors, context);
                context.RootBorder = content;
                context.Window.Content = content;

                context.Window.Show();
                CalculateAndSetNotificationPosition(context.Window);

                // 出现动画 - 透明度淡入 + 缩放（在 UI 线程内同步执行）
                try
                {
                    context.Window.Opacity = 0;
                    context.Window.RenderTransform = new ScaleTransform(0.92, 0.92);

                    var animation = new Animation
                    {
                        Duration = TimeSpan.FromMilliseconds(220),
                        Easing = new CubicEaseOut(),
                        FillMode = FillMode.Forward,
                        Children =
                        {
                            new KeyFrame
                            {
                                Cue = new Cue(0),
                                Setters = { new Setter(Window.OpacityProperty, 0d) }
                            },
                            new KeyFrame
                            {
                                Cue = new Cue(1),
                                Setters = { new Setter(Window.OpacityProperty, 1d) }
                            }
                        }
                    };
                    _ = animation.RunAsync(context.Window);

                    var transformAnimation = new Animation
                    {
                        Duration = TimeSpan.FromMilliseconds(220),
                        Easing = new CubicEaseOut(),
                        FillMode = FillMode.Forward,
                        Children =
                        {
                            new KeyFrame
                            {
                                Cue = new Cue(0),
                                Setters = { new Setter(Visual.RenderTransformProperty, new ScaleTransform(0.92, 0.92)) }
                            },
                            new KeyFrame
                            {
                                Cue = new Cue(1),
                                Setters = { new Setter(Visual.RenderTransformProperty, new ScaleTransform(1.0, 1.0)) }
                            }
                        }
                    };
                    _ = transformAnimation.RunAsync(context.Window);
                }
                catch
                {
                    // 动画失败时直接显示
                    if (context.Window != null)
                    {
                        context.Window.Opacity = 1;
                        context.Window.RenderTransform = new ScaleTransform(1.0, 1.0);
                    }
                }
            });

            await HandleNotificationAutoClose(duration, cancellationToken, context);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"显示通知失败: {ex.Message}");
        }
        finally
        {
            await CleanupNotificationWindow(context);
        }
    }

    /// <summary>
    /// 判断当前是否为深色模式
    /// </summary>
    private bool IsDarkMode()
    {
        var currentTheme = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
        return currentTheme == ThemeVariant.Dark;
    }

    /// <summary>
    /// 创建通知颜色配置
    /// </summary>
    private NotificationColors CreateNotificationColors(string type, bool isDarkMode)
    {
        // 每种类型使用鲜明的语义化色板，提升辨析度
        if (isDarkMode)
        {
            return type switch
            {
                "Success" => new NotificationColors
                {
                    AccentColor = Color.FromRgb(34, 197, 94),
                    AccentColorLight = Color.FromRgb(74, 222, 128),
                    BackgroundStart = Color.FromRgb(30, 41, 59),
                    BackgroundEnd = Color.FromRgb(15, 23, 42),
                    TitleColor = Color.FromRgb(240, 253, 244),
                    MessageColor = Color.FromRgb(203, 213, 225),
                    CloseButtonColor = Color.FromRgb(148, 163, 184),
                    CloseButtonHoverColor = Color.FromRgb(248, 113, 113),
                    BorderColor = Color.FromRgb(51, 65, 85),
                    IconBackgroundStart = Color.FromArgb(80, 34, 197, 94),
                    IconBackgroundEnd = Color.FromArgb(40, 34, 197, 94),
                    ShadowColor = Color.FromArgb(120, 0, 0, 0)
                },
                "Warning" => new NotificationColors
                {
                    AccentColor = Color.FromRgb(245, 158, 11),
                    AccentColorLight = Color.FromRgb(251, 191, 36),
                    BackgroundStart = Color.FromRgb(30, 41, 59),
                    BackgroundEnd = Color.FromRgb(15, 23, 42),
                    TitleColor = Color.FromRgb(254, 243, 199),
                    MessageColor = Color.FromRgb(203, 213, 225),
                    CloseButtonColor = Color.FromRgb(148, 163, 184),
                    CloseButtonHoverColor = Color.FromRgb(248, 113, 113),
                    BorderColor = Color.FromRgb(51, 65, 85),
                    IconBackgroundStart = Color.FromArgb(80, 245, 158, 11),
                    IconBackgroundEnd = Color.FromArgb(40, 245, 158, 11),
                    ShadowColor = Color.FromArgb(120, 0, 0, 0)
                },
                "Error" => new NotificationColors
                {
                    AccentColor = Color.FromRgb(239, 68, 68),
                    AccentColorLight = Color.FromRgb(248, 113, 113),
                    BackgroundStart = Color.FromRgb(30, 41, 59),
                    BackgroundEnd = Color.FromRgb(15, 23, 42),
                    TitleColor = Color.FromRgb(254, 226, 226),
                    MessageColor = Color.FromRgb(203, 213, 225),
                    CloseButtonColor = Color.FromRgb(148, 163, 184),
                    CloseButtonHoverColor = Color.FromRgb(248, 113, 113),
                    BorderColor = Color.FromRgb(51, 65, 85),
                    IconBackgroundStart = Color.FromArgb(80, 239, 68, 68),
                    IconBackgroundEnd = Color.FromArgb(40, 239, 68, 68),
                    ShadowColor = Color.FromArgb(120, 0, 0, 0)
                },
                _ => new NotificationColors
                {
                    AccentColor = Color.FromRgb(59, 130, 246),
                    AccentColorLight = Color.FromRgb(96, 165, 250),
                    BackgroundStart = Color.FromRgb(30, 41, 59),
                    BackgroundEnd = Color.FromRgb(15, 23, 42),
                    TitleColor = Color.FromRgb(219, 234, 254),
                    MessageColor = Color.FromRgb(203, 213, 225),
                    CloseButtonColor = Color.FromRgb(148, 163, 184),
                    CloseButtonHoverColor = Color.FromRgb(248, 113, 113),
                    BorderColor = Color.FromRgb(51, 65, 85),
                    IconBackgroundStart = Color.FromArgb(80, 59, 130, 246),
                    IconBackgroundEnd = Color.FromArgb(40, 59, 130, 246),
                    ShadowColor = Color.FromArgb(120, 0, 0, 0)
                }
            };
        }

        // 浅色模式 - 鲜明的语义色 + 柔和的背景
        return type switch
        {
            "Success" => new NotificationColors
            {
                AccentColor = Color.FromRgb(22, 163, 74),
                AccentColorLight = Color.FromRgb(34, 197, 94),
                BackgroundStart = Color.FromRgb(255, 255, 255),
                BackgroundEnd = Color.FromRgb(240, 253, 244),
                TitleColor = Color.FromRgb(20, 83, 45),
                MessageColor = Color.FromRgb(55, 65, 81),
                CloseButtonColor = Color.FromRgb(148, 163, 184),
                CloseButtonHoverColor = Color.FromRgb(220, 38, 38),
                BorderColor = Color.FromRgb(220, 252, 231),
                IconBackgroundStart = Color.FromRgb(220, 252, 231),
                IconBackgroundEnd = Color.FromRgb(187, 247, 208),
                ShadowColor = Color.FromArgb(45, 22, 163, 74)
            },
            "Warning" => new NotificationColors
            {
                AccentColor = Color.FromRgb(217, 119, 6),
                AccentColorLight = Color.FromRgb(245, 158, 11),
                BackgroundStart = Color.FromRgb(255, 255, 255),
                BackgroundEnd = Color.FromRgb(255, 251, 235),
                TitleColor = Color.FromRgb(120, 53, 15),
                MessageColor = Color.FromRgb(55, 65, 81),
                CloseButtonColor = Color.FromRgb(148, 163, 184),
                CloseButtonHoverColor = Color.FromRgb(220, 38, 38),
                BorderColor = Color.FromRgb(254, 243, 199),
                IconBackgroundStart = Color.FromRgb(254, 243, 199),
                IconBackgroundEnd = Color.FromRgb(253, 230, 138),
                ShadowColor = Color.FromArgb(45, 217, 119, 6)
            },
            "Error" => new NotificationColors
            {
                AccentColor = Color.FromRgb(220, 38, 38),
                AccentColorLight = Color.FromRgb(239, 68, 68),
                BackgroundStart = Color.FromRgb(255, 255, 255),
                BackgroundEnd = Color.FromRgb(254, 242, 242),
                TitleColor = Color.FromRgb(127, 29, 29),
                MessageColor = Color.FromRgb(55, 65, 81),
                CloseButtonColor = Color.FromRgb(148, 163, 184),
                CloseButtonHoverColor = Color.FromRgb(220, 38, 38),
                BorderColor = Color.FromRgb(254, 226, 226),
                IconBackgroundStart = Color.FromRgb(254, 226, 226),
                IconBackgroundEnd = Color.FromRgb(252, 165, 165),
                ShadowColor = Color.FromArgb(45, 220, 38, 38)
            },
            _ => new NotificationColors
            {
                AccentColor = Color.FromRgb(37, 99, 235),
                AccentColorLight = Color.FromRgb(59, 130, 246),
                BackgroundStart = Color.FromRgb(255, 255, 255),
                BackgroundEnd = Color.FromRgb(239, 246, 255),
                TitleColor = Color.FromRgb(30, 58, 138),
                MessageColor = Color.FromRgb(55, 65, 81),
                CloseButtonColor = Color.FromRgb(148, 163, 184),
                CloseButtonHoverColor = Color.FromRgb(220, 38, 38),
                BorderColor = Color.FromRgb(219, 234, 254),
                IconBackgroundStart = Color.FromRgb(219, 234, 254),
                IconBackgroundEnd = Color.FromRgb(191, 219, 254),
                ShadowColor = Color.FromArgb(45, 37, 99, 235)
            }
        };
    }

    /// <summary>
    /// 根据通知类型获取边框颜色
    /// </summary>
    private SolidColorBrush GetBorderBrush(string type)
    {
        return type switch
        {
            "Success" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
            "Warning" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
            "Error" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
            _ => new SolidColorBrush(Color.FromRgb(33, 150, 243))
        };
    }

    /// <summary>
    /// 根据通知类型获取图标颜色
    /// </summary>
    private SolidColorBrush GetIconBrush(string type)
    {
        return type switch
        {
            "Success" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
            "Warning" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
            "Error" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
            _ => new SolidColorBrush(Color.FromRgb(33, 150, 243))
        };
    }

    /// <summary>
    /// 创建通知窗口
    /// </summary>
    private Window CreateNotificationWindow(string title, NotificationColors colors)
    {
        return new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = (SettingsService.General.NotificationPosition == Models.NotificationPosition.Center)
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.Manual,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(colors.TitleColor),
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
            ShowActivated = false,
            Focusable = false,
            Opacity = 1
        };
    }

    /// <summary>
    /// 创建通知内容
    /// </summary>
    private Border CreateNotificationContent(string title, string message, string type, NotificationColors colors, NotificationWindowContext context)
    {
        // 根容器 - 渐变背景 + 柔和阴影 + 圆角
        var rootBorder = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(colors.BackgroundStart, 0),
                    new GradientStop(colors.BackgroundEnd, 1)
                }
            },
            BorderBrush = new SolidColorBrush(colors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(0, 16, 0, 16),
            Margin = new Thickness(12),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 6,
                Blur = 24,
                Spread = 0,
                Color = colors.ShadowColor
            })
        };

        var mainGrid = CreateMainGrid();
        AddAccentBorder(mainGrid, colors);
        AddIconContainer(mainGrid, type, colors, context);
        AddTitleTextBlock(mainGrid, title, colors);
        AddMessageTextBlock(mainGrid, message, colors);
        AddCloseButton(mainGrid, context);

        rootBorder.Child = mainGrid;
        return rootBorder;
    }

    /// <summary>
    /// 创建主网格
    /// </summary>
    private Grid CreateMainGrid()
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("6,Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(0)
        };
    }

    /// <summary>
    /// 添加强调边框（左侧发光条）
    /// </summary>
    private void AddAccentBorder(Grid mainGrid, NotificationColors colors)
    {
        // 左侧强调条 - 圆角 + 渐变
        var accentBorder = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(colors.AccentColor, 0),
                    new GradientStop(colors.AccentColorLight, 1)
                }
            },
            CornerRadius = new CornerRadius(14, 0, 0, 14),
            Width = 6
        };
        Grid.SetColumn(accentBorder, 0);
        Grid.SetRowSpan(accentBorder, 2);
        mainGrid.Children.Add(accentBorder);
    }

    /// <summary>
    /// 添加图标容器 - 渐变背景 + 几何图标
    /// </summary>
    private void AddIconContainer(Grid mainGrid, string type, NotificationColors colors, NotificationWindowContext context)
    {
        // 图标背景 - 渐变 + 阴影
        var iconContainer = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(colors.IconBackgroundStart, 0),
                    new GradientStop(colors.IconBackgroundEnd, 1)
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, colors.AccentColor.R, colors.AccentColor.G, colors.AccentColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(26),
            Width = 52,
            Height = 52,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(18, 0, 16, 0)
        };

        // 使用 Path 绘制几何图标
        var iconPath = CreateNotificationIconPath(type, colors);
        iconContainer.Child = iconPath;

        Grid.SetColumn(iconContainer, 1);
        Grid.SetRowSpan(iconContainer, 2);
        mainGrid.Children.Add(iconContainer);
    }

    /// <summary>
    /// 创建通知图标路径
    /// </summary>
    private Path CreateNotificationIconPath(string type, NotificationColors colors)
    {
        var pathData = type switch
        {
            // 对号 - Success
            "Success" => "M 4 12 L 9 17 L 18 7",
            // 警告三角 - Warning
            "Warning" => "M 12 3 L 22 20 L 2 20 Z",
            // 错号圆圈 - Error
            "Error" => "M 12 2 A 10 10 0 1 0 12 22 A 10 10 0 1 0 12 2 M 8 8 L 16 16 M 16 8 L 8 16",
            // 信息 i - Info
            _ => "M 12 2 A 10 10 0 1 0 12 22 A 10 10 0 1 0 12 2 M 12 8 L 12 8.01 M 12 11 L 12 17"
        };

        var path = new Path
        {
            Data = StreamGeometry.Parse(pathData),
            Stroke = new SolidColorBrush(colors.AccentColor),
            StrokeThickness = 2.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        return path;
    }

    /// <summary>
    /// 添加标题文本块 - 大字号 + 强对比度
    /// </summary>
    private void AddTitleTextBlock(Grid mainGrid, string title, NotificationColors colors)
    {
        var titleTextBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.Bold,
            FontSize = 17,
            Foreground = new SolidColorBrush(colors.TitleColor),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 12, 0)
        };
        Grid.SetColumn(titleTextBlock, 2);
        Grid.SetRow(titleTextBlock, 0);
        mainGrid.Children.Add(titleTextBlock);
    }

    /// <summary>
    /// 添加消息文本块 - 中等字号 + 中等对比度
    /// </summary>
    private void AddMessageTextBlock(Grid mainGrid, string message, NotificationColors colors)
    {
        var messageTextBlock = new TextBlock
        {
            Text = message,
            FontSize = 14,
            FontWeight = FontWeight.Normal,
            LineHeight = 20,
            Foreground = new SolidColorBrush(colors.MessageColor),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 12, 0),
            IsVisible = !string.IsNullOrWhiteSpace(message)
        };
        Grid.SetColumn(messageTextBlock, 2);
        Grid.SetRow(messageTextBlock, 1);
        mainGrid.Children.Add(messageTextBlock);
    }

    /// <summary>
    /// 添加关闭按钮 - 圆形 + hover 状态
    /// </summary>
    private void AddCloseButton(Grid mainGrid, NotificationWindowContext context)
    {
        var colors = context.Colors!;
        var closeButton = new Button
        {
            Content = "×",
            FontSize = 20,
            FontWeight = FontWeight.Normal,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(colors.CloseButtonColor),
            BorderThickness = new Thickness(0),
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 6, 8, 0),
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        context.CloseButton = closeButton;
        context.CloseHandler = (s, e) =>
        {
            if (context.CloseButton != null && context.CloseHandler != null)
            {
                context.CloseButton.Click -= context.CloseHandler;
            }
            context.Window?.Close();
        };
        closeButton.Click += context.CloseHandler;

        context.PointerEnteredHandler = (s, e) =>
        {
            if (context.CloseButton == null) return;
            context.CloseButton.Background = new SolidColorBrush(Color.FromArgb(40, colors.CloseButtonHoverColor.R, colors.CloseButtonHoverColor.G, colors.CloseButtonHoverColor.B));
            context.CloseButton.Foreground = new SolidColorBrush(colors.CloseButtonHoverColor);
        };

        context.PointerExitedHandler = (s, e) =>
        {
            if (context.CloseButton == null) return;
            context.CloseButton.Background = Brushes.Transparent;
            context.CloseButton.Foreground = new SolidColorBrush(colors.CloseButtonColor);
        };

        closeButton.PointerEntered += context.PointerEnteredHandler;
        closeButton.PointerExited += context.PointerExitedHandler;

        Grid.SetColumn(closeButton, 3);
        Grid.SetRow(closeButton, 0);
        mainGrid.Children.Add(closeButton);
    }

    /// <summary>
    /// 计算并设置通知窗口位置
    /// </summary>
    private void CalculateAndSetNotificationPosition(Window window)
    {
        var position = SettingsService.General.NotificationPosition;
        if (position == Models.NotificationPosition.Center)
        {
            return;
        }

        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        var screen = mainWindow != null ? mainWindow.Screens.ScreenFromPoint(mainWindow.Position) : null;
        if (screen == null)
        {
            return;
        }

        var wa = screen.WorkingArea;
        var width = (int)window.Width;
        var height = (int)window.Height;

        var (x, y) = CalculatePosition(position, wa, width, height);
        window.Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// 根据位置设置计算坐标
    /// </summary>
    private (int x, int y) CalculatePosition(NotificationPosition position, PixelRect workingArea, int width, int height)
    {
        var x = workingArea.X + workingArea.Width - width - 24;
        var y = workingArea.Y + 24;

        switch (position)
        {
            case NotificationPosition.TopLeft:
                x = workingArea.X + 24;
                y = workingArea.Y + 24;
                break;
            case NotificationPosition.TopRight:
                x = workingArea.X + workingArea.Width - width - 24;
                y = workingArea.Y + 24;
                break;
            case NotificationPosition.BottomLeft:
                x = workingArea.X + 24;
                y = workingArea.Y + workingArea.Height - height - 24;
                break;
            case NotificationPosition.BottomRight:
                x = workingArea.X + workingArea.Width - width - 24;
                y = workingArea.Y + workingArea.Height - height - 24;
                break;
        }

        return (x, y);
    }

    /// <summary>
    /// 处理通知自动关闭（带淡出动画）
    /// </summary>
    private async Task HandleNotificationAutoClose(int duration, CancellationToken cancellationToken, NotificationWindowContext context)
    {
        if (duration < 250)
        {
            duration = 250;
        }

        try
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);

            // 仅在未被取消的情况下执行淡出动画
            if (!cancellationToken.IsCancellationRequested && context.Window != null)
            {
                var windowRef = context.Window;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (windowRef == null || !windowRef.IsVisible) return;

                    try
                    {
                        var fadeOut = new Animation
                        {
                            Duration = TimeSpan.FromMilliseconds(180),
                            Easing = new CubicEaseIn(),
                            FillMode = FillMode.Forward,
                            Children =
                            {
                                new KeyFrame
                                {
                                    Cue = new Cue(0),
                                    Setters = { new Setter(Window.OpacityProperty, 1d) }
                                },
                                new KeyFrame
                                {
                                    Cue = new Cue(1),
                                    Setters = { new Setter(Window.OpacityProperty, 0d) }
                                }
                            }
                        };
                        await fadeOut.RunAsync(windowRef);
                    }
                    catch
                    {
                        // 忽略淡出动画异常
                    }
                });
            }
        }
        catch (TaskCanceledException)
        {
            // 任务被取消，正常退出
        }
        catch (OperationCanceledException)
        {
            // 任务被取消，正常退出
        }
    }

    /// <summary>
    /// 清理通知窗口资源
    /// </summary>
    private async Task CleanupNotificationWindow(NotificationWindowContext context)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (context.CloseButton != null)
                    {
                        if (context.CloseHandler != null)
                            context.CloseButton.Click -= context.CloseHandler;
                        if (context.PointerEnteredHandler != null)
                            context.CloseButton.PointerEntered -= context.PointerEnteredHandler;
                        if (context.PointerExitedHandler != null)
                            context.CloseButton.PointerExited -= context.PointerExitedHandler;
                    }
                }
                catch
                {
                }

                try
                {
                    context.Window?.Close();
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }

    #endregion

    /// <summary>
    /// 更新通知设置
    /// </summary>
    /// <param name="enabled">是否启用通知</param>
    public void UpdateNotificationSettings(bool enabled)
    {
        _notificationsEnabled = enabled;
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        // 由于NotificationService是单例，这里不释放实例
        // 只标记为已释放状态，防止后续操作
    }
}