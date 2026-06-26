using System;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public class AutomationSettingsModel
{
    [JsonPropertyName("isAutomationEnabled")]
    public bool IsAutomationEnabled { get; set; } = true;

    [JsonPropertyName("enableAutoShutdown")]
    public bool EnableAutoShutdown { get; set; } = false;

    [JsonPropertyName("autoShutdownTime")]
    public TimeSpan AutoShutdownTime { get; set; } = new TimeSpan(22, 0, 0);

    [JsonPropertyName("enableAutoRestart")]
    public bool EnableAutoRestart { get; set; } = false;

    [JsonPropertyName("autoRestartTime")]
    public TimeSpan AutoRestartTime { get; set; } = new TimeSpan(3, 0, 0);

    [JsonPropertyName("enableAutoLock")]
    public bool EnableAutoLock { get; set; } = false;

    [JsonPropertyName("autoLockTime")]
    public TimeSpan AutoLockTime { get; set; } = new TimeSpan(21, 30, 0);

    [JsonPropertyName("enableAutoNetworkLockOn")]
    public bool EnableAutoNetworkLockOn { get; set; } = false;

    [JsonPropertyName("autoNetworkLockOnTime")]
    public TimeSpan AutoNetworkLockOnTime { get; set; } = new TimeSpan(8, 0, 0);

    [JsonPropertyName("enableAutoNetworkLockOff")]
    public bool EnableAutoNetworkLockOff { get; set; } = false;

    [JsonPropertyName("autoNetworkLockOffTime")]
    public TimeSpan AutoNetworkLockOffTime { get; set; } = new TimeSpan(20, 0, 0);

    [JsonPropertyName("enableAutoAppBlockOn")]
    public bool EnableAutoAppBlockOn { get; set; } = false;

    [JsonPropertyName("autoAppBlockOnTime")]
    public TimeSpan AutoAppBlockOnTime { get; set; } = new TimeSpan(8, 0, 0);

    [JsonPropertyName("enableAutoAppBlockOff")]
    public bool EnableAutoAppBlockOff { get; set; } = false;

    [JsonPropertyName("autoAppBlockOffTime")]
    public TimeSpan AutoAppBlockOffTime { get; set; } = new TimeSpan(20, 0, 0);

    [JsonPropertyName("enableAutoWebcamCapture")]
    public bool EnableAutoWebcamCapture { get; set; } = false;

    [JsonPropertyName("autoWebcamCaptureTime")]
    public TimeSpan AutoWebcamCaptureTime { get; set; } = new TimeSpan(12, 0, 0);

    [JsonPropertyName("workflows")]
    public System.Collections.Generic.List<AutomationWorkflow> Workflows { get; set; } = new();

    [JsonPropertyName("schemes")]
    public System.Collections.Generic.List<string> Schemes { get; set; } = new() { "Default" };

    [JsonPropertyName("currentScheme")]
    public string CurrentScheme { get; set; } = "Default";
}
