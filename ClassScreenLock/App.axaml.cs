using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Views;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using ClassScreenLock.Services;
using ClassScreenLock.Helpers;
using System.Threading.Tasks;
using Avalonia.Styling;

namespace ClassScreenLock;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        IconProvider.Current.Register<FontAwesomeIconProvider>();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SplashWindow? splashWindow = null;

        // 全局异常处理
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            LogService.Instance.Log("Fatal", "UnhandledException", "AppDomain", ex?.Message ?? "Unknown error");
        };

        // UI线程异常捕获
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (s, e) =>
        {
            e.Handled = true;
            LogService.Instance.Log("Error", "UIException", "Dispatcher", e.Exception.ToString());
            NotificationService.Instance.ShowError($"程序遇到非预期错误: {e.Exception.Message}");
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var settings = SettingsService.General;
                RequestedThemeVariant = settings.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
            }
            catch { }

            // 初始化本地化资源，避免资源键闪现
            try
            {
                LocalizationService.Instance.Initialize();
                ApplySavedLanguageSettings();
            }
            catch { }

            splashWindow = new SplashWindow();
            splashWindow.Show();
            splashWindow.SetProgress(null, "正在启动…");

            var isMinimized = desktop.Args?.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)) ?? false;

            Services.LogService.Observe(Task.Run(async () =>
            {
                try
                {
                    splashWindow?.SetProgress(25, "正在准备通知系统…");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _ = NotificationService.Instance;
                    });

                    var requiresInit = InitializationService.Instance.RequiresInitialization;
                    if (!requiresInit)
                    {
                        splashWindow?.SetProgress(55, "正在启动后台服务…");
                        AppBlockingService.Instance.Start();
                        ScreenshotService.Instance.Start();
                        WebcamService.Instance.Start();
                        AutomationService.Instance.Start();

                        splashWindow?.SetProgress(75, "正在应用网络规则…");
                        await NetworkBlockingService.Instance.ApplyRulesAsync("AppStartup");
                    }

                    splashWindow?.SetProgress(90, "正在应用界面设置…");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ApplySavedFontSettings();
                        ApplySavedAccentColorSettings();
                    });

                    if (SettingsService.General.AutoStart)
                    {
                        ClassScreenLock.Helpers.AutoStartHelper.UpdateAutoStartPath();
                    }

                    splashWindow?.SetProgress(100, "启动完成");

                    // 初始化完成后再显示主界面
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var mainWindow = new MainWindow
                        {
                            DataContext = new MainWindowViewModel(),
                        };

                        desktop.MainWindow = mainWindow;
                        desktop.Exit += OnApplicationExit;

                        // Ripple 特效与 IPC 在主窗口创建后启用，避免启动阶段卡顿
                        RippleEffectService.Instance.Attach(desktop.MainWindow);
                        IpcService.Instance.Start();

                        if (isMinimized)
                        {
                            mainWindow.Opacity = 0;
                            mainWindow.Show();
                            mainWindow.Hide();
                            mainWindow.Opacity = 1;
                        }
                        else
                        {
                            mainWindow.Show();
                            mainWindow.Activate();
                        }

                        splashWindow?.Close();
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log("Error", "Initialization", "App", ex.Message);
                }
            }), "App.StartupInit");

            DisableAvaloniaDataAnnotationValidation();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void MenuShow_OnClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void MenuLock_OnClick(object? sender, EventArgs e)
    {
        LockScreenService.Instance.ActivateLock(SettingsService.Lock.BreakTimeLockMode);
    }

    private void MenuUnlock_OnClick(object? sender, EventArgs e)
    {
        LockScreenService.Instance.ManualDeactivateLock();
    }

    private async void MenuExit_OnClick(object? sender, EventArgs e)
    {
        var required = SettingsService.Lock.ExitAppMinAccountType;
        var allowed = required == null || SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value);
        
        if (!allowed)
        {
            // 如果权限不足，尝试弹出验证对话框
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var verifyVm = new SecurityCenterViewModel(); // 借用 SecurityCenterViewModel 的登录逻辑
                var verifyWindow = new VerifyWindow { DataContext = verifyVm };

                // 监听登录成功事件
                bool verified = false;
                verifyVm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(SecurityCenterViewModel.IsAuthenticated) && verifyVm.IsAuthenticated)
                    {
                        verified = true;
                        verifyWindow.Close();
                    }
                };

                if (desktop.MainWindow != null && desktop.MainWindow.IsVisible)
                {
                    await verifyWindow.ShowDialog(desktop.MainWindow);
                }
                else
                {
                    verifyWindow.Show();
                    // 对于非 ShowDialog 的情况，我们需要等待窗口关闭
                    var tcs = new TaskCompletionSource<bool>();
                    verifyWindow.Closed += (s, e) => tcs.TrySetResult(verified);
                    await tcs.Task;
                }
                
                if (!verified)
                {
                    NotificationService.Instance.ShowWarning("权限不足：退出应用需要更高权限");
                    return;
                }
                
                // 验证通过，重新检查权限
                allowed = required == null || SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value);
            }
        }

        if (allowed)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RealClose();
                }
                desktop.Shutdown();
            }
        }
    }

    private async void MenuOpenLockSettings_OnClick(object? sender, EventArgs e)
    {
        var required = SettingsService.Lock.SidebarLockSettingsMinAccountType;
        var allowed = required == null || SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value);

        if (!allowed)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var verifyVm = new SecurityCenterViewModel();
                var verifyWindow = new VerifyWindow { DataContext = verifyVm };

                bool verified = false;
                verifyVm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(SecurityCenterViewModel.IsAuthenticated) && verifyVm.IsAuthenticated)
                    {
                        verified = true;
                        verifyWindow.Close();
                    }
                };

                if (desktop.MainWindow != null && desktop.MainWindow.IsVisible)
                {
                    await verifyWindow.ShowDialog(desktop.MainWindow);
                }
                else
                {
                    verifyWindow.Show();
                    var tcs = new TaskCompletionSource<bool>();
                    verifyWindow.Closed += (s, e) => tcs.TrySetResult(verified);
                    await tcs.Task;
                }

                if (!verified)
                {
                    NotificationService.Instance.ShowWarning("权限不足：锁屏设置需要更高权限");
                    return;
                }

                allowed = required == null || SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value);
            }
        }

        if (!allowed) return;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime && lifetime.MainWindow is MainWindow mainWindow)
        {
            if (mainWindow.DataContext is MainWindowViewModel vm)
            {
                mainWindow.Show();
                mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                mainWindow.Activate();
                vm.NavigateToSecurityCenter();
            }
        }
    }

    private void MenuOpenSchedule_OnClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWindow)
        {
            if (mainWindow.DataContext is MainWindowViewModel vm)
            {
                mainWindow.Show();
                mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                mainWindow.Activate();
                vm.NavigateToSchedule();
            }
        }
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            desktop.MainWindow.Show();
            desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            desktop.MainWindow.Activate();
        }
    }
    
    // 应用退出时清理资源
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            // 停止子进程
            FloatingWidgetService.Instance.HideWidget();

            // 停止 IPC 服务
            IpcService.Instance.Stop();

            // 停止应用阻止服务
            AppBlockingService.Instance.Stop();

            // 清理网络拦截规则（恢复 Hosts 和防火墙）
            NetworkBlockingService.Instance.Cleanup();

            // 释放本地化服务资源
            if (LocalizationService.Instance is IDisposable localizationService)
            {
                localizationService.Dispose();
            }
            
            // 释放通知服务资源
            if (NotificationService.Instance is IDisposable notificationService)
            {
                notificationService.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"清理资源时出错: {ex.Message}");
        }
    }
    
    // 应用已保存的语言设置
    private void ApplySavedLanguageSettings()
    {
        try
        {
            // 初始化本地化服务
            var localizationService = LocalizationService.Instance;
            
            // 获取已保存的语言设置
            var settings = SettingsService.General;
            
            // 设置文化信息
            var cultureInfo = new System.Globalization.CultureInfo(settings.Language);
            System.Globalization.CultureInfo.CurrentCulture = cultureInfo;
            System.Globalization.CultureInfo.CurrentUICulture = cultureInfo;
            
            // 直接设置当前语言，不通过 Post，确保后续代码能立即获取到正确资源
            localizationService.CurrentLanguage = settings.Language;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"应用语言设置失败: {ex.Message}");
        }
    }

    private void ApplySavedFontSettings()
    {
        try
        {
            var settings = SettingsService.General;
            var fontFamily = FontHelper.BuildGlobalFontFamily(settings.FontFamily);
            var fontWeight = FontHelper.BuildGlobalFontWeight(settings.FontFamily);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Resources.Remove("GlobalFontFamily");
                Resources.Add("GlobalFontFamily", fontFamily);
                Resources["GlobalFontFamily"] = fontFamily;

                Resources.Remove("GlobalFontWeight");
                Resources.Add("GlobalFontWeight", fontWeight);
                Resources["GlobalFontWeight"] = fontWeight;
            });
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"应用字体设置失败: {ex.Message}");
        }
    }

    private void ApplySavedAccentColorSettings()
    {
        try
        {
            var settings = SettingsService.General;
            string accentColor = settings.AccentColor;

            // 如果使用系统强调色，则尝试获取系统强调色
            if (settings.UseSystemAccentColor && PlatformSettings != null)
            {
                var colorValues = PlatformSettings.GetColorValues();
                var systemAccent = colorValues.AccentColor1;
                accentColor = $"#{systemAccent.R:X2}{systemAccent.G:X2}{systemAccent.B:X2}";
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ThemeHelper.ApplyAccentColor(accentColor);
            });
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"应用强调色设置失败: {ex.Message}");
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
