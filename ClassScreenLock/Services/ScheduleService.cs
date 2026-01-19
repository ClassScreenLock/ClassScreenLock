using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Linq;
using ClassScreenLock.Models;

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
            var json = File.ReadAllText(sourcePath);
            var schedule = JsonSerializer.Deserialize<SchedulePlan>(json, JsonOptions);
            if (schedule != null)
            {
                // 为导入的时间表生成新的 ID 以避免覆盖本地已有的时间表
                schedule.Id = Guid.NewGuid().ToString();
                
                // 如果内部名称是默认的“新时间表”，尝试使用文件名作为名称
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入时间计划失败: {ex.Message}");
            throw;
        }
        return null;
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
        var schedules = LoadAllSchedules();
        return schedules.FirstOrDefault(s => s.IsActive) ?? schedules.FirstOrDefault();
    }

    public (TimePoint? current, TimePoint? next) GetCurrentAndNextTimePoint(TimeSpan time)
    {
        var schedule = GetActiveSchedule();
        if (schedule == null || schedule.TimePoints == null) return (null, null);

        var current = schedule.TimePoints
            .Where(t => time >= t.StartTime && time < t.EndTime)
            .FirstOrDefault();

        var next = schedule.TimePoints
            .Where(t => t.StartTime > time)
            .OrderBy(t => t.StartTime)
            .FirstOrDefault();

        return (current, next);
    }
}
