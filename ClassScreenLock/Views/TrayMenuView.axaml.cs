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
    public event EventHandler? UnlockClicked;
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

    private void OnUnlockClicked(object sender, PointerPressedEventArgs e)
    {
        UnlockClicked?.Invoke(this, EventArgs.Empty);
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
