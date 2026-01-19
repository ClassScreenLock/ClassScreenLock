using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BCryptNet = BCrypt.Net.BCrypt;
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

    public void UpdateSuperAdminPasswordSync(string username, string newHash)
    {
        lock (_lock)
        {
            var admin = _accounts.FirstOrDefault(a => a.AccountType == AccountType.SuperAdmin);
            if (admin != null)
            {
                admin.Username = username;
                admin.PasswordHash = newHash;
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

    public async Task<(bool success, string message)> LoginAsync(string username, string password)
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

            var usernameMatches = string.Equals(username, superAdmin.Username, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(username, SecurityService.Instance.Settings.AdminUsername, StringComparison.OrdinalIgnoreCase);
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
        
        // 登出时同时解除安全中心的授权状态
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
            return (false, "不允许创建额外的超级管理员");
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

            account.IsDisabled = true;
            SaveAccounts();
            LogService.Instance.Log("Account", "Deleted", account.Username);
        }

        await Task.CompletedTask;
        return (true, "账号已删除");
    }

    public bool HasPermission(AccountType requiredLevel)
    {
        var current = CurrentAccount;
        if (current == null)
        {
            return false;
        }

        return current.AccountType switch
        {
            AccountType.SuperAdmin => true,
            AccountType.Admin => requiredLevel != AccountType.SuperAdmin,
            AccountType.User => requiredLevel == AccountType.User,
            _ => false
        };
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
