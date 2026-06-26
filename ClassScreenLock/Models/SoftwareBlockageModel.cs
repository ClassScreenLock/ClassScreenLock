using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace ClassScreenLock.Models;

public class SoftwareBlockageModel
{
    // 遗留字段：旧版本使用 List<string> 存储阻止规则（未区分 name/path）。
    // 加载时若发现该字段非空，会被一次性迁移到 BlockedRulesTyped（按 LooksLikePath 自动推断类型），
    // 然后清空并保存。读取方应统一使用 GetEffectiveBlockedRules() 拿到合并后的强类型集合。
    [JsonPropertyName("blockedRules")]
    public List<string> BlockedRules { get; set; } = new();

    [JsonPropertyName("blockedRulesTyped")]
    public List<BlockedRule> BlockedRulesTyped { get; set; } = new();

    /// <summary>
    /// 返回合并后的强类型规则集合。
    /// </summary>
    public List<BlockedRule> GetEffectiveBlockedRules()
    {
        var result = new List<BlockedRule>();
        if (BlockedRulesTyped != null) result.AddRange(BlockedRulesTyped);
        return result;
    }

    [JsonPropertyName("blockedFileAclBackup")]
    public Dictionary<string, string> BlockedFileAclBackup { get; set; } = new();

    [JsonPropertyName("blockedFileHashes")]
    public Dictionary<string, string> BlockedFileHashes { get; set; } = new();

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

    [JsonPropertyName("customBrowserProcesses")]
    public List<string> CustomBrowserProcesses { get; set; } = new();

    [JsonPropertyName("enableChromiumAutoDetection")]
    public bool EnableChromiumAutoDetection { get; set; } = true;
}
