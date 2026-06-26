using System.Collections.Generic;
using System.Linq;

namespace ClassScreenLock.Models;

/// <summary>
/// 快速操作的静态定义（不可变）
/// </summary>
public class QuickActionDefinition
{
    /// <summary>
    /// 唯一 ID（与 QuickAction.Id 对应）
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 显示名称（本地化键）
    /// </summary>
    public string LabelKey { get; init; } = string.Empty;

    /// <summary>
    /// 描述（本地化键）
    /// </summary>
    public string DescriptionKey { get; init; } = string.Empty;

    /// <summary>
    /// 分组（用于编辑器分类）
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// 图标名称（对应 FluentIcons Symbol 枚举的字符串名）
    /// </summary>
    public string IconName { get; init; } = string.Empty;

    /// <summary>
    /// 强调色
    /// </summary>
    public string AccentColor { get; init; } = "#0078D4";

    /// <summary>
    /// 是否为可执行命令（true）还是导航（false）
    /// </summary>
    public bool IsCommand { get; init; }

    /// <summary>
    /// 命令/导航目标 ID
    /// </summary>
    public string TargetId { get; init; } = string.Empty;
}
