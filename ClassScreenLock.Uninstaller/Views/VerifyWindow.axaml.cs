using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassScreenLock.Uninstaller.ViewModels;

namespace ClassScreenLock.Uninstaller.Views;

public partial class VerifyWindow : Window
{
    public VerifyWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is VerifyViewModel vm)
        {
            vm.VerificationSucceeded += OnVerificationSucceeded;
        }
    }

    private void OnVerificationSucceeded(object? sender, EventArgs e)
    {
        // 验证成功，打开卸载主窗口并关闭验证窗口
        var uninstallWindow = new UninstallWindow
        {
            DataContext = new UninstallViewModel()
        };

        uninstallWindow.Show();
        this.Close();
    }
}
