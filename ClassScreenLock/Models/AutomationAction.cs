using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public class AutomationAction : ObservableObject
{
    private string _type = "LockFull";
    private string? _text;
    private double? _delaySeconds;

    [JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    [JsonPropertyName("text")]
    public string? Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    [JsonPropertyName("delaySeconds")]
    public double? DelaySeconds
    {
        get => _delaySeconds;
        set => SetProperty(ref _delaySeconds, value);
    }
}
