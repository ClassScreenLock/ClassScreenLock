using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Text.Json.Serialization;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class NetworkRuleService
{
    private static readonly string RulesFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Data",
        "Networkblockage.json");

    private static List<NetworkRule>? _cachedRules;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    static NetworkRuleService()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static List<NetworkRule> LoadRules()
    {
        if (_cachedRules != null) return _cachedRules;

        try
        {
            var directory = Path.GetDirectoryName(RulesFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            if (!File.Exists(RulesFilePath))
            {
                // 如果文件不存在，初始化默认规则
                var defaultRules = new List<NetworkRule>
                {
                    new NetworkRule { Domain = "douyin.com", Description = "抖音", IsEnabled = true },
                    new NetworkRule { Domain = "bilibili.com", Description = "B站", IsEnabled = true }
                };
                SaveRules(defaultRules);
                return defaultRules;
            }

            var json = File.ReadAllText(RulesFilePath);
            _cachedRules = JsonSerializer.Deserialize<List<NetworkRule>>(json, JsonOptions) ?? new();
            return _cachedRules;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载网络拦截规则失败: {ex.Message}");
            return new List<NetworkRule>();
        }
    }

    public static void SaveRules(List<NetworkRule> rules)
    {
        try
        {
            var directory = Path.GetDirectoryName(RulesFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var json = JsonSerializer.Serialize(rules, JsonOptions);
            File.WriteAllText(RulesFilePath, json);
            _cachedRules = rules;
            
            System.Diagnostics.Debug.WriteLine($"网络拦截规则已保存到: {RulesFilePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存网络拦截规则失败: {ex.Message}");
        }
    }
}
