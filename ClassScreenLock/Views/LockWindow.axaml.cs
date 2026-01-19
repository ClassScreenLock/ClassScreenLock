using Avalonia.Controls;
using ClassScreenLock.ViewModels;

namespace ClassScreenLock.Views;

public partial class LockWindow : Window
{
    public LockWindow()
    {
        InitializeComponent();
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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is LockWindowViewModel vm)
        {
            vm.StopTimer();
        }
        base.OnClosing(e);
    }
}
