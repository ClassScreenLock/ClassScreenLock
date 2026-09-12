using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using System.Text.RegularExpressions;

namespace ClassScreenLock.ViewModels;

public partial class NetworkManagementViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<NetworkRule> _networkRules = new();

    [ObservableProperty]
    private bool _isNetworkLockEnabled;

    [ObservableProperty]
    private bool _isMitmInterceptionEnabled;

    [ObservableProperty]
    private string _newDomain = string.Empty;

    [ObservableProperty]
    private string _newDescription = string.Empty;

    public NetworkManagementViewModel()
    {
        LoadSettings();
    }

    private bool _isInitialLoad = true;

    private void LoadSettings()
    {
        _isInitialLoad = true;
        
        // 1. 先加载拦截规则，避免被 OnIsNetworkLockEnabledChanged 触发的保存覆盖
        var rules = NetworkRuleService.LoadRules();
        NetworkRules = new ObservableCollection<NetworkRule>(rules);

        // 2. 再加载通用设置
        var settings = SettingsService.Blockage;
        if (settings != null)
        {
            IsNetworkLockEnabled = settings.IsNetworkLockEnabled;
            IsMitmInterceptionEnabled = settings.IsMitmInterceptionEnabled;
        }
        
        _isInitialLoad = false;
    }

    [RelayCommand]
    private async Task AddRule()
    {
        if (string.IsNullOrWhiteSpace(NewDomain)) return;

        string domain = NewDomain.Trim().ToLower();
        
        // 移除可能的协议前缀
        if (domain.StartsWith("http://")) domain = domain.Substring(7);
        if (domain.StartsWith("https://")) domain = domain.Substring(8);
        
        // 移除路径和查询参数
        int slashIndex = domain.IndexOf('/');
        if (slashIndex != -1) domain = domain.Substring(0, slashIndex);
        
        // 简单的域名验证
        string domainPattern = @"^([a-z0-9]+(-[a-z0-9]+)*\.)+[a-z]{2,}$";
        if (!Regex.IsMatch(domain, domainPattern))
        {
            NotificationService.Instance.ShowError(LocalizationService.Instance.GetString("Network_InvalidDomain"));
            return;
        }

        if (NetworkRules.Any(r => r.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase)))
        {
            NotificationService.Instance.ShowError(LocalizationService.Instance.GetString("Network_RuleExists"));
            return;
        }

        var newRule = new NetworkRule
        {
            Domain = domain,
            Description = string.IsNullOrWhiteSpace(NewDescription) ? domain : NewDescription.Trim(),
            IsEnabled = true,
            Type = "Domain"
        };

        NetworkRules.Add(newRule);
        await ApplyChanges();
        
        NewDomain = string.Empty;
        NewDescription = string.Empty;
        
        NotificationService.Instance.ShowSuccess(LocalizationService.Instance.GetString("Notification_Success"));
    }

    [RelayCommand]
    private async Task RemoveRule(NetworkRule rule)
    {
        if (rule != null)
        {
            NetworkRules.Remove(rule);
            await ApplyChanges();
        }
    }

    [RelayCommand]
    private async Task ToggleRule(NetworkRule rule)
    {
        if (rule != null)
        {
            rule.IsEnabled = !rule.IsEnabled;
            await ApplyChanges();
        }
    }

    [RelayCommand]
    private async Task ApplyChanges()
    {
        SaveSettings();
        await NetworkBlockingService.Instance.ApplyRulesAsync("UserManualApply");
        NotificationService.Instance.ShowSuccess(LocalizationService.Instance.GetString("Notify_SettingsSaved"));
    }

    private void SaveSettings()
    {
        if (_isInitialLoad) return;

        // 保存拦截设置
        SettingsService.UpdateBlockage(settings =>
        {
            settings.IsNetworkLockEnabled = IsNetworkLockEnabled;
            settings.IsMitmInterceptionEnabled = IsMitmInterceptionEnabled;
        });

        // 保存拦截规则到独立的 Networkblockage.json
        NetworkRuleService.SaveRules(NetworkRules.ToList());
    }

    partial void OnIsNetworkLockEnabledChanged(bool value)
    {
        if (_isInitialLoad) return;
        
        SaveSettings();
        _ = Task.Run(async () =>
        {
            try
            {
                await NetworkBlockingService.Instance.ApplyRulesAsync("UserToggle");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    NotificationService.Instance.ShowSuccess(LocalizationService.Instance.GetString("Notify_SettingsSaved"));
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    NotificationService.Instance.ShowError($"应用规则失败: {ex.Message}");
                });
            }
        });
    }

    partial void OnIsMitmInterceptionEnabledChanged(bool value)
    {
        if (_isInitialLoad) return;

        if (value)
        {
            // 开启 MITM：先弹出实验性功能确认框（在 UI 线程直接调用，避免跨线程创建 UI 元素）
            _ = ConfirmAndEnableMitmAsync();
        }
        else
        {
            // 关闭 MITM：保存设置并在后台应用规则
            SaveSettings();
            _ = ApplyMitmRulesAsync();
        }
    }

    /// <summary>
    /// 弹出实验性功能确认框，用户确认后保存设置并应用规则。
    /// 整个流程在 UI 线程启动，仅网络服务调用在后台执行。
    /// </summary>
    private async Task ConfirmAndEnableMitmAsync()
    {
        bool confirmed = await ShowMitmExperimentalConfirmAsync();

        if (!confirmed)
        {
            // 用户取消，还原开关（回到 UI 线程）
            IsMitmInterceptionEnabled = false;
            return;
        }

        // 用户确认，保存设置并在后台应用规则
        SaveSettings();
        await ApplyMitmRulesAsync();
    }

    /// <summary>
    /// 在后台调用网络服务应用规则，完成后回到 UI 线程显示通知。
    /// </summary>
    private static async Task ApplyMitmRulesAsync()
    {
        try
        {
            await NetworkBlockingService.Instance.ApplyRulesAsync("MitmToggle");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                NotificationService.Instance.ShowSuccess(LocalizationService.Instance.GetString("Notify_SettingsSaved"));
            });
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                NotificationService.Instance.ShowError($"应用规则失败: {ex.Message}");
            });
        }
    }

    /// <summary>
    /// 显示 MITM 实验性功能确认框（与删除确认框同款 ContentDialog 遮罩/弹出动画/阴影）。
    /// </summary>
    private async Task<bool> ShowMitmExperimentalConfirmAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return true;
        }

        var owner = desktop.MainWindow;
        var L = LocalizationService.Instance.GetString;
        var dialog = new FluentAvalonia.UI.Controls.ContentDialog
        {
            Title = L("Network_MitmExperimental_Title"),
            Content = L("Network_MitmExperimental_Message"),
            PrimaryButtonText = L("Network_MitmExperimental_Confirm"),
            CloseButtonText = L("Btn_Cancel"),
            DefaultButton = FluentAvalonia.UI.Controls.ContentDialogButton.Close
        };

        var result = owner != null
            ? await dialog.ShowAsync(owner)
            : await dialog.ShowAsync();

        return result == FluentAvalonia.UI.Controls.ContentDialogResult.Primary;
    }
}
