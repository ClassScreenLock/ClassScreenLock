using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public partial class ProtectionRule : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("name")]
    private string _name = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("description")]
    private string _description = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("isEnabled")]
    private bool _isEnabled = true;

    [ObservableProperty]
    [property: JsonPropertyName("processNames")]
    private List<string> _processNames = new();

    partial void OnProcessNamesChanged(List<string> value)
    {
        if (value == null) ProcessNames = new();
    }

    [ObservableProperty]
    [property: JsonPropertyName("isSystem")]
    private bool _isSystem = false; // 是否为内置规则
}
