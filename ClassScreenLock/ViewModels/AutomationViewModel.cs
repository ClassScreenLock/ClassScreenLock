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
            
            // 1. 加载方案列表
            var schemes = general.AutomationSchemes;
            if (schemes == null || schemes.Count == 0)
            {
                schemes = new List<string> { "Default" };
                SettingsService.UpdateGeneral(g => g.AutomationSchemes = schemes);
            }
            
            // 保持集合引用不变，只更新内容，避免绑定丢失
            AvailableSchemes.Clear();
            foreach (var s in schemes) AvailableSchemes.Add(s);
            
            // 2. 加载当前方案
            var scheme = general.CurrentAutomationScheme;
            if (string.IsNullOrEmpty(scheme) || !AvailableSchemes.Contains(scheme))
            {
                scheme = AvailableSchemes.FirstOrDefault() ?? "Default";
                SettingsService.UpdateGeneral(g => g.CurrentAutomationScheme = scheme);
            }
            
            CurrentAutomationScheme = scheme;
            _oldScheme = scheme;

            // 3. 加载工作流
            IsAutomationEnabled = settings.IsAutomationEnabled;
            var loadedWorkflows = settings.Workflows ?? new List<AutomationWorkflow>();
            
            Workflows.Clear();
            foreach (var wf in loadedWorkflows)
            {
                foreach (var t in wf.Triggers)
                {
                    if (string.Equals(t.Type, "ProcessRunning", StringComparison.OrdinalIgnoreCase) && !t.CheckIntervalSeconds.HasValue)
                    {
                        t.CheckIntervalSeconds = 5;
                    }
                }
                Workflows.Add(wf);
            }

            if (Workflows.Count == 0)
            {
                var wf = new AutomationWorkflow { Name = "新工作流", Scheme = scheme };
                Workflows.Add(wf);
            }

            UpdateVisibleWorkflows();
            SelectedWorkflow = Workflows.FirstOrDefault();
            
            // 4. 加载其他设置
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
        finally
        {
            _suppressUpdate = wasSuppressing;
            _isInternalLoading = false;
        }
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
        if (_attachedWorkflow != null)
        {
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

        _attachedWorkflow = SelectedWorkflow;
        if (_attachedWorkflow != null)
        {
            _attachedWorkflow.Triggers.CollectionChanged += OnTriggersCollectionChanged;
            foreach (var t in _attachedWorkflow.Triggers) t.PropertyChanged += OnTriggerPropertyChanged;
            _attachedWorkflow.Conditions.CollectionChanged += OnConditionsCollectionChanged;
            foreach (var c in _attachedWorkflow.Conditions) c.PropertyChanged += OnConditionPropertyChanged;
            _attachedWorkflow.Actions.CollectionChanged += OnActionsCollectionChanged;
            foreach (var a in _attachedWorkflow.Actions) a.PropertyChanged += OnActionPropertyChanged;
            _attachedWorkflow.RecoveryActions.CollectionChanged += OnActionsCollectionChanged;
            foreach (var a in _attachedWorkflow.RecoveryActions) a.PropertyChanged += OnActionPropertyChanged;
        }
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

        // sequential char match count
        int seq = 0;
        int qi = 0;
        for (int i = 0; i < n.Length && qi < q.Length; i++)
        {
            if (n[i] == q[qi]) { seq++; qi++; }
        }
        score += seq * 12; // weight

        // common substring length (simple window)
        int longest = 0;
        for (int i = 0; i < n.Length; i++)
        {
            int l = 0;
            for (int j = 0; i + j < n.Length && j < q.Length; j++)
            {
                if (n[i + j] == q[j]) l++; else break;
            }
            if (l > longest) longest = l;
        }
        score += longest * 8;

        return score;
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
                bool triggerMatched = false;
                foreach (var t in wf.Triggers)
                {
                    if (t.Type == "DailyTime" && t.Time.HasValue)
                    {
                        if (Math.Abs((currentTime - t.Time.Value).TotalSeconds) < 30) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "Interval" && t.IntervalMinutes.HasValue)
                    {
                        if (wf.LastTriggeredAt == null || (now - wf.LastTriggeredAt.Value).TotalMinutes >= t.IntervalMinutes.Value) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "OnStartup")
                    {
                        if (!_startupProcessed) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "ProcessRunning" && (!string.IsNullOrWhiteSpace(t.ProcessName) || !string.IsNullOrWhiteSpace(t.FilePath)))
                    {
                        bool exists = false;
                        var name = string.IsNullOrWhiteSpace(t.ProcessName) ? null : Path.GetFileNameWithoutExtension(t.ProcessName).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            try { exists = Process.GetProcesses().Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)); } catch { }
                        }
                        if (!exists && !string.IsNullOrWhiteSpace(t.FilePath))
                        {
                            var target = t.FilePath.Trim();
                            try { exists = Process.GetProcesses().Any(p => { try { return string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase); } catch { return false; } }); } catch { }
                        }
                        if (exists) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "ProcessNotRunning" && (!string.IsNullOrWhiteSpace(t.ProcessName) || !string.IsNullOrWhiteSpace(t.FilePath)))
                    {
                        bool exists = false;
                        var name = string.IsNullOrWhiteSpace(t.ProcessName) ? null : Path.GetFileNameWithoutExtension(t.ProcessName).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            try { exists = Process.GetProcesses().Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)); } catch { }
                        }
                        if (!exists && !string.IsNullOrWhiteSpace(t.FilePath))
                        {
                            var target = t.FilePath.Trim();
                            try { exists = Process.GetProcesses().Any(p => { try { return string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase); } catch { return false; } }); } catch { }
                        }
                        if (!exists) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "NetworkAvailable")
                    {
                        if (NetworkInterface.GetIsNetworkAvailable()) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "NetworkUnavailable")
                    {
                        if (!NetworkInterface.GetIsNetworkAvailable()) { triggerMatched = true; break; }
                    }
                    else if (t.Type == "FileExists" && !string.IsNullOrWhiteSpace(t.FilePath))
                    {
                        if (File.Exists(t.FilePath)) { triggerMatched = true; break; }
                    }
                }

                bool conditionsOk = !wf.ConditionsEnabled ? true : true;
                if (wf.ConditionsEnabled)
                {
                    foreach (var c in wf.Conditions)
                    {
                    if (c.Type == "IsLocked" && c.Bool.HasValue)
                    {
                        bool locked = LockScreenService.Instance.IsLocked || LockScreenService.Instance.IsProtectionOnlyActive;
                        if (locked != c.Bool.Value) { conditionsOk = false; break; }
                    }
                    else if (c.Type == "DayOfWeek" && c.Days != null && c.Days.Length > 0)
                    {
                        var d = now.DayOfWeek.ToString();
                        if (!c.Days.Contains(d)) { conditionsOk = false; break; }
                    }
                    else if (c.Type == "TimeRange" && c.Start.HasValue && c.End.HasValue)
                    {
                        if (!(currentTime >= c.Start.Value && currentTime <= c.End.Value)) { conditionsOk = false; break; }
                    }
                        else if (c.Type == "AppBlockingEnabled" && c.Bool.HasValue)
                        {
                            var enabled = SettingsService.Blockage.IsAppBlockingEnabled;
                            if (enabled != c.Bool.Value) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "NetworkLockEnabled" && c.Bool.HasValue)
                        {
                            var enabled = SettingsService.Blockage.IsNetworkLockEnabled;
                            if (enabled != c.Bool.Value) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "ProcessRunning" && (!string.IsNullOrWhiteSpace(c.ProcessName) || !string.IsNullOrWhiteSpace(c.FilePath)))
                        {
                            bool exists = false;
                            var name = string.IsNullOrWhiteSpace(c.ProcessName) ? null : Path.GetFileNameWithoutExtension(c.ProcessName).Trim();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                try { exists = Process.GetProcesses().Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)); } catch { }
                            }
                            if (!exists && !string.IsNullOrWhiteSpace(c.FilePath))
                            {
                                var target = c.FilePath.Trim();
                                try { exists = Process.GetProcesses().Any(p => { try { return string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase); } catch { return false; } }); } catch { }
                            }
                            if (!exists) { conditionsOk = false; break; }
                        }
                        else if (c.Type == "FileExists" && !string.IsNullOrWhiteSpace(c.FilePath))
                        {
                            if (!File.Exists(c.FilePath)) { conditionsOk = false; break; }
                        }
                    }
                }

                var satisfiedNow = triggerMatched && conditionsOk;
                if (satisfiedNow && !wf.PreviouslySatisfied)
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
                else if (!satisfiedNow && wf.RecoveryEnabled && wf.PreviouslySatisfied)
                {
                    LogService.Instance.Log("自动化", "执行恢复行动", "系统", $"工作流 [{wf.Name}] 已触发恢复");
                    foreach (var a in wf.RecoveryActions)
                    {
                        ExecuteWorkflowAction(a);
                    }
                    wf.PreviouslySatisfied = false;
                }
            }
            _startupProcessed = true;
        }

        // Check Auto Shutdown
        if (EnableAutoShutdown && AutoShutdownTime.HasValue)
        {
            var shutdownTime = AutoShutdownTime.Value;
            // Check if within 1 minute of scheduled time
            if (Math.Abs((currentTime - shutdownTime).TotalSeconds) < 30)
            {
                ExecuteShutdown();
            }
        }

        // Check Auto Restart
        if (EnableAutoRestart && AutoRestartTime.HasValue)
        {
            var restartTime = AutoRestartTime.Value;
            if (Math.Abs((currentTime - restartTime).TotalSeconds) < 30)
            {
                ExecuteRestart();
            }
        }

        // Check Auto Lock
        if (EnableAutoLock && AutoLockTime.HasValue)
        {
            var lockTime = AutoLockTime.Value;
            if (Math.Abs((currentTime - lockTime).TotalSeconds) < 30)
            {
                ExecuteLock();
            }
        }

        // Check Auto Network Lock ON
        if (EnableAutoNetworkLockOn && AutoNetworkLockOnTime.HasValue)
        {
            var t = AutoNetworkLockOnTime.Value;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteNetworkLock(true);
            }
        }

        // Check Auto Network Lock OFF
        if (EnableAutoNetworkLockOff && AutoNetworkLockOffTime.HasValue)
        {
            var t = AutoNetworkLockOffTime.Value;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteNetworkLock(false);
            }
        }

        // Check Auto App Block ON
        if (EnableAutoAppBlockOn && AutoAppBlockOnTime.HasValue)
        {
            var t = AutoAppBlockOnTime.Value;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteAppBlocking(true);
            }
        }

        // Check Auto App Block OFF
        if (EnableAutoAppBlockOff && AutoAppBlockOffTime.HasValue)
        {
            var t = AutoAppBlockOffTime.Value;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteAppBlocking(false);
            }
        }

        // Check Auto Webcam Capture
        if (EnableAutoWebcamCapture && AutoWebcamCaptureTime.HasValue)
        {
            var t = AutoWebcamCaptureTime.Value;
            if (Math.Abs((currentTime - t).TotalSeconds) < 30)
            {
                ExecuteWebcamCapture();
            }
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
        var t = action.Type;
        if (t == "Shutdown") ExecuteShutdown();
        else if (t == "Restart") ExecuteRestart();
        else if (t == "LockFull") ExecuteLock();
        else if (t == "NetworkLockOn") ExecuteNetworkLock(true);
        else if (t == "NetworkLockOff") ExecuteNetworkLock(false);
        else if (t == "AppBlockOn") ExecuteAppBlocking(true);
        else if (t == "AppBlockOff") ExecuteAppBlocking(false);
        else if (t == "WebcamCapture") ExecuteWebcamCapture(action);
        else if (t == "Notify") NotificationService.Instance.ShowInfo(action.Text ?? string.Empty);
        else if (t == "OpenUrl") ExecuteOpenUrl(action.Text);
        else if (t == "RunProcess") ExecuteRunProcess(action.Text);
        else if (t == "PlaySound") ExecutePlaySound();
        else if (t == "ScreenShot") ExecuteScreenShot(action);
        else if (t == "BasicProtectionOn") SettingsService.UpdateBlockage(s => s.IsBasicProtectionEnabled = true);
        else if (t == "BasicProtectionOff") SettingsService.UpdateBlockage(s => s.IsBasicProtectionEnabled = false);
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
}
