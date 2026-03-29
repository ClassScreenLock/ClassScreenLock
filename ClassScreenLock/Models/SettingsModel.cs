using System;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public class SettingsModel
{
    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 14;
    
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    
    [JsonPropertyName("darkMode")]
    public bool DarkMode { get; set; } = false;
    
    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#0078D4";
    
    [JsonPropertyName("showNotifications")]
    public bool ShowNotifications { get; set; } = true;
    
    [JsonPropertyName("language")]
    public string Language { get; set; } = "zh-CN";
    
    [JsonPropertyName("useSystemAccentColor")]
    public bool UseSystemAccentColor { get; set; } = false;

    [JsonPropertyName("notificationPosition")]
    public NotificationPosition NotificationPosition { get; set; } = NotificationPosition.Center;

    [JsonPropertyName("weeklyCycleCount")]
    public int WeeklyCycleCount { get; set; } = 4;

    [JsonPropertyName("termStartDate")]
    public DateTime? TermStartDate { get; set; }

    [JsonPropertyName("automationSchemes")]
    public System.Collections.Generic.List<string> AutomationSchemes { get; set; } = new() { "Default" };

    [JsonPropertyName("currentAutomationScheme")]
    public string CurrentAutomationScheme { get; set; } = "Default";

    [JsonPropertyName("automationConfigs")]
    public System.Collections.Generic.List<string> AutomationConfigs { get; set; } = new() { "Default" };

    [JsonPropertyName("currentAutomationConfig")]
    public string CurrentAutomationConfig { get; set; } = "Default";

    [JsonPropertyName("autoStartServices")]
    public bool AutoStartServices { get; set; } = true;

    [JsonPropertyName("maxLockDurationHours")]
    public int MaxLockDurationHours { get; set; } = 48;
}
