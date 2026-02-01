using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Globalization;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class SettingsService
{
    private static readonly string DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string GeneralSettingsPath = Path.Combine(DataDirectory, "settings.json");
    private static readonly string LockSettingsPath = Path.Combine(DataDirectory, "locksettings.json");
    private static readonly string ScreenshotSettingsPath = Path.Combine(DataDirectory, "screenshotsettings.json");
    private static readonly string BlockageSettingsPath = Path.Combine(DataDirectory, "softwareblockage.json");
    private static readonly string OldLockSettingsPath = Path.Combine(DataDirectory, "LockSettings.json");

    private static SettingsModel? _general;
    private static LockSettingsModel? _lock;
    private static ScreenshotSettingsModel? _screenshot;
    private static SoftwareBlockageModel? _blockage;

    public static SettingsModel General => _general ??= LoadSettings<SettingsModel>(GeneralSettingsPath);
    public static LockSettingsModel Lock => _lock ??= LoadSettings<LockSettingsModel>(LockSettingsPath);
    public static ScreenshotSettingsModel Screenshot => _screenshot ??= LoadSettings<ScreenshotSettingsModel>(ScreenshotSettingsPath);
    public static SoftwareBlockageModel Blockage => _blockage ??= LoadSettings<SoftwareBlockageModel>(BlockageSettingsPath);

    static SettingsService()
    {
        EnsureDirectoryExists();
        MigrateIfNeeded();
    }

    private static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(DataDirectory))
        {
            Directory.CreateDirectory(DataDirectory);
        }
    }

    private static void MigrateIfNeeded()
    {
        // 如果旧文件存在且新文件不存在，进行迁移
        if (File.Exists(OldLockSettingsPath) && !File.Exists(LockSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(OldLockSettingsPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var oldSettings = JsonSerializer.Deserialize<JsonElement>(json, options);

                // 迁移到 General
                var general = new SettingsModel();
                if (oldSettings.TryGetProperty("fontSize", out var fontSize)) general.FontSize = fontSize.GetDouble();
                if (oldSettings.TryGetProperty("fontFamily", out var fontFamily)) general.FontFamily = fontFamily.GetString() ?? general.FontFamily;
                if (oldSettings.TryGetProperty("autoStart", out var autoStart)) general.AutoStart = autoStart.GetBoolean();
                if (oldSettings.TryGetProperty("darkMode", out var darkMode)) general.DarkMode = darkMode.GetBoolean();
                if (oldSettings.TryGetProperty("accentColor", out var accentColor)) general.AccentColor = accentColor.GetString() ?? general.AccentColor;
                if (oldSettings.TryGetProperty("showNotifications", out var showNotifications)) general.ShowNotifications = showNotifications.GetBoolean();
                if (oldSettings.TryGetProperty("language", out var language)) general.Language = language.GetString() ?? general.Language;
                if (oldSettings.TryGetProperty("useSystemAccentColor", out var useSystemAccentColor)) general.UseSystemAccentColor = useSystemAccentColor.GetBoolean();
                SaveGeneral(general);

                // 迁移到 Lock
                var lockSet = new LockSettingsModel();
                if (oldSettings.TryGetProperty("lockTimeout", out var lockTimeout)) lockSet.LockTimeout = lockTimeout.GetInt32();
                if (oldSettings.TryGetProperty("enableBreakTimeLock", out var enableBreakTimeLock)) lockSet.EnableBreakTimeLock = enableBreakTimeLock.GetBoolean();
                if (oldSettings.TryGetProperty("breakTimeLockMode", out var breakTimeLockMode)) lockSet.BreakTimeLockMode = (LockMode)breakTimeLockMode.GetInt32();
                if (oldSettings.TryGetProperty("autoUnlockBeforeClassMinutes", out var autoUnlock)) lockSet.AutoUnlockBeforeClassMinutes = autoUnlock.GetInt32();
                if (oldSettings.TryGetProperty("allowedTopmostApps", out var allowedTopmost)) lockSet.AllowedTopmostApps = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(allowedTopmost.GetRawText()) ?? lockSet.AllowedTopmostApps;
                if (oldSettings.TryGetProperty("forcedTopmostApps", out var forcedTopmost)) lockSet.ForcedTopmostApps = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(forcedTopmost.GetRawText()) ?? lockSet.ForcedTopmostApps;
                if (oldSettings.TryGetProperty("showFloatingLockWidget", out var showFloating)) lockSet.ShowFloatingLockWidget = showFloating.GetBoolean();
                SaveLock(lockSet);

                // 迁移到 Blockage
                var blockage = new SoftwareBlockageModel();
                if (oldSettings.TryGetProperty("allowedApps", out var allowedApps)) blockage.AllowedApps = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(allowedApps.GetRawText()) ?? blockage.AllowedApps;
                if (oldSettings.TryGetProperty("blockedRules", out var blockedRules)) blockage.BlockedRules = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(blockedRules.GetRawText()) ?? blockage.BlockedRules;
                if (oldSettings.TryGetProperty("isNetworkLockEnabled", out var netLock)) blockage.IsNetworkLockEnabled = netLock.GetBoolean();
                if (oldSettings.TryGetProperty("isAppBlockingEnabled", out var appBlock)) blockage.IsAppBlockingEnabled = appBlock.GetBoolean();
                if (oldSettings.TryGetProperty("isBasicProtectionEnabled", out var basicProt)) blockage.IsBasicProtectionEnabled = basicProt.GetBoolean();
                if (oldSettings.TryGetProperty("protectionRules", out var protRules)) blockage.ProtectionRules = JsonSerializer.Deserialize<System.Collections.Generic.List<ProtectionRule>>(protRules.GetRawText()) ?? blockage.ProtectionRules;
                SaveBlockage(blockage);

                // 备份并删除旧文件
                File.Move(OldLockSettingsPath, OldLockSettingsPath + ".bak", true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"迁移设置失败: {ex.Message}");
            }
        }
    }

    private static T LoadSettings<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path))
            {
                var defaultVal = new T();
                if (defaultVal is SettingsModel general)
                {
                    general.Language = ResolveDefaultLanguage();
                }
                SaveSettingsInternal(path, defaultVal);
                return defaultVal;
            }

            if (!VerifyIntegrity(path))
            {
                System.Diagnostics.Debug.WriteLine($"文件完整性校验失败: {path}，尝试加载备份...");
                var backupPath = path + ".bak";
                if (File.Exists(backupPath))
                {
                    var backupJson = File.ReadAllText(backupPath);
                    var backupVal = JsonSerializer.Deserialize<T>(backupJson);
                    if (backupVal != null) return backupVal;
                }
            }

            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<T>(json, options) ?? new T();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载设置失败 ({path}): {ex.Message}");
            return new T();
        }
    }

    public static void SaveGeneral(SettingsModel settings) => SaveSettingsInternal(GeneralSettingsPath, settings);
    public static void SaveLock(LockSettingsModel settings) => SaveSettingsInternal(LockSettingsPath, settings);
    public static void SaveScreenshot(ScreenshotSettingsModel settings) => SaveSettingsInternal(ScreenshotSettingsPath, settings);
    public static void SaveBlockage(SoftwareBlockageModel settings) => SaveSettingsInternal(BlockageSettingsPath, settings);

    private static void SaveSettingsInternal<T>(string path, T settings)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(settings, options);
            
            // 原子写入
            AtomicWrite(path, json);
            
            // 更新哈希校验文件
            UpdateIntegrityHash(path, json);

            // 更新当前内存缓存
            if (path == GeneralSettingsPath) _general = settings as SettingsModel;
            else if (path == LockSettingsPath) _lock = settings as LockSettingsModel;
            else if (path == ScreenshotSettingsPath) _screenshot = settings as ScreenshotSettingsModel;
            else if (path == BlockageSettingsPath) _blockage = settings as SoftwareBlockageModel;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存设置失败 ({path}): {ex.Message}");
        }
    }

    private static void AtomicWrite(string path, string content)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        if (File.Exists(path))
        {
            var backupPath = path + ".bak";
            // File.Replace 在某些环境下可能不稳定，使用简单的 Move 替代
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Copy(path, backupPath);
            File.Delete(path);
            File.Move(tempPath, path);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static void UpdateIntegrityHash(string path, string content)
    {
        var hashPath = path + ".hash";
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
        File.WriteAllText(hashPath, BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());
    }

    private static bool VerifyIntegrity(string path)
    {
        var hashPath = path + ".hash";
        if (!File.Exists(hashPath)) return true; // 如果没有哈希文件，暂时认为有效

        try
        {
            var expectedHash = File.ReadAllText(hashPath).Trim();
            var content = File.ReadAllText(path);
            using var md5 = MD5.Create();
            var actualHash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(content))).Replace("-", "").ToLowerInvariant();
            return expectedHash == actualHash;
        }
        catch { return false; }
    }

    public static void UpdateGeneral(Action<SettingsModel> action) { action(General); SaveGeneral(General); }
    public static void UpdateLock(Action<LockSettingsModel> action) { action(Lock); SaveLock(Lock); }
    public static void UpdateBlockage(Action<SoftwareBlockageModel> action) { action(Blockage); SaveBlockage(Blockage); }

    private static string ResolveDefaultLanguage()
    {
        try
        {
            var name = CultureInfo.CurrentUICulture?.Name ?? string.Empty;
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en-US";
        }
        catch
        {
        }

        return "en-US";
    }
}
