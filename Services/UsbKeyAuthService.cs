using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

/// <summary>
/// USB密钥认证服务
/// 每个账户可配置自己的USB密钥，插入对应U盘即可免密码登录该账户
/// </summary>
public class UsbKeyAuthService : IDisposable
{
    private const string KeyFileName = "classscreenlock_usbkey.dat";
    private const int PollIntervalMs = 2000;

    private static readonly string DataDirectory = Path.Combine(Helpers.AppPathHelper.AppDirectory, "Data");
    private static readonly string ConfigPath = Path.Combine(DataDirectory, "usb_keys.json");

    private static readonly Lazy<UsbKeyAuthService> _instance = new(() => new UsbKeyAuthService());
    public static UsbKeyAuthService Instance => _instance.Value;

    private readonly object _lock = new();
    private List<UsbKeyModel> _keys = new();
    private Timer? _pollTimer;
    private readonly HashSet<string> _activeDriveLetters = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRunning;
    private bool _disposed;

    public bool IsUsbKeyAuthenticated { get; private set; }
    public AccountType? CurrentUsbKeyAccountType { get; private set; }
    public string? CurrentUsbKeyLabel { get; private set; }
    public string? CurrentUsbKeyUsername { get; private set; }

    public IReadOnlyList<UsbKeyModel> Keys
    {
        get { lock (_lock) { return _keys.ToList(); } }
    }

    public event EventHandler<UsbKeyStateChangedEventArgs>? StateChanged;

    private UsbKeyAuthService()
    {
        if (!Directory.Exists(DataDirectory))
            Directory.CreateDirectory(DataDirectory);
        LoadKeys();
        RefreshActiveDrives();
    }

    public void Start()
    {
        if (_disposed || _isRunning) return;
        _isRunning = true;
        _pollTimer = new Timer(OnPollTimerTick, null, PollIntervalMs, PollIntervalMs);
        LogService.Instance.Log("UsbKey", "Started", "System", "USB密钥监控已启动");
    }

    public void Stop()
    {
        _isRunning = false;
        _pollTimer?.Dispose();
        _pollTimer = null;
        LogService.Instance.Log("UsbKey", "Stopped", "System", "USB密钥监控已停止");
    }

    /// <summary>
    /// 注册USB密钥 - 绑定到当前登录账户
    /// </summary>
    public (bool success, string message) RegisterKey(string driveLetter, string label, AccountModel account)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
            return (false, "请指定驱动器盘符");

        var rootPath = $"{driveLetter}:\\";
        if (!Directory.Exists(rootPath))
            return (false, $"驱动器 {driveLetter}: 不存在");

        var token = GenerateToken();
        var tokenPath = Path.Combine(rootPath, KeyFileName);

        try
        {
            File.WriteAllText(tokenPath, Convert.ToBase64String(token), Encoding.UTF8);
            File.SetAttributes(tokenPath, FileAttributes.Hidden | FileAttributes.System);
        }
        catch (Exception ex)
        {
            return (false, $"无法写入密钥文件到U盘: {ex.Message}");
        }

        var tokenHash = ComputeHash(token);
        var volumeSerial = GetVolumeSerial(driveLetter);

        lock (_lock)
        {
            // 检查同一账户是否已有此U盘的密钥
            var existing = _keys.FirstOrDefault(k =>
                k.AccountId == account.Id &&
                k.DriveLetter.Equals(driveLetter, StringComparison.OrdinalIgnoreCase) &&
                k.VolumeSerial == volumeSerial);
            if (existing != null)
                return (false, $"该U盘已为此账户注册（\"{existing.Label}\"），请先撤销");

            var key = new UsbKeyModel
            {
                Label = label,
                AccountId = account.Id,
                AccountUsername = account.Username,
                AccountType = account.AccountType,
                TokenHash = tokenHash,
                DriveLetter = driveLetter.ToUpper(),
                VolumeSerial = volumeSerial
            };

            _keys.Add(key);
            SaveKeys();
        }

        LogService.Instance.Log("UsbKey", "Registered", account.Username,
            $"USB密钥已注册: {driveLetter}: -> {account.Username}({account.AccountType})");
        return (true, $"USB密钥 \"{label}\"（{driveLetter}:）已绑定到账户 {account.Username}");
    }

    /// <summary>
    /// 撤销USB密钥（仅账户本人可撤销自己的密钥）
    /// </summary>
    public (bool success, string message) RevokeKey(string keyId, Guid? accountId = null)
    {
        lock (_lock)
        {
            var key = _keys.FirstOrDefault(k => k.Id == keyId);
            if (key == null)
                return (false, "密钥不存在");

            // 如果指定了账户ID，验证是否为密钥所有者
            if (accountId.HasValue && key.AccountId != accountId.Value)
                return (false, "无权撤销其他账户的USB密钥");

            _keys.Remove(key);
            SaveKeys();

            if (IsUsbKeyAuthenticated && CurrentUsbKeyLabel == key.Label)
                LogoutUsbKeySession();
        }

        LogService.Instance.Log("UsbKey", "Revoked", keyId, "USB密钥已撤销");
        return (true, "USB密钥已撤销");
    }

    /// <summary>
    /// 获取指定账户的USB密钥列表
    /// </summary>
    public List<UsbKeyModel> GetKeysForAccount(Guid accountId)
    {
        lock (_lock)
        {
            return _keys.Where(k => k.AccountId == accountId).ToList();
        }
    }

    public void RefreshActiveDrives()
    {
        var activeDrives = GetRemovableDrives();
        var currentDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            foreach (var drive in activeDrives)
            {
                currentDrives.Add(drive);
                if (!_activeDriveLetters.Contains(drive))
                    HandleDriveInserted(drive);
            }

            var removed = _activeDriveLetters.Where(d => !currentDrives.Contains(d)).ToList();
            foreach (var drive in removed)
                HandleDriveRemoved(drive);

            _activeDriveLetters.Clear();
            foreach (var d in currentDrives)
                _activeDriveLetters.Add(d);
        }
    }

    public void LogoutUsbKeySession()
    {
        if (!IsUsbKeyAuthenticated) return;

        var label = CurrentUsbKeyLabel;
        IsUsbKeyAuthenticated = false;
        CurrentUsbKeyAccountType = null;
        CurrentUsbKeyLabel = null;
        CurrentUsbKeyUsername = null;

        AccountService.Instance.LogoutUsbKey();

        LogService.Instance.Log("UsbKey", "SessionEnded", label ?? "未知", "USB密钥会话已结束");
        OnStateChanged(new UsbKeyStateChangedEventArgs(UsbKeyState.Removed, null, label));
    }

    private void OnPollTimerTick(object? state)
    {
        if (!_isRunning || _disposed) return;
        try { RefreshActiveDrives(); }
        catch (Exception ex)
        {
            LogService.Instance.Log("UsbKey", "PollError", "System", ex.Message);
        }
    }

    private void HandleDriveInserted(string driveLetter)
    {
        var key = TryAuthenticateDrive(driveLetter);
        if (key == null) return;

        // 防止双登录：如果已有非USB会话登录，不自动切换
        var currentAccount = AccountService.Instance.CurrentAccount;
        if (currentAccount != null && !AccountService.Instance.IsUsbKeySession)
        {
            LogService.Instance.Log("UsbKey", "Blocked", key.Label,
                $"已有账户 {currentAccount.Username} 登录，跳过USB自动认证");
            return;
        }

        key.LastUsedAt = DateTime.Now;
        SaveKeys();

        IsUsbKeyAuthenticated = true;
        CurrentUsbKeyAccountType = key.AccountType;
        CurrentUsbKeyLabel = key.Label;
        CurrentUsbKeyUsername = key.AccountUsername;

        AccountService.Instance.LoginWithUsbKey(key);

        LogService.Instance.Log("UsbKey", "Authenticated", key.AccountUsername,
            $"USB密钥认证成功: {driveLetter}: -> {key.AccountUsername}({key.AccountType})");
        OnStateChanged(new UsbKeyStateChangedEventArgs(UsbKeyState.Authenticated, key.AccountType, key.Label));
    }

    private void HandleDriveRemoved(string driveLetter)
    {
        if (!IsUsbKeyAuthenticated) return;

        lock (_lock)
        {
            var currentKey = _keys.FirstOrDefault(k =>
                k.DriveLetter.Equals(driveLetter, StringComparison.OrdinalIgnoreCase));
            if (currentKey != null && currentKey.Label == CurrentUsbKeyLabel)
                LogoutUsbKeySession();
        }
    }

    public UsbKeyModel? TryAuthenticateDrive(string driveLetter)
    {
        var rootPath = $"{driveLetter}:\\";
        var tokenPath = Path.Combine(rootPath, KeyFileName);

        if (!File.Exists(tokenPath)) return null;

        try
        {
            var tokenBase64 = File.ReadAllText(tokenPath, Encoding.UTF8).Trim();
            var token = Convert.FromBase64String(tokenBase64);
            var tokenHash = ComputeHash(token);
            var volumeSerial = GetVolumeSerial(driveLetter);

            lock (_lock)
            {
                return _keys.FirstOrDefault(k =>
                    k.TokenHash == tokenHash &&
                    k.VolumeSerial == volumeSerial);
            }
        }
        catch
        {
            return null;
        }
    }

    private static byte[] GenerateToken()
    {
        var token = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(token);
        return token;
    }

    private static string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToBase64String(hash);
    }

    private static string GetVolumeSerial(string driveLetter)
    {
        try
        {
            var drive = new DriveInfo($"{driveLetter}:\\");
            return drive.VolumeLabel ?? drive.DriveFormat ?? "UNKNOWN";
        }
        catch { return "UNKNOWN"; }
    }

    private static List<string> GetRemovableDrives()
    {
        var drives = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Removable && drive.IsReady)
                    drives.Add(drive.Name.TrimEnd('\\', ':'));
            }
        }
        catch { }
        return drives;
    }

    private void LoadKeys()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                _keys = JsonSerializer.Deserialize<List<UsbKeyModel>>(json) ?? new();
            }
            else _keys = new();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("UsbKey", "LoadError", "System", ex.Message);
            _keys = new();
        }
    }

    private void SaveKeys()
    {
        try
        {
            var json = JsonSerializer.Serialize(_keys, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("UsbKey", "SaveError", "System", ex.Message);
        }
    }

    private void OnStateChanged(UsbKeyStateChangedEventArgs args)
    {
        StateChanged?.Invoke(this, args);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isRunning = false;
        _pollTimer?.Dispose();
        _pollTimer = null;
    }
}

public enum UsbKeyState { Authenticated, Removed }

public class UsbKeyStateChangedEventArgs : EventArgs
{
    public UsbKeyState State { get; }
    public AccountType? AccountType { get; }
    public string? Label { get; }

    public UsbKeyStateChangedEventArgs(UsbKeyState state, AccountType? accountType, string? label)
    {
        State = state;
        AccountType = accountType;
        Label = label;
    }
}
