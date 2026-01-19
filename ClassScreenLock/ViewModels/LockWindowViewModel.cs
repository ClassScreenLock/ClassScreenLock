using System;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;
using ClassScreenLock.Models;

namespace ClassScreenLock.ViewModels;

public partial class LockWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _currentTime = string.Empty;

    [ObservableProperty]
    private string _countdownText = string.Empty;

    [ObservableProperty]
    private bool _hasCountdown;

    [ObservableProperty]
    private string _nextClassLabel = string.Empty;

    [ObservableProperty]
    private bool _isAutoUnlockEnabled;

    [ObservableProperty]
    private bool _isLoginVisible;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _twoFactorCode = string.Empty;

    [ObservableProperty]
    private bool _isTwoFactorRequired;

    [ObservableProperty]
    private bool _isPasswordInputVisible = true;

    [ObservableProperty]
    private bool _isTwoFactorInputVisible;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private string _focusedField = "Username"; // 默认聚焦用户名

    [ObservableProperty]
    private bool _isCapsLockEnabled;

    [ObservableProperty]
    private bool _isSymbolModeEnabled;

    [ObservableProperty]
    private double _lockBackgroundOpacity;

    [ObservableProperty]
    private double _lockTextShadowOpacity;

    [ObservableProperty]
    private double _lockTextShadowBlurRadius;

    public bool IsLetterModeVisible => !IsSymbolModeEnabled;

    public char PasswordChar => IsPasswordVisible ? '\0' : '*';

    public string PasswordIcon => IsPasswordVisible ? "fas fa-eye-slash" : "fas fa-eye";

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
        OnPropertyChanged(nameof(PasswordChar));
        OnPropertyChanged(nameof(PasswordIcon));
    }

    [RelayCommand]
    private void SetFocusedField(string fieldName)
    {
        FocusedField = fieldName;
    }

    [RelayCommand]
    private void AppendToFocusedField(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var field = ResolveFocusedField();

        if (text.Length == 1 && char.IsLetter(text[0]) && IsCapsLockEnabled)
        {
            text = text.ToUpper(CultureInfo.CurrentCulture);
        }

        if (field == "Username")
        {
            Username += text;
        }
        else if (field == "Password")
        {
            Password += text;
        }
        else if (field == "TwoFactor")
        {
            TwoFactorCode += text;
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        var field = ResolveFocusedField();
        if (field == "Username" && Username.Length > 0)
        {
            Username = Username.Substring(0, Username.Length - 1);
        }
        else if (field == "Password" && Password.Length > 0)
        {
            Password = Password.Substring(0, Password.Length - 1);
        }
        else if (field == "TwoFactor" && TwoFactorCode.Length > 0)
        {
            TwoFactorCode = TwoFactorCode.Substring(0, TwoFactorCode.Length - 1);
        }
    }

    [RelayCommand]
    private void ClearFocusedField()
    {
        var field = ResolveFocusedField();
        if (field == "Username")
        {
            Username = string.Empty;
        }
        else if (field == "Password")
        {
            Password = string.Empty;
        }
        else if (field == "TwoFactor")
        {
            TwoFactorCode = string.Empty;
        }
    }

    [RelayCommand]
    private void ToggleCapsLock()
    {
        UpdateCapsLockState(!IsCapsLockEnabled);
    }

    public void UpdateCapsLockState(bool isEnabled)
    {
        IsCapsLockEnabled = isEnabled;
    }

    [RelayCommand]
    private void ToggleSymbolMode()
    {
        IsSymbolModeEnabled = !IsSymbolModeEnabled;
    }

    partial void OnIsSymbolModeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLetterModeVisible));
    }

    private DispatcherTimer? _timer;
    private DateTime? _nextClassTime;

    public LockWindowViewModel()
    {
        UpdateCurrentTime();
        LoadScheduleInfo();

        var settings = SettingsService.Lock;

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnTimerTick);
        _timer.Start();

        IsAutoUnlockEnabled = settings.EnableBreakTimeLock;

        LockBackgroundOpacity = settings.LockBackgroundOpacity;
        LockTextShadowOpacity = settings.LockTextShadowOpacity;
        LockTextShadowBlurRadius = settings.LockTextShadowBlurRadius;

        RefreshLoginFieldVisibility();
    }

    partial void OnUsernameChanged(string value)
    {
        if (!IsTwoFactorRequired)
        {
            TwoFactorCode = string.Empty;
        }
        RefreshLoginFieldVisibility();
    }

    partial void OnIsTwoFactorRequiredChanged(bool value)
    {
        RefreshLoginFieldVisibility();
    }

    private static bool IsSecurityAdminUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        var settingsName = SecurityService.Instance.Settings.AdminUsername;
        if (string.Equals(username, settingsName, StringComparison.OrdinalIgnoreCase)) return true;

        var superAdminName = AccountService.Instance.Accounts.FirstOrDefault(a => a.AccountType == AccountType.SuperAdmin)?.Username;
        if (string.IsNullOrWhiteSpace(superAdminName)) return false;
        return string.Equals(username, superAdminName, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshLoginFieldVisibility()
    {
        if (!IsSecurityAdminUsername(Username))
        {
            IsPasswordInputVisible = true;
            IsTwoFactorInputVisible = false;
            EnsureFocusedField();
            return;
        }

        var mode = SecurityService.Instance.GetEffectiveLoginVerificationMode();
        IsPasswordInputVisible = mode != AdminLoginVerificationMode.TwoFactorOnly;

        if (!SecurityService.Instance.Settings.IsTwoFactorEnabled)
        {
            IsTwoFactorInputVisible = false;
            EnsureFocusedField();
            return;
        }

        IsTwoFactorInputVisible = mode switch
        {
            AdminLoginVerificationMode.PasswordAndTwoFactor => IsTwoFactorRequired,
            AdminLoginVerificationMode.PasswordOrTwoFactor => true,
            AdminLoginVerificationMode.TwoFactorOnly => true,
            _ => false
        };

        EnsureFocusedField();
    }

    private string ResolveFocusedField()
    {
        var field = string.IsNullOrWhiteSpace(FocusedField) ? "Username" : FocusedField;
        if (field == "Password" && !IsPasswordInputVisible)
        {
            field = IsTwoFactorInputVisible ? "TwoFactor" : "Username";
        }
        else if (field == "TwoFactor" && !IsTwoFactorInputVisible)
        {
            field = IsPasswordInputVisible ? "Password" : "Username";
        }

        return field;
    }

    private void EnsureFocusedField()
    {
        var resolved = ResolveFocusedField();
        if (FocusedField != resolved)
        {
            FocusedField = resolved;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateCurrentTime();
        UpdateCountdown();
    }

    private void UpdateCurrentTime()
    {
        CurrentTime = DateTime.Now.ToString("HH:mm:ss");
    }

    private void LoadScheduleInfo()
    {
        var now = DateTime.Now.TimeOfDay;
        var (_, next) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);

        if (next != null)
        {
            _nextClassTime = DateTime.Today.Add(next.StartTime);
            var timeLabel = next.StartTime.Hours.ToString("D2") + ":" + next.StartTime.Minutes.ToString("D2");
            NextClassLabel = string.IsNullOrWhiteSpace(next.Label)
                ? $"下节课 {timeLabel} 开始"
                : next.Label;
            HasCountdown = true;
        }
        else
        {
            _nextClassTime = null;
            NextClassLabel = string.Empty;
            CountdownText = string.Empty;
            HasCountdown = false;
        }
    }

    private void UpdateCountdown()
    {
        if (_nextClassTime == null)
        {
            CountdownText = string.Empty;
            HasCountdown = false;
            return;
        }

        var autoUnlockMinutes = SettingsService.Lock.AutoUnlockBeforeClassMinutes;
        var autoUnlockTime = _nextClassTime.Value.AddMinutes(-autoUnlockMinutes);
        
        var now = DateTime.Now;
        if (now >= _nextClassTime.Value)
        {
            CountdownText = "即将上课";
            HasCountdown = true;
            return;
        }

        if (now >= autoUnlockTime)
        {
            var remainingToClass = _nextClassTime.Value - now;
            CountdownText = $"即将自动解锁，距离上课 {remainingToClass.Minutes:D2}:{remainingToClass.Seconds:D2}";
            HasCountdown = true;
        }
        else
        {
            var remainingToUnlock = autoUnlockTime - now;
            CountdownText = $"距离自动解锁: {remainingToUnlock.Minutes:D2}:{remainingToUnlock.Seconds:D2}";
            HasCountdown = true;
        }
    }

    [RelayCommand]
    private void UnlockEarly()
    {
        IsLoginVisible = true;
        Username = string.Empty;
        Password = string.Empty;
        TwoFactorCode = string.Empty;
        IsTwoFactorRequired = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelLogin()
    {
        IsLoginVisible = false;
        Username = string.Empty;
        Password = string.Empty;
        TwoFactorCode = string.Empty;
        IsTwoFactorRequired = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task VerifyUnlock()
    {
        var settings = SettingsService.Lock;

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "请输入用户名";
            return;
        }

        var isSecurityAdmin = IsSecurityAdminUsername(Username);
        PasswordVerificationResult? securityResult = null;

        if (isSecurityAdmin)
        {
            var mode = SecurityService.Instance.GetEffectiveLoginVerificationMode();

            if (mode is AdminLoginVerificationMode.PasswordOnly or AdminLoginVerificationMode.PasswordAndTwoFactor)
            {
                if (string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "请输入密码";
                    return;
                }
            }

            if (mode == AdminLoginVerificationMode.TwoFactorOnly)
            {
                if (string.IsNullOrWhiteSpace(TwoFactorCode))
                {
                    ErrorMessage = "请输入双重验证码";
                    return;
                }
            }
            else if (mode == AdminLoginVerificationMode.PasswordOrTwoFactor)
            {
                if (string.IsNullOrWhiteSpace(Password) && string.IsNullOrWhiteSpace(TwoFactorCode))
                {
                    ErrorMessage = "请输入密码或双重验证码";
                    return;
                }
            }

            if (mode == AdminLoginVerificationMode.PasswordAndTwoFactor && SecurityService.Instance.Settings.IsTwoFactorEnabled && !IsTwoFactorRequired)
            {
                var passwordOnly = await SecurityService.Instance.VerifyPasswordOnlyAsync(Username, Password);
                if (passwordOnly.Status == PasswordVerificationStatus.Success)
                {
                    IsTwoFactorRequired = true;
                    ErrorMessage = "请输入双重验证码";
                    return;
                }

                ErrorMessage = passwordOnly.Message;
                return;
            }

            securityResult = await SecurityService.Instance.VerifyPasswordAsync(Username, Password, TwoFactorCode);
            if (securityResult.Status != PasswordVerificationStatus.Success)
            {
                ErrorMessage = securityResult.Message;
                return;
            }

            var accountLoaded = AccountService.Instance.LoginFromSecuritySession(Username);
            if (!accountLoaded)
            {
                ErrorMessage = "验证成功，但无法加载账户权限";
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "请输入密码";
                return;
            }

            var (success, message) = await AccountService.Instance.LoginAsync(Username, Password);
            if (!success)
            {
                ErrorMessage = message;
                return;
            }
        }
        
        // 检查权限
        var currentAccount = AccountService.Instance.CurrentAccount;
        if (currentAccount == null || currentAccount.AccountType > settings.EarlyUnlockMinAccountType)
        {
            ErrorMessage = "该账户权限不足，无法提前解锁";
            // 登出，防止影响主程序的登录状态
            AccountService.Instance.Logout();
            return;
        }
        LockScreenService.Instance.ManualDeactivateLock();
        NotificationService.Instance.ShowInfo($"已由管理员 {currentAccount.Username} 提前解锁屏幕");
        
        IsLoginVisible = false;
        IsTwoFactorRequired = false;
        ErrorMessage = string.Empty;
    }

    partial void OnErrorMessageChanged(string value)
    {
        HasError = !string.IsNullOrWhiteSpace(value);
    }

    public void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }
}
