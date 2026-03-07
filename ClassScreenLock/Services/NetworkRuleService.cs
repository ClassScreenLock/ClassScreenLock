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
    private static bool? _cachedEnabled;

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
            
            // 尝试解析为新的格式（包含 enabled 和 rules 的对象）
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rules", out var rulesElement))
            {
                // 新格式：{"enabled": true, "rules": [...]}
                _cachedRules = JsonSerializer.Deserialize<List<NetworkRule>>(rulesElement.GetRawText(), JsonOptions) ?? new();
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                // 旧格式：[...] - 向后兼容
                _cachedRules = JsonSerializer.Deserialize<List<NetworkRule>>(json, JsonOptions) ?? new();
            }
            else
            {
                _cachedRules = new List<NetworkRule>();
            }
            
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

            // 获取当前的 enabled 状态
            var enabled = IsEnabled();
            
            // 创建包含 enabled 和 rules 的对象
            var config = new
            {
                enabled = enabled,
                rules = rules
            };

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(RulesFilePath, json);
            _cachedRules = rules;
            
            System.Diagnostics.Debug.WriteLine($"网络拦截规则已保存到：{RulesFilePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存网络拦截规则失败：{ex.Message}");
        }
    }

    public static bool IsEnabled()
    {
        if (_cachedEnabled.HasValue) return _cachedEnabled.Value;

        try
        {
            if (!File.Exists(RulesFilePath))
            {
                _cachedEnabled = true; // 默认启用
                return true;
            }

            var json = File.ReadAllText(RulesFilePath);
            using var doc = JsonDocument.Parse(json);
            
            // 检查是否有 enabled 字段
            if (doc.RootElement.TryGetProperty("enabled", out var enabledElement))
            {
                _cachedEnabled = enabledElement.GetBoolean();
                return _cachedEnabled.Value;
            }

            // 如果没有 enabled 字段，默认为 true
            _cachedEnabled = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"检查网络拦截启用状态失败：{ex.Message}");
            return true; // 默认启用
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            var directory = Path.GetDirectoryName(RulesFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var rules = _cachedRules ?? LoadRules();
            
            // 创建包含 enabled 字段的对象
            var config = new
            {
                enabled = enabled,
                rules = rules
            };

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(RulesFilePath, json);
            _cachedEnabled = enabled;
            
            System.Diagnostics.Debug.WriteLine($"网络拦截已{(enabled ? "启用" : "禁用")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置网络拦截状态失败：{ex.Message}");
        }
    }
}
