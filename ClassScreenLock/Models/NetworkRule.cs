using System;
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
    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("domain")]
    private string _domain = string.Empty;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("description")]
    private string _description = string.Empty;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("isEnabled")]
    private bool _isEnabled = true;

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("type")]
    private string _type = "Domain"; // "Domain" 或 "IP"

    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonPropertyName("method")]
    private InterceptionMethod _method = InterceptionMethod.App;
}
