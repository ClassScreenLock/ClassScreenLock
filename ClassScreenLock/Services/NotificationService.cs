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
        var settings = SettingsService.General;
        _notificationsEnabled = settings.ShowNotifications;
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
        if (!_notificationsEnabled) return;
        
        var localizedTitle = GetLocalizedString(title);
        var localizedMessage = GetLocalizedString(message);
        
        await ShowNotificationAsync(localizedTitle, localizedMessage, "Info", duration);
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

    public async Task<bool> ShowConfirmAsync(string message, string title)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var owner = desktop.MainWindow;

            var window = new Window
            {
                Title = title,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                SystemDecorations = SystemDecorations.BorderOnly
            };

            if (owner != null)
            {
                window.Icon = owner.Icon;
            }

            var grid = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("Auto,Auto")
            };

            var textBlock = new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 0, 0, 16),
                TextWrapping = TextWrapping.Wrap
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 12
            };

            var cancelButton = new Button
            {
                Content = GetLocalizedString("Btn_Cancel"),
                MinWidth = 80
            };

            var okButton = new Button
            {
                Content = GetLocalizedString("Btn_Save"),
                MinWidth = 80
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

    private async Task ShowNotificationCoreAsync(string title, string message, string type, int duration, CancellationToken cancellationToken)
    {
        Window? notificationWindow = null;
        Button? closeButton = null;
        EventHandler<RoutedEventArgs>? closeHandler = null;
        EventHandler<PointerEventArgs>? pointerEnteredHandler = null;
        EventHandler<PointerEventArgs>? pointerExitedHandler = null;
        SolidColorBrush? closeButtonForegroundBrush = null;
        bool isDarkMode = false;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;

                var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                var currentTheme = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
                isDarkMode = currentTheme == ThemeVariant.Dark;

                var backgroundBrush = isDarkMode
                    ? new SolidColorBrush(Color.FromRgb(32, 32, 32))
                    : new SolidColorBrush(Color.FromRgb(255, 255, 255));

                var foregroundBrush = isDarkMode
                    ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                    : new SolidColorBrush(Color.FromRgb(0, 0, 0));

                var chromeBrush = isDarkMode
                    ? new SolidColorBrush(Color.FromRgb(58, 58, 58))
                    : new SolidColorBrush(Color.FromRgb(230, 230, 230));

                var borderBrush = type switch
                {
                    "Success" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    "Warning" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    "Error" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    _ => new SolidColorBrush(Color.FromRgb(33, 150, 243))
                };

                var iconBrush = type switch
                {
                    "Success" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    "Warning" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    "Error" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    _ => new SolidColorBrush(Color.FromRgb(33, 150, 243))
                };

                var messageBrush = isDarkMode
                    ? new SolidColorBrush(Color.FromRgb(180, 180, 180))
                    : new SolidColorBrush(Color.FromRgb(100, 100, 100));

                closeButtonForegroundBrush = isDarkMode
                    ? new SolidColorBrush(Color.FromRgb(150, 150, 150))
                    : new SolidColorBrush(Color.FromRgb(100, 100, 100));

                var iconBackgroundBrush = isDarkMode
                    ? new SolidColorBrush(Color.FromRgb(45, 45, 45))
                    : new SolidColorBrush(Color.FromRgb(245, 245, 245));

                notificationWindow = new Window
                {
                    Title = title,
                    Width = 360,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = (SettingsService.General.NotificationPosition == Models.NotificationPosition.Center)
                        ? WindowStartupLocation.CenterScreen
                        : WindowStartupLocation.Manual,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    SystemDecorations = SystemDecorations.None,
                    Background = Brushes.Transparent,
                    Foreground = foregroundBrush,
                    ExtendClientAreaToDecorationsHint = true,
                    ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
                    ShowActivated = false,
                    Focusable = false,
                    Opacity = 1
                };

                var rootBorder = new Border
                {
                    Background = backgroundBrush,
                    BorderBrush = chromeBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12)
                };

                var mainGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("4,auto,*,auto"),
                    RowDefinitions = new RowDefinitions("auto,auto")
                };

                var accentBorder = new Border
                {
                    Background = borderBrush,
                    CornerRadius = new CornerRadius(6, 0, 0, 6)
                };
                Grid.SetColumn(accentBorder, 0);
                Grid.SetRowSpan(accentBorder, 2);
                mainGrid.Children.Add(accentBorder);

                var iconContainer = new Border
                {
                    Background = iconBackgroundBrush,
                    CornerRadius = new CornerRadius(16),
                    Width = 32,
                    Height = 32,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(6, 0, 4, 0)
                };
                var iconTextBlock = new TextBlock
                {
                    Text = type switch
                    {
                        "Success" => "✓",
                        "Warning" => "⚠",
                        "Error" => "✕",
                        _ => "ℹ"
                    },
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    Foreground = iconBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconContainer.Child = iconTextBlock;
                Grid.SetColumn(iconContainer, 1);
                Grid.SetRowSpan(iconContainer, 2);
                mainGrid.Children.Add(iconContainer);

                var titleTextBlock = new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 15,
                    Foreground = foregroundBrush,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(titleTextBlock, 2);
                Grid.SetRow(titleTextBlock, 0);
                mainGrid.Children.Add(titleTextBlock);

                var messageTextBlock = new TextBlock
                {
                    Text = message,
                    FontSize = 13,
                    Foreground = messageBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                    IsVisible = !string.IsNullOrWhiteSpace(message)
                };
                Grid.SetColumn(messageTextBlock, 2);
                Grid.SetRow(messageTextBlock, 1);
                mainGrid.Children.Add(messageTextBlock);

                closeButton = new Button
                {
                    Content = "×",
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    Background = Brushes.Transparent,
                    Foreground = closeButtonForegroundBrush,
                    BorderThickness = new Thickness(0),
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(8, -2, 0, 0)
                };

                closeHandler = (s, e) =>
                {
                    if (closeButton != null && closeHandler != null)
                    {
                        closeButton.Click -= closeHandler;
                    }
                    notificationWindow?.Close();
                };
                closeButton.Click += closeHandler;

                Grid.SetColumn(closeButton, 3);
                Grid.SetRow(closeButton, 0);
                mainGrid.Children.Add(closeButton);

                pointerEnteredHandler = (s, e) =>
                {
                    if (closeButton == null) return;

                    closeButton.Background = isDarkMode
                        ? new SolidColorBrush(Color.FromRgb(60, 60, 60))
                        : new SolidColorBrush(Color.FromRgb(230, 230, 230));
                    closeButton.Foreground = isDarkMode
                        ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                        : new SolidColorBrush(Color.FromRgb(0, 0, 0));
                };

                pointerExitedHandler = (s, e) =>
                {
                    if (closeButton == null) return;
                    closeButton.Background = Brushes.Transparent;
                    closeButton.Foreground = closeButtonForegroundBrush;
                };

                closeButton.PointerEntered += pointerEnteredHandler;
                closeButton.PointerExited += pointerExitedHandler;

                rootBorder.Child = mainGrid;
                notificationWindow.Content = rootBorder;
                notificationWindow.Show();

                var posAfterShow = SettingsService.General.NotificationPosition;
                if (posAfterShow != Models.NotificationPosition.Center)
                {
                    var main = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
                    var screen = main != null ? main.Screens.ScreenFromPoint(main.Position) : null;
                    if (screen != null)
                    {
                        var wa = screen.WorkingArea;
                        var width = (int)notificationWindow.Width;
                        var height = (int)notificationWindow.Height;
                        var x = wa.X + wa.Width - width - 24;
                        var y = wa.Y + 24;
                        if (posAfterShow == Models.NotificationPosition.TopLeft)
                        {
                            x = wa.X + 24;
                            y = wa.Y + 24;
                        }
                        else if (posAfterShow == Models.NotificationPosition.TopRight)
                        {
                            x = wa.X + wa.Width - width - 24;
                            y = wa.Y + 24;
                        }
                        else if (posAfterShow == Models.NotificationPosition.BottomLeft)
                        {
                            x = wa.X + 24;
                            y = wa.Y + wa.Height - height - 24;
                        }
                        else if (posAfterShow == Models.NotificationPosition.BottomRight)
                        {
                            x = wa.X + wa.Width - width - 24;
                            y = wa.Y + wa.Height - height - 24;
                        }
                        notificationWindow.Position = new PixelPoint(x, y);
                    }
                }
            });

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
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"显示通知失败: {ex.Message}");
        }
        finally
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        if (closeButton != null)
                        {
                            if (closeHandler != null) closeButton.Click -= closeHandler;
                            if (pointerEnteredHandler != null) closeButton.PointerEntered -= pointerEnteredHandler;
                            if (pointerExitedHandler != null) closeButton.PointerExited -= pointerExitedHandler;
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        notificationWindow?.Close();
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
    }

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
