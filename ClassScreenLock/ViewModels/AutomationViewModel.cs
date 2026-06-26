using System;
using System.Timers;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using Avalonia.Threading;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Media;
using Avalonia.Controls;
using Avalonia;
using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace ClassScreenLock.ViewModels;

public partial class AutomationViewModel : ViewModelBase
{
    private readonly Timer _checkTimer;
    private bool _suppressUpdate;
    private bool _startupProcessed;

    [ObservableProperty]
    private bool _enableAutoShutdown;

    [ObservableProperty]
    private TimeSpan? _autoShutdownTime;

    [ObservableProperty]
    private bool _enableAutoRestart;

    [ObservableProperty]
    private TimeSpan? _autoRestartTime;

    [ObservableProperty]
    private bool _enableAutoLock;

    [ObservableProperty]
    private TimeSpan? _autoLockTime;

    [ObservableProperty]
    private bool _enableAutoNetworkLockOn;

    [ObservableProperty]
    private TimeSpan? _autoNetworkLockOnTime;

    [ObservableProperty]
    private bool _enableAutoNetworkLockOff;

    [ObservableProperty]
    private TimeSpan? _autoNetworkLockOffTime;

    [ObservableProperty]
    private bool _enableAutoAppBlockOn;

    [ObservableProperty]
    private TimeSpan? _autoAppBlockOnTime;

    [ObservableProperty]
    private bool _enableAutoAppBlockOff;

    [ObservableProperty]
    private TimeSpan? _autoAppBlockOffTime;

    [ObservableProperty]
    private bool _enableAutoWebcamCapture;

    [ObservableProperty]
    private TimeSpan? _autoWebcamCaptureTime;

    [ObservableProperty]
    private bool _isAutomationEnabled;

    [ObservableProperty]
    private ObservableCollection<AutomationWorkflow> _workflows = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateWorkflowCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteWorkflowCommand))]
    private AutomationWorkflow? _selectedWorkflow;

    public int[] AvailableHours { get; } = Enumerable.Range(0, 24).ToArray();
    public int[] AvailableMinutes { get; } = Enumerable.Range(0, 60).ToArray();
    public double?[] AvailableCaptureDelays { get; } = new double?[] { 0.5, 1.0 }.Concat(Enumerable.Range(1, 30).Select(i => (double?)(i * 2))).ToArray();
    public int[] AvailableCheckIntervals { get; } = Enumerable.Range(1, 120).ToArray();

    [ObservableProperty]
    private ObservableCollection<string> _availableSchemes = new();

    [ObservableProperty]
    private string _currentAutomationScheme = string.Empty;

    [ObservableProperty]
    private string _newSchemeName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableConfigs = new();

    [ObservableProperty]
    private string _currentAutomationConfig = string.Empty;

    [ObservableProperty]
    private string _newConfigName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AutomationWorkflow> _visibleWorkflows = new();

    [ObservableProperty]
    private string _processFilterText = string.Empty;

    public class ProcessSuggestion
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Icon { get; set; } = "fas fa-window-restore";
    }

    public ObservableCollection<ProcessSuggestion> AvailableSuggestions { get; private set; } = new();
    public ObservableCollection<ProcessSuggestion> FilteredSuggestions { get; private set; } = new();

    public AutomationViewModel()
    {
        // 先初始化数据，再加载设置
        _availableSchemes = new ObservableCollection<string>();
        _workflows = new ObservableCollection<AutomationWorkflow>();
        
        LoadSettings();
        RefreshProcesses();
        
        _checkTimer = new Timer(5000);
        _checkTimer.Enabled = false;
        AutomationService.Instance.ForceCheck();
        try { SettingsService.GeneralChanged += OnGeneralChanged; } catch { }
        AttachSelectedWorkflowListeners();
    }

    private void OnGeneralChanged()
    {
        if (_suppressUpdate || _isInternalLoading) return;
        LoadSettings();
    }

    private bool _isInternalLoading;
    private void LoadSettings()
    {
        if (_isInternalLoading) return;
        _isInternalLoading = true;
        var wasSuppressing = _suppressUpdate;
        _suppressUpdate = true;
        try
        {
            var general = SettingsService.General;
            var settings = SettingsService.Automation;

            LoadSchemes(general);
            LoadWorkflows(settings, CurrentAutomationScheme);
            LoadOtherSettings(settings);
        }
        finally
        {
            _suppressUpdate = wasSuppressing;
            _isInternalLoading = false;
        }
    }

    private void LoadSchemes(SettingsModel general)
    {
        var schemes = general.AutomationSchemes;
        if (schemes == null || schemes.Count == 0)
        {
            schemes = new List<string> { "Default" };
            SettingsService.UpdateGeneral(g => g.AutomationSchemes = schemes);
        }

        AvailableSchemes.Clear();
        foreach (var s in schemes) AvailableSchemes.Add(s);

        var scheme = general.CurrentAutomationScheme;
        if (string.IsNullOrEmpty(scheme) || !AvailableSchemes.Contains(scheme))
        {
            scheme = AvailableSchemes.FirstOrDefault() ?? "Default";
            SettingsService.UpdateGeneral(g => g.CurrentAutomationScheme = scheme);
        }

        CurrentAutomationScheme = scheme;
        _oldScheme = scheme;
    }

    private void LoadWorkflows(AutomationSettingsModel settings, string scheme)
    {
        IsAutomationEnabled = settings.IsAutomationEnabled;
        var loadedWorkflows = settings.Workflows ?? new List<AutomationWorkflow>();

        Workflows.Clear();
        foreach (var wf in loadedWorkflows)
        {
            InitializeWorkflowTriggers(wf);
            Workflows.Add(wf);
        }

        if (Workflows.Count == 0)
        {
            var wf = new AutomationWorkflow { Name = "新工作流", Scheme = scheme };
            Workflows.Add(wf);
        }

        UpdateVisibleWorkflows();
        SelectedWorkflow = Workflows.FirstOrDefault();
    }

    private void InitializeWorkflowTriggers(AutomationWorkflow wf)
    {
        foreach (var t in wf.Triggers)
        {
            if (string.Equals(t.Type, "ProcessRunning", StringComparison.OrdinalIgnoreCase) && !t.CheckIntervalSeconds.HasValue)
            {
                t.CheckIntervalSeconds = 5;
            }
        }
    }

    private void LoadOtherSettings(AutomationSettingsModel settings)
    {
        EnableAutoShutdown = settings.EnableAutoShutdown;
        AutoShutdownTime = settings.AutoShutdownTime;
        EnableAutoRestart = settings.EnableAutoRestart;
        AutoRestartTime = settings.AutoRestartTime;
        EnableAutoLock = settings.EnableAutoLock;
        AutoLockTime = settings.AutoLockTime;
        EnableAutoNetworkLockOn = settings.EnableAutoNetworkLockOn;
        AutoNetworkLockOnTime = settings.AutoNetworkLockOnTime;
        EnableAutoNetworkLockOff = settings.EnableAutoNetworkLockOff;
        AutoNetworkLockOffTime = settings.AutoNetworkLockOffTime;
        EnableAutoAppBlockOn = settings.EnableAutoAppBlockOn;
        AutoAppBlockOnTime = settings.AutoAppBlockOnTime;
        EnableAutoAppBlockOff = settings.EnableAutoAppBlockOff;
        AutoAppBlockOffTime = settings.AutoAppBlockOffTime;
        EnableAutoWebcamCapture = settings.EnableAutoWebcamCapture;
        AutoWebcamCaptureTime = settings.AutoWebcamCaptureTime;
    }

    private string? _oldScheme;
    partial void OnCurrentAutomationSchemeChanged(string value)
    {
        if (_suppressUpdate || string.IsNullOrEmpty(value)) return;
        
        // 如果新旧值一样，不做处理
        if (value == _oldScheme) return;

        var wasSuppressing = _suppressUpdate;
        _suppressUpdate = true;
        try
        {
            // 1. 保存当前数据到旧方案（如果存在）
            if (!string.IsNullOrEmpty(_oldScheme))
            {
                // 先临时切回旧方案名以便 SaveSettings 能找到正确的路径
                SettingsService.UpdateGeneral(g => g.CurrentAutomationScheme = _oldScheme);
                SaveSettings();
            }

            // 2. 正式切换到新方案
            SettingsService.UpdateGeneral(g => g.CurrentAutomationScheme = value);
            _oldScheme = value;
            
            // 3. 加载新方案数据
            LoadSettings();
        }
        finally
        {
            _suppressUpdate = wasSuppressing;
        }
    }

    partial void OnCurrentAutomationConfigChanged(string value)
    {
        SettingsService.UpdateGeneral(g => { g.CurrentAutomationConfig = value; });
        LoadSettings();
    }

    partial void OnIsAutomationEnabledChanged(bool value) => SaveSettings();

    [RelayCommand]
    private void ToggleAutomation()
    {
        IsAutomationEnabled = !IsAutomationEnabled;
    }

    private void UpdateVisibleWorkflows()
    {
        var scheme = CurrentAutomationScheme ?? "Default";
        VisibleWorkflows = new ObservableCollection<AutomationWorkflow>(Workflows.Where(w => string.Equals(w.Scheme ?? "Default", scheme, StringComparison.OrdinalIgnoreCase)));
    }

    partial void OnEnableAutoShutdownChanged(bool value) => SaveSettings();
    partial void OnAutoShutdownTimeChanged(TimeSpan? value) => SaveSettings();
    partial void OnEnableAutoRestartChanged(bool value) => SaveSettings();
    partial void OnAutoRestartTimeChanged(TimeSpan? value) => SaveSettings();
    partial void OnEnableAutoLockChanged(bool value) => SaveSettings();
    partial void OnAutoLockTimeChanged(TimeSpan? value) => SaveSettings();
    partial void OnEnableAutoNetworkLockOnChanged(bool value) => SaveSettings();
    partial void OnAutoNetworkLockOnTimeChanged(TimeSpan? value) => SaveSettings();
    partial void OnEnableAutoNetworkLockOffChanged(bool value) => SaveSettings();
    partial void OnAutoNetworkLockOffTimeChanged(TimeSpan? value) => SaveSettings();
    partial void OnEnableAutoAppBlockOnChanged(bool value) => SaveSettings();
    partial void OnAutoAppBlockOnTimeChanged(TimeSpan? value) => SaveSettings();
    partial void OnEnableAutoAppBlockOffChanged(bool value) => SaveSettings();
    partial void OnAutoAppBlockOffTimeChanged(TimeSpan? value) => SaveSettings();
    partial void OnEnableAutoWebcamCaptureChanged(bool value) => SaveSettings();
    partial void OnAutoWebcamCaptureTimeChanged(TimeSpan? value) => SaveSettings();

    partial void OnSelectedWorkflowChanged(AutomationWorkflow? value)
    {
        SaveSettings();
        AttachSelectedWorkflowListeners();
    }

    private AutomationWorkflow? _attachedWorkflow;

    private void AttachSelectedWorkflowListeners()
    {
        DetachWorkflowListeners();
        _attachedWorkflow = SelectedWorkflow;
        AttachWorkflowListeners();
    }

    private void DetachWorkflowListeners()
    {
        if (_attachedWorkflow == null) return;
        try
        {
            _attachedWorkflow.Triggers.CollectionChanged -= OnTriggersCollectionChanged;
            foreach (var t in _attachedWorkflow.Triggers) t.PropertyChanged -= OnTriggerPropertyChanged;
            _attachedWorkflow.Conditions.CollectionChanged -= OnConditionsCollectionChanged;
            foreach (var c in _attachedWorkflow.Conditions) c.PropertyChanged -= OnConditionPropertyChanged;
            _attachedWorkflow.Actions.CollectionChanged -= OnActionsCollectionChanged;
            foreach (var a in _attachedWorkflow.Actions) a.PropertyChanged -= OnActionPropertyChanged;
            _attachedWorkflow.RecoveryActions.CollectionChanged -= OnActionsCollectionChanged;
            foreach (var a in _attachedWorkflow.RecoveryActions) a.PropertyChanged -= OnActionPropertyChanged;
        }
        catch { }
    }

    private void AttachWorkflowListeners()
    {
        if (_attachedWorkflow == null) return;
        _attachedWorkflow.Triggers.CollectionChanged += OnTriggersCollectionChanged;
        foreach (var t in _attachedWorkflow.Triggers) t.PropertyChanged += OnTriggerPropertyChanged;
        _attachedWorkflow.Conditions.CollectionChanged += OnConditionsCollectionChanged;
        foreach (var c in _attachedWorkflow.Conditions) c.PropertyChanged += OnConditionPropertyChanged;
        _attachedWorkflow.Actions.CollectionChanged += OnActionsCollectionChanged;
        foreach (var a in _attachedWorkflow.Actions) a.PropertyChanged += OnActionPropertyChanged;
        _attachedWorkflow.RecoveryActions.CollectionChanged += OnActionsCollectionChanged;
        foreach (var a in _attachedWorkflow.RecoveryActions) a.PropertyChanged += OnActionPropertyChanged;
    }

    private void OnTriggersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (AutomationTrigger t in e.NewItems) t.PropertyChanged += OnTriggerPropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (AutomationTrigger t in e.OldItems) t.PropertyChanged -= OnTriggerPropertyChanged;
        }
        SaveSettings();
    }

    private void OnConditionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (AutomationCondition c in e.NewItems) c.PropertyChanged += OnConditionPropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (AutomationCondition c in e.OldItems) c.PropertyChanged -= OnConditionPropertyChanged;
        }
        SaveSettings();
    }

    private void OnTriggerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveSettings();
    }

    private void OnConditionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveSettings();
    }

    private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (AutomationAction a in e.NewItems) a.PropertyChanged += OnActionPropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (AutomationAction a in e.OldItems) a.PropertyChanged -= OnActionPropertyChanged;
        }
        SaveSettings();
    }

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_suppressUpdate) return;

        SettingsService.UpdateGeneral(g =>
        {
            g.AutomationSchemes = AvailableSchemes.ToList();
            g.CurrentAutomationScheme = CurrentAutomationScheme ?? "Default";
        });

        SettingsService.UpdateAutomation(s =>
        {
            s.IsAutomationEnabled = IsAutomationEnabled;
            s.Workflows = Workflows.ToList();
            s.EnableAutoShutdown = EnableAutoShutdown;
            if (AutoShutdownTime.HasValue)
            {
                s.AutoShutdownTime = AutoShutdownTime.Value;
            }
            s.EnableAutoRestart = EnableAutoRestart;
            if (AutoRestartTime.HasValue)
            {
                s.AutoRestartTime = AutoRestartTime.Value;
            }
            s.EnableAutoLock = EnableAutoLock;
            if (AutoLockTime.HasValue)
            {
                s.AutoLockTime = AutoLockTime.Value;
            }
            s.EnableAutoNetworkLockOn = EnableAutoNetworkLockOn;
            if (AutoNetworkLockOnTime.HasValue)
            {
                s.AutoNetworkLockOnTime = AutoNetworkLockOnTime.Value;
            }
            s.EnableAutoNetworkLockOff = EnableAutoNetworkLockOff;
            if (AutoNetworkLockOffTime.HasValue)
            {
                s.AutoNetworkLockOffTime = AutoNetworkLockOffTime.Value;
            }
            s.EnableAutoAppBlockOn = EnableAutoAppBlockOn;
            if (AutoAppBlockOnTime.HasValue)
            {
                s.AutoAppBlockOnTime = AutoAppBlockOnTime.Value;
            }
            s.EnableAutoAppBlockOff = EnableAutoAppBlockOff;
            if (AutoAppBlockOffTime.HasValue)
            {
                s.AutoAppBlockOffTime = AutoAppBlockOffTime.Value;
            }
            s.EnableAutoWebcamCapture = EnableAutoWebcamCapture;
            if (AutoWebcamCaptureTime.HasValue)
            {
                s.AutoWebcamCaptureTime = AutoWebcamCaptureTime.Value;
            }
            s.Schemes = AvailableSchemes.ToList();
            s.CurrentScheme = CurrentAutomationScheme ?? "Default";
        });
    }

    [RelayCommand]
    private void AddScheme()
    {
        var name = (NewSchemeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!AvailableSchemes.Contains(name))
        {
            AvailableSchemes.Add(name);
            SettingsService.UpdateGeneral(g =>
            {
                if (g.AutomationSchemes == null) g.AutomationSchemes = new List<string>();
                if (!g.AutomationSchemes.Contains(name)) g.AutomationSchemes.Add(name);
            });
            SettingsService.EnsureAutomationSchemeFile(name);
        }
        NewSchemeName = string.Empty;
        SaveSettings();
    }

    [RelayCommand]
    private void RemoveScheme(string? name)
    {
        var target = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target)) return;
        if (AvailableSchemes.Count <= 1) return;
        if (AvailableSchemes.Contains(target))
        {
            AvailableSchemes.Remove(target);
            SettingsService.UpdateGeneral(g =>
            {
                if (g.AutomationSchemes != null)
                {
                    g.AutomationSchemes = g.AutomationSchemes.Where(x => !string.Equals(x, target, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (string.Equals(g.CurrentAutomationScheme, target, StringComparison.OrdinalIgnoreCase))
                    {
                        g.CurrentAutomationScheme = g.AutomationSchemes.FirstOrDefault() ?? "Default";
                    }
                }
            });
            if (string.Equals(CurrentAutomationScheme, target, StringComparison.OrdinalIgnoreCase))
            {
                CurrentAutomationScheme = AvailableSchemes.FirstOrDefault() ?? "Default";
            }
            UpdateVisibleWorkflows();
            SaveSettings();
        }
    }

    [RelayCommand]
    private void AddConfig()
    {
        var name = (NewConfigName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!AvailableConfigs.Contains(name)) AvailableConfigs.Add(name);
        SettingsService.UpdateGeneral(g =>
        {
            if (g.AutomationConfigs == null) g.AutomationConfigs = new System.Collections.Generic.List<string>();
            if (!g.AutomationConfigs.Contains(name)) g.AutomationConfigs.Add(name);
        });
        SettingsService.EnsureAutomationConfigFile(name);
        NewConfigName = string.Empty;
    }

    [RelayCommand]
    private void RemoveConfig(string? name)
    {
        var target = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target)) return;
        if (string.Equals(target, CurrentAutomationConfig, StringComparison.OrdinalIgnoreCase)) return;
        if (AvailableConfigs.Contains(target)) AvailableConfigs.Remove(target);
        SettingsService.UpdateGeneral(g =>
        {
            if (g.AutomationConfigs != null)
            {
                g.AutomationConfigs = g.AutomationConfigs.Where(x => !string.Equals(x, target, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        });
    }

    [RelayCommand]
    private void SetCurrentAutomationConfig(string? name)
    {
        var target = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target)) return;
        SettingsService.UpdateGeneral(g => { g.CurrentAutomationConfig = target; });
        LoadSettings();
    }

    [RelayCommand]
    private void SaveAutomationNow()
    {
        SettingsService.SaveAutomation(SettingsService.Automation);
    }

    private static IEnumerable<ProcessSuggestion> GetRunningProcessSuggestions()
    {
        var map = new Dictionary<string, ProcessSuggestion>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                var n = p.ProcessName;
                if (string.IsNullOrWhiteSpace(n)) continue;
                string path = string.Empty;
                try { path = p.MainModule?.FileName ?? string.Empty; } catch { path = string.Empty; }
                var icon = "fas fa-window-restore";
                if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) icon = "fas fa-file";
                if (!map.TryGetValue(n, out var existing))
                {
                    map[n] = new ProcessSuggestion { Name = n, Path = path, Icon = icon };
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(existing.Path) && !string.IsNullOrWhiteSpace(path)) existing.Path = path;
                }
            }
        }
        catch { }
        return map.Values.OrderBy(x => x.Name).ToList();
    }

    private void ApplyProcessFilter()
    {
        var ft = (ProcessFilterText ?? string.Empty).Trim();
        var src = AvailableSuggestions?.ToList() ?? new List<ProcessSuggestion>();

        List<ProcessSuggestion> ranked;
        if (!string.IsNullOrWhiteSpace(ft))
        {
            ranked = src
                .Select(s => new { item = s, score = ScoreCandidateForSuggestion(s, ft) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.item)
                .Take(12)
                .ToList();
        }
        else
        {
            ranked = src.Take(12).ToList();
        }
        FilteredSuggestions = new ObservableCollection<ProcessSuggestion>(ranked);
        OnPropertyChanged(nameof(FilteredSuggestions));
    }

    private static int ScoreCandidate(string name, string query)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        var q = (query ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(q)) return 0;

        int score = 0;
        if (n.Equals(q)) score += 1000;
        if (n.StartsWith(q)) score += 600;
        if (n.Contains(q)) score += 300;

        score += CalculateSequentialMatch(n, q) * 12;
        score += CalculateSubstringMatch(n, q) * 8;

        return score;
    }

    private static int CalculateSequentialMatch(string name, string query)
    {
        int seq = 0;
        int qi = 0;
        for (int i = 0; i < name.Length && qi < query.Length; i++)
        {
            if (name[i] == query[qi]) { seq++; qi++; }
        }
        return seq;
    }

    private static int CalculateSubstringMatch(string name, string query)
    {
        int longest = 0;
        for (int i = 0; i < name.Length; i++)
        {
            int l = 0;
            for (int j = 0; i + j < name.Length && j < query.Length; j++)
            {
                if (name[i + j] == query[j]) l++; else break;
            }
            if (l > longest) longest = l;
        }
        return longest;
    }

    private static int ScoreCandidateForSuggestion(ProcessSuggestion s, string query)
    {
        int score = ScoreCandidate(s?.Name ?? string.Empty, query);
        score += (ScoreCandidate(s?.Path ?? string.Empty, query) / 2);
        return score;
    }

    partial void OnProcessFilterTextChanged(string value)
    {
        ApplyProcessFilter();
    }

    [RelayCommand]
    private void RefreshProcesses()
    {
        AvailableSuggestions = new ObservableCollection<ProcessSuggestion>(GetRunningProcessSuggestions());
        OnPropertyChanged(nameof(AvailableSuggestions));
        ApplyProcessFilter();
    }

    [RelayCommand]
    private void SetFilterText(string? value)
    {
        ProcessFilterText = value ?? string.Empty;
    }

    [RelayCommand]
    private void UseFilterTextForTrigger(AutomationTrigger? trig)
    {
        if (trig == null) return;
        trig.ProcessName = (ProcessFilterText ?? string.Empty).Trim();
        var match = AvailableSuggestions?.FirstOrDefault(s => string.Equals(s.Name, trig.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (match != null && !string.IsNullOrWhiteSpace(match.Path))
        {
            trig.FilePath = match.Path;
        }
        SaveSettings();
    }

    [RelayCommand]
    private void UseFilterTextForCondition(AutomationCondition? cond)
    {
        if (cond == null) return;
        cond.ProcessName = (ProcessFilterText ?? string.Empty).Trim();
        var match = AvailableSuggestions?.FirstOrDefault(s => string.Equals(s.Name, cond.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (match != null && !string.IsNullOrWhiteSpace(match.Path))
        {
            cond.FilePath = match.Path;
        }
        SaveSettings();
    }

    private void OnCheckTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        AutomationService.Instance.ForceCheck();
    }

    private void EvaluateWorkflows()
    {
        var now = DateTime.Now;
        var currentTime = now.TimeOfDay;

        if (IsAutomationEnabled && Workflows.Any())
        {
            foreach (var wf in Workflows.Where(w => w.IsEnabled))
            {
                EvaluateSingleWorkflow(wf, now, currentTime);
            }
            _startupProcessed = true;
        }

        CheckBuiltInAutomations(currentTime);
    }

    private void EvaluateSingleWorkflow(AutomationWorkflow wf, DateTime now, TimeSpan currentTime)
    {
        bool triggerMatched = wf.Triggers.Any(t => EvaluateTrigger(t, wf, now, currentTime));

        bool conditionsOk = !wf.ConditionsEnabled || wf.Conditions.All(c => EvaluateCondition(c, now, currentTime));

        var satisfiedNow = triggerMatched && conditionsOk;
        if (satisfiedNow && !wf.PreviouslySatisfied)
        {
            ExecuteWorkflowActions(wf, now);
        }
        else if (!satisfiedNow && wf.RecoveryEnabled && wf.PreviouslySatisfied)
        {
            ExecuteRecoveryActions(wf);
        }
    }

    private bool EvaluateTrigger(AutomationTrigger t, AutomationWorkflow wf, DateTime now, TimeSpan currentTime)
    {
        return t.Type switch
        {
            "DailyTime" when t.Time.HasValue => Math.Abs((currentTime - t.Time.Value).TotalSeconds) < 30,
            "Interval" when t.IntervalMinutes.HasValue => wf.LastTriggeredAt == null || (now - wf.LastTriggeredAt.Value).TotalMinutes >= t.IntervalMinutes.Value,
            "OnStartup" => !_startupProcessed,
            "ProcessRunning" when !string.IsNullOrWhiteSpace(t.ProcessName) || !string.IsNullOrWhiteSpace(t.FilePath) => IsProcessRunning(t.ProcessName, t.FilePath),
            "ProcessNotRunning" when !string.IsNullOrWhiteSpace(t.ProcessName) || !string.IsNullOrWhiteSpace(t.FilePath) => !IsProcessRunning(t.ProcessName, t.FilePath),
            "NetworkAvailable" => NetworkInterface.GetIsNetworkAvailable(),
            "NetworkUnavailable" => !NetworkInterface.GetIsNetworkAvailable(),
            "FileExists" when !string.IsNullOrWhiteSpace(t.FilePath) => File.Exists(t.FilePath),
            _ => false
        };
    }

    private bool IsProcessRunning(string? processName, string? filePath)
    {
        bool exists = false;
        var name = string.IsNullOrWhiteSpace(processName) ? null : Path.GetFileNameWithoutExtension(processName).Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            try { exists = Process.GetProcesses().Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)); } catch { }
        }
        if (!exists && !string.IsNullOrWhiteSpace(filePath))
        {
            var target = filePath.Trim();
            try { exists = Process.GetProcesses().Any(p => { try { return string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase); } catch { return false; } }); } catch { }
        }
        return exists;
    }

    private bool EvaluateCondition(AutomationCondition c, DateTime now, TimeSpan currentTime)
    {
        return c.Type switch
        {
            "IsLocked" when c.Bool.HasValue => (LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive) == c.Bool.Value,
            "DayOfWeek" when c.Days != null && c.Days.Length > 0 => c.Days.Contains(now.DayOfWeek.ToString()),
            "TimeRange" when c.Start.HasValue && c.End.HasValue => currentTime >= c.Start.Value && currentTime <= c.End.Value,
            "AppBlockingEnabled" when c.Bool.HasValue => SettingsService.Blockage.IsAppBlockingEnabled == c.Bool.Value,
            "NetworkLockEnabled" when c.Bool.HasValue => SettingsService.Blockage.IsNetworkLockEnabled == c.Bool.Value,
            "ProcessRunning" when !string.IsNullOrWhiteSpace(c.ProcessName) || !string.IsNullOrWhiteSpace(c.FilePath) => IsProcessRunning(c.ProcessName, c.FilePath),
            "FileExists" when !string.IsNullOrWhiteSpace(c.FilePath) => File.Exists(c.FilePath),
            _ => true
        };
    }

    private void ExecuteWorkflowActions(AutomationWorkflow wf, DateTime now)
    {
        LogService.Instance.Log("自动化", "触发工作流", "系统", $"工作流 [{wf.Name}] 已触发");
        if (wf.Actions == null || wf.Actions.Count == 0)
        {
            NotificationService.Instance.ShowWarning("工作流已匹配，但未配置任何行动");
        }
        else
        {
            foreach (var a in wf.Actions)
            {
                ExecuteWorkflowAction(a);
            }
        }
        wf.LastTriggeredAt = now;
        wf.PreviouslySatisfied = true;
    }

    private void ExecuteRecoveryActions(AutomationWorkflow wf)
    {
        LogService.Instance.Log("自动化", "执行恢复行动", "系统", $"工作流 [{wf.Name}] 已触发恢复");
        foreach (var a in wf.RecoveryActions)
        {
            ExecuteWorkflowAction(a);
        }
        wf.PreviouslySatisfied = false;
    }

    private void CheckBuiltInAutomations(TimeSpan currentTime)
    {
        CheckTimeBasedAutomation(EnableAutoShutdown, AutoShutdownTime, ExecuteShutdown, currentTime);
        CheckTimeBasedAutomation(EnableAutoRestart, AutoRestartTime, ExecuteRestart, currentTime);
        CheckTimeBasedAutomation(EnableAutoLock, AutoLockTime, ExecuteLock, currentTime);
        CheckTimeBasedAutomation(EnableAutoNetworkLockOn, AutoNetworkLockOnTime, () => ExecuteNetworkLock(true), currentTime);
        CheckTimeBasedAutomation(EnableAutoNetworkLockOff, AutoNetworkLockOffTime, () => ExecuteNetworkLock(false), currentTime);
        CheckTimeBasedAutomation(EnableAutoAppBlockOn, AutoAppBlockOnTime, () => ExecuteAppBlocking(true), currentTime);
        CheckTimeBasedAutomation(EnableAutoAppBlockOff, AutoAppBlockOffTime, () => ExecuteAppBlocking(false), currentTime);
        CheckTimeBasedAutomation(EnableAutoWebcamCapture, AutoWebcamCaptureTime, () => ExecuteWebcamCapture(null), currentTime);
    }

    private void CheckTimeBasedAutomation(bool enabled, TimeSpan? scheduledTime, Action action, TimeSpan currentTime)
    {
        if (enabled && scheduledTime.HasValue && Math.Abs((currentTime - scheduledTime.Value).TotalSeconds) < 30)
        {
            action();
        }
    }

    [RelayCommand]
    private void RunAutomationCheck()
    {
        AutomationService.Instance.ForceCheck();
    }

    private void ExecuteShutdown()
    {
        LogService.Instance.Log("自动化", "关机", "系统", "将在 180 秒后关机，不可取消");
        NotificationService.Instance.ShowWarning("将在 180 秒后关机，不可取消", true);
        System.Diagnostics.Process.Start("shutdown", "/s /t 180 /c \"ClassScreenLock: Scheduled shutdown in 180 seconds\"");
    }

    private void ExecuteRestart()
    {
        _checkTimer.Stop();
        LogService.Instance.Log("自动化", "重启", "系统", "将在 180 秒后重启，不可取消");
        NotificationService.Instance.ShowWarning("将在 180 秒后重启，不可取消", true);
        System.Diagnostics.Process.Start("shutdown", "/r /t 180 /c \"ClassScreenLock: Scheduled restart in 180 seconds\"");
    }

    private void ExecuteLock()
    {
        if (InitializationService.Instance.RequiresInitialization)
        {
            LogService.Instance.Log("Warning", "Automation", "Lock", "Cannot lock: initialization required");
            return;
        }
        
        LogService.Instance.Log("Automation", "Lock", "Screen", "Activating full lock mode");
        LockScreenService.Instance.ActivateLock(LockMode.Full);
    }

    private async void ExecuteNetworkLock(bool enable)
    {
        LogService.Instance.Log("Automation", enable ? "NetworkLockOn" : "NetworkLockOff", "Network", "Toggling network lock");
        SettingsService.UpdateBlockage(s => { s.IsNetworkLockEnabled = enable; });
        await NetworkBlockingService.Instance.ApplyRulesAsync("Automation");
    }

    private void ExecuteAppBlocking(bool enable)
    {
        LogService.Instance.Log("Automation", enable ? "AppBlockOn" : "AppBlockOff", "AppBlocking", "Toggling app blocking");
        SettingsService.UpdateBlockage(s => { s.IsAppBlockingEnabled = enable; });
    }

    private void ExecuteWebcamCapture(AutomationAction? action = null)
    {
        var delay = Math.Clamp(action?.DelaySeconds ?? 0, 0, 60);
        Action doCapture = () =>
        {
            try
            {
                LogService.Instance.Log("自动化", "拍照", "摄像头", "开始拍照...");
                var settings = SettingsService.Screenshot;
                var moniker = settings.SelectedCameraMoniker;
                if (string.IsNullOrEmpty(moniker))
                {
                    try { moniker = WebcamService.Instance.GetAvailableCameras()?.FirstOrDefault() ?? string.Empty; } catch { moniker = string.Empty; }
                }
                if (string.IsNullOrEmpty(moniker))
                {
                    LogService.Instance.Log("自动化", "拍照失败", "摄像头", "未检测到可用摄像头");
                    return;
                }
                WebcamService.Instance.CaptureOnce(moniker);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("自动化", "拍照失败", "错误", ex.Message);
            }
        };
        if (delay > 0)
        {
            LogService.Observe(Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(delay)); doCapture(); }), "Automation.WebcamCapture");
        }
        else
        {
            LogService.Observe(Task.Run(() => doCapture()), "Automation.WebcamCapture");
        }
    }

    private void ExecuteWorkflowAction(AutomationAction action)
    {
        ExecuteActionByType(action.Type, action);
    }

    private void ExecuteActionByType(string actionType, AutomationAction action)
    {
        switch (actionType)
        {
            case "Shutdown":
                ExecuteShutdown();
                break;
            case "Restart":
                ExecuteRestart();
                break;
            case "LockFull":
                ExecuteLock();
                break;
            case "NetworkLockOn":
                ExecuteNetworkLock(true);
                break;
            case "NetworkLockOff":
                ExecuteNetworkLock(false);
                break;
            case "AppBlockOn":
                ExecuteAppBlocking(true);
                break;
            case "AppBlockOff":
                ExecuteAppBlocking(false);
                break;
            case "WebcamCapture":
                ExecuteWebcamCapture(action);
                break;
            case "Notify":
                ExecuteNotify(action.Text);
                break;
            case "OpenUrl":
                ExecuteOpenUrl(action.Text);
                break;
            case "RunProcess":
                ExecuteRunProcess(action.Text);
                break;
            case "PlaySound":
                ExecutePlaySound();
                break;
            case "ScreenShot":
                ExecuteScreenShot(action);
                break;
            case "BasicProtectionOn":
                ExecuteBasicProtection(true);
                break;
            case "BasicProtectionOff":
                ExecuteBasicProtection(false);
                break;
        }
    }

    private void ExecuteNotify(string? text)
    {
        NotificationService.Instance.ShowInfo(text ?? string.Empty);
    }

    private void ExecuteBasicProtection(bool enable)
    {
        SettingsService.UpdateBlockage(s => s.IsBasicProtectionEnabled = enable);
    }

    private void ExecuteOpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void ExecuteRunProcess(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { Process.Start(path); } catch { }
    }

    private Window? GetMainWindow()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        return desktop?.MainWindow;
    }

    private async Task<string?> PickFileAsync(string title)
    {
        var window = GetMainWindow();
        var provider = window?.StorageProvider;
        if (provider == null) return null;
        var options = new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = title
        };
        var result = await provider.OpenFilePickerAsync(options);
        var file = result?.FirstOrDefault();
        return file?.Path?.LocalPath;
    }

    [RelayCommand]
    private async Task PickRunProcessExe(AutomationAction? action)
    {
        if (action == null) return;
        try
        {
            var path = await PickFileAsync("选择程序");
            if (!string.IsNullOrWhiteSpace(path))
            {
                action.Text = path;
                SaveSettings();
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task PickTriggerExe(AutomationTrigger? trig)
    {
        if (trig == null) return;
        try
        {
            var path = await PickFileAsync("选择进程可执行文件");
            if (!string.IsNullOrWhiteSpace(path))
            {
                trig.FilePath = path;
                SaveSettings();
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task PickConditionExe(AutomationCondition? cond)
    {
        if (cond == null) return;
        try
        {
            var path = await PickFileAsync("选择进程可执行文件");
            if (!string.IsNullOrWhiteSpace(path))
            {
                cond.FilePath = path;
                SaveSettings();
            }
        }
        catch { }
    }

    private void ExecutePlaySound()
    {
        NotificationService.Instance.ShowInfo("提示");
    }

    private void ExecuteScreenShot(AutomationAction? action = null)
    {
        var delay = Math.Clamp(action?.DelaySeconds ?? 0, 0, 60);
        Action doShot = () =>
        {
            try
            {
                LogService.Instance.Log("自动化", "截屏", "屏幕", "开始截屏...");
                ScreenshotService.Instance.CaptureOnce();
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("自动化", "截屏失败", "错误", ex.Message);
            }
        };
        if (delay > 0)
        {
            LogService.Observe(Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(delay)); doShot(); }), "Automation.ScreenShot");
        }
        else
        {
            LogService.Observe(Task.Run(() => doShot()), "Automation.ScreenShot");
        }
    }

    [RelayCommand]
    private void AddWorkflow()
    {
        var wf = new AutomationWorkflow
        {
            Name = $"未命名自动化 {Workflows.Count + 1}",
            Scheme = CurrentAutomationScheme
        };
        Workflows.Add(wf);
        UpdateVisibleWorkflows();
        SelectedWorkflow = wf;
        SaveSettings();
    }

    [RelayCommand(CanExecute = nameof(CanOperateWorkflow))]
    private void DeleteWorkflow()
    {
        var wf = SelectedWorkflow;
        if (wf == null) return;
        Workflows.Remove(wf);
        UpdateVisibleWorkflows();
        SelectedWorkflow = VisibleWorkflows.FirstOrDefault();
        SaveSettings();
    }

    [RelayCommand(CanExecute = nameof(CanOperateWorkflow))]
    private void DuplicateWorkflow()
    {
        var wf = SelectedWorkflow;
        if (wf == null) return;
        var copy = new AutomationWorkflow
        {
            Name = wf.Name + " (副本)",
            IsEnabled = wf.IsEnabled,
            RecoveryEnabled = wf.RecoveryEnabled,
            Scheme = wf.Scheme,
            Triggers = new System.Collections.ObjectModel.ObservableCollection<AutomationTrigger>(
                wf.Triggers.Select(t => new AutomationTrigger { Type = t.Type, Time = t.Time, IntervalMinutes = t.IntervalMinutes, ProcessName = t.ProcessName, FilePath = t.FilePath })),
            Conditions = new System.Collections.ObjectModel.ObservableCollection<AutomationCondition>(
                wf.Conditions.Select(c => new AutomationCondition { Type = c.Type, Bool = c.Bool, Days = c.Days, Start = c.Start, End = c.End, ProcessName = c.ProcessName, FilePath = c.FilePath })),
            Actions = new System.Collections.ObjectModel.ObservableCollection<AutomationAction>(
                wf.Actions.Select(a => new AutomationAction { Type = a.Type, Text = a.Text, DelaySeconds = a.DelaySeconds })),
            RecoveryActions = new System.Collections.ObjectModel.ObservableCollection<AutomationAction>(
                wf.RecoveryActions.Select(a => new AutomationAction { Type = a.Type, Text = a.Text, DelaySeconds = a.DelaySeconds }))
        };
        Workflows.Add(copy);
        UpdateVisibleWorkflows();
        SelectedWorkflow = copy;
        SaveSettings();
    }

    private bool CanOperateWorkflow() => SelectedWorkflow != null;

    [RelayCommand]
    private void AddTrigger(string type)
    {
        if (SelectedWorkflow == null) return;
        var trig = new AutomationTrigger { Type = type };
        if (type == "DailyTime") trig.Time = new TimeSpan(9, 0, 0);
        if (type == "Interval") trig.IntervalMinutes = 60;
        if (type == "ProcessRunning" || type == "ProcessNotRunning") trig.ProcessName = "classisland";
        if (type == "ProcessRunning" && !trig.CheckIntervalSeconds.HasValue) trig.CheckIntervalSeconds = 5;
        if (type == "FileExists") trig.FilePath = "C:\\temp\\example.txt";
        SelectedWorkflow.Triggers.Add(trig);
        SaveSettings();
    }

    [RelayCommand]
    private void RemoveTrigger(AutomationTrigger? trigger)
    {
        if (SelectedWorkflow == null || trigger == null) return;
        SelectedWorkflow.Triggers.Remove(trigger);
        SaveSettings();
    }

    [RelayCommand]
    private void AddCondition(string type)
    {
        if (SelectedWorkflow == null) return;
        var cond = new AutomationCondition { Type = type };
        if (type == "IsLocked") cond.Bool = true;
        if (type == "TimeRange") { cond.Start = new TimeSpan(8, 0, 0); cond.End = new TimeSpan(18, 0, 0); }
        if (type == "DayOfWeek") cond.Days = new[] { System.DayOfWeek.Monday.ToString(), System.DayOfWeek.Tuesday.ToString(), System.DayOfWeek.Wednesday.ToString(), System.DayOfWeek.Thursday.ToString(), System.DayOfWeek.Friday.ToString() };
        if (type == "AppBlockingEnabled" || type == "NetworkLockEnabled") cond.Bool = true;
        if (type == "ProcessRunning") cond.ProcessName = "classisland";
        if (type == "FileExists") cond.FilePath = "C:\\temp\\example.txt";
        SelectedWorkflow.Conditions.Add(cond);
        SaveSettings();
    }

    [RelayCommand]
    private void RemoveCondition(AutomationCondition? cond)
    {
        if (SelectedWorkflow == null || cond == null) return;
        SelectedWorkflow.Conditions.Remove(cond);
        SaveSettings();
    }

    [RelayCommand]
    private void AddAction(string type)
    {
        if (SelectedWorkflow == null) return;
        var act = new AutomationAction { Type = type };
        SelectedWorkflow.Actions.Add(act);
        SaveSettings();
    }

    [RelayCommand]
    private void RemoveAction(AutomationAction? act)
    {
        if (SelectedWorkflow == null || act == null) return;
        SelectedWorkflow.Actions.Remove(act);
        SaveSettings();
    }

    [RelayCommand]
    private void AddRecoveryAction(string type)
    {
        if (SelectedWorkflow == null) return;
        var act = new AutomationAction { Type = type };
        SelectedWorkflow.RecoveryActions.Add(act);
        SaveSettings();
    }

    [RelayCommand]
    private void RemoveRecoveryAction(AutomationAction? act)
    {
        if (SelectedWorkflow == null || act == null) return;
        SelectedWorkflow.RecoveryActions.Remove(act);
        SaveSettings();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _checkTimer?.Stop();
            _checkTimer?.Dispose();
            try { SettingsService.GeneralChanged -= OnGeneralChanged; } catch { }
        }
        base.Dispose(disposing);
    }
}
