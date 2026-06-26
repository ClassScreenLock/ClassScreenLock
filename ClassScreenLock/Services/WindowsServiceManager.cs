using System;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace ClassScreenLock.Services;

public static class WindowsServiceManager
{
    public static bool IsServiceInstalled(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            var _ = sc.Status;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Task<bool> InstallAndStartServicesAsync()
    {
        return Task.FromResult(true);
    }

    public static Task<bool> UninstallServicesAsync()
    {
        return Task.FromResult(true);
    }
}
