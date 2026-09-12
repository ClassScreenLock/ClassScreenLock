using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace ClassScreenLock.ViewModels;

public partial class LockSettingsViewModel : ViewModelBase
{
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

    partial void OnShowFloatingLockWidgetChanged(bool value)
    {
        if (!value)
        {
            FloatingWidgetService.Instance.HideWidget();
        }
        else
        {
            LockScreenService.Instance.RefreshBreakWidgetVisibility();
        }
    }

    [ObservableProperty]
    private int _earlyUnlockMinAccountTypeIndex;

    [ObservableProperty]
    private double _lockBackgroundOpacity;

    [ObservableProperty]
    private double _lockTextShadowOpacity;

    [ObservableProperty]
    private double _lockTextShadowBlurRadius;

    [ObservableProperty]
    private bool _enableLockStateFileCheck;

    [ObservableProperty]
    private int _lockStateFileCheckIntervalSeconds;

    [ObservableProperty]
    private double _topmostRefreshInterval;

    public ObservableCollection<double> TopmostRefreshIntervalOptions { get; } = new()
    {
        0.01, 0.05, 0.1, 0.5, 1, 5, 10, 25, 50, 100, 200, 500
    };

    public LockSettingsViewModel()
    {
        LoadSettings();
    }

    private void LoadSettings()
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

        EnableLockStateFileCheck = settings.EnableLockStateFileCheck;
        LockStateFileCheckIntervalSeconds = settings.LockStateFileCheckIntervalSeconds;

        TopmostRefreshInterval = settings.TopmostRefreshInterval;

        CanEditBreakTimeLock = settings.BreakTimeLockSettingsMinAccountType == null
                                || SecurityService.Instance.IsAuthenticated
                                || AccountService.Instance.HasPermission(settings.BreakTimeLockSettingsMinAccountType.Value);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        SaveSettingsInternal(true);
    }

    private void SaveSettingsInternal(bool showNotification)
    {
        SettingsService.UpdateLock(settings =>
        {
            settings.EnableBreakTimeLock = EnableBreakTimeLock;
            settings.BreakTimeLockMode = BreakTimeLockMode;
            settings.AutoUnlockBeforeClassMinutes = (int)AutoUnlockBeforeClassMinutes;
            settings.LockTimeout = LockTimeout;
            settings.ShowFloatingLockWidget = ShowFloatingLockWidget;
            settings.EarlyUnlockMinAccountType = (AccountType)EarlyUnlockMinAccountTypeIndex;

            settings.LockBackgroundOpacity = LockBackgroundOpacity;
            settings.LockTextShadowOpacity = LockTextShadowOpacity;
            settings.LockTextShadowBlurRadius = LockTextShadowBlurRadius;

            settings.EnableLockStateFileCheck = EnableLockStateFileCheck;
            settings.LockStateFileCheckIntervalSeconds = LockStateFileCheckIntervalSeconds;
            settings.TopmostRefreshInterval = TopmostRefreshInterval;
        });
        
        LockScreenService.Instance.StopLockStateFileCheck();
        LockScreenService.Instance.StartLockStateFileCheck();
        
        if (showNotification)
        {
            NotificationService.Instance.ShowSuccess(LocalizationService.Instance.GetString("Notify_SettingsSaved") ?? "设置已保存");
        }
    }

    partial void OnEnableBreakTimeLockChanged(bool value) { }
    partial void OnBreakTimeLockModeChanged(LockMode value) { }
    partial void OnAutoUnlockBeforeClassMinutesChanged(decimal value) { }
}
