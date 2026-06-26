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
        _menuView.AppManagementClicked += (s, e) => { Hide(); AppManagementClicked?.Invoke(this, e); };
        _menuView.NetworkInterceptionClicked += (s, e) => { Hide(); NetworkInterceptionClicked?.Invoke(this, e); };
        _menuView.SecurityLogsClicked += (s, e) => { Hide(); SecurityLogsClicked?.Invoke(this, e); };
        _menuView.SecurityCenterClicked += (s, e) => { Hide(); SecurityCenterClicked?.Invoke(this, e); };
        _menuView.LockSettingsClicked += (s, e) => { Hide(); LockSettingsClicked?.Invoke(this, e); };
        _menuView.ScheduleClicked += (s, e) => { Hide(); ScheduleClicked?.Invoke(this, e); };
        _menuView.ExitClicked += (s, e) => { Hide(); ExitClicked?.Invoke(this, e); };
        
        Deactivated += OnDeactivated;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Hide();
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
    public event EventHandler? AppManagementClicked;
    public event EventHandler? NetworkInterceptionClicked;
    public event EventHandler? SecurityLogsClicked;
    public event EventHandler? SecurityCenterClicked;
    public event EventHandler? LockSettingsClicked;
    public event EventHandler? ScheduleClicked;
    public event EventHandler? ExitClicked;
}
