using System.Collections.Generic;

namespace ClassScreenLock.Models;

/// <summary>
/// 看门狗监测时长挡位。控制主程序与三个看门狗进程之间的互监间隔：
/// - NormalMs：正常状态下的检查间隔
/// - AbnormalMs：检测到异常（进程缺失等）时的快速检查间隔
/// </summary>
public sealed class WatchdogTier
{
    public int Tier { get; init; }
    public string Name { get; init; } = string.Empty;
    public int NormalMs { get; init; }
    public int AbnormalMs { get; init; }

    public string NormalDisplay => $"{NormalMs} 毫秒";
    public string AbnormalDisplay => $"{AbnormalMs} 毫秒";

    /// <summary>挡位速查：极速 100ms → 极低功耗 5000ms</summary>
    public static readonly IReadOnlyList<WatchdogTier> All = new List<WatchdogTier>
    {
        new() { Tier = 1, Name = "极速响应", NormalMs = 100, AbnormalMs = 50 },
        new() { Tier = 2, Name = "快速响应", NormalMs = 250, AbnormalMs = 100 },
        new() { Tier = 3, Name = "标准平衡", NormalMs = 500, AbnormalMs = 200 },
        new() { Tier = 4, Name = "稳定运行", NormalMs = 1000, AbnormalMs = 500 },
        new() { Tier = 5, Name = "节能模式", NormalMs = 2000, AbnormalMs = 1000 },
        new() { Tier = 6, Name = "极低功耗", NormalMs = 5000, AbnormalMs = 2500 },
    };

    public static WatchdogTier GetByTier(int tier)
    {
        foreach (var item in All)
        {
            if (item.Tier == tier) return item;
        }
        // 越界回退到"稳定运行"
        return All[3];
    }
}
