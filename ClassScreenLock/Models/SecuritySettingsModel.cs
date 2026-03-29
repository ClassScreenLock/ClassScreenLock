using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public enum AdminLoginVerificationMode
{
    PasswordAndTwoFactor = 0,
    PasswordOnly = 1,
    TwoFactorOnly = 2,
    PasswordOrTwoFactor = 3
}

public class SecuritySettingsModel
{
    [JsonPropertyName("adminUsername")]
    public string AdminUsername { get; set; } = "admin";

    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }
        = 0;

    [JsonPropertyName("lockoutUntil")]
    public DateTime? LockoutUntil { get; set; }
        = null;

    [JsonPropertyName("lastPasswordChange")]
    public DateTime? LastPasswordChange { get; set; }
        = null;

    [JsonPropertyName("lastLeakCheck")]
    public DateTime? LastLeakCheck { get; set; }
        = null;

    [JsonPropertyName("leakDetected")]
    public bool LeakDetected { get; set; }
        = false;

    [JsonPropertyName("isTwoFactorEnabled")]
    public bool IsTwoFactorEnabled { get; set; } = false;

    [JsonPropertyName("twoFactorSecret")]
    public string TwoFactorSecret { get; set; } = string.Empty;

    [JsonPropertyName("loginVerificationMode")]
    public AdminLoginVerificationMode LoginVerificationMode { get; set; } = AdminLoginVerificationMode.PasswordAndTwoFactor;

    [JsonPropertyName("failedAttempts")]
    public List<DateTime> FailedAttempts { get; set; } = new();

    [JsonPropertyName("enableSoftwareSecurity")]
    public bool EnableSoftwareSecurity { get; set; } = true;
}
