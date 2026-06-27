using System;
using System.IO;
using System.Text.Json;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public enum InitStep
{
    UserAgreement = 0,
    SystemConfig = 1,
    UserPreferences = 2,
    MonitoringConfig = 3,
    PermissionSetup = 4,
    AdminAccount = 5,
    TwoFactorBinding = 6,
    AppBlocking = 7,
    NetworkBlocking = 8
}

public class InitializationService
{
    private static readonly Lazy<InitializationService> _instance = new(() => new InitializationService());
    public static InitializationService Instance => _instance.Value;

    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
    private static readonly string StatePath = Path.Combine(DataDirectory, "init_state.json");

    private readonly object _lock = new();
    private InitState _state = new();

    private InitializationService()
    {
        if (!Directory.Exists(DataDirectory))
        {
            Directory.CreateDirectory(DataDirectory);
        }

        LoadStateInternal();
    }

    public bool RequiresInitialization
    {
        get
        {
            lock (_lock)
            {
                // 先完整验证所有步骤，不再提前返回
                var include2fa = ShouldIncludeTwoFactorBinding();
                var twoFactorDone = include2fa ? _state.TwoFactorBindingDone : true;

                // 检查管理员是否配置完成（包括集控端账号）
                var adminConfigured = IsAdminConfiguredNoLock();

                var result = !(_state.UserAgreementDone &&
                         _state.SystemConfigDone &&
                         _state.UserPreferencesDone &&
                         _state.MonitoringConfigDone &&
                         _state.PermissionSetupDone &&
                         adminConfigured &&
                         twoFactorDone &&
                         _state.AppBlockingDone &&
                         _state.NetworkBlockingDone);

                // 诊断日志：逐个输出每个条件的值，用于排查每次启动进入初始化的问题
                LogService.Instance.Log("Init", "RequiresInit_Diag", "System",
                    $"结果={result} | " +
                    $"UserAgreement={_state.UserAgreementDone} " +
                    $"SystemConfig={_state.SystemConfigDone} " +
                    $"UserPrefs={_state.UserPreferencesDone} " +
                    $"Monitoring={_state.MonitoringConfigDone} " +
                    $"Permission={_state.PermissionSetupDone} " +
                    $"AdminConfigured={adminConfigured} " +
                    $"TwoFactorDone={twoFactorDone}(include2fa={include2fa}) " +
                    $"AppBlocking={_state.AppBlockingDone} " +
                    $"NetworkBlocking={_state.NetworkBlockingDone}");

                return result;
            }
        }
    }

    public int CurrentStepIndex
    {
        get
        {
            lock (_lock)
            {
                return GetNextStepIndexNoLock();
            }
        }
    }

    public void ResetState()
    {
        lock (_lock)
        {
            _state = new InitState { StartedAt = DateTime.Now };
            SaveStateInternal();
            LogService.Instance.Log("Init", "Reset", "System");
        }
    }

    public void ReloadState()
    {
        lock (_lock)
        {
            LoadStateInternal();
            LogService.Instance.Log("Init", "Reloaded", "System");
        }
    }

    public void MarkStepComplete(InitStep step)
    {
        lock (_lock)
        {
            switch (step)
            {
                case InitStep.UserAgreement:
                    _state.UserAgreementDone = true;
                    break;
                case InitStep.SystemConfig:
                    _state.SystemConfigDone = true;
                    break;
                case InitStep.UserPreferences:
                    _state.UserPreferencesDone = true;
                    break;
                case InitStep.MonitoringConfig:
                    _state.MonitoringConfigDone = true;
                    break;
                case InitStep.PermissionSetup:
                    _state.PermissionSetupDone = true;
                    break;
                case InitStep.AdminAccount:
                    _state.AdminAccountDone = true;
                    break;
                case InitStep.TwoFactorBinding:
                    _state.TwoFactorBindingDone = true;
                    break;
                case InitStep.AppBlocking:
                    _state.AppBlockingDone = true;
                    break;
                case InitStep.NetworkBlocking:
                    _state.NetworkBlockingDone = true;
                    break;
            }
            SaveStateInternal();
            LogService.Instance.Log("Init", "StepCompleted", step.ToString());
        }
    }

    public void SaveState()
    {
        lock (_lock)
        {
            SaveStateInternal();
        }
    }

    public bool IsStepCompleted(InitStep step)
    {
        lock (_lock)
        {
            return step switch
            {
                InitStep.UserAgreement => _state.UserAgreementDone,
                InitStep.SystemConfig => _state.SystemConfigDone,
                InitStep.UserPreferences => _state.UserPreferencesDone,
                InitStep.MonitoringConfig => _state.MonitoringConfigDone,
                InitStep.PermissionSetup => _state.PermissionSetupDone,
                InitStep.AdminAccount => IsAdminConfiguredNoLock(),
                InitStep.TwoFactorBinding => _state.TwoFactorBindingDone,
                InitStep.AppBlocking => _state.AppBlockingDone,
                InitStep.NetworkBlocking => _state.NetworkBlockingDone,
                _ => false
            };
        }
    }

    private int GetNextStepIndexNoLock()
    {
        if (!_state.UserAgreementDone) return (int)InitStep.UserAgreement;
        if (!_state.SystemConfigDone) return (int)InitStep.SystemConfig;
        if (!_state.UserPreferencesDone) return (int)InitStep.UserPreferences;
        if (!_state.MonitoringConfigDone) return (int)InitStep.MonitoringConfig;
        if (!_state.PermissionSetupDone) return (int)InitStep.PermissionSetup;
        if (!IsAdminConfiguredNoLock()) return (int)InitStep.AdminAccount;
        if (ShouldIncludeTwoFactorBinding() && !_state.TwoFactorBindingDone) return (int)InitStep.TwoFactorBinding;
        if (!_state.AppBlockingDone) return (int)InitStep.AppBlocking;
        if (!_state.NetworkBlockingDone) return (int)InitStep.NetworkBlocking;
        return (int)InitStep.NetworkBlocking;
    }

    private static bool IsAdminConfiguredNoLock()
    {
        // 先检查 AccountService 是否有超级管理员（包括集控端账号）
        var acctInitialized = AccountService.Instance.IsInitialized;
        if (acctInitialized)
        {
            // 检查是否有有效的超级管理员（不论是本地还是集控端）
            var hasValidSuperAdmin = AccountService.Instance.HasValidSuperAdmin();
            if (hasValidSuperAdmin)
            {
                LogService.Instance.Log("Init", "AdminCheck", "System", "管理员已配置（AccountService：有有效超级管理员）");
                return true;
            }
        }

        // 再检查 SecurityService 的密码哈希（作为备用验证）
        var passwordHash = SecurityService.Instance.Settings.PasswordHash;
        var hasHash = !string.IsNullOrWhiteSpace(passwordHash);
        LogService.Instance.Log("Init", "AdminCheck", "System",
            $"AccountService.IsInitialized={acctInitialized}, HasValidSuperAdmin={AccountService.Instance.HasValidSuperAdmin()}, PasswordHash非空={hasHash}");
        return hasHash;
    }

    private void LoadStateInternal()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                _state = new InitState { StartedAt = DateTime.Now };
                LogService.Instance.Log("Init", "LoadState", "System", $"init_state.json 不存在（路径：{StatePath}），创建新的空状态");
                SaveStateInternal();
                return;
            }

            var json = File.ReadAllText(StatePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _state = JsonSerializer.Deserialize<InitState>(json, options) ?? new InitState { StartedAt = DateTime.Now };
            LogService.Instance.Log("Init", "LoadState", "System",
                $"已加载 init_state.json：UA={_state.UserAgreementDone} SC={_state.SystemConfigDone} UP={_state.UserPreferencesDone} " +
                $"MC={_state.MonitoringConfigDone} PS={_state.PermissionSetupDone} AA={_state.AdminAccountDone} " +
                $"2FA={_state.TwoFactorBindingDone} AB={_state.AppBlockingDone} NB={_state.NetworkBlockingDone}");
        }
        catch (Exception ex)
        {
            _state = new InitState { StartedAt = DateTime.Now };
            LogService.Instance.Log("Error", "Init", "LoadState", $"加载 init_state.json 失败：{ex.Message}");
        }
    }

    private void SaveStateInternal()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(_state, options);
            var tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, json);

            // 使用原子替换策略，避免 Delete 触发 FileSystemWatcher 在间隔期捕获到文件缺失
            // 步骤：写临时文件 → 原文件重命名为备份 → 临时文件重命名为正式文件
            if (File.Exists(StatePath))
            {
                var bak = StatePath + ".bak";
                // 删除旧备份文件（.bak 被 FileSystemWatcher 排除，不影响同步）
                if (File.Exists(bak)) File.Delete(bak);
                // 重命名原文件为备份（产生 Renamed 事件，目标为 .bak，被 watcher 排除）
                File.Move(StatePath, bak);
            }
            // 重命名临时文件为正式文件（产生 Renamed 事件，源为 .tmp，被 watcher 排除）
            File.Move(tmp, StatePath);

            // 清理备份文件
            var oldBak = StatePath + ".bak";
            if (File.Exists(oldBak))
            {
                try { File.Delete(oldBak); } catch { }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Init", "SaveState", $"保存初始化状态失败：{ex.Message}");
        }
    }

    private class InitState
    {
        public DateTime? StartedAt { get; set; } = DateTime.Now;
        public bool UserAgreementDone { get; set; }
        public bool SystemConfigDone { get; set; }
        public bool UserPreferencesDone { get; set; }
        public bool MonitoringConfigDone { get; set; }
        public bool PermissionSetupDone { get; set; }
        public bool AdminAccountDone { get; set; }
        public bool TwoFactorBindingDone { get; set; }
        public bool AppBlockingDone { get; set; }
        public bool NetworkBlockingDone { get; set; }
    }

    private bool ShouldIncludeTwoFactorBinding()
    {
        var settings = SecurityService.Instance.Settings;
        var enabled = settings.IsTwoFactorEnabled;
        var mode = settings.LoginVerificationMode;
        return mode != AdminLoginVerificationMode.PasswordOnly && !enabled;
    }
}
