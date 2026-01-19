using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Models;
using ClassScreenLock.Services;

namespace ClassScreenLock.ViewModels;

public partial class ScheduleViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<SchedulePlan> _schedules = new();

    [ObservableProperty]
    private SchedulePlan? _selectedSchedule;

    partial void OnSelectedScheduleChanged(SchedulePlan? value)
    {
        SelectedTimePoint = null;
    }

    [ObservableProperty]
    private TimePoint? _selectedTimePoint;

    [ObservableProperty]
    private bool _isBreakNow;

    [ObservableProperty]
    private TimePoint? _currentBreakTimePoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    private bool _isDetailVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSchedulePanel))]
    private bool _isSchedulePanelVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanel))]
    private bool _isSettingsVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [NotifyPropertyChangedFor(nameof(ShowSchedulePanel))]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanel))]
    [NotifyPropertyChangedFor(nameof(CanToggleSettings))]
    private bool _isMobileView;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanel))]
    [NotifyPropertyChangedFor(nameof(CanToggleSettings))]
    private bool _isLargeView;

    public bool ShowSchedulePanel => IsMobileView && IsSchedulePanelVisible;

    public bool ShowSettingsPanel => (IsLargeView || IsSettingsVisible) && !IsMobileView;

    public bool CanToggleSettings => !IsMobileView && !IsLargeView;

    public bool IsListVisible => true; // 始终保持列表可见，详情页以覆盖层形式出现

    partial void OnSelectedTimePointChanged(TimePoint? value)
    {
        IsDetailVisible = value != null;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        SelectedTimePoint = null;
        IsDetailVisible = false;
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsVisible = !IsSettingsVisible;
        if (IsSettingsVisible) IsDetailVisible = false;
        if (IsSettingsVisible) IsSchedulePanelVisible = false;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsVisible = false;
    }

    [RelayCommand]
    private void ToggleSchedulePanel()
    {
        IsSchedulePanelVisible = !IsSchedulePanelVisible;
        if (IsSchedulePanelVisible)
        {
            IsSettingsVisible = false;
            IsDetailVisible = false;
        }
    }

    [RelayCommand]
    private void CloseSchedulePanel()
    {
        IsSchedulePanelVisible = false;
    }

    public static TimePointType[] AllTimePointTypes => Enum.GetValues<TimePointType>();

    private readonly DispatcherTimer _breakTimer;

    public ScheduleViewModel()
    {
        LoadSchedules();
        if (Schedules.Count == 0)
        {
            AddSchedule();
        }
        else
        {
            SelectedSchedule = Schedules.FirstOrDefault();
        }

        _breakTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, (_, _) => UpdateBreakState());
        _breakTimer.Start();
    }

    [RelayCommand]
    private void LoadSchedules()
    {
        var loaded = ScheduleService.Instance.LoadAllSchedules();
        Schedules = new ObservableCollection<SchedulePlan>(loaded);
        if (SelectedSchedule == null && Schedules.Any())
        {
            SelectedSchedule = Schedules.First();
        }
    }

    [RelayCommand]
    private void AddSchedule()
    {
        var newSchedule = new SchedulePlan { Name = "新时间表" };
        Schedules.Add(newSchedule);
        SelectedSchedule = newSchedule;
        SaveCurrentSchedule();
    }

    [RelayCommand]
    private void CopySchedule()
    {
        if (SelectedSchedule == null) return;

        var newSchedule = new SchedulePlan
        {
            Name = SelectedSchedule.Name + " (副本)",
            DefaultClassDuration = SelectedSchedule.DefaultClassDuration,
            DefaultBreakDuration = SelectedSchedule.DefaultBreakDuration
        };

        foreach (var tp in SelectedSchedule.TimePoints)
        {
            newSchedule.TimePoints.Add(new TimePoint
            {
                Label = tp.Label,
                Type = tp.Type,
                StartTime = tp.StartTime,
                EndTime = tp.EndTime,
                Description = tp.Description
            });
        }

        Schedules.Add(newSchedule);
        SelectedSchedule = newSchedule;
        SaveCurrentSchedule();
    }

    [RelayCommand]
    private void DeleteSchedule()
    {
        if (SelectedSchedule != null)
        {
            ScheduleService.Instance.DeleteSchedule(SelectedSchedule.Id);
            Schedules.Remove(SelectedSchedule);
            SelectedSchedule = Schedules.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void ImportSchedule(string path)
    {
        try
        {
            var imported = ScheduleService.Instance.ImportSchedule(path);
            if (imported != null)
            {
                Schedules.Add(imported);
                SelectedSchedule = imported;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ExportSchedule(string path)
    {
        if (SelectedSchedule == null) return;
        try
        {
            ScheduleService.Instance.ExportSchedule(SelectedSchedule, path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void AddClass()
    {
        if (SelectedSchedule == null) return;

        var startTime = GetNextStartTime();
        var endTime = startTime.Add(TimeSpan.FromMinutes(SelectedSchedule.DefaultClassDuration));

        var timePoint = new TimePoint
        {
            Type = TimePointType.Class,
            Label = "新课程",
            StartTime = startTime,
            EndTime = endTime
        };
        SelectedSchedule.TimePoints.Add(timePoint);
        SelectedTimePoint = timePoint;
        SaveCurrentSchedule();
    }

    [RelayCommand]
    private void AddBreak()
    {
        if (SelectedSchedule == null) return;

        var startTime = GetNextStartTime();
        var endTime = startTime.Add(TimeSpan.FromMinutes(SelectedSchedule.DefaultBreakDuration));

        var timePoint = new TimePoint
        {
            Type = TimePointType.Break,
            Label = "课间休息",
            StartTime = startTime,
            EndTime = endTime
        };
        SelectedSchedule.TimePoints.Add(timePoint);
        SelectedTimePoint = timePoint;
        SaveCurrentSchedule();
    }

    private DateTime _lastClickTime = DateTime.MinValue;

    [RelayCommand]
    private async Task StartBreakLock()
    {
        if (!IsBreakNow)
        {
            return;
        }

        var now = DateTime.Now;
        if ((now - _lastClickTime).TotalMilliseconds < 500)
        {
            // 双击成功
            var settings = SettingsService.Lock;
            LockScreenService.Instance.ActivateLock(settings.BreakTimeLockMode);
            NotificationService.Instance.ShowSuccess("下课锁屏已启动");
            _lastClickTime = DateTime.MinValue; // 重置
        }
        else
        {
            // 第一次点击
            _lastClickTime = now;
            NotificationService.Instance.ShowInfo("请再次点击按钮以确认锁屏 (防误触)");
            
            // 3秒后重置，如果还没点击第二次
            await Task.Delay(3000);
            if (_lastClickTime == now)
            {
                _lastClickTime = DateTime.MinValue;
            }
        }
    }

    private void UpdateBreakState()
    {
        var settings = SettingsService.Lock;
        if (!settings.EnableBreakTimeLock)
        {
            IsBreakNow = false;
            CurrentBreakTimePoint = null;
            return;
        }

        var now = DateTime.Now.TimeOfDay;

        var schedule = SelectedSchedule ?? Schedules.FirstOrDefault();
        if (schedule == null || schedule.TimePoints == null || schedule.TimePoints.Count == 0)
        {
            IsBreakNow = false;
            CurrentBreakTimePoint = null;
            return;
        }

        var breakPoint = schedule.TimePoints
            .Where(t => t.Type == TimePointType.Break)
            .FirstOrDefault(t => now >= t.StartTime && now < t.EndTime);

        if (breakPoint != null)
        {
            IsBreakNow = true;
            CurrentBreakTimePoint = breakPoint;
        }
        else
        {
            IsBreakNow = false;
            CurrentBreakTimePoint = null;
        }
    }

    [RelayCommand]
    private void AddDivider()
    {
        if (SelectedSchedule == null) return;

        var startTime = GetNextStartTime();
        var timePoint = new TimePoint
        {
            Type = TimePointType.Divider,
            Label = "分割线",
            StartTime = startTime,
            EndTime = startTime
        };
        SelectedSchedule.TimePoints.Add(timePoint);
        SelectedTimePoint = timePoint;
        SaveCurrentSchedule();
    }

    [RelayCommand]
    private void AddAction()
    {
        if (SelectedSchedule == null) return;

        var startTime = GetNextStartTime();
        var timePoint = new TimePoint
        {
            Type = TimePointType.Action,
            Label = "行动点",
            StartTime = startTime,
            EndTime = startTime
        };
        SelectedSchedule.TimePoints.Add(timePoint);
        SelectedTimePoint = timePoint;
        SaveCurrentSchedule();
    }

    [RelayCommand]
    private void DeleteTimePoint(TimePoint? timePoint)
    {
        var target = timePoint ?? SelectedTimePoint;
        if (SelectedSchedule != null && target != null)
        {
            SelectedSchedule.TimePoints.Remove(target);
            if (SelectedTimePoint == target)
            {
                SelectedTimePoint = null;
                IsDetailVisible = false;
            }
            SaveCurrentSchedule();
        }
    }

    [RelayCommand]
    private void SaveCurrentSchedule()
    {
        if (SelectedSchedule != null)
        {
            ScheduleService.Instance.SaveSchedule(SelectedSchedule);
        }
    }

    [RelayCommand]
    private void ActivateSchedule(SchedulePlan? schedule)
    {
        var target = schedule ?? SelectedSchedule;
        if (target == null) return;

        foreach (var s in Schedules)
        {
            s.IsActive = (s.Id == target.Id);
            ScheduleService.Instance.SaveSchedule(s);
        }
        
        NotificationService.Instance.ShowSuccess($"已激活时间计划: {target.Name}");
    }

    private TimeSpan GetNextStartTime()
    {
        if (SelectedSchedule == null || !SelectedSchedule.TimePoints.Any())
        {
            return new TimeSpan(8, 0, 0); // 默认早上8点
        }

        var last = SelectedSchedule.TimePoints.OrderBy(t => t.EndTime).Last();
        return last.EndTime;
    }
}
