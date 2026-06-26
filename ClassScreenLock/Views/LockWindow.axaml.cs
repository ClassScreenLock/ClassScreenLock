using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Services;
using Avalonia.Threading;

namespace ClassScreenLock.Views;

public partial class LockWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private const int SC_RESTORE = 0xF120;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int SWP_NOZORDER = 0x0004;
    private const int HWND_TOPMOST = -1;
    private const int HWND_TOP = 0;
    private const int HWND_BOTTOM = 1;

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

    public LockWindow()
    {
        InitializeComponent();
        
        this.DataContextChanged += OnDataContextChanged;
        this.PropertyChanged += OnWindowPropertyChanged;
        this.Opened += OnLockWindowOpened;
    }

    private void OnLockWindowOpened(object? sender, EventArgs e)
    {
        try
        {
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
                
                LogService.Instance.Log("Debug", "LockWindow", "WndProc", 
                    "已移除最小化/最大化按钮样式，设置置顶扩展样式");
                
                StartProtectionTimer();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "LockWindow", "WndProcHook", 
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
        
        LogService.Instance.Log("Debug", "LockWindow", "ProtectionTimer", $"已启动窗口保护定时器 ({interval}ms)");
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
                LogService.Instance.Log("Warning", "LockWindow", "ProtectionTimer", 
                    "检测到窗口被最小化 (IsIconic)，立即恢复");
                
                ShowWindow(_hwnd, 9); // SW_RESTORE
                SetWindowPos(_hwnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            
            if (!IsWindowVisible(_hwnd))
            {
                LogService.Instance.Log("Warning", "LockWindow", "ProtectionTimer", 
                    "检测到窗口被隐藏，立即恢复");
                
                ShowWindow(_hwnd, 9); // SW_RESTORE
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
            LogService.Instance.Log("Error", "LockWindow", "ProtectionTimer", 
                $"保护定时器异常: {ex.Message}");
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            if (WindowState == WindowState.Minimized)
            {
                LogService.Instance.Log("Warning", "LockWindow", "StateChanged", 
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
    
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is LockWindowViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }
    
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LockWindowViewModel.IsShutdownDialogVisible))
        {
            if (DataContext is LockWindowViewModel vm && vm.IsShutdownDialogVisible)
            {
                AnimateShutdownDialogIn();
            }
        }
    }
    
    private void AnimateShutdownDialogIn()
    {
        var overlay = this.FindControl<Border>("ShutdownOverlay");
        var dialog = this.FindControl<Border>("ShutdownDialog");
        if (dialog == null || overlay == null) return;
        
        var scaleTransform = dialog.RenderTransform as ScaleTransform;
        if (scaleTransform == null)
        {
            scaleTransform = new ScaleTransform(1, 1);
            dialog.RenderTransform = scaleTransform;
        }
        
        overlay.Opacity = 0;
        dialog.Opacity = 0;
        scaleTransform.ScaleX = 0.9;
        scaleTransform.ScaleY = 0.9;
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            overlay.Opacity = 1;
            dialog.Opacity = 1;
            scaleTransform.ScaleX = 1;
            scaleTransform.ScaleY = 1;
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateCapsLockState(GetCapsLockState());
        
        WindowProtectionService.Instance.ApplyProtectionAsync(this);
    }

    private void TextBox_OnGotFocus(object? sender, Avalonia.Input.GotFocusEventArgs e)
    {
        if (sender is TextBox textBox && DataContext is LockWindowViewModel vm)
        {
            if (textBox.Name == "UsernameBox")
            {
                vm.SetFocusedFieldCommand.Execute("Username");
            }
            else if (textBox.Name == "PasswordBox")
            {
                vm.SetFocusedFieldCommand.Execute("Password");
            }
            else if (textBox.Name == "TwoFactorBox")
            {
                vm.SetFocusedFieldCommand.Execute("TwoFactor");
            }
        }
    }

    private void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.CapsLock)
        {
            UpdateCapsLockState(GetCapsLockState());
        }
    }

    private void PasswordBox_OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (DataContext is LockWindowViewModel vm && !string.IsNullOrEmpty(e.Text))
        {
            if (vm.IsCapsLockEnabled)
            {
                var newText = new string(e.Text!.Select(ch => char.IsLetter(ch) ? char.ToLower(ch) : ch).ToArray());
                vm.AppendToFocusedFieldCommand.Execute(newText);
                e.Handled = true;
            }
        }
    }

    private void Window_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.CapsLock)
        {
            UpdateCapsLockState(GetCapsLockState());
        }
    }

    private static bool GetCapsLockState()
    {
        try
        {
            return Console.CapsLock;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateCapsLockState(bool isEnabled)
    {
        if (DataContext is LockWindowViewModel vm)
        {
            vm.UpdateCapsLockState(isEnabled);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StopProtectionTimer();
        
        if (DataContext is LockWindowViewModel vm)
        {
            vm.StopTimer();
        }
        base.OnClosing(e);
    }
}
