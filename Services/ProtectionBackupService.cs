using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ClassScreenLock.Services;

public class ProtectionBackupService
{
    private static readonly Lazy<ProtectionBackupService> _instance = new(() => new ProtectionBackupService());
    public static ProtectionBackupService Instance => _instance.Value;

    private static readonly string DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string BackupDirectory = Path.Combine(DataDirectory, "Backup");
    private static readonly string LogFile = Path.Combine(BackupDirectory, "backup_log.json");
    private static readonly string ReportPath = Path.Combine(BackupDirectory, "protection_report.json");
    private static readonly string ErrorLog = Path.Combine(BackupDirectory, "error_log.json");

    private ProtectionBackupService()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            Directory.CreateDirectory(BackupDirectory);
        }
    }

    public bool BackupExists => File.Exists(LogFile);

    public async Task<List<BackupItem>> GetBackupDetailsAsync()
    {
        if (!File.Exists(LogFile)) return new List<BackupItem>();
        var content = await File.ReadAllTextAsync(LogFile);
        return JsonSerializer.Deserialize<List<BackupItem>>(content) ?? new List<BackupItem>();
    }

    public class BackupItem
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string BackupFileName { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public DateTime BackupTime { get; set; }
    }

    public async Task<bool> CreateBackupAsync()
    {
        try
        {
            if (!Directory.Exists(BackupDirectory))
            {
                Directory.CreateDirectory(BackupDirectory);
            }

            // 获取需要防护的文件列表 (默认为 Data 目录下的所有 json 文件，排除备份目录)
            var filesToProtect = Directory.GetFiles(DataDirectory, "*.json", SearchOption.TopDirectoryOnly);
            var backupItems = new List<BackupItem>();

            foreach (var file in filesToProtect)
            {
                var fileName = Path.GetFileName(file);
                var backupFileName = $"{Guid.NewGuid()}_{fileName}.bak";
                var backupPath = Path.Combine(BackupDirectory, backupFileName);

                // 计算校验和
                var checksum = await CalculateChecksumAsync(file);

                // 执行备份
                File.Copy(file, backupPath, true);

                backupItems.Add(new BackupItem
                {
                    OriginalPath = file,
                    BackupFileName = backupFileName,
                    Checksum = checksum,
                    BackupTime = DateTime.Now
                });
            }

            // 记录备份日志
            await File.WriteAllTextAsync(LogFile, JsonSerializer.Serialize(backupItems, new JsonSerializerOptions { WriteIndented = true }));
            
            LogService.Instance.Log("Backup", "CreateSuccess", $"已备份 {backupItems.Count} 个文件");
            return true;
        }
        catch (Exception ex)
        {
            await LogErrorAsync("Backup", ex.Message);
            return false;
        }
    }

    public async Task<bool> RestoreBackupAsync()
    {
        try
        {
            if (!File.Exists(LogFile))
            {
                await LogErrorAsync("Restore", "未找到备份日志文件");
                return false;
            }

            var logContent = await File.ReadAllTextAsync(LogFile);
            var backupItems = JsonSerializer.Deserialize<List<BackupItem>>(logContent);

            if (backupItems == null || !backupItems.Any())
            {
                await LogErrorAsync("Restore", "备份日志内容为空");
                return false;
            }

            var restoredFiles = new List<string>();
            bool hasErrors = false;

            foreach (var item in backupItems)
            {
                var backupPath = Path.Combine(BackupDirectory, item.BackupFileName);
                if (!File.Exists(backupPath))
                {
                    await LogErrorAsync("Restore", $"未找到备份文件: {item.BackupFileName}");
                    hasErrors = true;
                    continue;
                }

                // 验证备份文件的完整性
                var currentChecksum = await CalculateChecksumAsync(backupPath);
                if (currentChecksum != item.Checksum)
                {
                    await LogErrorAsync("Restore", $"备份文件完整性校验失败: {item.BackupFileName}");
                    hasErrors = true;
                    continue;
                }

                // 执行恢复
                File.Copy(backupPath, item.OriginalPath, true);
                restoredFiles.Add(item.OriginalPath);
            }

            if (hasErrors)
            {
                // 如果有错误，保留备份文件
                await LogErrorAsync("Restore", "恢复过程中出现部分错误，已保留备份文件");
                return false;
            }

            // 恢复成功后删除备份
            foreach (var item in backupItems)
            {
                var backupPath = Path.Combine(BackupDirectory, item.BackupFileName);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            if (File.Exists(LogFile)) File.Delete(LogFile);

            // 生成报告
            await GenerateReportAsync(restoredFiles);
            
            LogService.Instance.Log("Restore", "RestoreSuccess", $"已成功恢复 {restoredFiles.Count} 个文件");
            return true;
        }
        catch (Exception ex)
        {
            await LogErrorAsync("Restore", ex.Message);
            return false;
        }
    }

    private async Task<string> CalculateChecksumAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task LogErrorAsync(string operation, string message)
    {
        var errorEntry = new
        {
            Time = DateTime.Now,
            Operation = operation,
            Message = message
        };
        
        var errors = new List<object>();
        if (File.Exists(ErrorLog))
        {
            try
            {
                var existing = await File.ReadAllTextAsync(ErrorLog);
                errors = JsonSerializer.Deserialize<List<object>>(existing) ?? new List<object>();
            }
            catch { }
        }
        
        errors.Add(errorEntry);
        await File.WriteAllTextAsync(ErrorLog, JsonSerializer.Serialize(errors, new JsonSerializerOptions { WriteIndented = true }));
        LogService.Instance.Log("BackupError", operation, message);
    }

    private async Task GenerateReportAsync(List<string> restoredFiles)
    {
        var report = new
        {
            Time = DateTime.Now,
            Status = "Success",
            RestoredFilesCount = restoredFiles.Count,
            Files = restoredFiles
        };
        
        await File.WriteAllTextAsync(ReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
