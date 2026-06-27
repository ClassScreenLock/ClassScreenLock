using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Markup.Xaml;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Views;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using ClassScreenLock.Services;
using ClassScreenLock.Helpers;
using ClassScreenLock.Models;
using System.Threading.Tasks;
using Avalonia.Styling;
using Avalonia.Platform;
using System.Diagnostics;
using System.Threading;

namespace ClassScreenLock;

public partial class App : Application
{
    private TrayPopupWindow? _trayPopup;
    private DateTime _lastClickTime = DateTime.MinValue;
    private const int DoubleClickTimeMs = 500;
    private nint _iconHandle;
    private uint _trayIconId = 1;
    private bool _trayIconCreated;
    
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int IMAGE_ICON = 1;
    private const int LR_DEFAULTSIZE = 0x00000040;
    
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    
    [DllImport("user32.dll")]
    private static extern nint LoadImage(nint hInst, string lpszName, uint uType, int cx, int cy, uint fuLoad);
    
    [DllImport("user32.dll")]
    private static extern nint LoadImage(nint hInst, nint lpszName, uint uType, int cx, int cy, uint fuLoad);
    
    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);
    
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);
    
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
    
    [DllImport("user32.dll")]
    private static extern nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);
    
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);
    
    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
    
    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandle(string? lpModuleName);
    
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindResource(nint hModule, nint lpName, nint lpType);
    
    [DllImport("kernel32.dll")]
    private static extern nint LoadResource(nint hModule, nint hResInfo);
    
    [DllImport("kernel32.dll")]
    private static extern nint LockResource(nint hResData);
    
    [DllImport("kernel32.dll")]
    private static extern uint SizeofResource(nint hModule, nint hResInfo);

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);
    private static WndProcDelegate? _wndProcDelegate;
    private static nint _messageWindow;
    
    private System.Threading.Timer? _watchdogMonitorTimer;
    private static readonly object _watchdogLock = new object();
    private static App? _appInstance;
    
    private static int _watchdogConsecutiveExceptions = 0;
    private static int _watchdogConsecutiveNormal = 0;
    private static bool _watchdogIsAbnormalState = false;
    private static readonly object _watchdogStateLock = new object();
    private static readonly TimeSpan _watchdogNormalInterval = TimeSpan.FromMilliseconds(375);
    private static readonly TimeSpan _watchdogAbnormalInterval = TimeSpan.FromMilliseconds(125);
    private const int WATCHDOG_REQUIRED_NORMAL_COUNT = 10;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
    }

    private bool _isInitialized = false;

    public override void Initialize()
    {
        if (_isInitialized) return;
        
        AvaloniaXamlLoader.Load(this);
        IconProvider.Current.Register<FontAwesomeIconProvider>();
        _appInstance = this;
        _isInitialized = true;
    }

    private void InitializeNotifyIcon()
    {
        try
        {
            _iconHandle = LoadIconFromResource();
            CreateMessageWindow();

            var data = CreateTrayIconData();
            AddTrayIcon(data);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "NotifyIcon", "App", $"初始化托盘图标失败: {ex.Message}");
        }
    }

    private nint LoadIconFromResource()
    {
        var moduleHandle = GetModuleHandle(null);
        var iconSizeX = GetSystemMetrics(11);
        var iconSizeY = GetSystemMetrics(12);

        // 尝试从模块资源加载图标
        var iconHandle = LoadImage(moduleHandle, new nint(1), IMAGE_ICON, iconSizeX, iconSizeY, 0);

        if (iconHandle == nint.Zero)
        {
            iconHandle = LoadIconFromEmbeddedResource(iconSizeX, iconSizeY);
        }

        // 最后尝试加载系统默认图标
        if (iconHandle == nint.Zero)
        {
            iconHandle = LoadIcon(nint.Zero, new nint(32512));
        }

        return iconHandle;
    }

    private nint LoadIconFromEmbeddedResource(int iconSizeX, int iconSizeY)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("ClassScreenLock.Assets.logo.ico");

        if (stream == null) return nint.Zero;

        var iconBytes = ReadStreamBytes(stream);
        var tempPath = Path.Combine(Path.GetTempPath(), $"csl_tray_{Guid.NewGuid():N}.ico");
        File.WriteAllBytes(tempPath, iconBytes);

        var iconHandle = LoadImage(nint.Zero, tempPath, IMAGE_ICON, iconSizeX, iconSizeY, 0x00000010);

        try { File.Delete(tempPath); } catch { }

        return iconHandle;
    }

    private byte[] ReadStreamBytes(Stream stream)
    {
        var iconBytes = new byte[stream.Length];
        var bytesRead = 0;
        while (bytesRead < iconBytes.Length)
        {
            var read = stream.Read(iconBytes, bytesRead, iconBytes.Length - bytesRead);
            if (read == 0) break;
            bytesRead += read;
        }
        return iconBytes;
    }

    private NOTIFYICONDATA CreateTrayIconData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _messageWindow,
            uID = _trayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = LocalizationService.Instance.GetString("AppTitle") ?? "课堂锁屏"
        };
    }

    private void AddTrayIcon(NOTIFYICONDATA data)
    {
        if (Shell_NotifyIcon(NIM_ADD, ref data))
        {
            _trayIconCreated = true;
            LogService.Instance.Log("Info", "NotifyIcon", "App", "托盘图标初始化成功");
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            LogService.Instance.Log("Error", "NotifyIcon", "App", $"Shell_NotifyIcon 失败, 错误码: {error}");
        }
    }
    
    private void CreateMessageWindow()
    {
        var className = "ClassScreenLockTrayIconClass";
        var wndClass = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate = WndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className
        };
        
        RegisterClass(ref wndClass);
        
        _messageWindow = CreateWindowEx(0, className, "ClassScreenLockTrayIcon", 0, 0, 0, 0, 0, nint.Zero, nint.Zero, GetModuleHandle(null), nint.Zero);
    }
    
    private static nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_TRAYICON && _appInstance != null)
        {
            var mouseMsg = (uint)lParam.ToInt32();
            
            switch (mouseMsg)
            {
                case WM_LBUTTONDOWN:
                    var now = DateTime.Now;
                    var timeSinceLastClick = (now - _appInstance._lastClickTime).TotalMilliseconds;
                    
                    if (timeSinceLastClick >= DoubleClickTimeMs)
                    {
                        _ = _appInstance.ShowTrayPopupAsync();
                    }
                    break;
                    
                case WM_LBUTTONDBLCLK:
                    _appInstance._lastClickTime = DateTime.Now;
                    _appInstance.ShowMainWindow();
                    break;
                    
                case WM_RBUTTONDOWN:
                    _ = _appInstance.ShowTrayPopupAsync();
                    break;
            }
        }
        
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SetupGlobalExceptionHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splashWindow = CreateAndShowSplashWindow();
            var isMinimized = CheckMinimizedMode(desktop);

            InitializeServices(splashWindow);
            StartBackgroundTasks(splashWindow, desktop, isMinimized);
            ApplyThemeSettings(splashWindow);

            DisableAvaloniaDataAnnotationValidation();
            InitializeNotifyIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            LogService.Instance.Log("Fatal", "UnhandledException", "AppDomain", ex?.Message ?? "Unknown error");
        };

        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (s, e) =>
        {
            e.Handled = true;
            LogService.Instance.Log("Error", "UIException", "Dispatcher", e.Exception.ToString());
            NotificationService.Instance.ShowError($"程序遇到非预期错误: {e.Exception.Message}");
        };
    }

    private SplashWindow CreateAndShowSplashWindow()
    {
        var splashWindow = new SplashWindow();
        splashWindow.Show();
        splashWindow.SetProgress(null, "正在核验数据完整性…");
        return splashWindow;
    }

    private bool CheckMinimizedMode(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var isMinimized = desktop.Args?.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)) ?? false;
        LogService.Instance.Log("Info", "Startup", "App", $"应用启动，isMinimized = {isMinimized}, Args = {string.Join(", ", desktop.Args ?? Array.Empty<string>())}");
        return isMinimized;
    }

    private void InitializeServices(SplashWindow splashWindow)
    {
        // 先停止锁屏状态文件检查，确保不会在恢复前触发
        LockScreenService.Instance.StopLockStateFileCheck();

        // 执行数据恢复，确保在任何其他服务初始化之前完成
        try
        {
            Task.Run(async () =>
            {
                try
                {
                    // 标记初始化正在进行，暂停文件监控同步
                    DataProtectionService.Instance.SetInitializationInProgress(true);
                    await DataProtectionService.Instance.VerifyAndRestoreDataAsync();
                    DataProtectionService.Instance.SetInitializationInProgress(false);
                }
                catch (Exception ex)
                {
                    DataProtectionService.Instance.SetInitializationInProgress(false);
                    LogService.Instance.Log("Error", "DataProtection", "App", $"数据恢复失败：{ex.Message}");
                }
            }).GetAwaiter().GetResult();
        }
        catch { }

        // 启动看门狗监测定时器
        StartWatchdogMonitor();

        // 初始化本地化资源
        try
        {
            LocalizationService.Instance.Initialize();
            ApplySavedLanguageSettings();
        }
        catch { }

        // 注意：锁屏状态文件检查将在 CreateMainWindowAsync 中启动
        // 确保在 RestoreLockStateOnStartup 之后启动
    }

    private void StartBackgroundTasks(SplashWindow splashWindow, IClassicDesktopStyleApplicationLifetime desktop, bool isMinimized)
    {
        // 后台执行启动前置任务
        Task.Run(() =>
        {
            try { UiAccessService.Instance.CheckAndElevate(); } catch { }
            try { ProcessProtector.EnableProtection(); } catch { }
            try
            {
                var existingWatchdogs = Process.GetProcessesByName("CSL.Watchdog");
                if (existingWatchdogs.Length < 3)
                {
                    Program.StartWatchdogProcess();
                }
            }
            catch { }
        });

        Services.LogService.Observe(Task.Run(async () =>
        {
            try
            {
                await InitializeBackgroundServicesAsync(splashWindow, desktop, isMinimized);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "Initialization", "App", ex.Message);
            }
        }), "App.StartupInit");
    }

    private async Task InitializeBackgroundServicesAsync(SplashWindow splashWindow, IClassicDesktopStyleApplicationLifetime desktop, bool isMinimized)
    {
        LogService.Instance.Log("Info", "Startup", "App", "开始初始化后台服务...");
        splashWindow?.SetProgress(20, "正在准备通知系统…");

        await InitializeNotificationServiceAsync();
        await InitializeDataProtectionAsync(splashWindow);
        await InitializeWindowProtectionAsync(splashWindow);

        var requiresInit = InitializationService.Instance.RequiresInitialization;
        LogService.Instance.Log("Info", "Startup", "App", $"RequiresInitialization = {requiresInit}");

        if (!requiresInit)
        {
            await StartCoreServicesAsync(splashWindow, desktop);
        }

        ConfigureAutoStartAsync();
        await ApplyInterfaceSettingsAsync(splashWindow);
        await CreateMainWindowAsync(splashWindow, desktop, isMinimized);
    }

    private async Task InitializeNotificationServiceAsync()
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            _ = NotificationService.Instance;
        });

        // 初始化屏幕监控服务：订阅 WebSocket 上的集控端命令
        try
        {
            ScreenMonitorService.Instance.Initialize();
            LockScreenService.Instance.InitializeRemoteControl();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "ScreenMonitor", "App", $"屏幕监控服务初始化失败: {ex.Message}");
        }

        // 订阅集控端推送的消息：显示通知并按需调用 Windows 语音模块朗读
        WebSocketService.Instance.OnDeviceMessage += HandleDeviceMessage;
    }

    /// <summary>
    /// 处理来自集控端的消息：显示通知，并根据 readAloud 调用 Windows 内置语音模块朗读
    /// </summary>
    private void HandleDeviceMessage(string message, bool readAloud, string sender,
        BannerSize size = BannerSize.Small,
        BannerFontSize fontSize = BannerFontSize.Medium,
        BannerDurationMode durationMode = BannerDurationMode.Auto,
        int customDurationSeconds = 10,
        bool lockWindow = false)
    {
        try
        {
            // 详细日志：确认 HandleDeviceMessage 被触发
            LogService.Instance.Log("Info", "DeviceMessage", "App",
                $"HandleDeviceMessage 被调用: message='{message}', readAloud={readAloud}, sender='{sender}', size={size}, fontSize={fontSize}, durationMode={durationMode}, lockWindow={lockWindow}");

            var title = string.IsNullOrEmpty(sender)
                ? "集控消息"
                : "集控消息";

            // 使用独立的 BannerService 显示半透明大横幅（始终显示，不受通知设置影响）
            LogService.Instance.Log("Info", "DeviceMessage", "App",
                $"调用 BannerService.ShowBanner: title='{title}', size={size}, fontSize={fontSize}, durationMode={durationMode}, lockWindow={lockWindow}");
            BannerService.Instance.ShowBanner(title, message, sender, size, fontSize, durationMode, customDurationSeconds, lockWindow);

            if (readAloud && !string.IsNullOrWhiteSpace(message))
            {
                LogService.Instance.Log("Info", "DeviceMessage", "App", $"开始朗读消息");
                _ = Task.Run(() => Speak(message));
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DeviceMessage", "App", $"处理集控消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 调用 Windows 内置语音模块（System.Speech）朗读文本
    /// </summary>
    private void Speak(string text)
    {
        try
        {
            using var synth = new System.Speech.Synthesis.SpeechSynthesizer();

            // 优先使用中文语音（如已安装）
            try
            {
                var voices = synth.GetInstalledVoices();
                var zhVoice = voices.FirstOrDefault(v =>
                    v.VoiceInfo?.Culture?.Name?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true);
                if (zhVoice != null)
                {
                    synth.SelectVoice(zhVoice.VoiceInfo.Name);
                }
            }
            catch
            {
                // 选用默认语音即可
            }

            synth.Rate = 0;
            synth.Speak(text);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "Speech", "App", $"语音朗读失败: {ex.Message}");
        }
    }

    private async Task InitializeDataProtectionAsync(SplashWindow? splashWindow)
    {
        splashWindow?.SetProgress(35, "正在更新数据备份…");
        try
        {
            await DataProtectionService.Instance.CreateEncryptedBackupAsync();
            DataProtectionService.Instance.EnsureAllFilesProtected();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DataProtection", "App", $"数据保护初始化失败：{ex.Message}");
        }
    }

    private Task InitializeWindowProtectionAsync(SplashWindow? splashWindow)
    {
        splashWindow?.SetProgress(45, "正在初始化安全模块…");
        try
        {
            WindowProtectionService.Instance.InitializeFromSettings();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "WindowProtection", "App", $"窗口保护初始化失败：{ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task StartCoreServicesAsync(SplashWindow? splashWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        LogService.Instance.Log("Info", "Startup", "App", "初始化已完成，启动后台服务...");
        splashWindow?.SetProgress(55, "正在启动后台服务…");

        InstallWindowsServicesAsync().ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                splashWindow?.SetProgress(65, "正在启动后台服务…");
            });
        });

        StartBackgroundServiceInstances();
        LoadOrganizationConfigurationAsync();
        ApplyNetworkRulesAsync();

        splashWindow?.SetProgress(80, "正在启动后台服务…");
        return Task.CompletedTask;
    }

    private async Task InstallWindowsServicesAsync()
    {
        try
        {
            await WindowsServiceManager.InstallAndStartServicesAsync();
            LogService.Instance.Log("Info", "ServiceManager", "App", "Windows services installed and started");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "ServiceManager", "App", $"Failed to install/start services: {ex.Message}");
        }
    }

    private void StartBackgroundServiceInstances()
    {
        var serviceTasks = new List<Task>
        {
            Task.Run(() => AppBlockingService.Instance.Start()),
            Task.Run(() => ScreenshotService.Instance.Start()),
            Task.Run(() => WebcamService.Instance.Start()),
            Task.Run(() => AutomationService.Instance.Start()),
            Task.Run(() => MutualProtectionService.Instance.Start())
        };

        _ = Task.WhenAll(serviceTasks).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                LogService.Instance.Log("Error", "Services", "App", $"部分服务启动失败：{t.Exception?.Message}");
            }
            else
            {
                LogService.Instance.Log("Info", "Services", "App", "所有服务已启动完成");
            }
        });
    }

    private void LoadOrganizationConfigurationAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await OrganizationService.Instance.LoadOrganizationAsync();
                if (OrganizationService.Instance.HasJoinedOrganization)
                {
                    OrganizationService.Instance.StartPeriodicSyncWithTimer();
                    SettingsService.UpdateBlockage(s => s.IsNetworkLockEnabled = true);
                    Console.WriteLine("[DEBUG] 已加入集控，自动启用网络拦截功能");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "Organization", "App", $"后台加载组织配置失败: {ex.Message}");
            }
        });
    }

    private void ApplyNetworkRulesAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                await NetworkBlockingService.Instance.ApplyRulesAsync("AppStartup");
                LogService.Instance.Log("Info", "NetworkBlocking", "App", "网络规则已应用");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "NetworkBlocking", "App", $"应用网络规则失败：{ex.Message}");
            }
        });
    }

    private void ConfigureAutoStartAsync()
    {
        _ = Task.Run(() =>
        {
            try
            {
                AutoStartHelper.CheckAndRepairAutoStart();
                AutoStartHelper.CheckAndRepairWatchdogAutoStart();
                AutoStartHelper.StartPeriodicCheck();
                LogService.Instance.Log("Info", "AutoStart", "App", "开机自启动已检查并修复完成（主程序 + 看门狗），定时检查已启动");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "AutoStart", "App", $"配置开机自启动失败：{ex.Message}");
            }
        });
    }

    private Task ApplyInterfaceSettingsAsync(SplashWindow? splashWindow)
    {
        splashWindow?.SetProgress(90, "正在应用界面设置…");
        _ = Task.Run(async () =>
        {
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplySavedFontSettings();
                    ApplySavedAccentColorSettings();
                });
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "Settings", "App", $"应用界面设置失败：{ex.Message}");
            }
        });
        return Task.CompletedTask;
    }

    private async Task CreateMainWindowAsync(SplashWindow? splashWindow, IClassicDesktopStyleApplicationLifetime desktop, bool isMinimized)
    {
        splashWindow?.SetProgress(100, "启动完成");
        await Task.Delay(300);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                LogService.Instance.Log("Info", "MainWindow", "App", "开始创建主窗口...");
                Console.WriteLine("[DEBUG] Creating MainWindow...");

                var mainWindow = CreateMainWindow();
                desktop.MainWindow = mainWindow;
                desktop.Exit += OnApplicationExit;

                LogService.Instance.Log("Info", "MainWindow", "App", $"主窗口已创建，isMinimized = {isMinimized}");
                Console.WriteLine($"[DEBUG] isMinimized = {isMinimized}");

                RippleEffectService.Instance.Attach(desktop.MainWindow);
                IpcService.Instance.Start();

                ShowMainWindowByMode(mainWindow, isMinimized);

                splashWindow?.Close();
                LogService.Instance.Log("Info", "MainWindow", "App", "启动完成，闪屏窗口已关闭");

                // 先恢复锁屏状态，再启动定时检查
                LockScreenService.Instance.RestoreLockStateOnStartup();
                // 在锁屏状态恢复完成后，启动定时检查
                LockScreenService.Instance.StartLockStateFileCheck();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Error creating MainWindow: {ex}");
                LogService.Instance.Log("Error", "MainWindow Creation", "App", ex.ToString());
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private MainWindow CreateMainWindow()
    {
        var mainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(),
        };

        if (SettingsService.General.DarkMode)
        {
            mainWindow.Classes.Add("dark");
        }

        return mainWindow;
    }

    private void ShowMainWindowByMode(MainWindow mainWindow, bool isMinimized)
    {
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
    }

    private void ApplyThemeSettings(SplashWindow splashWindow)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var settings = SettingsService.General;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RequestedThemeVariant = settings.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
                    if (settings.DarkMode)
                    {
                        splashWindow.Classes.Add("dark");
                    }
                });
            }
            catch { }
        });
    }

    private async System.Threading.Tasks.Task ShowTrayPopupAsync()
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            ShowTrayPopup();
        });
    }

    private void ShowTrayPopup()
    {
        EnsureTrayPopupInitialized();

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var mainWindow = desktop.MainWindow;
        if (mainWindow == null) return;

        // 使用菜单窗口实际尺寸进行精确位置计算，确保退出键始终可见
        // 如果 Bounds 还未初始化（首次显示），使用 UserControl 的 Width/Height 兜底
        var menuWidth = _trayPopup != null && _trayPopup.Bounds.Width > 0
            ? (int)_trayPopup.Bounds.Width
            : 260;  // TrayMenuView 中定义的 Width
        var menuHeight = _trayPopup != null && _trayPopup.Bounds.Height > 0
            ? (int)_trayPopup.Bounds.Height
            : 380;  // 设计高度

        // 优先使用鼠标所在的屏幕（解决多屏环境下位置错乱问题）
        Screen? targetScreen = null;
        POINT cursorPos = default;
        if (GetCursorPos(out cursorPos))
        {
            // 尝试找到包含鼠标位置的屏幕
            var pixelCursor = new PixelPoint(cursorPos.X, cursorPos.Y);
            targetScreen = FindScreenAtPoint(mainWindow, pixelCursor);

            // 兜底：使用主窗口所在屏幕 / 主屏幕
            targetScreen ??= mainWindow.Screens.Primary ?? mainWindow.Screens.All.FirstOrDefault();
            if (targetScreen == null) return;

            var position = CalculatePopupPosition(pixelCursor, targetScreen, menuWidth, menuHeight);
            _trayPopup!.ShowAtPosition(position);
            return;
        }

        // GetCursorPos 失败时使用默认位置（基于任务栏位置）
        targetScreen = mainWindow.Screens.Primary ?? mainWindow.Screens.All.FirstOrDefault();
        if (targetScreen == null) return;

        var defaultPos = GetDefaultPosition(targetScreen, menuWidth, menuHeight, 8);
        _trayPopup!.ShowAtPosition(defaultPos);
    }

    /// <summary>
    /// 在所有屏幕中查找包含指定绝对坐标的屏幕（解决多屏坐标混乱问题）
    /// </summary>
    private Screen? FindScreenAtPoint(Window mainWindow, PixelPoint point)
    {
        foreach (var screen in mainWindow.Screens.All)
        {
            var bounds = screen.Bounds;
            if (point.X >= bounds.X && point.X < bounds.X + bounds.Width &&
                point.Y >= bounds.Y && point.Y < bounds.Y + bounds.Height)
            {
                return screen;
            }
        }
        return null;
    }

    private PixelPoint CalculatePopupPosition(PixelPoint cursorPos, Screen screen, int menuWidth, int menuHeight)
    {
        const int margin = 8;

        // 1. 优先基于光标位置计算（即使坐标是 (0,0)，只要 GetCursorPos 成功就算有效）
        return CalculatePositionFromCursor(cursorPos, screen, menuWidth, menuHeight, margin);
    }

    private void EnsureTrayPopupInitialized()
    {
        if (_trayPopup != null) return;

        _trayPopup = new TrayPopupWindow();
        _trayPopup.ShowClicked += MenuShow_OnClick;
        _trayPopup.LockClicked += MenuLock_OnClick;
        _trayPopup.AppManagementClicked += MenuAppManagement_OnClick;
        _trayPopup.NetworkInterceptionClicked += MenuNetworkInterception_OnClick;
        _trayPopup.SecurityLogsClicked += MenuSecurityLogs_OnClick;
        _trayPopup.SecurityCenterClicked += MenuSecurityCenter_OnClick;
        _trayPopup.LockSettingsClicked += MenuOpenLockSettings_OnClick;
        _trayPopup.ScheduleClicked += MenuOpenSchedule_OnClick;
        _trayPopup.ExitClicked += MenuExit_OnClick;
    }

    private PixelPoint CalculatePositionFromCursor(PixelPoint cursorPos, Screen screen, int menuWidth, int menuHeight, int margin)
    {
        var workArea = screen.WorkingArea;
        var workLeft = workArea.X;
        var workTop = workArea.Y;
        var workRight = workArea.X + workArea.Width;
        var workBottom = workArea.Y + workArea.Height;

        // 1. 优先让菜单出现在托盘图标上方，留出 margin 间距
        double y = cursorPos.Y - menuHeight - margin;

        // 2. 如果上方空间不足，则放到光标下方；同样保留 margin
        if (y < workTop)
        {
            y = cursorPos.Y + margin;
        }

        // 3. 再次校验：若放下方后菜单底部仍然超出屏幕工作区，则贴底对齐，确保退出键始终可见
        if (y + menuHeight > workBottom)
        {
            y = workBottom - menuHeight;
        }

        // 4. 极小屏幕（菜单比工作区还高）：贴顶显示
        if (y < workTop)
        {
            y = workTop;
        }

        // 5. 水平方向：默认与光标右侧对齐，并保证完全可见
        double x = cursorPos.X;
        if (x + menuWidth > workRight)
        {
            x = workRight - menuWidth;
        }
        if (x < workLeft)
        {
            x = workLeft;
        }

        return new PixelPoint((int)x, (int)y);
    }

    private PixelPoint GetDefaultPosition(Screen screen, int menuWidth, int menuHeight, int margin)
    {
        var taskbarPosition = GetTaskbarPosition(screen);
        var (x, y) = GetPositionByTaskbarPosition(taskbarPosition, screen, menuWidth, menuHeight, margin);
        return new PixelPoint((int)x, (int)y);
    }

    private (double x, double y) GetPositionByTaskbarPosition(TaskbarPosition position, Screen screen, int menuWidth, int menuHeight, int margin)
    {
        var workArea = screen.WorkingArea;
        double x, y;

        switch (position)
        {
            case TaskbarPosition.Top:
                x = workArea.X + workArea.Width - menuWidth - margin;
                y = workArea.Y + margin;
                break;
            case TaskbarPosition.Left:
                x = workArea.X + margin;
                y = workArea.Y + workArea.Height - menuHeight - margin;
                break;
            case TaskbarPosition.Right:
            case TaskbarPosition.Bottom:
            default:
                x = workArea.X + workArea.Width - menuWidth - margin;
                y = workArea.Y + workArea.Height - menuHeight - margin;
                break;
        }

        return (x, y);
    }

    private TaskbarPosition GetTaskbarPosition(Screen screen)
    {
        if (screen == null) return TaskbarPosition.Bottom;

        var bounds = screen.Bounds;
        var workingArea = screen.WorkingArea;

        if (workingArea.Y > bounds.Y)
            return TaskbarPosition.Top;
        if (workingArea.Height < bounds.Height)
            return TaskbarPosition.Bottom;
        if (workingArea.X > bounds.X)
            return TaskbarPosition.Left;
        if (workingArea.Width < bounds.Width)
            return TaskbarPosition.Right;

        return TaskbarPosition.Bottom;
    }

    private enum TaskbarPosition
    {
        Top,
        Bottom,
        Left,
        Right
    }

    private void MenuShow_OnClick(object? sender, EventArgs e)
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            ShowMainWindow();
            return;
        }
        
        ShowMainWindow();
    }

    private void MenuLock_OnClick(object? sender, EventArgs e)
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            NotificationService.Instance.ShowWarning("请先完成初始设置");
            return;
        }
        
        LockScreenService.Instance.ActivateLock(SettingsService.Lock.BreakTimeLockMode);
    }

    private void MenuAppManagement_OnClick(object? sender, EventArgs e)
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            ShowMainWindow();
            return;
        }
        
        ShowMainWindow();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWindow && mainWindow.DataContext is MainWindowViewModel vm)
        {
            vm.NavigateToAppManagement();
        }
    }

    private void MenuNetworkInterception_OnClick(object? sender, EventArgs e)
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            ShowMainWindow();
            return;
        }
        
        ShowMainWindow();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWindow && mainWindow.DataContext is MainWindowViewModel vm)
        {
            vm.NavigateToNetworkInterception();
        }
    }

    private void MenuSecurityLogs_OnClick(object? sender, EventArgs e)
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            ShowMainWindow();
            return;
        }
        
        ShowMainWindow();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWindow && mainWindow.DataContext is MainWindowViewModel vm)
        {
            vm.NavigateToSecurityLogs();
        }
    }

    private void MenuSecurityCenter_OnClick(object? sender, EventArgs e)
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            ShowMainWindow();
            return;
        }
        
        ShowMainWindow();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWindow && mainWindow.DataContext is MainWindowViewModel vm)
        {
            vm.NavigateToSecurityCenter();
        }
    }

    private async void MenuExit_OnClick(object? sender, EventArgs e)
    {
        var required = SettingsService.Lock.ExitAppMinAccountType;
        var allowed = CheckInitialPermission(required);

        if (!allowed)
        {
            allowed = await VerifyPermissionAsync(required, "退出应用需要更高权限");
            if (!allowed) return;
        }

        if (allowed && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            CleanupTrayResources();
            ShutdownApplication(desktop);
        }
    }

    private bool CheckInitialPermission(AccountType? required)
    {
        return required == null || SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value);
    }

    private async Task<bool> VerifyPermissionAsync(AccountType? required, string warningMessage)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        var verifyVm = new SecurityCenterViewModel();
        var verifyWindow = CreateVerifyWindow(verifyVm);

        bool verified = false;
        verifyVm.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(SecurityCenterViewModel.IsAuthenticated) && verifyVm.IsAuthenticated)
            {
                verified = true;
                verifyWindow.Close();
            }
        };

        await ShowVerifyWindowAsync(verifyWindow, desktop);

        if (!verified)
        {
            NotificationService.Instance.ShowWarning($"权限不足：{warningMessage}");
            return false;
        }

        return CheckInitialPermission(required);
    }

    private VerifyWindow CreateVerifyWindow(SecurityCenterViewModel verifyVm)
    {
        var verifyWindow = new VerifyWindow { DataContext = verifyVm };

        if (SettingsService.General.DarkMode)
        {
            verifyWindow.Classes.Add("dark");
        }

        return verifyWindow;
    }

    private async Task ShowVerifyWindowAsync(VerifyWindow verifyWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow != null && desktop.MainWindow.IsVisible)
        {
            await verifyWindow.ShowDialog(desktop.MainWindow);
        }
        else
        {
            verifyWindow.Show();
            var tcs = new TaskCompletionSource<bool>();
            verifyWindow.Closed += (s, e) => tcs.TrySetResult(true);
            await tcs.Task;
        }
    }

    private void CleanupTrayResources()
    {
        if (_trayIconCreated)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _messageWindow,
                uID = _trayIconId
            };
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _trayIconCreated = false;
        }

        if (_messageWindow != nint.Zero)
        {
            DestroyWindow(_messageWindow);
            _messageWindow = nint.Zero;
        }

        if (_iconHandle != nint.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = nint.Zero;
        }
    }

    private void ShutdownApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is MainWindow mainWindow)
        {
            mainWindow.RealClose();
        }
        desktop.Shutdown();
    }

    private async void MenuOpenLockSettings_OnClick(object? sender, EventArgs e)
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            NotificationService.Instance.ShowWarning("请先完成初始设置");
            ShowMainWindow();
            return;
        }

        var required = SettingsService.Lock.SidebarLockSettingsMinAccountType;
        var allowed = CheckInitialPermission(required);

        if (!allowed)
        {
            allowed = await VerifyPermissionAsync(required, "锁屏设置需要更高权限");
        }

        if (!allowed) return;

        NavigateToSecurityCenter();
    }

    private void NavigateToSecurityCenter()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
            return;

        if (lifetime.MainWindow is not MainWindow mainWindow)
            return;

        if (mainWindow.DataContext is MainWindowViewModel vm)
        {
            mainWindow.Show();
            mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            mainWindow.Activate();
            vm.NavigateToSecurityCenter();
        }
    }

    private void MenuOpenSchedule_OnClick(object? sender, EventArgs e)
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            NotificationService.Instance.ShowWarning("请先完成初始设置");
            ShowMainWindow();
            return;
        }
        
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
    
    private void StartWatchdogMonitor()
    {
        lock (_watchdogLock)
        {
            if (_watchdogMonitorTimer != null)
                return;
            
            _watchdogMonitorTimer = new System.Threading.Timer(CheckWatchdog, null, _watchdogNormalInterval, _watchdogNormalInterval);
            LogService.Instance.Log("Info", "WatchdogMonitor", "App", $"看门狗监测已启动（动态间隔：正常375毫秒，异常125毫秒）");
        }
    }
    
    private void StopWatchdogMonitor()
    {
        lock (_watchdogLock)
        {
            _watchdogMonitorTimer?.Dispose();
            _watchdogMonitorTimer = null;
            LogService.Instance.Log("Info", "WatchdogMonitor", "App", "看门狗监测已停止");
        }
    }
    
    private void TerminateWatchdogProcesses()
    {
        try
        {
            var watchdogProcesses = Process.GetProcessesByName("CSL.Watchdog");
            if (watchdogProcesses.Length > 0)
            {
                LogService.Instance.Log("Info", "WatchdogTerminator", "App", $"正在终止 {watchdogProcesses.Length} 个看门狗进程...");
                
                foreach (var process in watchdogProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Warning", "WatchdogTerminator", "App", $"终止看门狗进程失败: {ex.Message}");
                    }
                }
                
                LogService.Instance.Log("Info", "WatchdogTerminator", "App", "所有看门狗进程已终止");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "WatchdogTerminator", "App", $"终止看门狗进程失败: {ex.Message}");
        }
    }
    
    private void CheckWatchdog(object? state)
    {
        bool hasException = false;
        
        try
        {
            var watchdogProcesses = Process.GetProcessesByName("CSL.Watchdog");
            if (watchdogProcesses.Length == 0)
            {
                hasException = true;
                LogService.Instance.Log("Warning", "WatchdogMonitor", "App", "检测到看门狗进程已退出，正在重启...");
                Program.StartWatchdogProcess();
            }
            else if (watchdogProcesses.Length < 3)
            {
                hasException = true;
                LogService.Instance.Log("Warning", "WatchdogMonitor", "App", $"检测到看门狗实例不足（{watchdogProcesses.Length}/3），正在补充...");
                Program.StartWatchdogProcess();
            }
        }
        catch (Exception ex)
        {
            hasException = true;
            LogService.Instance.Log("Error", "WatchdogMonitor", "App", $"检查看门狗失败: {ex.Message}");
        }
        
        UpdateWatchdogCheckInterval(hasException);
    }
    
    private void UpdateWatchdogCheckInterval(bool hasException)
    {
        lock (_watchdogStateLock)
        {
            TimeSpan newInterval;
            
            if (hasException)
            {
                _watchdogIsAbnormalState = true;
                _watchdogConsecutiveExceptions++;
                _watchdogConsecutiveNormal = 0;
                newInterval = _watchdogAbnormalInterval;
                
                if (_watchdogConsecutiveExceptions == 1)
                {
                    LogService.Instance.Log("Warning", "WatchdogMonitor", "App", $"异常状态检测！切换到125毫秒检查间隔。连续异常次数: {_watchdogConsecutiveExceptions}");
                }
            }
            else
            {
                if (_watchdogIsAbnormalState)
                {
                    _watchdogConsecutiveNormal++;
                    
                    if (_watchdogConsecutiveNormal >= WATCHDOG_REQUIRED_NORMAL_COUNT)
                    {
                        _watchdogConsecutiveExceptions = 0;
                        _watchdogConsecutiveNormal = 0;
                        _watchdogIsAbnormalState = false;
                        newInterval = _watchdogNormalInterval;
                        LogService.Instance.Log("Info", "WatchdogMonitor", "App", $"连续{_watchdogConsecutiveNormal}次正常检查后恢复正常状态。切换到375毫秒检查间隔。");
                    }
                    else
                    {
                        newInterval = _watchdogAbnormalInterval;
                        LogService.Instance.Log("Debug", "WatchdogMonitor", "App", $"正常检查 {_watchdogConsecutiveNormal}/{WATCHDOG_REQUIRED_NORMAL_COUNT}。保持125毫秒间隔。");
                    }
                }
                else
                {
                    newInterval = _watchdogNormalInterval;
                }
            }
            
            _watchdogMonitorTimer?.Change(newInterval, newInterval);
        }
    }
    
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            StopWatchdogMonitor();
            
            TerminateWatchdogProcesses();
            
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

            // 清理锁屏状态文件
            LockScreenService.Instance.Stop();
            LockScreenService.Instance.CleanupLockStateFile();

            // 停止应用阻止服务
            AppBlockingService.Instance.Stop();

            // 停止自动化服务
            AutomationService.Instance.Stop();

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
