using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public partial class ProtectionRule : ObservableObject
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private bool _isEnabled = true;
    private bool _hasConfirmedRiskWarning;
    private List<string> _processNames = new();
    private List<string> _commandLineKeywords = new();
    private bool _isSystem;

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    [JsonPropertyName("description")]
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>
    /// 该防护项是否已弹过一次"手动开启风险确认"。为 true 后单独手动开启不再重复弹框。
    /// 通过总开关"全部开启"时会把子项一并置为已确认，避免之后单独开启再弹。
    /// </summary>
    [JsonPropertyName("hasConfirmedRiskWarning")]
    public bool HasConfirmedRiskWarning
    {
        get => _hasConfirmedRiskWarning;
        set => SetProperty(ref _hasConfirmedRiskWarning, value);
    }

    [JsonPropertyName("processNames")]
    public List<string> ProcessNames
    {
        get => _processNames;
        set
        {
            if (SetProperty(ref _processNames, value))
            {
                OnProcessNamesChanged(value);
            }
        }
    }

    /// <summary>
    /// 命令行关键词：进程名命中 ProcessNames 且命令行包含任一关键词时才拦截。
    /// 用于 MMC 共享宿主（services.msc / gpedit.msc / secpol.msc 均由 mmc.exe 承载，
    /// 只能靠命令行区分，避免误伤磁盘管理等其它管理单元）。空列表 = 仅按进程名拦截。
    /// </summary>
    [JsonPropertyName("commandLineKeywords")]
    public List<string> CommandLineKeywords
    {
        get => _commandLineKeywords;
        set => SetProperty(ref _commandLineKeywords, value ?? new());
    }

    [JsonPropertyName("isSystem")]
    public bool IsSystem
    {
        get => _isSystem;
        set => SetProperty(ref _isSystem, value);
    }

    private void OnProcessNamesChanged(List<string> value)
    {
        if (value == null) ProcessNames = new();
    }
}
