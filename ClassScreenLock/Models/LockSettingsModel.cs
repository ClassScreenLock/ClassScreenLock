using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace ClassScreenLock.Models;

public class LockSettingsModel
{
    [JsonPropertyName("lockTimeout")]
    public int LockTimeout { get; set; } = 30; // 秒

    [JsonPropertyName("enableBreakTimeLock")]
    public bool EnableBreakTimeLock { get; set; } = true;

    [JsonPropertyName("breakTimeLockMode")]
    public LockMode BreakTimeLockMode { get; set; } = LockMode.Full;

    [JsonPropertyName("autoUnlockBeforeClassMinutes")]
    public int AutoUnlockBeforeClassMinutes { get; set; } = 3;

    [JsonPropertyName("allowedTopmostApps")]
    public List<string> AllowedTopmostApps { get; set; } = new() { "Classisland" };

    [JsonPropertyName("forcedTopmostApps")]
    public List<string> ForcedTopmostApps { get; set; } = new();

    [JsonPropertyName("showFloatingLockWidget")]
    public bool ShowFloatingLockWidget { get; set; } = true;

    [JsonPropertyName("earlyUnlockMinAccountType")]
    public AccountType EarlyUnlockMinAccountType { get; set; } = AccountType.Admin;

    [JsonPropertyName("lockBackgroundOpacity")]
    public double LockBackgroundOpacity { get; set; } = 0.4;

    [JsonPropertyName("lockTextShadowOpacity")]
    public double LockTextShadowOpacity { get; set; } = 0.9;

    [JsonPropertyName("lockTextShadowBlurRadius")]
    public double LockTextShadowBlurRadius { get; set; } = 8.0;

    [JsonPropertyName("exitAppMinAccountType")]
    public AccountType? ExitAppMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarHomeMinAccountType")]
    public AccountType? SidebarHomeMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarLockSettingsMinAccountType")]
    public AccountType? SidebarLockSettingsMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarScheduleMinAccountType")]
    public AccountType? SidebarScheduleMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarAppManagementMinAccountType")]
    public AccountType? SidebarAppManagementMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarNetworkInterceptionMinAccountType")]
    public AccountType? SidebarNetworkInterceptionMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarSecurityLogsMinAccountType")]
    public AccountType? SidebarSecurityLogsMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarSecurityCenterMinAccountType")]
    public AccountType? SidebarSecurityCenterMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarSettingsMinAccountType")]
    public AccountType? SidebarSettingsMinAccountType { get; set; } = null;

    [JsonPropertyName("sidebarAboutMinAccountType")]
    public AccountType? SidebarAboutMinAccountType { get; set; } = null;
}
