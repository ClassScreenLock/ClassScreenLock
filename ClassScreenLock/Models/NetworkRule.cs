using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public partial class NetworkRule : ObservableObject
{
    private string _domain = string.Empty;
    private string _description = string.Empty;
    private bool _isEnabled = true;
    private string _type = "Domain";

    [System.Text.Json.Serialization.JsonPropertyName("domain")]
    public string Domain
    {
        get => _domain;
        set => SetProperty(ref _domain, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("isEnabled")]
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }
}
