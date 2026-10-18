using System;
using System.Collections.Generic;

namespace ClassScreenLock.Uninstaller.Models;

/// <summary>
/// 管理员登录验证模式（与主程序 SecuritySettingsModel 保持一致）
/// </summary>
public enum AdminLoginVerificationMode
{
    /// <summary>仅密码验证</summary>
    PasswordOnly = 0,

    /// <summary>仅双重验证码验证（2FA）</summary>
    TwoFactorOnly = 1,

    /// <summary>密码或双重验证码任一通过即可</summary>
    PasswordOrTwoFactor = 2,

    /// <summary>密码 + 双重验证码都必须通过</summary>
    PasswordAndTwoFactor = 3
}

/// <summary>
/// 安全设置模型（简化版，仅包含卸载验证所需字段）
/// 与主程序 ClassScreenLock.Models.SecuritySettingsModel 结构兼容，
/// 用于从 security.json 反序列化读取管理员凭据。
/// </summary>
public class SecuritySettingsModel
{
    public string AdminUsername { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime LastPasswordChange { get; set; }
    public DateTime LastLeakCheck { get; set; }

    public bool IsTwoFactorEnabled { get; set; }
    public string TwoFactorSecret { get; set; } = string.Empty;
    public AdminLoginVerificationMode LoginVerificationMode { get; set; }

    public int FailedCount { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public List<DateTime>? FailedAttempts { get; set; }

    public bool LeakDetected { get; set; }
}
