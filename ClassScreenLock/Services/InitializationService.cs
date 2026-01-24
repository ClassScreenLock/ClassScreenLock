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
    PermissionSetup = 3,
    AdminAccount = 4,
    TwoFactorBinding = 5,
    AppBlocking = 6,
    NetworkBlocking = 7
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
                var include2fa = ShouldIncludeTwoFactorBinding();
                var twoFactorDone = include2fa ? _state.TwoFactorBindingDone : true;
                var adminConfigured = IsAdminConfiguredNoLock();
                return !(_state.UserAgreementDone &&
                         _state.SystemConfigDone &&
                         _state.UserPreferencesDone &&
                         _state.PermissionSetupDone &&
                         adminConfigured &&
                         twoFactorDone &&
                         _state.AppBlockingDone &&
                         _state.NetworkBlockingDone);
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
        if (!_state.PermissionSetupDone) return (int)InitStep.PermissionSetup;
        if (!IsAdminConfiguredNoLock()) return (int)InitStep.AdminAccount;
        if (ShouldIncludeTwoFactorBinding() && !_state.TwoFactorBindingDone) return (int)InitStep.TwoFactorBinding;
        if (!_state.AppBlockingDone) return (int)InitStep.AppBlocking;
        if (!_state.NetworkBlockingDone) return (int)InitStep.NetworkBlocking;
        return (int)InitStep.NetworkBlocking;
    }

    private static bool IsAdminConfiguredNoLock()
    {
        if (!AccountService.Instance.IsInitialized)
        {
            return false;
        }
        var passwordHash = SecurityService.Instance.Settings.PasswordHash;
        return !string.IsNullOrWhiteSpace(passwordHash);
    }

    private void LoadStateInternal()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                _state = new InitState { StartedAt = DateTime.Now };
                SaveStateInternal();
                return;
            }

            var json = File.ReadAllText(StatePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _state = JsonSerializer.Deserialize<InitState>(json, options) ?? new InitState { StartedAt = DateTime.Now };
        }
        catch
        {
            _state = new InitState { StartedAt = DateTime.Now };
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
            if (File.Exists(StatePath))
            {
                var bak = StatePath + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Copy(StatePath, bak, true);
                File.Delete(StatePath);
            }
            File.Move(tmp, StatePath);
        }
        catch
        {
        }
    }

    private class InitState
    {
        public DateTime? StartedAt { get; set; } = DateTime.Now;
        public bool UserAgreementDone { get; set; }
        public bool SystemConfigDone { get; set; }
        public bool UserPreferencesDone { get; set; }
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
