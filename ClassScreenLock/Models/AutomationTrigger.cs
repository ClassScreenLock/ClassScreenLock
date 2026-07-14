using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public class AutomationTrigger : ObservableObject
{
    private string _type = "DailyTime";
    private TimeSpan? _time;
    private int? _intervalMinutes;
    private string? _processName;
    private string? _filePath;
    private int? _checkIntervalSeconds;
    private DateTime? _lastCheckedAt;
    private DateTime? _triggerLastTriggeredAt;
    private bool? _lastNetworkStatus;
    private bool? _lastFileExistsStatus;

    [JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    [JsonPropertyName("time")]
    public TimeSpan? Time
    {
        get => _time;
        set
        {
            if (SetProperty(ref _time, value))
            {
                OnPropertyChanged(nameof(Hour));
                OnPropertyChanged(nameof(Minute));
            }
        }
    }

    [JsonIgnore]
    public int Hour
    {
        get => Time?.Hours ?? 0;
        set
        {
            var current = Time ?? TimeSpan.Zero;
            Time = new TimeSpan(value, current.Minutes, 0);
        }
    }

    [JsonIgnore]
    public int Minute
    {
        get => Time?.Minutes ?? 0;
        set
        {
            var current = Time ?? TimeSpan.Zero;
            Time = new TimeSpan(current.Hours, value, 0);
        }
    }

    [JsonPropertyName("intervalMinutes")]
    public int? IntervalMinutes
    {
        get => _intervalMinutes;
        set => SetProperty(ref _intervalMinutes, value);
    }

    [JsonPropertyName("processName")]
    public string? ProcessName
    {
        get => _processName;
        set => SetProperty(ref _processName, value);
    }

    [JsonPropertyName("filePath")]
    public string? FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    [JsonPropertyName("checkIntervalSeconds")]
    public int? CheckIntervalSeconds
    {
        get => _checkIntervalSeconds;
        set => SetProperty(ref _checkIntervalSeconds, value);
    }

    [JsonIgnore]
    public DateTime? LastCheckedAt
    {
        get => _lastCheckedAt;
        set => SetProperty(ref _lastCheckedAt, value);
    }

    [JsonIgnore]
    public DateTime? TriggerLastTriggeredAt
    {
        get => _triggerLastTriggeredAt;
        set => SetProperty(ref _triggerLastTriggeredAt, value);
    }

    [JsonIgnore]
    public bool? LastNetworkStatus
    {
        get => _lastNetworkStatus;
        set => SetProperty(ref _lastNetworkStatus, value);
    }

    [JsonIgnore]
    public bool? LastFileExistsStatus
    {
        get => _lastFileExistsStatus;
        set => SetProperty(ref _lastFileExistsStatus, value);
    }
}
