using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

/// <summary>
/// 阻止规则的匹配类型。
/// Name  = 按进程名（如 powershell.exe），会匹配所有同名进程
/// Path  = 按可执行文件绝对路径，仅匹配该精确路径
/// </summary>
public enum BlockedRuleKind
{
    Name = 0,
    Path = 1
}

public partial class BlockedRule : ObservableObject
{
    [ObservableProperty]
    private BlockedRuleKind _kind;

    [ObservableProperty]
    private string _value = string.Empty;

    public BlockedRule() { }

    public BlockedRule(BlockedRuleKind kind, string value)
    {
        _kind = kind;
        _value = value ?? string.Empty;
    }

    /// <summary>
    /// 规范化显示（Path 类型统一大写、去掉引号、绝对化）。
    /// </summary>
    public string DisplayValue
    {
        get
        {
            if (string.IsNullOrEmpty(Value)) return string.Empty;
            return Kind == BlockedRuleKind.Path ? Normalize(Value) : Value.Trim();
        }
    }

    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            var trimmed = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;
            return System.IO.Path.GetFullPath(trimmed);
        }
        catch
        {
            return path.Trim();
        }
    }

    [JsonIgnore]
    public bool IsPathRule => Kind == BlockedRuleKind.Path;

    public override string ToString() => $"{Kind}:{Value}";
}
