using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BCryptNet = BCrypt.Net.BCrypt;
using OtpNet;
using QRCoder;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class PasswordPolicyResult
{
    public bool IsValid { get; set; }
    public int Score { get; set; }
    public string StrengthLabel { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

public enum PasswordVerificationStatus
{
    Success,
    InvalidCredentials,
    LockedOut,
    NotConfigured,
    Error
}

public class PasswordVerificationResult
{
    public PasswordVerificationStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RemainingAttempts { get; set; }
        = 0;
    public DateTime? LockoutUntil { get; set; }
        = null;
}

public class PasswordChangeResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

public class HibpCheckResult
{
    public bool Success { get; set; }
    public bool IsPwned { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SecurityReport
{
    public int FailedLoginCount { get; set; }
    public int LockoutCount { get; set; }
    public int PasswordChangeCount { get; set; }
    public int LeakDetectedCount { get; set; }
}

public class TwoFactorSetupResult
{
    public string Secret { get; set; } = string.Empty;
    public string QrCodeUri { get; set; } = string.Empty;
}

public class SecurityService
{
    private const int MinLength = 12;
    private const int MaxFailedAttempts = 10;
    private const int AnomalyWindowMinutes = 1;
    private const int AnomalyThreshold = 5;
    private const int WorkFactor = 12;

    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
    private static readonly string SecuritySettingsPath = Path.Combine(DataDirectory, "security.json");

    private static readonly Lazy<SecurityService> _instance = new(() => new SecurityService());
    public static SecurityService Instance => _instance.Value;

    private readonly object _lock = new();
    private SecuritySettingsModel? _settings;
    private readonly HttpClient _httpClient;
    private bool _isAuthenticated;

    private SecurityService()
    {
        if (!Directory.Exists(DataDirectory))
        {
            Directory.CreateDirectory(DataDirectory);
        }

        var handler = new SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ClassScreenLock-Security-Client");
    }

    public SecuritySettingsModel Settings
    {
        get
        {
            lock (_lock)
            {
                if (_settings == null)
                {
                    _settings = LoadSettings();
                }
                return _settings;
            }
        }
    }

    public byte[] GenerateQrCode(string text)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }

    public TwoFactorSetupResult GenerateTwoFactorSetup(string username)
    {
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secret = Base32Encoding.ToString(secretBytes);
        
        // 这里的 QrCodeUri 实际上是用于生成二维码的 URI 格式
        // otpauth://totp/Issuer:Label?secret=Secret&issuer=Issuer
        var issuer = "ClassScreenLock";
        var label = $"{issuer}:{username}";
        var qrCodeUri = $"otpauth://totp/{label}?secret={secret}&issuer={issuer}";

        return new TwoFactorSetupResult
        {
            Secret = secret,
            QrCodeUri = qrCodeUri
        };
    }

    public async Task<PasswordChangeResult> EnableTwoFactorAsync(string secret, string code)
    {
        var result = new PasswordChangeResult();
        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes);
            
            if (totp.VerifyTotp(code, out long timeStepMatched))
            {
                var settings = Settings;
                settings.IsTwoFactorEnabled = true;
                settings.TwoFactorSecret = secret;
                SaveSettings(settings);
                
                result.Success = true;
                result.Message = "双重验证已启用";
                LogService.Instance.Log("Security", "2FAEnabled", "System");
            }
            else
            {
                result.Success = false;
                result.Message = "验证码不正确";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = "启用双重验证时发生错误: " + ex.Message;
        }
        return await Task.FromResult(result);
    }

    public async Task<PasswordChangeResult> DisableTwoFactorAsync(string password)
    {
        var result = new PasswordChangeResult();
        try
        {
            var settings = Settings;
            if (!BCryptNet.Verify(password, settings.PasswordHash))
            {
                result.Success = false;
                result.Message = "当前管理员密码不正确";
                return result;
            }

            settings.IsTwoFactorEnabled = false;
            settings.TwoFactorSecret = string.Empty;
            SaveSettings(settings);
            
            result.Success = true;
            result.Message = "双重验证已禁用";
            LogService.Instance.Log("Security", "2FADisabled", "System");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = "禁用双重验证时发生错误: " + ex.Message;
        }
        return await Task.FromResult(result);
    }

    public bool VerifyTwoFactorCode(string code)
    {
        var settings = Settings;
        if (!settings.IsTwoFactorEnabled) return true;

        try
        {
            var secretBytes = Base32Encoding.ToBytes(settings.TwoFactorSecret);
            var totp = new Totp(secretBytes);
            return totp.VerifyTotp(code, out _);
        }
        catch
        {
            return false;
        }
    }

    public AdminLoginVerificationMode GetEffectiveLoginVerificationMode()
    {
        var settings = Settings;
        return settings.IsTwoFactorEnabled ? settings.LoginVerificationMode : AdminLoginVerificationMode.PasswordOnly;
    }

    public void SetLoginVerificationMode(AdminLoginVerificationMode mode)
    {
        var settings = Settings;
        settings.LoginVerificationMode = mode;
        SaveSettings(settings);
    }

    public bool IsAuthenticated
    {
        get
        {
            lock (_lock)
            {
                return _isAuthenticated;
            }
        }
    }

    public PasswordPolicyResult ValidatePolicy(string password)
    {
        var result = new PasswordPolicyResult();

        if (string.IsNullOrEmpty(password))
        {
            result.Errors.Add("密码不能为空");
            result.StrengthLabel = "无";
            result.Score = 0;
            return result;
        }

        var score = 0;

        if (password.Length >= MinLength)
        {
            score += 30;
        }
        else
        {
            result.Errors.Add($"密码长度至少为 {MinLength} 个字符");
        }

        if (password.Any(char.IsUpper)) score += 15;
        else result.Errors.Add("必须包含大写字母");

        if (password.Any(char.IsLower)) score += 15;
        else result.Errors.Add("必须包含小写字母");

        if (password.Any(char.IsDigit)) score += 15;
        else result.Errors.Add("必须包含数字");

        if (password.Any(c => !char.IsLetterOrDigit(c))) score += 10;

        var distinctChars = password.Distinct().Count();
        if (distinctChars < 4)
        {
            result.Errors.Add("禁止使用过多重复字符");
        }
        else
        {
            score += 10;
        }

        if (IsCommonWeakPassword(password))
        {
            result.Errors.Add("密码过于常见，存在高风险");
            score = Math.Min(score, 30);
        }

        result.Score = Math.Clamp(score, 0, 100);
        result.IsValid = result.Errors.Count == 0;

        if (result.Score < 40) result.StrengthLabel = "弱";
        else if (result.Score < 70) result.StrengthLabel = "中";
        else result.StrengthLabel = "强";

        return result;
    }

    public async Task<bool> InitializeAdminAsync(string username, string password)
    {
        try
        {
            var settings = Settings;
            // 只有当密码哈希为空时才允许初始化
            if (!string.IsNullOrWhiteSpace(settings.PasswordHash))
            {
                LogService.Instance.Log("Security", "AdminInitializeSkipped", username, "密码已设置，跳过初始化");
                return true; // 已经初始化过了也算成功
            }

            settings.AdminUsername = username;
            settings.PasswordHash = BCryptNet.HashPassword(password, workFactor: WorkFactor);
            settings.LastPasswordChange = DateTime.Now;
            settings.LastLeakCheck = DateTime.Now;
            settings.FailedCount = 0;
            settings.LockoutUntil = null;

            SaveSettings(settings);
            LogService.Instance.Log("Security", "AdminInitialized", username);
            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Security", "AdminInitializeError", username, ex.Message);
            return false;
        }
    }

    public async Task<PasswordVerificationResult> VerifyPasswordOnlyAsync(string username, string password)
    {
        try
        {
            var settings = Settings;

            if (string.IsNullOrWhiteSpace(settings.PasswordHash))
            {
                return new PasswordVerificationResult
                {
                    Status = PasswordVerificationStatus.NotConfigured,
                    Message = "尚未设置管理员密码"
                };
            }

            if (settings.LockoutUntil.HasValue && settings.LockoutUntil.Value > DateTime.Now)
            {
                return new PasswordVerificationResult
                {
                    Status = PasswordVerificationStatus.LockedOut,
                    Message = "账户已被锁定，请稍后再试",
                    LockoutUntil = settings.LockoutUntil
                };
            }

            if (!string.Equals(settings.AdminUsername, username, StringComparison.OrdinalIgnoreCase) || 
                !BCryptNet.Verify(password, settings.PasswordHash))
            {
                await RegisterFailedAttemptAsync("验证失败");
                var remaining = Math.Max(0, MaxFailedAttempts - settings.FailedCount);
                return new PasswordVerificationResult
                {
                    Status = PasswordVerificationStatus.InvalidCredentials,
                    Message = "用户名或密码错误",
                    RemainingAttempts = remaining
                };
            }

            return new PasswordVerificationResult
            {
                Status = PasswordVerificationStatus.Success,
                Message = "密码验证成功"
            };
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Security", "VerifyError", username, ex.Message);
            return new PasswordVerificationResult
            {
                Status = PasswordVerificationStatus.Error,
                Message = "验证过程中发生错误"
            };
        }
    }

    public async Task<PasswordVerificationResult> VerifyPasswordAsync(string username, string password, string? twoFactorCode = null)
    {
        try
        {
            var settings = Settings;

            if (string.IsNullOrWhiteSpace(settings.PasswordHash))
            {
                LogService.Instance.Log("Security", "VerifyFailed", username, "管理员密码尚未设置");
                SetAuthenticated(false);
                return new PasswordVerificationResult
                {
                    Status = PasswordVerificationStatus.NotConfigured,
                    Message = "尚未设置管理员密码"
                };
            }

            if (settings.LockoutUntil.HasValue && settings.LockoutUntil.Value > DateTime.Now)
            {
                SetAuthenticated(false);
                return new PasswordVerificationResult
                {
                    Status = PasswordVerificationStatus.LockedOut,
                    Message = "账户已被锁定，请稍后再试",
                    LockoutUntil = settings.LockoutUntil
                };
            }

            var usernameMatches = string.Equals(settings.AdminUsername, username, StringComparison.OrdinalIgnoreCase);
            var passwordMatches = usernameMatches && !string.IsNullOrWhiteSpace(password) && BCryptNet.Verify(password, settings.PasswordHash);
            var twoFactorMatches = settings.IsTwoFactorEnabled && !string.IsNullOrWhiteSpace(twoFactorCode) && VerifyTwoFactorCode(twoFactorCode);

            var mode = GetEffectiveLoginVerificationMode();

            bool success;
            string failureMessage;
            string failureReason;

            switch (mode)
            {
                case AdminLoginVerificationMode.PasswordOnly:
                    success = passwordMatches;
                    failureMessage = "用户名或密码错误";
                    failureReason = "验证失败";
                    break;
                case AdminLoginVerificationMode.TwoFactorOnly:
                    success = usernameMatches && twoFactorMatches;
                    failureMessage = "双重验证码不正确";
                    failureReason = "2FA验证失败";
                    break;
                case AdminLoginVerificationMode.PasswordOrTwoFactor:
                    success = passwordMatches || (usernameMatches && twoFactorMatches);
                    failureMessage = "用户名、密码或双重验证码错误";
                    failureReason = "验证失败";
                    break;
                case AdminLoginVerificationMode.PasswordAndTwoFactor:
                default:
                    if (!passwordMatches)
                    {
                        success = false;
                        failureMessage = "用户名或密码错误";
                        failureReason = "验证失败";
                    }
                    else if (!twoFactorMatches)
                    {
                        success = false;
                        failureMessage = "双重验证码不正确";
                        failureReason = "2FA验证失败";
                    }
                    else
                    {
                        success = true;
                        failureMessage = string.Empty;
                        failureReason = string.Empty;
                    }
                    break;
            }

            if (!success)
            {
                SetAuthenticated(false);
                await RegisterFailedAttemptAsync(failureReason);
                var lockedNow = settings.LockoutUntil.HasValue && settings.LockoutUntil.Value > DateTime.Now;
                var remaining = Math.Max(0, MaxFailedAttempts - settings.FailedCount);
                return new PasswordVerificationResult
                {
                    Status = lockedNow ? PasswordVerificationStatus.LockedOut : PasswordVerificationStatus.InvalidCredentials,
                    Message = lockedNow ? "账户已被锁定，请稍后再试" : failureMessage,
                    RemainingAttempts = remaining,
                    LockoutUntil = settings.LockoutUntil
                };
            }

            ResetFailedAttempts();

            LogService.Instance.Log("Security", "LoginSuccess", username);
            SetAuthenticated(true);

            return new PasswordVerificationResult
            {
                Status = PasswordVerificationStatus.Success,
                Message = "登录成功",
                RemainingAttempts = MaxFailedAttempts
            };
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Security", "LoginError", username, ex.Message);
            SetAuthenticated(false);
            return new PasswordVerificationResult
            {
                Status = PasswordVerificationStatus.Error,
                Message = "登录过程中发生错误"
            };
        }
    }

    public void Logout()
    {
        SetAuthenticated(false);
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(string username, string currentPassword, string newPassword, string confirmPassword)
    {
        var result = new PasswordChangeResult();

        try
        {
            var settings = Settings;

            if (!string.IsNullOrWhiteSpace(settings.PasswordHash) && !InitializationService.Instance.RequiresInitialization)
            {
                if (string.IsNullOrEmpty(currentPassword) || !BCryptNet.Verify(currentPassword, settings.PasswordHash))
                {
                    result.Errors.Add("当前密码不正确");
                    result.Message = "修改密码失败";
                    return result;
                }
            }

            if (newPassword != confirmPassword)
            {
                result.Errors.Add("两次输入的新密码不一致");
            }

            var policy = ValidatePolicy(newPassword);
            if (!policy.IsValid)
            {
                result.Errors.AddRange(policy.Errors);
            }

            if (result.Errors.Count > 0)
            {
                result.Message = "新密码不符合安全要求";
                return result;
            }

            var hibp = await CheckPasswordLeakAsync(newPassword);
            if (hibp.Success && hibp.IsPwned)
            {
                settings.LeakDetected = true;
                settings.LastLeakCheck = DateTime.Now;
                SaveSettings(settings);

                LogService.Instance.Log("Security", "PasswordLeakDetected", username, hibp.Message);

                result.Errors.Add("该密码已出现在泄露数据库中，请更换更强的密码");
                result.Message = "新密码不安全";
                return result;
            }

            settings.PasswordHash = BCryptNet.HashPassword(newPassword, workFactor: WorkFactor);
            settings.AdminUsername = username;
            settings.LastPasswordChange = DateTime.Now;
            settings.LeakDetected = hibp.IsPwned;
            settings.LastLeakCheck = DateTime.Now;
            ResetFailedAttempts();

            SaveSettings(settings);

            // 同步更新 AccountService 中的超级管理员密码
            AccountService.Instance.UpdateSuperAdminPasswordSync(username, settings.PasswordHash);

            LogService.Instance.Log("Security", "PasswordChanged", username);

            result.Success = true;
            result.Message = "密码已成功更新";
            return result;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Security", "PasswordChangeError", username, ex.Message);
            result.Message = "修改密码过程中发生错误";
            return result;
        }
    }

    public async Task<HibpCheckResult> CheckPasswordLeakAsync(string password)
    {
        var result = new HibpCheckResult();

        if (string.IsNullOrEmpty(password))
        {
            result.Message = "密码不能为空";
            return result;
        }

        try
        {
            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
            var hashString = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToUpperInvariant();

            var prefix = hashString.Substring(0, 5);
            var suffix = hashString.Substring(5);

            // 使用 GetAsync 并配置缓存策略（如果需要）
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.pwnedpasswords.com/range/{prefix}");
            var response = await _httpClient.SendAsync(request);
            
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                result.Message = "请求过于频繁，请稍后再试";
                return result;
            }
            
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var lines = content.Split('\n');

            foreach (var line in lines)
            {
                var parts = line.Split(':');
                if (parts.Length >= 2 && parts[0].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Success = true;
                    result.IsPwned = true;
                    result.Message = "密码已被泄露";
                    return result;
                }
            }

            result.Success = true;
            result.IsPwned = false;
            result.Message = "未在泄露数据库中发现该密码";
            return result;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Security", "HibpError", "HIBP", ex.Message);
            result.Message = "无法连接到泄露检测服务";
            return result;
        }
    }

    public SecurityReport GenerateReport(TimeSpan range)
    {
        var since = DateTime.Now - range;
        var logs = LogService.Instance.LoadLogs();

        var securityLogs = logs.Where(l => string.Equals(l.Type, "Security", StringComparison.OrdinalIgnoreCase)
                                           && l.Timestamp >= since).ToList();

        var failed = securityLogs.Count(l => string.Equals(l.Action, "LoginFailed", StringComparison.OrdinalIgnoreCase));
        var locked = securityLogs.Count(l => string.Equals(l.Action, "AccountLocked", StringComparison.OrdinalIgnoreCase));
        var changed = securityLogs.Count(l => string.Equals(l.Action, "PasswordChanged", StringComparison.OrdinalIgnoreCase));
        var leaked = securityLogs.Count(l => string.Equals(l.Action, "PasswordLeakDetected", StringComparison.OrdinalIgnoreCase));

        return new SecurityReport
        {
            FailedLoginCount = failed,
            LockoutCount = locked,
            PasswordChangeCount = changed,
            LeakDetectedCount = leaked
        };
    }

    public bool IsBiometricAvailable => false;

    public Task<bool> AuthenticateWithBiometricsAsync()
    {
        LogService.Instance.Log("Security", "BiometricNotConfigured", "Biometric");
        return Task.FromResult(false);
    }

    private SecuritySettingsModel LoadSettings()
    {
        try
        {
            if (!File.Exists(SecuritySettingsPath))
            {
                // 如果文件不存在，返回一个默认的设置对象
                // 不要在 LoadSettings 中调用 SaveSettings，以免引起锁嵌套或文件访问冲突
                return new SecuritySettingsModel();
            }

            var json = File.ReadAllText(SecuritySettingsPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<SecuritySettingsModel>(json, options) ?? new SecuritySettingsModel();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Security", "LoadSettingsError", "System", ex.Message);
            return new SecuritySettingsModel();
        }
    }

    private void SaveSettings(SecuritySettingsModel settings)
    {
        try
        {
            if (!Directory.Exists(DataDirectory))
            {
                Directory.CreateDirectory(DataDirectory);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(settings, options);

            // 使用简单、原子的方式写入文件
            var tempPath = SecuritySettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(SecuritySettingsPath))
            {
                var backupPath = SecuritySettingsPath + ".bak";
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Copy(SecuritySettingsPath, backupPath, true);
                File.Delete(SecuritySettingsPath);
            }

            File.Move(tempPath, SecuritySettingsPath);

            lock (_lock)
            {
                _settings = settings;
            }
            
            // 确保更新内存中的实例
            LogService.Instance.Log("Security", "SettingsSaved", "System", "安全设置已持久化到磁盘");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Security", "SaveSettingsError", "System", ex.Message);
        }
    }

    private async Task RegisterFailedAttemptAsync(string reason)
    {
        var settings = Settings;

        settings.FailedCount++;
        settings.FailedAttempts ??= new List<DateTime>();
        settings.FailedAttempts.Insert(0, DateTime.Now);

        if (settings.FailedAttempts.Count > 50)
        {
            settings.FailedAttempts = settings.FailedAttempts.Take(50).ToList();
        }

        var recent = settings.FailedAttempts.Where(t => t >= DateTime.Now.AddMinutes(-AnomalyWindowMinutes)).ToList();
        if (recent.Count >= AnomalyThreshold)
        {
            LogService.Instance.Log("Security", "AnomalousLogin", Settings.AdminUsername, "短时间内多次失败登录尝试");
        }

        if (settings.FailedCount >= MaxFailedAttempts && (!settings.LockoutUntil.HasValue || settings.LockoutUntil <= DateTime.Now))
        {
            settings.LockoutUntil = DateTime.Now.AddMinutes(30);
            LogService.Instance.Log("Security", "AccountLocked", Settings.AdminUsername, reason);
        }
        else
        {
            LogService.Instance.Log("Security", "LoginFailed", Settings.AdminUsername, reason);
        }

        SaveSettings(settings);
        await Task.CompletedTask;
    }

    public void ResetFailedAttempts()
    {
        var settings = Settings;
        settings.FailedCount = 0;
        settings.LockoutUntil = null;
        settings.FailedAttempts = new List<DateTime>();
        SaveSettings(settings);
    }

    private void SetAuthenticated(bool value)
    {
        lock (_lock)
        {
            _isAuthenticated = value;
        }
    }

    private static bool IsCommonWeakPassword(string password)
    {
        var weakList = new[]
        {
            "123456",
            "123456789",
            "qwerty",
            "password",
            "111111",
            "123123",
            "12345678",
            "000000",
            "abc123",
            "password1"
        };

        return weakList.Any(w => string.Equals(w, password, StringComparison.OrdinalIgnoreCase));
    }
}
