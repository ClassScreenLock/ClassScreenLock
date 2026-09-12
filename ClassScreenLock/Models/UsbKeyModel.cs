using System;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

/// <summary>
/// USB密钥认证配置模型
/// 每个USB密钥绑定到具体账户，插入对应U盘即可免密码登录该账户
/// </summary>
public class UsbKeyModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [JsonPropertyName("label")]
    public string Label { get; set; } = "USB密钥";

    [JsonPropertyName("accountId")]
    public Guid AccountId { get; set; }

    [JsonPropertyName("accountUsername")]
    public string AccountUsername { get; set; } = string.Empty;

    [JsonPropertyName("accountType")]
    public AccountType AccountType { get; set; } = AccountType.User;

    [JsonPropertyName("tokenHash")]
    public string TokenHash { get; set; } = string.Empty;

    [JsonPropertyName("driveLetter")]
    public string DriveLetter { get; set; } = string.Empty;

    [JsonPropertyName("volumeSerial")]
    public string VolumeSerial { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }
}
