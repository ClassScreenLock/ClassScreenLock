using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
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
            // 立即创建并显示SplashWindow，不做任何延迟操作
            splashWindow = new SplashWindow();
            splashWindow.Show();
            splashWindow.SetProgress(null, "正在启动…");

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

            var isMinimized = desktop.Args?.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)) ?? false;
            LogService.Instance.Log("Info", "Startup", "App", $"应用启动，isMinimized = {isMinimized}, Args = {string.Join(", ", desktop.Args ?? Array.Empty<string>())}");

            Services.LogService.Observe(Task.Run(async () =>
            {
                try
                {
                    LogService.Instance.Log("Info", "Startup", "App", "开始初始化后台服务...");
                    splashWindow?.SetProgress(25, "正在准备通知系统…");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _ = NotificationService.Instance;
                    });

                    // 初始化数据保护服务并执行数据核验
                    splashWindow?.SetProgress(35, "正在核验数据完整性…");
                    try
                    {
                        await DataProtectionService.Instance.VerifyAndRestoreDataAsync();
                        // 确保创建备份并设置系统隐藏保护
                        await DataProtectionService.Instance.CreateEncryptedBackupAsync();
                        // 确保所有 AppData 文件都被系统隐藏保护
                        DataProtectionService.Instance.EnsureAllFilesProtected();
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Error", "DataProtection", "App", $"数据保护初始化失败：{ex.Message}");
                    }

                    var requiresInit = InitializationService.Instance.RequiresInitialization;
                    LogService.Instance.Log("Info", "Startup", "App", $"RequiresInitialization = {requiresInit}");
                    
                    if (!requiresInit)
                    {
                        LogService.Instance.Log("Info", "Startup", "App", "初始化已完成，启动后台服务...");
                        splashWindow?.SetProgress(55, "正在启动后台服务…");
                        
                        // 并行启动后台服务以减少启动时间
                        var serviceTasks = new List<Task>
                        {
                            Task.Run(() => AppBlockingService.Instance.Start()),
                            Task.Run(() => ScreenshotService.Instance.Start()),
                            Task.Run(() => WebcamService.Instance.Start()),
                            Task.Run(() => AutomationService.Instance.Start()),
                            Task.Run(() => MutualProtectionService.Instance.Start())
                        };

                        // 同时加载组织配置
                        splashWindow?.SetProgress(60, "正在加载组织配置…");
                        var orgTask = OrganizationService.Instance.LoadOrganizationAsync();
                        
                        // 等待所有服务启动完成
                        await Task.WhenAll(serviceTasks);
                        await orgTask;
                        
                        splashWindow?.SetProgress(65, "正在启动配置同步…");
                        if (OrganizationService.Instance.HasJoinedOrganization)
                        {
                            OrganizationService.Instance.StartPeriodicSyncWithTimer();
                            
                            // 如果已加入集控，启用网络拦截功能
                            SettingsService.UpdateBlockage(s => s.IsNetworkLockEnabled = true);
                            Console.WriteLine("[DEBUG] 已加入集控，自动启用网络拦截功能");
                        }

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
                        try
                        {
                            LogService.Instance.Log("Info", "MainWindow", "App", "开始创建主窗口...");
                            Console.WriteLine("[DEBUG] Creating MainWindow...");
                            
                            var mainWindow = new MainWindow
                            {
                                DataContext = new MainWindowViewModel(),
                            };

                            desktop.MainWindow = mainWindow;
                            desktop.Exit += OnApplicationExit;

                            LogService.Instance.Log("Info", "MainWindow", "App", $"主窗口已创建，isMinimized = {isMinimized}");
                            Console.WriteLine($"[DEBUG] isMinimized = {isMinimized}");

                            // Ripple 特效与 IPC 在主窗口创建后启用，避免启动阶段卡顿
                            RippleEffectService.Instance.Attach(desktop.MainWindow);
                            IpcService.Instance.Start();

                            if (isMinimized)
                            {
                                mainWindow.Opacity = 0;
                                mainWindow.Show();
                                mainWindow.Hide();
                                mainWindow.Opacity = 1;
                                LogService.Instance.Log("Info", "MainWindow", "App", "主窗口已隐藏（最小化模式）");
                                Console.WriteLine("[DEBUG] MainWindow hidden (minimized mode)");
                            }
                            else
                            {
                                mainWindow.Show();
                                mainWindow.Activate();
                                LogService.Instance.Log("Info", "MainWindow", "App", "主窗口已显示并激活");
                                Console.WriteLine("[DEBUG] MainWindow shown and activated");
                            }

                            splashWindow?.Close();
                            LogService.Instance.Log("Info", "MainWindow", "App", "启动完成，闪屏窗口已关闭");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[DEBUG] Error creating MainWindow: {ex}");
                            LogService.Instance.Log("Error", "MainWindow Creation", "App", ex.ToString());
                        }
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
        try
        {
            LogService.Instance.Log("Info", "ShowMainWindow", "App", "尝试显示主窗口...");
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow != null)
                {
                    var window = desktop.MainWindow;
                    LogService.Instance.Log("Info", "ShowMainWindow", "App", $"主窗口存在，当前状态：IsVisible={window.IsVisible}, WindowState={window.WindowState}, Position=({window.Position.X}, {window.Position.Y}), Size=({window.Bounds.Width}x{window.Bounds.Height})");
                    
                    // 强制将窗口移到屏幕中央
                    window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;
                    
                    window.Show();
                    window.WindowState = Avalonia.Controls.WindowState.Normal;
                    window.Activate();
                    window.Focus();
                    
                    // 临时设置为最顶层，确保窗口可见
                    window.Topmost = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        window.Topmost = false;
                    }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
                    
                    LogService.Instance.Log("Info", "ShowMainWindow", "App", "主窗口已显示并激活");
                }
                else
                {
                    LogService.Instance.Log("Error", "ShowMainWindow", "App", "主窗口不存在！desktop.MainWindow = null");
                }
            }
            else
            {
                LogService.Instance.Log("Error", "ShowMainWindow", "App", "ApplicationLifetime 不是 IClassicDesktopStyleApplicationLifetime");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "ShowMainWindow", "App", $"显示主窗口失败: {ex.Message}");
        }
    }
    
    // 应用退出时清理资源
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            // 创建退出标记文件，通知看门狗主进程正常退出
            var exitFlagFile = Path.Combine(AppContext.BaseDirectory, "exit.flag");
            File.Create(exitFlagFile).Dispose();
            Console.WriteLine("Created exit.flag file");
            
            // 禁用应用防护，确保重启后不会自动启用
            SettingsService.UpdateBlockage(s =>
            {
                s.IsBasicProtectionEnabled = false;
                s.IsAppBlockingEnabled = false;
            });
            Console.WriteLine("Disabled protection settings");
            
            // 停止配置同步
            OrganizationService.Instance.StopPeriodicSyncTimer();
            
            // 通知组织服务应用程序正在退出（保留组织信息）
            OrganizationService.Instance.OnApplicationExit();

            // 停止子进程
            FloatingWidgetService.Instance.HideWidget();

            // 停止 IPC 服务
            IpcService.Instance.Stop();

            // 停止应用阻止服务
            AppBlockingService.Instance.Stop();

            // 停止互相守护服务
            MutualProtectionService.Instance.Stop();

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

            // 发送设备离线通知（后台执行，不阻塞退出）
            var deviceService = DeviceService.Instance;
            if (deviceService != null)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await deviceService.SendOfflineNotificationAsync();
                        Console.WriteLine("✓ 离线通知发送成功");
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"发送离线通知失败: {ex.Message}");
                    }
                });
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
