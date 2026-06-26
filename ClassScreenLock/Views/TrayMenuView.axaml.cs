using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ClassScreenLock.Views;

public partial class TrayMenuView : UserControl
{
    public TrayMenuView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
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

    private void OnShowClicked(object sender, PointerPressedEventArgs e)
    {
        ShowClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnLockClicked(object sender, PointerPressedEventArgs e)
    {
        LockClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnAppManagementClicked(object sender, PointerPressedEventArgs e)
    {
        AppManagementClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnNetworkInterceptionClicked(object sender, PointerPressedEventArgs e)
    {
        NetworkInterceptionClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnSecurityLogsClicked(object sender, PointerPressedEventArgs e)
    {
        SecurityLogsClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnSecurityCenterClicked(object sender, PointerPressedEventArgs e)
    {
        SecurityCenterClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnLockSettingsClicked(object sender, PointerPressedEventArgs e)
    {
        LockSettingsClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnScheduleClicked(object sender, PointerPressedEventArgs e)
    {
        ScheduleClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitClicked(object sender, PointerPressedEventArgs e)
    {
        ExitClicked?.Invoke(this, EventArgs.Empty);
    }
}
