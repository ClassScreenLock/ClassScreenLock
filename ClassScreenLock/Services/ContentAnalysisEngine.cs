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
    public string MatchedDomain { get; set; } = string.Empty;
}

public class ContentAnalysisEngine
{
    private static readonly ContentAnalysisEngine _instance = new();
    public static ContentAnalysisEngine Instance => _instance;

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

    private static readonly Regex DomainExtractionRegex = new(
        @"(?:https?://)?(?:www\.)?([a-zA-Z0-9][-a-zA-Z0-9]*(?:\.[a-zA-Z0-9][-a-zA-Z0-9]*)+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private ContentAnalysisEngine() { }

    public AnalysisResult Analyze(string text, List<Models.NetworkRule> rules)
    {
        if (string.IsNullOrWhiteSpace(text)) 
            return new AnalysisResult { IsViolation = false };

        var extractedDomains = ExtractAllDomains(text);

        foreach (var rule in rules.Where(r => r.IsEnabled))
        {
            if (!string.IsNullOrEmpty(rule.Domain))
            {
                string ruleDomain = NormalizeDomain(rule.Domain);
                
                foreach (var extractedDomain in extractedDomains)
                {
                    if (DomainMatchesRule(extractedDomain, ruleDomain, out bool isSubdomain))
                    {
                        return new AnalysisResult 
                        { 
                            IsViolation = true, 
                            Confidence = 1.0f, 
                            Reason = isSubdomain ? "Subdomain Rule Match" : "Domain Rule Match", 
                            MatchedPattern = rule.Domain,
                            MatchedDomain = extractedDomain
                        };
                    }
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

    private static string ExtractDomainToken(string domain)
    {
        try
        {
            var parts = domain.Split('.');
            if (parts.Length == 0) return string.Empty;
            var first = parts[0];
            if (string.Equals(first, "www", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                first = parts[1];
            }
            return first;
        }
        catch { return string.Empty; }
    }

    private static bool ContainsWord(string text, string word)
    {
        var pattern = $@"(^|\b|\s){Regex.Escape(word)}(\b|\s|$)";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }

    private static readonly Dictionary<string, string[]> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bilibili", new [] { "哔哩哔哩", "B站", "Bilibili" } },
        { "douyin", new [] { "抖音", "Douyin" } },
        { "github", new [] { "GitHub" } },
        { "google", new [] { "Google" } },
        { "youtube", new [] { "YouTube" } },
        { "discord", new [] { "Discord" } },
        { "pixiv", new [] { "Pixiv" } },
        { "twitter", new [] { "Twitter", "X" } },
        { "facebook", new [] { "Facebook" } },
        { "instagram", new [] { "Instagram" } },
        { "steam", new [] { "Steam" } },
        { "wikipedia", new [] { "Wikipedia", "维基百科" } }
    };

    private static bool ContainsAlias(string text, string token)
    {
        if (!_aliases.TryGetValue(token, out var arr)) return false;
        foreach (var a in arr)
        {
            if (text.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static HashSet<string> ExtractAllDomains(string text)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        var matches = DomainExtractionRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count > 1)
            {
                var domain = match.Groups[1].Value.ToLowerInvariant();
                domains.Add(domain);
            }
        }

        var simpleDomainPattern = new Regex(@"\b([a-zA-Z0-9][-a-zA-Z0-9]{0,61}[a-zA-Z0-9]\.[a-zA-Z]{2,})\b", RegexOptions.IgnoreCase);
        var simpleMatches = simpleDomainPattern.Matches(text);
        foreach (Match match in simpleMatches)
        {
            if (match.Success)
            {
                var domain = match.Groups[1].Value.ToLowerInvariant();
                domains.Add(domain);
            }
        }

        return domains;
    }

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return string.Empty;
        
        var normalized = domain.Trim().ToLowerInvariant();
        
        if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(8);
        else if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(7);
        
        if (normalized.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(4);
        
        var slashIndex = normalized.IndexOf('/');
        if (slashIndex >= 0)
            normalized = normalized.Substring(0, slashIndex);
        
        var colonIndex = normalized.IndexOf(':');
        if (colonIndex >= 0)
            normalized = normalized.Substring(0, colonIndex);
        
        return normalized.TrimEnd('.');
    }

    private static bool DomainMatchesRule(string extractedDomain, string ruleDomain, out bool isSubdomain)
    {
        isSubdomain = false;
        if (string.IsNullOrWhiteSpace(extractedDomain) || string.IsNullOrWhiteSpace(ruleDomain))
            return false;

        var extracted = NormalizeDomain(extractedDomain);
        var rule = NormalizeDomain(ruleDomain);

        if (string.Equals(extracted, rule, StringComparison.OrdinalIgnoreCase))
            return true;

        if (extracted.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase))
        {
            isSubdomain = true;
            return true;
        }

        return false;
    }
}
