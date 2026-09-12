using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Services;

namespace ClassScreenLock.Views;

public partial class MainWindow : Window
{
    private bool _isClosing = false;
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnPropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 应用窗口防截屏保护。自定义标题栏已成为客户区的一部分，
        // SetWindowDisplayAffinity 不再干扰标题栏渲染。
        WindowProtectionService.Instance.ApplyProtectionAsync(this);

        // 拦截 Ctrl+V：用 Win32 原始剪贴板 API 读取粘贴内容（SYSTEM 下 OLE 封送失败会导致中文丢失）
        SystemPasteInterceptor.Attach(this);
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            _viewModel?.UpdateMaximizedState(WindowState == WindowState.Maximized);
        }
        else if (e.Property == IsVisibleProperty && e.NewValue is bool visible)
        {
            // 窗口"关闭即隐藏"时不会经过导航流程，需在此显式暂停/恢复应用管理页的后台定时器，
            // 否则隐藏后定时器仍每隔数秒全量枚举进程，持续占用 CPU 与句柄。
            _viewModel?.OnWindowVisibilityChanged(visible);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            _viewModel = vm;
            vm.SetMainWindow(this);
        }
    }

    /// <summary>
    /// 标题栏拖拽：点击空白区域拖动窗口。
    /// </summary>
    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// 双击标题栏最大化/还原。
    /// </summary>
    private void TitleBar_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        var required = SettingsService.Lock.ExitAppMinAccountType;
        if (required != null)
        {
            if (!AccountService.Instance.HasPermissionOrSecurityAuth(required.Value))
            {
                e.Cancel = true;
                NotificationService.Instance.ShowWarning(LocalizationService.Instance.GetString("SecurityCenter_Msg_InsufficientPermission") ?? "权限不足，无法退出软件");
                return;
            }
        }

        ClassScreenLock.Services.AccountService.Instance.Logout();

        base.OnClosing(e);
    }

    public void RealClose()
    {
        _isClosing = true;
        Close();
    }
}
