using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

/// <summary>
/// 表示一个可配置的快速操作按钮
/// </summary>
public class QuickAction
{
    /// <summary>
    /// 唯一标识符，用于关联预定义功能
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 用户显示名称（可选覆盖）
    /// </summary>
    [JsonPropertyName("customLabel")]
    public string? CustomLabel { get; set; }

    /// <summary>
    /// 是否在快速操作栏中显示
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 用户自定义的排序顺序
    /// </summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }

    public QuickAction() { }

    public QuickAction(string id, int order, bool enabled = true)
    {
        Id = id;
        Order = order;
        Enabled = enabled;
    }
}
