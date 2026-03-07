using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Styling;
using Avalonia;
using Avalonia.Controls;
using ClassScreenLock.Views;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Services;
using ClassScreenLock.Models;
using System;

namespace ClassScreenLock.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _status = "准备就绪";
    
    [ObservableProperty]
    private ThemeVariant _currentTheme = ThemeVariant.Light;
    
    [ObservableProperty]
    private SidebarViewModel _sidebarViewModel;
    
    [ObservableProperty]
    private UserControl? _currentView;
    
    [ObservableProperty]
    private HomeViewModel _homeViewModel;

    [ObservableProperty]
    private SettingsViewModel _settingsViewModel;

    [ObservableProperty]
    private AppManagementViewModel _appManagementViewModel;

    [ObservableProperty]
    private NetworkManagementViewModel _networkManagementViewModel;

    [ObservableProperty]
    private LogManagementViewModel _logManagementViewModel;

    // 锁屏设置已迁移至安全中心

    [ObservableProperty]
    private ScheduleViewModel _scheduleViewModel;

    [ObservableProperty]
    private SecurityCenterViewModel _securityCenterViewModel;

    [ObservableProperty]
    private ScreenshotHistoryViewModel _screenshotHistoryViewModel;

    [ObservableProperty]
    private WebcamHistoryViewModel _webcamHistoryViewModel;

    [ObservableProperty]
    private AutomationViewModel _automationViewModel;

    [ObservableProperty]
    private OrganizationViewModel _organizationViewModel;

    [ObservableProperty]
    private bool _isInitialized;
    
    private bool _disposed = false;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyLockActive))]
    private bool _isLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyLockActive))]
    private bool _isProtectionOnlyActive;

    public bool IsAnyLockActive => IsLocked || IsProtectionOnlyActive;

    [ObservableProperty]
    private bool _isOnboarding;

    public string Greeting { get; } = "欢迎使用 ClassScreenLock!";
    public string AppVersion { get; } = new AboutViewModel().AppVersion;
    
    public MainWindowViewModel()
    {
        SidebarViewModel = new SidebarViewModel();
        // 设置关于页面的引用
        SidebarViewModel.SetMainWindowViewModel(this);
        
        // 创建主页视图模型实例
        HomeViewModel = new HomeViewModel(this);

        // 创建设置视图模型实例
        SettingsViewModel = new SettingsViewModel();
        
        // 创建应用管理视图模型实例
        AppManagementViewModel = new AppManagementViewModel();

        // 创建网络拦截视图模型实例
        NetworkManagementViewModel = new NetworkManagementViewModel();

        // 创建日志管理视图模型实例
        LogManagementViewModel = new LogManagementViewModel();

        // 锁屏设置已迁移至安全中心

        // 创建时间计划视图模型实例
        ScheduleViewModel = new ScheduleViewModel();

        // 创建密码安全中心视图模型实例
        SecurityCenterViewModel = new SecurityCenterViewModel();

        // 创建截图历史视图模型实例
        ScreenshotHistoryViewModel = new ScreenshotHistoryViewModel();

        WebcamHistoryViewModel = new WebcamHistoryViewModel();

// 创建自动化视图模型实例
        AutomationViewModel = new AutomationViewModel();

        // 创建组织管理视图模型实例
        OrganizationViewModel = new OrganizationViewModel();
        
        // 监听锁定状态
        IsLocked = LockScreenService.Instance.IsLocked;
        IsProtectionOnlyActive = LockScreenService.Instance.IsProtectionOnlyActive;
        LockScreenService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LockScreenService.IsLocked))
            {
                IsLocked = LockScreenService.Instance.IsLocked;
            }
            else if (e.PropertyName == nameof(LockScreenService.IsProtectionOnlyActive))
            {
                IsProtectionOnlyActive = LockScreenService.Instance.IsProtectionOnlyActive;
            }
        };

        // 应用设置
        ApplySettings();

        // 检查初始化状态
        CheckInitialization();
    }

    private void CheckInitialization()
    {
        var required = InitializationService.Instance.RequiresInitialization;
        IsInitialized = !required;
        if (required)
        {
            NavigateToInitialization();
        }
        else
        {
            NavigateToHome();
        }
    }

    public void NavigateToInitialization()
    {
        var vm = new InitializationViewModel(this);
        vm.StepIndex = 0;
        CurrentView = new InitializationView { DataContext = vm };
        Status = "系统初始化";
        IsOnboarding = true;
    }

    public void NavigateToHome()
    {
        OnViewChanging();
        HomeViewModel.UpdateStatus();
        CurrentView = new HomeView { DataContext = HomeViewModel };
        Status = "主界面";
        IsOnboarding = false;
    }

    [RelayCommand]
    private void StartLock()
    {
        Status = "屏幕锁定已启动";
        LockScreenService.Instance.ActivateLock(LockMode.Full);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        // 切换主题并更新设置
        CurrentTheme = CurrentTheme == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light;
        Application.Current!.RequestedThemeVariant = CurrentTheme;

        // 更新设置中的暗黑模式状态
        SettingsViewModel.DarkMode = (CurrentTheme == ThemeVariant.Dark);

        Status = CurrentTheme == ThemeVariant.Light ? "已切换到浅色主题" : "已切换到深色主题";
    }

    private void OnViewChanging()
    {
        // 停止所有具有后台刷新任务的视图模型的定时器
        AppManagementViewModel?.StopRefreshTimer();
        // 如果有其他视图模型也需要停止，可以在这里添加
    }

    public void NavigateToAbout()
    {
        OnViewChanging();
        CurrentView = new About();
        Status = "关于页面";
    }

    public void NavigateToSchedule()
    {
        OnViewChanging();
        CurrentView = new ScheduleView { DataContext = ScheduleViewModel };
        Status = "时间计划";
        IsOnboarding = false;
    }

    // 锁屏设置已迁移至安全中心
    
    public void NavigateToSettings()
    {
        OnViewChanging();
        CurrentView = new SettingsView { DataContext = SettingsViewModel };
        Status = "系统设置";
        IsOnboarding = false;
    }

    public void NavigateToAppManagement()
    {
        LogService.Instance.Log("Navigation", "AppManagement", "MainWindowViewModel", "正在跳转到应用管理页面");
        OnViewChanging();
        AppManagementViewModel.RefreshAppsCommand.Execute(null);
        AppManagementViewModel.StartRefreshTimer();
        CurrentView = new AppManagementView { DataContext = AppManagementViewModel };
        Status = "应用管理";
        IsOnboarding = false;
    }

    public void NavigateToNetworkInterception()
    {
        OnViewChanging();
        CurrentView = new NetworkManagementView { DataContext = NetworkManagementViewModel };
        Status = "网络拦截";
        IsOnboarding = false;
    }

    public void NavigateToSecurityLogs()
    {
        OnViewChanging();
        CurrentView = new LogManagementView { DataContext = LogManagementViewModel };
        LogManagementViewModel.RefreshLogsCommand.Execute(null);
        Status = "安全日志";
        IsOnboarding = false;
    }

    public void NavigateToSecurityCenter()
    {
        OnViewChanging();
        CurrentView = new SecurityCenterView { DataContext = SecurityCenterViewModel };
        Status = "安全中心";
        IsOnboarding = false;
    }

    public void NavigateToScreenshotHistory()
    {
        OnViewChanging();
        ScreenshotHistoryViewModel.LoadScreenshotsCommand.Execute(null);
        CurrentView = new ScreenshotHistoryView { DataContext = ScreenshotHistoryViewModel };
        Status = "屏幕记录";
        IsOnboarding = false;
    }

    public void NavigateToWebcamHistory()
    {
        OnViewChanging();
        WebcamHistoryViewModel.LoadScreenshotsCommand.Execute(null);
        CurrentView = new WebcamHistoryView { DataContext = WebcamHistoryViewModel };
        Status = "摄像头记录";
        IsOnboarding = false;
    }

    public void NavigateToAutomation()
    {
        OnViewChanging();
        CurrentView = new AutomationView { DataContext = AutomationViewModel };
        Status = "自动化扩展";
        IsOnboarding = false;
    }

    public void NavigateToOrganization()
    {
        OnViewChanging();
        // 使用已初始化的 OrganizationViewModel 实例，避免状态丢失
        CurrentView = new OrganizationView { DataContext = OrganizationViewModel };
        Status = "组织管理";
        IsOnboarding = false;
    }

    private void ApplySettings()
    {
        var settings = SettingsService.General;
        
        // 应用主题设置
        CurrentTheme = settings.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        Application.Current!.RequestedThemeVariant = CurrentTheme;
    }
    
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 释放托管资源
                SidebarViewModel?.Dispose();
                SettingsViewModel?.Dispose();
                AppManagementViewModel?.Dispose();
                CurrentView = null;
            }
            
            _disposed = true;
        }
        
        // 调用基类的Dispose方法
        base.Dispose(disposing);
    }
}
