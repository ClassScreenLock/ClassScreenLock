using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public enum InterceptionMethod
{
    App,    // 应用程序内高频拦截 (Process Monitor)
    Hosts,  // 系统 Hosts 文件阻断
    Both    // 同时使用两种方式
}

public partial class NetworkRule : ObservableObject
{
    private string _domain = string.Empty;
    private string _description = string.Empty;
    private bool _isEnabled = true;
    private string _type = "Domain";
    private InterceptionMethod _method = InterceptionMethod.App;

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

    [System.Text.Json.Serialization.JsonPropertyName("method")]
    public InterceptionMethod Method
    {
        get => _method;
        set => SetProperty(ref _method, value);
    }
}
