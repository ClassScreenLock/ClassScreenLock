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
    private string _newAllowedApp = string.Empty;

    [ObservableProperty]
    private string _newForcedApp = string.Empty;

    [ObservableProperty]
    private bool _enableLockStateFileCheck;

    [ObservableProperty]
    private int _lockStateFileCheckIntervalSeconds;

    public ObservableCollection<string> AllowedTopmostApps { get; } = new();
    public ObservableCollection<string> ForcedTopmostApps { get; } = new();

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
            settings.AllowedTopmostApps = AllowedTopmostApps.ToList();
            settings.ForcedTopmostApps = ForcedTopmostApps.ToList();

            settings.LockBackgroundOpacity = LockBackgroundOpacity;
            settings.LockTextShadowOpacity = LockTextShadowOpacity;
            settings.LockTextShadowBlurRadius = LockTextShadowBlurRadius;

            settings.EnableLockStateFileCheck = EnableLockStateFileCheck;
            settings.LockStateFileCheckIntervalSeconds = LockStateFileCheckIntervalSeconds;
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
}
