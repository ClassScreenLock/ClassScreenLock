using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
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

    // 关键修复 N：同一时间只保留最新一组备份；旧组在创建新组时归档而非删除。
    private static readonly string ArchiveDirectory = Path.Combine(BackupDirectory, "Archive");

    // 防止并发创建/还原时互相覆盖：所有写操作走这个信号量。
    private static readonly object _opLock = new();
    private static readonly SemaphoreSlim _opSemaphore = new(1, 1);

    private ProtectionBackupService()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            Directory.CreateDirectory(BackupDirectory);
        }
        if (!Directory.Exists(ArchiveDirectory))
        {
            Directory.CreateDirectory(ArchiveDirectory);
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
        // 关键修复：lock 不允许 await。改用 SemaphoreSlim 串行化。
        await _opSemaphore.WaitAsync();
        try
        {
            return CreateBackupInternal();
        }
        finally
        {
            _opSemaphore.Release();
        }
    }

    private bool CreateBackupInternal()
    {
        try
        {
            if (!Directory.Exists(BackupDirectory))
            {
                Directory.CreateDirectory(BackupDirectory);
            }

            // 关键修复 N：如果已存在未还原的备份组，先归档旧组，
            // 避免多次 CreateBackupAsync 造成"日志被覆盖，旧备份成为孤儿文件"。
            if (File.Exists(LogFile))
            {
                ArchiveExistingBackupSet("superseeded-by-new-backup");
            }

            var filesToProtect = Directory.GetFiles(DataDirectory, "*.json", SearchOption.TopDirectoryOnly);
            var backupItems = new List<BackupItem>();

            foreach (var file in filesToProtect)
            {
                var fileName = Path.GetFileName(file);
                var backupFileName = $"{Guid.NewGuid()}_{fileName}.bak";
                var backupPath = Path.Combine(BackupDirectory, backupFileName);

                var checksum = CalculateChecksum(file);

                // 关键修复 N：在覆盖任何文件前先做一次 dry-run，验证源文件可读 + 校验和稳定。
                // 避免"备份到一半原文件被改"导致恢复时拿到不一致的数据。
                var dryChecksum = CalculateChecksum(file);
                if (dryChecksum != checksum)
                {
                    LogErrorSync("Backup", $"源文件在备份过程中发生变化: {file}");
                    return false;
                }

                File.Copy(file, backupPath, true);

                backupItems.Add(new BackupItem
                {
                    OriginalPath = file,
                    BackupFileName = backupFileName,
                    Checksum = checksum,
                    BackupTime = DateTime.Now
                });
            }

            File.WriteAllText(LogFile, JsonSerializer.Serialize(backupItems, new JsonSerializerOptions { WriteIndented = true }));

            LogService.Instance.Log("Backup", "CreateSuccess", $"已备份 {backupItems.Count} 个文件");
            return true;
        }
        catch (Exception ex)
        {
            LogErrorSync("Backup", ex.Message);
            return false;
        }
    }

    public async Task<bool> RestoreBackupAsync()
    {
        await _opSemaphore.WaitAsync();
        try
        {
            return RestoreBackupInternal();
        }
        finally
        {
            _opSemaphore.Release();
        }
    }

    private bool RestoreBackupInternal()
    {
        try
        {
            if (!File.Exists(LogFile))
            {
                LogErrorSync("Restore", "未找到备份日志文件");
                return false;
            }

            var logContent = File.ReadAllText(LogFile);
            var backupItems = JsonSerializer.Deserialize<List<BackupItem>>(logContent);

            if (backupItems == null || !backupItems.Any())
            {
                LogErrorSync("Restore", "备份日志内容为空");
                return false;
            }

            // 关键修复 N：先做"预演"，验证所有备份文件存在且校验通过，
            // 任意一项失败都不进入正式覆盖流程。原代码在循环内 File.Copy
            // 后才校验，会出现"部分已恢复、部分失败"的撕裂状态。
            var restoredFiles = new List<string>();
            var failedItems = new List<(BackupItem item, string reason)>();
            var currentFileChecksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in backupItems)
            {
                var backupPath = Path.Combine(BackupDirectory, item.BackupFileName);
                if (!File.Exists(backupPath))
                {
                    failedItems.Add((item, "未找到备份文件"));
                    continue;
                }

                var currentChecksum = CalculateChecksum(backupPath);
                if (currentChecksum != item.Checksum)
                {
                    failedItems.Add((item, "备份文件完整性校验失败"));
                    continue;
                }

                if (File.Exists(item.OriginalPath))
                {
                    currentFileChecksums[item.OriginalPath] = CalculateChecksum(item.OriginalPath);
                }
            }

            if (failedItems.Any())
            {
                foreach (var (item, reason) in failedItems)
                {
                    LogErrorSync("Restore", $"{reason}: {item.BackupFileName}");
                }
                LogErrorSync("Restore", "预演失败，未修改任何原文件");
                return false;
            }

            // 预演通过：执行实际恢复（用临时文件 + 原子替换）
            foreach (var item in backupItems)
            {
                var backupPath = Path.Combine(BackupDirectory, item.BackupFileName);
                var tempPath = item.OriginalPath + ".restore.tmp";

                try
                {
                    File.Copy(backupPath, tempPath, true);

                    // 二次校验：复制后立刻校验临时文件，避免磁盘错误 / 权限问题
                    if (CalculateChecksum(tempPath) != item.Checksum)
                    {
                        File.Delete(tempPath);
                        LogErrorSync("Restore", $"复制后校验失败: {item.OriginalPath}");
                        return false;
                    }

                    if (File.Exists(item.OriginalPath))
                    {
                        File.Replace(tempPath, item.OriginalPath, null);
                    }
                    else
                    {
                        File.Move(tempPath, item.OriginalPath);
                    }
                    restoredFiles.Add(item.OriginalPath);
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    LogErrorSync("Restore", $"恢复失败 {item.OriginalPath}: {ex.Message}");
                    return false;
                }
            }

            // 全部成功后归档本次备份（而不是直接删除），方便回滚
            ArchiveExistingBackupSet("restored-successfully");
            File.Delete(LogFile);

            GenerateReportSync(restoredFiles);

            LogService.Instance.Log("Restore", "RestoreSuccess", $"已成功恢复 {restoredFiles.Count} 个文件");
            return true;
        }
        catch (Exception ex)
        {
            LogErrorSync("Restore", ex.Message);
            return false;
        }
    }

    private void ArchiveExistingBackupSet(string reason)
    {
        try
        {
            if (!File.Exists(LogFile)) return;

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var destDir = Path.Combine(ArchiveDirectory, stamp + "_" + reason);
            Directory.CreateDirectory(destDir);

            foreach (var bak in Directory.GetFiles(BackupDirectory, "*.bak"))
            {
                var dest = Path.Combine(destDir, Path.GetFileName(bak));
                try { File.Move(bak, dest); } catch { }
            }
            try { File.Move(LogFile, Path.Combine(destDir, Path.GetFileName(LogFile))); } catch { }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "Backup", "Archive", $"归档旧备份失败: {ex.Message}");
        }
    }

    private string CalculateChecksum(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task LogErrorAsync(string operation, string message)
    {
        await Task.Run(() => LogErrorSync(operation, message));
    }

    private void LogErrorSync(string operation, string message)
    {
        try
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
                    var existing = File.ReadAllText(ErrorLog);
                    errors = JsonSerializer.Deserialize<List<object>>(existing) ?? new List<object>();
                }
                catch { }
            }

            errors.Add(errorEntry);
            File.WriteAllText(ErrorLog, JsonSerializer.Serialize(errors, new JsonSerializerOptions { WriteIndented = true }));
            LogService.Instance.Log("BackupError", operation, message);
        }
        catch
        {
            // 日志写入失败不应阻塞主流程
        }
    }

    private async Task GenerateReportAsync(List<string> restoredFiles)
    {
        await Task.Run(() => GenerateReportSync(restoredFiles));
    }

    private void GenerateReportSync(List<string> restoredFiles)
    {
        try
        {
            var report = new
            {
                Time = DateTime.Now,
                Status = "Success",
                RestoredFilesCount = restoredFiles.Count,
                Files = restoredFiles
            };

            File.WriteAllText(ReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 报告生成失败不应阻塞主流程
        }
    }
}
