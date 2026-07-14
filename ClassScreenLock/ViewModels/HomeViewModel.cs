using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassScreenLock.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _mainVM;

    /// <summary>
    /// 用户可编辑的快速操作列表
    /// </summary>
    public ObservableCollection<QuickActionItemViewModel> QuickActions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockStatusText))]
    [NotifyPropertyChangedFor(nameof(LockStatusIcon))]
    [NotifyPropertyChangedFor(nameof(CanLock))]
    [NotifyPropertyChangedFor(nameof(IsProtectionActive))]
    private bool _isLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockStatusText))]
    [NotifyPropertyChangedFor(nameof(LockStatusIcon))]
    [NotifyPropertyChangedFor(nameof(CanLock))]
    private bool _isProtectionOnlyActive;

    [ObservableProperty]
    private string _systemStatusText = "系统正常运行";

    [ObservableProperty]
    private string _nextScheduleText = "暂无安排";

    [ObservableProperty]
    private int _blockedAppsCount;

    [ObservableProperty]
    private int _blockedNetworkCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiAccessStatusText))]
    private bool _hasUiAccess;

    public string LockStatusText => IsLocked ? "屏幕已锁定" : (IsProtectionOnlyActive ? "防护模式" : "屏幕未锁定");

    /// <summary>
    /// 状态图标 (FontAwesome 字符串) - 通过转换器转为 Fluent 图标
    /// </summary>
    public string LockStatusIcon => IsLocked ? "lock-closed" : (IsProtectionOnlyActive ? "shield" : "lock-open");

    public bool CanLock => !IsLocked && !IsProtectionOnlyActive;

    public bool IsProtectionActive => IsProtectionOnlyActive;

    public string UiAccessStatusText => HasUiAccess ? "已启用" : "未启用";

    public HomeViewModel(MainWindowViewModel mainVM)
    {
        _mainVM = mainVM;
        LoadQuickActions();

        // 监听设置变化，刷新快速操作
        SettingsService.GeneralChanged += OnGeneralChanged;

        // 监听锁定状态
        IsLocked = LockScreenService.Instance.IsLocked;
        IsProtectionOnlyActive = LockScreenService.Instance.IsProtectionOnlyActive;
        LockScreenService.Instance.PropertyChanged += OnLockScreenPropertyChanged;

        // 异步加载统计数据，避免阻塞UI线程
        _ = Task.Run(() => RefreshStats());
    }

    private void OnLockScreenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LockScreenService.IsLocked))
        {
            IsLocked = LockScreenService.Instance.IsLocked;
        }
        else if (e.PropertyName == nameof(LockScreenService.IsProtectionOnlyActive))
        {
            IsProtectionOnlyActive = LockScreenService.Instance.IsProtectionOnlyActive;
        }
    }

    private void OnGeneralChanged()
    {
        Dispatcher.UIThread.Post(LoadQuickActions);
    }

    private void LoadQuickActions()
    {
        QuickActions.Clear();
        var configured = SettingsService.General.QuickActions;

        // 首次使用：加载默认 4 个
        if (configured.Count == 0)
        {
            foreach (var id in QuickActionCatalog.DefaultActionIds)
            {
                var def = QuickActionCatalog.FindById(id);
                if (def != null) QuickActions.Add(QuickActionItemViewModel.FromDefinition(def));
            }
            // 同步到配置中
            PersistCurrentAsDefault();
            return;
        }

        // 按 Order 排序
        var ordered = configured
            .Where(q => q.Enabled)
            .OrderBy(q => q.Order)
            .ToList();

        foreach (var qa in ordered)
        {
            var def = QuickActionCatalog.FindById(qa.Id);
            if (def != null)
            {
                QuickActions.Add(QuickActionItemViewModel.FromDefinition(def));
            }
        }
    }

    private void PersistCurrentAsDefault()
    {
        var list = QuickActions.Select((qa, i) => new QuickAction(qa.Id, i, true)).ToList();
        SettingsService.UpdateGeneral(g => g.QuickActions = list);
    }

    private void RefreshStats()
    {
        try
        {
            // 受控应用数
            var blockage = SettingsService.Blockage;
            BlockedAppsCount = blockage?.GetEffectiveBlockedRules()?.Count ?? 0;
        }
        catch
        {
            BlockedAppsCount = 0;
        }

        try
        {
            // 拦截域名数
            var rules = NetworkRuleService.LoadRules();
            BlockedNetworkCount = rules?.Count ?? 0;
        }
        catch
        {
            BlockedNetworkCount = 0;
        }

        try
        {
            // 下节课
            var (nextPoint, nextDate) = ScheduleService.Instance.GetNextClassPoint();
            if (nextPoint != null && nextDate.HasValue)
            {
                NextScheduleText = $"{nextDate.Value:MM/dd} {nextPoint.StartTime:hh\\:mm} {nextPoint.Label}";
            }
            else
            {
                NextScheduleText = "暂无安排";
            }
        }
        catch
        {
            NextScheduleText = "暂无安排";
        }

        try
        {
            // UIAccess
            HasUiAccess = UiAccessService.Instance.HasUiAccess;
        }
        catch
        {
            HasUiAccess = false;
        }
    }

    [RelayCommand]
    private void OpenQuickAction(QuickActionItemViewModel? item)
    {
        if (item == null) return;
        if (item.IsCommand)
        {
            _mainVM.ExecuteQuickActionCommand(item.TargetId);
        }
        else
        {
            _mainVM.NavigateTo(item.TargetId);
        }
    }

    [RelayCommand]
    private void EditQuickActions()
    {
        _mainVM.ShowQuickActionEditor();
    }

    [RelayCommand]
    private void StartLock()
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            _mainVM.Status = "请先完成初始设置";
            return;
        }
        var lockMode = SettingsService.Lock.BreakTimeLockMode;
        _mainVM.Status = lockMode == LockMode.ProtectionOnly ? "仅防护模式已启动" : "屏幕锁定已启动";
        LockScreenService.Instance.ActivateLock(lockMode);
    }

    /// <summary>
    /// 刷新主页状态（供快速操作调用 / 导航到主页时）
    /// </summary>
    public void RefreshStatus()
    {
        LoadQuickActions();
        RefreshStats();
        _mainVM.Status = "已刷新";
    }
}
