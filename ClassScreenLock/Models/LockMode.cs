using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public enum LockMode
{
    [JsonPropertyName("protectionOnly")]
    ProtectionOnly,
    [JsonPropertyName("full")]
    Full
}

