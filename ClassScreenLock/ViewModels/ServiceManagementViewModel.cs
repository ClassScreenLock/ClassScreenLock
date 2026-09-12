using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;

namespace ClassScreenLock.ViewModels;

public partial class ServiceManagementViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _watchdogServiceInstalled;

    [ObservableProperty]
    private bool _watchdogServiceRunning;

    [ObservableProperty]
    private string _serviceStatusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _autoStartServices;

    [ObservableProperty]
    private string _serviceDetailsText = string.Empty;

    public ServiceManagementViewModel()
    {
        RefreshServiceStatus();
    }

    partial void OnAutoStartServicesChanged(bool value)
    {
        SettingsService.General.AutoStartServices = value;
    }

    [RelayCommand]
    private async Task InstallServicesAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var success = await WindowsServiceManager.InstallAndStartServicesAsync();

            if (success)
            {
                ServiceStatusText = "看门狗服务安装并启动成功";
                NotificationService.Instance.ShowSuccess("Windows 看门狗服务已安装并启动");
            }
            else
            {
                ServiceStatusText = "服务安装失败，请查看日志";
                NotificationService.Instance.ShowError("Windows 看门狗服务安装失败，请查看日志");
            }

            RefreshServiceStatus();
        }
        catch (Exception ex)
        {
            ServiceStatusText = $"服务安装错误：{ex.Message}";
            NotificationService.Instance.ShowError($"服务安装错误：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallServicesAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var success = await WindowsServiceManager.UninstallServicesAsync();

            if (success)
            {
                ServiceStatusText = "看门狗服务已卸载";
                NotificationService.Instance.ShowSuccess("Windows 看门狗服务已卸载");
            }
            else
            {
                ServiceStatusText = "服务卸载失败";
                NotificationService.Instance.ShowError("Windows 看门狗服务卸载失败，请查看日志");
            }

            RefreshServiceStatus();
        }
        catch (Exception ex)
        {
            ServiceStatusText = $"服务卸载错误：{ex.Message}";
            NotificationService.Instance.ShowError($"服务卸载错误：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestartServicesAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var success = await WindowsServiceManager.RestartServicesAsync();

            if (success)
            {
                ServiceStatusText = "看门狗服务已重启";
                NotificationService.Instance.ShowSuccess("Windows 看门狗服务已重启");
            }
            else
            {
                ServiceStatusText = "服务重启失败";
                NotificationService.Instance.ShowError("Windows 看门狗服务重启失败，请查看日志");
            }

            RefreshServiceStatus();
        }
        catch (Exception ex)
        {
            ServiceStatusText = $"服务重启错误：{ex.Message}";
            NotificationService.Instance.ShowError($"服务重启错误：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RefreshServiceStatus()
    {
        WatchdogServiceInstalled = WindowsServiceManager.IsServiceInstalled(WindowsServiceManager.WatchdogServiceName);
        WatchdogServiceRunning = WindowsServiceManager.IsServiceRunning(WindowsServiceManager.WatchdogServiceName);

        if (WatchdogServiceInstalled)
        {
            if (WatchdogServiceRunning)
            {
                ServiceStatusText = "看门狗服务正在运行";
                ServiceDetailsText = "服务以 LocalSystem 权限运行，正在使用 CreateProcessAsUser API 监控并保护主程序。";
            }
            else
            {
                ServiceStatusText = "服务已安装但未运行";
                ServiceDetailsText = "建议点击「重启服务」以启动看门狗服务。";
            }
        }
        else
        {
            ServiceStatusText = "看门狗服务未安装";
            ServiceDetailsText = "点击「安装服务」以启用 SYSTEM 级看门狗保护。";
        }
    }
}
