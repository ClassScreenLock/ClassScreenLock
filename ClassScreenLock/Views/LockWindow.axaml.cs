using Avalonia.Controls;
using Avalonia.Input;
using System;
using ClassScreenLock.ViewModels;

namespace ClassScreenLock.Views;

public partial class LockWindow : Window
{
    public LockWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateCapsLockState(GetCapsLockState());
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
        if (DataContext is LockWindowViewModel vm)
        {
            vm.StopTimer();
        }
        base.OnClosing(e);
    }
}
