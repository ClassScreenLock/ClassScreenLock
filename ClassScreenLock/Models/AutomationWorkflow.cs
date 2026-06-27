using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public class AutomationWorkflow : ObservableObject
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "新自动化";
    private bool _isEnabled = true;
    private bool _recoveryEnabled = true;
    private string _scheme = "Default";
    private ObservableCollection<AutomationTrigger> _triggers = new();
    private ObservableCollection<AutomationCondition> _conditions = new();
    private ObservableCollection<AutomationAction> _actions = new();
    private ObservableCollection<AutomationAction> _recoveryActions = new();
    private DateTime? _lastTriggeredAt;
    private bool _previouslySatisfied;
    private bool _conditionsEnabled = true;
    private int _triggerCount;

    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    [JsonPropertyName("recoveryEnabled")]
    public bool RecoveryEnabled
    {
        get => _recoveryEnabled;
        set => SetProperty(ref _recoveryEnabled, value);
    }

    [JsonPropertyName("scheme")]
    public string Scheme
    {
        get => _scheme;
        set => SetProperty(ref _scheme, value);
    }

    [JsonPropertyName("triggers")]
    public ObservableCollection<AutomationTrigger> Triggers
    {
        get => _triggers;
        set => SetProperty(ref _triggers, value);
    }

    [JsonPropertyName("conditions")]
    public ObservableCollection<AutomationCondition> Conditions
    {
        get => _conditions;
        set => SetProperty(ref _conditions, value);
    }

    [JsonPropertyName("actions")]
    public ObservableCollection<AutomationAction> Actions
    {
        get => _actions;
        set => SetProperty(ref _actions, value);
    }

    [JsonPropertyName("recoveryActions")]
    public ObservableCollection<AutomationAction> RecoveryActions
    {
        get => _recoveryActions;
        set => SetProperty(ref _recoveryActions, value);
    }

    [JsonPropertyName("lastTriggeredAt")]
    public DateTime? LastTriggeredAt
    {
        get => _lastTriggeredAt;
        set => SetProperty(ref _lastTriggeredAt, value);
    }

    [JsonPropertyName("previouslySatisfied")]
    public bool PreviouslySatisfied
    {
        get => _previouslySatisfied;
        set => SetProperty(ref _previouslySatisfied, value);
    }

    [JsonPropertyName("conditionsEnabled")]
    public bool ConditionsEnabled
    {
        get => _conditionsEnabled;
        set => SetProperty(ref _conditionsEnabled, value);
    }

    [JsonPropertyName("triggerCount")]
    public int TriggerCount
    {
        get => _triggerCount;
        set => SetProperty(ref _triggerCount, value);
    }
}
