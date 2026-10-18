using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClassScreenLock.Uninstaller.ViewModels;
using ClassScreenLock.Uninstaller.Views;
using FluentIcons.Avalonia;

namespace ClassScreenLock.Uninstaller;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 首先显示管理员验证窗口
            desktop.MainWindow = new VerifyWindow
            {
                DataContext = new VerifyViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
