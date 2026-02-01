using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using ClassScreenLock.Services;
using ClassScreenLock.Models;
using System;

namespace ClassScreenLock.ViewModels;

public partial class SidebarViewModel : ViewModelBase
    {
        [ObservableProperty]
        private double _sidebarWidth = 250;
        
        [ObservableProperty]
        private bool _isExpanded = true;
        
        [ObservableProperty]
        private string _toggleIcon = "fas fa-chevron-left";
        
        [ObservableProperty]
        private string _toggleText = string.Empty;

        [ObservableProperty]
        private string _accountTypeText = string.Empty;

        [ObservableProperty]
        private string _accountName = string.Empty;

        [ObservableProperty]
        private string _accountStatusText = string.Empty;

        [ObservableProperty]
        private string _loginTimeText = string.Empty;

        [ObservableProperty]
        private string _accountIcon = "fa-user";

        [ObservableProperty]
        private string _accountDetailText = string.Empty;

        [ObservableProperty]
        private bool _isAccountLoading;

        [ObservableProperty]
        private bool _hasAccountError;

        [ObservableProperty]
        private bool _isSuperAdmin;

        [ObservableProperty]
    private string _loginUsername = string.Empty;

    [ObservableProperty]
    private string _loginPassword = string.Empty;

    [ObservableProperty]
    private bool _isInitialized;

    private MainWindowViewModel? _mainWindowViewModel;
    private bool _disposed = false;

    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new ObservableCollection<MenuItemViewModel>();
        
        public SidebarViewModel()
        {
            // 初始化本地化文本
            _toggleText = IsExpanded ? 
                LocalizationService.Instance.GetString("Sidebar_Collapse") : 
                LocalizationService.Instance.GetString("Sidebar_Expand");
            _toggleIcon = IsExpanded ? "fas fa-chevron-left" : "fas fa-chevron-right";
            _sidebarWidth = IsExpanded ? 250 : 50;

            // 订阅语言变化事件
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            
            // 延迟初始化菜单项，确保Application.Current已经完全初始化
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(100);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RefreshMenuItems();
                    RefreshAccountInfo();
                });
            });
        }
        
        // 语言变化时刷新侧边栏菜单项
        private void OnLanguageChanged(object? sender, string e)
        {
            if (!_disposed)
            {
                RefreshMenuItems();
                RefreshAccountInfo();
            }
        }
    
    // 刷新菜单项的本地化文本
    public void RefreshMenuItems()
    {
        MenuItems.Clear();
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_Home"), "fa-home", NavigateCommand, "Home"));
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_Schedule"), "fa-calendar-alt", NavigateCommand, "Schedule"));
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_AppManagement"), "fa-th-large", NavigateCommand, "AppManagement"));
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_NetworkInterception"), "fa-network-wired", NavigateCommand, "NetworkInterception"));
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_SecurityLogs"), "fa-clipboard-list", NavigateCommand, "SecurityLogs"));
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_SecurityCenter"), "fas fa-user-shield", NavigateCommand, "SecurityCenter"));
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_ScreenshotHistory"), "fas fa-camera", NavigateCommand, "ScreenshotHistory"));
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_Settings"), "fa-cog", NavigateCommand, "Settings"));
        MenuItems.Add(new MenuItemViewModel(LocalizationService.Instance.GetString("Sidebar_About"), "fa-info-circle", NavigateCommand, "About"));
    }

        [ObservableProperty]
        private bool _isLoggedIn;

        public void RefreshAccountInfo()
        {
            try
            {
                IsAccountLoading = true;
                HasAccountError = false;

                var current = AccountService.Instance.CurrentAccount;
                IsInitialized = AccountService.Instance.IsInitialized;
                var securityLoggedIn = SecurityService.Instance.IsAuthenticated;
                IsLoggedIn = current != null || securityLoggedIn;

                if (current == null)
                {
                    AccountTypeText = string.Empty;
                    LoginTimeText = string.Empty;
                    IsSuperAdmin = false;

                    if (IsInitialized)
                    {
                        if (securityLoggedIn)
                        {
                            AccountStatusText = LocalizationService.Instance.GetString("Account_Status_LoggedIn");
                            AccountName = SecurityService.Instance.Settings.AdminUsername;
                            AccountIcon = "fa-user-shield";
                            AccountTypeText = LocalizationService.Instance.GetString("Account_Type_Admin");
                            AccountDetailText = LocalizationService.Instance.GetString("Account_Status_LoggedIn");
                            IsSuperAdmin = true; // 管理员通过安全中心登录，赋予超级管理员权限以管理账户
                        }
                        else
                        {
                            AccountStatusText = LocalizationService.Instance.GetString("Account_Status_LoggedOut");
                            AccountName = string.Empty;
                            AccountIcon = "fa-user";
                            AccountDetailText = LocalizationService.Instance.GetString("Account_Status_LoggedOut");
                        }
                    }
                    else
                    {
                        AccountStatusText = LocalizationService.Instance.GetString("Account_Init_Title");
                        AccountName = string.Empty;
                        AccountIcon = "fa-user";
                        AccountDetailText = LocalizationService.Instance.GetString("Account_Init_Subtitle");
                    }

                    return;
                }

                AccountName = current.Username;
                AccountTypeText = current.AccountType switch
                {
                    AccountType.SuperAdmin => LocalizationService.Instance.GetString("Account_Type_SuperAdmin"),
                    AccountType.Admin => LocalizationService.Instance.GetString("Account_Type_Admin"),
                    _ => LocalizationService.Instance.GetString("Account_Type_User")
                };

                AccountStatusText = current.IsLocked
                    ? LocalizationService.Instance.GetString("Account_Status_Locked")
                    : LocalizationService.Instance.GetString("Account_Status_LoggedIn");

                var loginTime = AccountService.Instance.CurrentLoginTime;
                LoginTimeText = loginTime.HasValue ? loginTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
                IsSuperAdmin = (current.AccountType == AccountType.SuperAdmin) || securityLoggedIn;

                AccountIcon = current.IsLocked ? "fa-user-lock" : "fa-user-circle";

                AccountDetailText = string.Join("\n", new[]
                {
                    LocalizationService.Instance.GetString("Account_Username") + ": " + current.Username,
                    LocalizationService.Instance.GetString("Account_Type") + ": " + AccountTypeText,
                    LocalizationService.Instance.GetString("Account_LoginTime") + ": " + LoginTimeText,
                    LocalizationService.Instance.GetString("Account_Status") + ": " + AccountStatusText
                });
            }
            catch
            {
                HasAccountError = true;
                IsLoggedIn = false;
                AccountName = string.Empty;
                AccountTypeText = string.Empty;
                AccountStatusText = LocalizationService.Instance.GetString("Account_Status_Error");
                LoginTimeText = string.Empty;
                AccountIcon = "fa-user-slash";
                AccountDetailText = LocalizationService.Instance.GetString("Account_Status_ErrorDetail");
            }
            finally
            {
                IsAccountLoading = false;
            }
        }
    
    public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
    {
        _mainWindowViewModel = mainWindowViewModel;
    }
    
    partial void OnIsExpandedChanged(bool value)
    {
        if (_disposed) return;
        
        // 立即更新图标和文本内容
        ToggleIcon = value ? "fas fa-chevron-left" : "fas fa-chevron-right";
        ToggleText = value ? LocalizationService.Instance.GetString("Sidebar_Collapse") : LocalizationService.Instance.GetString("Sidebar_Expand");
        
        // 立即更新宽度，Avalonia 的 DoubleTransition 会处理动画
        SidebarWidth = value ? 250 : 50;
    }
    
    [RelayCommand]
    private void ToggleSidebar()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (!IsInitialized)
        {
            _mainWindowViewModel?.NavigateToInitialization();
            return;
        }
        var username = string.IsNullOrWhiteSpace(LoginUsername) ? "superadmin" : LoginUsername.Trim();
        var password = LoginPassword ?? string.Empty;

        var result = await AccountService.Instance.LoginAsync(username, password);
        if (result.success)
        {
            LoginPassword = string.Empty;
            RefreshAccountInfo();
            NotificationService.Instance.ShowSuccess(result.message);
        }
        else
        {
            NotificationService.Instance.ShowWarning(result.message);
        }
    }

    [RelayCommand]
    private void Logout()
    {
        AccountService.Instance.Logout();
        RefreshAccountInfo();
        _mainWindowViewModel?.NavigateToHome(); // 登出后跳转到主页
        NotificationService.Instance.ShowInfo(LocalizationService.Instance.GetString("Account_Status_LoggedOut"));
    }

    [RelayCommand]
    private void Navigate(string target)
    {
        if (!IsInitialized)
        { 
            _mainWindowViewModel?.NavigateToInitialization();
            return;
        }

        if (!CanAccessSidebar(target))
        {
            NotificationService.Instance.ShowWarning("权限不足或未登录，无法访问该功能");
            _mainWindowViewModel?.NavigateToSecurityCenter();
            return;
        }

        if (_disposed) return;

        switch (target)
        {
            case "Home":
                _mainWindowViewModel?.NavigateToHome();
                break;
            // 锁屏设置已迁移至安全中心
            case "Schedule":
                _mainWindowViewModel?.NavigateToSchedule();
                break;
            case "AppManagement":
                _mainWindowViewModel?.NavigateToAppManagement();
                break;
            case "NetworkInterception":
                _mainWindowViewModel?.NavigateToNetworkInterception();
                break;
            case "SecurityCenter":
                _mainWindowViewModel?.NavigateToSecurityCenter();
                break;
            case "ScreenshotHistory":
                _mainWindowViewModel?.NavigateToScreenshotHistory();
                break;
            case "SecurityLogs":
                _mainWindowViewModel?.NavigateToSecurityLogs();
                break;
            case "Settings":
                _mainWindowViewModel?.NavigateToSettings();
                break;
            case "About":
                _mainWindowViewModel?.NavigateToAbout();
                break;
        }
    }

    private bool CanAccessSidebar(string target)
    {
        var s = SettingsService.Lock;
        AccountType? required = target switch
        {
            "Home" => s.SidebarHomeMinAccountType,
            "LockSettings" => s.SidebarLockSettingsMinAccountType,
            "Schedule" => s.SidebarScheduleMinAccountType,
            "AppManagement" => s.SidebarAppManagementMinAccountType,
            "NetworkInterception" => s.SidebarNetworkInterceptionMinAccountType,
            "SecurityLogs" => s.SidebarSecurityLogsMinAccountType,
            "ScreenshotHistory" => s.SidebarScreenshotHistoryMinAccountType,
            "SecurityCenter" => s.SidebarSecurityCenterMinAccountType,
            "Settings" => s.SidebarSettingsMinAccountType,
            "About" => s.SidebarAboutMinAccountType,
            _ => null
        };

        if (required == null) return true;
        if (SecurityService.Instance.IsAuthenticated) return true;
        return AccountService.Instance.HasPermission(required.Value);
    }
    
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 取消订阅事件
                if (LocalizationService.Instance != null)
                {
                    LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
                }
                
                // 清理菜单项
                MenuItems.Clear();
                
                // 清理引用
                _mainWindowViewModel = null;
            }
            
            _disposed = true;
        }
        
        // 调用基类的Dispose方法
        base.Dispose(disposing);
    }
}

public partial class MenuItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title;
    
    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    private string _target = string.Empty;
    
    [ObservableProperty]
    private ICommand _command;
    
    public MenuItemViewModel(string title, string icon, ICommand command, string target = "")
    {
        Title = title;
        Icon = icon;
        Command = command;
        Target = target;
    }
}
