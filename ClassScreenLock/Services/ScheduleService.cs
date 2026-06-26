using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Linq;
using ClassScreenLock.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClassScreenLock.Services;

public class ScheduleService
{
    private static readonly string SchedulesDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Data",
        "Schedules");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static ScheduleService? _instance;
    public static ScheduleService Instance => _instance ??= new ScheduleService();

    private ScheduleService()
    {
        EnsureDirectoryExists();
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(SchedulesDirectory))
        {
            Directory.CreateDirectory(SchedulesDirectory);
        }
    }

    public List<SchedulePlan> LoadAllSchedules()
    {
        var schedules = new List<SchedulePlan>();
        try
        {
            EnsureDirectoryExists();
            var files = Directory.GetFiles(SchedulesDirectory, "*.json");
            foreach (var file in files)
            {
                var json = File.ReadAllText(file);
                var schedule = JsonSerializer.Deserialize<SchedulePlan>(json, JsonOptions);
                if (schedule != null)
                {
                    schedules.Add(schedule);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载时间计划失败: {ex.Message}");
        }
        return schedules;
    }

    private string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }

    public void SaveSchedule(SchedulePlan schedule)
    {
        try
        {
            EnsureDirectoryExists();
            
            // 查找是否已有该 ID 的文件（可能名称已更改）
            var existingFiles = Directory.GetFiles(SchedulesDirectory, "*.json");
            foreach (var file in existingFiles)
            {
                try
                {
                    var jsonContent = File.ReadAllText(file);
                    // 简单的字符串包含检查 ID，避免完整反序列化以提高性能
                    if (jsonContent.Contains($"\"id\": \"{schedule.Id}\"") || jsonContent.Contains($"\"id\":\"{schedule.Id}\""))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var newName = SanitizeFileName(schedule.Name);
                        
                        // 如果文件名不同，删除旧文件
                        if (fileName != newName)
                        {
                            File.Delete(file);
                        }
                        break;
                    }
                }
                catch { /* 忽略读取错误 */ }
            }

            var safeName = SanitizeFileName(schedule.Name);
            var filePath = Path.Combine(SchedulesDirectory, $"{safeName}.json");
            
            // 处理重名情况：如果同名文件存在但 ID 不同，加后缀
            int counter = 1;
            while (File.Exists(filePath))
            {
                try
                {
                    var jsonContent = File.ReadAllText(filePath);
                    if (jsonContent.Contains($"\"id\": \"{schedule.Id}\"") || jsonContent.Contains($"\"id\":\"{schedule.Id}\""))
                    {
                        // 是同一个 ID 的文件，直接覆盖
                        break;
                    }
                }
                catch { }
                
                filePath = Path.Combine(SchedulesDirectory, $"{safeName}_{counter++}.json");
            }

            var json = JsonSerializer.Serialize(schedule, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存时间计划失败: {ex.Message}");
        }
    }

    public void ExportSchedule(SchedulePlan schedule, string targetPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(schedule, JsonOptions);
            File.WriteAllText(targetPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出时间计划失败: {ex.Message}");
            throw;
        }
    }

    public SchedulePlan? ImportSchedule(string sourcePath)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext is ".yml" or ".yaml")
            {
                var yamlText = File.ReadAllText(sourcePath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var root = deserializer.Deserialize<YamlRoot>(yamlText);
                if (root?.Schedules != null && root.Schedules.Count > 0)
                {
                    var subjectDict = (root.Subjects ?? new List<YamlSubject>())
                        .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                        .ToDictionary(s => s.Name!, s => s);

                    SchedulePlan? firstPlan = null;
                    foreach (var s in root.Schedules)
                    {
                        var plan = new SchedulePlan
                        {
                            Name = string.IsNullOrWhiteSpace(s.Name) ? (Path.GetFileNameWithoutExtension(sourcePath) ?? "新时间表") : s.Name,
                            EnableDay = s.EnableDay == 0 ? null : s.EnableDay,
                            Weeks = string.IsNullOrWhiteSpace(s.Weeks) ? null : s.Weeks
                        };

                        // subjects
                        var subjects = new System.Collections.ObjectModel.ObservableCollection<Subject>();
                        foreach (var subj in root.Subjects ?? new List<YamlSubject>())
                        {
                            subjects.Add(new Subject
                            {
                                Name = subj.Name ?? string.Empty,
                                SimplifiedName = subj.SimplifiedName ?? string.Empty,
                                Teacher = subj.Teacher ?? string.Empty,
                                Room = subj.Room ?? string.Empty
                            });
                        }
                        plan.Subjects = subjects;

                        // classes -> time points
                        foreach (var c in s.Classes ?? new List<YamlClass>())
                        {
                            var isBreak = string.IsNullOrWhiteSpace(c.Subject);
                            var start = ParseTime(c.StartTime);
                            var end = ParseTime(c.EndTime);
                            var tp = new TimePoint
                            {
                                Type = isBreak ? TimePointType.Break : TimePointType.Class,
                                Label = isBreak ? "课间休息" : (subjectDict.TryGetValue(c.Subject ?? string.Empty, out var info) ? (info.SimplifiedName ?? info.Name ?? c.Subject ?? "课程") : (c.Subject ?? "课程")),
                                StartTime = start,
                                EndTime = end,
                                Description = isBreak ? string.Empty : BuildDescription(subjectDict, c.Subject)
                            };
                            plan.TimePoints.Add(tp);
                        }

                        plan.Id = Guid.NewGuid().ToString();
                        SaveSchedule(plan);
                        firstPlan ??= plan;
                    }

                    return firstPlan;
                }
                return null;
            }
            else
            {
                var json = File.ReadAllText(sourcePath);
                var schedule = JsonSerializer.Deserialize<SchedulePlan>(json, JsonOptions);
                if (schedule != null)
                {
                    schedule.Id = Guid.NewGuid().ToString();
                    if (schedule.Name == "新时间表")
                    {
                        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            schedule.Name = fileName;
                        }
                    }
                    SaveSchedule(schedule);
                    return schedule;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入时间计划失败: {ex.Message}");
            throw;
        }
        return null;
    }

    private static TimeSpan ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new TimeSpan(0, 0, 0);
        if (TimeSpan.TryParse(s, out var ts)) return ts;
        // 支持像 07:30 或 7:30
        var parts = s.Split(':');
        if (parts.Length >= 2)
        {
            int h = int.TryParse(parts[0], out var hh) ? hh : 0;
            int m = int.TryParse(parts[1], out var mm) ? mm : 0;
            return new TimeSpan(h, m, 0);
        }
        return new TimeSpan(0, 0, 0);
    }

    private static string BuildDescription(Dictionary<string, YamlSubject> subjectDict, string? subjectName)
    {
        if (string.IsNullOrWhiteSpace(subjectName)) return string.Empty;
        if (subjectDict.TryGetValue(subjectName, out var info))
        {
            var teacher = string.IsNullOrWhiteSpace(info.Teacher) ? string.Empty : $"教师: {info.Teacher}";
            var room = string.IsNullOrWhiteSpace(info.Room) ? string.Empty : $"教室: {info.Room}";
            var sep = (!string.IsNullOrEmpty(teacher) && !string.IsNullOrEmpty(room)) ? "  " : string.Empty;
            return $"{teacher}{sep}{room}";
        }
        return string.Empty;
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

    public void DeleteSchedule(string id)
    {
        try
        {
            var files = Directory.GetFiles(SchedulesDirectory, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var jsonContent = File.ReadAllText(file);
                    if (jsonContent.Contains($"\"id\": \"{id}\"") || jsonContent.Contains($"\"id\":\"{id}\""))
                    {
                        File.Delete(file);
                        break;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"删除时间计划失败: {ex.Message}");
        }
    }

    public SchedulePlan? GetActiveSchedule()
    {
        var weeklyPlan = WeeklyScheduleService.Instance.BuildPlanFor(DateTime.Now);
        return weeklyPlan;
    }

    public (TimePoint? point, DateTime? classDateTime) GetNextClassPoint()
    {
        var now = DateTime.Now;
        var currentTime = now.TimeOfDay;

        for (int daysAhead = 0; daysAhead <= 7; daysAhead++)
        {
            var targetDate = now.Date.AddDays(daysAhead);
            var schedule = WeeklyScheduleService.Instance.BuildPlanFor(targetDate);
            if (schedule == null || schedule.TimePoints == null || schedule.TimePoints.Count == 0)
            {
                continue;
            }

            var futureClasses = schedule.TimePoints
                .Where(t => t.Type == TimePointType.Class)
                .ToList();

            if (daysAhead == 0)
            {
                var todayClass = futureClasses
                    .Where(t => t.StartTime > currentTime)
                    .OrderBy(t => t.StartTime)
                    .FirstOrDefault();
                if (todayClass != null)
                {
                    return (todayClass, targetDate.Add(todayClass.StartTime));
                }
            }
            else
            {
                var earliestClass = futureClasses
                    .OrderBy(t => t.StartTime)
                    .FirstOrDefault();
                if (earliestClass != null)
                {
                    var classDateTime = targetDate.Add(earliestClass.StartTime);
                    return (earliestClass, classDateTime);
                }
            }
        }

        return (null, null);
    }

    public (TimePoint? current, TimePoint? next) GetCurrentAndNextTimePoint(TimeSpan time)
    {
        var schedule = GetActiveSchedule();
        if (schedule == null || schedule.TimePoints == null || schedule.TimePoints.Count == 0)
        {
            return (new TimePoint
            {
                Type = TimePointType.Break,
                Label = "课间休息",
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(24)
            }, null);
        }

        var current = schedule.TimePoints
            .Where(t => time >= t.StartTime && time < t.EndTime)
            .FirstOrDefault();

        var next = schedule.TimePoints
            .Where(t => t.StartTime > time)
            .OrderBy(t => t.StartTime)
            .FirstOrDefault();

        if (current == null)
        {
            var classPoint = schedule.TimePoints
                .Where(t => t.Type == TimePointType.Class)
                .FirstOrDefault(t => time >= t.StartTime && time < t.EndTime);

            if (classPoint == null)
            {
                current = new TimePoint
                {
                    Type = TimePointType.Break,
                    Label = "课间休息",
                    StartTime = TimeSpan.Zero,
                    EndTime = TimeSpan.FromHours(24)
                };
            }
        }

        return (current, next);
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
}
