using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;
using ClassScreenLock.Models;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using System.IO;
using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using System.Windows.Input;
using Avalonia.Styling;
using Avalonia.Controls;
using ClassScreenLock.Helpers;
using System.Diagnostics;

namespace ClassScreenLock.ViewModels;

public partial class InitializationViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainWindowViewModel;

    [ObservableProperty]
    private int _stepIndex;

    [ObservableProperty]
    private int _stepDisplay;

    [ObservableProperty]
    private int _stepTotal;

    [ObservableProperty]
    private int _stepProgressValue;

    [ObservableProperty]
    private int _stepProgressMax;

    [ObservableProperty]
    private bool _canSkip;

    [ObservableProperty]
    private bool _isAgreementAccepted;

    [ObservableProperty]
    private string _agreementContent = string.Empty;

    [ObservableProperty]
    private string _agreementConnectionStatus = "未连接";

    [ObservableProperty]
    private bool _isAgreementConnectionSuccessful;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private int _passwordStrength;

    [ObservableProperty]
    private bool _darkMode;

    [ObservableProperty]
    private string _accentColor = "#0078D4";

    [ObservableProperty]
    private string _language = "zh-CN";

    [ObservableProperty]
    private bool _useSystemAccentColor;

    [ObservableProperty]
    private double _fontSize;

    [ObservableProperty]
    private string _fontFamily = "Microsoft YaHei UI";

    [ObservableProperty]
    private bool _showNotifications;

    [ObservableProperty]
    private int _loginVerificationModeIndex;

    [ObservableProperty]
    private bool _appBlockingEnabled;

    [ObservableProperty]
    private bool _basicProtectionEnabled;

    [ObservableProperty]
    private string _blockedRulesText = string.Empty;

    [ObservableProperty]
    private bool _isAdminStepCompleted;

    [ObservableProperty]
    private bool _hasSuperAdminAccount;

    [ObservableProperty]
    private string _newAdminPassword = string.Empty;

    [ObservableProperty]
    private string _confirmAdminPassword = string.Empty;

    [ObservableProperty]
    private bool _isOnboardingIncomplete;

    [ObservableProperty]
    private bool _canResetAdminPassword;

    [ObservableProperty]
    private bool _networkLockEnabled;

    [ObservableProperty]
    private string _networkDomainsText = string.Empty;

    [ObservableProperty]
    private bool _enableClassScreenshot;
    [ObservableProperty]
    private int _classScreenshotInterval;
    [ObservableProperty]
    private bool _enableBreakScreenshot;
    [ObservableProperty]
    private int _breakScreenshotInterval;
    [ObservableProperty]
    private bool _enableClassWebcam;
    [ObservableProperty]
    private int _classWebcamInterval;
    [ObservableProperty]
    private bool _enableBreakWebcam;
    [ObservableProperty]
    private int _breakWebcamInterval;

    [ObservableProperty]
    private List<CameraItem> _cameraOptions = new();

    [ObservableProperty]
    private string? _selectedCamera;

    public record CameraItem(string Name, string Moniker)
    {
        public override string ToString() => Name;
    }

    public List<int> ScreenshotIntervalOptions { get; } = new() { 1, 2, 5, 10, 15, 30, 60 };

    public List<string> LanguageOptions { get; } = new() { "zh-CN", "en-US" };
    public List<string> AccentColorOptions { get; } = new()
    {
        "#0078D4", "#2B88D8", "#005A9E", "#D83B01", "#E81123",
        "#107C10", "#00B7C3", "#5C2D91", "#A4262C", "#FFB900"
    };

        [ObservableProperty]
        private string _repositoryUrl = "https://github.com/jiugulixiaoniu/ClassScreenLock";

        [ObservableProperty]
        private string _userAgreementUrl = "https://jiugulixiaoniu.github.io/ClassScreenLock-Offical/UserAgreement.html";

        [ObservableProperty]
        private string _privacyPolicyUrl = "https://jiugulixiaoniu.github.io/ClassScreenLock-Offical/PrivacyPolicy.html";

    [ObservableProperty]
    private bool _isWelcomeVisible = true;

    [ObservableProperty]
    private double _welcomeOpacity = 1.0;

    public InitializationViewModel(MainWindowViewModel mainWindowViewModel)
    {
        _mainWindowViewModel = mainWindowViewModel;
        LoadInitialState();
        _ = ShowWelcomeAnimationAsync();
    }

    private async Task ShowWelcomeAnimationAsync()
    {
        // 欢迎动画展示时间
        await Task.Delay(2500);
        WelcomeOpacity = 0;
        await Task.Delay(500); // 等待淡出动画完成
        IsWelcomeVisible = false;
    }

    partial void OnStepIndexChanged(int value)
    {
        RefreshStepUi(value);
        IsAdminStepCompleted = InitializationService.Instance.IsStepCompleted(InitStep.AdminAccount);
        HasSuperAdminAccount = AccountService.Instance.IsInitialized;
        IsOnboardingIncomplete = InitializationService.Instance.RequiresInitialization;
        CanResetAdminPassword = HasSuperAdminAccount && IsOnboardingIncomplete;
        if ((InitStep)value == InitStep.TwoFactorBinding && ShouldIncludeTwoFactorBinding())
        {
            if (!SecurityService.Instance.Settings.IsTwoFactorEnabled && QrCodeBitmap == null)
            {
                PrepareTwoFactorSetup();
            }
        }
    }

    private void LoadInitialState()
    {
        UserAgreementUrl = "https://classscreenlock.us.ci/eula";
        AgreementConnectionStatus = $"协议地址：{UserAgreementUrl}";
        IsAgreementConnectionSuccessful = true;

        var general = SettingsService.General;
        DarkMode = general.DarkMode;
        AccentColor = string.IsNullOrWhiteSpace(general.AccentColor) ? "#0078D4" : general.AccentColor;
        Language = string.IsNullOrWhiteSpace(general.Language) ? "zh-CN" : general.Language;
        UseSystemAccentColor = general.UseSystemAccentColor;

        FontSize = general.FontSize;
        FontFamily = general.FontFamily;
        ShowNotifications = general.ShowNotifications;

        var screenshot = SettingsService.Screenshot;
        EnableClassScreenshot = screenshot.EnableClassScreenshot;
        ClassScreenshotInterval = screenshot.ClassScreenshotInterval;
        EnableBreakScreenshot = screenshot.EnableBreakScreenshot;
        BreakScreenshotInterval = screenshot.BreakScreenshotInterval;
        EnableClassWebcam = screenshot.EnableClassWebcam;
        ClassWebcamInterval = screenshot.ClassWebcamInterval;
        EnableBreakWebcam = screenshot.EnableBreakWebcam;
        BreakWebcamInterval = screenshot.BreakWebcamInterval;
        SelectedCamera = screenshot.SelectedCameraMoniker;

        // Load cameras
        var cameras = WebcamService.Instance.GetAvailableCamerasWithNames();
        CameraOptions = cameras.Select(kvp => new CameraItem(kvp.Value, kvp.Key)).ToList();
        
        if (string.IsNullOrEmpty(SelectedCamera) && CameraOptions.Any())
        {
            SelectedCamera = CameraOptions.First().Moniker;
        }

        LoginVerificationModeIndex = (int)SecurityService.Instance.Settings.LoginVerificationMode;

        StepIndex = InitializationService.Instance.CurrentStepIndex;
        RefreshStepUi(StepIndex);
        IsAdminStepCompleted = InitializationService.Instance.IsStepCompleted(InitStep.AdminAccount);
        HasSuperAdminAccount = AccountService.Instance.IsInitialized;
        IsOnboardingIncomplete = InitializationService.Instance.RequiresInitialization;
        CanResetAdminPassword = HasSuperAdminAccount && IsOnboardingIncomplete;
        LogService.Instance.Log("Init", "Resume", StepIndex.ToString());

        var blockage = SettingsService.Blockage;
        AppBlockingEnabled = blockage.IsAppBlockingEnabled;
        BasicProtectionEnabled = blockage.IsBasicProtectionEnabled;
        NetworkLockEnabled = blockage.IsNetworkLockEnabled;
        BlockedRulesText = string.Join(",", blockage.BlockedRules ?? new System.Collections.Generic.List<string>());
        var rules = NetworkRuleService.LoadRules();
        NetworkDomainsText = string.Join(",", rules?.Where(r => r.IsEnabled && r.Type == "Domain").Select(r => r.Domain) ?? Enumerable.Empty<string>());

        if ((InitStep)StepIndex == InitStep.TwoFactorBinding && ShouldIncludeTwoFactorBinding())
        {
            PrepareTwoFactorSetup();
        }
    }


    [RelayCommand]
    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenUserAgreement()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UserAgreementUrl) { UseShellExecute = true });
        }
        catch { }
    }


    partial void OnPasswordChanged(string value)
    {
        var result = SecurityService.Instance.ValidatePolicy(value);
        PasswordStrength = result.Score;
        ValidationMessage = string.Join("\n", result.Errors);
    }

    partial void OnDarkModeChanged(bool value)
    {
        SettingsService.UpdateGeneral(s => s.DarkMode = value);
        _ = ThemeHelper.ApplyThemeCircularReveal(value);
    }

    partial void OnAccentColorChanged(string value)
    {
        SettingsService.UpdateGeneral(s => s.AccentColor = value);
        ThemeHelper.ApplyAccentColor(value);
    }

    partial void OnUseSystemAccentColorChanged(bool value)
    {
        SettingsService.UpdateGeneral(s => s.UseSystemAccentColor = value);
        var color = value ? GetSystemAccentColor() : AccentColor;
        ThemeHelper.ApplyAccentColor(color);
        if (value)
        {
            AccentColor = color;
        }
    }

    partial void OnFontSizeChanged(double value)
    {
        SettingsService.UpdateGeneral(s => s.FontSize = value);
        ApplyFontSizeChange(value);
    }

    partial void OnFontFamilyChanged(string value)
    {
        SettingsService.UpdateGeneral(s => s.FontFamily = value);
    }

    partial void OnShowNotificationsChanged(bool value)
    {
        SettingsService.UpdateGeneral(s => s.ShowNotifications = value);
        NotificationService.Instance.UpdateNotificationSettings(value);
    }

    partial void OnLanguageChanged(string value)
    {
        SettingsService.UpdateGeneral(s => s.Language = value);
        ApplyLanguageChange(value);
    }

    [RelayCommand]
    private void Back()
    {
        if (StepIndex > (int)InitStep.UserAgreement)
        {
            var nextIndex = StepIndex - 1;
            // 如果是 2FA 步骤且当前设置不需要 2FA，则再往前跳一步
            if (nextIndex == (int)InitStep.TwoFactorBinding && !ShouldIncludeTwoFactorBinding())
            {
                nextIndex--;
            }

            if (nextIndex >= (int)InitStep.UserAgreement)
            {
                StepIndex = nextIndex;
                RefreshStepUi(StepIndex);
                LogService.Instance.Log("Init", "Back", StepIndex.ToString());
                InitializationService.Instance.SaveState();
            }
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        switch ((InitStep)StepIndex)
        {
            case InitStep.UserAgreement:
                if (!IsAgreementAccepted)
                {
                    NotificationService.Instance.ShowWarning("请先阅读并同意用户协议");
                    return;
                }
                InitializationService.Instance.MarkStepComplete(InitStep.UserAgreement);
                StepIndex++;
                RefreshStepUi(StepIndex);
                break;
            case InitStep.SystemConfig:
                if (!ValidateSystemConfig())
                {
                    return;
                }
                SettingsService.UpdateGeneral(s =>
                {
                    s.DarkMode = DarkMode;
                    s.AccentColor = AccentColor;
                    s.Language = Language;
                    s.UseSystemAccentColor = UseSystemAccentColor;
                });
                InitializationService.Instance.MarkStepComplete(InitStep.SystemConfig);
                StepIndex++;
                if ((InitStep)StepIndex == InitStep.TwoFactorBinding && !ShouldIncludeTwoFactorBinding())
                {
                    StepIndex++;
                }
                RefreshStepUi(StepIndex);
                break;
            case InitStep.UserPreferences:
                if (!ValidateUserPreferences())
                {
                    return;
                }
                SettingsService.UpdateGeneral(s =>
                {
                    s.FontSize = FontSize;
                    s.FontFamily = FontFamily;
                    s.ShowNotifications = ShowNotifications;
                });
                InitializationService.Instance.MarkStepComplete(InitStep.UserPreferences);
                StepIndex++;
                RefreshStepUi(StepIndex);
                break;
            case InitStep.MonitoringConfig:
                SettingsService.UpdateScreenshot(s =>
                {
                    s.EnableClassScreenshot = EnableClassScreenshot;
                    s.ClassScreenshotInterval = ClassScreenshotInterval;
                    s.EnableBreakScreenshot = EnableBreakScreenshot;
                    s.BreakScreenshotInterval = BreakScreenshotInterval;
                    s.EnableClassWebcam = EnableClassWebcam;
                    s.ClassWebcamInterval = ClassWebcamInterval;
                    s.EnableBreakWebcam = EnableBreakWebcam;
                    s.BreakWebcamInterval = BreakWebcamInterval;
                    s.SelectedCameraMoniker = SelectedCamera ?? string.Empty;
                });
                InitializationService.Instance.MarkStepComplete(InitStep.MonitoringConfig);
                StepIndex++;
                RefreshStepUi(StepIndex);
                break;
            case InitStep.PermissionSetup:
                SecurityService.Instance.SetLoginVerificationMode((AdminLoginVerificationMode)LoginVerificationModeIndex);
                StepTotal = ShouldIncludeTwoFactorBinding() ? 9 : 8;
                InitializationService.Instance.MarkStepComplete(InitStep.PermissionSetup);
                StepIndex++;
                if ((InitStep)StepIndex == InitStep.TwoFactorBinding && !ShouldIncludeTwoFactorBinding())
                {
                    StepIndex++;
                }
                RefreshStepUi(StepIndex);
                break;
            case InitStep.AdminAccount:
                if (InitializationService.Instance.IsStepCompleted(InitStep.AdminAccount))
                {
                    StepIndex++;
                    if ((InitStep)StepIndex == InitStep.TwoFactorBinding && !ShouldIncludeTwoFactorBinding())
                    {
                        StepIndex++;
                    }
                    RefreshStepUi(StepIndex);
                }
                else
                {
                    await InitializeAdminAsync();
                }
                break;
            case InitStep.TwoFactorBinding:
                if (!ShouldIncludeTwoFactorBinding())
                {
                    InitializationService.Instance.MarkStepComplete(InitStep.TwoFactorBinding);
                    StepIndex++;
                    RefreshStepUi(StepIndex);
                    break;
                }
                if (SecurityService.Instance.Settings.IsTwoFactorEnabled)
                {
                    InitializationService.Instance.MarkStepComplete(InitStep.TwoFactorBinding);
                    StepIndex++;
                    RefreshStepUi(StepIndex);
                    break;
                }
                if (string.IsNullOrWhiteSpace(TwoFactorInputCode))
                {
                    NotificationService.Instance.ShowWarning("请输入双重验证码");
                    return;
                }
                var secretToUse = UseManualSecret && !string.IsNullOrWhiteSpace(ManualSecretInput) ? ManualSecretInput : TwoFactorSecret;
                var result2fa = await SecurityService.Instance.EnableTwoFactorAsync(secretToUse, TwoFactorInputCode);
                if (result2fa.Success)
                {
                    InitializationService.Instance.MarkStepComplete(InitStep.TwoFactorBinding);
                    NotificationService.Instance.ShowSuccess("双重验证已启用");
                    StepIndex++;
                    StepTotal = ShouldIncludeTwoFactorBinding() ? 9 : 8;
                    RefreshStepUi(StepIndex);
                }
                else
                {
                    NotificationService.Instance.ShowError(result2fa.Message);
                }
                break;
            case InitStep.AppBlocking:
                ApplyAppBlocking();
                InitializationService.Instance.MarkStepComplete(InitStep.AppBlocking);
                StepIndex++;
                RefreshStepUi(StepIndex);
                break;
            case InitStep.NetworkBlocking:
                await ApplyNetworkBlockingAsync();
                InitializationService.Instance.MarkStepComplete(InitStep.NetworkBlocking);
                NotificationService.Instance.ShowSuccess("初始化完成");
                _mainWindowViewModel.IsInitialized = true;
                // 初始化完成后再启动应用拦截服务，以免在引导阶段误拦截
                AppBlockingService.Instance.Start();
                ScreenshotService.Instance.Start();
                WebcamService.Instance.Start();
                _mainWindowViewModel.NavigateToHome();
                break;
        }
        InitializationService.Instance.SaveState();
    }

    [RelayCommand]
    private void Skip()
    {
        if ((InitStep)StepIndex == InitStep.AdminAccount)
        {
            NotificationService.Instance.ShowWarning("无法跳过管理员账户设置");
            return;
        }
        if (!CanSkip)
        {
            NotificationService.Instance.ShowWarning("已是最后一步");
            return;
        }
        InitializationService.Instance.MarkStepComplete((InitStep)StepIndex);
        StepIndex++;
        if ((InitStep)StepIndex == InitStep.TwoFactorBinding && !ShouldIncludeTwoFactorBinding())
        {
            StepIndex++;
        }
        RefreshStepUi(StepIndex);
        InitializationService.Instance.SaveState();
        LogService.Instance.Log("Init", "Skip", ((InitStep)StepIndex).ToString());
    }

    private bool ValidateSystemConfig()
    {
        if (string.IsNullOrWhiteSpace(Language))
        {
            NotificationService.Instance.ShowWarning("请选择语言");
            return false;
        }
        if (string.IsNullOrWhiteSpace(AccentColor) || !AccentColor.StartsWith("#"))
        {
            NotificationService.Instance.ShowWarning("请选择有效的强调色");
            return false;
        }
        return true;
    }

    private bool ValidateUserPreferences()
    {
        if (FontSize < 10 || FontSize > 32)
        {
            NotificationService.Instance.ShowWarning("字体大小需在 10-32 之间");
            return false;
        }
        if (string.IsNullOrWhiteSpace(FontFamily))
        {
            NotificationService.Instance.ShowWarning("请选择字体");
            return false;
        }
        return true;
    }

    private async Task InitializeAdminAsync()
    {
        if (AccountService.Instance.IsInitialized)
        {
            if (string.IsNullOrWhiteSpace(SecurityService.Instance.Settings.PasswordHash))
            {
                if (string.IsNullOrWhiteSpace(NewAdminPassword) || string.IsNullOrWhiteSpace(ConfirmAdminPassword))
                {
                    NotificationService.Instance.ShowWarning("请先设置新的管理员密码");
                    return;
                }
                if (NewAdminPassword != ConfirmAdminPassword)
                {
                    NotificationService.Instance.ShowWarning(LocalizationService.Instance.GetString("Account_ConfirmPassword_NotMatch") ?? "两次输入的密码不一致");
                    return;
                }
                var policy2 = SecurityService.Instance.ValidatePolicy(NewAdminPassword);
                if (!policy2.IsValid)
                {
                    NotificationService.Instance.ShowWarning("密码不符合安全策略要求");
                    return;
                }
                var name2 = GetExistingAdminUsername();
                var resetResult = await SecurityService.Instance.ChangePasswordAsync(name2, string.Empty, NewAdminPassword, ConfirmAdminPassword);
                if (!resetResult.Success)
                {
                    NotificationService.Instance.ShowWarning(string.IsNullOrEmpty(resetResult.Message) ? "重置密码失败" : resetResult.Message);
                    return;
                }
            }

            InitializationService.Instance.MarkStepComplete(InitStep.AdminAccount);
            IsAdminStepCompleted = true;
            HasSuperAdminAccount = true;
            NotificationService.Instance.ShowSuccess("管理员账户已存在");
            _mainWindowViewModel.SidebarViewModel.RefreshAccountInfo();
            StepIndex++;
            if ((InitStep)StepIndex == InitStep.TwoFactorBinding && !ShouldIncludeTwoFactorBinding())
            {
                StepIndex++;
            }
            RefreshStepUi(StepIndex);
            if ((InitStep)StepIndex == InitStep.TwoFactorBinding && ShouldIncludeTwoFactorBinding())
            {
                PrepareTwoFactorSetup();
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            NotificationService.Instance.ShowWarning("请输入超级管理员用户名");
            return;
        }

        if (Password != ConfirmPassword)
        {
            NotificationService.Instance.ShowWarning(LocalizationService.Instance.GetString("Account_ConfirmPassword_NotMatch") ?? "两次输入的密码不一致");
            return;
        }

        var policy = SecurityService.Instance.ValidatePolicy(Password);
        if (!policy.IsValid)
        {
            NotificationService.Instance.ShowWarning("密码不符合安全策略要求");
            return;
        }

        var result = await AccountService.Instance.EnsureSuperAdminExistsAsync(Username, Password);
        if (result)
        {
            InitializationService.Instance.MarkStepComplete(InitStep.AdminAccount);
            IsAdminStepCompleted = true;
            HasSuperAdminAccount = true;
            NotificationService.Instance.ShowSuccess(LocalizationService.Instance.GetString("Account_Init_Success") ?? "初始化成功");
            _mainWindowViewModel.SidebarViewModel.RefreshAccountInfo();
            StepIndex++;
            if ((InitStep)StepIndex == InitStep.TwoFactorBinding && !ShouldIncludeTwoFactorBinding())
            {
                StepIndex++;
            }
            RefreshStepUi(StepIndex);
            if ((InitStep)StepIndex == InitStep.TwoFactorBinding && ShouldIncludeTwoFactorBinding())
            {
                PrepareTwoFactorSetup();
            }
        }
        else
        {
            NotificationService.Instance.ShowWarning("初始化失败，请确保密码符合策略要求");
        }
    }

    [RelayCommand]
    private async Task ResetAdminPasswordAsync()
    {
        if (!InitializationService.Instance.RequiresInitialization)
        {
            NotificationService.Instance.ShowWarning("引导已完成，无法在此重置管理员密码");
            return;
        }
        var name = GetExistingAdminUsername();
        var result = await SecurityService.Instance.ChangePasswordAsync(name, string.Empty, NewAdminPassword, ConfirmAdminPassword);
        if (result.Success)
        {
            NewAdminPassword = string.Empty;
            ConfirmAdminPassword = string.Empty;
            NotificationService.Instance.ShowSuccess(result.Message);
            _mainWindowViewModel.SidebarViewModel.RefreshAccountInfo();
        }
        else
        {
            NotificationService.Instance.ShowWarning(string.IsNullOrEmpty(result.Message) ? "重置密码失败" : result.Message);
        }
    }

    private static string GetExistingAdminUsername()
    {
        var superAdmin = AccountService.Instance.Accounts.FirstOrDefault(a => a.AccountType == AccountType.SuperAdmin);
        if (superAdmin != null && !string.IsNullOrWhiteSpace(superAdmin.Username))
        {
            return superAdmin.Username;
        }
        var settingsName = SecurityService.Instance.Settings.AdminUsername;
        if (!string.IsNullOrWhiteSpace(settingsName))
        {
            return settingsName;
        }
        return "admin";
    }

    private void ApplyAppBlocking()
    {
        var blocked = (BlockedRulesText ?? string.Empty)
            .Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        SettingsService.UpdateBlockage(b =>
        {
            b.IsAppBlockingEnabled = AppBlockingEnabled;
            b.IsBasicProtectionEnabled = BasicProtectionEnabled;
            b.BlockedRules = blocked;
        });
    }

    private async Task ApplyNetworkBlockingAsync()
    {
        SettingsService.UpdateBlockage(b =>
        {
            b.IsNetworkLockEnabled = NetworkLockEnabled;
        });
        var domains = (NetworkDomainsText ?? string.Empty)
            .Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToLower())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
        var rules = NetworkRuleService.LoadRules();
        var newRules = domains.Select(d => new NetworkRule
        {
            Domain = d,
            Description = d,
            IsEnabled = true,
            Type = "Domain"
        }).ToList();
        NetworkRuleService.SaveRules(newRules);
        await NetworkBlockingService.Instance.ApplyRulesAsync("InitializationComplete");
    }

    private void ApplyFontSizeChange(double fontSize)
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = desktop.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.FontSize = fontSize;
            }
        }
    }

    private string GetSystemAccentColor()
    {
        try
        {
            if (Application.Current?.PlatformSettings != null)
            {
                var colorValues = Application.Current.PlatformSettings.GetColorValues();
                var accentColor = colorValues.AccentColor1;
                return $"#{accentColor.R:X2}{accentColor.G:X2}{accentColor.B:X2}";
            }
        }
        catch
        {
        }
        return "#0078D4";
    }

    private void ApplyLanguageChange(string language)
    {
        try
        {
            var cultureInfo = new System.Globalization.CultureInfo(language);
            System.Globalization.CultureInfo.CurrentCulture = cultureInfo;
            System.Globalization.CultureInfo.CurrentUICulture = cultureInfo;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                LocalizationService.Instance.CurrentLanguage = language;
            });
        }
        catch
        {
        }
    }

    [ObservableProperty]
    private string _twoFactorSecret = string.Empty;

    [ObservableProperty]
    private Bitmap? _qrCodeBitmap;

    [ObservableProperty]
    private string _twoFactorInputCode = string.Empty;

    [ObservableProperty]
    private bool _useManualSecret;

    [ObservableProperty]
    private string _manualSecretInput = string.Empty;

    private bool ShouldIncludeTwoFactorBinding()
    {
        var mode = (AdminLoginVerificationMode)LoginVerificationModeIndex;
        return mode != AdminLoginVerificationMode.PasswordOnly;
    }

    private void RefreshStepUi(int stepIndex)
    {
        var include2fa = ShouldIncludeTwoFactorBinding();
        StepTotal = include2fa ? 9 : 8;
        StepProgressMax = Math.Max(0, StepTotal - 1);

        var display = stepIndex + 1;
        if (!include2fa && stepIndex > (int)InitStep.TwoFactorBinding)
        {
            display--;
        }
        StepDisplay = Math.Clamp(display, 1, StepTotal);
        StepProgressValue = Math.Clamp(StepDisplay - 1, 0, StepProgressMax);
        CanSkip = StepDisplay < StepTotal && (InitStep)stepIndex != InitStep.UserAgreement && (InitStep)stepIndex != InitStep.AdminAccount;
    }

    private void PrepareTwoFactorSetup()
    {
        if (UseManualSecret)
        {
            QrCodeBitmap = null;
            TwoFactorSecret = string.Empty;
            return;
        }
        var name = string.IsNullOrWhiteSpace(Username) ? SecurityService.Instance.Settings.AdminUsername : Username;
        var setup = SecurityService.Instance.GenerateTwoFactorSetup(name);
        TwoFactorSecret = setup.Secret;
        try
        {
            var qrBytes = SecurityService.Instance.GenerateQrCode(setup.QrCodeUri);
            using var ms = new MemoryStream(qrBytes);
            QrCodeBitmap = new Bitmap(ms);
        }
        catch
        {
            QrCodeBitmap = null;
        }
    }

    partial void OnUseManualSecretChanged(bool value)
    {
        if ((InitStep)StepIndex == InitStep.TwoFactorBinding && ShouldIncludeTwoFactorBinding())
        {
            PrepareTwoFactorSetup();
        }
    }

    partial void OnLoginVerificationModeIndexChanged(int value)
    {
        if (!ShouldIncludeTwoFactorBinding() && (InitStep)StepIndex == InitStep.TwoFactorBinding)
        {
            StepIndex = (int)InitStep.AppBlocking;
            return;
        }
        RefreshStepUi(StepIndex);
    }
}
