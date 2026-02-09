using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace ClassScreenLock.Models;

public class SoftwareBlockageModel
{
    [JsonPropertyName("blockedRules")]
    public List<string> BlockedRules { get; set; } = new();

    [JsonPropertyName("blockedFileAclBackup")]
    public Dictionary<string, string> BlockedFileAclBackup { get; set; } = new();

    [JsonPropertyName("isNetworkLockEnabled")]
    public bool IsNetworkLockEnabled { get; set; } = false;

    [JsonPropertyName("isAppBlockingEnabled")]
    public bool IsAppBlockingEnabled { get; set; } = true;

    [JsonPropertyName("isBasicProtectionEnabled")]
    public bool IsBasicProtectionEnabled { get; set; } = true;

    [JsonPropertyName("protectionRules")]
    public List<ProtectionRule> ProtectionRules { get; set; } = new()
     {
         new ProtectionRule { Name = "任务管理器", ProcessNames = new List<string> { "Taskmgr" }, IsEnabled = true },
         new ProtectionRule { Name = "注册表编辑器", ProcessNames = new List<string> { "regedit" }, IsEnabled = true },
         new ProtectionRule { Name = "命令提示符", ProcessNames = new List<string> { "cmd" }, IsEnabled = true },
         new ProtectionRule { Name = "PowerShell", ProcessNames = new List<string> { "powershell" }, IsEnabled = true },
         new ProtectionRule { Name = "控制面板", ProcessNames = new List<string> { "control" }, IsEnabled = true }
     };
}
