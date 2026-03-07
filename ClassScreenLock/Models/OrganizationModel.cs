using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public class OrganizationModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("joinedAt")]
    public DateTime? JoinedAt { get; set; }

    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("lastSyncTime")]
    public DateTime? LastSyncTime { get; set; }

    [JsonPropertyName("contactPhone")]
    public string ContactPhone { get; set; } = string.Empty;

    [JsonPropertyName("className")]
    public string ClassName { get; set; } = string.Empty;

    [JsonPropertyName("personInCharge")]
    public string PersonInCharge { get; set; } = string.Empty;

    [JsonPropertyName("securityConfig")]
    public SecurityConfiguration? SecurityConfig { get; set; }

    [JsonPropertyName("networkConfig")]
    public NetworkConfiguration? NetworkConfig { get; set; }
}

public class SecurityConfiguration
{
    [JsonPropertyName("admin")]
    public AdminConfig Admin { get; set; } = new();

    [JsonPropertyName("accounts")]
    public AccountConfig[] Accounts { get; set; } = Array.Empty<AccountConfig>();

    [JsonPropertyName("security")]
    public SecuritySettings Security { get; set; } = new();

    [JsonPropertyName("lockSettings")]
    public LockSettings LockSettings { get; set; } = new();

    [JsonPropertyName("permissions")]
    public PermissionsConfig Permissions { get; set; } = new();

    [JsonPropertyName("screenLock")]
    public ScreenLockConfig ScreenLock { get; set; } = new();

    [JsonPropertyName("syncInterval")]
    public int SyncInterval { get; set; } = 30;

    [JsonPropertyName("processManagement")]
    public ProcessManagementConfig ProcessManagement { get; set; } = new();

    [JsonPropertyName("usbControl")]
    public UsbControlConfig UsbControl { get; set; } = new();

    [JsonPropertyName("appWhitelist")]
    public AppWhitelistConfig AppWhitelist { get; set; } = new();

    [JsonPropertyName("screenshotControl")]
    public ScreenshotControlConfig ScreenshotControl { get; set; } = new();
}

public class AdminConfig
{
    [JsonPropertyName("adminUsername")]
    public string AdminUsername { get; set; } = "admin";

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public class AccountConfig
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("accountType")]
        public int AccountType { get; set; } = 0;

        [JsonPropertyName("isTwoFactorEnabled")]
        public bool IsTwoFactorEnabled { get; set; } = false;

        [JsonPropertyName("twoFactorSecret")]
        public string TwoFactorSecret { get; set; } = string.Empty;
    }

public class SecuritySettings
{
    [JsonPropertyName("isTwoFactorEnabled")]
    public bool IsTwoFactorEnabled { get; set; } = false;

    [JsonPropertyName("twoFactorSecret")]
    public string TwoFactorSecret { get; set; } = string.Empty;

    [JsonPropertyName("loginVerificationMode")]
    public int LoginVerificationMode { get; set; } = 0;
}

public class LockSettings
{
    [JsonPropertyName("lockTimeout")]
    public int LockTimeout { get; set; } = 30;

    [JsonPropertyName("enableBreakTimeLock")]
    public bool EnableBreakTimeLock { get; set; } = true;

    [JsonPropertyName("breakTimeLockMode")]
    public int BreakTimeLockMode { get; set; } = 2;

    [JsonPropertyName("autoUnlockBeforeClassMinutes")]
    public int AutoUnlockBeforeClassMinutes { get; set; } = 3;

    [JsonPropertyName("showFloatingLockWidget")]
    public bool ShowFloatingLockWidget { get; set; } = true;

    [JsonPropertyName("earlyUnlockMinAccountType")]
    public int EarlyUnlockMinAccountType { get; set; } = 2;

    [JsonPropertyName("lockBackgroundOpacity")]
    public double LockBackgroundOpacity { get; set; } = 0.1;

    [JsonPropertyName("lockTextShadowOpacity")]
    public double LockTextShadowOpacity { get; set; } = 0.3;

    [JsonPropertyName("lockTextShadowBlurRadius")]
    public int LockTextShadowBlurRadius { get; set; } = 16;
}

public class PermissionsConfig
{
    [JsonPropertyName("exitAppMinAccountType")]
    public int ExitAppMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarHomeMinAccountType")]
    public int SidebarHomeMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarLockSettingsMinAccountType")]
    public int SidebarLockSettingsMinAccountType { get; set; } = 0;

    [JsonPropertyName("breakTimeLockSettingsMinAccountType")]
    public int BreakTimeLockSettingsMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarScheduleMinAccountType")]
    public int SidebarScheduleMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarAppManagementMinAccountType")]
    public int SidebarAppManagementMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarNetworkInterceptionMinAccountType")]
    public int SidebarNetworkInterceptionMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarSecurityLogsMinAccountType")]
    public int SidebarSecurityLogsMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarScreenshotHistoryMinAccountType")]
    public int SidebarScreenshotHistoryMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarWebcamHistoryMinAccountType")]
    public int SidebarWebcamHistoryMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarAutomationMinAccountType")]
    public int SidebarAutomationMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarSecurityCenterMinAccountType")]
    public int SidebarSecurityCenterMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarSettingsMinAccountType")]
    public int SidebarSettingsMinAccountType { get; set; } = 0;

    [JsonPropertyName("sidebarAboutMinAccountType")]
    public int SidebarAboutMinAccountType { get; set; } = 0;

    [JsonPropertyName("earlyUnlockMinAccountType")]
    public int EarlyUnlockMinAccountType { get; set; } = 2;
}

public class ScreenLockConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "full";

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 30;
}

public class ProcessManagementConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("blockedProcesses")]
    public string BlockedProcesses { get; set; } = string.Empty;
}

public class UsbControlConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("blockStorage")]
    public bool BlockStorage { get; set; } = true;

    [JsonPropertyName("blockKeyboard")]
    public bool BlockKeyboard { get; set; } = false;

    [JsonPropertyName("blockMouse")]
    public bool BlockMouse { get; set; } = false;
}

public class AppWhitelistConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("allowedApps")]
    public string AllowedApps { get; set; } = string.Empty;
}

public class ScreenshotControlConfig
{
    [JsonPropertyName("blockScreenshot")]
    public bool BlockScreenshot { get; set; } = true;

    [JsonPropertyName("blockScreenRecord")]
    public bool BlockScreenRecord { get; set; } = true;
}

public class NetworkConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "blacklist";

    [JsonPropertyName("domainRules")]
    public List<DomainRule> DomainRules { get; set; } = new();
}

public class DomainRule
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}


