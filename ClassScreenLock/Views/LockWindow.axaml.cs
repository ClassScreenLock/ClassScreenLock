using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Linq;
using System.Threading.Tasks;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Services;

namespace ClassScreenLock.Views;

public partial class LockWindow : Window
{
    public LockWindow()
    {
        InitializeComponent();
        
        this.DataContextChanged += OnDataContextChanged;
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
        if (DataContext is LockWindowViewModel vm)
        {
            vm.StopTimer();
        }
        base.OnClosing(e);
    }
}
