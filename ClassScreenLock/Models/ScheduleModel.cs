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
    private string _id = Guid.NewGuid().ToString();
    private string _label = string.Empty;
    private TimePointType _type;
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

    private void OnStartTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(StartHour));
        OnPropertyChanged(nameof(StartMinute));
    }

    private void OnEndTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(EndHour));
        OnPropertyChanged(nameof(EndMinute));
    }

    private string _description = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("label")]
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public TimePointType Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("startTime")]
    public TimeSpan StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnStartTimeChanged(value);
            }
        }
    }

    [System.Text.Json.Serialization.JsonPropertyName("endTime")]
    public TimeSpan EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value))
            {
                OnEndTimeChanged(value);
            }
        }
    }

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

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
    private string _id = Guid.NewGuid().ToString();
    private string _name = "新时间表";
    private ObservableCollection<TimePoint> _timePoints = new();
    private int _defaultClassDuration = 45;
    private int _defaultBreakDuration = 10;
    private bool _isActive;
    private ObservableCollection<Subject>? _subjects = new();
    private int? _enableDay;
    private string? _weeks;

    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("timePoints")]
    public ObservableCollection<TimePoint> TimePoints
    {
        get => _timePoints;
        set => SetProperty(ref _timePoints, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("defaultClassDuration")]
    public int DefaultClassDuration
    {
        get => _defaultClassDuration;
        set => SetProperty(ref _defaultClassDuration, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("defaultBreakDuration")]
    public int DefaultBreakDuration
    {
        get => _defaultBreakDuration;
        set => SetProperty(ref _defaultBreakDuration, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("isActive")]
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("subjects")]
    public ObservableCollection<Subject>? Subjects
    {
        get => _subjects;
        set => SetProperty(ref _subjects, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("enableDay")]
    public int? EnableDay
    {
        get => _enableDay;
        set => SetProperty(ref _enableDay, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("weeks")]
    public string? Weeks
    {
        get => _weeks;
        set => SetProperty(ref _weeks, value);
    }
}

public partial class Subject : ObservableObject
{
    private string _name = string.Empty;
    private string _simplifiedName = string.Empty;
    private string _teacher = string.Empty;
    private string _room = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("simplifiedName")]
    public string SimplifiedName
    {
        get => _simplifiedName;
        set => SetProperty(ref _simplifiedName, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("teacher")]
    public string Teacher
    {
        get => _teacher;
        set => SetProperty(ref _teacher, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("room")]
    public string Room
    {
        get => _room;
        set => SetProperty(ref _room, value);
    }
}
