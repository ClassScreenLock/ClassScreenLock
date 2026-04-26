using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BCryptNet = BCrypt.Net.BCrypt;
using OtpNet;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class AccountService
{
    private const int MaxSubAccounts = 5;

    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
    private static readonly string AccountsPath = Path.Combine(DataDirectory, "accounts.json");

    private static readonly Lazy<AccountService> _instance = new(() => new AccountService());
    public static AccountService Instance => _instance.Value;

    private readonly object _lock = new();
    private List<AccountModel> _accounts = new();
    private AccountModel? _currentAccount;
    private DateTime? _loginTime;
    private bool _isInitialized;

    private AccountService()
    {
        if (!Directory.Exists(DataDirectory))
        {
            Directory.CreateDirectory(DataDirectory);
        }

        LoadAccounts();
        _isInitialized = _accounts.Any(a => a.AccountType == AccountType.SuperAdmin);
    }

    public bool IsInitialized
    {
        get
        {
            lock (_lock)
            {
                return _isInitialized;
            }
        }
    }

    public AccountModel? CurrentAccount
    {
        get
        {
            lock (_lock)
            {
                return _currentAccount;
            }
        }
    }

    public DateTime? CurrentLoginTime
    {
        get
        {
            lock (_lock)
            {
                return _loginTime;
            }
        }
    }

    public IReadOnlyList<AccountModel> Accounts
    {
        get
        {
            lock (_lock)
            {
                return _accounts.Where(a => !a.IsDisabled).OrderByDescending(a => a.AccountType).ThenBy(a => a.Username).ToList();
            }
        }
    }

    /// <summary>
    /// 获取所有账户（包括被禁用的），用于恢复操作
    /// </summary>
    public IReadOnlyList<AccountModel> GetAllAccountsForRestore()
    {
        lock (_lock)
        {
            return _accounts.ToList();
        }
    }

    /// <summary>
    /// 重新启用账户（用于退出集控端后恢复超级管理员账户）
    /// </summary>
    public void ReenableAccount(Guid accountId)
    {
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                account.IsDisabled = false;
                SaveAccounts();
                LogService.Instance.Log("Account", "Reenabled", account.Username);
            }
        }
    }

    public void DisableAccount(Guid accountId)
    {
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                account.IsDisabled = true;
                SaveAccounts();
                LogService.Instance.Log("Account", "Disabled", account.Username);
            }
        }
    }

    public void UpdateSuperAdminPasswordSync(string username, string newHash)
    {
        lock (_lock)
        {
            // 优先更新本地超级管理员账户（非组织账户且未禁用）
            var admin = _accounts.FirstOrDefault(a => a.AccountType == AccountType.SuperAdmin && !a.IsFromOrganization && !a.IsDisabled);
            
            // 如果没有本地超级管理员账户，找未禁用的超级管理员账户
            if (admin == null)
            {
                admin = _accounts.FirstOrDefault(a => a.AccountType == AccountType.SuperAdmin && !a.IsDisabled);
            }
            
            // 如果还是没有，找第一个超级管理员账户（不管是否禁用）
            if (admin == null)
            {
                admin = _accounts.FirstOrDefault(a => a.AccountType == AccountType.SuperAdmin);
            }
            
            if (admin != null)
            {
                admin.Username = username;
                admin.PasswordHash = newHash;
                SaveAccounts();
            }
        }
    }

    public void UpdateAccountIsFromOrganization(Guid accountId, bool isFromOrganization)
    {
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                account.IsFromOrganization = isFromOrganization;
                SaveAccounts();
            }
        }
    }

    public void UpdateAccountType(Guid accountId, AccountType accountType)
    {
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                account.AccountType = accountType;
                SaveAccounts();
            }
        }
    }

    public async Task<bool> EnsureSuperAdminExistsAsync(string username, string password)
    {
        lock (_lock)
        {
            if (_accounts.Any(a => a.AccountType == AccountType.SuperAdmin))
            {
                return false;
            }
        }

        var policy = SecurityService.Instance.ValidatePolicy(password);
        if (!policy.IsValid)
        {
            return false;
        }

        var account = new AccountModel
        {
            Username = username,
            AccountType = AccountType.SuperAdmin,
            PasswordHash = BCryptNet.HashPassword(password)
        };

        lock (_lock)
        {
            _accounts.Add(account);
            _isInitialized = true;
            SaveAccounts();
        }

        // 同步初始化 SecurityService 的管理员密码
        var securityInitResult = await SecurityService.Instance.InitializeAdminAsync(username, password);
        if (!securityInitResult)
        {
            LogService.Instance.Log("Account", "SecurityInitFailed", username, "安全服务初始化失败");
            // 虽然安全服务初始化失败，但主账号已创建，我们继续，
            // 用户以后可以在密码中心重新设置
        }

        LogService.Instance.Log("Account", "SuperAdminCreated", account.Username);
        await Task.CompletedTask;
        return true;
    }

    private static bool VerifyTwoFactorCode(string secret, string? code)
    {
        if (string.IsNullOrWhiteSpace(secret)) return true;
        if (string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes);
            return totp.VerifyTotp(code, out _);
        }
        catch
        {
            return false;
        }
    }

    public bool IsAccountTwoFactorEnabled(string username)
    {
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase) && !a.IsDisabled);
            return account != null && account.IsTwoFactorEnabled && !string.IsNullOrWhiteSpace(account.TwoFactorSecret);
        }
    }

    public async Task<(bool success, string message)> LoginAsync(string username, string password, string? twoFactorCode = null)
    {
        lock (_lock)
        {
            if (_accounts.Count == 0)
            {
                return (false, "尚未初始化账户，请先创建超级管理员账户");
            }
        }

        AccountModel? account;
        lock (_lock)
        {
            account = _accounts.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase) && !a.IsDisabled);
        }

        if (account == null)
        {
            LogService.Instance.Log("Account", "LoginFailed", username, "账号不存在");
            return (false, "账号不存在或已禁用");
        }

        if (account.IsLocked)
        {
            LogService.Instance.Log("Account", "LoginRejected", username, "账号已锁定");
            return (false, "账号已被锁定");
        }

        if (!BCryptNet.Verify(password, account.PasswordHash))
        {
            LogService.Instance.Log("Account", "LoginFailed", username, "密码错误");
            return (false, "用户名或密码错误");
        }

        if (account.IsTwoFactorEnabled && !string.IsNullOrWhiteSpace(account.TwoFactorSecret))
        {
            var twoFactorOk = VerifyTwoFactorCode(account.TwoFactorSecret, twoFactorCode);
            if (!twoFactorOk)
            {
                LogService.Instance.Log("Account", "LoginFailed", username, "双重验证码不正确");
                return (false, "双重验证码不正确或未提供");
            }
        }

        lock (_lock)
        {
            _currentAccount = account;
            _loginTime = DateTime.Now;
            account.LastLoginAt = _loginTime;
            SaveAccounts();
        }

        LogService.Instance.Log("Account", "LoginSuccess", username);
        await Task.CompletedTask;
        return (true, "登录成功");
    }

    public TwoFactorSetupResult GenerateTwoFactorSetupForAccount(string username)
    {
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secret = Base32Encoding.ToString(secretBytes);
        var issuer = "ClassScreenLock";
        var label = $"{issuer}:{username}";
        var qrCodeUri = $"otpauth://totp/{label}?secret={secret}&issuer={issuer}";
        return new TwoFactorSetupResult { Secret = secret, QrCodeUri = qrCodeUri };
    }

    public async Task<PasswordChangeResult> EnableTwoFactorForAccountAsync(Guid accountId, string secret, string code)
    {
        var result = new PasswordChangeResult();
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId && !a.IsDisabled);
            if (account == null)
            {
                result.Success = false;
                result.Message = "账号不存在或已禁用";
                return result;
            }

            var ok = VerifyTwoFactorCode(secret, code);
            if (!ok)
            {
                result.Success = false;
                result.Message = "验证码不正确";
                return result;
            }

            account.IsTwoFactorEnabled = true;
            account.TwoFactorSecret = secret;
            SaveAccounts();
            LogService.Instance.Log("Account", "2FAEnabled", account.Username);
            result.Success = true;
            result.Message = "双重验证已启用";
        }
        return await Task.FromResult(result);
    }

    public async Task<PasswordChangeResult> DisableTwoFactorForAccountAsync(Guid accountId)
    {
        var result = new PasswordChangeResult();
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId && !a.IsDisabled);
            if (account == null)
            {
                result.Success = false;
                result.Message = "账号不存在或已禁用";
                return result;
            }

            account.IsTwoFactorEnabled = false;
            account.TwoFactorSecret = string.Empty;
            SaveAccounts();
            LogService.Instance.Log("Account", "2FADisabled", account.Username);
            result.Success = true;
            result.Message = "双重验证已禁用";
        }
        return await Task.FromResult(result);
    }

    public async Task<PasswordChangeResult> UpdateAccountTwoFactorAsync(Guid accountId, bool isEnabled, string secret)
    {
        var result = new PasswordChangeResult();
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId && !a.IsDisabled);
            if (account == null)
            {
                result.Success = false;
                result.Message = "账号不存在或已禁用";
                return result;
            }

            account.IsTwoFactorEnabled = isEnabled;
            account.TwoFactorSecret = secret;
            SaveAccounts();
            LogService.Instance.Log("Account", "2FAUpdated", account.Username);
            result.Success = true;
            result.Message = "双重验证设置已更新";
        }
        return await Task.FromResult(result);
    }

    public bool LoginFromSecuritySession(string username)
    {
        if (!SecurityService.Instance.IsAuthenticated)
        {
            return false;
        }

        AccountModel? superAdmin;
        lock (_lock)
        {
            superAdmin = _accounts.FirstOrDefault(a => a.AccountType == AccountType.SuperAdmin && !a.IsDisabled);
            if (superAdmin == null)
            {
                return false;
            }

            
            var usernameMatches = string.Equals(username, superAdmin.Username, StringComparison.OrdinalIgnoreCase);
            if (!usernameMatches)
            {
                return false;
            }

            _currentAccount = superAdmin;
            _loginTime = DateTime.Now;
            superAdmin.LastLoginAt = _loginTime;
            SaveAccounts();
        }

        LogService.Instance.Log("Account", "LoginSuccess", username, "SecuritySession");
        return true;
    }

    public void Logout()
    {
        lock (_lock)
        {
            if (_currentAccount != null)
            {
                LogService.Instance.Log("Account", "Logout", _currentAccount.Username);
            }
            _currentAccount = null;
            _loginTime = null;
        }
        
        SecurityService.Instance.Logout();
    }

    public async Task<(bool success, string message)> CreateSubAccountAsync(string username, string password, AccountType accountType)
    {
        var current = CurrentAccount;
        if (current == null || current.AccountType != AccountType.SuperAdmin)
        {
            LogService.Instance.Log("Account", "CreateDenied", username, "非超级管理员尝试创建账号");
            return (false, "只有超级管理员可以创建子账号");
        }

        if (accountType == AccountType.SuperAdmin)
        {
            // 允许最多两个超级管理员账户（本地 + 集控端）
            var superAdminCount = _accounts.Count(a => a.AccountType == AccountType.SuperAdmin);
            if (superAdminCount >= 2)
            {
                return (false, "超级管理员账户数量已达到上限");
            }
        }

        if (accountType != AccountType.Admin && accountType != AccountType.User)
        {
            return (false, "仅支持创建管理员或用户类型的账号");
        }

        lock (_lock)
        {
            var subCount = _accounts.Count(a => a.AccountType != AccountType.SuperAdmin && !a.IsDisabled);
            if (subCount >= MaxSubAccounts)
            {
                return (false, "子账号数量已达到上限");
            }

            if (_accounts.Any(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase) && !a.IsDisabled))
            {
                return (false, "该用户名已存在");
            }
        }

        var policy = SecurityService.Instance.ValidatePolicy(password);
        if (!policy.IsValid)
        {
            return (false, "密码不符合安全策略要求");
        }

        var account = new AccountModel
        {
            Username = username,
            AccountType = accountType,
            PasswordHash = BCryptNet.HashPassword(password)
        };

        lock (_lock)
        {
            _accounts.Add(account);
            SaveAccounts();
        }

        LogService.Instance.Log("Account", "Created", account.Username, accountType.ToString());
        await Task.CompletedTask;
        return (true, "账号创建成功");
    }

    public async Task<(bool success, string message)> DeleteAccountAsync(Guid accountId)
    {
        var current = CurrentAccount;
        if (current == null || current.AccountType != AccountType.SuperAdmin)
        {
            LogService.Instance.Log("Account", "DeleteDenied", accountId.ToString(), "非超级管理员尝试删除账号");
            return (false, "只有超级管理员可以删除账号");
        }

        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null)
            {
                return (false, "账号不存在");
            }

            if (account.AccountType == AccountType.SuperAdmin)
            {
                return (false, "不能删除超级管理员账号");
            }

            _accounts.Remove(account);
            SaveAccounts();
            LogService.Instance.Log("Account", "Deleted", account.Username);
        }

        await Task.CompletedTask;
        return (true, "账号已删除");
    }

    public void DeleteAccountInternal(Guid accountId)
    {
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                _accounts.Remove(account);
                SaveAccounts();
                LogService.Instance.Log("Account", "DeletedInternal", account.Username);
            }
        }
    }

    public (bool success, string message) CreateSubAccountInternal(string username, string password, AccountType accountType)
    {
        lock (_lock)
        {
            if (accountType == AccountType.SuperAdmin)
            {
                // 允许最多两个超级管理员账户（本地 + 集控端）
                var superAdminCount = _accounts.Count(a => a.AccountType == AccountType.SuperAdmin);
                if (superAdminCount >= 2)
                {
                    return (false, "超级管理员账户数量已达到上限（最多两个）");
                }
            }
            else
            {
                // 普通账户和管理员账户的限制
                var subCount = _accounts.Count(a => a.AccountType != AccountType.SuperAdmin && !a.IsDisabled);
                if (subCount >= MaxSubAccounts)
                {
                    return (false, "子账号数量已达到上限");
                }
            }

            if (_accounts.Any(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "该用户名已存在");
            }
        }

        var policy = SecurityService.Instance.ValidatePolicy(password);
        if (!policy.IsValid)
        {
            return (false, "密码不符合安全策略要求");
        }

        var account = new AccountModel
        {
            Username = username,
            AccountType = accountType,
            PasswordHash = BCryptNet.HashPassword(password)
        };

        lock (_lock)
        {
            _accounts.Add(account);
            SaveAccounts();
            LogService.Instance.Log("Account", "CreatedInternal", account.Username);
        }

        return (true, "账号创建成功");
    }

    public bool HasPermission(AccountType requiredLevel)
    {
        var current = CurrentAccount;
        if (current == null)
        {
            return false;
        }

        var currentLevel = (int)current.AccountType;
        var requiredLevelValue = (int)requiredLevel;

        return currentLevel <= requiredLevelValue;
    }

    public bool HasPermissionOrSecurityAuth(AccountType requiredLevel)
    {
        if (SecurityService.Instance.IsAuthenticated)
        {
            LogService.Instance.Log("Debug", "Permission", "HasPermissionOrSecurityAuth", $"SecurityService.IsAuthenticated=true, required={requiredLevel}");
            return true;
        }
        
        var current = CurrentAccount;
        var hasPerm = HasPermission(requiredLevel);
        LogService.Instance.Log("Debug", "Permission", "HasPermissionOrSecurityAuth", 
            $"CurrentAccount={current?.Username ?? "null"}, AccountType={current?.AccountType}, required={requiredLevel}, result={hasPerm}");
        
        return hasPerm;
    }

    public bool EnsurePermission(AccountType requiredLevel, string operationName)
    {
        var allowed = HasPermission(requiredLevel);
        if (!allowed)
        {
            var currentName = CurrentAccount?.Username ?? "Anonymous";
            LogService.Instance.Log("Account", "PermissionDenied", currentName, operationName);
        }

        return allowed;
    }

    private void LoadAccounts()
    {
        try
        {
            if (!File.Exists(AccountsPath))
            {
                _accounts = new List<AccountModel>();
                return;
            }

            var json = File.ReadAllText(AccountsPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _accounts = JsonSerializer.Deserialize<List<AccountModel>>(json, options) ?? new List<AccountModel>();
        }
        catch
        {
            _accounts = new List<AccountModel>();
        }
    }

    private void SaveAccounts()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(_accounts, options);
            var tempPath = AccountsPath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(AccountsPath))
            {
                var backup = AccountsPath + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Copy(AccountsPath, backup);
                File.Delete(AccountsPath);
            }

            File.Move(tempPath, AccountsPath);
        }
        catch
        {
        }
    }
}
