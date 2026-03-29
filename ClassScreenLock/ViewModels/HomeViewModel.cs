using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;
using ClassScreenLock.Models;

namespace ClassScreenLock.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainWindowViewModel;

    [ObservableProperty]
    private string _lockStatusText = string.Empty;

    [ObservableProperty]
    private string _lockStatusIcon = string.Empty;

    [ObservableProperty]
    private string _nextScheduleText = "无";

    [ObservableProperty]
    private int _blockedAppsCount;

    [ObservableProperty]
    private int _blockedNetworkCount;

    [ObservableProperty]
    private string _systemStatusText = "系统正常运行中";

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private bool _hasUiAccess;

    [ObservableProperty]
    private string _uiAccessStatusText = string.Empty;

    public HomeViewModel(MainWindowViewModel mainWindowViewModel)
    {
        _mainWindowViewModel = mainWindowViewModel;
        UpdateStatus();
        
        // 订阅服务事件以更新 UI
        LockScreenService.Instance.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(LockScreenService.IsLocked))
            {
                UpdateStatus();
            }
        };
    }

    public void UpdateStatus()
    {
        IsLocked = LockScreenService.Instance.IsLocked;
        LockStatusText = IsLocked ? "屏幕已锁定" : "屏幕未锁定";
        LockStatusIcon = IsLocked ? "fas fa-lock" : "fas fa-lock-open";
        
        // 更新UIAccess状态
        HasUiAccess = UiAccessService.Instance.HasUiAccess;
        UiAccessStatusText = UiAccessService.Instance.StatusMessage;
        
        // 更新其他状态信息
        RefreshStats();
    }

    [RelayCommand]
    private void RefreshStats()
    {
        // 更新被拦截应用数量
        BlockedAppsCount = (SettingsService.Blockage.BlockedRules?.Count ?? 0) + 
                           (SettingsService.Blockage.ProtectionRules?.Count(r => r.IsEnabled) ?? 0);
        
        // 更新网络拦截数量（如果有的话）
        // BlockedNetworkCount = ...
        
        // 更新下一个计划任务
        var (_, nextPoint) = ScheduleService.Instance.GetCurrentAndNextTimePoint(DateTime.Now.TimeOfDay);
        if (nextPoint != null)
        {
            NextScheduleText = $"{nextPoint.StartTime:hh\\:mm} - {nextPoint.Label}";
        }
        else
        {
            NextScheduleText = "今日无后续计划";
        }
    }

    [RelayCommand]
    private void StartLock()
    {
        _mainWindowViewModel.StartLockCommand.Execute(null);
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _mainWindowViewModel.NavigateToSettings();
    }
}
