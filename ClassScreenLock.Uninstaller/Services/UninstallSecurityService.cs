using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BCryptNet = BCrypt.Net.BCrypt;
using OtpNet;
using ClassScreenLock.Uninstaller.Helpers;
using ClassScreenLock.Uninstaller.Models;

namespace ClassScreenLock.Uninstaller.Services;

public enum UninstallVerificationStatus
{
    Success,
    InvalidCredentials,
    LockedOut,
    NotConfigured,
    Error
}

public class UninstallVerificationResult
{
    public UninstallVerificationStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RemainingAttempts { get; set; }
    public DateTime? LockoutUntil { get; set; }
}

/// <summary>
/// 卸载程序的管理员验证服务。
/// 
/// 验证逻辑与主程序 SecurityService.VerifyPasswordAsync 保持一致：
/// - 读取主程序 Data/security.json 中的密码哈希（BCrypt）和 2FA 配置
/// - 支持多种验证模式（仅密码 / 仅 2FA / 或 / 且）
/// - 支持账户锁定（10 次失败后锁定 30 分钟）
/// 
/// 注意：卸载程序只读取 security.json，不修改它，
/// 因为卸载完成后整个 Data 目录都会被删除。
/// 但为了与主程序行为一致，验证失败时仍会更新失败计数。
/// </summary>
public class UninstallSecurityService
{
    private const int MaxFailedAttempts = 10;
    private const int WorkFactor = 12;

    private static readonly Lazy<UninstallSecurityService> _instance = new(() => new UninstallSecurityService());
    public static UninstallSecurityService Instance => _instance.Value;

    private readonly object _lock = new();
    private SecuritySettingsModel? _settings;

    private UninstallSecurityService() { }

    /// <summary>
    /// 是否已配置管理员密码
    /// </summary>
    public bool IsPasswordConfigured
    {
        get
        {
            var settings = GetSettings();
            return !string.IsNullOrWhiteSpace(settings.PasswordHash);
        }
    }

    /// <summary>
    /// 是否已启用双重验证
    /// </summary>
    public bool IsTwoFactorEnabled
    {
        get
        {
            var settings = GetSettings();
            return settings.IsTwoFactorEnabled;
        }
    }

    /// <summary>
    /// 获取当前有效的登录验证模式
    /// （未启用 2FA 时始终为 PasswordOnly）
    /// </summary>
    public AdminLoginVerificationMode EffectiveLoginMode
    {
        get
        {
            var settings = GetSettings();
            return settings.IsTwoFactorEnabled
                ? settings.LoginVerificationMode
                : AdminLoginVerificationMode.PasswordOnly;
        }
    }

    /// <summary>
    /// 管理员用户名
    /// </summary>
    public string AdminUsername
    {
        get
        {
            var settings = GetSettings();
            return settings.AdminUsername;
        }
    }

    /// <summary>
    /// 验证管理员密码（与主程序 VerifyPasswordAsync 逻辑一致）
    /// </summary>
    public UninstallVerificationResult VerifyPassword(string username, string password, string? twoFactorCode = null)
    {
        try
        {
            var settings = GetSettings();

            if (string.IsNullOrWhiteSpace(settings.PasswordHash))
            {
                return new UninstallVerificationResult
                {
                    Status = UninstallVerificationStatus.NotConfigured,
                    Message = "尚未设置管理员密码"
                };
            }

            // 检查锁定状态
            if (settings.LockoutUntil.HasValue && settings.LockoutUntil.Value > DateTime.Now)
            {
                return new UninstallVerificationResult
                {
                    Status = UninstallVerificationStatus.LockedOut,
                    Message = "账户已被锁定，请稍后再试",
                    LockoutUntil = settings.LockoutUntil
                };
            }

            bool usernameMatches = string.Equals(settings.AdminUsername, username, StringComparison.OrdinalIgnoreCase);
            bool passwordMatches = usernameMatches &&
                                    !string.IsNullOrWhiteSpace(password) &&
                                    BCryptNet.Verify(password, settings.PasswordHash);
            bool twoFactorMatches = settings.IsTwoFactorEnabled &&
                                     !string.IsNullOrWhiteSpace(twoFactorCode) &&
                                     VerifyTwoFactorCode(settings, twoFactorCode);

            var mode = EffectiveLoginMode;
            bool success;
            string failureMessage;

            switch (mode)
            {
                case AdminLoginVerificationMode.PasswordOnly:
                    success = passwordMatches;
                    failureMessage = "用户名或密码错误";
                    break;
                case AdminLoginVerificationMode.TwoFactorOnly:
                    success = usernameMatches && twoFactorMatches;
                    failureMessage = "双重验证码不正确";
                    break;
                case AdminLoginVerificationMode.PasswordOrTwoFactor:
                    success = passwordMatches || (usernameMatches && twoFactorMatches);
                    failureMessage = "用户名、密码或双重验证码错误";
                    break;
                case AdminLoginVerificationMode.PasswordAndTwoFactor:
                default:
                    if (!passwordMatches)
                    {
                        success = false;
                        failureMessage = "用户名或密码错误";
                    }
                    else if (!twoFactorMatches)
                    {
                        success = false;
                        failureMessage = "双重验证码不正确";
                    }
                    else
                    {
                        success = true;
                        failureMessage = string.Empty;
                    }
                    break;
            }

            if (!success)
            {
                RegisterFailedAttempt(settings);
                var remaining = Math.Max(0, MaxFailedAttempts - settings.FailedCount);
                var lockedNow = settings.LockoutUntil.HasValue && settings.LockoutUntil.Value > DateTime.Now;
                return new UninstallVerificationResult
                {
                    Status = lockedNow ? UninstallVerificationStatus.LockedOut : UninstallVerificationStatus.InvalidCredentials,
                    Message = lockedNow ? "账户已被锁定，请稍后再试" : failureMessage,
                    RemainingAttempts = remaining,
                    LockoutUntil = settings.LockoutUntil
                };
            }

            // 验证成功，重置失败计数
            ResetFailedAttempts(settings);

            return new UninstallVerificationResult
            {
                Status = UninstallVerificationStatus.Success,
                Message = "验证成功",
                RemainingAttempts = MaxFailedAttempts
            };
        }
        catch (Exception ex)
        {
            return new UninstallVerificationResult
            {
                Status = UninstallVerificationStatus.Error,
                Message = $"验证过程中发生错误: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 验证双重验证码（TOTP）
    /// </summary>
    private bool VerifyTwoFactorCode(SecuritySettingsModel settings, string code)
    {
        if (!settings.IsTwoFactorEnabled || string.IsNullOrWhiteSpace(settings.TwoFactorSecret))
            return false;

        try
        {
            var secretBytes = Base32Encoding.ToBytes(settings.TwoFactorSecret);
            var totp = new Totp(secretBytes);
            return totp.VerifyTotp(code, out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 注册一次失败尝试（更新内存 + 写回文件）
    /// </summary>
    private void RegisterFailedAttempt(SecuritySettingsModel settings)
    {
        settings.FailedCount++;
        settings.FailedAttempts ??= [];
        settings.FailedAttempts.Insert(0, DateTime.Now);

        if (settings.FailedAttempts.Count > 50)
        {
            settings.FailedAttempts = settings.FailedAttempts.Take(50).ToList();
        }

        if (settings.FailedCount >= MaxFailedAttempts &&
            (!settings.LockoutUntil.HasValue || settings.LockoutUntil <= DateTime.Now))
        {
            settings.LockoutUntil = DateTime.Now.AddMinutes(30);
        }

        SaveSettings(settings);
    }

    /// <summary>
    /// 重置失败计数
    /// </summary>
    private void ResetFailedAttempts(SecuritySettingsModel settings)
    {
        settings.FailedCount = 0;
        settings.LockoutUntil = null;
        settings.FailedAttempts = [];
        SaveSettings(settings);
    }

    private SecuritySettingsModel GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    private SecuritySettingsModel LoadSettings()
    {
        try
        {
            var path = AppPathHelper.SecuritySettingsPath;
            if (!File.Exists(path))
            {
                return new SecuritySettingsModel();
            }

            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<SecuritySettingsModel>(json, options) ?? new SecuritySettingsModel();
        }
        catch
        {
            return new SecuritySettingsModel();
        }
    }

    private void SaveSettings(SecuritySettingsModel settings)
    {
        try
        {
            var path = AppPathHelper.SecuritySettingsPath;
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(settings, options);

            // 原子写入
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(path))
            {
                var backupPath = path + ".bak";
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(path, backupPath);
            }

            File.Move(tempPath, path);

            lock (_lock)
            {
                _settings = settings;
            }
        }
        catch
        {
            // 卸载过程中写入失败不阻塞流程
        }
    }
}
