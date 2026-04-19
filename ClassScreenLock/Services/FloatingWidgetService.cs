using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ClassScreenLock.Services;

public class FloatingWidgetService
{
    private static FloatingWidgetService? _instance;
    public static FloatingWidgetService Instance => _instance ??= new FloatingWidgetService();

    private FloatingBreakButtonWindow? _window;
    private bool _keepAlive;

    public void ShowWidget()
    {
        _keepAlive = true;
        if (!SettingsService.Lock.ShowFloatingLockWidget)
        {
            // 全局关闭开关时，任何地方都不应强制打开下课按钮进程
            HideWidget();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                if (!_window.IsVisible)
                {
                    _window.Opacity = 0;
                    _window.Show();
                    _window.EnsureBottom();
                    Dispatcher.UIThread.Post(() => { if (_window != null) _window.Opacity = 1; });
                }
                return;
            }

            _window = new FloatingBreakButtonWindow();
            _window.Closed += (_, __) =>
            {
                _window = null;
                if (_keepAlive && SettingsService.Lock.ShowFloatingLockWidget)
                {
                    ShowWidget();
                }
            };
            _window.Opacity = 0;
            _window.Show();
        });
    }

    public void HideWidget()
    {
        _keepAlive = false;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _window?.Close();
            }
            catch
            {
            }
            finally
            {
                _window = null;
            }
        });
    }

    public void ToggleWidget(bool show)
    {
        _keepAlive = show;
        if (show)
            ShowWidget();
        else
            HideWidget();
    }

    public void OnSettingChanged(bool show)
    {
        ToggleWidget(show);
    }

    private sealed class FloatingBreakButtonWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_BOTTOM = new(1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        private const double DragThreshold = 5.0;

        private readonly Button _button;
        private readonly Panel _normalContent;
        private readonly Panel _confirmContent;
        private bool _isConfirming;
        private DateTime _lastClickTime;
        private PixelPoint _dragStartPosition;
        private PixelPoint _pointerScreenStartPosition;
        private bool _isPointerDown;
        private bool _hasDragged;

        public FloatingBreakButtonWindow()
        {
            Width = 80;
            Height = 80;
            SystemDecorations = SystemDecorations.None;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = false;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            Focusable = false;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

            _button = new Button
            {
                Width = 60,
                Height = 60,
                MinWidth = 60,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(30)
            };

            _button.Classes.Add("accent");
            _button.Classes.Add("circular");

            _normalContent = BuildNormalContent();
            _confirmContent = BuildConfirmContent();
            _confirmContent.IsVisible = false;

            _button.Content = new Grid
            {
                Children =
                {
                    _normalContent,
                    _confirmContent
                }
            };

            _button.AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _button.AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _button.AddHandler(PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);

            _button.Click += OnButtonClick;

            Content = new Grid
            {
                Background = Brushes.Transparent,
                Children =
                {
                    _button
                }
            };

            Opened += (_, __) =>
            {
                RestorePosition();
                EnsureBottom();
                Dispatcher.UIThread.Post(() => Opacity = 1);
            };
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !_isConfirming)
            {
                _isPointerDown = true;
                _hasDragged = false;
                var localPos = e.GetPosition(this);
                _pointerScreenStartPosition = new PixelPoint(Position.X + (int)localPos.X, Position.Y + (int)localPos.Y);
                _dragStartPosition = Position;
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isPointerDown && !_isConfirming)
            {
                var point = e.GetCurrentPoint(this);
                if (point.Properties.IsLeftButtonPressed)
                {
                    var localPos = point.Position;
                    var currentPointerScreenPos = new PixelPoint(Position.X + (int)localPos.X, Position.Y + (int)localPos.Y);
                    var delta = currentPointerScreenPos - _pointerScreenStartPosition;
                    var distance = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);

                    if (distance > DragThreshold)
                    {
                        _hasDragged = true;
                        Position = new PixelPoint(_dragStartPosition.X + delta.X, _dragStartPosition.Y + delta.Y);
                    }
                }
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isPointerDown)
            {
                _isPointerDown = false;

                if (_hasDragged)
                {
                    SavePosition();
                }
            }
        }

        private void OnButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_hasDragged)
            {
                _hasDragged = false;
                e.Handled = true;
                return;
            }

            HandleButtonClick();
        }

        private void HandleButtonClick()
        {
            var now = DateTime.Now;
            if (!_isConfirming)
            {
                _isConfirming = true;
                _lastClickTime = now;
                UpdateButtonStyle(true);

                LogService.Observe(Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_isConfirming && (DateTime.Now - _lastClickTime).TotalMilliseconds >= 2900)
                        {
                            _isConfirming = false;
                            UpdateButtonStyle(false);
                        }
                    });
                }), "FloatingWidget.ConfirmTimeout");
            }
            else
            {
                if ((now - _lastClickTime).TotalMilliseconds < 3000)
                {
                    if (InitializationService.Instance.RequiresInitialization)
                    {
                        NotificationService.Instance.ShowWarning("请先完成初始设置");
                        _isConfirming = false;
                        UpdateButtonStyle(false);
                        return;
                    }
                    
                    var mode = SettingsService.Lock.BreakTimeLockMode;
                    LockScreenService.Instance.ActivateLock(mode);
                    _isConfirming = false;
                    UpdateButtonStyle(false);
                }
                else
                {
                    _isConfirming = true;
                    _lastClickTime = now;
                    UpdateButtonStyle(true);
                }
            }
        }

        private void UpdateButtonStyle(bool confirming)
        {
            if (confirming)
            {
                _button.Classes.Remove("accent");
                if (!_button.Classes.Contains("danger"))
                {
                    _button.Classes.Add("danger");
                }
            }
            else
            {
                _button.Classes.Remove("danger");
                if (!_button.Classes.Contains("accent"))
                {
                    _button.Classes.Add("accent");
                }
            }

            _normalContent.IsVisible = !confirming;
            _confirmContent.IsVisible = confirming;
        }

        public void EnsureBottom()
        {
            if (!OperatingSystem.IsWindows()) return;

            Dispatcher.UIThread.Post(() =>
            {
                var handle = TryGetPlatformHandle()?.Handle;
                if (handle != null)
                {
                    SetWindowPos(handle.Value, HWND_BOTTOM, 0, 0, 0, 0,
                        SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
                }
            });
        }

        private void SavePosition()
        {
            try
            {
                var pos = Position;
                SettingsService.UpdateLock(s =>
                {
                    s.FloatingWidgetPositionX = pos.X;
                    s.FloatingWidgetPositionY = pos.Y;
                });
            }
            catch
            {
            }
        }

        private void RestorePosition()
        {
            try
            {
                var settings = SettingsService.Lock;
                if (settings.FloatingWidgetPositionX.HasValue && settings.FloatingWidgetPositionY.HasValue)
                {
                    Position = new PixelPoint((int)settings.FloatingWidgetPositionX.Value, (int)settings.FloatingWidgetPositionY.Value);
                }
                else
                {
                    var screens = Screens;
                    var s = screens?.Primary;
                    if (s != null)
                    {
                        var wa = s.WorkingArea;
                        Position = new PixelPoint(wa.X + wa.Width - 120, wa.Y + wa.Height - 140);
                    }
                }
            }
            catch
            {
            }
        }

        private static Panel BuildNormalContent()
        {
            return new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Path
                    {
                        Data = Geometry.Parse("M12,17A2,2 0 0,0 14,15C14,13.89 13.11,13 12,13A2,2 0 0,0 10,15A2,2 0 0,0 12,17M18,8A2,2 0 0,1 20,10V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V10C4,8.89 4.9,8 6,8H7V6A5,5 0 0,1 12,1A5,5 0 0,1 17,6V8H18M12,3A3,3 0 0,0 9,6V8H15V6A3,3 0 0,0 12,3Z"),
                        Fill = Brushes.White,
                        Stretch = Stretch.Uniform,
                        Width = 18,
                        Height = 18,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "锁屏",
                        FontSize = 11,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            };
        }

        private static Panel BuildConfirmContent()
        {
            return new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Path
                    {
                        Data = Geometry.Parse("M12,2C11.31,2 10.61,2.13 9.96,2.39L4.59,4.54C3.61,4.93 3,5.88 3,6.94V13C3,17.5 6,21.5 10.45,23.32L12,24L13.55,23.32C18,21.5 21,17.5 21,13V6.94C21,5.88 20.39,4.93 19.41,4.54L14.04,2.39C13.39,2.13 12.69,2 12,2M12,4C12.47,4 12.93,4.09 13.36,4.27L18.72,6.41C18.9,6.49 19,6.67 19,6.87V13C19,16.5 16.75,19.6 13.35,21.07L12,21.65L10.65,21.07C7.25,19.6 5,16.5 5,13V6.87C5,6.67 5.1,6.49 5.28,6.41L10.64,4.27C11.07,4.09 11.53,4 12,4M12,7L7,12H10V17H14V12H17L12,7Z"),
                        Fill = Brushes.White,
                        Stretch = Stretch.Uniform,
                        Width = 18,
                        Height = 18,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "确认",
                        FontSize = 11,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            };
        }
    }
}
