using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public partial class WeeklyScheduleFile : ObservableObject
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "周课表";
    private int _weekNumber = 1;
    private int _defaultClassDuration = 45;
    private int _defaultBreakDuration = 10;
    private ObservableCollection<Subject> _subjects = new();
    private ObservableCollection<WeeklyDaySchedule> _days = new();

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

    [JsonPropertyName("weekNumber")]
    public int WeekNumber
    {
        get => _weekNumber;
        set => SetProperty(ref _weekNumber, value);
    }

    [JsonPropertyName("defaultClassDuration")]
    public int DefaultClassDuration
    {
        get => _defaultClassDuration;
        set => SetProperty(ref _defaultClassDuration, value);
    }

    [JsonPropertyName("defaultBreakDuration")]
    public int DefaultBreakDuration
    {
        get => _defaultBreakDuration;
        set => SetProperty(ref _defaultBreakDuration, value);
    }

    [JsonPropertyName("subjects")]
    public ObservableCollection<Subject> Subjects
    {
        get => _subjects;
        set => SetProperty(ref _subjects, value);
    }

    [JsonPropertyName("days")]
    public ObservableCollection<WeeklyDaySchedule> Days
    {
        get => _days;
        set => SetProperty(ref _days, value);
    }
}

public partial class WeeklyDaySchedule : ObservableObject
{
    private int _enableDay;
    private ObservableCollection<WeeklyClass> _classes = new();

    [JsonPropertyName("enableDay")]
    public int EnableDay
    {
        get => _enableDay;
        set => SetProperty(ref _enableDay, value);
    }

    [JsonPropertyName("classes")]
    public ObservableCollection<WeeklyClass> Classes
    {
        get => _classes;
        set => SetProperty(ref _classes, value);
    }
}

public partial class WeeklyClass : ObservableObject
{
    private TimePointType? _type;
    private string? _subject;
    private string? _label;
    private string? _description;
    private string _startTime = "08:00";
    private string _endTime = "08:45";

    [JsonPropertyName("type")]
    public TimePointType? Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    [JsonPropertyName("subject")]
    public string? Subject
    {
        get => _subject;
        set => SetProperty(ref _subject, value);
    }

    [JsonPropertyName("label")]
    public string? Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    [JsonPropertyName("description")]
    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    [JsonPropertyName("startTime")]
    public string StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    [JsonPropertyName("endTime")]
    public string EndTime
    {
        get => _endTime;
        set => SetProperty(ref _endTime, value);
    }
}
