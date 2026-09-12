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

    /// <summary>
    /// 是否已经向用户展示过"首次开启基础防护前请备份注册表"的警告并被确认。
    /// 首次开启基础防护时弹出注册表备份提醒，用户确认后置为 true，之后不再重复弹出该首次提醒。
    /// </summary>
    [JsonPropertyName("hasConfirmedBasicProtectionWarning")]
    public bool HasConfirmedBasicProtectionWarning { get; set; } = false;

    /// <summary>
    /// 安全中心登录账户的最低放行权限等级（AccountType 数值：0=仅超管，1=管理员及以上，2=所有登录账户）。
    /// 当前登录安全中心的账户权限等级（数值）不高于该值时，直接放行所有被拦截的进程
    /// （CMD / PowerShell / ISE / 任务管理器 / 注册表编辑器等，IFEO 劫持与轮询/WMI 拦截均放行）。
    /// 默认 1：管理员及以上登录即放行。
    /// </summary>
    [JsonPropertyName("minimumAllowedAccountType")]
    public int MinimumAllowedAccountType { get; set; } = 1;

    /// <summary>
    /// MITM 深度流量检查开关（Titanium.Web.Proxy 本地代理，解密 HTTPS 并实时检查）。
    /// 开启时作为网络拦截的首选机制，旧的 hosts/防火墙/窗口扫描作为兜底保留。
    /// </summary>
    [JsonPropertyName("isMitmInterceptionEnabled")]
    public bool IsMitmInterceptionEnabled { get; set; } = false;

    /// <summary>
    /// MITM 本地代理监听端口。
    /// </summary>
    [JsonPropertyName("mitmProxyPort")]
    public int MitmProxyPort { get; set; } = 8391;

    /// <summary>
    /// 基础防护规则的当前版本号。旧配置（无此字段）默认为 0。
    /// 加载时若磁盘版本低于 <see cref="CurrentProtectionRulesVersion"/>，
    /// 会用 <see cref="CreateDefaultProtectionRules"/> 的规范集覆盖旧规则
    /// （保留同名规则的开关状态），从根本上解决"新旧两版规则并存 / 条目数量忽多忽少"的问题。
    /// </summary>
    [JsonPropertyName("protectionRulesVersion")]
    public int ProtectionRulesVersion { get; set; } = 0;

    /// <summary>
    /// 基础防护规则的规范版本号。规则集有实质性变更时递增此常量即可触发一次性迁移。
    /// </summary>
    public const int CurrentProtectionRulesVersion = 3;

    [JsonPropertyName("protectionRules")]
    public List<ProtectionRule> ProtectionRules { get; set; } = CreateDefaultProtectionRules();

    /// <summary>
    /// 基础防护规则的唯一权威来源。Model 初始化器、ViewModel 与 AppBlockingService
    /// 全部调用此方法，避免出现"多处默认定义不一致（例如 6 条 vs 4 条）"的历史问题。
    /// 采用拆分最细的 6 条规则（拦截项最多的版本）。
    /// </summary>
    public static List<ProtectionRule> CreateDefaultProtectionRules()
    {
        return new List<ProtectionRule>
        {
            new() { Name = "命令提示符 (CMD)", IsEnabled = true, IsSystem = true, ProcessNames = new List<string> { "cmd" } },
            new() { Name = "PowerShell", IsEnabled = true, IsSystem = true, ProcessNames = new List<string> { "powershell", "pwsh", "powershell_ise" } },
            new() { Name = "脚本宿主 (VBS/JS)", IsEnabled = true, IsSystem = true, ProcessNames = new List<string> { "wscript", "cscript" } },
            new() { Name = "任务管理器", IsEnabled = true, IsSystem = true, ProcessNames = new List<string> { "taskmgr", "resmon" } },
            new() { Name = "注册表编辑器", IsEnabled = true, IsSystem = true, ProcessNames = new List<string> { "regedit" } },
            new() { Name = "服务与组策略管理", IsEnabled = true, IsSystem = true, ProcessNames = new List<string> { "mmc" }, CommandLineKeywords = new List<string> { "services.msc", "gpedit.msc", "secpol.msc" } }
        };
    }

    [JsonPropertyName("customBrowserProcesses")]
    public List<string> CustomBrowserProcesses { get; set; } = new();

    [JsonPropertyName("enableChromiumAutoDetection")]
    public bool EnableChromiumAutoDetection { get; set; } = true;
}
