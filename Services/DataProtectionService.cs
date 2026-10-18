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

    private static readonly string DataDirectory = Helpers.AppPathHelper.DataDirectory;
    private static readonly string AppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassScreenLock");
    private static readonly string EncryptedBackupFile = Path.Combine(AppDataDirectory, "ClassScreenLock_backup.dat");
    private static readonly string SyncLogFile = Path.Combine(AppDataDirectory, "ClassScreenLock_sync_log.json");

    private FileSystemWatcher _fileWatcher = null!;
    private readonly object _syncLock = new();
    private readonly object _fileLock = new();
    private bool _isSyncing = false;
    private bool _isInitializationInProgress = false;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private const int SyncCooldownMs = 500; // 500 毫秒冷却时间
    private const int MaxLogEntries = 100; // 最多保留 100 条日志
    private const int MaxLogFileSizeKB = 500; // 日志文件最大 500KB

    /// <summary>最近一次备份的文件指纹（相对路径 → "长度:最后修改时间"），用于增量备份跳过未变化文件</summary>
    private readonly Dictionary<string, string> _lastBackupFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fingerprintLock = new();

    /// <summary>内存缓存最近一次备份包内容（相对路径 → 文件数据），增量备份无需重复解密旧包</summary>
    private Dictionary<string, BackupFile>? _cachedBackupFiles = null;
    private readonly object _cacheLock = new();

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

    public void SetInitializationInProgress(bool inProgress)
    {
        _isInitializationInProgress = inProgress;
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
        // 如果初始化正在进行，跳过同步
        if (_isInitializationInProgress)
        {
            return;
        }

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
        // 如果初始化正在进行，跳过同步
        if (_isInitializationInProgress)
        {
            return;
        }

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
        return await BuildAndWriteBackupAsync(forceFull: true);
    }

    public async Task<bool> SyncToAppDataAsync()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 150;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                // 增量备份：文件未变化时跳过读取/校验/序列化，只重写加密包
                return await BuildAndWriteBackupAsync(forceFull: false);
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

    /// <summary>
    /// 构建并写入加密备份包。
    /// - 并行读取 + 并行校验和（多核加速）
    /// - 增量模式：未变化文件（长度+最后修改时间指纹一致）直接复用上次内容，跳过 IO/哈希
    /// </summary>
    private async Task<bool> BuildAndWriteBackupAsync(bool forceFull)
    {
        var dataFiles = GetAllDataFiles();

        // 阶段 1：收集指纹，识别需要重新读取的文件（增量模式）
        var filesToRead = new List<(string Path, string Relative, string Fingerprint)>();
        var fingerprintSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in dataFiles)
        {
            string fingerprint;
            try
            {
                var fi = new FileInfo(file);
                fingerprint = $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                continue;
            }

            var rel = GetRelativePath(file, DataDirectory);
            fingerprintSnapshot[rel] = fingerprint;

            if (!forceFull)
            {
                lock (_fingerprintLock)
                {
                    // 与上次备份指纹一致 → 跳过
                    if (_lastBackupFingerprints.TryGetValue(rel, out var last) && last == fingerprint)
                    {
                        continue;
                    }
                }
            }

            filesToRead.Add((file, rel, fingerprint));
        }

        var backupData = new BackupData
        {
            Files = new List<BackupFile>(dataFiles.Length),
            Timestamp = DateTime.Now
        };

        // 阶段 2：并行读取已变化的文件（预分配序号，避免 IndexOf 的 O(n²)）
        var readResults = new BackupFile[filesToRead.Count];
        var indexedFiles = filesToRead.Select((item, idx) => (item, idx)).ToArray();
        await Parallel.ForEachAsync(indexedFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (entry, ct) =>
        {
            try
            {
                var fileData = await File.ReadAllBytesAsync(entry.item.Path, ct);
                var checksum = CalculateChecksum(fileData);
                readResults[entry.idx] = new BackupFile
                {
                    RelativePath = entry.item.Relative,
                    Content = fileData,
                    Checksum = checksum,
                    LastModified = File.GetLastWriteTime(entry.item.Path)
                };
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Warning", "DataProtection", "Backup", $"读取文件失败 {entry.item.Relative}: {ex.Message}");
            }
        });

        // 阶段 3：合并——变化文件用新数据，未变化文件复用上次备份内容（增量）
        if (forceFull || filesToRead.Count > 0)
        {
            foreach (var read in readResults)
            {
                if (read != null)
                {
                    backupData.Files.Add(read);
                }
            }

            if (!forceFull)
            {
                // 增量：补充未变化文件（优先内存缓存，无缓存才解密备份包）
                var remaining = new HashSet<string>(fingerprintSnapshot.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var r in readResults)
                {
                    if (r != null) remaining.Remove(r.RelativePath);
                }

                if (remaining.Count > 0)
                {
                    var previous = LoadBackupFilesFromDisk();
                    foreach (var rel in remaining)
                    {
                        if (previous.TryGetValue(rel, out var prevFile))
                        {
                            backupData.Files.Add(prevFile);
                        }
                    }
                }
            }
        }
        else
        {
            // 增量且没有任何文件变化：完全复用上次备份包，无需重新序列化加密
            LogService.Instance.Log("DataProtection", "Synced", "System", "数据无变化，跳过备份重写");
            return true;
        }

        // 阶段 4：序列化 + 加密 + 写盘
        var backupJson = JsonSerializer.Serialize(backupData);
        var encryptedData = EncryptData(Encoding.UTF8.GetBytes(backupJson));
        await WriteFileWithRetryAsync(EncryptedBackupFile, encryptedData);

        // 更新指纹快照与内存缓存
        lock (_fingerprintLock)
        {
            _lastBackupFingerprints.Clear();
            foreach (var kvp in fingerprintSnapshot)
            {
                _lastBackupFingerprints[kvp.Key] = kvp.Value;
            }
        }
        lock (_cacheLock)
        {
            _cachedBackupFiles = backupData.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        }

        SetSystemHiddenFile(EncryptedBackupFile);
        SetSystemHiddenFile(SyncLogFile);

        await LogSyncOperation("Sync", dataFiles.Length);
        LogService.Instance.Log("DataProtection", "Synced", "System", "数据已同步到 AppData");
        return true;
    }

    /// <summary>
    /// 加载最近一次备份包的文件映射。优先用内存缓存（避免每次同步都解密整个备份包），
    /// 缓存缺失时才从磁盘解密读取。
    /// </summary>
    private Dictionary<string, BackupFile> LoadBackupFilesFromDisk()
    {
        lock (_cacheLock)
        {
            if (_cachedBackupFiles != null)
            {
                return _cachedBackupFiles;
            }
        }

        try
        {
            if (!File.Exists(EncryptedBackupFile))
            {
                return new Dictionary<string, BackupFile>(StringComparer.OrdinalIgnoreCase);
            }

            var encryptedData = File.ReadAllBytes(EncryptedBackupFile);
            var decryptedData = DecryptData(encryptedData);
            var backupData = JsonSerializer.Deserialize<BackupData>(Encoding.UTF8.GetString(decryptedData));

            var map = backupData?.Files?.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                      ?? new Dictionary<string, BackupFile>(StringComparer.OrdinalIgnoreCase);

            lock (_cacheLock)
            {
                _cachedBackupFiles = map;
            }
            return map;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "DataProtection", "Backup", $"读取旧备份包失败（将退化为全量备份）: {ex.Message}");
            return new Dictionary<string, BackupFile>(StringComparer.OrdinalIgnoreCase);
        }
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
                // 在恢复之前，先保存 init_state.json 的完整内容，防止备份覆盖导致初始化状态回退
                string? savedInitStateJson = null;
                var initStatePath = Path.Combine(DataDirectory, "init_state.json");
                try
                {
                    if (File.Exists(initStatePath))
                    {
                        savedInitStateJson = await File.ReadAllTextAsync(initStatePath);
                    }
                }
                catch { }

                var restored = await RestoreFromBackupAsync(backupData);
                if (restored)
                {
                    // 智能判断是否需要重载初始化状态
                    // 只有当备份数据比当前状态更"完成"时才重载
                    try
                    {
                        // 重载状态（此时磁盘上的 init_state.json 可能已被备份覆盖）
                        InitializationService.Instance.ReloadState();
                        var reloadedRequiresInit = InitializationService.Instance.RequiresInitialization;

                        // 如果恢复后需要初始化但恢复前我们保存了完成的 init_state.json
                        // 说明备份中的 init_state.json 是未完成状态，需要写回正确的版本
                        if (reloadedRequiresInit && savedInitStateJson != null)
                        {
                            LogService.Instance.Log("Warning", "DataProtection", "ReloadInit",
                                "恢复的数据是未完成初始化状态，正在修复...");
                            try
                            {
                                // 写回恢复前保存的正确 init_state.json
                                await File.WriteAllTextAsync(initStatePath, savedInitStateJson);
                                InitializationService.Instance.ReloadState();
                                LogService.Instance.Log("Info", "DataProtection", "ReloadInit",
                                    "初始化状态已修复为完成状态");
                            }
                            catch (Exception writeEx)
                            {
                                LogService.Instance.Log("Error", "DataProtection", "ReloadInit",
                                    $"修复 init_state.json 失败：{writeEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Warning", "DataProtection", "ReloadInit",
                            $"重新加载初始化状态失败：{ex.Message}");
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