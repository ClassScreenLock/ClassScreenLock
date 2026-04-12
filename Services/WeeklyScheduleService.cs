using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ClassScreenLock.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClassScreenLock.Services;

public class WeeklyScheduleService
{
    private static readonly string WeeklyDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Data",
        "WeeklySchedules");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static WeeklyScheduleService? _instance;
    public static WeeklyScheduleService Instance => _instance ??= new WeeklyScheduleService();

    private WeeklyScheduleService()
    {
        EnsureDirectoryExists();
        EnsureDefaultWeeklyFiles();
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(WeeklyDirectory)) Directory.CreateDirectory(WeeklyDirectory);
    }

    private void EnsureDefaultWeeklyFiles()
    {
        var count = GetCycleCount();
        for (int w = 1; w <= count; w++)
        {
            var path = GetFilePath(w);
            if (!File.Exists(path))
            {
                var weekly = new WeeklyScheduleFile
                {
                    Name = $"第{w}周课表",
                    WeekNumber = w,
                };
                weekly.Days = new System.Collections.ObjectModel.ObservableCollection<WeeklyDaySchedule>();
                for (int d = 1; d <= 7; d++)
                {
                    weekly.Days.Add(new WeeklyDaySchedule { EnableDay = d });
                }
                SaveWeekly(weekly);
            }
        }
    }

    private static string GetFilePath(int weekNumber) => Path.Combine(WeeklyDirectory, $"Week{weekNumber}.json");

    private static WeeklyScheduleFile CreateDefaultWeekly(int weekNumber)
    {
        var weekly = new WeeklyScheduleFile
        {
            Name = $"第{weekNumber}周课表",
            WeekNumber = weekNumber,
        };

        weekly.Days = new System.Collections.ObjectModel.ObservableCollection<WeeklyDaySchedule>();
        for (int d = 1; d <= 7; d++)
        {
            weekly.Days.Add(new WeeklyDaySchedule { EnableDay = d });
        }

        return weekly;
    }

    public List<WeeklyScheduleFile> LoadAllWeekly()
    {
        var list = new List<WeeklyScheduleFile>();
        try
        {
            EnsureDirectoryExists();
            foreach (var file in Directory.GetFiles(WeeklyDirectory, "*.json"))
            {
                var json = File.ReadAllText(file);
                var weekly = JsonSerializer.Deserialize<WeeklyScheduleFile>(json, JsonOptions);
                if (weekly != null) list.Add(weekly);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载周课表失败: {ex.Message}");
        }
        var count = GetCycleCount();
        for (int w = 1; w <= count; w++)
        {
            if (list.All(x => x.WeekNumber != w))
            {
                var created = GetWeekly(w);
                if (created != null) list.Add(created);
            }
        }

        return list.Where(w => w.WeekNumber >= 1 && w.WeekNumber <= count).OrderBy(w => w.WeekNumber).ToList();
    }

    public WeeklyScheduleFile? GetWeekly(int weekNumber)
    {
        try
        {
            var count = GetCycleCount();
            if (weekNumber < 1 || weekNumber > count) return null;

            EnsureDirectoryExists();
            var path = GetFilePath(weekNumber);
            if (!File.Exists(path))
            {
                var created = CreateDefaultWeekly(weekNumber);
                SaveWeekly(created);
                return created;
            }
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WeeklyScheduleFile>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取周课表失败: {ex.Message}");
            return null;
        }
    }

    public void SaveWeekly(WeeklyScheduleFile weekly)
    {
        try
        {
            EnsureDirectoryExists();
            if (weekly.Days == null || weekly.Days.Count == 0)
            {
                weekly.Days = new System.Collections.ObjectModel.ObservableCollection<WeeklyDaySchedule>();
                for (int d = 1; d <= 7; d++) weekly.Days.Add(new WeeklyDaySchedule { EnableDay = d });
            }
            var path = GetFilePath(weekly.WeekNumber);
            var json = JsonSerializer.Serialize(weekly, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存周课表失败: {ex.Message}");
        }
    }

    public List<WeeklyScheduleFile> ImportCsesYaml(string sourcePath)
    {
        var results = new List<WeeklyScheduleFile>();
        try
        {
            var yamlText = File.ReadAllText(sourcePath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var root = deserializer.Deserialize<YamlRoot>(yamlText);
            if (root == null) return results;

            var desiredCount = DetermineCycleCountFromYaml(root);
            SettingsService.UpdateGeneral(s => s.WeeklyCycleCount = desiredCount);

            var weekTargets = new Dictionary<int, WeeklyScheduleFile>();
            var count = desiredCount;
            for (int w = 1; w <= count; w++)
            {
                var weekly = GetWeekly(w) ?? new WeeklyScheduleFile { WeekNumber = w, Name = $"第{w}周课表" };
                weekly.Subjects = new System.Collections.ObjectModel.ObservableCollection<Subject>();
                foreach (var subj in root.Subjects ?? new List<YamlSubject>())
                {
                    var subject = new Subject
                    {
                        Name = subj.Name ?? string.Empty,
                        SimplifiedName = subj.SimplifiedName ?? string.Empty,
                        Teacher = subj.Teacher ?? string.Empty,
                        Room = subj.Room ?? string.Empty
                    };

                    weekly.Subjects.Add(subject);
                }
                if (weekly.Days == null || weekly.Days.Count == 0)
                {
                    weekly.Days = new System.Collections.ObjectModel.ObservableCollection<WeeklyDaySchedule>();
                    for (int d = 1; d <= 7; d++) weekly.Days.Add(new WeeklyDaySchedule { EnableDay = d });
                }
                weekTargets[w] = weekly;
            }

            foreach (var s in root.Schedules ?? new List<YamlDaySchedule>())
            {
                var targets = ResolveWeekTargets(s.Weeks, count);
                var dayIndex = s.EnableDay == 0 ? 1 : s.EnableDay;
                foreach (var w in targets)
                {
                    var weekly = weekTargets[w];
                    var day = weekly.Days.FirstOrDefault(d => d.EnableDay == dayIndex) ?? new WeeklyDaySchedule { EnableDay = dayIndex };
                    if (!weekly.Days.Contains(day)) weekly.Days.Add(day);
                    day.Classes.Clear();

                    var subjectDict = weekly.Subjects
                        .Where(su => !string.IsNullOrWhiteSpace(su.Name))
                        .ToDictionary(su => su.Name, su => su, StringComparer.OrdinalIgnoreCase);

                    foreach (var c in s.Classes ?? new List<YamlClass>())
                    {
                        var isBreak = string.IsNullOrWhiteSpace(c.Subject);
                        var label = isBreak
                            ? "课间休息"
                            : (subjectDict.TryGetValue(c.Subject ?? string.Empty, out var info)
                                ? (string.IsNullOrWhiteSpace(info.SimplifiedName) ? (info.Name ?? c.Subject ?? "课程") : info.SimplifiedName)
                                : (c.Subject ?? "课程"));

                        var description = isBreak
                            ? string.Empty
                            : (subjectDict.TryGetValue(c.Subject ?? string.Empty, out var info2)
                                ? ResolveSubjectDescription(weekly, info2.Name)
                                : string.Empty);

                        day.Classes.Add(new WeeklyClass
                        {
                            Type = isBreak ? TimePointType.Break : TimePointType.Class,
                            Subject = c.Subject,
                            StartTime = c.StartTime ?? "08:00",
                            EndTime = c.EndTime ?? "08:45",
                            Label = label,
                            Description = description
                        });
                    }
                }
            }

            foreach (var kv in weekTargets)
            {
                SaveWeekly(kv.Value);
                results.Add(kv.Value);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入周课表失败: {ex.Message}");
            throw;
        }
        return results;
    }

    public List<WeeklyScheduleFile> ImportWeeklyJson(string sourcePath)
    {
        var results = new List<WeeklyScheduleFile>();
        try
        {
            var jsonText = File.ReadAllText(sourcePath);
            
            // 尝试解析为列表
            List<WeeklyScheduleFile>? list = null;
            try
            {
                list = JsonSerializer.Deserialize<List<WeeklyScheduleFile>>(jsonText, JsonOptions);
            }
            catch
            {
                // 忽略解析列表失败，尝试解析为单个对象
            }

            if (list != null && list.Any())
            {
                var count = list.Select(w => w.WeekNumber).Where(n => n >= 1 && n <= 6).DefaultIfEmpty(1).Max();
                var currentCount = GetCycleCount();
                if (count > currentCount)
                {
                    SettingsService.UpdateGeneral(s => s.WeeklyCycleCount = count);
                }
                foreach (var w in list)
                {
                    SaveWeekly(w);
                    results.Add(w);
                }
                return results;
            }

            // 尝试解析为单个对象
            var single = JsonSerializer.Deserialize<WeeklyScheduleFile>(jsonText, JsonOptions);
            if (single != null)
            {
                var currentCount = GetCycleCount();
                if (single.WeekNumber > currentCount)
                {
                    SettingsService.UpdateGeneral(s => s.WeeklyCycleCount = Math.Clamp(single.WeekNumber, 1, 6));
                }
                SaveWeekly(single);
                results.Add(single);
                return results;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入周课表JSON失败: {ex.Message}");
        }
        return results;
    }

    private static List<int> ResolveWeekTargets(string? rule)
    {
        var count = GetCycleCount();
        if (string.IsNullOrWhiteSpace(rule)) return Enumerable.Range(1, count).ToList();
        var text = rule.Trim();
        if (text.Equals("不限", StringComparison.OrdinalIgnoreCase) || text.Equals("any", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(1, count).ToList();
        if (text.Equals("单周", StringComparison.OrdinalIgnoreCase) || text.Equals("single", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(1, count).Where(i => i % 2 == 1).ToList();
        if (text.Equals("双周", StringComparison.OrdinalIgnoreCase) || text.Equals("double", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(1, count).Where(i => i % 2 == 0).ToList();
        var parts = text.Split(new[] { ',', '，', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>();
        foreach (var p in parts)
        {
            if (int.TryParse(p, out var n) && n >= 1 && n <= count) list.Add(n);
        }
        return list.Count == 0 ? Enumerable.Range(1, count).ToList() : list.Distinct().OrderBy(x => x).ToList();
    }

    private static List<int> ResolveWeekTargets(string? rule, int count)
    {
        if (string.IsNullOrWhiteSpace(rule)) return Enumerable.Range(1, count).ToList();
        var text = rule.Trim();
        if (text.Equals("不限", StringComparison.OrdinalIgnoreCase) || text.Equals("any", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(1, count).ToList();
        if (text.Equals("单周", StringComparison.OrdinalIgnoreCase) || text.Equals("single", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(1, count).Where(i => i % 2 == 1).ToList();
        if (text.Equals("双周", StringComparison.OrdinalIgnoreCase) || text.Equals("double", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(1, count).Where(i => i % 2 == 0).ToList();
        var parts = text.Split(new[] { ',', '，', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>();
        foreach (var p in parts)
        {
            if (int.TryParse(p, out var n) && n >= 1 && n <= count) list.Add(n);
        }
        return list.Count == 0 ? Enumerable.Range(1, count).ToList() : list.Distinct().OrderBy(x => x).ToList();
    }

    private class YamlRoot
    {
        public int Version { get; set; }
        public List<YamlSubject>? Subjects { get; set; }
        public List<YamlDaySchedule>? Schedules { get; set; }
    }

    private class YamlSubject
    {
        public string? Name { get; set; }
        public string? SimplifiedName { get; set; }
        public string? Teacher { get; set; }
        public string? Room { get; set; }
    }

    private class YamlDaySchedule
    {
        public string? Name { get; set; }
        public List<YamlClass>? Classes { get; set; }
        public int EnableDay { get; set; }
        public string? Weeks { get; set; }
    }

    private class YamlClass
    {
        public string? Subject { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
    }

    public SchedulePlan? BuildPlanFor(int weekNumber, int dayIndex)
    {
        var weekly = GetWeekly(weekNumber);
        if (weekly == null) return null;
        var day = weekly.Days.FirstOrDefault(d => d.EnableDay == dayIndex) ?? new WeeklyDaySchedule { EnableDay = dayIndex };

        var plan = new SchedulePlan
        {
            Name = $"第{weekNumber}周-第{dayIndex}日",
            DefaultClassDuration = weekly.DefaultClassDuration,
            DefaultBreakDuration = weekly.DefaultBreakDuration,
            Subjects = weekly.Subjects,
            EnableDay = dayIndex,
            Weeks = weekNumber.ToString()
        };

        var entries = day.Classes
            .Select(c =>
            {
                var start = ParseTime(c.StartTime);
                var end = ParseTime(c.EndTime);
                if (end < start) end = start;
                var type = c.Type ?? (string.IsNullOrWhiteSpace(c.Subject) ? TimePointType.Break : TimePointType.Class);
                if (type is TimePointType.Divider or TimePointType.Action)
                {
                    end = start;
                }
                var label = !string.IsNullOrWhiteSpace(c.Label)
                    ? c.Label!
                    : type switch
                    {
                        TimePointType.Break => "课间休息",
                        TimePointType.Divider => "分割线",
                        TimePointType.Action => "行动点",
                        _ => ResolveSubjectLabel(weekly, c.Subject ?? string.Empty)
                    };
                var description = !string.IsNullOrWhiteSpace(c.Description)
                    ? c.Description!
                    : type == TimePointType.Class ? ResolveSubjectDescription(weekly, c.Subject ?? string.Empty) : string.Empty;
                return new
                {
                    Type = type,
                    Start = start,
                    End = end,
                    Label = label,
                    Description = description
                };
            })
            .ToList();

        foreach (var entry in entries)
        {
            plan.TimePoints.Add(new TimePoint
            {
                Type = entry.Type,
                Label = entry.Label,
                StartTime = entry.Start,
                EndTime = entry.End,
                Description = entry.Description
            });
        }

        return plan;
    }

    public SchedulePlan? BuildPlanFor(DateTime now)
    {
        var count = GetCycleCount();
        var cycleIndex = CalculateCycleIndex(now, count);
        var dayIndex = ToDayIndex(now.DayOfWeek);
        return BuildPlanFor(cycleIndex, dayIndex);
    }

    public static int GetCurrentCycleIndex()
    {
        return GetCycleIndexFor(DateTime.Now);
    }

    public static int GetCycleIndexFor(DateTime date)
    {
        var count = GetCycleCount();
        return CalculateCycleIndex(date, count);
    }

    private static int GetCycleCount()
    {
        try
        {
            var n = SettingsService.General.WeeklyCycleCount;
            if (n < 1) n = 1;
            if (n > 6) n = 6;
            return n;
        }
        catch { return 4; }
    }

    private static DateTime? GetTermStartDate()
    {
        try
        {
            return SettingsService.General.TermStartDate;
        }
        catch
        {
            return null;
        }
    }

    private static int CalculateCycleIndex(DateTime date, int cycleCount)
    {
        if (cycleCount < 1) cycleCount = 1;

        var termStart = GetTermStartDate();
        if (termStart == null)
        {
            var calFallback = CultureInfo.CurrentCulture.Calendar;
            var weekOfYear = calFallback.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            return ((weekOfYear - 1) % cycleCount) + 1;
        }

        var startDate = termStart.Value.Date;
        var currentDate = date.Date;
        var diffDays = (currentDate - startDate).TotalDays;
        if (diffDays < 0) diffDays = 0;
        var weekOffset = (int)(diffDays / 7);
        return (weekOffset % cycleCount) + 1;
    }

    private static int DetermineCycleCountFromYaml(YamlRoot root)
    {
        try
        {
            var digits = new List<int>();
            bool hasSingle = false, hasDouble = false, hasAnyOrEmpty = false;
            foreach (var s in root.Schedules ?? new List<YamlDaySchedule>())
            {
                var w = s.Weeks?.Trim();
                if (string.IsNullOrWhiteSpace(w)) { hasAnyOrEmpty = true; continue; }
                if (w.Equals("不限", StringComparison.OrdinalIgnoreCase) || w.Equals("any", StringComparison.OrdinalIgnoreCase)) { hasAnyOrEmpty = true; continue; }
                if (w.Equals("单周", StringComparison.OrdinalIgnoreCase) || w.Equals("single", StringComparison.OrdinalIgnoreCase)) { hasSingle = true; continue; }
                if (w.Equals("双周", StringComparison.OrdinalIgnoreCase) || w.Equals("double", StringComparison.OrdinalIgnoreCase)) { hasDouble = true; continue; }
                var parts = w.Split(new[] { ',', '，', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var p in parts)
                {
                    if (int.TryParse(p, out var n)) digits.Add(n);
                }
            }
            int desired = 4;
            if (digits.Count > 0)
            {
                desired = Math.Clamp(digits.Max(), 1, 6);
                if (digits.Distinct().Count() == 1 && digits.First() == 1) desired = 1;
            }
            else if (hasSingle || hasDouble)
            {
                desired = 2;
            }
            else if (hasAnyOrEmpty)
            {
                desired = 4;
            }
            return desired;
        }
        catch { return 4; }
    }

    public void SaveDayFromPlan(int weekNumber, int dayIndex, SchedulePlan plan)
    {
        var weekly = GetWeekly(weekNumber) ?? new WeeklyScheduleFile { WeekNumber = weekNumber, Name = $"第{weekNumber}周课表" };
        weekly.DefaultClassDuration = plan.DefaultClassDuration;
        weekly.DefaultBreakDuration = plan.DefaultBreakDuration;
        weekly.Subjects = plan.Subjects ?? new System.Collections.ObjectModel.ObservableCollection<Subject>();

        var day = weekly.Days.FirstOrDefault(d => d.EnableDay == dayIndex);
        if (day == null)
        {
            day = new WeeklyDaySchedule { EnableDay = dayIndex };
            weekly.Days.Add(day);
        }
        day.Classes.Clear();

        foreach (var tp in plan.TimePoints)
        {
            var type = tp.Type;
            var start = tp.StartTime;
            var end = tp.EndTime;
            if (type is TimePointType.Divider or TimePointType.Action)
            {
                end = start;
            }
            if (end < start) end = start;

            var subjectName = type == TimePointType.Class ? ResolveSubjectNameFromLabel(weekly, tp.Label) : null;
            day.Classes.Add(new WeeklyClass
            {
                Type = type,
                Subject = subjectName,
                Label = tp.Label,
                Description = string.IsNullOrWhiteSpace(tp.Description) ? null : tp.Description,
                StartTime = FormatTime(start),
                EndTime = FormatTime(end)
            });
        }

        SaveWeekly(weekly);
    }

    private static int ToDayIndex(DayOfWeek day) => day switch
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

    private static TimeSpan ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new TimeSpan(0, 0, 0);
        if (TimeSpan.TryParse(s, out var ts)) return ts;
        var parts = s.Split(':');
        if (parts.Length >= 2)
        {
            int h = int.TryParse(parts[0], out var hh) ? hh : 0;
            int m = int.TryParse(parts[1], out var mm) ? mm : 0;
            return new TimeSpan(h, m, 0);
        }
        return new TimeSpan(0, 0, 0);
    }

    private static string FormatTime(TimeSpan t)
    {
        return new DateTime(t.Ticks).ToString("HH:mm");
    }

    private static string ResolveSubjectLabel(WeeklyScheduleFile weekly, string subject)
    {
        var info = weekly.Subjects?.FirstOrDefault(s => s.Name == subject);
        if (info == null) return subject;
        return string.IsNullOrWhiteSpace(info.SimplifiedName) ? (info.Name ?? subject) : info.SimplifiedName;
    }

    private static string ResolveSubjectDescription(WeeklyScheduleFile weekly, string subject)
    {
        var info = weekly.Subjects?.FirstOrDefault(s => s.Name == subject);
        if (info == null) return string.Empty;
        var teacher = string.IsNullOrWhiteSpace(info.Teacher) ? string.Empty : $"教师: {info.Teacher}";
        var room = string.IsNullOrWhiteSpace(info.Room) ? string.Empty : $"教室: {info.Room}";
        var sep = (!string.IsNullOrEmpty(teacher) && !string.IsNullOrEmpty(room)) ? "  " : string.Empty;
        return $"{teacher}{sep}{room}";
    }

    private static string? ResolveSubjectNameFromLabel(WeeklyScheduleFile weekly, string label)
    {
        var match = weekly.Subjects?.FirstOrDefault(s => s.SimplifiedName == label || s.Name == label);
        return match?.Name ?? label;
    }
}
