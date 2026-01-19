using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public enum TimePointType
{
    Class,      // 上课
    Break,      // 课间
    Divider,    // 分割线
    Action      // 行动
}

public partial class TimePoint : ObservableObject
{
    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("id")]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("label")]
    private string _label = string.Empty;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("type")]
    private TimePointType _type;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("startTime")]
    private TimeSpan _startTime;

    public int StartHour
    {
        get => StartTime.Hours;
        set
        {
            StartTime = new TimeSpan(value, StartTime.Minutes, 0);
            OnPropertyChanged(nameof(StartHour));
        }
    }

    public int StartMinute
    {
        get => StartTime.Minutes;
        set
        {
            StartTime = new TimeSpan(StartTime.Hours, value, 0);
            OnPropertyChanged(nameof(StartMinute));
        }
    }

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("endTime")]
    private TimeSpan _endTime;

    public int EndHour
    {
        get => EndTime.Hours;
        set
        {
            EndTime = new TimeSpan(value, EndTime.Minutes, 0);
            OnPropertyChanged(nameof(EndHour));
        }
    }

    public int EndMinute
    {
        get => EndTime.Minutes;
        set
        {
            EndTime = new TimeSpan(EndTime.Hours, value, 0);
            OnPropertyChanged(nameof(EndMinute));
        }
    }

    public int Duration
    {
        get => (int)(EndTime - StartTime).TotalMinutes;
        set
        {
            EndTime = StartTime.Add(TimeSpan.FromMinutes(value));
            OnPropertyChanged(nameof(Duration));
        }
    }

    partial void OnStartTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(StartHour));
        OnPropertyChanged(nameof(StartMinute));
    }

    partial void OnEndTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(EndHour));
        OnPropertyChanged(nameof(EndMinute));
    }

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("description")]
    private string _description = string.Empty;

    public string TypeDisplayName => Type switch
    {
        TimePointType.Class => "上课",
        TimePointType.Break => "课间",
        TimePointType.Divider => "分割线",
        TimePointType.Action => "行动",
        _ => "未知"
    };
}

public partial class SchedulePlan : ObservableObject
{
    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("id")]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("name")]
    private string _name = "新时间表";

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("timePoints")]
    private ObservableCollection<TimePoint> _timePoints = new();

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("defaultClassDuration")]
    private int _defaultClassDuration = 45;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("defaultBreakDuration")]
    private int _defaultBreakDuration = 10;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("isActive")]
    private bool _isActive;
}
