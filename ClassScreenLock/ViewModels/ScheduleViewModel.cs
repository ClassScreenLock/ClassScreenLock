using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
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
    private bool _isSyncingDaySelection;
    private bool _isCoercingTimePoint;
    private bool _suppressTermStartDateTextSideEffects;

    [ObservableProperty]
    private ObservableCollection<SchedulePlan> _schedules = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanelWithSchedule))]
    private SchedulePlan? _selectedSchedule;

    partial void OnSelectedScheduleChanged(SchedulePlan? value)
    {
        SelectedTimePoint = null;
        AttachScheduleHandlers(value);
        SortTimePointsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSortTimePoints));
        OnPropertyChanged(nameof(ShowSettingsPanelWithSchedule));
    }

    [ObservableProperty]
    private TimePoint? _selectedTimePoint;

    [ObservableProperty]
    private bool _isBreakNow;

    [ObservableProperty]
    private TimePoint? _currentBreakTimePoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanelWithSchedule))]
    private bool _isDetailVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSchedulePanel))]
    private bool _isSchedulePanelVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanel))]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanelWithSchedule))]
    private bool _isSettingsVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [NotifyPropertyChangedFor(nameof(ShowSchedulePanel))]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanel))]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanelWithSchedule))]
    [NotifyPropertyChangedFor(nameof(CanToggleSettings))]
    private bool _isMobileView;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanel))]
    [NotifyPropertyChangedFor(nameof(ShowSettingsPanelWithSchedule))]
    [NotifyPropertyChangedFor(nameof(CanToggleSettings))]
    private bool _isLargeView;

    public bool ShowSchedulePanel => IsMobileView && IsSchedulePanelVisible;

    public bool ShowSettingsPanel => (IsLargeView || IsSettingsVisible) && !IsMobileView;

    public bool ShowSettingsPanelWithSchedule => ShowSettingsPanel && SelectedSchedule != null && !IsDetailVisible;

    public bool CanToggleSettings => !IsMobileView && !IsLargeView;

    public bool IsListVisible => true; // 始终保持列表可见，详情页以覆盖层形式出现

    partial void OnSelectedTimePointChanged(TimePoint? value)
    {
        // 打开详情面板时，自动关闭设置面板，避免详情覆盖设置导致用户被卡住
        if (value != null)
        {
            IsSettingsVisible = false;
        }
        IsDetailVisible = value != null;
        MoveSelectedUpCommand.NotifyCanExecuteChanged();
        MoveSelectedDownCommand.NotifyCanExecuteChanged();
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

    public static int[] HourOptions { get; } = Enumerable.Range(0, 24).ToArray();

    public static int[] MinuteOptions { get; } = Enumerable.Range(0, 60).ToArray();

    private readonly DispatcherTimer _breakTimer;
    private SchedulePlan? _hookedSchedule;

    public ScheduleViewModel()
    {
        InitializeOptions();
        LoadSchedules();
        IsWeeklyMode = true;

        SelectedWeekNumber = WeeklyScheduleService.GetCurrentCycleIndex();

        _breakTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, (_, _) => UpdateBreakState());
        _breakTimer.Start();

        // 监听集控课表同步完成事件，自动刷新UI
        WeeklyScheduleService.Instance.OnScheduleSynced += () =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoadSchedules();
                RefreshWeekOptions();
                RefreshWeeklySelection();
                RefreshTodayScheduleSummary();
            });
        };
    }

    private void InitializeOptions()
    {
        RefreshWeekOptions();
        if (DayOptions.Count == 0)
        {
            for (int d = 1; d <= 7; d++)
            {
                DayOptions.Add(d);
            }
        }
    }

    private void RefreshWeekOptions()
    {
        WeekOptions.Clear();
        for (int w = 1; w <= WeeklyCycleCount; w++)
        {
            WeekOptions.Add(w);
        }
    }

    [ObservableProperty]
    private ObservableCollection<ClassScreenLock.Models.WeeklyScheduleFile> _weeklySchedules = new();

    [ObservableProperty]
    private ClassScreenLock.Models.WeeklyScheduleFile? _selectedWeekly;

    partial void OnSelectedWeeklyChanged(ClassScreenLock.Models.WeeklyScheduleFile? value)
    {
        if (value != null)
        {
            UpdateDayPlans();
            SelectedDayIndex = 1;
        }
    }

    [ObservableProperty]
    private bool _isWeeklyMode = true;

    [ObservableProperty]
    private string _todayScheduleSummary = string.Empty;

    [ObservableProperty]
    private int _selectedWeekNumber = 1;

    [ObservableProperty]
    private int _selectedDayIndex = ToDayIndex(DateTime.Now.DayOfWeek);

    [ObservableProperty]
    private int _weeklyCycleCount = SettingsService.General.WeeklyCycleCount;

    [ObservableProperty]
    private string _termStartDateText = SettingsService.General.TermStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    [ObservableProperty]
    private DateTime? _termStartDateCalendar = SettingsService.General.TermStartDate;

    [ObservableProperty]
    private bool _isTermStartDateInvalid;

    [ObservableProperty]
    private string _termStartDateValidationMessage = string.Empty;

    public ObservableCollection<int> WeeklyCycleOptions { get; } = new() { 1, 2, 3, 4, 5, 6 };

    partial void OnWeeklyCycleCountChanged(int value)
    {
        if (value < 1) value = 1;
        if (value > 6) value = 6;
        SettingsService.UpdateGeneral(s => s.WeeklyCycleCount = value);
        if (SelectedWeekNumber > value) SelectedWeekNumber = value;
        LoadSchedules();
        RefreshWeekOptions();
        RefreshWeeklySelection();
        RefreshTodayScheduleSummary();
    }

    [RelayCommand]
    private async Task SaveAndBackup()
    {
        try
        {
            // 先保存当前设置
            SettingsService.UpdateGeneral(s => s.WeeklyCycleCount = WeeklyCycleCount);
            
            // 重新加载课表以确保所有文件都已创建
            LoadSchedules();
            RefreshWeekOptions();
            RefreshWeeklySelection();
            RefreshTodayScheduleSummary();
            
            // 等待文件写入完成
            await Task.Delay(500);
            
            // 同步备份所有数据
            await DataProtectionService.Instance.SyncToAppDataAsync();
            
            NotificationService.Instance.ShowSuccess("设置已保存并备份");
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"保存失败: {ex.Message}");
        }
    }

    partial void OnTermStartDateTextChanged(string value)
    {
        if (_suppressTermStartDateTextSideEffects)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            SetTermStartDate(null);
            return;
        }

        if (TryParseDate(value, out var date))
        {
            SetTermStartDate(date);
            return;
        }

        IsTermStartDateInvalid = true;
        TermStartDateValidationMessage = "日期格式无效，示例：2026-02-18";
    }

    partial void OnTermStartDateCalendarChanged(DateTime? value)
    {
        if (_suppressTermStartDateTextSideEffects)
        {
            return;
        }

        SetTermStartDate(value?.Date);
    }

    [RelayCommand]
    private void ClearTermStartDate()
    {
        SetTermStartDate(null);
    }

    public ObservableCollection<int> WeekOptions { get; } = new();

    public ObservableCollection<int> DayOptions { get; } = new();

    [RelayCommand]
    private void SetWeekNumber(int week)
    {
        if (week < 1) week = 1;
        if (week > WeeklyCycleCount) week = WeeklyCycleCount;
        SelectedWeekNumber = week;
    }

    [RelayCommand]
    private void SetDayIndex(int day)
    {
        if (day < 1) day = 1;
        if (day > 7) day = 7;
        SelectedDayIndex = day;
    }

    [RelayCommand]
    private void SetWeeklyCycle(object? count)
    {
        int n = WeeklyCycleCount;
        if (count is int i) n = i;
        else if (count is string s && int.TryParse(s, out var parsed)) n = parsed;
        WeeklyCycleCount = n;
    }

    partial void OnIsWeeklyModeChanged(bool value)
    {
        if (value)
        {
            RefreshWeeklySelection();
        }
    }

    partial void OnSelectedWeekNumberChanged(int value)
    {
        if (IsWeeklyMode)
        {
            RefreshWeeklySelection();
        }
    }

    partial void OnSelectedDayIndexChanged(int value)
    {
        if (!_isSyncingDaySelection)
        {
            _isSyncingDaySelection = true;
            SelectedDayTabIndex = Math.Clamp(value - 1, 0, 6);
            _isSyncingDaySelection = false;
        }

        if (IsWeeklyMode)
        {
            RefreshWeeklySelection();
        }
    }

    [ObservableProperty]
    private int _selectedDayTabIndex;

    partial void OnSelectedDayTabIndexChanged(int value)
    {
        if (_isSyncingDaySelection) return;

        _isSyncingDaySelection = true;
        SelectedDayIndex = value + 1;
        _isSyncingDaySelection = false;
    }

    private void RefreshWeeklySelection()
    {
        UpdateDayPlans();
        SelectedSchedule = SelectedDayIndex switch
        {
            1 => Day1Plan,
            2 => Day2Plan,
            3 => Day3Plan,
            4 => Day4Plan,
            5 => Day5Plan,
            6 => Day6Plan,
            7 => Day7Plan,
            _ => Day1Plan
        };
        RefreshTodayScheduleSummary();
    }

    [RelayCommand]
    private void LoadSchedules()
    {
        var weeklies = WeeklyScheduleService.Instance.LoadAllWeekly();
        WeeklySchedules = new ObservableCollection<ClassScreenLock.Models.WeeklyScheduleFile>(weeklies);
        WeeklyCycleCount = SettingsService.General.WeeklyCycleCount;

        _suppressTermStartDateTextSideEffects = true;
        TermStartDateCalendar = SettingsService.General.TermStartDate;
        TermStartDateText = SettingsService.General.TermStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        _suppressTermStartDateTextSideEffects = false;

        if (SelectedWeekly == null && WeeklySchedules.Any())
        {
            SelectedWeekly = WeeklySchedules.First();
        }
        RefreshTodayScheduleSummary();
    }

    [RelayCommand]
    private void AddWeekly()
    {
        var existing = WeeklyScheduleService.Instance.LoadAllWeekly();
        var count = SettingsService.General.WeeklyCycleCount;
        for (int w = 1; w <= count; w++)
        {
            if (!existing.Any(e => e.WeekNumber == w))
            {
                var weekly = new ClassScreenLock.Models.WeeklyScheduleFile { WeekNumber = w, Name = $"第{w}周课表" };
                WeeklyScheduleService.Instance.SaveWeekly(weekly);
                LoadSchedules();
                SelectedWeekly = weekly;
                NotificationService.Instance.ShowSuccess($"已创建第{w}周课表");
                return;
            }
        }
        NotificationService.Instance.ShowInfo($"{count}周课表已存在");
    }

    [RelayCommand]
    private void CopyWeekly()
    {
        if (SelectedWeekly == null) return;
        var w = SelectedWeekly.WeekNumber;
        var subjects = new ObservableCollection<Subject>(SelectedWeekly.Subjects.Select(s => new Subject
        {
            Name = s.Name,
            SimplifiedName = s.SimplifiedName,
            Teacher = s.Teacher,
            Room = s.Room
        }));

        var days = new ObservableCollection<WeeklyDaySchedule>(SelectedWeekly.Days.Select(d => new WeeklyDaySchedule
        {
            EnableDay = d.EnableDay,
            Classes = new ObservableCollection<WeeklyClass>(d.Classes.Select(c => new WeeklyClass
            {
                Type = c.Type,
                Subject = c.Subject,
                Label = c.Label,
                Description = c.Description,
                StartTime = c.StartTime,
                EndTime = c.EndTime
            }))
        }));

        var clone = new ClassScreenLock.Models.WeeklyScheduleFile
        {
            Name = SelectedWeekly.Name + "(副本)",
            WeekNumber = w,
            DefaultClassDuration = SelectedWeekly.DefaultClassDuration,
            DefaultBreakDuration = SelectedWeekly.DefaultBreakDuration,
            Subjects = subjects,
            Days = days
        };
        WeeklyScheduleService.Instance.SaveWeekly(clone);
        LoadSchedules();
        SelectedWeekly = clone;
    }

    private bool CanMoveSelectedUp()
    {
        if (SelectedSchedule == null || SelectedTimePoint == null) return false;
        var index = SelectedSchedule.TimePoints.IndexOf(SelectedTimePoint);
        return index > 0;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedUp))]
    private void MoveSelectedUp()
    {
        if (SelectedSchedule == null || SelectedTimePoint == null) return;
        var index = SelectedSchedule.TimePoints.IndexOf(SelectedTimePoint);
        if (index <= 0) return;
        SelectedSchedule.TimePoints.Move(index, index - 1);
        SaveCurrentScheduleCore(false);
        MoveSelectedUpCommand.NotifyCanExecuteChanged();
        MoveSelectedDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveSelectedDown()
    {
        if (SelectedSchedule == null || SelectedTimePoint == null) return false;
        var index = SelectedSchedule.TimePoints.IndexOf(SelectedTimePoint);
        return index >= 0 && index < SelectedSchedule.TimePoints.Count - 1;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedDown))]
    private void MoveSelectedDown()
    {
        if (SelectedSchedule == null || SelectedTimePoint == null) return;
        var index = SelectedSchedule.TimePoints.IndexOf(SelectedTimePoint);
        if (index < 0 || index >= SelectedSchedule.TimePoints.Count - 1) return;
        SelectedSchedule.TimePoints.Move(index, index + 1);
        SaveCurrentScheduleCore(false);
        MoveSelectedUpCommand.NotifyCanExecuteChanged();
        MoveSelectedDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanSortTimePointsCore()
    {
        return SelectedSchedule != null && SelectedSchedule.TimePoints.Count >= 2;
    }

    public bool CanSortTimePoints => CanSortTimePointsCore();

    [RelayCommand(CanExecute = nameof(CanSortTimePointsCore))]
    private void SortTimePoints()
    {
        if (SelectedSchedule == null || SelectedSchedule.TimePoints.Count <= 1) return;
        var ordered = SelectedSchedule.TimePoints
            .OrderBy(t => t.StartTime)
            .ThenBy(t => t.EndTime)
            .ToList();

        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var item = ordered[targetIndex];
            var currentIndex = SelectedSchedule.TimePoints.IndexOf(item);
            if (currentIndex >= 0 && currentIndex != targetIndex)
            {
                SelectedSchedule.TimePoints.Move(currentIndex, targetIndex);
            }
        }

        SaveCurrentScheduleCore(false);
        MoveSelectedUpCommand.NotifyCanExecuteChanged();
        MoveSelectedDownCommand.NotifyCanExecuteChanged();
        SortTimePointsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSortTimePoints));
    }

    [RelayCommand]
    private void ImportSchedule(string path)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".yml" or ".yaml")
            {
                WeeklyScheduleService.Instance.ImportCsesYaml(path);
                LoadSchedules();
                return;
            }
            try
            {
                var importedWeeklies = WeeklyScheduleService.Instance.ImportWeeklyJson(path);
                if (importedWeeklies.Any())
                {
                    LoadSchedules();
                    SelectedWeekly = importedWeeklies.First();
                    return;
                }
            }
            catch { }
            LoadSchedules();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearWeekly()
    {
        if (SelectedWeekly == null) return;
        var weekly = WeeklyScheduleService.Instance.GetWeekly(SelectedWeekly.WeekNumber) ?? SelectedWeekly;
        if (weekly.Days == null || weekly.Days.Count == 0)
        {
            weekly.Days = new System.Collections.ObjectModel.ObservableCollection<WeeklyDaySchedule>();
            for (int d = 1; d <= 7; d++) weekly.Days.Add(new WeeklyDaySchedule { EnableDay = d });
        }
        foreach (var day in weekly.Days)
        {
            day.Classes.Clear();
        }
        WeeklyScheduleService.Instance.SaveWeekly(weekly);
        LoadSchedules();
        UpdateDayPlans();
        NotificationService.Instance.ShowSuccess($"已清空第{weekly.WeekNumber}周课表");
    }

    [RelayCommand]
    private void ExportSchedule(string path)
    {
        try
        {
            var weekNum = SelectedWeekly?.WeekNumber ?? SelectedWeekNumber;
            var weekly = WeeklyScheduleService.Instance.GetWeekly(weekNum);
            if (weekly == null) return;
            WeeklyScheduleService.Instance.SaveWeekly(weekly);
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
        SaveCurrentScheduleCore(false);
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
        SaveCurrentScheduleCore(false);
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

        var schedule = ScheduleService.Instance.GetActiveSchedule() ?? SelectedSchedule;
        if (schedule == null || schedule.TimePoints == null || schedule.TimePoints.Count == 0)
        {
            IsBreakNow = true;
            CurrentBreakTimePoint = null;
            return;
        }

        var classPoint = schedule.TimePoints
            .Where(t => t.Type == TimePointType.Class)
            .FirstOrDefault(t => now >= t.StartTime && now < t.EndTime);

        if (classPoint != null)
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
            IsBreakNow = true;
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
        SaveCurrentScheduleCore(false);
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
        SaveCurrentScheduleCore(false);
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
            SaveCurrentScheduleCore(false);
        }
    }

    private void SaveCurrentScheduleCore(bool showNotification)
    {
        if (SelectedSchedule == null)
        {
            return;
        }

        var weekNum = SelectedWeekly?.WeekNumber ?? SelectedWeekNumber;
        WeeklyScheduleService.Instance.SaveDayFromPlan(weekNum, SelectedDayIndex, SelectedSchedule);

        RefreshTodayScheduleSummary();
        if (showNotification)
        {
            NotificationService.Instance.ShowSuccess("时间计划已保存");
        }
    }

    [RelayCommand]
    private void SaveCurrentSchedule()
    {
        SaveCurrentScheduleCore(true);
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

    private static int ToDayIndex(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            DayOfWeek.Sunday => 7,
            _ => 1
        };
    }

    private static string ToChineseWeekday(int dayIndex)
    {
        return dayIndex switch
        {
            1 => "一",
            2 => "二",
            3 => "三",
            4 => "四",
            5 => "五",
            6 => "六",
            7 => "日",
            _ => dayIndex.ToString()
        };
    }

    private void RefreshTodayScheduleSummary()
    {
        try
        {
            var plan = ScheduleService.Instance.GetActiveSchedule();
            if (plan == null)
            {
                TodayScheduleSummary = "今天没有匹配的时间计划";
                return;
            }

            string dayPart = plan.EnableDay.HasValue ? $"周{ToChineseWeekday(plan.EnableDay.Value)}" : string.Empty;
            string weekPart = string.IsNullOrWhiteSpace(plan.Weeks) ? string.Empty : $"周序 {plan.Weeks}";
            string rulePart;
            if (!string.IsNullOrEmpty(dayPart) && !string.IsNullOrEmpty(weekPart))
            {
                rulePart = $"{dayPart} / {weekPart}";
            }
            else if (!string.IsNullOrEmpty(dayPart))
            {
                rulePart = dayPart;
            }
            else if (!string.IsNullOrEmpty(weekPart))
            {
                rulePart = weekPart;
            }
            else
            {
                rulePart = "通用";
            }

            TodayScheduleSummary = $"今天执行：{plan.Name}（{rulePart}）";
        }
        catch
        {
            TodayScheduleSummary = "今天执行的时间计划获取失败";
        }
    }

    [ObservableProperty]
    private SchedulePlan? _day1Plan;
    [ObservableProperty]
    private SchedulePlan? _day2Plan;
    [ObservableProperty]
    private SchedulePlan? _day3Plan;
    [ObservableProperty]
    private SchedulePlan? _day4Plan;
    [ObservableProperty]
    private SchedulePlan? _day5Plan;
    [ObservableProperty]
    private SchedulePlan? _day6Plan;
    [ObservableProperty]
    private SchedulePlan? _day7Plan;

    private void UpdateDayPlans()
    {
        var weekNum = SelectedWeekly?.WeekNumber ?? SelectedWeekNumber;
        Day1Plan = WeeklyScheduleService.Instance.BuildPlanFor(weekNum, 1) ?? new SchedulePlan { Name = $"第{weekNum}周-周一", DefaultClassDuration = 45, DefaultBreakDuration = 10, EnableDay = 1, Weeks = weekNum.ToString() };
        Day2Plan = WeeklyScheduleService.Instance.BuildPlanFor(weekNum, 2) ?? new SchedulePlan { Name = $"第{weekNum}周-周二", DefaultClassDuration = 45, DefaultBreakDuration = 10, EnableDay = 2, Weeks = weekNum.ToString() };
        Day3Plan = WeeklyScheduleService.Instance.BuildPlanFor(weekNum, 3) ?? new SchedulePlan { Name = $"第{weekNum}周-周三", DefaultClassDuration = 45, DefaultBreakDuration = 10, EnableDay = 3, Weeks = weekNum.ToString() };
        Day4Plan = WeeklyScheduleService.Instance.BuildPlanFor(weekNum, 4) ?? new SchedulePlan { Name = $"第{weekNum}周-周四", DefaultClassDuration = 45, DefaultBreakDuration = 10, EnableDay = 4, Weeks = weekNum.ToString() };
        Day5Plan = WeeklyScheduleService.Instance.BuildPlanFor(weekNum, 5) ?? new SchedulePlan { Name = $"第{weekNum}周-周五", DefaultClassDuration = 45, DefaultBreakDuration = 10, EnableDay = 5, Weeks = weekNum.ToString() };
        Day6Plan = WeeklyScheduleService.Instance.BuildPlanFor(weekNum, 6) ?? new SchedulePlan { Name = $"第{weekNum}周-周六", DefaultClassDuration = 45, DefaultBreakDuration = 10, EnableDay = 6, Weeks = weekNum.ToString() };
        Day7Plan = WeeklyScheduleService.Instance.BuildPlanFor(weekNum, 7) ?? new SchedulePlan { Name = $"第{weekNum}周-周日", DefaultClassDuration = 45, DefaultBreakDuration = 10, EnableDay = 7, Weeks = weekNum.ToString() };
    }

    private void AttachScheduleHandlers(SchedulePlan? schedule)
    {
        if (_hookedSchedule == schedule) return;

        if (_hookedSchedule != null)
        {
            _hookedSchedule.TimePoints.CollectionChanged -= OnTimePointsChanged;
            foreach (var tp in _hookedSchedule.TimePoints)
            {
                tp.PropertyChanged -= OnTimePointPropertyChanged;
            }
        }

        _hookedSchedule = schedule;
        if (_hookedSchedule == null) return;

        _hookedSchedule.TimePoints.CollectionChanged += OnTimePointsChanged;
        foreach (var tp in _hookedSchedule.TimePoints)
        {
            tp.PropertyChanged += OnTimePointPropertyChanged;
        }
    }

    private void OnTimePointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is TimePoint tp)
                {
                    tp.PropertyChanged += OnTimePointPropertyChanged;
                }
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is TimePoint tp)
                {
                    tp.PropertyChanged -= OnTimePointPropertyChanged;
                }
            }
        }

        SaveCurrentScheduleCore(false);
        SortTimePointsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSortTimePoints));
    }

    private void OnTimePointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isCoercingTimePoint)
        {
            return;
        }

        if (sender is TimePoint tp)
        {
            _isCoercingTimePoint = true;
            if (tp.Type is TimePointType.Divider or TimePointType.Action)
            {
                if (tp.EndTime != tp.StartTime)
                {
                    tp.EndTime = tp.StartTime;
                }
            }
            else
            {
                if (tp.EndTime < tp.StartTime)
                {
                    tp.EndTime = tp.StartTime;
                }
            }
            _isCoercingTimePoint = false;
        }

        SaveCurrentScheduleCore(false);
    }

    [RelayCommand]
    private void SaveWeeklyAll()
    {
        var weekNum = SelectedWeekly?.WeekNumber ?? SelectedWeekNumber;
        if (Day1Plan != null) WeeklyScheduleService.Instance.SaveDayFromPlan(weekNum, 1, Day1Plan);
        if (Day2Plan != null) WeeklyScheduleService.Instance.SaveDayFromPlan(weekNum, 2, Day2Plan);
        if (Day3Plan != null) WeeklyScheduleService.Instance.SaveDayFromPlan(weekNum, 3, Day3Plan);
        if (Day4Plan != null) WeeklyScheduleService.Instance.SaveDayFromPlan(weekNum, 4, Day4Plan);
        if (Day5Plan != null) WeeklyScheduleService.Instance.SaveDayFromPlan(weekNum, 5, Day5Plan);
        if (Day6Plan != null) WeeklyScheduleService.Instance.SaveDayFromPlan(weekNum, 6, Day6Plan);
        if (Day7Plan != null) WeeklyScheduleService.Instance.SaveDayFromPlan(weekNum, 7, Day7Plan);
        NotificationService.Instance.ShowSuccess($"已保存第{weekNum}周七天课表");
    }

    private void SetTermStartDate(DateTime? date)
    {
        SettingsService.UpdateGeneral(s => s.TermStartDate = date?.Date);

        _suppressTermStartDateTextSideEffects = true;
        TermStartDateCalendar = date?.Date;
        TermStartDateText = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        _suppressTermStartDateTextSideEffects = false;

        IsTermStartDateInvalid = false;
        TermStartDateValidationMessage = string.Empty;

        SelectedWeekNumber = WeeklyScheduleService.GetCurrentCycleIndex();
        RefreshWeeklySelection();
        RefreshTodayScheduleSummary();
    }

    private static bool TryParseDate(string text, out DateTime date)
    {
        text = text.Trim();
        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy-M-d",
            "yyyy/MM/dd",
            "yyyy/M/d",
            "yyyy.MM.dd",
            "yyyy.M.d"
        };

        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _breakTimer?.Stop();
        }
        base.Dispose(disposing);
    }
}
