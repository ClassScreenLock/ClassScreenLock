using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;
using ClassScreenLock.Models;
using Avalonia.Threading;

namespace ClassScreenLock.ViewModels;

public partial class FloatingLockWidgetViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _infoText = string.Empty;

    private readonly DispatcherTimer _timer;

    public FloatingLockWidgetViewModel()
    {
        UpdateInfoText();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (s, e) => UpdateInfoText();
        _timer.Start();
    }

    private void UpdateInfoText()
    {
        var now = DateTime.Now.TimeOfDay;
        var (current, next) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);
        var settings = SettingsService.Lock;

        if (next != null && next.Type == TimePointType.Class)
        {
            var unlockTime = next.StartTime.Subtract(TimeSpan.FromMinutes(settings.AutoUnlockBeforeClassMinutes));
            var timeUntilUnlock = unlockTime - now;

            if (timeUntilUnlock.TotalSeconds > 0)
            {
                var countdown = string.Format("{0:D2}:{1:D2}:{2:D2}", 
                    (int)timeUntilUnlock.TotalHours, 
                    timeUntilUnlock.Minutes, 
                    timeUntilUnlock.Seconds);
                InfoText = $"仅防护模式运行中。系统将于 {DateTime.Today.Add(unlockTime):HH:mm:ss} 自动解除 (剩余 {countdown})。";
            }
            else
            {
                InfoText = "仅防护模式运行中。即将自动解除...";
            }
        }
        else if (current != null && current.Type == TimePointType.Break)
        {
            var endTime = DateTime.Today.Add(current.EndTime);
            InfoText = $"仅防护模式运行中。本次课间预计 {endTime:HH:mm} 结束。";
        }
        else
        {
            InfoText = "仅防护模式运行中。暂无自动解除计划。";
        }
    }

    [RelayCommand]
    private async Task Unlock()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "请输入用户名和密码";
            return;
        }

        var settings = SettingsService.Lock;

        var (success, message) = await AccountService.Instance.LoginAsync(Username, Password);
        if (!success)
        {
            ErrorMessage = message;
            return;
        }

        var currentAccount = AccountService.Instance.CurrentAccount;
        if (currentAccount == null || currentAccount.AccountType > settings.EarlyUnlockMinAccountType)
        {
            ErrorMessage = "该账户权限不足，无法解除仅防护模式";
            AccountService.Instance.Logout();
            return;
        }

        LockScreenService.Instance.DeactivateLock();
        NotificationService.Instance.ShowInfo($"已由管理员 {currentAccount.Username} 解除仅防护模式");
    }
}
