using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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

        CancellationTokenSource? ctsToDispose = null;
        Task taskToWait = Task.CompletedTask;
        CancellationToken token;

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
            token = _activeNotificationCts.Token;

            _activeNotificationTask = ShowNotificationCoreAsync(title, message, type, duration, token);
        }

        if (ctsToDispose != null)
        {
            _ = taskToWait.ContinueWith(_ => ctsToDispose.Dispose(), TaskScheduler.Default);
        }

        await _activeNotificationTask;
    }

    #region ShowNotificationCoreAsync 重构方法

    /// <summary>
    /// 通知窗口创建结果
    /// </summary>
    private class NotificationWindowContext
    {
        public Window? Window { get; set; }
        public Button? CloseButton { get; set; }
        public EventHandler<RoutedEventArgs>? CloseHandler { get; set; }
        public EventHandler<PointerEventArgs>? PointerEnteredHandler { get; set; }
        public EventHandler<PointerEventArgs>? PointerExitedHandler { get; set; }
        public SolidColorBrush? CloseButtonForegroundBrush { get; set; }
        public bool IsDarkMode { get; set; }
    }

    /// <summary>
    /// 通知颜色配置
    /// </summary>
    private class NotificationColors
    {
        public SolidColorBrush BackgroundBrush { get; set; } = new SolidColorBrush(Colors.White);
        public SolidColorBrush ForegroundBrush { get; set; } = new SolidColorBrush(Colors.Black);
        public SolidColorBrush ChromeBrush { get; set; } = new SolidColorBrush(Colors.LightGray);
        public SolidColorBrush BorderBrush { get; set; } = new SolidColorBrush(Colors.Blue);
        public SolidColorBrush IconBrush { get; set; } = new SolidColorBrush(Colors.Blue);
        public SolidColorBrush MessageBrush { get; set; } = new SolidColorBrush(Colors.Gray);
        public SolidColorBrush CloseButtonForegroundBrush { get; set; } = new SolidColorBrush(Colors.Gray);
        public SolidColorBrush IconBackgroundBrush { get; set; } = new SolidColorBrush(Colors.WhiteSmoke);
    }

    /// <summary>
    /// 显示通知核心逻辑
    /// </summary>
    private async Task ShowNotificationCoreAsync(string title, string message, string type, int duration, CancellationToken cancellationToken)
    {
        var context = new NotificationWindowContext();

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;

                context.IsDarkMode = IsDarkMode();
                var colors = CreateNotificationColors(type, context.IsDarkMode);
                context.CloseButtonForegroundBrush = colors.CloseButtonForegroundBrush;

                context.Window = CreateNotificationWindow(title, colors);
                var content = CreateNotificationContent(title, message, type, colors, context);
                context.Window.Content = content;

                context.Window.Show();
                CalculateAndSetNotificationPosition(context.Window);
            });

            await HandleNotificationAutoClose(duration, cancellationToken);
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
        return new NotificationColors
        {
            BackgroundBrush = isDarkMode
                ? new SolidColorBrush(Color.FromRgb(32, 32, 32))
                : new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            ForegroundBrush = isDarkMode
                ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                : new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            ChromeBrush = isDarkMode
                ? new SolidColorBrush(Color.FromRgb(58, 58, 58))
                : new SolidColorBrush(Color.FromRgb(230, 230, 230)),
            BorderBrush = GetBorderBrush(type),
            IconBrush = GetIconBrush(type),
            MessageBrush = isDarkMode
                ? new SolidColorBrush(Color.FromRgb(180, 180, 180))
                : new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            CloseButtonForegroundBrush = isDarkMode
                ? new SolidColorBrush(Color.FromRgb(150, 150, 150))
                : new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            IconBackgroundBrush = isDarkMode
                ? new SolidColorBrush(Color.FromRgb(45, 45, 45))
                : new SolidColorBrush(Color.FromRgb(245, 245, 245))
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
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = (SettingsService.General.NotificationPosition == Models.NotificationPosition.Center)
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.Manual,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            Foreground = colors.ForegroundBrush,
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
        var rootBorder = new Border
        {
            Background = colors.BackgroundBrush,
            BorderBrush = colors.ChromeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24, 20, 24, 20),
            MinHeight = 160
        };

        var mainGrid = CreateMainGrid();
        AddAccentBorder(mainGrid, colors.BorderBrush);
        AddIconContainer(mainGrid, type, colors.IconBrush, colors.IconBackgroundBrush);
        AddTitleTextBlock(mainGrid, title, colors.ForegroundBrush);
        AddMessageTextBlock(mainGrid, message, colors.MessageBrush);
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
            ColumnDefinitions = new ColumnDefinitions("6,auto,*,auto"),
            RowDefinitions = new RowDefinitions("auto,auto"),
            MinHeight = 88
        };
    }

    /// <summary>
    /// 添加强调边框
    /// </summary>
    private void AddAccentBorder(Grid mainGrid, SolidColorBrush borderBrush)
    {
        var accentBorder = new Border
        {
            Background = borderBrush,
            CornerRadius = new CornerRadius(8, 0, 0, 8)
        };
        Grid.SetColumn(accentBorder, 0);
        Grid.SetRowSpan(accentBorder, 2);
        mainGrid.Children.Add(accentBorder);
    }

    /// <summary>
    /// 添加图标容器
    /// </summary>
    private void AddIconContainer(Grid mainGrid, string type, SolidColorBrush iconBrush, SolidColorBrush iconBackgroundBrush)
    {
        var iconContainer = new Border
        {
            Background = iconBackgroundBrush,
            CornerRadius = new CornerRadius(24),
            Width = 48,
            Height = 48,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };

        var iconText = type switch
        {
            "Success" => "✓",
            "Warning" => "⚠",
            "Error" => "✕",
            _ => "ℹ"
        };

        var iconTextBlock = new TextBlock
        {
            Text = iconText,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = iconBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        iconContainer.Child = iconTextBlock;
        Grid.SetColumn(iconContainer, 1);
        Grid.SetRowSpan(iconContainer, 2);
        mainGrid.Children.Add(iconContainer);
    }

    /// <summary>
    /// 添加标题文本块
    /// </summary>
    private void AddTitleTextBlock(Grid mainGrid, string title, SolidColorBrush foregroundBrush)
    {
        var titleTextBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 19,
            Foreground = foregroundBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
        Grid.SetColumn(titleTextBlock, 2);
        Grid.SetRow(titleTextBlock, 0);
        Grid.SetRowSpan(titleTextBlock, 2);
        mainGrid.Children.Add(titleTextBlock);
    }

    /// <summary>
    /// 添加消息文本块
    /// </summary>
    private void AddMessageTextBlock(Grid mainGrid, string message, SolidColorBrush messageBrush)
    {
        var messageTextBlock = new TextBlock
        {
            Text = message,
            FontSize = 16,
            Foreground = messageBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 28, 0, 0),
            IsVisible = !string.IsNullOrWhiteSpace(message)
        };
        Grid.SetColumn(messageTextBlock, 2);
        Grid.SetRow(messageTextBlock, 1);
        mainGrid.Children.Add(messageTextBlock);
    }

    /// <summary>
    /// 添加关闭按钮
    /// </summary>
    private void AddCloseButton(Grid mainGrid, NotificationWindowContext context)
    {
        var closeButton = new Button
        {
            Content = "×",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Background = Brushes.Transparent,
            Foreground = context.CloseButtonForegroundBrush,
            BorderThickness = new Thickness(0),
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
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
            context.CloseButton.Background = context.IsDarkMode
                ? new SolidColorBrush(Color.FromRgb(60, 60, 60))
                : new SolidColorBrush(Color.FromRgb(230, 230, 230));
            context.CloseButton.Foreground = context.IsDarkMode
                ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                : new SolidColorBrush(Color.FromRgb(0, 0, 0));
        };

        context.PointerExitedHandler = (s, e) =>
        {
            if (context.CloseButton == null) return;
            context.CloseButton.Background = Brushes.Transparent;
            context.CloseButton.Foreground = context.CloseButtonForegroundBrush;
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
    /// 处理通知自动关闭
    /// </summary>
    private async Task HandleNotificationAutoClose(int duration, CancellationToken cancellationToken)
    {
        if (duration < 250)
        {
            duration = 250;
        }

        try
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
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