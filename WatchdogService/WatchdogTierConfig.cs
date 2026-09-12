using System;
using System.IO;
using System.Text.Json;

namespace CSL.WatchdogService;

/// <summary>
/// 看门狗监测时长挡位配置。与主程序 Models/WatchdogTier.cs 保持一致：
/// 从主程序 Data/settings.json 读取 watchdogMonitorTier（默认 4 = 稳定运行 1000ms/500ms），
/// 使服务与看门狗的监测速度与主程序安全中心"监测时长"挡位设置相同。
/// </summary>
internal static class WatchdogTierConfig
{
    public struct Tier
    {
        public int NormalMs;
        public int AbnormalMs;
    }

    private static readonly (int Tier, int NormalMs, int AbnormalMs)[] Tiers = new[]
    {
        (1, 100, 50),    // 极速响应
        (2, 250, 100),   // 快速响应
        (3, 500, 200),   // 标准平衡
        (4, 1000, 500),  // 稳定运行
        (5, 2000, 1000), // 节能模式
        (6, 5000, 2500), // 极低功耗
    };

    public static Tier GetByTier(int tier)
    {
        foreach (var t in Tiers)
        {
            if (t.Tier == tier) return new Tier { NormalMs = t.NormalMs, AbnormalMs = t.AbnormalMs };
        }
        // 越界回退到"稳定运行"
        return new Tier { NormalMs = 1000, AbnormalMs = 500 };
    }

    /// <summary>
    /// 从主程序的 settings.json 读取挡位。文件不存在/解析失败时回退到默认挡位 4。
    /// </summary>
    public static Tier LoadFromSettings(string settingsPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
            {
                return GetByTier(4);
            }

            using var doc = JsonDocument.Parse(File.ReadAllBytes(settingsPath));
            if (doc.RootElement.TryGetProperty("watchdogMonitorTier", out var tierProp) &&
                tierProp.TryGetInt32(out int tier))
            {
                return GetByTier(tier);
            }
        }
        catch (Exception ex)
        {
            ServiceLogger.Log($"WatchdogTierConfig: 读取挡位配置失败: {ex.Message}");
        }

        return GetByTier(4);
    }
}
