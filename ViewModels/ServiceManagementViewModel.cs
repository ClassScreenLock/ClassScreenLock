using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;

namespace ClassScreenLock.ViewModels;

public partial class ServiceManagementViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _lockScreenServiceInstalled;

    [ObservableProperty]
    private bool _monitorServiceInstalled;

    [ObservableProperty]
    private bool _lockScreenServiceRunning;

    [ObservableProperty]
    private bool _monitorServiceRunning;

    [ObservableProperty]
    private string _serviceStatusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _autoStartServices;

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
                ServiceStatusText = "服务安装并启动成功";
                NotificationService.Instance.ShowSuccess("Windows 服务已安装并启动");
            }
            else
            {
                ServiceStatusText = "服务安装失败";
                NotificationService.Instance.ShowError("Windows 服务安装失败，请查看日志");
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
                ServiceStatusText = "服务已卸载";
                NotificationService.Instance.ShowSuccess("Windows 服务已卸载");
            }
            else
            {
                ServiceStatusText = "服务卸载失败";
                NotificationService.Instance.ShowError("Windows 服务卸载失败，请查看日志");
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
            await WindowsServiceManager.UninstallServicesAsync();
            await Task.Delay(1000);
            
            var success = await WindowsServiceManager.InstallAndStartServicesAsync();
            
            if (success)
            {
                ServiceStatusText = "服务已重启";
                NotificationService.Instance.ShowSuccess("Windows 服务已重启");
            }
            else
            {
                ServiceStatusText = "服务重启失败";
                NotificationService.Instance.ShowError("Windows 服务重启失败，请查看日志");
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
        LockScreenServiceInstalled = WindowsServiceManager.IsServiceInstalled("CSL.LockScreenService");
        MonitorServiceInstalled = WindowsServiceManager.IsServiceInstalled("CSL.MainMonitorService");
        
        LockScreenServiceRunning = false;
        MonitorServiceRunning = false;

        try
        {
            using var lockScreenSc = new System.ServiceProcess.ServiceController("CSL.LockScreenService");
            LockScreenServiceRunning = lockScreenSc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch { }

        try
        {
            using var monitorSc = new System.ServiceProcess.ServiceController("CSL.MainMonitorService");
            MonitorServiceRunning = monitorSc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch { }

        if (LockScreenServiceInstalled && MonitorServiceInstalled)
        {
            if (LockScreenServiceRunning && MonitorServiceRunning)
            {
                ServiceStatusText = "所有服务正在运行";
            }
            else
            {
                ServiceStatusText = "服务已安装但未运行";
            }
        }
        else if (LockScreenServiceInstalled || MonitorServiceInstalled)
        {
            ServiceStatusText = "服务部分安装";
        }
        else
        {
            ServiceStatusText = "服务未安装";
        }
    }
}
