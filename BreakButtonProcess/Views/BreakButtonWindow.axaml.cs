using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BreakButtonProcess.Views;

public partial class BreakButtonWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private System.Timers.Timer? _bottomTimer;
    private DateTime _lastClickTime = DateTime.MinValue;
    private bool _isConfirming = false;

    public BreakButtonWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += (_, __) => _bottomTimer?.Stop();
        
        var b = this.FindControl<Button>("LockButton");
        if (b != null)
        {
            b.Click += (_, __) => HandleButtonClick();
        }

        // 极其频繁地确保置底，频率提高到 500ms 一次
        _bottomTimer = new System.Timers.Timer(500);
        _bottomTimer.Elapsed += (_, __) => EnsureBottom();
        _bottomTimer.Start();
    }

    private void HandleButtonClick()
    {
        var now = DateTime.Now;
        if (!_isConfirming)
        {
            // 第一次点击，进入确认状态
            _isConfirming = true;
            _lastClickTime = now;
            UpdateButtonStyle(true);
            
            // 3秒后自动重置确认状态
            Task.Run(async () =>
            {
                await Task.Delay(3000);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_isConfirming && (DateTime.Now - _lastClickTime).TotalMilliseconds >= 2900)
                    {
                        _isConfirming = false;
                        UpdateButtonStyle(false);
                    }
                });
            });
        }
        else
        {
            // 第二次点击，且在 3 秒内，触发锁定
            if ((now - _lastClickTime).TotalMilliseconds < 3000)
            {
                SendLockCommand();
                _isConfirming = false;
                UpdateButtonStyle(false);
            }
            else
            {
                // 超时了，重新开始确认逻辑
                _isConfirming = true;
                _lastClickTime = now;
                UpdateButtonStyle(true);
            }
        }
    }

    private void UpdateButtonStyle(bool confirming)
    {
        var b = this.FindControl<Button>("LockButton");
        var normal = this.FindControl<StackPanel>("NormalContent");
        var confirm = this.FindControl<StackPanel>("ConfirmContent");

        if (b != null)
        {
            if (confirming) b.Classes.Add("confirming");
            else b.Classes.Remove("confirming");
        }
        if (normal != null) normal.IsVisible = !confirming;
        if (confirm != null) confirm.IsVisible = confirming;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var screens = Screens;
        if (screens != null)
        {
            var s = screens.Primary;
            if (s != null)
            {
                var wa = s.WorkingArea;
                // 放在右下角稍微靠上的位置，避免被任务栏完全遮挡，但保持置底
                Position = new PixelPoint(wa.X + wa.Width - 120, wa.Y + wa.Height - 140);
            }
        }

        EnsureBottom();
    }

    private void EnsureBottom()
    {
        if (OperatingSystem.IsWindows())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var handle = TryGetPlatformHandle()?.Handle;
                if (handle != null)
                {
                    SetWindowPos(handle.Value, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                }
            });
        }
    }

    private void SendLockCommand()
    {
        try
        {
            using var client = new NamedPipeClientStream("ClassScreenLock_IPC");
            client.Connect(500); // 增加超时时间到 500ms
            var msg = Encoding.UTF8.GetBytes("LOCK\n");
            client.Write(msg, 0, msg.Length);
            client.Flush();
        }
        catch
        {
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
