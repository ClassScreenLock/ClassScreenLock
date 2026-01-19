using System;
using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public class AccountModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [JsonPropertyName("accountType")]
    public AccountType AccountType { get; set; } = AccountType.User;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }
        = null;

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; }
        = false;

    [JsonPropertyName("isDisabled")]
    public bool IsDisabled { get; set; }
        = false;
}

