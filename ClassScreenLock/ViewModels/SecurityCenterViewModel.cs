using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Avalonia.Media.Imaging;
using ClassScreenLock.Services;
using ClassScreenLock.Models;

namespace ClassScreenLock.ViewModels;

public partial class SecurityCenterViewModel : ViewModelBase
{
    private bool _suppressLoginVerificationModeSave;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _loginPassword = string.Empty;

    [ObservableProperty]
    private bool _isPasswordVisible;

    public char PasswordChar => IsPasswordVisible ? '\0' : '●';

    public string PasswordIcon => IsPasswordVisible ? "fas fa-eye-slash" : "fas fa-eye";

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
        OnPropertyChanged(nameof(PasswordChar));
        OnPropertyChanged(nameof(PasswordIcon));
    }

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isSuperAdmin;

    [ObservableProperty]
    private string _accountTypeText = string.Empty;

    [ObservableProperty]
    private string _loginMessage = string.Empty;

    [ObservableProperty]
    private string _accountStatusText = string.Empty;

    [ObservableProperty]
    private string _loginTimeText = string.Empty;

    [ObservableProperty]
    private int _remainingAttempts = 10;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _lockoutMessage = string.Empty;

    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private int _passwordStrengthScore;

    [ObservableProperty]
    private string _passwordStrengthLabel = "无";

    [ObservableProperty]
    private string _passwordValidationErrors = string.Empty;

    [ObservableProperty]
    private string _hibpStatus = string.Empty;

    [ObservableProperty]
    private bool _isTwoFactorEnabled;

    [ObservableProperty]
    private bool _isTwoFactorConfigured;

    [ObservableProperty]
    private ObservableCollection<string> _loginVerificationModeOptions = new()
    {
        "密码 + 双重验证码（全部都要）",
        "密码 或 双重验证码（任意其一）",
        "仅密码",
        "仅双重验证码"
    };

    [ObservableProperty]
    private string _selectedLoginVerificationMode = string.Empty;

    [ObservableProperty]
    private bool _isLoginPasswordVisible = true;

    [ObservableProperty]
    private bool _isLoginTwoFactorVisible;

    [ObservableProperty]
    private string _twoFactorSecret = string.Empty;

    [ObservableProperty]
    private string _twoFactorQrCodeData = string.Empty;

    [ObservableProperty]
    private Bitmap? _qrCodeBitmap;

    [ObservableProperty]
    private string _twoFactorInputCode = string.Empty;

    [ObservableProperty]
    private bool _isSettingUpTwoFactor;

    [ObservableProperty]
    private string _securityReportText = string.Empty;

    [ObservableProperty]
    private bool _isBiometricAvailable = SecurityService.Instance.IsBiometricAvailable;

    [ObservableProperty]
    private ObservableCollection<string> _authorizationLevels = new() { "无", "普通用户", "管理员", "超级管理员" };

    [ObservableProperty]
    private string _exitAppLevel = "无";

    [ObservableProperty]
    private string _sidebarHomeLevel = "无";
    [ObservableProperty]
    private string _sidebarLockSettingsLevel = "无";
    [ObservableProperty]
    private string _sidebarScheduleLevel = "无";
    [ObservableProperty]
    private string _sidebarAppManagementLevel = "无";
    [ObservableProperty]
    private string _sidebarNetworkInterceptionLevel = "无";
    [ObservableProperty]
    private string _sidebarSecurityLogsLevel = "无";
    [ObservableProperty]
    private string _sidebarSecurityCenterLevel = "无";
    [ObservableProperty]
    private string _sidebarSettingsLevel = "无";
    [ObservableProperty]
    private string _sidebarAboutLevel = "无";

    // 账户管理相关
    [ObservableProperty]
    private ObservableCollection<AccountModel> _accounts = new();

    [ObservableProperty]
    private string _newAccountUsername = string.Empty;

    [ObservableProperty]
    private string _newAccountPassword = string.Empty;

    [ObservableProperty]
    private AccountType _newAccountType = AccountType.User;

    [ObservableProperty]
    private ObservableCollection<AccountType> _availableAccountTypes = new() { AccountType.User, AccountType.Admin };

    public SecurityCenterViewModel()
    {
        // 强制刷新设置以确保获取最新数据
        var settings = SecurityService.Instance.Settings;
        RemainingAttempts = Math.Max(0, 10 - settings.FailedCount);
        IsAuthenticated = SecurityService.Instance.IsAuthenticated;
        UpdateSuperAdminStatus();

        if (settings.LockoutUntil.HasValue && settings.LockoutUntil.Value > DateTime.Now)
        { 
            IsLocked = true;
            LockoutMessage = $"账户已锁定，直到 {settings.LockoutUntil:yyyy-MM-dd HH:mm:ss}";
        }

        RefreshReport();
        if (IsAuthenticated)
        {
            RefreshAccounts();
        }

        var lockSettings = SettingsService.Lock;
        ExitAppLevel = ToLevelText(lockSettings.ExitAppMinAccountType);

        SidebarHomeLevel = ToLevelText(lockSettings.SidebarHomeMinAccountType);
        SidebarLockSettingsLevel = ToLevelText(lockSettings.SidebarLockSettingsMinAccountType);
        SidebarScheduleLevel = ToLevelText(lockSettings.SidebarScheduleMinAccountType);
        SidebarAppManagementLevel = ToLevelText(lockSettings.SidebarAppManagementMinAccountType);
        SidebarNetworkInterceptionLevel = ToLevelText(lockSettings.SidebarNetworkInterceptionMinAccountType);
        SidebarSecurityLogsLevel = ToLevelText(lockSettings.SidebarSecurityLogsMinAccountType);
        SidebarSecurityCenterLevel = ToLevelText(lockSettings.SidebarSecurityCenterMinAccountType);
        SidebarSettingsLevel = ToLevelText(lockSettings.SidebarSettingsMinAccountType);
        SidebarAboutLevel = ToLevelText(lockSettings.SidebarAboutMinAccountType);

        // 加载双重验证状态
        IsTwoFactorEnabled = SecurityService.Instance.Settings.IsTwoFactorEnabled;
        IsTwoFactorConfigured = IsTwoFactorEnabled;
        _suppressLoginVerificationModeSave = true;
        SelectedLoginVerificationMode = ToLoginVerificationModeText(SecurityService.Instance.Settings.LoginVerificationMode);
        _suppressLoginVerificationModeSave = false;
        RefreshLoginFieldVisibility();
    }

    private void UpdateSuperAdminStatus()
    {
        var current = AccountService.Instance.CurrentAccount;
        var securityLoggedIn = SecurityService.Instance.IsAuthenticated;
        IsAuthenticated = securityLoggedIn;
        IsSuperAdmin = (current != null && current.AccountType == AccountType.SuperAdmin) || securityLoggedIn;
        
        if (IsSuperAdmin)
        {
            AccountTypeText = LocalizationService.Instance.GetString("Account_Type_SuperAdmin") ?? "超级管理员";
        }
        else if (current != null)
        {
            AccountTypeText = current.AccountType switch
            {
                AccountType.Admin => LocalizationService.Instance.GetString("Account_Type_Admin") ?? "管理员",
                _ => LocalizationService.Instance.GetString("Account_Type_User") ?? "普通用户"
            };
        }
        else
        {
            AccountTypeText = "未知";
        }

        // 更新登录状态和时间
        if (current != null)
        {
            Username = current.Username;
            AccountStatusText = current.IsLocked ? "已锁定" : "已登录";
            var loginTime = AccountService.Instance.CurrentLoginTime;
            LoginTimeText = loginTime.HasValue ? loginTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "未知";
        }
        else if (securityLoggedIn)
        {
            Username = SecurityService.Instance.Settings.AdminUsername;
            AccountStatusText = "已登录";
            LoginTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); // 安全中心管理员登录通常没有持久化登录时间，显示当前时间
        }
        else
        {
            Username = string.Empty;
            AccountStatusText = "未登录";
            LoginTimeText = string.Empty;
        }
    }

    [ObservableProperty]
    private string _loginTwoFactorCode = string.Empty;

    [ObservableProperty]
    private bool _isTwoFactorRequired;

    partial void OnSelectedLoginVerificationModeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (_suppressLoginVerificationModeSave) return;

        if (!IsAuthenticated)
        {
            NotificationService.Instance.ShowWarning("请先完成管理员登录");
            _suppressLoginVerificationModeSave = true;
            SelectedLoginVerificationMode = ToLoginVerificationModeText(SecurityService.Instance.Settings.LoginVerificationMode);
            _suppressLoginVerificationModeSave = false;
            return;
        }

        SecurityService.Instance.SetLoginVerificationMode(ParseLoginVerificationMode(value));
        RefreshLoginFieldVisibility();
    }

    partial void OnIsTwoFactorRequiredChanged(bool value)
    {
        RefreshLoginFieldVisibility();
    }

    private void RefreshLoginFieldVisibility()
    {
        var mode = SecurityService.Instance.GetEffectiveLoginVerificationMode();
        IsLoginPasswordVisible = mode != AdminLoginVerificationMode.TwoFactorOnly;

        if (!SecurityService.Instance.Settings.IsTwoFactorEnabled)
        {
            IsLoginTwoFactorVisible = false;
            return;
        }

        IsLoginTwoFactorVisible = mode switch
        {
            AdminLoginVerificationMode.PasswordAndTwoFactor => IsTwoFactorRequired,
            AdminLoginVerificationMode.PasswordOrTwoFactor => true,
            AdminLoginVerificationMode.TwoFactorOnly => true,
            _ => false
        };
    }

    private static AdminLoginVerificationMode ParseLoginVerificationMode(string text)
    {
        return text switch
        {
            "密码 + 双重验证码（全部都要）" => AdminLoginVerificationMode.PasswordAndTwoFactor,
            "密码 或 双重验证码（任意其一）" => AdminLoginVerificationMode.PasswordOrTwoFactor,
            "仅密码" => AdminLoginVerificationMode.PasswordOnly,
            "仅双重验证码" => AdminLoginVerificationMode.TwoFactorOnly,
            _ => AdminLoginVerificationMode.PasswordAndTwoFactor
        };
    }

    private static string ToLoginVerificationModeText(AdminLoginVerificationMode mode)
    {
        return mode switch
        {
            AdminLoginVerificationMode.PasswordAndTwoFactor => "密码 + 双重验证码（全部都要）",
            AdminLoginVerificationMode.PasswordOrTwoFactor => "密码 或 双重验证码（任意其一）",
            AdminLoginVerificationMode.PasswordOnly => "仅密码",
            AdminLoginVerificationMode.TwoFactorOnly => "仅双重验证码",
            _ => "密码 + 双重验证码（全部都要）"
        };
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            NotificationService.Instance.ShowWarning("请输入用户名");
            return;
        }

        var passwordCopy = LoginPassword;
        LoginMessage = string.Empty;
        LockoutMessage = string.Empty;

        var mode = SecurityService.Instance.GetEffectiveLoginVerificationMode();

        if (mode is AdminLoginVerificationMode.PasswordOnly or AdminLoginVerificationMode.PasswordAndTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(passwordCopy))
            {
                NotificationService.Instance.ShowWarning("请输入密码");
                return;
            }
        }

        if (mode == AdminLoginVerificationMode.TwoFactorOnly)
        {
            if (string.IsNullOrWhiteSpace(LoginTwoFactorCode))
            {
                NotificationService.Instance.ShowWarning("请输入双重验证码");
                return;
            }
        }
        else if (mode == AdminLoginVerificationMode.PasswordOrTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(passwordCopy) && string.IsNullOrWhiteSpace(LoginTwoFactorCode))
            {
                NotificationService.Instance.ShowWarning("请输入密码或双重验证码");
                return;
            }
        }

        if (mode == AdminLoginVerificationMode.PasswordAndTwoFactor && SecurityService.Instance.Settings.IsTwoFactorEnabled && !IsTwoFactorRequired)
        {
            var passwordResult = await SecurityService.Instance.VerifyPasswordOnlyAsync(Username, passwordCopy);
            if (passwordResult.Status == PasswordVerificationStatus.Success)
            {
                IsTwoFactorRequired = true;
                RefreshLoginFieldVisibility();
                LoginMessage = "请输入双重验证码";
                return;
            }

            LoginMessage = passwordResult.Message;
            return;
        }

        var result = await SecurityService.Instance.VerifyPasswordAsync(Username, passwordCopy, LoginTwoFactorCode);

        switch (result.Status)
        {
            case PasswordVerificationStatus.Success:
                IsAuthenticated = true;
                IsLocked = false;
                RemainingAttempts = result.RemainingAttempts;
                LoginMessage = result.Message;
                LoginPassword = string.Empty;
                IsTwoFactorRequired = false;
                LoginTwoFactorCode = string.Empty;
                
                // 同步登录到 AccountService，确保可以进行账户管理
                var accountLoggedIn = false;
                if (!string.IsNullOrWhiteSpace(passwordCopy))
                {
                    var (success, _) = await AccountService.Instance.LoginAsync(Username, passwordCopy);
                    accountLoggedIn = success;
                }
                if (!accountLoggedIn)
                {
                    AccountService.Instance.LoginFromSecuritySession(Username);
                }
                
                UpdateSuperAdminStatus();
                RefreshAccounts();

                // 登录成功后通知侧边栏刷新
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow?.DataContext is MainWindowViewModel mainVm)
                {
                    mainVm.SidebarViewModel.RefreshAccountInfo();
                }

                NotificationService.Instance.ShowSuccess("管理员登录成功");
                break;
            case PasswordVerificationStatus.LockedOut:
                IsAuthenticated = false;
                IsLocked = true;
                RemainingAttempts = 0;
                LockoutMessage = result.Message;
                if (result.LockoutUntil.HasValue)
                {
                    LockoutMessage += $"，解锁时间：{result.LockoutUntil:yyyy-MM-dd HH:mm:ss}";
                }
                NotificationService.Instance.ShowError(LockoutMessage);
                break;
            case PasswordVerificationStatus.NotConfigured:
                IsAuthenticated = false;
                LoginMessage = result.Message;
                NotificationService.Instance.ShowWarning(result.Message);
                break;
            default:
                IsAuthenticated = false;
                RemainingAttempts = result.RemainingAttempts;
                LoginMessage = result.Message;
                NotificationService.Instance.ShowWarning(result.Message);
                break;
        }

        RefreshReport();
    }

    [RelayCommand]
    private void Logout()
    {
        SecurityService.Instance.Logout();
        // 确保 AccountService 也同步登出，防止权限状态不一致
        AccountService.Instance.Logout();
        
        IsAuthenticated = false;
        IsSuperAdmin = false;
        IsTwoFactorRequired = false;
        Username = string.Empty;
        LoginPassword = string.Empty;
        LoginTwoFactorCode = string.Empty;
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        LoginMessage = "已退出管理员登录";
        Accounts.Clear();

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is MainWindowViewModel mainVm)
        {
            mainVm.SidebarViewModel.RefreshAccountInfo();
        }
    }

    private static string ToLevelText(AccountType? type)
    {
        return type switch
        {
            AccountType.SuperAdmin => "超级管理员",
            AccountType.Admin => "管理员",
            AccountType.User => "普通用户",
            _ => "无"
        };
    }

    private static AccountType? FromLevelText(string text)
    {
        return text switch
        {
            "超级管理员" => AccountType.SuperAdmin,
            "管理员" => AccountType.Admin,
            "普通用户" => AccountType.User,
            _ => null
        };
    }

    private static AccountType ResolveRequired(AccountType? configured)
    {
        return configured ?? AccountType.Admin;
    }

    private static bool HasPrivilege(AccountType required)
    {
        if (SecurityService.Instance.IsAuthenticated) return true;
        return AccountService.Instance.HasPermission(required);
    }

    [RelayCommand]
    private void ApplyAuthorizationSettings()
    {
        if (!IsAuthenticated && !IsSuperAdmin)
        {
            NotificationService.Instance.ShowWarning("权限不足，无法修改权限配置");
            return;
        }

        var exitApp = FromLevelText(ExitAppLevel);

        var sbHome = FromLevelText(SidebarHomeLevel);
        var sbLock = FromLevelText(SidebarLockSettingsLevel);
        var sbSchedule = FromLevelText(SidebarScheduleLevel);
        var sbApp = FromLevelText(SidebarAppManagementLevel);
        var sbNet = FromLevelText(SidebarNetworkInterceptionLevel);
        var sbLogs = FromLevelText(SidebarSecurityLogsLevel);
        var sbSec = FromLevelText(SidebarSecurityCenterLevel);
        var sbSettings = FromLevelText(SidebarSettingsLevel);
        var sbAbout = FromLevelText(SidebarAboutLevel);

        var before = SettingsService.Lock;
        SettingsService.UpdateLock(s =>
        {
            s.ExitAppMinAccountType = exitApp;

            s.SidebarHomeMinAccountType = sbHome;
            s.SidebarLockSettingsMinAccountType = sbLock;
            s.SidebarScheduleMinAccountType = sbSchedule;
            s.SidebarAppManagementMinAccountType = sbApp;
            s.SidebarNetworkInterceptionMinAccountType = sbNet;
            s.SidebarSecurityLogsMinAccountType = sbLogs;
            s.SidebarSecurityCenterMinAccountType = sbSec;
            s.SidebarSettingsMinAccountType = sbSettings;
            s.SidebarAboutMinAccountType = sbAbout;
        });

        LogPermissionChange("SidebarHome", before.SidebarHomeMinAccountType, sbHome);
        LogPermissionChange("SidebarLockSettings", before.SidebarLockSettingsMinAccountType, sbLock);
        LogPermissionChange("SidebarSchedule", before.SidebarScheduleMinAccountType, sbSchedule);
        LogPermissionChange("SidebarAppManagement", before.SidebarAppManagementMinAccountType, sbApp);
        LogPermissionChange("SidebarNetworkInterception", before.SidebarNetworkInterceptionMinAccountType, sbNet);
        LogPermissionChange("SidebarSecurityLogs", before.SidebarSecurityLogsMinAccountType, sbLogs);
        LogPermissionChange("SidebarSecurityCenter", before.SidebarSecurityCenterMinAccountType, sbSec);
        LogPermissionChange("SidebarSettings", before.SidebarSettingsMinAccountType, sbSettings);
        LogPermissionChange("SidebarAbout", before.SidebarAboutMinAccountType, sbAbout);

        NotificationService.Instance.ShowSuccess("权限配置已更新");
    }

    private void LogPermissionChange(string name, AccountType? oldValue, AccountType? newValue)
    {
        var oldText = ToLevelText(oldValue);
        var newText = ToLevelText(newValue);
        if (oldText != newText)
        {
            LogService.Instance.Log("Permission", "Changed", name, $"{oldText} -> {newText}");
        }
    }

    private void ApplySidebarPermissionLevelsImmediate()
    {
        if (!IsAuthenticated && !IsSuperAdmin)
        {
            // 如果没有权限，我们将设置恢复到当前服务中的值，以防止UI显示不一致
            var lockSettings = SettingsService.Lock;
            ExitAppLevel = ToLevelText(lockSettings.ExitAppMinAccountType);
            SidebarHomeLevel = ToLevelText(lockSettings.SidebarHomeMinAccountType);
            SidebarLockSettingsLevel = ToLevelText(lockSettings.SidebarLockSettingsMinAccountType);
            SidebarScheduleLevel = ToLevelText(lockSettings.SidebarScheduleMinAccountType);
            SidebarAppManagementLevel = ToLevelText(lockSettings.SidebarAppManagementMinAccountType);
            SidebarNetworkInterceptionLevel = ToLevelText(lockSettings.SidebarNetworkInterceptionMinAccountType);
            SidebarSecurityLogsLevel = ToLevelText(lockSettings.SidebarSecurityLogsMinAccountType);
            SidebarSecurityCenterLevel = ToLevelText(lockSettings.SidebarSecurityCenterMinAccountType);
            SidebarSettingsLevel = ToLevelText(lockSettings.SidebarSettingsMinAccountType);
            SidebarAboutLevel = ToLevelText(lockSettings.SidebarAboutMinAccountType);
            return;
        }

        var before = SettingsService.Lock;
        var sbHome = FromLevelText(SidebarHomeLevel);
        var sbLock = FromLevelText(SidebarLockSettingsLevel);
        var sbSchedule = FromLevelText(SidebarScheduleLevel);
        var sbApp = FromLevelText(SidebarAppManagementLevel);
        var sbNet = FromLevelText(SidebarNetworkInterceptionLevel);
        var sbLogs = FromLevelText(SidebarSecurityLogsLevel);
        var sbSec = FromLevelText(SidebarSecurityCenterLevel);
        var sbSettings = FromLevelText(SidebarSettingsLevel);
        var sbAbout = FromLevelText(SidebarAboutLevel);

        SettingsService.UpdateLock(s =>
        {
            s.SidebarHomeMinAccountType = sbHome;
            s.SidebarLockSettingsMinAccountType = sbLock;
            s.SidebarScheduleMinAccountType = sbSchedule;
            s.SidebarAppManagementMinAccountType = sbApp;
            s.SidebarNetworkInterceptionMinAccountType = sbNet;
            s.SidebarSecurityLogsMinAccountType = sbLogs;
            s.SidebarSecurityCenterMinAccountType = sbSec;
            s.SidebarSettingsMinAccountType = sbSettings;
            s.SidebarAboutMinAccountType = sbAbout;
        });

        LogPermissionChange("SidebarHome", before.SidebarHomeMinAccountType, sbHome);
        LogPermissionChange("SidebarLockSettings", before.SidebarLockSettingsMinAccountType, sbLock);
        LogPermissionChange("SidebarSchedule", before.SidebarScheduleMinAccountType, sbSchedule);
        LogPermissionChange("SidebarAppManagement", before.SidebarAppManagementMinAccountType, sbApp);
        LogPermissionChange("SidebarNetworkInterception", before.SidebarNetworkInterceptionMinAccountType, sbNet);
        LogPermissionChange("SidebarSecurityLogs", before.SidebarSecurityLogsMinAccountType, sbLogs);
        LogPermissionChange("SidebarSecurityCenter", before.SidebarSecurityCenterMinAccountType, sbSec);
        LogPermissionChange("SidebarSettings", before.SidebarSettingsMinAccountType, sbSettings);
        LogPermissionChange("SidebarAbout", before.SidebarAboutMinAccountType, sbAbout);
    }

    partial void OnSidebarHomeLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarLockSettingsLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarScheduleLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarAppManagementLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarNetworkInterceptionLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarSecurityLogsLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarSecurityCenterLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarSettingsLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarAboutLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();

    [RelayCommand]
    private void ExitApp()
    {
        var required = SettingsService.Lock.ExitAppMinAccountType;
        if (required != null && !HasPrivilege(required.Value))
        {
            NotificationService.Instance.ShowWarning("权限不足，无法退出应用");
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand]
    private void RefreshAccounts()
    {
        if (!IsSuperAdmin) return;
        
        var accountList = AccountService.Instance.Accounts;
        Accounts = new ObservableCollection<AccountModel>(accountList);
    }

    [RelayCommand]
    private async Task CreateAccountAsync()
    {
        if (!IsSuperAdmin) return;

        if (string.IsNullOrWhiteSpace(NewAccountUsername))
        {
            NotificationService.Instance.ShowWarning("请输入用户名");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewAccountPassword))
        {
            NotificationService.Instance.ShowWarning("请输入密码");
            return;
        }

        var result = await AccountService.Instance.CreateSubAccountAsync(NewAccountUsername, NewAccountPassword, NewAccountType);
        if (result.success)
        {
            NotificationService.Instance.ShowSuccess(result.message);
            NewAccountUsername = string.Empty;
            NewAccountPassword = string.Empty;
            RefreshAccounts();
            
            // 通知侧边栏
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.DataContext is MainWindowViewModel mainVm)
            {
                mainVm.SidebarViewModel.RefreshAccountInfo();
            }
        }
        else
        {
            NotificationService.Instance.ShowError(result.message);
        }
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(AccountModel account)
    {
        if (!IsSuperAdmin || account == null) return;

        if (account.AccountType == AccountType.SuperAdmin)
        {
            NotificationService.Instance.ShowWarning("不能删除超级管理员账号");
            return;
        }

        var confirmed = await NotificationService.Instance.ShowConfirmAsync(
            $"确定要删除账户 \"{account.Username}\" 吗？",
            "删除确认");

        if (!confirmed) return;

        var result = await AccountService.Instance.DeleteAccountAsync(account.Id);
        if (result.success)
        {
            NotificationService.Instance.ShowSuccess(result.message);
            RefreshAccounts();
            
            // 通知侧边栏
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.DataContext is MainWindowViewModel mainVm)
            {
                mainVm.SidebarViewModel.RefreshAccountInfo();
            }
        }
        else
        {
            NotificationService.Instance.ShowError(result.message);
        }
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (!IsAuthenticated && !string.IsNullOrWhiteSpace(SecurityService.Instance.Settings.PasswordHash))
        {
            NotificationService.Instance.ShowWarning("请先完成管理员登录");
            return;
        }

        var result = await SecurityService.Instance.ChangePasswordAsync(Username, CurrentPassword, NewPassword, ConfirmPassword);

        if (result.Success)
        {
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            PasswordValidationErrors = string.Empty;
            PasswordStrengthLabel = "无";
            PasswordStrengthScore = 0;
            NotificationService.Instance.ShowSuccess(result.Message);
        }
        else
        {
            PasswordValidationErrors = string.Join("；", result.Errors);
            NotificationService.Instance.ShowWarning(string.IsNullOrEmpty(result.Message) ? "修改密码失败" : result.Message);
        }

        RefreshReport();
    }

    [RelayCommand]
    private async Task CheckLeakAsync()
    {
        HibpStatus = string.Empty;

        var result = await SecurityService.Instance.CheckPasswordLeakAsync(NewPassword);
        HibpStatus = result.Message;

        if (!result.Success)
        {
            NotificationService.Instance.ShowWarning(result.Message);
        }
        else if (result.IsPwned)
        {
            NotificationService.Instance.ShowError(result.Message);
        }
        else
        {
            NotificationService.Instance.ShowSuccess(result.Message);
        }

        RefreshReport();
    }

    [RelayCommand]
    private async Task GenerateStrongPasswordAsync()
    {
        var password = GenerateStrongPassword();

        NewPassword = password;
        ConfirmPassword = password;

        var success = await NotificationService.Instance.TrySetClipboardTextAsync(password);
        if (success)
        {
            NotificationService.Instance.ShowInfo("已生成强密码并复制到剪贴板，请使用密码管理器保存");
        }
        else
        {
            NotificationService.Instance.ShowWarning("无法访问剪贴板，但已在输入框中填入强密码");
        }
    }

    [RelayCommand]
    private async Task SetupTwoFactorAsync()
    {
        if (!IsAuthenticated)
        {
            NotificationService.Instance.ShowWarning("请先完成管理员登录");
            IsSettingUpTwoFactor = false;
            IsTwoFactorEnabled = SecurityService.Instance.Settings.IsTwoFactorEnabled;
            IsTwoFactorConfigured = IsTwoFactorEnabled;
            RefreshLoginFieldVisibility();
            return;
        }

        // 如果正在设置中，用户再次切换开关（即想要取消设置）
        if (IsSettingUpTwoFactor)
        {
            CancelTwoFactorSetup();
            return;
        }

        // 检查实际设置状态，而不是 UI 绑定的状态
        bool isEnabledInSettings = SecurityService.Instance.Settings.IsTwoFactorEnabled;

        if (isEnabledInSettings)
        {
            // 想要禁用
            if (string.IsNullOrWhiteSpace(CurrentPassword))
            {
                NotificationService.Instance.ShowWarning("请在“修改管理员密码”处输入当前密码以确认身份");
                IsTwoFactorEnabled = true; // 恢复 UI 状态
                return;
            }

            var confirmed = await NotificationService.Instance.ShowConfirmAsync("确定要禁用双重验证吗？", "安全警告");
            if (confirmed)
            {
                var result = await SecurityService.Instance.DisableTwoFactorAsync(CurrentPassword);
                if (result.Success)
                {
                    IsTwoFactorEnabled = false;
                    IsTwoFactorConfigured = false;
                    IsTwoFactorRequired = false;
                    LoginTwoFactorCode = string.Empty;
                    RefreshLoginFieldVisibility();
                    NotificationService.Instance.ShowSuccess("双重验证已禁用");
                }
                else
                {
                    NotificationService.Instance.ShowError(result.Message);
                    IsTwoFactorEnabled = true; // 恢复 UI 状态
                }
            }
            else
            {
                IsTwoFactorEnabled = true; // 恢复 UI 状态
            }
            return;
        }

        // 想要开启
        IsSettingUpTwoFactor = true;
        var setup = SecurityService.Instance.GenerateTwoFactorSetup(Username);
        TwoFactorSecret = setup.Secret;
        TwoFactorQrCodeData = setup.QrCodeUri;
        
        // 生成二维码图片
        try
        {
            var qrBytes = SecurityService.Instance.GenerateQrCode(setup.QrCodeUri);
            using var ms = new MemoryStream(qrBytes);
            QrCodeBitmap = new Bitmap(ms);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Security", "QrCodeGenerationError", Username, ex.Message);
            QrCodeBitmap = null;
        }

        // 此时 UI 上的 IsTwoFactorEnabled 已经是 true 了，但实际设置还没变
        // 我们在 VerifyAndEnableTwoFactorAsync 中才会真正更新它
    }

    [RelayCommand]
    private async Task VerifyAndEnableTwoFactorAsync()
    {
        if (!IsAuthenticated)
        {
            NotificationService.Instance.ShowWarning("请先完成管理员登录");
            return;
        }

        var result = await SecurityService.Instance.EnableTwoFactorAsync(TwoFactorSecret, TwoFactorInputCode);
        if (result.Success)
        {
            IsTwoFactorEnabled = true;
            IsTwoFactorConfigured = true;
            _suppressLoginVerificationModeSave = true;
            SelectedLoginVerificationMode = ToLoginVerificationModeText(SecurityService.Instance.Settings.LoginVerificationMode);
            _suppressLoginVerificationModeSave = false;
            RefreshLoginFieldVisibility();
            IsSettingUpTwoFactor = false;
            TwoFactorInputCode = string.Empty;
            NotificationService.Instance.ShowSuccess("双重验证已成功启用");
        }
        else
        {
            NotificationService.Instance.ShowError(result.Message);
            // 验证失败不需要立即恢复 IsTwoFactorEnabled，因为用户还在设置界面中
        }
    }

    [RelayCommand]
    private void CancelTwoFactorSetup()
    {
        if (!IsAuthenticated)
        {
            NotificationService.Instance.ShowWarning("请先完成管理员登录");
        }

        IsSettingUpTwoFactor = false;
        TwoFactorInputCode = string.Empty;
        IsTwoFactorEnabled = SecurityService.Instance.Settings.IsTwoFactorEnabled;
        IsTwoFactorConfigured = IsTwoFactorEnabled;
        RefreshLoginFieldVisibility();
    }

    [RelayCommand]
    private async Task UseBiometricAsync()
    {
        if (!SecurityService.Instance.IsBiometricAvailable)
        {
            NotificationService.Instance.ShowWarning("当前设备未配置生物识别认证");
            return;
        }

        var success = await SecurityService.Instance.AuthenticateWithBiometricsAsync();
        if (success)
        {
            IsAuthenticated = true;
            LoginMessage = "已通过生物识别完成管理员验证";
            NotificationService.Instance.ShowSuccess(LoginMessage);
        }
        else
        {
            NotificationService.Instance.ShowWarning("生物识别验证失败");
        }
    }

    [RelayCommand]
    private void RefreshReport()
    {
        var report = SecurityService.Instance.GenerateReport(TimeSpan.FromDays(30));

        var builder = new StringBuilder();
        builder.AppendLine("最近 30 天密码安全概览：");
        builder.AppendLine($"· 失败登录次数：{report.FailedLoginCount}");
        builder.AppendLine($"· 账户锁定次数：{report.LockoutCount}");
        builder.AppendLine($"· 密码修改次数：{report.PasswordChangeCount}");
        builder.AppendLine($"· 密码泄露告警次数：{report.LeakDetectedCount}");

        SecurityReportText = builder.ToString();
    }

    partial void OnNewPasswordChanged(string value)
    {
        var policy = SecurityService.Instance.ValidatePolicy(value ?? string.Empty);
        PasswordStrengthScore = policy.Score;
        PasswordStrengthLabel = policy.StrengthLabel;
        PasswordValidationErrors = string.Join("；", policy.Errors);
    }

    private static string GenerateStrongPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*()-_=+[]{}";

        var all = upper + lower + digits + symbols;
        var chars = new char[16];

        using var rng = RandomNumberGenerator.Create();

        chars[0] = GetRandomChar(rng, upper);
        chars[1] = GetRandomChar(rng, lower);
        chars[2] = GetRandomChar(rng, digits);
        chars[3] = GetRandomChar(rng, symbols);

        for (var i = 4; i < chars.Length; i++)
        {
            chars[i] = GetRandomChar(rng, all);
        }

        for (var i = chars.Length - 1; i > 0; i--)
        {
            var buffer = new byte[1];
            rng.GetBytes(buffer);
            var j = buffer[0] % (i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char GetRandomChar(RandomNumberGenerator rng, string chars)
    {
        var buffer = new byte[1];
        rng.GetBytes(buffer);
        var index = buffer[0] % chars.Length;
        return chars[index];
    }
}
