using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public partial class ProtectionRule : ObservableObject
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private bool _isEnabled = true;
    private List<string> _processNames = new();
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
