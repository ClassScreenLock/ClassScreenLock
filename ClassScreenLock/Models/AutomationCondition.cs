using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public class AutomationCondition : ObservableObject
{
    private string _type = "TimeRange";
    private bool? _bool;
    private string[]? _days;
    private TimeSpan? _start;
    private TimeSpan? _end;
    private string? _processName;
    private string? _filePath;
    private int? _checkIntervalSeconds;
    private DateTime? _lastCheckedAt;

    [JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    [JsonPropertyName("bool")]
    public bool? Bool
    {
        get => _bool;
        set => SetProperty(ref _bool, value);
    }

    [JsonPropertyName("days")]
    public string[]? Days
    {
        get => _days;
        set => SetProperty(ref _days, value);
    }

    [JsonPropertyName("start")]
    public TimeSpan? Start
    {
        get => _start;
        set
        {
            if (SetProperty(ref _start, value))
            {
                OnPropertyChanged(nameof(StartHour));
                OnPropertyChanged(nameof(StartMinute));
            }
        }
    }

    [JsonPropertyName("end")]
    public TimeSpan? End
    {
        get => _end;
        set
        {
            if (SetProperty(ref _end, value))
            {
                OnPropertyChanged(nameof(EndHour));
                OnPropertyChanged(nameof(EndMinute));
            }
        }
    }

    [JsonIgnore]
    public int StartHour
    {
        get => Start?.Hours ?? 0;
        set { var c = Start ?? TimeSpan.Zero; Start = new TimeSpan(value, c.Minutes, 0); }
    }

    [JsonIgnore]
    public int StartMinute
    {
        get => Start?.Minutes ?? 0;
        set { var c = Start ?? TimeSpan.Zero; Start = new TimeSpan(c.Hours, value, 0); }
    }

    [JsonIgnore]
    public int EndHour
    {
        get => End?.Hours ?? 0;
        set { var c = End ?? TimeSpan.Zero; End = new TimeSpan(value, c.Minutes, 0); }
    }

    [JsonIgnore]
    public int EndMinute
    {
        get => End?.Minutes ?? 0;
        set { var c = End ?? TimeSpan.Zero; End = new TimeSpan(c.Hours, value, 0); }
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
}
