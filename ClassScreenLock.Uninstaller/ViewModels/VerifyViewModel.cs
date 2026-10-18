using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Uninstaller.Models;
using ClassScreenLock.Uninstaller.Services;

namespace ClassScreenLock.Uninstaller.ViewModels;

/// <summary>
/// 管理员验证窗口 ViewModel
/// 与主程序 SecurityCenterViewModel 的验证逻辑保持一致
/// </summary>
public partial class VerifyViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _twoFactorCode = string.Empty;

    [ObservableProperty]
    private string _loginMessage = string.Empty;

    [ObservableProperty]
    private bool _isVerifying;

    [ObservableProperty]
    private bool _passwordVisible;

    /// <summary>
    /// 是否显示密码输入框
    /// </summary>
    public bool IsPasswordVisible
    {
        get
        {
            var mode = UninstallSecurityService.Instance.EffectiveLoginMode;
            return mode == AdminLoginVerificationMode.PasswordOnly
                || mode == AdminLoginVerificationMode.PasswordOrTwoFactor
                || mode == AdminLoginVerificationMode.PasswordAndTwoFactor;
        }
    }

    /// <summary>
    /// 是否显示双重验证输入框
    /// </summary>
    public bool IsTwoFactorVisible
    {
        get
        {
            var mode = UninstallSecurityService.Instance.EffectiveLoginMode;
            return mode == AdminLoginVerificationMode.TwoFactorOnly
                || mode == AdminLoginVerificationMode.PasswordOrTwoFactor
                || mode == AdminLoginVerificationMode.PasswordAndTwoFactor;
        }
    }

    /// <summary>
    /// 密码显示字符（隐藏时用 ●，显示时用 \0 即空）
    /// </summary>
    public char PasswordChar => PasswordVisible ? '\0' : '●';

    /// <summary>
    /// 密码显示/隐藏按钮文字
    /// </summary>
    public string PasswordToggleLabel => PasswordVisible ? "隐藏" : "显示";

    /// <summary>
    /// 是否可以验证
    /// </summary>
    public bool CanVerify => !IsVerifying;

    /// <summary>
    /// 验证成功事件（由 View 订阅，用于关闭窗口或导航）
    /// </summary>
    public event EventHandler? VerificationSucceeded;

    public VerifyViewModel()
    {
        // 不预填任何账户信息：每次卸载都要求手动输入管理员用户名与凭据。
        Username = string.Empty;
    }

    partial void OnPasswordVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordChar));
        OnPropertyChanged(nameof(PasswordToggleLabel));
    }

    partial void OnIsVerifyingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanVerify));
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        PasswordVisible = !PasswordVisible;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Verify()
    {
        if (IsVerifying) return;

        IsVerifying = true;
        LoginMessage = string.Empty;

        try
        {
            var result = await System.Threading.Tasks.Task.Run(() =>
                UninstallSecurityService.Instance.VerifyPassword(Username, Password, TwoFactorCode));

            switch (result.Status)
            {
                case UninstallVerificationStatus.Success:
                    LoginMessage = string.Empty;
                    VerificationSucceeded?.Invoke(this, EventArgs.Empty);
                    break;

                case UninstallVerificationStatus.InvalidCredentials:
                    LoginMessage = result.Message;
                    if (result.RemainingAttempts > 0)
                    {
                        LoginMessage += $"（剩余尝试次数：{result.RemainingAttempts}）";
                    }
                    // 清空密码框
                    Password = string.Empty;
                    TwoFactorCode = string.Empty;
                    break;

                case UninstallVerificationStatus.LockedOut:
                    var lockUntil = result.LockoutUntil;
                    if (lockUntil.HasValue)
                    {
                        var remaining = lockUntil.Value - DateTime.Now;
                        if (remaining.TotalMinutes > 1)
                        {
                            LoginMessage = $"账户已被锁定，请 {Math.Ceiling(remaining.TotalMinutes)} 分钟后再试";
                        }
                        else
                        {
                            LoginMessage = $"账户已被锁定，请 {Math.Ceiling(remaining.TotalSeconds)} 秒后再试";
                        }
                    }
                    else
                    {
                        LoginMessage = result.Message;
                    }
                    Password = string.Empty;
                    TwoFactorCode = string.Empty;
                    break;

                case UninstallVerificationStatus.NotConfigured:
                    // 未设置密码的情况下，直接允许卸载（因为没有管理员可以验证）
                    // 但卸载程序本身要求管理员权限（UAC），所以已经是管理员身份
                    LoginMessage = string.Empty;
                    VerificationSucceeded?.Invoke(this, EventArgs.Empty);
                    break;

                case UninstallVerificationStatus.Error:
                default:
                    LoginMessage = result.Message;
                    break;
            }
        }
        catch (Exception ex)
        {
            LoginMessage = $"验证过程出错: {ex.Message}";
        }
        finally
        {
            IsVerifying = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        // 取消验证，关闭程序
        Environment.Exit(0);
    }
}
