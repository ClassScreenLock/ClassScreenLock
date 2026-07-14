using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Services;
using System;

namespace ClassScreenLock.Views;

public partial class MainWindow : Window
{
    private bool _isClosing = false;
    private MainWindowViewModel? _viewModel;

    // 手动拖动窗口所需的状态（同时支持鼠标与触屏，因 BeginMoveDrag 在 Windows 上仅响应鼠标）。
    // 关键：必须使用与窗口位置无关的屏幕坐标源（GetCursorPos），否则设置 Position 后
    // 下一次 PointerMoved 的相对坐标会随窗口一起漂移，形成正反馈导致严重抖动。
    private bool _isManualDragging;
    private PixelPoint _dragStartCursorScreen;   // 按下时指针的屏幕坐标
    private PixelPoint _dragStartWindowPosition; // 按下时窗口的屏幕坐标
    private int _dragPointerId = -1;              // 正在拖动的指针 id，用于过滤多指

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnPropertyChanged;

        var titleBar = this.FindControl<Border>("TitleBar");
        if (titleBar != null)
        {
            // 使用 PointerPressed / PointerMoved / PointerReleased 手动驱动窗口拖动，
            // 这样对鼠标和触屏都有效；Window.BeginMoveDrag 在 Windows 触屏下不会触发。
            titleBar.PointerPressed += OnTitleBarPointerPressed;
            titleBar.PointerMoved += OnTitleBarPointerMoved;
            titleBar.PointerReleased += OnTitleBarPointerReleased;
            titleBar.PointerCaptureLost += OnTitleBarPointerCaptureLost;
            titleBar.DoubleTapped += OnTitleBarDoubleTapped;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        WindowProtectionService.Instance.ApplyProtectionAsync(this);
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            _viewModel?.UpdateMaximizedState(WindowState == WindowState.Maximized);
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

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isManualDragging) return;

        // 仅对主指针（第一个手指/鼠标左键）启动拖动，避免与多指手势冲突
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        // 已最大化时不通过手动拖动调整位置（保持平台原生体验）
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) return;

        // 用 GetCursorPos 取一次稳定的屏幕坐标作为基准，避免依赖窗口相对坐标
        if (!GetCursorPos(out var cursor)) return;

        _isManualDragging = true;
        _dragPointerId = e.Pointer.Id;
        _dragStartCursorScreen = new PixelPoint(cursor.X, cursor.Y);
        _dragStartWindowPosition = Position;

        // 捕获指针，使后续 PointerMoved 在用户拖出标题栏时仍能持续触发
        if (sender is IInputElement inputElement)
        {
            e.Pointer.Capture(inputElement);
        }
        e.Handled = true;
    }

    private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isManualDragging) return;
        // 只跟踪启动拖动的那根指针，忽略其它手指
        if (e.Pointer.Id != _dragPointerId) return;

        // 始终用屏幕坐标计算位移，避免窗口位置变化反过来影响坐标（这是抖动的根因）
        if (!GetCursorPos(out var cursor)) return;

        var dx = cursor.X - _dragStartCursorScreen.X;
        var dy = cursor.Y - _dragStartCursorScreen.Y;

        Position = new PixelPoint(
            _dragStartWindowPosition.X + dx,
            _dragStartWindowPosition.Y + dy);

        e.Handled = true;
    }

    private void OnTitleBarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndManualDrag(e.Pointer);
    }

    private void OnTitleBarPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndManualDrag(e.Pointer);
    }

    private void EndManualDrag(Avalonia.Input.IPointer? pointer)
    {
        if (!_isManualDragging) return;

        _isManualDragging = false;
        _dragPointerId = -1;

        try
        {
            pointer?.Capture(null);
        }
        catch
        {
            // 指针可能已失效，忽略
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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