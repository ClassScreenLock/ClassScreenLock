using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClassScreenLock.Services;

public class AnalysisResult
{
    public bool IsViolation { get; set; }
    public float Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string MatchedPattern { get; set; } = string.Empty;
}

public class ContentAnalysisEngine
{
    private static readonly ContentAnalysisEngine _instance = new();
    public static ContentAnalysisEngine Instance => _instance;

    // 特征关键词权重
    private readonly Dictionary<string, float> _featureWeights = new()
    {
        { "vpn", 0.8f },
        { "proxy", 0.7f },
        { "unblock", 0.6f },
        { "game", 0.4f },
        { "video", 0.3f },
        { "streaming", 0.5f },
        { "casino", 0.9f },
        { "betting", 0.9f },
        { "porn", 1.0f },
        { "adult", 1.0f }
    };

    private ContentAnalysisEngine() { }

    public AnalysisResult Analyze(string text, List<Models.NetworkRule> rules)
    {
        if (string.IsNullOrWhiteSpace(text)) 
            return new AnalysisResult { IsViolation = false };

        // 1. 精确匹配规则 (同时检查域名和描述)
        foreach (var rule in rules.Where(r => r.IsEnabled))
        {
            if (!string.IsNullOrEmpty(rule.Domain))
            {
                string domain = rule.Domain.ToLower();
                if (text.Contains(domain, StringComparison.OrdinalIgnoreCase))
                {
                    return new AnalysisResult 
                    { 
                        IsViolation = true, 
                        Confidence = 1.0f, 
                        Reason = "Domain Rule Match", 
                        MatchedPattern = rule.Domain 
                    };
                }
            }

        }

        // 2. 特征启发式分析 (Heuristic Analysis)
        float score = 0;
        string matchedFeature = "";
        
        foreach (var feature in _featureWeights)
        {
            if (text.Contains(feature.Key, StringComparison.OrdinalIgnoreCase))
            {
                score += feature.Value;
                if (string.IsNullOrEmpty(matchedFeature)) matchedFeature = feature.Key;
            }
        }

        // 如果得分超过阈值，视为违规
        if (score >= 1.2f)
        {
            return new AnalysisResult 
            { 
                IsViolation = true, 
                Confidence = Math.Min(score / 2.0f, 0.95f), 
                Reason = "Heuristic Analysis", 
                MatchedPattern = matchedFeature 
            };
        }

        return new AnalysisResult { IsViolation = false };
    }
}
