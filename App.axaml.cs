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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        IconProvider.Current.Register<FontAwesomeIconProvider>();
        _appInstance = this;
    }

    private void InitializeNotifyIcon()
    {
        try
        {
            var moduleHandle = GetModuleHandle(null);
            
            _iconHandle = LoadImage(moduleHandle, new nint(1), IMAGE_ICON, GetSystemMetrics(11), GetSystemMetrics(12), 0);
            
            if (_iconHandle == nint.Zero)
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("ClassScreenLock.Assets.logo.ico");
                if (stream != null)
                {
                    var iconBytes = new byte[stream.Length];
                    var bytesRead = 0;
                    while (bytesRead < iconBytes.Length)
                    {
                        var read = stream.Read(iconBytes, bytesRead, iconBytes.Length - bytesRead);
                        if (read == 0) break;
                        bytesRead += read;
                    }
                    
                    var tempPath = Path.Combine(Path.GetTempPath(), $"csl_tray_{Guid.NewGuid():N}.ico");
                    File.WriteAllBytes(tempPath, iconBytes);
                    
                    _iconHandle = LoadImage(nint.Zero, tempPath, IMAGE_ICON, GetSystemMetrics(11), GetSystemMetrics(12), 0x00000010);
                    
                    try { File.Delete(tempPath); } catch { }
                }
            }
            
            if (_iconHandle == nint.Zero)
            {
                _iconHandle = LoadIcon(nint.Zero, new nint(32512));
            }
            
            CreateMessageWindow();
            
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _messageWindow,
                uID = _trayIconId,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _iconHandle,
                szTip = LocalizationService.Instance.GetString("AppTitle") ?? "课堂锁屏"
            };
            
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
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "NotifyIcon", "App", $"初始化托盘图标失败: {ex.Message}");
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
            // 立即创建并显示 SplashWindow，不做任何延迟操作
            splashWindow = new SplashWindow();
            
            // 默认先不应用主题，避免阻塞
            splashWindow.Show();
            splashWindow.SetProgress(null, "正在启动…");

            // 后台应用主题设置
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

            // 后台执行启动前置任务（最快路径）
            Task.Run(() =>
            {
                try
                {
                    // 检测并尝试 UIAccess 提权
                    UiAccessService.Instance.CheckAndElevate();
                }
                catch { }
                
                try
                {
                    // 启用进程保护
                    ProcessProtector.EnableProtection();
                }
                catch { }
                
                try
                {
                    // UIAccess 提权后启动看门狗，避免重复启动
                    Program.StartWatchdogProcess();
                }
                catch { }
            });
            
            // 启动看门狗监测定时器
            StartWatchdogMonitor();

            // 初始化本地化资源，避免资源键闪现
            try
            {
                LocalizationService.Instance.Initialize();
                ApplySavedLanguageSettings();
            }
            catch { }

            LockScreenService.Instance.StartLockStateFileCheck();

            var isMinimized = desktop.Args?.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)) ?? false;
            LogService.Instance.Log("Info", "Startup", "App", $"应用启动，isMinimized = {isMinimized}, Args = {string.Join(", ", desktop.Args ?? Array.Empty<string>())}");

            Services.LogService.Observe(Task.Run(async () =>
            {
                try
                {
                    LogService.Instance.Log("Info", "Startup", "App", "开始初始化后台服务...");
                    splashWindow?.SetProgress(20, "正在准备通知系统…");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _ = NotificationService.Instance;
                    });

                    // 初始化数据保护服务并执行数据核验
                    splashWindow?.SetProgress(35, "正在核验数据完整性…");
                    try
                    {
                        await DataProtectionService.Instance.VerifyAndRestoreDataAsync();
                        await DataProtectionService.Instance.CreateEncryptedBackupAsync();
                        DataProtectionService.Instance.EnsureAllFilesProtected();
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Error", "DataProtection", "App", $"数据保护初始化失败：{ex.Message}");
                    }

                    splashWindow?.SetProgress(45, "正在初始化安全模块…");
                    try
                    {
                        WindowProtectionService.Instance.InitializeFromSettings();
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Error", "WindowProtection", "App", $"窗口保护初始化失败：{ex.Message}");
                    }

                    var requiresInit = InitializationService.Instance.RequiresInitialization;
                    LogService.Instance.Log("Info", "Startup", "App", $"RequiresInitialization = {requiresInit}");
                    
                    if (!requiresInit)
                    {
                        LogService.Instance.Log("Info", "Startup", "App", "初始化已完成，启动后台服务...");
                        splashWindow?.SetProgress(55, "正在启动后台服务…");
                        
                        // 注册并启动 Windows 服务
                        try
                        {
                            await WindowsServiceManager.InstallAndStartServicesAsync();
                            LogService.Instance.Log("Info", "ServiceManager", "App", "Windows services installed and started");
                        }
                        catch (Exception ex)
                        {
                            LogService.Instance.Log("Error", "ServiceManager", "App", $"Failed to install/start services: {ex.Message}");
                        }

                        splashWindow?.SetProgress(65, "正在启动后台服务…");
                        
                        // 并行启动后台服务以减少启动时间
                        var serviceTasks = new List<Task>
                        {
                            Task.Run(() => AppBlockingService.Instance.Start()),
                            Task.Run(() => ScreenshotService.Instance.Start()),
                            Task.Run(() => WebcamService.Instance.Start()),
                            Task.Run(() => AutomationService.Instance.Start()),
                            Task.Run(() => MutualProtectionService.Instance.Start())
                        };

                        // 不等待所有服务启动完成，立即继续启动流程
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
                        
                        // 组织配置加载在后台进行，不阻塞启动流程
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

                        // 网络规则应用在后台异步执行，不阻塞启动流程
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(2000); // 延迟 2 秒，等待主窗口显示
                                await NetworkBlockingService.Instance.ApplyRulesAsync("AppStartup");
                                LogService.Instance.Log("Info", "NetworkBlocking", "App", "网络规则已应用");
                            }
                            catch (Exception ex)
                            {
                                LogService.Instance.Log("Error", "NetworkBlocking", "App", $"应用网络规则失败：{ex.Message}");
                            }
                        });

                        splashWindow?.SetProgress(80, "正在启动后台服务…");
                    }

                    // 开机自启动配置移到后台异步执行，不阻塞启动流程（无论是否需要初始化都会执行）
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            // 立即检查并修复所有自启动方式，不延迟
                            // 这可以解决用户在任务管理器禁用后状态不一致的问题
                            AutoStartHelper.CheckAndRepairAutoStart();
                            
                            // 检查并修复看门狗的自启动状态
                            AutoStartHelper.CheckAndRepairWatchdogAutoStart();
                            
                            // 启动定时检查任务，持续监控自启动状态
                            AutoStartHelper.StartPeriodicCheck();
                            
                            LogService.Instance.Log("Info", "AutoStart", "App", "开机自启动已检查并修复完成（主程序 + 看门狗），定时检查已启动");
                        }
                        catch (Exception ex)
                        {
                            LogService.Instance.Log("Error", "AutoStart", "App", $"配置开机自启动失败：{ex.Message}");
                        }
                    });

                    // 立即更新进度到 90%，不阻塞等待界面设置
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

                    // 立即更新进度到 100%
                    splashWindow?.SetProgress(100, "启动完成");

                    // 稍等 300ms 让用户看到完整的进度条，然后显示主窗口
                    await Task.Delay(300);

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

                            // 根据设置应用 dark 类
                            if (SettingsService.General.DarkMode)
                            {
                                mainWindow.Classes.Add("dark");
                            }

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
            
            // 初始化自定义托盘图标（替代 Avalonia TrayIcon）
            InitializeNotifyIcon();
        }

        base.OnFrameworkInitializationCompleted();
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
        if (_trayPopup == null)
        {
            _trayPopup = new TrayPopupWindow();
            _trayPopup.ShowClicked += MenuShow_OnClick;
            _trayPopup.LockClicked += MenuLock_OnClick;
            _trayPopup.UnlockClicked += MenuUnlock_OnClick;
            _trayPopup.LockSettingsClicked += MenuOpenLockSettings_OnClick;
            _trayPopup.ScheduleClicked += MenuOpenSchedule_OnClick;
            _trayPopup.ExitClicked += MenuExit_OnClick;
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        
        var mainWindow = desktop.MainWindow;
        var screen = mainWindow?.Screens.Primary ?? mainWindow?.Screens.All.FirstOrDefault();
        if (screen == null) return;

        var menuWidth = 240;
        var menuHeight = 320;
        var margin = 8;
        
        double x, y;
        
        if (GetCursorPos(out var cursorPos))
        {
            x = cursorPos.X;
            y = cursorPos.Y - menuHeight - margin * 5;
            
            if (x + menuWidth > screen.WorkingArea.X + screen.WorkingArea.Width)
                x = screen.WorkingArea.X + screen.WorkingArea.Width - menuWidth - margin;
            if (y < screen.WorkingArea.Y)
                y = cursorPos.Y + margin;
            if (y + menuHeight > screen.WorkingArea.Y + screen.WorkingArea.Height)
                y = screen.WorkingArea.Y + screen.WorkingArea.Height - menuHeight - margin;
            
            if (x < screen.WorkingArea.X)
                x = screen.WorkingArea.X + margin;
        }
        else
        {
            var taskbarPosition = GetTaskbarPosition(mainWindow!);
            
            switch (taskbarPosition)
            {
                case TaskbarPosition.Bottom:
                    x = screen.WorkingArea.X + screen.WorkingArea.Width - menuWidth - margin;
                    y = screen.WorkingArea.Y + screen.WorkingArea.Height - menuHeight - margin;
                    break;
                case TaskbarPosition.Top:
                    x = screen.WorkingArea.X + screen.WorkingArea.Width - menuWidth - margin;
                    y = screen.WorkingArea.Y + margin;
                    break;
                case TaskbarPosition.Left:
                    x = screen.WorkingArea.X + margin;
                    y = screen.WorkingArea.Y + screen.WorkingArea.Height - menuHeight - margin;
                    break;
                case TaskbarPosition.Right:
                    x = screen.WorkingArea.X + screen.WorkingArea.Width - menuWidth - margin;
                    y = screen.WorkingArea.Y + screen.WorkingArea.Height - menuHeight - margin;
                    break;
                default:
                    x = screen.WorkingArea.X + screen.WorkingArea.Width - menuWidth - margin;
                    y = screen.WorkingArea.Y + screen.WorkingArea.Height - menuHeight - margin;
                    break;
            }
        }

        _trayPopup.ShowAtPosition(new PixelPoint((int)x, (int)y));
    }

    private TaskbarPosition GetTaskbarPosition(Window mainWindow)
    {
        var screen = mainWindow.Screens.Primary ?? mainWindow.Screens.All.FirstOrDefault();
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

                // 根据设置应用 dark 类
                if (SettingsService.General.DarkMode)
                {
                    verifyWindow.Classes.Add("dark");
                }

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
                // 释放托盘图标资源
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
        if (InitializationService.Instance.RequiresInitialization)
        {
            NotificationService.Instance.ShowWarning("请先完成初始设置");
            ShowMainWindow();
            return;
        }
        
        var required = SettingsService.Lock.SidebarLockSettingsMinAccountType;
        var allowed = required == null || SecurityService.Instance.IsAuthenticated || AccountService.Instance.HasPermission(required.Value);

        if (!allowed)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var verifyVm = new SecurityCenterViewModel();
                var verifyWindow = new VerifyWindow { DataContext = verifyVm };

                // 根据设置应用 dark 类
                if (SettingsService.General.DarkMode)
                {
                    verifyWindow.Classes.Add("dark");
                }

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
            
            // 每 3 秒检查一次看门狗，与看门狗监测主程序的频率相当
            _watchdogMonitorTimer = new System.Threading.Timer(CheckWatchdog, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
            LogService.Instance.Log("Info", "WatchdogMonitor", "App", "看门狗监测已启动（间隔：3秒）");
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
    
    private void CheckWatchdog(object? state)
    {
        try
        {
            var exitFlagFile = Path.Combine(AppContext.BaseDirectory, "exit.flag");
            if (File.Exists(exitFlagFile))
            {
                return;
            }
            
            var watchdogProcesses = Process.GetProcessesByName("CSL.Watchdog");
            if (watchdogProcesses.Length == 0)
            {
                LogService.Instance.Log("Warning", "WatchdogMonitor", "App", "检测到看门狗进程已退出，正在重启...");
                Program.StartWatchdogProcess();
            }
            else if (watchdogProcesses.Length < 3)
            {
                LogService.Instance.Log("Warning", "WatchdogMonitor", "App", $"检测到看门狗实例不足（{watchdogProcesses.Length}/3），正在补充...");
                Program.StartWatchdogProcess();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "WatchdogMonitor", "App", $"检查看门狗失败: {ex.Message}");
        }
    }
    
    // 应用退出时清理资源
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            // 停止看门狗监测
            StopWatchdogMonitor();
            
            // 创建退出标记文件，通知看门狗主进程正常退出
            var exitFlagFile = Path.Combine(AppContext.BaseDirectory, "exit.flag");
            var currentProcess = Process.GetCurrentProcess();
            var timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var exitFlagContent = $"{currentProcess.Id}|{timestamp}";
            File.WriteAllText(exitFlagFile, exitFlagContent);
            Console.WriteLine($"Created exit.flag file with PID={currentProcess.Id}, timestamp={timestamp}");
            
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
