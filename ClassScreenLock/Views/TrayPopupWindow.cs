using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using ClassScreenLock.Services;
using ClassScreenLock.Views;

namespace ClassScreenLock.Views;

public class TrayPopupWindow : Window
{
    private readonly TrayMenuView _menuView;

    public TrayPopupWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        TransparencyLevelHint = new List<WindowTransparencyLevel> { WindowTransparencyLevel.Transparent };
        ExtendClientAreaToDecorationsHint = true;
        ClipToBounds = false;
        
        if (SettingsService.General.DarkMode)
        {
            Classes.Add("dark");
        }
        
        _menuView = new TrayMenuView();
        Content = _menuView;
        
        _menuView.ShowClicked += (s, e) => { Hide(); ShowClicked?.Invoke(this, e); };
        _menuView.LockClicked += (s, e) => { Hide(); LockClicked?.Invoke(this, e); };
        _menuView.UnlockClicked += (s, e) => { Hide(); UnlockClicked?.Invoke(this, e); };
        _menuView.LockSettingsClicked += (s, e) => { Hide(); LockSettingsClicked?.Invoke(this, e); };
        _menuView.ScheduleClicked += (s, e) => { Hide(); ScheduleClicked?.Invoke(this, e); };
        _menuView.ExitClicked += (s, e) => { Hide(); ExitClicked?.Invoke(this, e); };
        
        Deactivated += OnDeactivated;
        PointerPressed += OnWindowPointerPressed;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Hide();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            var position = e.GetPosition(this);
            if (position.X < 0 || position.Y < 0 || 
                position.X > Bounds.Width || position.Y > Bounds.Height)
            {
                Hide();
            }
        }
    }

    public void ShowAtPosition(PixelPoint position)
    {
        if (SettingsService.General.DarkMode)
        {
            if (!Classes.Contains("dark"))
                Classes.Add("dark");
        }
        else
        {
            Classes.Remove("dark");
        }
        
        Position = position;
        Show();
        Activate();
        Focus();
    }

    public event EventHandler? ShowClicked;
    public event EventHandler? LockClicked;
    public event EventHandler? UnlockClicked;
    public event EventHandler? LockSettingsClicked;
    public event EventHandler? ScheduleClicked;
    public event EventHandler? ExitClicked;
}
