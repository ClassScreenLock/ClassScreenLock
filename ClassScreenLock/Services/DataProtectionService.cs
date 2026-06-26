using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.AccessControl;
using ClassScreenLock.Services;

namespace ClassScreenLock.Services;

public class DataProtectionService
{
    private static readonly Lazy<DataProtectionService> _instance = new(() => new DataProtectionService());
    public static DataProtectionService Instance => _instance.Value;

    private static readonly string DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassScreenLock");
    private static readonly string EncryptedBackupFile = Path.Combine(AppDataDirectory, "ClassScreenLock_backup.dat");
    private static readonly string SyncLogFile = Path.Combine(AppDataDirectory, "ClassScreenLock_sync_log.json");

    private FileSystemWatcher _fileWatcher = null!;
    private readonly object _syncLock = new();
    private readonly object _fileLock = new();
    private bool _isSyncing = false;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private const int SyncCooldownMs = 500; // 500 毫秒冷却时间
    private const int MaxLogEntries = 100; // 最多保留 100 条日志
    private const int MaxLogFileSizeKB = 500; // 日志文件最大 500KB

    private DataProtectionService()
    {
        InitializeAppDataDirectory();
        InitializeFileWatcher();
    }

    private void InitializeAppDataDirectory()
    {
        if (!Directory.Exists(AppDataDirectory))
        {
            Directory.CreateDirectory(AppDataDirectory);
        }
        
        // 设置目录为隐藏和系统属性
        SetSystemHiddenDirectory(AppDataDirectory);
    }

    private void SetSystemHiddenDirectory(string directoryPath)
    {
        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            // 设置隐藏和系统属性
            dirInfo.Attributes = FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System;
            
            // 同时设置目录下所有现有文件的属性
            if (Directory.Exists(directoryPath))
            {
                foreach (var file in Directory.GetFiles(directoryPath))
                {
                    var fileInfo = new FileInfo(file);
                    fileInfo.Attributes = FileAttributes.Hidden | FileAttributes.System;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DataProtection", "SetSystemHidden", $"设置系统隐藏失败：{ex.Message}");
        }
    }

    private void SetSystemHiddenFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                fileInfo.Attributes = FileAttributes.Hidden | FileAttributes.System;
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DataProtection", "SetSystemHidden", $"设置文件系统隐藏失败：{ex.Message}");
        }
    }

    public void EnsureAllFilesProtected()
    {
        SetSystemHiddenDirectory(AppDataDirectory);
        SetSystemHiddenFile(EncryptedBackupFile);
        SetSystemHiddenFile(SyncLogFile);
        CleanupLogFiles();
        ProtectDataDirectoryFiles();
        LogService.Instance.Log("DataProtection", "Protected", "System", "已设置 AppData 目录和文件的系统隐藏属性");
    }

    public void ProtectDataDirectoryFiles()
    {
        try
        {
            if (!Directory.Exists(DataDirectory))
            {
                return;
            }

            var protectedExtensions = new[] { ".dat", ".hash", ".bak" };
            var protectedFiles = Directory.GetFiles(DataDirectory, "*", SearchOption.AllDirectories)
                .Where(f => protectedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

            foreach (var file in protectedFiles)
            {
                SetSystemHiddenFile(file);
            }

            LogService.Instance.Log("DataProtection", "Protected", "DataFiles", "已设置 Data 目录下 .dat/.hash/.bak 文件的系统隐藏属性");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DataProtection", "ProtectDataFiles", $"保护 Data 目录文件失败：{ex.Message}");
        }
    }

    private void CleanupLogFiles()
    {
        try
        {
            if (!File.Exists(SyncLogFile))
            {
                return;
            }

            var fileInfo = new FileInfo(SyncLogFile);
            
            // 检查文件大小
            if (fileInfo.Length > MaxLogFileSizeKB * 1024)
            {
                TrimLogFileBySize();
                return;
            }

            // 检查日志条目数量
            var logContent = File.ReadAllText(SyncLogFile);
            if (string.IsNullOrWhiteSpace(logContent))
            {
                return;
            }

            var logs = JsonSerializer.Deserialize<List<LogEntry>>(logContent);
            if (logs != null && logs.Count > MaxLogEntries)
            {
                TrimLogFileByCount(logs);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DataProtection", "CleanupLog", $"清理日志文件失败：{ex.Message}");
        }
    }

    private void TrimLogFileBySize()
    {
        try
        {
            var logContent = File.ReadAllText(SyncLogFile);
            if (string.IsNullOrWhiteSpace(logContent))
            {
                return;
            }

            var logs = JsonSerializer.Deserialize<List<LogEntry>>(logContent);
            if (logs == null || logs.Count == 0)
            {
                return;
            }

            // 只保留最新的日志
            var trimmedLogs = logs.Skip(Math.Max(0, logs.Count - MaxLogEntries)).ToList();
            SaveTrimmedLogs(trimmedLogs);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DataProtection", "TrimLog", $"按大小裁剪日志失败：{ex.Message}");
        }
    }

    private void TrimLogFileByCount(List<LogEntry> logs)
    {
        try
        {
            // 只保留最新的日志
            var trimmedLogs = logs.Skip(Math.Max(0, logs.Count - MaxLogEntries)).ToList();
            SaveTrimmedLogs(trimmedLogs);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DataProtection", "TrimLog", $"按数量裁剪日志失败：{ex.Message}");
        }
    }

    private void SaveTrimmedLogs(List<LogEntry> logs)
    {
        lock (_fileLock)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var trimmedContent = JsonSerializer.Serialize(logs, options);
            File.WriteAllText(SyncLogFile, trimmedContent);
            LogService.Instance.Log("DataProtection", "LogCleaned", "System", $"已清理日志文件，保留最新 {logs.Count} 条记录");
        }
    }

    private void InitializeFileWatcher()
    {
        _fileWatcher = new FileSystemWatcher
        {
            Path = DataDirectory,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _fileWatcher.Changed += OnDataFileChanged;
        _fileWatcher.Created += OnDataFileChanged;
        _fileWatcher.Deleted += OnDataFileChanged;
        _fileWatcher.Renamed += OnDataFileRenamed;
    }

    private void OnDataFileChanged(object sender, FileSystemEventArgs e)
    {
        var ext = Path.GetExtension(e.FullPath);
        var protectedExtensions = new[] { ".dat", ".hash", ".bak" };
        
        if (protectedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            SetSystemHiddenFile(e.FullPath);
        }
        
        if (Path.GetFileName(e.FullPath) == "logs.json" || 
            ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            return;
        
        var now = DateTime.Now;
        if (_isSyncing || (now - _lastSyncTime).TotalMilliseconds < SyncCooldownMs)
            return;
        
        _isSyncing = true;
        _lastSyncTime = now;
        
        Task.Run(async () => 
        {
            try
            {
                await SyncToAppDataAsync();
            }
            finally
            {
                _isSyncing = false;
            }
        });
    }

    private void OnDataFileRenamed(object sender, RenamedEventArgs e)
    {
        var ext = Path.GetExtension(e.FullPath);
        var protectedExtensions = new[] { ".dat", ".hash", ".bak" };
        
        if (protectedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            SetSystemHiddenFile(e.FullPath);
        }
        
        if (Path.GetFileName(e.FullPath) == "logs.json" || 
            Path.GetFileName(e.OldFullPath) == "logs.json" ||
            ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(e.OldFullPath).Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            return;
        
        var now = DateTime.Now;
        if (_isSyncing || (now - _lastSyncTime).TotalMilliseconds < SyncCooldownMs)
            return;
        
        _isSyncing = true;
        _lastSyncTime = now;
        
        Task.Run(async () => 
        {
            try
            {
                await SyncToAppDataAsync();
            }
            finally
            {
                _isSyncing = false;
            }
        });
    }

    public async Task<bool> CreateEncryptedBackupAsync()
    {
        try
        {
            var dataFiles = GetAllDataFiles();
            var backupData = new BackupData
            {
                Files = new List<BackupFile>(),
                Timestamp = DateTime.Now
            };

            foreach (var file in dataFiles)
            {
                var fileData = await File.ReadAllBytesAsync(file);
                var relativePath = GetRelativePath(file, DataDirectory);
                var checksum = CalculateChecksum(fileData);

                backupData.Files.Add(new BackupFile
                {
                    RelativePath = relativePath,
                    Content = fileData,
                    Checksum = checksum,
                    LastModified = File.GetLastWriteTime(file)
                });
            }

            var backupJson = JsonSerializer.Serialize(backupData);
            var encryptedData = EncryptData(Encoding.UTF8.GetBytes(backupJson));
            await WriteFileWithRetryAsync(EncryptedBackupFile, encryptedData);
            
            // 设置备份文件为系统隐藏
            SetSystemHiddenFile(EncryptedBackupFile);
            SetSystemHiddenFile(SyncLogFile);

            await LogSyncOperation("CreateBackup", dataFiles.Length);
            LogService.Instance.Log("DataProtection", "BackupCreated", "System", "已创建加密备份");
            return true;
        }
        catch (Exception ex)
        {
            await LogErrorAsync("CreateBackup", ex.Message);
            return false;
        }
    }

    public async Task<bool> SyncToAppDataAsync()
    {
        const int maxRetries = 5;
        const int retryDelayMs = 200;
        
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var dataFiles = GetAllDataFiles();
                var backupData = new BackupData
                {
                    Files = new List<BackupFile>(),
                    Timestamp = DateTime.Now
                };

                foreach (var file in dataFiles)
                {
                    var fileData = await File.ReadAllBytesAsync(file);
                    var relativePath = GetRelativePath(file, DataDirectory);
                    var checksum = CalculateChecksum(fileData);

                    backupData.Files.Add(new BackupFile
                    {
                        RelativePath = relativePath,
                        Content = fileData,
                        Checksum = checksum,
                        LastModified = File.GetLastWriteTime(file)
                    });
                }

                var backupJson = JsonSerializer.Serialize(backupData);
                var encryptedData = EncryptData(Encoding.UTF8.GetBytes(backupJson));
                
                // 使用安全的文件写入方式
                await WriteFileWithRetryAsync(EncryptedBackupFile, encryptedData);
                
                // 设置备份文件为系统隐藏
                SetSystemHiddenFile(EncryptedBackupFile);
                SetSystemHiddenFile(SyncLogFile);

                await LogSyncOperation("Sync", dataFiles.Length);
                LogService.Instance.Log("DataProtection", "Synced", "System", "数据已同步到 AppData");
                return true;
            }
            catch (UnauthorizedAccessException ex) when (retry < maxRetries - 1)
            {
                LogService.Instance.Log("Warning", "DataProtection", "Sync", $"同步失败，正在重试 ({retry + 1}/{maxRetries}): {ex.Message}");
                await Task.Delay(retryDelayMs * (retry + 1));
            }
            catch (IOException ex) when (retry < maxRetries - 1)
            {
                LogService.Instance.Log("Warning", "DataProtection", "Sync", $"同步失败，正在重试 ({retry + 1}/{maxRetries}): {ex.Message}");
                await Task.Delay(retryDelayMs * (retry + 1));
            }
            catch (Exception ex)
            {
                await LogErrorAsync("Sync", ex.Message);
                return false;
            }
        }
        
        await LogErrorAsync("Sync", "达到最大重试次数");
        return false;
    }

    private async Task WriteFileWithRetryAsync(string filePath, byte[] data)
    {
        const int maxAttempts = 3;
        const int delayMs = 100;
        
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                // 先尝试删除旧文件
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    await Task.Delay(50);
                }
                
                // 确保目录存在
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // 写入新文件
                using (var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await fs.WriteAsync(data, 0, data.Length);
                }
                
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(delayMs);
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(delayMs);
            }
        }
        
        // 如果上述方法都失败，使用最后手段：写入临时文件然后替换
        var tempFile = filePath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempFile, data);
            if (File.Exists(filePath))
            {
                File.Replace(tempFile, filePath, null);
            }
            else
            {
                File.Move(tempFile, filePath);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    public async Task<bool> VerifyAndRestoreDataAsync()
    {
        try
        {
            if (!File.Exists(EncryptedBackupFile))
            {
                // 没有备份文件，创建一个
                return await CreateEncryptedBackupAsync();
            }

            var encryptedData = await File.ReadAllBytesAsync(EncryptedBackupFile);
            var decryptedData = DecryptData(encryptedData);
            var backupData = JsonSerializer.Deserialize<BackupData>(Encoding.UTF8.GetString(decryptedData));

            if (backupData == null)
            {
                await LogErrorAsync("Verify", "备份数据损坏");
                return false;
            }

            var currentFiles = GetAllDataFiles();
            var currentFileMap = currentFiles.ToDictionary(f => GetRelativePath(f, DataDirectory));
            var backupFileMap = backupData.Files.ToDictionary(f => f.RelativePath);

            bool needsRestore = false;

            // 检查文件数量
            if (currentFiles.Length != backupData.Files.Count)
            {
                needsRestore = true;
            }
            else
            {
                // 检查每个文件
                foreach (var backupFile in backupData.Files)
                {
                    if (!currentFileMap.TryGetValue(backupFile.RelativePath, out var currentFile))
                    {
                        needsRestore = true;
                        break;
                    }

                    // 检查修改时间
                    if (File.GetLastWriteTime(currentFile) != backupFile.LastModified)
                    {
                        needsRestore = true;
                        break;
                    }

                    // 检查校验和
                    var currentData = await File.ReadAllBytesAsync(currentFile);
                    var currentChecksum = CalculateChecksum(currentData);
                    if (currentChecksum != backupFile.Checksum)
                    {
                        needsRestore = true;
                        break;
                    }
                }
            }

            if (needsRestore)
            {
                var restored = await RestoreFromBackupAsync(backupData);
                if (restored)
                {
                    // 恢复数据后重新加载初始化状态，避免进入重新初始化流程
                    try
                    {
                        InitializationService.Instance.ReloadState();
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Warning", "DataProtection", "ReloadInit", $"重新加载初始化状态失败：{ex.Message}");
                    }
                }
                return restored;
            }

            return true;
        }
        catch (Exception ex)
        {
            await LogErrorAsync("Verify", ex.Message);
            return false;
        }
    }

    private async Task<bool> RestoreFromBackupAsync(BackupData backupData)
    {
        try
        {
            // 确保 Data 目录存在
            if (!Directory.Exists(DataDirectory))
            {
                Directory.CreateDirectory(DataDirectory);
            }

            var restoredFiles = new List<string>();
            var backupFileMap = backupData.Files.ToDictionary(f => f.RelativePath);

            // 恢复被修改的文件
            foreach (var backupFile in backupData.Files)
            {
                var fullPath = Path.Combine(DataDirectory, backupFile.RelativePath);
                var directory = Path.GetDirectoryName(fullPath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(fullPath, backupFile.Content);
                File.SetLastWriteTime(fullPath, backupFile.LastModified);
                restoredFiles.Add(fullPath);
            }

            // 不删除新文件，只恢复被修改的文件
            // 这样可以避免误删用户新增的合法文件

            await LogSyncOperation("Restore", restoredFiles.Count);
            LogService.Instance.Log("DataProtection", "Restored", $"已从备份恢复 {restoredFiles.Count} 个文件");
            return true;
        }
        catch (Exception ex)
        {
            await LogErrorAsync("Restore", ex.Message);
            return false;
        }
    }

    private string[] GetAllDataFiles()
    {
        if (!Directory.Exists(DataDirectory))
        {
            return Array.Empty<string>();
        }

        var excludedDirectories = new[] { "Screenshots", "Webcam", "Backup" };
        var excludedExtensions = new[] { ".tmp", ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        const long maxFileSize = 10 * 1024 * 1024;

        return Directory.GetFiles(DataDirectory, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var relativePath = Path.GetRelativePath(DataDirectory, f);
                var dirName = relativePath.Split(Path.DirectorySeparatorChar)[0];
                
                if (excludedDirectories.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                    return false;
                
                if (Path.GetFileName(f).StartsWith("."))
                    return false;
                
                if (Path.GetFileName(f) == "logs.json")
                    return false;
                
                var ext = Path.GetExtension(f);
                if (excludedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    return false;
                
                try
                {
                    var fileInfo = new FileInfo(f);
                    if (fileInfo.Length > maxFileSize)
                        return false;
                }
                catch
                {
                    return false;
                }
                
                return true;
            })
            .ToArray();
    }

    private string GetRelativePath(string fullPath, string basePath)
    {
        return Path.GetRelativePath(basePath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private string CalculateChecksum(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private byte[] EncryptData(byte[] data)
    {
        using var aes = Aes.Create();
        // 使用32字节(256位)密钥
        var keyString = "ClassScreenLockDataProtect123456"; // 正好32字节
        aes.Key = Encoding.UTF8.GetBytes(keyString);
        aes.IV = new byte[16];
        
        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        
        cs.Write(data, 0, data.Length);
        cs.FlushFinalBlock();
        
        return ms.ToArray();
    }

    private byte[] DecryptData(byte[] data)
    {
        using var aes = Aes.Create();
        // 使用32字节(256位)密钥
        var keyString = "ClassScreenLockDataProtect123456"; // 正好32字节
        aes.Key = Encoding.UTF8.GetBytes(keyString);
        aes.IV = new byte[16];
        
        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(data);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var msDecrypt = new MemoryStream();
        
        cs.CopyTo(msDecrypt);
        return msDecrypt.ToArray();
    }

    private async Task LogSyncOperation(string operation, int fileCount)
    {
        var logEntry = new SyncLogEntry
        {
            Time = DateTime.Now,
            Operation = operation,
            FileCount = fileCount,
            Status = "Success"
        };

        await AppendToSyncLog(logEntry);
    }

    private async Task LogErrorAsync(string operation, string message)
    {
        var logEntry = new SyncLogEntry
        {
            Time = DateTime.Now,
            Operation = operation,
            Status = "Error",
            Message = message
        };

        await AppendToSyncLog(logEntry);
        LogService.Instance.Log("DataProtectionError", operation, message);
    }

    private async Task AppendToSyncLog(SyncLogEntry entry)
    {
        lock (_fileLock)
        {
            try
            {
                var logs = new List<SyncLogEntry>();
                
                if (File.Exists(SyncLogFile))
                {
                    try
                    {
                        var existing = File.ReadAllText(SyncLogFile);
                        logs = JsonSerializer.Deserialize<List<SyncLogEntry>>(existing) ?? new List<SyncLogEntry>();
                    }
                    catch { }
                }
                
                logs.Add(entry);
                
                // 只保留最近100条日志
                if (logs.Count > 100)
                {
                    logs = logs.Skip(logs.Count - 100).ToList();
                }
                
                File.WriteAllText(SyncLogFile, JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
        await Task.CompletedTask;
    }

    private class BackupData
    {
        public List<BackupFile> Files { get; set; } = new List<BackupFile>();
        public DateTime Timestamp { get; set; }
    }

    private class BackupFile
    {
        public string RelativePath { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string Checksum { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
    }

    private class SyncLogEntry
    {
        public DateTime Time { get; set; }
        public string Operation { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}