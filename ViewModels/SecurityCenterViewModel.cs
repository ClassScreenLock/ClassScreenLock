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
    private bool _isInitializing;

    private static string L(string key) => LocalizationService.Instance.GetString(key) ?? key;

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
    private bool _enableSoftwareSecurity = true;

    [ObservableProperty]
    private bool _enableLockStateFileCheck;

    [ObservableProperty]
    private int _lockStateFileCheckIntervalSeconds;

    [ObservableProperty]
    private ObservableCollection<string> _loginVerificationModeOptions = new()
    {
        L("SecurityCenter_PasswordPlusTwoFactor"),
        L("SecurityCenter_PasswordOrTwoFactor"),
        L("SecurityCenter_PasswordOnly"),
        L("SecurityCenter_TwoFactorOnly")
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
    private ObservableCollection<string> _authorizationLevels = new() { 
        L("SecurityCenter_None"), 
        L("SecurityCenter_OrdinaryUser"), 
        L("SecurityCenter_Administrator"), 
        L("SecurityCenter_SuperAdministrator") 
    };

    [ObservableProperty]
    private string _exitAppLevel = "无";

    [ObservableProperty]
    private string _sidebarHomeLevel = "无";
    [ObservableProperty]
    private string _sidebarLockSettingsLevel = "无";
    [ObservableProperty]
    private string _breakTimeLockSettingsLevel = "无";
    [ObservableProperty]
    private string _sidebarScheduleLevel = "无";
    [ObservableProperty]
    private string _sidebarAppManagementLevel = "无";
    [ObservableProperty]
    private string _sidebarNetworkInterceptionLevel = "无";
    [ObservableProperty]
    private string _sidebarSecurityLogsLevel = "无";
    [ObservableProperty]
    private string _sidebarScreenshotHistoryLevel = "无";
    [ObservableProperty]
    private string _sidebarWebcamHistoryLevel = "无";
    [ObservableProperty]
    private string _sidebarAutomationLevel = "无";
    [ObservableProperty]
    private string _sidebarSecurityCenterLevel = "无";
    [ObservableProperty]
    private string _sidebarOrganizationLevel = "无";
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
        _isInitializing = true;
        
        // 强制刷新设置以确保获取最新数据
        var settings = SecurityService.Instance.Settings;
        RemainingAttempts = Math.Max(0, 10 - settings.FailedCount);
        IsAuthenticated = SecurityService.Instance.IsAuthenticated;
        UpdateSuperAdminStatus();

        if (settings.LockoutUntil.HasValue && settings.LockoutUntil.Value > DateTime.Now)
        { 
            IsLocked = true;
            LockoutMessage = $"{L("SecurityCenter_Msg_AccountLocked")} {settings.LockoutUntil:yyyy-MM-dd HH:mm:ss}";
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
        BreakTimeLockSettingsLevel = ToLevelText(lockSettings.BreakTimeLockSettingsMinAccountType);
        SidebarScheduleLevel = ToLevelText(lockSettings.SidebarScheduleMinAccountType);
        SidebarAppManagementLevel = ToLevelText(lockSettings.SidebarAppManagementMinAccountType);
        SidebarNetworkInterceptionLevel = ToLevelText(lockSettings.SidebarNetworkInterceptionMinAccountType);
        SidebarSecurityLogsLevel = ToLevelText(lockSettings.SidebarSecurityLogsMinAccountType);
        SidebarScreenshotHistoryLevel = ToLevelText(lockSettings.SidebarScreenshotHistoryMinAccountType);
        SidebarWebcamHistoryLevel = ToLevelText(lockSettings.SidebarWebcamHistoryMinAccountType);
        SidebarAutomationLevel = ToLevelText(lockSettings.SidebarAutomationMinAccountType);
        SidebarSecurityCenterLevel = ToLevelText(lockSettings.SidebarSecurityCenterMinAccountType);
        SidebarOrganizationLevel = ToLevelText(lockSettings.SidebarOrganizationMinAccountType);
        SidebarSettingsLevel = ToLevelText(lockSettings.SidebarSettingsMinAccountType);
        SidebarAboutLevel = ToLevelText(lockSettings.SidebarAboutMinAccountType);

        // 加载双重验证状态
        IsTwoFactorEnabled = SecurityService.Instance.Settings.IsTwoFactorEnabled;
        IsTwoFactorConfigured = IsTwoFactorEnabled;
        EnableSoftwareSecurity = SecurityService.Instance.Settings.EnableSoftwareSecurity;
        _suppressLoginVerificationModeSave = true;
        SelectedLoginVerificationMode = ToLoginVerificationModeText(SecurityService.Instance.Settings.LoginVerificationMode);
        _suppressLoginVerificationModeSave = false;
        RefreshLoginFieldVisibility();
        LoadLockSettings();
        
        _isInitializing = false;
    }

    private void InitializeMaxLockDurationOptions()
    {
        if (_maxLockDurationOptions != null)
        {
            return;
        }
        
        _maxLockDurationOptions = new ObservableCollection<string>();
        _maxLockDurationOptions.Add(L("SecurityCenter_Unlimited"));
        
        for (int i = 6; i <= 120; i++)
        {
            _maxLockDurationOptions.Add($"{i} {L("SecurityCenter_Hours")}");
        }
    }

    private void UpdateSuperAdminStatus()
    {
        var current = AccountService.Instance.CurrentAccount;
        var securityLoggedIn = SecurityService.Instance.IsAuthenticated;
        
        IsAuthenticated = securityLoggedIn || (current != null && (current.AccountType == AccountType.SuperAdmin || current.AccountType == AccountType.Admin));
        IsSuperAdmin = (current != null && current.AccountType == AccountType.SuperAdmin) || securityLoggedIn;
        
        if (IsSuperAdmin)
        {
            AccountTypeText = L("Account_Type_SuperAdmin");
        }
        else if (current != null)
        {
            AccountTypeText = current.AccountType switch
            {
                AccountType.Admin => L("Account_Type_Admin"),
                _ => L("Account_Type_User")
            };
        }
        else
        {
            AccountTypeText = L("SecurityCenter_Unknown");
        }

        if (current != null)
        {
            Username = current.Username;
            AccountStatusText = current.IsLocked ? L("SecurityCenter_Locked") : L("SecurityCenter_LoggedIn");
            var loginTime = AccountService.Instance.CurrentLoginTime;
            LoginTimeText = loginTime.HasValue ? loginTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : L("SecurityCenter_Unknown");
        }
        else if (securityLoggedIn)
        {
            var superAdmin = AccountService.Instance.Accounts.FirstOrDefault(a => a.AccountType == AccountType.SuperAdmin && !a.IsDisabled);
            Username = superAdmin?.Username ?? SecurityService.Instance.Settings.AdminUsername;
            AccountStatusText = L("SecurityCenter_LoggedIn");
            LoginTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        else
        {
            Username = string.Empty;
            AccountStatusText = L("SecurityCenter_NotLoggedIn");
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
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_CompleteAdminLoginFirst"));
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
            // 如果系统层面的 2FA 未启用，但账户层面启用了，则显示验证码输入框
            IsLoginTwoFactorVisible = AccountService.Instance.IsAccountTwoFactorEnabled(Username);
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

    partial void OnUsernameChanged(string value)
    {
        RefreshLoginFieldVisibility();
    }

    private static AdminLoginVerificationMode ParseLoginVerificationMode(string text)
    {
        if (text == L("SecurityCenter_PasswordPlusTwoFactor")) return AdminLoginVerificationMode.PasswordAndTwoFactor;
        if (text == L("SecurityCenter_PasswordOrTwoFactor")) return AdminLoginVerificationMode.PasswordOrTwoFactor;
        if (text == L("SecurityCenter_PasswordOnly")) return AdminLoginVerificationMode.PasswordOnly;
        if (text == L("SecurityCenter_TwoFactorOnly")) return AdminLoginVerificationMode.TwoFactorOnly;
        return AdminLoginVerificationMode.PasswordAndTwoFactor;
    }

    private static string ToLoginVerificationModeText(AdminLoginVerificationMode mode)
    {
        return mode switch
        {
            AdminLoginVerificationMode.PasswordAndTwoFactor => L("SecurityCenter_PasswordPlusTwoFactor"),
            AdminLoginVerificationMode.PasswordOrTwoFactor => L("SecurityCenter_PasswordOrTwoFactor"),
            AdminLoginVerificationMode.PasswordOnly => L("SecurityCenter_PasswordOnly"),
            AdminLoginVerificationMode.TwoFactorOnly => L("SecurityCenter_TwoFactorOnly"),
            _ => L("SecurityCenter_PasswordPlusTwoFactor")
        };
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_EnterUsername"));
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
                NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_EnterPassword"));
                return;
            }
        }

        if (mode == AdminLoginVerificationMode.TwoFactorOnly)
        {
            if (string.IsNullOrWhiteSpace(LoginTwoFactorCode))
            {
                NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_EnterTwoFactorCode"));
                return;
            }
        }
        else if (mode == AdminLoginVerificationMode.PasswordOrTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(passwordCopy) && string.IsNullOrWhiteSpace(LoginTwoFactorCode))
            {
                NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_EnterPasswordOrTwoFactor"));
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
                LoginMessage = L("SecurityCenter_Msg_EnterTwoFactorToContinue");
                return;
            }

            LoginMessage = passwordResult.Message;
            return;
        }

        var result = await SecurityService.Instance.VerifyPasswordAsync(Username, passwordCopy, LoginTwoFactorCode);

        switch (result.Status)
        {
            case PasswordVerificationStatus.Success:
                {
                    IsAuthenticated = true;
                    IsLocked = false;
                    RemainingAttempts = result.RemainingAttempts;
                    LoginMessage = result.Message;
                    LoginPassword = string.Empty;
                    IsTwoFactorRequired = false;
                    LoginTwoFactorCode = string.Empty;
                    
                    var accountLoggedIn = false;
                    if (!string.IsNullOrWhiteSpace(passwordCopy))
                    {
                        var (success, _) = await AccountService.Instance.LoginAsync(Username, passwordCopy);
                        accountLoggedIn = success;
                    }
                    if (!accountLoggedIn)
                    {
                        var securitySessionOk = AccountService.Instance.LoginFromSecuritySession(Username);
                        if (!securitySessionOk)
                        {
                            SecurityService.Instance.Logout();
                            IsAuthenticated = false;
                            RemainingAttempts = result.RemainingAttempts;
                            LoginMessage = L("SecurityCenter_Msg_AccountNotFoundOrDisabled");
                            NotificationService.Instance.ShowWarning(LoginMessage);
                            return;
                        }
                    }
                    
                    UpdateSuperAdminStatus();
                    RefreshAccounts();

                    if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                        desktop.MainWindow?.DataContext is MainWindowViewModel mainVm)
                    {
                        mainVm.SidebarViewModel.RefreshAccountInfo();
                    }

                    NotificationService.Instance.ShowSuccess(L("SecurityCenter_Msg_AdminLoginSuccess"));
                }
                break;
            case PasswordVerificationStatus.LockedOut:
                {
                    IsAuthenticated = false;
                    IsLocked = true;
                    RemainingAttempts = 0;
                    LockoutMessage = result.Message;
                    if (result.LockoutUntil.HasValue)
                    {
                        LockoutMessage += $"，{L("SecurityCenter_UnlockTime")}: {result.LockoutUntil:yyyy-MM-dd HH:mm:ss}";
                    }
                    NotificationService.Instance.ShowError(LockoutMessage);
                }
                break;
            case PasswordVerificationStatus.NotConfigured:
                {
                    IsAuthenticated = false;
                    LoginMessage = result.Message;
                    NotificationService.Instance.ShowWarning(result.Message);
                }
                break;
            default:
                {
                    IsAuthenticated = false;
                    RemainingAttempts = result.RemainingAttempts;
                    LoginMessage = result.Message;
                    NotificationService.Instance.ShowWarning(result.Message);

                    if (!string.IsNullOrWhiteSpace(passwordCopy))
                    {
                        var (ok, msg) = await AccountService.Instance.LoginAsync(Username, passwordCopy, LoginTwoFactorCode);
                        if (ok)
                        {
                            UpdateSuperAdminStatus();
                            var current = AccountService.Instance.CurrentAccount;
                            if (current != null && (current.AccountType == AccountType.Admin || current.AccountType == AccountType.SuperAdmin))
                            {
                                SecurityService.Instance.ResetFailedAttempts();
                                RemainingAttempts = 10;
                                
                                IsAuthenticated = true;
                                LoginMessage = string.Empty;
                                NotificationService.Instance.ShowSuccess(L("SecurityCenter_Msg_AdminLoginSuccess"));
                                
                                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                                    desktop.MainWindow?.DataContext is MainWindowViewModel mainVm)
                                {
                                    mainVm.SidebarViewModel.RefreshAccountInfo();
                                }
                            }
                            else
                            {
                                NotificationService.Instance.ShowSuccess(L("SecurityCenter_Msg_AccountLoginSuccessLowPrivilege"));
                                LoginMessage = L("SecurityCenter_Msg_LoggedInAsAccount");
                            }
                        }
                        else
                        {
                            NotificationService.Instance.ShowWarning(msg);
                        }
                    }
                }
                break;
        }

        RefreshReport();
    }

    [RelayCommand]
    private void Logout()
    {
        SecurityService.Instance.Logout();
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
        LoginMessage = L("SecurityCenter_Msg_LoggedOut");
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
            AccountType.SuperAdmin => L("SecurityCenter_SuperAdministrator"),
            AccountType.Admin => L("SecurityCenter_Administrator"),
            AccountType.User => L("SecurityCenter_OrdinaryUser"),
            _ => L("SecurityCenter_None")
        };
    }

    private static AccountType? FromLevelText(string text)
    {
        if (text == L("SecurityCenter_SuperAdministrator")) return AccountType.SuperAdmin;
        if (text == L("SecurityCenter_Administrator")) return AccountType.Admin;
        if (text == L("SecurityCenter_OrdinaryUser")) return AccountType.User;
        return null;
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
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_InsufficientPermission"));
            return;
        }

        var exitApp = FromLevelText(ExitAppLevel);

        var sbHome = FromLevelText(SidebarHomeLevel);
        var sbLock = FromLevelText(SidebarLockSettingsLevel);
        var brLock = FromLevelText(BreakTimeLockSettingsLevel);
        var sbSchedule = FromLevelText(SidebarScheduleLevel);
        var sbApp = FromLevelText(SidebarAppManagementLevel);
        var sbNet = FromLevelText(SidebarNetworkInterceptionLevel);
        var sbLogs = FromLevelText(SidebarSecurityLogsLevel);
        var sbHistory = FromLevelText(SidebarScreenshotHistoryLevel);
        var sbWebcam = FromLevelText(SidebarWebcamHistoryLevel);
        var sbAutomation = FromLevelText(SidebarAutomationLevel);
        var sbSec = FromLevelText(SidebarSecurityCenterLevel);
        var sbSettings = FromLevelText(SidebarSettingsLevel);
        var sbAbout = FromLevelText(SidebarAboutLevel);

        var before = SettingsService.Lock;
        SettingsService.UpdateLock(s =>
        {
            s.ExitAppMinAccountType = exitApp;

            s.SidebarHomeMinAccountType = sbHome;
            s.SidebarLockSettingsMinAccountType = sbLock;
            s.BreakTimeLockSettingsMinAccountType = brLock;
            s.SidebarScheduleMinAccountType = sbSchedule;
            s.SidebarAppManagementMinAccountType = sbApp;
            s.SidebarNetworkInterceptionMinAccountType = sbNet;
            s.SidebarSecurityLogsMinAccountType = sbLogs;
            s.SidebarScreenshotHistoryMinAccountType = sbHistory;
            s.SidebarWebcamHistoryMinAccountType = sbWebcam;
            s.SidebarAutomationMinAccountType = sbAutomation;
            s.SidebarSecurityCenterMinAccountType = sbSec;
            s.SidebarSettingsMinAccountType = sbSettings;
            s.SidebarAboutMinAccountType = sbAbout;
        });

        LogPermissionChange("SidebarHome", before.SidebarHomeMinAccountType, sbHome);
        LogPermissionChange("SidebarLockSettings", before.SidebarLockSettingsMinAccountType, sbLock);
        LogPermissionChange("BreakTimeLockSettings", before.BreakTimeLockSettingsMinAccountType, brLock);
        LogPermissionChange("SidebarSchedule", before.SidebarScheduleMinAccountType, sbSchedule);
        LogPermissionChange("SidebarAppManagement", before.SidebarAppManagementMinAccountType, sbApp);
        LogPermissionChange("SidebarNetworkInterception", before.SidebarNetworkInterceptionMinAccountType, sbNet);
        LogPermissionChange("SidebarSecurityLogs", before.SidebarSecurityLogsMinAccountType, sbLogs);
        LogPermissionChange("SidebarScreenshotHistory", before.SidebarScreenshotHistoryMinAccountType, sbHistory);
        LogPermissionChange("SidebarWebcamHistory", before.SidebarWebcamHistoryMinAccountType, sbWebcam);
        LogPermissionChange("SidebarAutomation", before.SidebarAutomationMinAccountType, sbAutomation);
        LogPermissionChange("SidebarSecurityCenter", before.SidebarSecurityCenterMinAccountType, sbSec);
        LogPermissionChange("SidebarSettings", before.SidebarSettingsMinAccountType, sbSettings);
        LogPermissionChange("SidebarAbout", before.SidebarAboutMinAccountType, sbAbout);

        NotificationService.Instance.ShowSuccess(L("SecurityCenter_Msg_PermissionsUpdated"));
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
            BreakTimeLockSettingsLevel = ToLevelText(lockSettings.BreakTimeLockSettingsMinAccountType);
            SidebarScheduleLevel = ToLevelText(lockSettings.SidebarScheduleMinAccountType);
            SidebarAppManagementLevel = ToLevelText(lockSettings.SidebarAppManagementMinAccountType);
            SidebarNetworkInterceptionLevel = ToLevelText(lockSettings.SidebarNetworkInterceptionMinAccountType);
            SidebarSecurityLogsLevel = ToLevelText(lockSettings.SidebarSecurityLogsMinAccountType);
            SidebarScreenshotHistoryLevel = ToLevelText(lockSettings.SidebarScreenshotHistoryMinAccountType);
            SidebarWebcamHistoryLevel = ToLevelText(lockSettings.SidebarWebcamHistoryMinAccountType);
            SidebarAutomationLevel = ToLevelText(lockSettings.SidebarAutomationMinAccountType);
            SidebarSecurityCenterLevel = ToLevelText(lockSettings.SidebarSecurityCenterMinAccountType);
            SidebarOrganizationLevel = ToLevelText(lockSettings.SidebarOrganizationMinAccountType);
            SidebarSettingsLevel = ToLevelText(lockSettings.SidebarSettingsMinAccountType);
            SidebarAboutLevel = ToLevelText(lockSettings.SidebarAboutMinAccountType);
            return;
        }

        var before = SettingsService.Lock;
        var sbHome = FromLevelText(SidebarHomeLevel);
        var sbLock = FromLevelText(SidebarLockSettingsLevel);
        var brLock = FromLevelText(BreakTimeLockSettingsLevel);
        var sbSchedule = FromLevelText(SidebarScheduleLevel);
        var sbApp = FromLevelText(SidebarAppManagementLevel);
        var sbNet = FromLevelText(SidebarNetworkInterceptionLevel);
        var sbLogs = FromLevelText(SidebarSecurityLogsLevel);
        var sbHistory = FromLevelText(SidebarScreenshotHistoryLevel);
        var sbWebcam = FromLevelText(SidebarWebcamHistoryLevel);
        var sbAutomation = FromLevelText(SidebarAutomationLevel);
        var sbSec = FromLevelText(SidebarSecurityCenterLevel);
        var sbOrg = FromLevelText(SidebarOrganizationLevel);
        var sbSettings = FromLevelText(SidebarSettingsLevel);
        var sbAbout = FromLevelText(SidebarAboutLevel);

        SettingsService.UpdateLock(s =>
        {
            s.SidebarHomeMinAccountType = sbHome;
            s.SidebarLockSettingsMinAccountType = sbLock;
            s.BreakTimeLockSettingsMinAccountType = brLock;
            s.SidebarScheduleMinAccountType = sbSchedule;
            s.SidebarAppManagementMinAccountType = sbApp;
            s.SidebarNetworkInterceptionMinAccountType = sbNet;
            s.SidebarSecurityLogsMinAccountType = sbLogs;
            s.SidebarScreenshotHistoryMinAccountType = sbHistory;
            s.SidebarWebcamHistoryMinAccountType = sbWebcam;
            s.SidebarAutomationMinAccountType = sbAutomation;
            s.SidebarSecurityCenterMinAccountType = sbSec;
            s.SidebarOrganizationMinAccountType = sbOrg;
            s.SidebarSettingsMinAccountType = sbSettings;
            s.SidebarAboutMinAccountType = sbAbout;
        });

        LogPermissionChange("SidebarHome", before.SidebarHomeMinAccountType, sbHome);
        LogPermissionChange("SidebarLockSettings", before.SidebarLockSettingsMinAccountType, sbLock);
        LogPermissionChange("BreakTimeLockSettings", before.BreakTimeLockSettingsMinAccountType, brLock);
        LogPermissionChange("SidebarSchedule", before.SidebarScheduleMinAccountType, sbSchedule);
        LogPermissionChange("SidebarAppManagement", before.SidebarAppManagementMinAccountType, sbApp);
        LogPermissionChange("SidebarNetworkInterception", before.SidebarNetworkInterceptionMinAccountType, sbNet);
        LogPermissionChange("SidebarSecurityLogs", before.SidebarSecurityLogsMinAccountType, sbLogs);
        LogPermissionChange("SidebarScreenshotHistory", before.SidebarScreenshotHistoryMinAccountType, sbHistory);
        LogPermissionChange("SidebarWebcamHistory", before.SidebarWebcamHistoryMinAccountType, sbWebcam);
        LogPermissionChange("SidebarAutomation", before.SidebarAutomationMinAccountType, sbAutomation);
        LogPermissionChange("SidebarSecurityCenter", before.SidebarSecurityCenterMinAccountType, sbSec);
        LogPermissionChange("SidebarOrganization", before.SidebarOrganizationMinAccountType, sbOrg);
        LogPermissionChange("SidebarSettings", before.SidebarSettingsMinAccountType, sbSettings);
        LogPermissionChange("SidebarAbout", before.SidebarAboutMinAccountType, sbAbout);
    }

    [ObservableProperty]
    private bool _canEditBreakTimeLock;

    [ObservableProperty]
    private bool _enableBreakTimeLock;

    [ObservableProperty]
    private LockMode _breakTimeLockMode;

    [ObservableProperty]
    private decimal _autoUnlockBeforeClassMinutes;

    [ObservableProperty]
    private int _lockTimeout;

    [ObservableProperty]
    private bool _showFloatingLockWidget;

    [ObservableProperty]
    private int _earlyUnlockMinAccountTypeIndex;

    [ObservableProperty]
    private double _lockBackgroundOpacity;

    [ObservableProperty]
    private double _lockTextShadowOpacity;

    [ObservableProperty]
    private double _lockTextShadowBlurRadius;

    [ObservableProperty]
    private int _maxLockDurationHours = 48;

    [ObservableProperty]
    private int _maxLockDurationIndex = 19;

    private ObservableCollection<string>? _maxLockDurationOptions;
    public ObservableCollection<string> MaxLockDurationOptions 
    { 
        get
        {
            if (_maxLockDurationOptions == null)
            {
                InitializeMaxLockDurationOptions();
            }
            return _maxLockDurationOptions!;
        }
    }

    [ObservableProperty]
    private string _newAllowedApp = string.Empty;

    [ObservableProperty]
    private string _newForcedApp = string.Empty;

    public ObservableCollection<string> AllowedTopmostApps { get; } = new();
    public ObservableCollection<string> ForcedTopmostApps { get; } = new();

    [RelayCommand]
    private void SaveLockSettings()
    {
        SettingsService.UpdateLock(settings =>
        {
            settings.EnableBreakTimeLock = EnableBreakTimeLock;
            settings.BreakTimeLockMode = BreakTimeLockMode;
            settings.AutoUnlockBeforeClassMinutes = (int)AutoUnlockBeforeClassMinutes;
            settings.LockTimeout = LockTimeout;
            settings.ShowFloatingLockWidget = ShowFloatingLockWidget;
            settings.EarlyUnlockMinAccountType = (AccountType)EarlyUnlockMinAccountTypeIndex;
            settings.AllowedTopmostApps = AllowedTopmostApps.ToList();
            settings.ForcedTopmostApps = ForcedTopmostApps.ToList();
            settings.LockBackgroundOpacity = LockBackgroundOpacity;
            settings.LockTextShadowOpacity = LockTextShadowOpacity;
            settings.LockTextShadowBlurRadius = LockTextShadowBlurRadius;
        });
        SettingsService.UpdateGeneral(settings =>
        {
            settings.MaxLockDurationHours = MaxLockDurationHours;
        });
        NotificationService.Instance.ShowSuccess(LocalizationService.Instance.GetString("Notify_SettingsSaved") ?? "设置已保存");
        if (!ShowFloatingLockWidget)
        {
            FloatingWidgetService.Instance.HideWidget();
        }
        else
        {
            LockScreenService.Instance.RefreshBreakWidgetVisibility();
        }
    }

    private void LoadLockSettings()
    {
        var settings = SettingsService.Lock;
        EnableBreakTimeLock = settings.EnableBreakTimeLock;
        BreakTimeLockMode = settings.BreakTimeLockMode;
        AutoUnlockBeforeClassMinutes = settings.AutoUnlockBeforeClassMinutes;
        LockTimeout = settings.LockTimeout;
        ShowFloatingLockWidget = settings.ShowFloatingLockWidget;
        EarlyUnlockMinAccountTypeIndex = (int)settings.EarlyUnlockMinAccountType;
        LockBackgroundOpacity = settings.LockBackgroundOpacity;
        LockTextShadowOpacity = settings.LockTextShadowOpacity;
        LockTextShadowBlurRadius = settings.LockTextShadowBlurRadius;

        MaxLockDurationHours = SettingsService.General.MaxLockDurationHours;

        AllowedTopmostApps.Clear();
        foreach (var app in settings.AllowedTopmostApps)
        {
            AllowedTopmostApps.Add(app);
        }

        ForcedTopmostApps.Clear();
        foreach (var app in settings.ForcedTopmostApps)
        {
            ForcedTopmostApps.Add(app);
        }

        EnableLockStateFileCheck = settings.EnableLockStateFileCheck;
        LockStateFileCheckIntervalSeconds = settings.LockStateFileCheckIntervalSeconds;

        CanEditBreakTimeLock = settings.BreakTimeLockSettingsMinAccountType == null
                               || SecurityService.Instance.IsAuthenticated
                               || AccountService.Instance.HasPermission(settings.BreakTimeLockSettingsMinAccountType.Value);
    }

    [RelayCommand]
    private void AddAllowedApp()
    {
        if (!string.IsNullOrWhiteSpace(NewAllowedApp) && !AllowedTopmostApps.Contains(NewAllowedApp))
        {
            AllowedTopmostApps.Add(NewAllowedApp);
            NewAllowedApp = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveAllowedApp(string app)
    {
        AllowedTopmostApps.Remove(app);
    }

    [RelayCommand]
    private void AddForcedApp()
    {
        if (!string.IsNullOrWhiteSpace(NewForcedApp) && !ForcedTopmostApps.Contains(NewForcedApp))
        {
            ForcedTopmostApps.Add(NewForcedApp);
            NewForcedApp = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveForcedApp(string app)
    {
        ForcedTopmostApps.Remove(app);
    }

    partial void OnSidebarHomeLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarLockSettingsLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnBreakTimeLockSettingsLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarScheduleLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarAppManagementLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarNetworkInterceptionLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarSecurityLogsLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarSecurityCenterLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarOrganizationLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarSettingsLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();
    partial void OnSidebarAboutLevelChanged(string value) => ApplySidebarPermissionLevelsImmediate();

    partial void OnEnableSoftwareSecurityChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }
        
        if (!IsAuthenticated)
        {
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_CompleteAdminLoginFirst"));
            EnableSoftwareSecurity = !value;
            return;
        }

        WindowProtectionService.Instance.SetSoftwareSecurityEnabled(value);
        NotificationService.Instance.ShowSuccess($"{L("SecurityCenter_SoftwareSecurity")} {(value ? L("SecurityCenter_Enabled") : L("SecurityCenter_Disabled"))}");
    }

    partial void OnEnableLockStateFileCheckChanged(bool value)
    {
        if (!IsAuthenticated) return;

        SettingsService.UpdateLock(settings => settings.EnableLockStateFileCheck = value);
        if (value)
        {
            LockScreenService.Instance.StartLockStateFileCheck();
        }
        else
        {
            LockScreenService.Instance.StopLockStateFileCheck();
        }
        NotificationService.Instance.ShowSuccess($"{L("SecurityCenter_LockStateFileCheck")} {(value ? L("SecurityCenter_Enabled") : L("SecurityCenter_Disabled"))}");
    }

    partial void OnLockStateFileCheckIntervalSecondsChanged(int value)
    {
        if (!IsAuthenticated) return;

        var clampedValue = Math.Clamp(value, 1, 60);
        if (clampedValue != value)
        {
            LockStateFileCheckIntervalSeconds = clampedValue;
            return;
        }

        SettingsService.UpdateLock(settings => settings.LockStateFileCheckIntervalSeconds = clampedValue);
        if (EnableLockStateFileCheck)
        {
            LockScreenService.Instance.StopLockStateFileCheck();
            LockScreenService.Instance.StartLockStateFileCheck();
        }
    }

    [RelayCommand]
    private void ExitApp()
    {
        var required = SettingsService.Lock.ExitAppMinAccountType;
        if (required != null && !HasPrivilege(required.Value))
        {
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_InsufficientPermission"));
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
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_EnterUsername"));
            return;
        }

        if (string.IsNullOrWhiteSpace(NewAccountPassword))
        {
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_EnterPassword"));
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
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_CannotDeleteSuperAdmin"));
            return;
        }

        var confirmed = await NotificationService.Instance.ShowConfirmAsync(
            $"{L("SecurityCenter_Msg_DeleteAccountConfirm")} \"{account.Username}\"?",
            L("SecurityCenter_DeleteConfirm"));

        if (!confirmed) return;

        var result = await AccountService.Instance.DeleteAccountAsync(account.Id);
        if (result.success)
        {
            NotificationService.Instance.ShowSuccess(result.message);
            RefreshAccounts();
            
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
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_CompleteAdminLoginFirst"));
            return;
        }

        var result = await SecurityService.Instance.ChangePasswordAsync(Username, CurrentPassword, NewPassword, ConfirmPassword);

        if (result.Success)
        {
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            PasswordValidationErrors = string.Empty;
            PasswordStrengthLabel = L("SecurityCenter_None");
            PasswordStrengthScore = 0;
            NotificationService.Instance.ShowSuccess(result.Message);
        }
        else
        {
            PasswordValidationErrors = string.Join("；", result.Errors);
            NotificationService.Instance.ShowWarning(string.IsNullOrEmpty(result.Message) ? L("SecurityCenter_Msg_PasswordChangeFailed") : result.Message);
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
            NotificationService.Instance.ShowInfo(L("SecurityCenter_Msg_StrongPasswordCopied"));
        }
        else
        {
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_ClipboardAccessFailed"));
        }
    }

    [RelayCommand]
    private async Task SetupTwoFactorAsync()
    {
        if (!IsAuthenticated)
        {
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_CompleteAdminLoginFirst"));
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
                NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_EnterCurrentPasswordToConfirm"));
                IsTwoFactorEnabled = true;
                return;
            }

            var confirmed = await NotificationService.Instance.ShowConfirmAsync(L("SecurityCenter_Msg_ConfirmDisableTwoFactor"), L("SecurityCenter_SecurityWarning"));
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
                    NotificationService.Instance.ShowSuccess(L("SecurityCenter_Msg_TwoFactorDisabled"));
                }
                else
                {
                    NotificationService.Instance.ShowError(result.Message);
                    IsTwoFactorEnabled = true;
                }
            }
            else
            {
                IsTwoFactorEnabled = true;
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
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_CompleteAdminLoginFirst"));
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
            NotificationService.Instance.ShowSuccess(L("SecurityCenter_Msg_TwoFactorSetupSuccess"));
        }
        else
        {
            NotificationService.Instance.ShowError(result.Message);
        }
    }

    [RelayCommand]
    private void CancelTwoFactorSetup()
    {
        if (!IsAuthenticated)
        {
            NotificationService.Instance.ShowWarning(L("SecurityCenter_Msg_CompleteAdminLoginFirst"));
        }

        IsSettingUpTwoFactor = false;
        TwoFactorInputCode = string.Empty;
        IsTwoFactorEnabled = SecurityService.Instance.Settings.IsTwoFactorEnabled;
        IsTwoFactorConfigured = IsTwoFactorEnabled;
        RefreshLoginFieldVisibility();
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
        PasswordValidationErrors = string.Join(",", policy.Errors);
    }

    partial void OnMaxLockDurationIndexChanged(int value)
    {
        MaxLockDurationHours = value switch
        {
            0 => 0,
            >= 1 and <= 115 => value + 5,
            _ => 48
        };
    }

    partial void OnMaxLockDurationHoursChanged(int value)
    {
        MaxLockDurationIndex = value switch
        {
            0 => 0,
            >= 6 and <= 120 => value - 5,
            _ => 19
        };
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
