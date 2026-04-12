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
        });

        // 保存拦截规则到独立的 Networkblockage.json
        NetworkRuleService.SaveRules(NetworkRules.ToList());
    }

    partial void OnIsNetworkLockEnabledChanged(bool value)
    {
        if (_isInitialLoad) return;
        
        _ = ApplyChanges();
    }
}
